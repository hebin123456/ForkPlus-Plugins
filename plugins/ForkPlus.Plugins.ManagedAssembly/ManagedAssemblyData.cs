using System;
using System.Collections.Generic;

namespace ForkPlus.Plugins.ManagedAssembly
{
	/// <summary>键路径对比表的一行：稳定键路径 + 展示值（三种口径共用同一行模型）。</summary>
	internal sealed class ManagedRow
	{
		internal string Path;

		internal string Value;
	}

	/// <summary>一次托管程序集解析的结果：程序集标识 + 三种口径的键路径行。</summary>
	internal sealed class ManagedDocument
	{
		/// <summary>程序集标识口径：名字 / 版本 / 公钥标记 / 目标框架 / 架构 / 标志。</summary>
		internal List<ManagedRow> IdentityRows = new List<ManagedRow>();

		/// <summary>类型口径：命名空间→类型→方法 / 字段。</summary>
		internal List<ManagedRow> TypeRows = new List<ManagedRow>();

		/// <summary>引用口径：AssemblyRef 清单。</summary>
		internal List<ManagedRow> ReferenceRows = new List<ManagedRow>();

		/// <summary>程序集简单名（状态行展示用）。</summary>
		internal string AssemblyName;

		/// <summary>程序集版本（状态行展示用）。</summary>
		internal string AssemblyVersion;

		internal int TypeCount;

		internal int MethodCount;

		internal int FieldCount;

		internal int ReferenceCount;

		/// <summary>是否因超阈值截断（类型 / 方法行数上限）。</summary>
		internal bool Truncated;
	}

	internal enum ManagedState
	{
		Same,
		Changed,
		LeftOnly,
		RightOnly
	}

	/// <summary>键路径对齐后的一行：键路径 + 旧 / 新两侧的值 + 变更状态。</summary>
	internal sealed class ManagedRowDiff
	{
		internal string Path;

		internal string Left;

		internal string Right;

		internal ManagedState State;
	}

	/// <summary>键路径 diff 结果：Rows 为键路径并集的四态行，计数用于状态行汇总。</summary>
	internal sealed class ManagedDiffResult
	{
		internal List<ManagedRowDiff> Rows = new List<ManagedRowDiff>();

		internal int Changed;

		internal int LeftOnly;

		internal int RightOnly;

		internal int Total => Rows.Count;

		internal bool HasDifferences => Changed > 0 || LeftOnly > 0 || RightOnly > 0;
	}

	/// <summary>
	/// 托管程序集对齐：两侧键路径行取并集——两侧都有且值相等 = same、都有但不等 = changed、
	/// 仅左侧有 = left only、仅右侧有 = right only；行序保持左侧解析顺序，再追加仅右侧的键。
	/// </summary>
	internal static class ManagedDiff
	{
		internal static ManagedDiffResult ComputeRows(List<ManagedRow> left, List<ManagedRow> right)
		{
			ManagedDiffResult result = new ManagedDiffResult();
			Dictionary<string, ManagedRow> rightMap = new Dictionary<string, ManagedRow>(StringComparer.Ordinal);
			if (right != null)
			{
				foreach (ManagedRow row in right)
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
				foreach (ManagedRow row in left)
				{
					if (string.IsNullOrEmpty(row.Path) || consumed.Contains(row.Path))
					{
						continue;
					}
					consumed.Add(row.Path);
					if (rightMap.TryGetValue(row.Path, out ManagedRow match))
					{
						bool same = string.Equals(row.Value ?? string.Empty, match.Value ?? string.Empty, StringComparison.Ordinal);
						Add(result, row.Path, row.Value, match.Value, same ? ManagedState.Same : ManagedState.Changed);
					}
					else
					{
						Add(result, row.Path, row.Value, null, ManagedState.LeftOnly);
					}
				}
			}
			if (right != null)
			{
				foreach (ManagedRow row in right)
				{
					if (string.IsNullOrEmpty(row.Path) || consumed.Contains(row.Path))
					{
						continue;
					}
					consumed.Add(row.Path);
					Add(result, row.Path, null, row.Value, ManagedState.RightOnly);
				}
			}
			return result;
		}

		private static void Add(ManagedDiffResult result, string path, string left, string right, ManagedState state)
		{
			result.Rows.Add(new ManagedRowDiff
			{
				Path = path,
				Left = left,
				Right = right,
				State = state
			});
			switch (state)
			{
			case ManagedState.Changed:
				result.Changed++;
				break;
			case ManagedState.LeftOnly:
				result.LeftOnly++;
				break;
			case ManagedState.RightOnly:
				result.RightOnly++;
				break;
			}
		}
	}
}