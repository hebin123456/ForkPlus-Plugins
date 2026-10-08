using System.Collections.Generic;

namespace ForkPlus.Plugins.Office
{
	/// <summary>
	/// 提取后的内容块（Word 段落 / 表格、Excel 工作表网格、PPT 幻灯片文字的统一投影）。
	/// 插件只把「可读内容」抽出来交给 <see cref="OfficeDiffView"/> 渲染，不还原原始版面。
	/// </summary>
	internal abstract class OfficeBlock
	{
	}

	/// <summary>标题块：文档小节标题、工作表名、幻灯片序号。Level 1 为文档级，2 为小节级。</summary>
	internal sealed class OfficeHeadingBlock : OfficeBlock
	{
		public OfficeHeadingBlock(string text, int level)
		{
			Text = text ?? string.Empty;
			Level = level;
		}

		public string Text { get; }

		public int Level { get; }
	}

	/// <summary>段落内的一段文字及基础字符格式（Word / PowerPoint 的 run 投影）。</summary>
	internal sealed class OfficeInline
	{
		public OfficeInline(string text, bool bold, bool italic, bool underline, bool strike, string colorHex, double? sizePt)
		{
			Text = text ?? string.Empty;
			Bold = bold;
			Italic = italic;
			Underline = underline;
			Strike = strike;
			ColorHex = colorHex;
			SizePt = sizePt;
		}

		public string Text { get; }

		public bool Bold { get; }

		public bool Italic { get; }

		public bool Underline { get; }

		public bool Strike { get; }

		/// <summary>前景色（RRGGBB / AARRGGBB 十六进制，大写）；null 表示跟随视图默认前景色。</summary>
		public string ColorHex { get; }

		/// <summary>字号（磅）；null 表示与文档默认字号一致（渲染端按段落块的 BaseSizePt 归一）。</summary>
		public double? SizePt { get; }

		/// <summary>是否带任何可见字符格式（全空时可省掉 Inlines，直接渲染纯文本）。</summary>
		public bool HasFormat => Bold || Italic || Underline || Strike || ColorHex != null || SizePt != null;
	}

	/// <summary>段落块：Word 正文段落、PPT 文本框里的一行文字。空文本用于保留段间距。</summary>
	internal sealed class OfficeParagraphBlock : OfficeBlock
	{
		public OfficeParagraphBlock(string text, IReadOnlyList<OfficeInline> runs = null, double? baseSizePt = null)
		{
			Text = text ?? string.Empty;
			Runs = runs;
			BaseSizePt = baseSizePt;
		}

		public string Text { get; }

		/// <summary>带字符格式的 run 序列；为 null 表示整段无特殊格式（按 <see cref="Text"/> 渲染）。</summary>
		public IReadOnlyList<OfficeInline> Runs { get; }

		/// <summary>文档默认字号（磅），用于把 run 的 <see cref="OfficeInline.SizePt"/> 换算成视图内相对字号；null 时视图按内置基准渲染。</summary>
		public double? BaseSizePt { get; }

		/// <summary>是否值得走 Inlines 渲染（至少一段带格式，或含多段不同格式）。</summary>
		public bool HasFormatting
		{
			get
			{
				IReadOnlyList<OfficeInline> runs = Runs;
				if (runs == null || runs.Count == 0)
				{
					return false;
				}
				if (runs.Count > 1)
				{
					return true;
				}
				return runs[0].HasFormat;
			}
		}
	}

	/// <summary>表格块：Word 表格、Excel 工作表数据区。行长度可能不一致（右侧按空单元格补齐）。</summary>
	internal sealed class OfficeTableBlock : OfficeBlock
	{
		public OfficeTableBlock(string caption, IReadOnlyList<IReadOnlyList<string>> rows)
		{
			Caption = caption;
			Rows = rows ?? (IReadOnlyList<IReadOnlyList<string>>)new List<IReadOnlyList<string>>();
		}

		public string Caption { get; }

		public IReadOnlyList<IReadOnlyList<string>> Rows { get; }
	}

	/// <summary>Excel 单个工作表的分区：工作表名 + 该表专属的标题 / 表格块（供视图做 sheet 标签页切换）。</summary>
	internal sealed class OfficeSheetSection
	{
		public OfficeSheetSection(string name, IReadOnlyList<OfficeBlock> blocks)
		{
			Name = name ?? string.Empty;
			Blocks = blocks ?? (IReadOnlyList<OfficeBlock>)new List<OfficeBlock>();
		}

		public string Name { get; }

		public IReadOnlyList<OfficeBlock> Blocks { get; }
	}

	/// <summary>一侧文档的提取结果：Kind 为 "Word" / "Excel" / "PowerPoint"，Blocks 为按序排列的内容块。</summary>
	internal sealed class OfficeDocumentModel
	{
		public OfficeDocumentModel(string kind, IReadOnlyList<OfficeBlock> blocks, IReadOnlyList<OfficeSheetSection> sheets = null)
		{
			Kind = kind ?? string.Empty;
			Blocks = blocks ?? (IReadOnlyList<OfficeBlock>)new List<OfficeBlock>();
			Sheets = sheets;
		}

		public string Kind { get; }

		public IReadOnlyList<OfficeBlock> Blocks { get; }

		/// <summary>Excel 的按工作表分区（配合视图的 sheet 标签页切换）；Word / PowerPoint 为 null（按 Blocks 平铺）。</summary>
		public IReadOnlyList<OfficeSheetSection> Sheets { get; }

		/// <summary>粗略的字数统计（所有可见文本字符数），用于视图顶部的摘要徽章。</summary>
		public int TextLength
		{
			get
			{
				int total = 0;
				foreach (OfficeBlock block in Blocks)
				{
					if (block is OfficeHeadingBlock heading)
					{
						total += heading.Text.Length;
					}
					else if (block is OfficeParagraphBlock paragraph)
					{
						total += paragraph.Text.Length;
					}
					else if (block is OfficeTableBlock table)
					{
						foreach (IReadOnlyList<string> row in table.Rows)
						{
							foreach (string cell in row)
							{
								total += cell?.Length ?? 0;
							}
						}
					}
				}
				return total;
			}
		}
	}
}
