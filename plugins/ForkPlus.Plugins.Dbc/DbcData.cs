using System;
using System.Collections.Generic;
using System.Text;

namespace ForkPlus.Plugins.Dbc
{
	/// <summary>数据节点的种类：标量 / 映射（对象 / 报文 / 信号）/ 序列。</summary>
	internal enum DataKind
	{
		Scalar,
		Map,
		Seq
	}

	/// <summary>映射里的一项（保序）。</summary>
	internal sealed class DataEntry
	{
		internal string Key;

		internal DataNode Node;

		internal DataEntry(string key, DataNode node)
		{
			Key = key;
			Node = node;
		}
	}

	/// <summary>
	/// 与具体解析库无关的统一数据模型：DBC 解析后把节点 / 报文 / 信号 / 环境变量拍扁成这棵树，
	/// 后续的 diff、渲染只认这一套（与结构化数据插件的模型同源，插件各自自包含）。
	/// </summary>
	internal sealed class DataNode
	{
		internal DataKind Kind;

		/// <summary>标量的展示文本（数字 / 字符串 / 布尔都归一成字符串，diff 按字符串比）。</summary>
		internal string Value;

		/// <summary>映射项（仅 <see cref="DataKind.Map"/>）。</summary>
		internal List<DataEntry> Entries;

		/// <summary>序列项（仅 <see cref="DataKind.Seq"/>）。</summary>
		internal List<DataNode> Items;

		internal static DataNode Scalar(string value)
		{
			return new DataNode
			{
				Kind = DataKind.Scalar,
				Value = value ?? string.Empty
			};
		}

		internal static DataNode Map()
		{
			return new DataNode
			{
				Kind = DataKind.Map,
				Entries = new List<DataEntry>()
			};
		}

		internal static DataNode Seq()
		{
			return new DataNode
			{
				Kind = DataKind.Seq,
				Items = new List<DataNode>()
			};
		}

		internal void Add(string key, DataNode node)
		{
			Entries.Add(new DataEntry(key, node));
		}

		internal void Add(DataNode node)
		{
			Items.Add(node);
		}
	}

	internal enum DiffState
	{
		Same,
		Changed,
		LeftOnly,
		RightOnly
	}

	/// <summary>语义 diff 的一行：同一键路径在旧 / 新两侧的值与变更状态。</summary>
	internal sealed class DiffRow
	{
		internal string Path;

		internal string Left;

		internal string Right;

		internal bool LeftPresent;

		internal bool RightPresent;

		internal DiffState State;
	}

	internal sealed class DiffSummary
	{
		internal List<DiffRow> Rows = new List<DiffRow>();

		/// <summary>物化到 <see cref="Rows"/> 的行数上限：计数照常进行（状态行需要全量统计），
		/// 超限的行不再生成键路径字符串，避免超大文件的行列表本身成为内存 / GC 瓶颈。</summary>
		internal int MaxRows;

		/// <summary>Same 行是否物化进 <see cref="Rows"/>（默认只物化差异行，表格虚拟化后按需看全量）。</summary>
		internal bool IncludeSame;

		internal int Same;

		internal int Changed;

		internal int LeftOnly;

		internal int RightOnly;

		internal int Total => Same + Changed + LeftOnly + RightOnly;

		internal bool HasDifferences => Changed > 0 || LeftOnly > 0 || RightOnly > 0;
	}

	/// <summary>
	/// 语义 diff（v2 重写）：不再「两侧各自拍平成键路径列表 + 字典求并集」，而是直接在两棵树上
	/// 递归做保序归并——
	/// <list type="bullet">
	/// <item>键路径用段栈（<c>segments</c>）回溯共享，只在真正物化一行时才拼接字符串；
	/// 旧实现对每个叶子按层级重复分配完整路径串（25 万叶子 ≈ 每侧上百万次字符串分配），是 GC 瓶颈；</item>
	/// <item>Same 行默认不物化（计数仍全量），键路径表只携带差异行；</item>
	/// <item>行序与旧实现一致：左侧原序在前，右侧独有路径按右序追加。</item>
	/// </list>
	/// 空 Map/Seq 沿用旧拍平语义：空容器本身是一行叶子（<c>{}</c> / <c>[]</c>），非空容器不占行。
	/// </summary>
	internal static class DataDiff
	{
		/// <summary>计算两棵树的语义 diff。maxRows &lt; 0 视为不限行数。</summary>
		internal static DiffSummary Compute(DataNode left, DataNode right, int maxRows, bool includeSame)
		{
			DiffSummary summary = new DiffSummary
			{
				MaxRows = maxRows < 0 ? int.MaxValue : maxRows,
				IncludeSame = includeSame
			};
			List<string> segments = new List<string>(24);
			Walk(left, right, segments, summary);
			return summary;
		}

