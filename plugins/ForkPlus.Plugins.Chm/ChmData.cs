using System;
using System.Collections.Generic;

namespace ForkPlus.Plugins.Chm
{
	/// <summary>
	/// CHM 目录里的一个条目（PMGL 项）：内部路径（含前导 '/'，目录项以 '/' 结尾）+
	/// 存储空间（1 = 未压缩 / 2 = LZX 压缩 / 其它）+ 内容流内偏移（Start）+ 未压缩长度（Length）+
	/// 内容指纹（Hash，短 hex；未计算或非文件为 null）。
	/// </summary>
	internal sealed class ChmEntry
	{
		internal string Path;

		/// <summary>1 = 未压缩（数据区直读）；2 = LZX 压缩（MSCompressed 内容流内）；其它视为元数据。</summary>
		internal int Space;

		/// <summary>未压缩内容流（或数据区）内的起始偏移。</summary>
		internal long Start;

		/// <summary>未压缩长度（字节）。</summary>
		internal long Length;

		/// <summary>内容指纹（SHA-256 前 8 字节 hex）；未算或不适用为 null。</summary>
		internal string Hash;

		/// <summary>是否为目录项（路径以 '/' 结尾）。</summary>
		internal bool IsDirectory
		{
			get { return !string.IsNullOrEmpty(Path) && Path[Path.Length - 1] == '/'; }
		}
	}

	/// <summary>元数据 / 统计对比表的一行：稳定键路径（如 "title"、"content blocks"）+ 展示值。</summary>
	internal sealed class ChmRow
	{
		internal string Path;

		internal string Value;
	}

	/// <summary>一次 CHM 解析的结果：标题（#SYSTEM title，缺失为 null）+ 元数据行 + 目录条目 + 统计。</summary>
	internal sealed class ChmDocument
	{
		/// <summary>#SYSTEM 里的标题；缺失为 null（视图回退用文件名）。</summary>
		internal string Title;

		/// <summary>元数据 / 统计行（键路径稳定排序）。</summary>
		internal List<ChmRow> Rows = new List<ChmRow>();

		/// <summary>目录条目（含元数据对象与目录项）。</summary>
		internal List<ChmEntry> Entries = new List<ChmEntry>();

		/// <summary>文件条目数（非目录）。</summary>
		internal int FileCount;

		/// <summary>目录条目数（路径以 '/' 结尾）。</summary>
		internal int DirectoryCount;

		/// <summary>全部条目未压缩字节和。</summary>
		internal long TotalBytes;

		// ---- 预览（「预览」模式渲染默认主题页）所需的原始数据引用 ----

		/// <summary>默认主题页内部路径（#SYSTEM 记录 2）；缺失为 null。</summary>
		internal string DefaultTopic;

		/// <summary>CHM 原始字节（按路径取条目内容用；与解析入参同一实例，不再拷贝）。</summary>
		internal byte[] Raw;

		/// <summary>数据区偏移（ITSF data_offset）。</summary>
		internal long DataOffset;

		/// <summary>MSCompressed 存储号；-1 表示未压缩。</summary>
		internal int CompressedSpace = -1;

		/// <summary>已解压内容流（压缩存储条目由其切片）；无压缩或未解压为 null。</summary>
		internal byte[] Decompressed;

		/// <summary>内部路径（不区分大小写）→ 条目。</summary>
		internal Dictionary<string, ChmEntry> ByPath;
	}

	/// <summary>四态：相同 / 已变 / 仅左 / 仅右。</summary>
	internal enum ChmState
	{
		Same,
		Changed,
		LeftOnly,
		RightOnly
	}

	/// <summary>元数据对齐后的一行：键路径 + 旧 / 新两侧的值 + 变更状态。</summary>
	internal sealed class ChmRowDiff
	{
		internal string Path;

		internal string Left;

		internal string Right;

		internal ChmState State;
	}

	/// <summary>目录条目对齐后的一行：按内部路径对齐的左右条目 + 变更状态。</summary>
	internal sealed class ChmEntryDiff
	{
		internal ChmEntry Left;

		internal ChmEntry Right;

		internal ChmState State;
	}

	/// <summary>元数据 diff 结果：Rows 为键路径并集的四态行 + 计数。</summary>
	internal sealed class ChmRowDiffResult
	{
		internal List<ChmRowDiff> Rows = new List<ChmRowDiff>();

		internal int Changed;

		internal int LeftOnly;

		internal int RightOnly;

		internal int Total
		{
			get { return Rows.Count; }
		}

		internal bool HasDifferences
		{
			get { return Changed > 0 || LeftOnly > 0 || RightOnly > 0; }
		}
	}

	/// <summary>目录条目 diff 结果：与 <see cref="ChmRowDiffResult"/> 同构，Row 里带左右 <see cref="ChmEntry"/>。</summary>
	internal sealed class ChmEntryDiffResult
	{
		internal List<ChmEntryDiff> Rows = new List<ChmEntryDiff>();

		internal int Changed;

		internal int LeftOnly;

		internal int RightOnly;

		internal int Total
		{
			get { return Rows.Count; }
		}

