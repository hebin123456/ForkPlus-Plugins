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

	/// <summary>段落块：Word 正文段落、PPT 文本框里的一行文字。空文本用于保留段间距。</summary>
	internal sealed class OfficeParagraphBlock : OfficeBlock
	{
		public OfficeParagraphBlock(string text)
		{
			Text = text ?? string.Empty;
		}

		public string Text { get; }
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

	/// <summary>一侧文档的提取结果：Kind 为 "Word" / "Excel" / "PowerPoint"，Blocks 为按序排列的内容块。</summary>
	internal sealed class OfficeDocumentModel
	{
		public OfficeDocumentModel(string kind, IReadOnlyList<OfficeBlock> blocks)
		{
			Kind = kind ?? string.Empty;
			Blocks = blocks ?? (IReadOnlyList<OfficeBlock>)new List<OfficeBlock>();
		}

		public string Kind { get; }

		public IReadOnlyList<OfficeBlock> Blocks { get; }
	}
}