		private static void Walk(DataNode left, DataNode right, List<string> segments, DiffSummary summary)
		{
			if (left == null && right == null)
			{
				return;
			}
			if (left == null)
			{
				EmitSide(right, DiffState.RightOnly, segments, summary);
				return;
			}
			if (right == null)
			{
				EmitSide(left, DiffState.LeftOnly, segments, summary);
				return;
			}
			if (left.Kind != right.Kind)
			{
				// 类型不一致：旧拍平语义下两侧路径集合不相交（标量无子路径），各自整棵按单侧输出。
				EmitSide(left, DiffState.LeftOnly, segments, summary);
				EmitSide(right, DiffState.RightOnly, segments, summary);
				return;
			}
			switch (left.Kind)
			{
			case DataKind.Scalar:
				if (string.Equals(left.Value, right.Value, StringComparison.Ordinal))
				{
					AddRow(summary, segments, left.Value, right.Value, DiffState.Same);
				}
				else
				{
					AddRow(summary, segments, left.Value, right.Value, DiffState.Changed);
				}
				break;
			case DataKind.Map:
				MergeMaps(left, right, segments, summary);
				break;
			case DataKind.Seq:
				MergeSeqs(left, right, segments, summary);
				break;
			}
		}

		private static void MergeMaps(DataNode left, DataNode right, List<string> segments, DiffSummary summary)
		{
			if (left.Entries.Count == 0 && right.Entries.Count == 0)
			{
				AddRow(summary, segments, "{}", "{}", DiffState.Same);
				return;
			}
			if (left.Entries.Count == 0)
			{
				AddRow(summary, segments, "{}", null, DiffState.LeftOnly);
				EmitMapChildren(right, DiffState.RightOnly, segments, summary);
				return;
			}
			if (right.Entries.Count == 0)
			{
				AddRow(summary, segments, null, "{}", DiffState.RightOnly);
				EmitMapChildren(left, DiffState.LeftOnly, segments, summary);
				return;
			}
			Dictionary<string, DataNode> rightByKey = new Dictionary<string, DataNode>(right.Entries.Count, StringComparer.Ordinal);
			for (int i = 0; i < right.Entries.Count; i++)
			{
				DataEntry entry = right.Entries[i];
				if (!rightByKey.ContainsKey(entry.Key))
				{
					rightByKey.Add(entry.Key, entry.Node);
				}
			}
			HashSet<string> merged = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < left.Entries.Count; i++)
			{
				DataEntry entry = left.Entries[i];
				if (!merged.Add(entry.Key))
				{
					continue;
				}
				rightByKey.TryGetValue(entry.Key, out DataNode rightNode);
				segments.Add(entry.Key);
				Walk(entry.Node, rightNode, segments, summary);
				segments.RemoveAt(segments.Count - 1);
			}
			for (int i = 0; i < right.Entries.Count; i++)
			{
				DataEntry entry = right.Entries[i];
				if (!merged.Add(entry.Key))
				{
					continue;
				}
				segments.Add(entry.Key);
				Walk(null, entry.Node, segments, summary);
				segments.RemoveAt(segments.Count - 1);
			}
		}

