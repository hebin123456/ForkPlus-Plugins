using System;
using System.Collections.Generic;
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

		private static OfficeDocumentModel ExtractWord(byte[] bytes)
		{
			List<OfficeBlock> blocks = new List<OfficeBlock>();
			using (MemoryStream stream = new MemoryStream(bytes))
			using (WordprocessingDocument doc = WordprocessingDocument.Open(stream, false))
			{
				Body body = doc.MainDocumentPart?.Document?.Body;
				if (body != null)
				{
					// 按文档顺序遍历，段落与表格交错保留原有阅读顺序。
					foreach (OpenXmlElement element in body.Elements())
					{
						if (element is W.Paragraph paragraph)
						{
							AddWordParagraph(blocks, paragraph);
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

		private static void AddWordParagraph(List<OfficeBlock> blocks, W.Paragraph paragraph)
		{
			string text = Clean(paragraph.InnerText);
			if (text.Length == 0)
			{
				blocks.Add(new OfficeParagraphBlock(string.Empty));
				return;
			}
			if (IsHeadingStyle(paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value))
			{
				blocks.Add(new OfficeHeadingBlock(text, 2));
			}
			else
			{
				blocks.Add(new OfficeParagraphBlock(text));
			}
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

		/// <summary>是否为标题样式：样式 id 含 heading/标题，或内置标题的纯数字样式 id（1-9）。</summary>
		private static bool IsHeadingStyle(string styleId)
		{
			if (string.IsNullOrEmpty(styleId))
			{
				return false;
			}
			string id = styleId.Trim();
			if (id.Length == 1 && id[0] >= '1' && id[0] <= '9')
			{
				return true;
			}
			return id.IndexOf("heading", StringComparison.OrdinalIgnoreCase) >= 0
				|| id.IndexOf("标题", StringComparison.Ordinal) >= 0;
		}

		// ---- Excel (.xlsx) ----

		private static OfficeDocumentModel ExtractExcel(byte[] bytes)
		{
			List<OfficeBlock> blocks = new List<OfficeBlock>();
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
							caption += "  (" + rows.Count + "/" + extracted + " rows)";
						}
						blocks.Add(new OfficeHeadingBlock(caption, 1));
						blocks.Add(new OfficeTableBlock(null, rows));
					}
				}
			}
			return new OfficeDocumentModel("Excel", blocks);
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
						blocks.Add(new OfficeHeadingBlock("Slide " + index, 1));
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
								AddSlideText(blocks, paragraph.InnerText);
							}
						}
					}
				}
			}
			return new OfficeDocumentModel("PowerPoint", blocks);
		}

		private static void AddSlideText(List<OfficeBlock> blocks, string raw)
		{
			if (string.IsNullOrEmpty(raw))
			{
				return;
			}
			foreach (string line in raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
			{
				string text = Clean(line);
				if (text.Length > 0)
				{
					blocks.Add(new OfficeParagraphBlock(text));
				}
			}
		}

		// ---- 通用 ----

		/// <summary>去掉控制字符、折叠内部空白并 trim；空行保留为长度 0。</summary>
		private static string Clean(string raw)
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
			return sb.ToString().Trim();
		}
	}
}