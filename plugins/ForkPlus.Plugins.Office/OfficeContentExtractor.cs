using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using D = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ForkPlus.Plugins.Office
{
	/// <summary>
	/// 用 Open XML SDK（MIT）把 Office OOXML 文档抽成可读内容块（不还原版面，只取正文/单元格/幻灯片文字）。
	/// 现代格式 .docx/.xlsx/.pptx 各走一个解析器，输出统一的块序列。
	///
	/// 不认领老格式 .doc/.ppt/.xls：宽松许可下没有能解析它们的 .NET 库。
	/// </summary>
	internal static class OfficeContentExtractor
	{
		/// <summary>工作表最多提取的行数（超出部分截断并在标题注明）。</summary>
		private const int MaxSheetRows = 400;

		/// <summary>工作表最多提取的列数。</summary>
		private const int MaxSheetCols = 64;

		/// <summary>按扩展名选择解析器；不认识的扩展名抛 <see cref="NotSupportedException"/>。</summary>
		public static OfficeDocumentModel Extract(string path, byte[] bytes)
		{
			if (bytes == null || bytes.Length == 0)
			{
				throw new InvalidDataException("no bytes");
			}
			string ext = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
			switch (ext)
			{
				case ".docx":
					return ExtractWord(bytes);
				case ".xlsx":
					return ExtractExcel(bytes);
				case ".pptx":
					return ExtractPresentation(bytes);
				default:
					throw new NotSupportedException(ext);
			}
		}

		// ---- Word (.docx) ----

		/// <summary>Word 隐式默认字号（半磅）：styles.xml 无 docDefaults 时按 10pt 兜底。</summary>
		private const uint WordDefaultHalfPt = 20;

		private static OfficeDocumentModel ExtractWord(byte[] bytes)
		{
			List<OfficeBlock> blocks = new List<OfficeBlock>();
			using (MemoryStream stream = new MemoryStream(bytes))
			using (WordprocessingDocument doc = WordprocessingDocument.Open(stream, false))
			{
				Body body = doc.MainDocumentPart?.Document?.Body;
				if (body != null)
				{
					// 样式表（docDefaults + 段落/字符样式的 rPr）只解析一次，段落 run 逐个解析时复用。
					WordStyles styles = new WordStyles(doc.MainDocumentPart?.StyleDefinitionsPart?.Styles);
					// 按文档顺序遍历，段落与表格交错保留原有阅读顺序。
					foreach (OpenXmlElement element in body.Elements())
					{
						if (element is W.Paragraph paragraph)
						{
							AddWordParagraph(blocks, paragraph, styles);
						}
						else if (element is W.Table table)
						{
							blocks.Add(ToTableBlock(table));
						}
					}
				}
			}
			return new OfficeDocumentModel("Word", blocks);
		}

		private static void AddWordParagraph(List<OfficeBlock> blocks, W.Paragraph paragraph, WordStyles styles)
		{
			string text = Clean(paragraph.InnerText);
			if (text.Length == 0)
			{
				blocks.Add(new OfficeParagraphBlock(string.Empty));
				return;
			}
			int level = HeadingLevel(paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value);
			if (level > 0)
			{
				blocks.Add(new OfficeHeadingBlock(text, level));
				return;
			}
			blocks.Add(new OfficeParagraphBlock(text, ExtractWordRuns(paragraph, styles), styles.DefaultPt));
		}

		/// <summary>
		/// 把段落里的 <c>w:r</c>（含 <c>w:hyperlink</c> / <c>w:ins</c> 包裹的 run，排除修订删除的 <c>w:del</c>）
		/// 抽成带字符格式的 run；无有效 run 返回 null。
		/// </summary>
		private static IReadOnlyList<OfficeInline> ExtractWordRuns(W.Paragraph paragraph, WordStyles styles)
		{
			string paraStyleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
			List<OfficeInline> runs = new List<OfficeInline>();
			foreach (OpenXmlElement child in paragraph.Elements())
			{
				if (child is W.Run run)
				{
					AddWordRun(runs, run, styles, paraStyleId);
				}
				else if (child is W.Hyperlink hyperlink)
				{
					foreach (W.Run inner in hyperlink.Elements<W.Run>())
					{
						AddWordRun(runs, inner, styles, paraStyleId);
					}
				}
				else if (child is W.InsertedRun inserted)
				{
					foreach (W.Run inner in inserted.Elements<W.Run>())
					{
						AddWordRun(runs, inner, styles, paraStyleId);
					}
				}
			}
			return runs.Count == 0 ? null : runs;
		}

		/// <summary>单个 run → <see cref="OfficeInline"/>：字符格式按 docDefaults → 段落样式 → 字符样式 → 直接 rPr 解析。
		/// run 文本不 trim（保留边界空格做词间距），只去掉控制字符并折叠空白。</summary>
		private static void AddWordRun(List<OfficeInline> runs, W.Run run, WordStyles styles, string paraStyleId)
		{
			string text = CleanPreserve(run.InnerText);
			if (text.Length == 0)
			{
				return;
			}
			CharProps props = styles.Resolve(paraStyleId, run.RunProperties);
			AppendRun(runs, text, props.Bold ?? false, props.Italic ?? false, props.Underline ?? false, props.Strike ?? false,
				NormalizeColor(props.Color), styles.SizePtOf(props));
		}

		private static void AppendRun(List<OfficeInline> runs, string text, bool bold, bool italic, bool underline, bool strike, string colorHex, double? sizePt)
		{
			if (runs.Count > 0)
			{
				OfficeInline last = runs[runs.Count - 1];
				if (last.Bold == bold && last.Italic == italic && last.Underline == underline && last.Strike == strike
					&& last.ColorHex == colorHex && last.SizePt == sizePt)
				{
					runs[runs.Count - 1] = new OfficeInline(last.Text + text, bold, italic, underline, strike, colorHex, sizePt);
					return;
				}
			}
			runs.Add(new OfficeInline(text, bold, italic, underline, strike, colorHex, sizePt));
		}

		private static OfficeTableBlock ToTableBlock(W.Table table)
		{
			List<IReadOnlyList<string>> rows = new List<IReadOnlyList<string>>();
			foreach (W.TableRow row in table.Elements<W.TableRow>())
			{
				List<string> cells = new List<string>();
				foreach (W.TableCell cell in row.Elements<W.TableCell>())
				{
					cells.Add(Clean(cell.InnerText));
				}
				rows.Add(cells);
			}
			return new OfficeTableBlock(null, rows);
		}

		/// <summary>
		/// 把标题样式 id 映射到标题级别（1-9）；非标题返回 0。样式 id 含 heading / 标题时取其末尾的
		/// 数字（如 <c>Heading1</c> / <c>标题 2</c>）；内置标题的纯数字样式 id（<c>1</c>-<c>9</c>）即级别；
		/// 命中标题但无数字（如 <c>Heading</c>）按 1 级处理。
		/// </summary>
		private static int HeadingLevel(string styleId)
		{
			if (string.IsNullOrEmpty(styleId))
			{
				return 0;
			}
			string id = styleId.Trim();
			if (id.Length == 1 && id[0] >= '1' && id[0] <= '9')
			{
				return id[0] - '0';
			}
			bool heading = id.IndexOf("heading", StringComparison.OrdinalIgnoreCase) >= 0
				|| id.IndexOf("标题", StringComparison.Ordinal) >= 0;
			if (!heading)
			{
				return 0;
			}
			for (int i = id.Length - 1; i >= 0; i--)
			{
				if (id[i] >= '1' && id[i] <= '9')
				{
					return id[i] - '0';
				}
			}
			return 1;
		}

		/// <summary>字符格式的中间表示：null 字段表示「未指定，继承更上层」（docDefaults → 段落样式 → 字符样式 → 直接 rPr）。</summary>
		private sealed class CharProps
		{
			public bool? Bold;

			public bool? Italic;

			public bool? Underline;

			public bool? Strike;

			/// <summary>前景色原文（<c>w:color w:val</c>，可能为 "auto"）；null 表示未指定。</summary>
			public string Color;

			/// <summary>字号（半磅）；null 表示未指定。</summary>
			public uint? SizeHalfPt;
		}

		/// <summary>
		/// styles.xml 的样式表：docDefaults rPr + 各样式的 rPr 与 basedOn 链。run 的字符级样式（加粗 / 颜色等）
		/// 常写在字符样式或段落样式里而非直接 rPr 上，不解析样式表会整片丢失格式。
		/// </summary>
		private sealed class WordStyles
		{
			/// <summary>styleId → 样式自身的 rPr。</summary>
			public readonly Dictionary<string, CharProps> PropsById = new Dictionary<string, CharProps>();

			/// <summary>styleId → basedOn 的 styleId。</summary>
			public readonly Dictionary<string, string> BasedOn = new Dictionary<string, string>();

			public readonly CharProps DocDefaults;

			/// <summary>docDefaults 字号（半磅；无 docDefaults 按 <see cref="WordDefaultHalfPt"/> 兜底）。</summary>
			public readonly uint DefaultHalfPt;

			/// <summary>文档默认字号（磅），挂在段落块上供渲染端归一化 run 字号。</summary>
			public readonly double DefaultPt;

			public WordStyles(W.Styles styles)
			{
				DocDefaults = ReadProps(styles?.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle) ?? new CharProps();
				DefaultHalfPt = DocDefaults.SizeHalfPt ?? WordDefaultHalfPt;
				DefaultPt = DefaultHalfPt / 2.0;
				if (styles != null)
				{
					foreach (W.Style style in styles.Elements<W.Style>())
					{
						string id = style.StyleId?.Value;
						if (string.IsNullOrEmpty(id))
						{
							continue;
						}
						PropsById[id] = ReadProps(style.StyleRunProperties);
						BasedOn[id] = style.BasedOn?.Val?.Value;
					}
				}
			}

			/// <summary>解析 run 的生效字符格式：docDefaults → 段落样式链 → 字符样式链（w:rStyle）→ 直接 rPr，后者覆盖前者。</summary>
			public CharProps Resolve(string paraStyleId, W.RunProperties direct)
			{
				CharProps props = MergeChain(DocDefaults, paraStyleId);
				props = MergeChain(props, direct?.GetFirstChild<W.RunStyle>()?.Val?.Value);
				return Merge(props, ReadProps(direct));
			}

			/// <summary>字号（磅）：与文档默认一致时返回 null（渲染端跟随段落基准字号），否则返回实际磅值。</summary>
			public double? SizePtOf(CharProps props)
			{
				uint resolved = props.SizeHalfPt ?? DefaultHalfPt;
				return resolved != DefaultHalfPt ? resolved / 2.0 : (double?)null;
			}

			/// <summary>沿 basedOn 链合并样式字符格式（祖先先合并、叶子最后覆盖；带环与深度保护）。</summary>
			private CharProps MergeChain(CharProps current, string styleId)
			{
				if (string.IsNullOrEmpty(styleId) || !PropsById.ContainsKey(styleId))
				{
					return current;
				}
				List<string> chain = new List<string>();
				HashSet<string> seen = new HashSet<string>();
				string id = styleId;
				while (!string.IsNullOrEmpty(id) && PropsById.ContainsKey(id) && seen.Add(id) && chain.Count < 16)
				{
					chain.Add(id);
					BasedOn.TryGetValue(id, out id);
				}
				for (int i = chain.Count - 1; i >= 0; i--)
				{
					current = Merge(current, PropsById[chain[i]]);
				}
				return current;
			}
		}

		/// <summary>读取一个 rPr 容器（run / 样式 / docDefaults 通用）里的字符格式；未出现的属性为 null（继承上层）。
		/// 加粗 / 斜体 / 字号同时读复杂文变体（<c>w:bCs</c> / <c>w:iCs</c> / <c>w:szCs</c>），中文 run 的格式常只在 Cs 上。</summary>
		private static CharProps ReadProps(OpenXmlElement rPr)
		{
			if (rPr == null)
			{
				return null;
			}
			W.Underline underlineElement = rPr.GetFirstChild<W.Underline>();
			return new CharProps
			{
				Bold = OnOrNull(rPr.GetFirstChild<W.Bold>()) ?? OnOrNull(rPr.GetFirstChild<W.BoldComplexScript>()),
				Italic = OnOrNull(rPr.GetFirstChild<W.Italic>()) ?? OnOrNull(rPr.GetFirstChild<W.ItalicComplexScript>()),
				Underline = underlineElement == null ? null : (bool?)(underlineElement.Val == null || underlineElement.Val.Value != W.UnderlineValues.None),
				Strike = OnOrNull(rPr.GetFirstChild<W.Strike>()) ?? OnOrNull(rPr.GetFirstChild<W.DoubleStrike>()),
				Color = rPr.GetFirstChild<W.Color>()?.Val?.Value,
				SizeHalfPt = HalfPoints(rPr.GetFirstChild<W.FontSize>()?.Val?.Value) ?? HalfPoints(rPr.GetFirstChild<W.FontSizeComplexScript>()?.Val?.Value),
			};
		}

		/// <summary>叠加一层字符格式：<c>over</c> 未指定的字段沿用 <c>under</c>。</summary>
		private static CharProps Merge(CharProps under, CharProps over)
		{
			if (under == null)
			{
				return over ?? new CharProps();
			}
			if (over == null)
			{
				return under;
			}
			return new CharProps
			{
				Bold = over.Bold ?? under.Bold,
				Italic = over.Italic ?? under.Italic,
				Underline = over.Underline ?? under.Underline,
				Strike = over.Strike ?? under.Strike,
				Color = over.Color ?? under.Color,
				SizeHalfPt = over.SizeHalfPt ?? under.SizeHalfPt,
			};
		}

		/// <summary><c>w:b</c> / <c>w:i</c> 等开关：元素存在即生效，显式 val="0/false" 表示关闭；不存在返回 null（继承上层）。</summary>
		private static bool? OnOrNull(OnOffType value)
		{
			if (value == null)
			{
				return null;
			}
			return value.Val == null || value.Val.Value;
		}

		/// <summary>半磅字号原文（<c>w:sz</c> / <c>w:szCs</c> 的 val）→ 数值；无效或非正数返回 null。</summary>
		private static uint? HalfPoints(string value)
		{
			if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint half) && half > 0)
			{
				return half;
			}
			return null;
		}

		/// <summary>规范化前景色：null / "auto" → null；其余要求 6 或 8 位十六进制（RRGGBB / AARRGGBB），统一去 # 大写。</summary>
		private static string NormalizeColor(string value)
		{
			if (string.IsNullOrEmpty(value) || string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			string hex = value.TrimStart('#');
			if (hex.Length != 6 && hex.Length != 8)
			{
				return null;
			}
			foreach (char ch in hex)
			{
				if (!Uri.IsHexDigit(ch))
				{
					return null;
				}
			}
			return hex.ToUpperInvariant();
		}

		// ---- Excel (.xlsx) ----

		private static OfficeDocumentModel ExtractExcel(byte[] bytes)
		{
			List<OfficeBlock> blocks = new List<OfficeBlock>();
			List<OfficeSheetSection> sections = new List<OfficeSheetSection>();
			using (MemoryStream stream = new MemoryStream(bytes))
			using (SpreadsheetDocument doc = SpreadsheetDocument.Open(stream, false))
			{
				WorkbookPart workbookPart = doc.WorkbookPart;
				Sheets sheets = workbookPart?.Workbook?.Sheets;
				SharedStringTable sharedStrings = workbookPart?.SharedStringTablePart?.SharedStringTable;
				if (sheets != null)
				{
					foreach (S.Sheet sheet in sheets.Elements<S.Sheet>())
					{
						string caption = sheet.Name?.Value ?? string.Empty;
						WorksheetPart worksheetPart = workbookPart.GetPartById(sheet.Id?.Value) as WorksheetPart;
						SheetData data = worksheetPart?.Worksheet?.GetFirstChild<SheetData>();
						int extracted;
						List<IReadOnlyList<string>> rows = ReadSheet(data, sharedStrings, out extracted);
						if (extracted > rows.Count)
						{
							caption += "  " + OfficeStrings.F("({0}/{1} rows)", rows.Count, extracted);
						}
						// 平铺块与按表分区各留一份：分区供视图的 sheet 标签页切换，平铺块供单表文档与统计复用。
						List<OfficeBlock> sheetBlocks = new List<OfficeBlock>();
						sheetBlocks.Add(new OfficeHeadingBlock(caption, 1));
						sheetBlocks.Add(new OfficeTableBlock(null, rows));
						blocks.AddRange(sheetBlocks);
						sections.Add(new OfficeSheetSection(sheet.Name?.Value ?? string.Empty, sheetBlocks));
					}
				}
			}
			return new OfficeDocumentModel("Excel", blocks, sections);
		}

		private static List<IReadOnlyList<string>> ReadSheet(SheetData data, SharedStringTable sharedStrings, out int totalRows)
		{
			List<IReadOnlyList<string>> rows = new List<IReadOnlyList<string>>();
			totalRows = 0;
			if (data == null)
			{
				return rows;
			}
			foreach (S.Row row in data.Elements<S.Row>())
			{
				totalRows++;
				if (rows.Count >= MaxSheetRows)
				{
					continue;
				}
				List<string> cells = new List<string>();
				foreach (S.Cell cell in row.Elements<S.Cell>())
				{
					int column = ColumnIndex(cell.CellReference?.Value);
					if (column < 0)
					{
						column = cells.Count;
					}
					if (column >= MaxSheetCols)
					{
						continue;
					}
					while (cells.Count <= column)
					{
						cells.Add(string.Empty);
					}
					cells[column] = CellText(cell, sharedStrings);
				}
				rows.Add(cells);
			}
			return rows;
		}

		/// <summary>把单元格引用（如 "B3"）的字母部分转成 0 基列号；无字母返回 -1。</summary>
		private static int ColumnIndex(string cellReference)
		{
			if (string.IsNullOrEmpty(cellReference))
			{
				return -1;
			}
			int index = 0;
			bool sawLetter = false;
			foreach (char ch in cellReference)
			{
				if (ch >= 'A' && ch <= 'Z')
				{
					index = index * 26 + (ch - 'A' + 1);
					sawLetter = true;
				}
				else if (ch >= 'a' && ch <= 'z')
				{
					index = index * 26 + (ch - 'a' + 1);
					sawLetter = true;
				}
				else
				{
					break;
				}
			}
			return sawLetter ? index - 1 : -1;
		}

		private static string CellText(S.Cell cell, SharedStringTable sharedStrings)
		{
			try
			{
				CellValues type = cell.DataType?.Value ?? CellValues.Number;
				if (type == CellValues.SharedString)
				{
					string raw = cell.CellValue?.Text;
					if (sharedStrings != null && int.TryParse(raw, out int index) && index >= 0 && index < sharedStrings.Count())
					{
						return Clean(sharedStrings.ElementAt(index)?.InnerText ?? string.Empty);
					}
					return Clean(raw ?? string.Empty);
				}
				if (type == CellValues.InlineString)
				{
					return Clean(cell.InlineString?.InnerText ?? string.Empty);
				}
				// 数值 / 布尔 / 日期 / 公式缓存值：CellValue 即文本表示；无则为空。
				return Clean(cell.CellValue?.Text ?? cell.InnerText);
			}
			catch (Exception)
			{
				return string.Empty;
			}
		}

			// ---- PowerPoint (.pptx) ----

		/// <summary>PPT 母版正文的隐式默认字号（磅）：幻灯片 run 的 a:rPr@sz 为百分之磅，缺省按 18pt 兜底。</summary>
		private const double PptDefaultPt = 18.0;

		private static OfficeDocumentModel ExtractPresentation(byte[] bytes)
		{
			List<OfficeBlock> blocks = new List<OfficeBlock>();
			using (MemoryStream stream = new MemoryStream(bytes))
			using (PresentationDocument doc = PresentationDocument.Open(stream, false))
			{
				PresentationPart presentationPart = doc.PresentationPart;
				P.SlideIdList slideIds = presentationPart?.Presentation?.SlideIdList;
				if (slideIds != null)
				{
					int index = 0;
					foreach (P.SlideId slideId in slideIds.Elements<P.SlideId>())
					{
						index++;
						blocks.Add(new OfficeHeadingBlock(OfficeStrings.F("Slide {0}", index), 1));
						string relationshipId = slideId.RelationshipId?.Value;
						SlidePart slidePart = relationshipId == null ? null : presentationPart.GetPartById(relationshipId) as SlidePart;
						P.ShapeTree tree = slidePart?.Slide?.CommonSlideData?.ShapeTree;
						if (tree == null)
						{
							continue;
						}
						foreach (P.Shape shape in tree.Elements<P.Shape>())
						{
							P.TextBody textBody = shape.TextBody;
							if (textBody == null)
							{
								continue;
							}
							foreach (D.Paragraph paragraph in textBody.Elements<D.Paragraph>())
							{
								AddSlideParagraph(blocks, paragraph);
							}
						}
					}
				}
			}
			return new OfficeDocumentModel("PowerPoint", blocks);
		}

		private static void AddSlideParagraph(List<OfficeBlock> blocks, D.Paragraph paragraph)
		{
			string text = Clean(paragraph.InnerText);
			if (text.Length == 0)
			{
				return;
			}
			List<OfficeInline> runs = new List<OfficeInline>();
			foreach (D.Run run in paragraph.Elements<D.Run>())
			{
				string runText = CleanPreserve(run.Text?.Text ?? run.InnerText);
				if (runText.Length == 0)
				{
					continue;
				}
				D.RunProperties props = run.RunProperties;
				bool bold = props?.Bold?.Value ?? false;
				bool italic = props?.Italic?.Value ?? false;
				bool underline = props?.Underline != null && props.Underline.Value != D.TextUnderlineValues.None;
				bool strike = props?.Strike != null && props.Strike.Value != D.TextStrikeValues.NoStrike;
				string colorHex = NormalizeColor(props?.GetFirstChild<D.SolidFill>()?.RgbColorModelHex?.Val?.Value);
				double? sizePt = null;
				int? size = props?.FontSize?.Value;
				if (size.HasValue && size.Value > 0)
				{
					double pt = size.Value / 100.0;
					if (Math.Abs(pt - PptDefaultPt) > 0.01)
					{
						sizePt = pt;
					}
				}
				AppendRun(runs, runText, bold, italic, underline, strike, colorHex, sizePt);
			}
			blocks.Add(new OfficeParagraphBlock(text, runs.Count == 0 ? null : runs, PptDefaultPt));
		}

		// ---- 通用 ----

		/// <summary>去掉控制字符、折叠内部空白并 trim；空行保留为长度 0。</summary>
		private static string Clean(string raw)
		{
			return CleanPreserve(raw).Trim();
		}

		/// <summary>同 <see cref="Clean"/> 但不 trim 两端：行内混排时 run 边界上的空格是词间距，
		/// trim 掉会让相邻两个不同样式的词粘在一起（"Hello " + "world" → "Helloworld"）。</summary>
		private static string CleanPreserve(string raw)
		{
			if (string.IsNullOrEmpty(raw))
			{
				return string.Empty;
			}
			StringBuilder sb = new StringBuilder(raw.Length);
			bool lastWasSpace = false;
			foreach (char ch in raw)
			{
				char c = (ch == '\t' || ch == '\r' || ch == '\n' || ch == '\u000B' || ch == '\u000C') ? ' ' : ch;
				if (c < ' ')
				{
					continue;
				}
				if (c == ' ')
				{
					if (lastWasSpace)
					{
						continue;
					}
					lastWasSpace = true;
				}
				else
				{
					lastWasSpace = false;
				}
				sb.Append(c);
			}
			return sb.ToString();
		}
	}
}