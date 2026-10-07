using System.Collections.Generic;

namespace ForkPlus.Plugins.Structured
{
	/// <summary>结构化数据的节点种类：标量 / 映射（对象 / 表 / 节点）/ 序列（数组）。</summary>
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
	/// 与具体格式（JSON / YAML / TOML / XML / INI）无关的统一数据模型。
	/// 五种解析器都把各自的文档拍扁成同一棵树，后续的拍平、diff、渲染只认这一套。
	/// </summary>
	internal sealed class DataNode
	{
		internal DataKind Kind;

		/// <summary>标量的展示文本（数字 / 字符串 / 布尔 / null 都归一成字符串，diff 按字符串比）。</summary>
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

	/// <summary>某个叶子节点的「键路径 + 展示值」。</summary>
	internal sealed class FlatEntry
	{
		internal string Path;

		internal string Value;

		/// <summary>该节点是空映射 / 空序列（本身无标量值，展示为 <c>{}</c> / <c>[]</c>）。</summary>
		internal bool Container;
	}

	/// <summary>把数据树按深度优先拍平成「键路径 → 值」列表，路径用 <c>a.b[0].c</c> 记法。</summary>
	internal static class DataFlatten
	{
		internal static List<FlatEntry> Flatten(DataNode root)
		{
			List<FlatEntry> list = new List<FlatEntry>();
			if (root != null)
			{
				Walk(root, string.Empty, list);
			}
			return list;
		}

		private static void Walk(DataNode node, string path, List<FlatEntry> list)
		{
			switch (node.Kind)
			{
			case DataKind.Scalar:
				list.Add(new FlatEntry
				{
					Path = path,
					Value = node.Value
				});
				break;
			case DataKind.Map:
				if (node.Entries.Count == 0)
				{
					list.Add(new FlatEntry
					{
						Path = path,
						Value = "{}",
						Container = true
					});
					break;
				}
				foreach (DataEntry entry in node.Entries)
				{
					Walk(entry.Node, Join(path, entry.Key), list);
				}
				break;
			case DataKind.Seq:
				if (node.Items.Count == 0)
				{
					list.Add(new FlatEntry
					{
						Path = path,
						Value = "[]",
						Container = true
					});
					break;
				}
				for (int i = 0; i < node.Items.Count; i++)
				{
					Walk(node.Items[i], path + "[" + i + "]", list);
				}
				break;
			}
		}

		private static string Join(string parent, string key)
		{
			return string.IsNullOrEmpty(parent) ? key : (parent + "." + key);
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

		internal int Same;

		internal int Changed;

		internal int LeftOnly;

		internal int RightOnly;

		internal int Total => Rows.Count;

		/// <summary>任一侧缺失时，缺失侧展示此占位而不是留空，避免看起来像渲染失败。</summary>
		internal bool HasDifferences => Changed > 0 || LeftOnly > 0 || RightOnly > 0;
	}

	/// <summary>
	/// 语义 diff：把两侧数据树各自拍平成键路径集合，按路径求并集后逐行判定
	/// 相同 / 已变更 / 仅左 / 仅右。顺序取「先左后右」——左侧原有路径保持原序，
	/// 右侧独有的路径追加在后，便于顺着文件结构读。
	/// </summary>
	internal static class DataDiff
	{
		internal static DiffSummary Compute(DataNode left, DataNode right)
		{
			List<FlatEntry> leftList = DataFlatten.Flatten(left);
			List<FlatEntry> rightList = DataFlatten.Flatten(right);

			Dictionary<string, FlatEntry> leftMap = Index(leftList);
			Dictionary<string, FlatEntry> rightMap = Index(rightList);

			DiffSummary summary = new DiffSummary();
			HashSet<string> emitted = new HashSet<string>();
			Emit(leftList, leftMap, rightMap, summary, emitted);
			Emit(rightList, leftMap, rightMap, summary, emitted);
			return summary;
		}

		private static void Emit(List<FlatEntry> order, Dictionary<string, FlatEntry> leftMap, Dictionary<string, FlatEntry> rightMap, DiffSummary summary, HashSet<string> emitted)
		{
			foreach (FlatEntry entry in order)
			{
				if (!emitted.Add(entry.Path))
				{
					continue;
				}
				bool hasLeft = leftMap.TryGetValue(entry.Path, out FlatEntry left);
				bool hasRight = rightMap.TryGetValue(entry.Path, out FlatEntry right);
				DiffRow row = new DiffRow
				{
					Path = entry.Path,
					LeftPresent = hasLeft,
					RightPresent = hasRight,
					Left = hasLeft ? left.Value : null,
					Right = hasRight ? right.Value : null
				};
				if (hasLeft && hasRight)
				{
					if (string.Equals(left.Value, right.Value, System.StringComparison.Ordinal))
					{
						row.State = DiffState.Same;
						summary.Same++;
					}
					else
					{
						row.State = DiffState.Changed;
						summary.Changed++;
					}
				}
				else if (hasLeft)
				{
					row.State = DiffState.LeftOnly;
					summary.LeftOnly++;
				}
				else
				{
					row.State = DiffState.RightOnly;
					summary.RightOnly++;
				}
				summary.Rows.Add(row);
			}
		}

		private static Dictionary<string, FlatEntry> Index(List<FlatEntry> list)
		{
			Dictionary<string, FlatEntry> map = new Dictionary<string, FlatEntry>();
			foreach (FlatEntry entry in list)
			{
				if (!map.ContainsKey(entry.Path))
				{
					map.Add(entry.Path, entry);
				}
			}
			return map;
		}
	}
}
