using System;
using System.Collections.Generic;

namespace ForkPlus.Plugins.Pcap
{
	/// <summary>统计表的一行：键路径 → 标量值（两侧按 Path 并集对齐做语义 diff）。</summary>
	internal sealed class PcapRow
	{
		internal string Path;

		internal string Value;
	}

	/// <summary>包列表的一项：相对时间 / 长度 / 协议 / 五元组（含端口时为 ip:port）。</summary>
	internal sealed class PcapPacket
	{
		/// <summary>包序号（从 1 起），包列表按它对齐。</summary>
		internal int Index;

		/// <summary>抓包时间戳（秒，绝对值）。</summary>
		internal double Seconds;

		/// <summary>相对首个包的毫秒数（渲染 / 对齐用）。</summary>
		internal double RelMs;

		internal long Length;

		internal string Proto = "Other";

		internal string Source = string.Empty;

		internal string Dest = string.Empty;

		/// <summary>单格摘要：<c>0.152s · 74 B · TCP 10.0.0.1:52311 → 10.0.0.2:443</c>；两侧相等即判「相同」。</summary>
		internal string Summary = string.Empty;
	}

	/// <summary>一次解析的结果：格式名 + 统计行 + 包列表（聚合在解析阶段全部算好）。</summary>
	internal sealed class PcapDocument
	{
		internal string Format = "?";

		internal List<PcapRow> Rows = new List<PcapRow>();

		internal List<PcapPacket> Packets = new List<PcapPacket>();

		/// <summary>包总数（统计按全部包计算，<see cref="Packets"/> 只保留前若干个）。</summary>
		internal int PacketCount;

		/// <summary>包数达到硬上限（200000）被截断。</summary>
		internal bool Truncated;
	}

	internal enum PcapState
	{
		Same,
		Changed,
		LeftOnly,
		RightOnly
	}

	/// <summary>统计 diff 的一行：同一键路径在旧 / 新两侧的值与变更状态。</summary>
	internal sealed class PcapDiffRow
	{
		internal string Path;

		/// <summary>旧值；仅右侧有时为 null（视图渲染成 not present）。</summary>
		internal string Left;

		/// <summary>新值；仅左侧有时为 null。</summary>
		internal string Right;

		internal PcapState State;
	}

	/// <summary>统计行 diff 结果（键路径并集四态）。</summary>
	internal sealed class PcapDiffResult
	{
		internal List<PcapDiffRow> Rows = new List<PcapDiffRow>();

		internal int Changed;

		internal int LeftOnly;

		internal int RightOnly;

		internal int Total => Rows.Count;

		internal bool HasDifferences => Changed > 0 || LeftOnly > 0 || RightOnly > 0;
	}

	/// <summary>包 diff 的一行：同一序号在旧 / 新两侧的包与变更状态。</summary>
	internal sealed class PacketDiffRow
	{
		internal PcapPacket Left;

		internal PcapPacket Right;

		internal PcapState State;
	}

	/// <summary>包列表 diff 结果（按 Index 对齐，Summary 相等 = 相同）。</summary>
	internal sealed class PacketDiffResult
	{
		internal List<PacketDiffRow> Rows = new List<PacketDiffRow>();

		internal int Changed;

		internal int LeftOnly;

		internal int RightOnly;

		internal int Total => Rows.Count;

		internal bool HasDifferences => Changed > 0 || LeftOnly > 0 || RightOnly > 0;
	}

	/// <summary>
	/// 抓包语义 diff：统计行两侧各按 Path 排序，取并集逐行四态；
	/// 包列表按 Index 对齐，两侧同序号摘要全等判「相同」。
	/// </summary>
	internal static class PcapDiff
	{
		internal static PcapDiffResult ComputeRows(PcapDocument left, PcapDocument right)
		{
			PcapDiffResult result = new PcapDiffResult();
			List<PcapRow> leftRows = left?.Rows;
			List<PcapRow> rightRows = right?.Rows;
			int i = 0;
			int j = 0;
			while (i < (leftRows?.Count ?? 0) || j < (rightRows?.Count ?? 0))
			{
				if (leftRows == null || i >= leftRows.Count)
				{
					Add(result, rightRows[j].Path, null, rightRows[j].Value, PcapState.RightOnly);
					j++;
					continue;
				}
				if (rightRows == null || j >= rightRows.Count)
				{
					Add(result, leftRows[i].Path, leftRows[i].Value, null, PcapState.LeftOnly);
					i++;
					continue;
				}
				int comparison = string.CompareOrdinal(leftRows[i].Path, rightRows[j].Path);
				if (comparison == 0)
				{
					bool same = string.Equals(leftRows[i].Value, rightRows[j].Value, StringComparison.Ordinal);
					Add(result, leftRows[i].Path, leftRows[i].Value, rightRows[j].Value, same ? PcapState.Same : PcapState.Changed);
					i++;
					j++;
				}
				else if (comparison < 0)
				{
					Add(result, leftRows[i].Path, leftRows[i].Value, null, PcapState.LeftOnly);
					i++;
				}
				else
				{
					Add(result, rightRows[j].Path, null, rightRows[j].Value, PcapState.RightOnly);
					j++;
				}
			}
			return result;
		}

		private static void Add(PcapDiffResult result, string path, string left, string right, PcapState state)
		{
			result.Rows.Add(new PcapDiffRow
			{
				Path = path,
				Left = left,
				Right = right,
				State = state
			});
			switch (state)
			{
			case PcapState.Changed:
				result.Changed++;
				break;
			case PcapState.LeftOnly:
				result.LeftOnly++;
				break;
			case PcapState.RightOnly:
				result.RightOnly++;
				break;
			}
		}

		internal static PacketDiffResult ComputePackets(PcapDocument left, PcapDocument right)
		{
			PacketDiffResult result = new PacketDiffResult();
			List<PcapPacket> leftPackets = left?.Packets;
			List<PcapPacket> rightPackets = right?.Packets;
			int i = 0;
			int j = 0;
			while (i < (leftPackets?.Count ?? 0) || j < (rightPackets?.Count ?? 0))
			{
				if (leftPackets == null || i >= leftPackets.Count)
				{
					Add(result, null, rightPackets[j], PcapState.RightOnly);
					j++;
					continue;
				}
				if (rightPackets == null || j >= rightPackets.Count)
				{
					Add(result, leftPackets[i], null, PcapState.LeftOnly);
					i++;
					continue;
				}
				int comparison = leftPackets[i].Index.CompareTo(rightPackets[j].Index);
				if (comparison == 0)
				{
					bool same = string.Equals(leftPackets[i].Summary, rightPackets[j].Summary, StringComparison.Ordinal);
					Add(result, leftPackets[i], rightPackets[j], same ? PcapState.Same : PcapState.Changed);
					i++;
					j++;
				}
				else if (comparison < 0)
				{
					Add(result, leftPackets[i], null, PcapState.LeftOnly);
					i++;
				}
				else
				{
					Add(result, null, rightPackets[j], PcapState.RightOnly);
					j++;
				}
			}
			return result;
		}

		private static void Add(PacketDiffResult result, PcapPacket left, PcapPacket right, PcapState state)
		{
			result.Rows.Add(new PacketDiffRow
			{
				Left = left,
				Right = right,
				State = state
			});
			switch (state)
			{
			case PcapState.Changed:
				result.Changed++;
				break;
			case PcapState.LeftOnly:
				result.LeftOnly++;
				break;
			case PcapState.RightOnly:
				result.RightOnly++;
				break;
			}
		}
	}
}
