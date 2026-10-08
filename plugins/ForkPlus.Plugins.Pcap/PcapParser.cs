using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ForkPlus.Plugins.Pcap
{
	/// <summary>
	/// 抓包解析：把 pcap（含纳秒变体）/ pcapng 两种容器各自解析成 <see cref="PcapDocument"/>，
	/// 并在解析阶段同步完成聚合（统计行 + 包列表）。
	///
	/// pcap：前 4 字节魔数（按小端 u32 读）决定后续字段的字节序与时间精度——
	/// 0xA1B2C3D4 小端微秒（文件字节 D4 C3 B2 A1）、0xD4C3B2A1 大端微秒、
	/// 0xA1B23C4D 小端纳秒、0x4D3CB2A1 大端纳秒；
	/// 随后 20 字节文件头（版本 / thiszone / sigfigs / snaplen / network），再逐包
	/// ts_sec / ts_frac / caplen / origlen + caplen 字节数据。
	///
	/// pcapng：块循环（类型 u32 + 总长 u32 + 体 + 尾部总长 u32，块 / 选项长度均 pad 到 4 字节倍），
	/// 字节序由首块 SHB（0x0A0D0D0A）体内偏移 8 处的魔数 0x1A2B3C4D（大端）/ 0x4D3C2B1A（小端）决定；
	/// IDB（0x00000001）登记接口（linktype + if_tsresol，选项 9：首字节高位置 1 → 2^-n 秒，否则 10^-n 秒），
	/// EPB（0x00000006）出包（时间 = ts_high&lt;&lt;32 | ts_low × 单位）、SPB（0x00000003）出包（无时间戳，沿用上一包），
	/// SHB 与未知块按 total_len 跳过。
	///
	/// 包解析（dissect）尽力而为：支持 Ethernet（含 0x8100 / 0x88A8 VLAN）/ raw IP / Linux SLL 链路层，
	/// 剥出 IPv4 的协议与五元组（TCP / UDP 带端口，会话键 ip:port→ip:port）；解析不出的包归 "Other"，
	/// 不让整体失败。截断包能取多少取多少，取不到就降级 "Other"。
	///
	/// 防御：长度越界、魔数不符抛 <see cref="InvalidDataException"/>（"not a pcap/pcapng file"），
	/// 由 <see cref="Parse"/> 统一转成 error 文案，视图按「无法解析」提示处理。
	/// </summary>
	internal static class PcapParser
	{
		/// <summary>单侧包数硬上限：达到即停止读取并记 Truncated。</summary>
		internal const int MaxPackets = 200000;

		/// <summary>包列表保留条数上限（统计仍按全部包计算）。</summary>
		internal const int MaxPacketsKept = 2000;

		/// <summary>会话 Top 行数。</summary>
		private const int TopConversations = 16;

		/// <summary>解析入口：成功返回文档，失败返回 null 并回填 error。</summary>
		internal static PcapDocument Parse(byte[] data, out string error)
		{
			error = null;
			if (data == null)
			{
				data = Array.Empty<byte>();
			}
			try
			{
				if (data.Length >= 4)
				{
					uint magic = (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
					if (magic == 0xA1B2C3D4u || magic == 0xD4C3B2A1u || magic == 0xA1B23C4Du || magic == 0x4D3CB2A1u)
					{
						return ParsePcap(data, magic);
					}
				}
				if (data.Length >= 12 && data[0] == 0x0A && data[1] == 0x0D && data[2] == 0x0D && data[3] == 0x0A)
				{
					return ParsePcapNg(data);
				}
				throw new InvalidDataException("not a pcap/pcapng file");
			}
			catch (Exception ex)
			{
				error = ex.GetType().Name + ": " + ex.Message;
				return null;
			}
		}

		// ---- pcap ----

		private static PcapDocument ParsePcap(byte[] data, uint magic)
		{
			bool bigEndian;
			double fracUnit;
			switch (magic)
			{
			case 0xA1B2C3D4u:
				bigEndian = false;
				fracUnit = 1e-6;
				break;
			case 0xD4C3B2A1u:
				bigEndian = true;
				fracUnit = 1e-6;
				break;
			case 0xA1B23C4Du:
				bigEndian = false;
				fracUnit = 1e-9;
				break;
			default:
				bigEndian = true;
				fracUnit = 1e-9;
				break;
			}
			if (data.Length < 24)
			{
				throw new InvalidDataException("truncated pcap file header");
			}
			// 文件头的版本 / thiszone / sigfigs / snaplen 只影响解析器行为，不参与展示；network 即 linktype。
			int linktype = (int)(ReadU32(data, 20, bigEndian) & 0xFFFFu);
			PcapDocument document = new PcapDocument
			{
				Format = "pcap"
			};
			Aggregator aggregator = new Aggregator();
			int offset = 24;
			while (offset + 16 <= data.Length)
			{
				uint tsSec = ReadU32(data, offset, bigEndian);
				uint tsFrac = ReadU32(data, offset + 4, bigEndian);
				uint caplen = ReadU32(data, offset + 8, bigEndian);
				uint origlen = ReadU32(data, offset + 12, bigEndian);
				if ((long)offset + 16L + caplen > data.Length)
				{
					throw new InvalidDataException("truncated pcap packet data");
				}
				aggregator.Add(data, offset + 16, (int)caplen, tsSec + tsFrac * fracUnit, origlen, linktype);
				offset += 16 + (int)caplen;
				if (aggregator.Count >= MaxPackets)
				{
					aggregator.Truncated = true;
					break;
				}
			}
			aggregator.Finish(document, linktype);
			return document;
		}

		// ---- pcapng ----

		private static PcapDocument ParsePcapNg(byte[] data)
		{
			bool bigEndian;
			if (data[8] == 0x1A && data[9] == 0x2B && data[10] == 0x3C && data[11] == 0x4D)
			{
				bigEndian = true;
			}
			else if (data[8] == 0x4D && data[9] == 0x3C && data[10] == 0x2B && data[11] == 0x1A)
			{
				bigEndian = false;
			}
			else
			{
				throw new InvalidDataException("not a pcap/pcapng file");
			}
			PcapDocument document = new PcapDocument
			{
				Format = "pcapng"
			};
			List<InterfaceInfo> interfaces = new List<InterfaceInfo>();
			Aggregator aggregator = new Aggregator();
			int offset = 0;
			while (offset + 8 <= data.Length)
			{
				uint blockType = ReadU32(data, offset, bigEndian);
				uint totalLen = ReadU32(data, offset + 4, bigEndian);
				if (totalLen < 12u || totalLen % 4u != 0u || (long)offset + totalLen > data.Length)
				{
					throw new InvalidDataException("corrupted pcapng block length");
				}
				if (ReadU32(data, offset + (int)totalLen - 4, bigEndian) != totalLen)
				{
					throw new InvalidDataException("corrupted pcapng block length");
				}
				int bodyStart = offset + 8;
				int bodyEnd = offset + (int)totalLen - 4;
				if (blockType == 0x00000001u)
				{
					// IDB：linktype u16 + reserved u16 + snaplen u32 + options（if_tsresol 定时间分辨率）。
					if (bodyEnd - bodyStart < 8)
					{
						throw new InvalidDataException("corrupted pcapng interface block");
					}
					interfaces.Add(new InterfaceInfo
					{
						Linktype = ReadU16(data, bodyStart, bigEndian),
						TsResol = ReadTsResol(data, bodyStart + 8, bodyEnd, bigEndian)
					});
				}
				else if (blockType == 0x00000006u)
				{
					// EPB：interface id u32 + ts_high/ts_low u32×2 + caplen/origlen u32×2 + 包数据。
					if (interfaces.Count == 0)
					{
						throw new InvalidDataException("pcapng packet before interface block");
					}
					if (bodyEnd - bodyStart < 20)
					{
						throw new InvalidDataException("corrupted pcapng enhanced packet block");
					}
					uint interfaceId = ReadU32(data, bodyStart, bigEndian);
					uint tsHigh = ReadU32(data, bodyStart + 4, bigEndian);
					uint tsLow = ReadU32(data, bodyStart + 8, bigEndian);
					uint caplen = ReadU32(data, bodyStart + 12, bigEndian);
					uint origlen = ReadU32(data, bodyStart + 16, bigEndian);
					if ((long)bodyStart + 20L + caplen > bodyEnd)
					{
						throw new InvalidDataException("truncated pcapng packet data");
					}
					InterfaceInfo info = interfaceId < interfaces.Count ? interfaces[(int)interfaceId] : interfaces[0];
					double seconds = ((ulong)tsHigh << 32 | tsLow) * info.TsResol;
					aggregator.Add(data, bodyStart + 20, (int)caplen, seconds, origlen, info.Linktype);
				}
				else if (blockType == 0x00000003u)
				{
					// SPB：origlen u32 + 数据（pad 到 4 字节倍）；不带时间戳，沿用上一包的时间。
					if (interfaces.Count == 0)
					{
						throw new InvalidDataException("pcapng packet before interface block");
					}
					if (bodyEnd - bodyStart < 4)
					{
						throw new InvalidDataException("corrupted pcapng simple packet block");
					}
					uint origlen = ReadU32(data, bodyStart, bigEndian);
					long available = bodyEnd - bodyStart - 4;
					int count = (int)Math.Min(available, (long)origlen);
					aggregator.Add(data, bodyStart + 4, count, aggregator.LastSeconds, origlen, interfaces[0].Linktype);
				}
				// SHB（0x0A0D0D0A）与未知块按 total_len 跳过。
				offset += (int)totalLen;
				if (aggregator.Count >= MaxPackets)
				{
					aggregator.Truncated = true;
					break;
				}
			}
			aggregator.Finish(document, interfaces.Count > 0 ? interfaces[0].Linktype : 0);
			return document;
		}

		/// <summary>扫 IDB 选项找 if_tsresol（code 9）；缺省 10^-6 秒。选项头 code u16 + len u16，值 pad 到 4 倍。</summary>
		private static double ReadTsResol(byte[] data, int start, int end, bool bigEndian)
		{
			double unit = 1e-6;
			int offset = start;
			while (offset + 4 <= end)
			{
				ushort code = ReadU16(data, offset, bigEndian);
				ushort length = ReadU16(data, offset + 2, bigEndian);
				if (code == 0)
				{
					break;
				}
				if (code == 9 && length >= 1 && offset + 5 <= end)
				{
					byte resolution = data[offset + 4];
					unit = (resolution & 0x80) != 0 ? Math.Pow(2.0, -(resolution & 0x7F)) : Math.Pow(10.0, -resolution);
					break;
				}
				int padded = (length + 3) & ~3;
				if (offset + 4 + padded > end)
				{
					break;
				}
				offset += 4 + padded;
			}
			return unit;
		}

		// ---- 包解析（dissect，尽力而为） ----

		/// <summary>
		/// 剥链路层与 IP 头，取协议名与五元组摘要。linktype 1 = Ethernet（VLAN 再跳 4 字节）、
		/// 101 / 12 / 14 = raw IP、113 = Linux SLL（偏移 14 处 u16 ethertype）；
		/// 0x0800 → IPv4、0x86DD → IPv6、0x0806 → ARP；取不到的归 "Other"。
		/// </summary>
		private static void Dissect(byte[] data, int offset, int count, int linktype, out string proto, out string source, out string dest)
		{
			proto = "Other";
			source = string.Empty;
			dest = string.Empty;
			int ipStart = -1;
			if (linktype == 1)
			{
				if (count < 14)
				{
					return;
				}
				int ethertype = ReadU16(data, offset + 12, true);
				int payload = offset + 14;
				if (ethertype == 0x8100 || ethertype == 0x88A8)
				{
					if (count < 18)
					{
						return;
					}
					ethertype = ReadU16(data, offset + 16, true);
					payload = offset + 18;
				}
				if (ethertype == 0x0800)
				{
					ipStart = payload;
				}
				else if (ethertype == 0x86DD)
				{
					proto = "IPv6";
					return;
				}
				else if (ethertype == 0x0806)
				{
					proto = "ARP";
					return;
				}
				else
				{
					return;
				}
			}
			else if (linktype == 101 || linktype == 12 || linktype == 14)
			{
				// raw IP：按版本位区分 v4 / v6。
				if (count < 1)
				{
					return;
				}
				int version = data[offset] >> 4;
				if (version == 6)
				{
					proto = "IPv6";
					return;
				}
				if (version != 4)
				{
					return;
				}
				ipStart = offset;
			}
			else if (linktype == 113)
			{
				if (count < 16)
				{
					return;
				}
				int ethertype = ReadU16(data, offset + 14, true);
				if (ethertype == 0x0800)
				{
					ipStart = offset + 16;
				}
				else if (ethertype == 0x86DD)
				{
					proto = "IPv6";
					return;
				}
				else if (ethertype == 0x0806)
				{
					proto = "ARP";
					return;
				}
				else
				{
					return;
				}
			}
			else
			{
				return;
			}

			// IPv4：首字节低 4 位 = IHL，头长 IHL×4；protocol 在偏移 9、src 在 12、dst 在 16（均自 IP 头起）。
			int end = offset + count;
			if (ipStart + 20 > end)
			{
				return;
			}
			int ihl = (data[ipStart] & 0x0F) * 4;
			if (ihl < 20 || ipStart + ihl > end)
			{
				return;
			}
			int protocol = data[ipStart + 9];
			string src = FormatIp(data, ipStart + 12);
			string dst = FormatIp(data, ipStart + 16);
			if (protocol == 6)
			{
				proto = "TCP";
			}
			else if (protocol == 17)
			{
				proto = "UDP";
			}
			else if (protocol == 1)
			{
				proto = "ICMP";
				source = src;
				dest = dst;
				return;
			}
			else
			{
				proto = "IP " + protocol.ToString(CultureInfo.InvariantCulture);
				source = src;
				dest = dst;
				return;
			}
			// TCP / UDP：IP 头后 4 字节 = srcport / dstport（大端），会话与摘要带上端口。
			if (ipStart + ihl + 4 <= end)
			{
				int srcPort = ReadU16(data, ipStart + ihl, true);
				int dstPort = ReadU16(data, ipStart + ihl + 2, true);
				source = src + ":" + srcPort.ToString(CultureInfo.InvariantCulture);
				dest = dst + ":" + dstPort.ToString(CultureInfo.InvariantCulture);
			}
			else
			{
				source = src;
				dest = dst;
			}
		}

		// ---- 聚合 ----

		/// <summary>接口表条目：IDB 登记的 linktype 与时间分辨率。</summary>
		private sealed class InterfaceInfo
		{
			internal int Linktype;

			internal double TsResol = 1e-6;
		}

		/// <summary>边读边聚合：包计数 / 字节数 / 时间范围 / 协议分布 / 会话统计 / 包列表。</summary>
		private sealed class Aggregator
		{
			private readonly List<PcapPacket> _packets = new List<PcapPacket>();

			private readonly long[] _protocols = new long[6];

			private readonly Dictionary<string, long> _convPackets = new Dictionary<string, long>();

			private readonly Dictionary<string, long> _convBytes = new Dictionary<string, long>();

			private double _first = double.MaxValue;

			private double _last = double.MinValue;

			private long _bytes;

			internal int Count { get; private set; }

			internal bool Truncated;

			/// <summary>最近一个包的时间戳（SPB 无时间戳时沿用）。</summary>
			internal double LastSeconds;

			internal void Add(byte[] data, int offset, int count, double seconds, long origlen, int linktype)
			{
				Count++;
				_bytes += origlen;
				LastSeconds = seconds;
				if (seconds < _first)
				{
					_first = seconds;
				}
				if (seconds > _last)
				{
					_last = seconds;
				}
				string proto;
				string source;
				string dest;
				Dissect(data, offset, count, linktype, out proto, out source, out dest);
				_protocols[ProtocolBucket(proto)]++;
				if ((proto == "TCP" || proto == "UDP") && source.Length > 0 && dest.Length > 0)
				{
					string key = source + "→" + dest;
					_convPackets.TryGetValue(key, out long packets);
					_convPackets[key] = packets + 1;
					_convBytes.TryGetValue(key, out long bytes);
					_convBytes[key] = bytes + origlen;
				}
				if (_packets.Count < MaxPacketsKept)
				{
					_packets.Add(new PcapPacket
					{
						Index = Count,
						Seconds = seconds,
						Length = origlen,
						Proto = proto,
						Source = source,
						Dest = dest
					});
				}
			}

			/// <summary>收尾：算相对时间与摘要，落统计行（Path 排序稳定，0 也保留，保证两侧对齐）。</summary>
			internal void Finish(PcapDocument document, int linktype)
			{
				document.PacketCount = Count;
				document.Truncated = Truncated;
				CultureInfo culture = CultureInfo.InvariantCulture;
				double first = _first == double.MaxValue ? 0.0 : _first;
				double last = _last == double.MinValue ? 0.0 : _last;
				foreach (PcapPacket packet in _packets)
				{
					packet.RelMs = (packet.Seconds - first) * 1000.0;
					packet.Summary = BuildSummary(packet, culture);
				}
				document.Packets = _packets;

				List<PcapRow> rows = new List<PcapRow>();
				AddRow(rows, "format", document.Format);
				AddRow(rows, "linktype", LinktypeText(linktype, culture));
				AddRow(rows, "packets", Count.ToString(culture));
				AddRow(rows, "bytes", _bytes.ToString(culture));
				AddRow(rows, "first packet", Count > 0 ? FormatLocalTime(first) : "-");
				AddRow(rows, "last packet", Count > 0 ? FormatLocalTime(last) : "-");
				AddRow(rows, "duration", (last - first).ToString("0.###", culture));
				AddRow(rows, "protocol.TCP", _protocols[0].ToString(culture));
				AddRow(rows, "protocol.UDP", _protocols[1].ToString(culture));
				AddRow(rows, "protocol.ICMP", _protocols[2].ToString(culture));
				AddRow(rows, "protocol.IPv6", _protocols[3].ToString(culture));
				AddRow(rows, "protocol.ARP", _protocols[4].ToString(culture));
				AddRow(rows, "protocol.Other", _protocols[5].ToString(culture));
				AddRow(rows, "conversations", _convPackets.Count.ToString(culture));
				List<string> top = new List<string>(_convPackets.Keys);
				top.Sort(delegate (string a, string b)
				{
					int byCount = _convPackets[b].CompareTo(_convPackets[a]);
					return byCount != 0 ? byCount : string.CompareOrdinal(a, b);
				});
				int shown = 0;
				foreach (string key in top)
				{
					if (shown >= TopConversations)
					{
						break;
					}
					shown++;
					AddRow(rows, "conv." + key, _convPackets[key].ToString(culture) + " packets · " + _convBytes[key].ToString(culture) + " bytes");
				}
				rows.Sort(delegate (PcapRow a, PcapRow b)
				{
					return string.CompareOrdinal(a.Path, b.Path);
				});
				document.Rows = rows;
			}
		}

		/// <summary>摘要：<c>0.152s · 74 B · TCP 10.0.0.1:52311 → 10.0.0.2:443</c>；无五元组时只给协议名。</summary>
		private static string BuildSummary(PcapPacket packet, CultureInfo culture)
		{
			string head = (packet.RelMs / 1000.0).ToString("0.###", culture) + "s · " + packet.Length.ToString(culture) + " B · " + packet.Proto;
			if (packet.Source.Length > 0 && packet.Dest.Length > 0)
			{
				return head + " " + packet.Source + " → " + packet.Dest;
			}
			if (packet.Source.Length > 0)
			{
				return head + " " + packet.Source;
			}
			return head;
		}

		/// <summary>协议名 → 统计桶：TCP / UDP / ICMP / IPv6 / ARP 之外（含 "IP n"）一律归 Other。</summary>
		private static int ProtocolBucket(string proto)
		{
			switch (proto)
			{
			case "TCP":
				return 0;
			case "UDP":
				return 1;
			case "ICMP":
				return 2;
			case "IPv6":
				return 3;
			case "ARP":
				return 4;
			default:
				return 5;
			}
		}

		private static void AddRow(List<PcapRow> rows, string path, string value)
		{
			rows.Add(new PcapRow
			{
				Path = path,
				Value = value
			});
		}

		/// <summary>秒 → 本地时间 <c>yyyy-MM-dd HH:mm:ss.fff</c>。</summary>
		private static string FormatLocalTime(double seconds)
		{
			long milliseconds = (long)Math.Round(seconds * 1000.0);
			return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
		}

		/// <summary>linktype 行的值：常见值附可读名，其余只给数字。</summary>
		private static string LinktypeText(int linktype, CultureInfo culture)
		{
			switch (linktype)
			{
			case 0:
				return "0 (loopback)";
			case 1:
				return "1 (Ethernet)";
			case 6:
				return "6 (Token Ring)";
			case 9:
				return "9 (PPP)";
			case 12:
			case 14:
			case 101:
				return linktype.ToString(culture) + " (raw IP)";
			case 113:
				return "113 (Linux SLL)";
			case 276:
				return "276 (Linux SLL2)";
			default:
				return linktype.ToString(culture);
			}
		}

		private static string FormatIp(byte[] data, int offset)
		{
			return data[offset].ToString(CultureInfo.InvariantCulture) + "." + data[offset + 1].ToString(CultureInfo.InvariantCulture) + "." + data[offset + 2].ToString(CultureInfo.InvariantCulture) + "." + data[offset + 3].ToString(CultureInfo.InvariantCulture);
		}

		private static ushort ReadU16(byte[] data, int offset, bool bigEndian)
		{
			return bigEndian
				? (ushort)((data[offset] << 8) | data[offset + 1])
				: (ushort)((data[offset + 1] << 8) | data[offset]);
		}

		private static uint ReadU32(byte[] data, int offset, bool bigEndian)
		{
			if (bigEndian)
			{
				return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
			}
			return (uint)((data[offset + 3] << 24) | (data[offset + 2] << 16) | (data[offset + 1] << 8) | data[offset]);
		}
	}
}