		internal bool HasDifferences
		{
			get { return Changed > 0 || LeftOnly > 0 || RightOnly > 0; }
		}
	}

	/// <summary>
	/// CHM 对齐：
	/// <list type="bullet">
	/// <item>元数据 —— 两侧 Rows 按键路径取并集（两侧都有且值相等 = same、不等 = changed、
	/// 仅左 = left only、仅右 = right only）；行序保持解析器产出的稳定顺序。</item>
	/// <item>目录条目 —— 按内部路径（不区分大小写）对齐：两侧都有时比较长度与内容指纹
	/// （长度不等、或两侧都有指纹且不同 = changed），仅一侧有 = left / right only。</item>
	/// </list>
	/// </summary>
	internal static class ChmDiff
	{
		internal static ChmRowDiffResult ComputeRows(List<ChmRow> left, List<ChmRow> right)
		{
			ChmRowDiffResult result = new ChmRowDiffResult();
			Dictionary<string, ChmRow> rightMap = new Dictionary<string, ChmRow>(StringComparer.Ordinal);
			if (right != null)
			{
				foreach (ChmRow row in right)
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
				foreach (ChmRow row in left)
				{
					if (string.IsNullOrEmpty(row.Path) || consumed.Contains(row.Path))
					{
						continue;
					}
					consumed.Add(row.Path);
					if (rightMap.TryGetValue(row.Path, out ChmRow match))
					{
						bool same = string.Equals(row.Value ?? string.Empty, match.Value ?? string.Empty, StringComparison.Ordinal);
						AddRow(result, row.Path, row.Value, match.Value, same ? ChmState.Same : ChmState.Changed);
					}
					else
					{
						AddRow(result, row.Path, row.Value, null, ChmState.LeftOnly);
					}
				}
			}
			if (right != null)
			{
				foreach (ChmRow row in right)
				{
					if (string.IsNullOrEmpty(row.Path) || consumed.Contains(row.Path))
					{
						continue;
					}
					consumed.Add(row.Path);
					AddRow(result, row.Path, null, row.Value, ChmState.RightOnly);
				}
			}
			return result;
		}

		internal static ChmEntryDiffResult ComputeEntries(List<ChmEntry> left, List<ChmEntry> right)
		{
			ChmEntryDiffResult result = new ChmEntryDiffResult();
			Dictionary<string, ChmEntry> rightMap = new Dictionary<string, ChmEntry>(StringComparer.OrdinalIgnoreCase);
			if (right != null)
			{
				foreach (ChmEntry entry in right)
				{
					if (!string.IsNullOrEmpty(entry.Path) && !rightMap.ContainsKey(entry.Path))
					{
						rightMap.Add(entry.Path, entry);
					}
				}
			}
			HashSet<string> consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (left != null)
			{
				foreach (ChmEntry entry in left)
				{
					if (string.IsNullOrEmpty(entry.Path) || consumed.Contains(entry.Path))
					{
						continue;
					}
					consumed.Add(entry.Path);
					if (rightMap.TryGetValue(entry.Path, out ChmEntry match))
					{
						AddEntry(result, entry, match, SameEntry(entry, match) ? ChmState.Same : ChmState.Changed);
					}
					else
					{
						AddEntry(result, entry, null, ChmState.LeftOnly);
					}
				}
			}
			if (right != null)
			{
				foreach (ChmEntry entry in right)
				{
					if (string.IsNullOrEmpty(entry.Path) || consumed.Contains(entry.Path))
					{
						continue;
					}
					consumed.Add(entry.Path);
					AddEntry(result, null, entry, ChmState.RightOnly);
				}
			}
			return result;
		}

		/// <summary>条目是否相同：长度不同即不同；长度相同且两侧都有内容指纹时按指纹判定。</summary>
		private static bool SameEntry(ChmEntry left, ChmEntry right)
		{
			if (left.Length != right.Length)
			{
				return false;
			}
			if (left.Hash != null && right.Hash != null)
			{
				return string.Equals(left.Hash, right.Hash, StringComparison.Ordinal);
			}
			return true;
		}

		private static void AddRow(ChmRowDiffResult result, string path, string left, string right, ChmState state)
		{
			result.Rows.Add(new ChmRowDiff
			{
				Path = path,
				Left = left,
				Right = right,
				State = state
			});
			CountState(state, out int changed, out int leftOnly, out int rightOnly);
			result.Changed += changed;
			result.LeftOnly += leftOnly;
			result.RightOnly += rightOnly;
		}

		private static void AddEntry(ChmEntryDiffResult result, ChmEntry left, ChmEntry right, ChmState state)
		{
			result.Rows.Add(new ChmEntryDiff
			{
				Left = left,
				Right = right,
				State = state
			});
			CountState(state, out int changed, out int leftOnly, out int rightOnly);
			result.Changed += changed;
			result.LeftOnly += leftOnly;
			result.RightOnly += rightOnly;
		}

		private static void CountState(ChmState state, out int changed, out int leftOnly, out int rightOnly)
		{
			changed = state == ChmState.Changed ? 1 : 0;
			leftOnly = state == ChmState.LeftOnly ? 1 : 0;
			rightOnly = state == ChmState.RightOnly ? 1 : 0;
		}
	}
}