		private static void MergeSeqs(DataNode left, DataNode right, List<string> segments, DiffSummary summary)
		{
			if (left.Items.Count == 0 && right.Items.Count == 0)
			{
				AddRow(summary, segments, "[]", "[]", DiffState.Same);
				return;
			}
			if (left.Items.Count == 0)
			{
				AddRow(summary, segments, "[]", null, DiffState.LeftOnly);
				EmitSeqChildren(right, DiffState.RightOnly, segments, summary);
				return;
			}
			if (right.Items.Count == 0)
			{
				AddRow(summary, segments, null, "[]", DiffState.RightOnly);
				EmitSeqChildren(left, DiffState.LeftOnly, segments, summary);
				return;
			}
			int shared = Math.Min(left.Items.Count, right.Items.Count);
			for (int i = 0; i < shared; i++)
			{
				segments.Add("[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]");
				Walk(left.Items[i], right.Items[i], segments, summary);
				segments.RemoveAt(segments.Count - 1);
			}
			for (int i = shared; i < left.Items.Count; i++)
			{
				segments.Add("[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]");
				Walk(left.Items[i], null, segments, summary);
				segments.RemoveAt(segments.Count - 1);
			}
			for (int i = shared; i < right.Items.Count; i++)
			{
				segments.Add("[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]");
				Walk(null, right.Items[i], segments, summary);
				segments.RemoveAt(segments.Count - 1);
			}
		}

		/// <summary>整棵子树按单侧（仅左 / 仅右）输出：叶子逐行上报，容器下钻。</summary>
		private static void EmitSide(DataNode node, DiffState state, List<string> segments, DiffSummary summary)
		{
			switch (node.Kind)
			{
			case DataKind.Scalar:
				AddRow(summary, segments,
					state == DiffState.LeftOnly ? node.Value : null,
					state == DiffState.LeftOnly ? null : node.Value,
					state);
				break;
			case DataKind.Map:
				if (node.Entries.Count == 0)
				{
					AddRow(summary, segments,
						state == DiffState.LeftOnly ? "{}" : null,
						state == DiffState.LeftOnly ? null : "{}",
						state);
				}
				else
				{
					EmitMapChildren(node, state, segments, summary);
				}
				break;
			case DataKind.Seq:
				if (node.Items.Count == 0)
				{
					AddRow(summary, segments,
						state == DiffState.LeftOnly ? "[]" : null,
						state == DiffState.LeftOnly ? null : "[]",
						state);
				}
				else
				{
					EmitSeqChildren(node, state, segments, summary);
				}
				break;
			}
		}

		private static void EmitMapChildren(DataNode node, DiffState state, List<string> segments, DiffSummary summary)
		{
			for (int i = 0; i < node.Entries.Count; i++)
			{
				segments.Add(node.Entries[i].Key);
				EmitSide(node.Entries[i].Node, state, segments, summary);
				segments.RemoveAt(segments.Count - 1);
			}
		}

		private static void EmitSeqChildren(DataNode node, DiffState state, List<string> segments, DiffSummary summary)
		{
			for (int i = 0; i < node.Items.Count; i++)
			{
				segments.Add("[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]");
				EmitSide(node.Items[i], state, segments, summary);
				segments.RemoveAt(segments.Count - 1);
			}
		}

		/// <summary>计数总是进行（状态行需要全量统计）；行物化受 Same 过滤 + 行数上限约束。</summary>
		private static void AddRow(DiffSummary summary, List<string> segments, string left, string right, DiffState state)
		{
			switch (state)
			{
			case DiffState.Same:
				summary.Same++;
				break;
			case DiffState.Changed:
				summary.Changed++;
				break;
			case DiffState.LeftOnly:
				summary.LeftOnly++;
				break;
			case DiffState.RightOnly:
				summary.RightOnly++;
				break;
			}
			if (state == DiffState.Same && !summary.IncludeSame)
			{
				return;
			}
			if (summary.Rows.Count >= summary.MaxRows)
			{
				return;
			}
			summary.Rows.Add(new DiffRow
			{
				Path = JoinPath(segments),
				Left = left,
				Right = right,
				LeftPresent = state != DiffState.RightOnly,
				RightPresent = state != DiffState.LeftOnly,
				State = state
			});
		}

		/// <summary>段栈拼键路径：映射段以 <c>.</c> 相连，序列段 <c>[i]</c> 直接追加（与旧拍平记法一致）。</summary>
		private static string JoinPath(List<string> segments)
		{
			if (segments.Count == 1)
			{
				return segments[0];
			}
			int capacity = 0;
			for (int i = 0; i < segments.Count; i++)
			{
				capacity += segments[i].Length + 1;
			}
			StringBuilder builder = new StringBuilder(capacity);
			for (int i = 0; i < segments.Count; i++)
			{
				if (i > 0 && segments[i][0] != '[')
				{
					builder.Append('.');
				}
				builder.Append(segments[i]);
			}
			return builder.ToString();
		}
	}
}
