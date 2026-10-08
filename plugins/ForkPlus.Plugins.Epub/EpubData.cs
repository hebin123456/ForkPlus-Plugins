using System;
using System.Collections.Generic;

namespace ForkPlus.Plugins.Epub
{
	/// <summary>元数据对比表的一行：稳定键路径（如 "title"、"creator[1]"）+ 展示值。</summary>
	internal sealed class EpubRow
	{
		internal string Path;

		internal string Value;
	}

	/// <summary>
	/// spine 里的一个章节项：序号（spine 顺序，1 起）+ 解析成 zip 根相对的 href +
	/// 章节标题（nav / NCX 尽力而为，拿不到为 null）+ media-type + zip 条目未压缩大小。
	/// </summary>
	internal sealed class EpubChapter
	{
		internal int Index;

		/// <summary>zip 根相对路径（manifest href 按 OPF 目录解析后的全路径）。</summary>
		internal string Href;

		/// <summary>nav / NCX 里拿到的标题；拿不到为 null（显示回退 href 文件名）。</summary>
		internal string Title;

		internal string MediaType;

		/// <summary>zip 条目未压缩大小（字节）；条目缺失为 0。</summary>
		internal long SizeBytes;

		/// <summary>spine itemref 的 linear 属性是否非 "no"（"no" 的项保留但标注）。</summary>
		internal bool Linear = true;
	}

	/// <summary>一次 EPUB 解析的结果：书名（dc:title，缺失为 null）+ 元数据行 + 章节列表 + 统计。</summary>
	internal sealed class EpubDocument
	{
		/// <summary>dc:title；缺失为 null（视图回退用文件名）。</summary>
		internal string Title;

		/// <summary>元数据 / 统计行（键路径稳定排序）。</summary>
		internal List<EpubRow> Rows = new List<EpubRow>();

		internal List<EpubChapter> Chapters = new List<EpubChapter>();

		/// <summary>zip 条目总数。</summary>
		internal int FileCount;

		/// <summary>全部 zip 条目未压缩字节和。</summary>
		internal long TotalBytes;
	}

	internal enum EpubState
	{
		Same,
		Changed,
		LeftOnly,
		RightOnly
	}

	/// <summary>元数据对齐后的一行：键路径 + 旧 / 新两侧的值 + 变更状态。</summary>
	internal sealed class EpubRowDiff
	{
		internal string Path;

		/// <summary>左侧值；左侧无此键为 null。</summary>
		internal string Left;

		/// <summary>右侧值；右侧无此键为 null。</summary>
		internal string Right;

		internal EpubState State;
	}

	/// <summary>章节对齐后的一行：按 spine 序号对齐的左右章节 + 变更状态。</summary>
	internal sealed class EpubChapterDiff
	{
		internal EpubChapter Left;

		internal EpubChapter Right;

		internal EpubState State;
	}

	/// <summary>元数据 diff 结果：Rows 为键路径并集的四态行，计数用于状态行汇总（Metadata 口径）。</summary>
	internal sealed class EpubDiffResult
	{
		internal List<EpubRowDiff> Rows = new List<EpubRowDiff>();

		internal int Changed;

		internal int LeftOnly;

		internal int RightOnly;

		internal int Total => Rows.Count;

		internal bool HasDifferences => Changed > 0 || LeftOnly > 0 || RightOnly > 0;
	}

	/// <summary>章节 diff 结果：与 <see cref="EpubDiffResult"/> 同构，Row 里带左右 <see cref="EpubChapter"/>。</summary>
	internal sealed class ChapterDiffResult
	{
		internal List<EpubChapterDiff> Rows = new List<EpubChapterDiff>();

		internal int Changed;

		internal int LeftOnly;

		internal int RightOnly;

		internal int Total => Rows.Count;

		internal bool HasDifferences => Changed > 0 || LeftOnly > 0 || RightOnly > 0;
	}

