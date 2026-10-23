using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace ForkPlus.Plugins.Markdown
{
	/// <summary>一侧文档的一个标题（大纲条目）。</summary>
	internal sealed class MarkdownHeading
	{
		/// <summary>标题原文（保留大小写与空白，用于展示）。</summary>
		internal string Text;

		/// <summary>标题层级（1-6）。</summary>
		internal int Level;
	}

	/// <summary>大纲 diff 的一行：同一标题在旧 / 新两侧的层级与变更状态。</summary>
	internal sealed class MarkdownOutlineRow
	{
		internal string Text;

		internal int LeftLevel;

		internal int RightLevel;

		internal bool LeftPresent;

		internal bool RightPresent;

		internal MarkdownOutlineState State;
	}

	internal enum MarkdownOutlineState
	{
		Same,
		Changed,
		LeftOnly,
		RightOnly
	}

	internal sealed class MarkdownOutlineDiff
	{
		internal List<MarkdownOutlineRow> Rows = new List<MarkdownOutlineRow>();

		internal int Same;

		internal int Changed;

		internal int LeftOnly;

		internal int RightOnly;

		internal int Total => Rows.Count;

		internal bool HasDifferences => Changed > 0 || LeftOnly > 0 || RightOnly > 0;
	}

	/// <summary>
	/// Markdown 大纲解析与语义 diff。
	///
	/// 解析：Markdig 文档对象模型抽全部标题（<c>HeadingBlock</c>，含 setext 风格），层级 + 行内文本。
	/// diff：**键 = 标题文本的归一化**（去首尾空白、折叠连续空白、统一小写），同侧重复标题按出现
	/// 次序加序号消歧——刻意**不用位置索引**做键：在中间插入一个标题会让后续同级标题的序号整体
	/// 漂移，产生大面积伪差异；按标题文本对齐时，插入 / 删除标题只影响该行本身。
	/// 同键两侧都在：层级相同 → 相同，层级变了（升降级）→ 已变；仅一侧 → 仅左 / 仅右。
	/// 行序取「先左后右」——左侧原有标题保持文档序，右侧独有的追加在后，便于顺着文档结构读。
	/// </summary>
	internal static class MarkdownOutline
	{
		/// <summary>两侧标题数之和超过此值不 diff（防超长文档拖死 UI；单侧 300MB 阈值已先挡掉极端情况）。</summary>
		internal const int MaxHeadings = 4000;

		private static readonly Regex WhitespaceRun = new Regex("\\s+", RegexOptions.Compiled);

		/// <summary>解析一侧 Markdown 的标题大纲（后台线程调用；解析失败抛异常由调用方统一记录）。</summary>
		internal static List<MarkdownHeading> Extract(string markdown)
		{
			List<MarkdownHeading> headings = new List<MarkdownHeading>();
			if (string.IsNullOrEmpty(markdown))
			{
				return headings;
			}
			MarkdownDocument document = Markdig.Markdown.Parse(markdown, MarkdownHtmlComposer.Pipeline);
			foreach (HeadingBlock heading in document.Descendants<HeadingBlock>())
			{
				headings.Add(new MarkdownHeading
				{
					Text = ExtractText(heading),
					Level = heading.Level
				});
				if (headings.Count >= MaxHeadings)
				{
					break;
				}
			}
			return headings;
		}

		/// <summary>把标题行内的字面文本拼出来（强调 / 代码等容器递归取 <c>LiteralInline</c>，代码span取 <c>CodeInline</c>）。</summary>
		private static string ExtractText(HeadingBlock heading)
		{
			if (heading.Inline == null)
			{
				return string.Empty;
			}
			StringBuilder sb = new StringBuilder();
			foreach (Inline inline in heading.Inline.Descendants())
			{
				LiteralInline literal = inline as LiteralInline;
				if (literal != null)
				{
					sb.Append(literal.Content.ToString());
					continue;
				}
				CodeInline code = inline as CodeInline;
				if (code != null)
				{
					sb.Append(code.Content);
				}
			}
			return sb.ToString().Trim();
		}

		/// <summary>两侧标题大纲的语义 diff（四态 + 汇总计数）。</summary>
		internal static MarkdownOutlineDiff Compute(List<MarkdownHeading> left, List<MarkdownHeading> right)
		{
			MarkdownOutlineDiff diff = new MarkdownOutlineDiff();
			Dictionary<string, MarkdownHeading> leftMap = Index(left);
			Dictionary<string, MarkdownHeading> rightMap = Index(right);
			HashSet<string> emitted = new HashSet<string>();
			Emit(left, leftMap, rightMap, diff, emitted);
			Emit(right, leftMap, rightMap, diff, emitted);
			return diff;
		}

		private static void Emit(List<MarkdownHeading> order, Dictionary<string, MarkdownHeading> leftMap, Dictionary<string, MarkdownHeading> rightMap, MarkdownOutlineDiff diff, HashSet<string> emitted)
		{
			// 侧内出现次序计数：与 Index 的消歧口径严格一致（同侧第 2 次出现起键尾追加 #2 / #3…）。
			Dictionary<string, int> occurrences = new Dictionary<string, int>();
			foreach (MarkdownHeading heading in order)
			{
				string normalized = Normalize(heading.Text);
				int occurrence;
				occurrences.TryGetValue(normalized, out occurrence);
				occurrences[normalized] = occurrence + 1;
				string key = occurrence == 0 ? normalized : (normalized + "#" + (occurrence + 1));
				if (!emitted.Add(key))
				{
					continue;
				}
				bool hasLeft = leftMap.TryGetValue(key, out MarkdownHeading left);
				bool hasRight = rightMap.TryGetValue(key, out MarkdownHeading right);
				MarkdownOutlineRow row = new MarkdownOutlineRow
				{
					Text = heading.Text,
					LeftPresent = hasLeft,
					RightPresent = hasRight,
					LeftLevel = hasLeft ? left.Level : 0,
					RightLevel = hasRight ? right.Level : 0
				};
				if (hasLeft && hasRight)
				{
					if (left.Level == right.Level)
					{
						row.State = MarkdownOutlineState.Same;
						diff.Same++;
					}
					else
					{
						row.State = MarkdownOutlineState.Changed;
						diff.Changed++;
					}
				}
				else if (hasLeft)
				{
					row.State = MarkdownOutlineState.LeftOnly;
					diff.LeftOnly++;
				}
				else
				{
					row.State = MarkdownOutlineState.RightOnly;
					diff.RightOnly++;
				}
				diff.Rows.Add(row);
			}
		}

		/// <summary>一侧标题列表 → 键索引；同侧重复标题按出现次序消歧（第 2 次起键尾追加 #2 / #3…）。</summary>
		private static Dictionary<string, MarkdownHeading> Index(List<MarkdownHeading> headings)
		{
			Dictionary<string, MarkdownHeading> map = new Dictionary<string, MarkdownHeading>();
			Dictionary<string, int> occurrences = new Dictionary<string, int>();
			foreach (MarkdownHeading heading in headings)
			{
				string normalized = Normalize(heading.Text);
				int occurrence;
				occurrences.TryGetValue(normalized, out occurrence);
				occurrences[normalized] = occurrence + 1;
				string key = occurrence == 0 ? normalized : (normalized + "#" + (occurrence + 1));
				if (!map.ContainsKey(key))
				{
					map.Add(key, heading);
				}
			}
			return map;
		}

		/// <summary>归一化：折叠连续空白为单空格、去首尾、统一小写（大小写差异不视为不同标题）。</summary>
		internal static string Normalize(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return string.Empty;
			}
			return WhitespaceRun.Replace(text, " ").Trim().ToLowerInvariant();
		}
	}
}