	/// <summary>
	/// EPUB 对齐：
	/// <list type="bullet">
	/// <item>元数据 —— 两侧 Rows 按键路径取并集：两侧都有且值相等 = same、都有但不等 = changed、
	/// 仅左侧有 = left only、仅右侧有 = right only；行序保持解析器产出的稳定键路径顺序
	/// （先左侧顺序，再追加仅右侧的键）。</item>
	/// <item>章节 —— 按 spine 序号对齐：同序号两侧都有时比较 href 与标题（全等 = same，不等 = changed），
	/// 仅一侧有该序号 = left / right only。</item>
	/// </list>
	/// </summary>
	internal static class EpubDiff
	{
		/// <summary>元数据行按 Path 并集对齐。</summary>
		internal static EpubDiffResult ComputeRows(List<EpubRow> left, List<EpubRow> right)
		{
			EpubDiffResult result = new EpubDiffResult();
			Dictionary<string, EpubRow> rightMap = new Dictionary<string, EpubRow>(StringComparer.Ordinal);
			if (right != null)
			{
				foreach (EpubRow row in right)
				{
					if (!string.IsNullOrEmpty(row.Path) && !rightMap.ContainsKey(row.Path))
					{
						rightMap.Add(row.Path, row);
					}
				}
			}
			HashSet<string> consumed = new HashSet<string>(StringComparer.Ordinal);
			if (left != null)
			{
				foreach (EpubRow row in left)
				{
					if (string.IsNullOrEmpty(row.Path) || consumed.Contains(row.Path))
					{
						continue;
					}
					consumed.Add(row.Path);
					if (rightMap.TryGetValue(row.Path, out EpubRow match))
					{
						bool same = string.Equals(row.Value ?? string.Empty, match.Value ?? string.Empty, StringComparison.Ordinal);
						AddRow(result, row.Path, row.Value, match.Value, same ? EpubState.Same : EpubState.Changed);
					}
					else
					{
						AddRow(result, row.Path, row.Value, null, EpubState.LeftOnly);
					}
				}
			}
			if (right != null)
			{
				foreach (EpubRow row in right)
				{
					if (string.IsNullOrEmpty(row.Path) || consumed.Contains(row.Path))
					{
						continue;
					}
					consumed.Add(row.Path);
					AddRow(result, row.Path, null, row.Value, EpubState.RightOnly);
				}
			}
			return result;
		}

		/// <summary>章节按 spine 序号对齐：同序号比较 href 与标题。</summary>
		internal static ChapterDiffResult ComputeChapters(List<EpubChapter> left, List<EpubChapter> right)
		{
			ChapterDiffResult result = new ChapterDiffResult();
			Dictionary<int, EpubChapter> rightMap = new Dictionary<int, EpubChapter>();
			if (right != null)
			{
				foreach (EpubChapter chapter in right)
				{
					if (!rightMap.ContainsKey(chapter.Index))
					{
						rightMap.Add(chapter.Index, chapter);
					}
				}
			}
			HashSet<int> consumed = new HashSet<int>();
			if (left != null)
			{
				foreach (EpubChapter chapter in left)
				{
					if (consumed.Contains(chapter.Index))
					{
						continue;
					}
					consumed.Add(chapter.Index);
					if (rightMap.TryGetValue(chapter.Index, out EpubChapter match))
					{
						bool same = string.Equals(chapter.Href ?? string.Empty, match.Href ?? string.Empty, StringComparison.Ordinal)
							&& string.Equals(chapter.Title ?? string.Empty, match.Title ?? string.Empty, StringComparison.Ordinal);
						AddChapter(result, chapter, match, same ? EpubState.Same : EpubState.Changed);
					}
					else
					{
						AddChapter(result, chapter, null, EpubState.LeftOnly);
					}
				}
			}
			if (right != null)
			{
				foreach (EpubChapter chapter in right)
				{
					if (consumed.Contains(chapter.Index))
					{
						continue;
					}
					consumed.Add(chapter.Index);
					AddChapter(result, null, chapter, EpubState.RightOnly);
				}
			}
			return result;
		}

		private static void AddRow(EpubDiffResult result, string path, string left, string right, EpubState state)
		{
			result.Rows.Add(new EpubRowDiff
			{
				Path = path,
				Left = left,
				Right = right,
				State = state
			});
			switch (state)
			{
			case EpubState.Changed:
				result.Changed++;
				break;
			case EpubState.LeftOnly:
				result.LeftOnly++;
				break;
			case EpubState.RightOnly:
				result.RightOnly++;
				break;
			}
		}

		private static void AddChapter(ChapterDiffResult result, EpubChapter left, EpubChapter right, EpubState state)
		{
			result.Rows.Add(new EpubChapterDiff
			{
				Left = left,
				Right = right,
				State = state
			});
			switch (state)
			{
			case EpubState.Changed:
				result.Changed++;
				break;
			case EpubState.LeftOnly:
				result.LeftOnly++;
				break;
			case EpubState.RightOnly:
				result.RightOnly++;
				break;
			}
		}
	}
}
