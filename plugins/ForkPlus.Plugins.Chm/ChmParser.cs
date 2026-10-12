using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ForkPlus.Plugins.Chm
{
	/// <summary>
	/// CHM（已编译 HTML 帮助）解析：纯托管实现，零第三方依赖。
	///
	/// 结构（与 CHMLib 一致，见参考实现的 chm_lib.c）：
	/// <list type="bullet">
	/// <item>文件头 <c>ITSF</c>（0x60 字节，V3）：签名、版本、最后修改时间、语言 ID、
	/// 目录区偏移 <c>dir_offset</c> / 长度 <c>dir_len</c>、数据区偏移 <c>data_offset</c>。</item>
	/// <item>目录头 <c>ITSP</c>（0x54 字节）：块长 <c>block_len</c>、索引根 / 头页号。</item>
	/// <item>目录块 <c>PMGL</c>（叶子）：逐条给出「内部路径 + space + 数据偏移 + 未压缩长度」，
	/// 经 <c>block_next</c> 串成链表；遍历链表即得全部条目（含 <c>::DataSpace/*</c> 元数据对象）。</item>
	/// <item>MSCompressed 存储的 <c>Content</c> 内容流用 LZX 压缩（<see cref="LzxDecoder"/>）；
	/// 是否压缩由 <c>::DataSpace/NameList</c> 里 <c>MSCompressed</c> 的存储号决定，
	/// 重置表 / 控制块 / 内容流三件套齐全时才尝试解压。</item>
	/// </list>
	///
	/// 产出（见 <see cref="ChmDocument"/>）：标题（<c>/#SYSTEM</c> 记录 3）与默认主题（记录 2）等
	/// #SYSTEM 信息、头字段统计、以及全部目录条目（<see cref="ChmEntry"/>，含短内容指纹）。
	///
	/// 对外入口 <see cref="Parse(byte[], out string)"/> 不抛异常：失败返回 null 并回填 error，
	/// 视图按「无法解析」提示处理。
	/// </summary>
	internal static class ChmParser
	{
		/// <summary>ITSF V2 / V3 头长度。</summary>
		private const int ItsfV2Len = 0x58;

		private const int ItsfV3Len = 0x60;

		/// <summary>ITSP V1 头长度。</summary>
		private const int ItspV1Len = 0x54;

		/// <summary>PMGL / PMGI 头长度。</summary>
		private const int PmglLen = 0x14;

		private const int PmgiLen = 0x08;

		/// <summary>LZXC 重置表 V1 长度。</summary>
		private const int LzxcResetTableV1Len = 0x28;

		/// <summary>LZXC 控制块最小 / V2 长度。</summary>
		private const int LzxcMinLen = 0x18;

		private const int LzxcV2Len = 0x1C;

		/// <summary>路径串长度上限（与 CHMLib 的 CHM_MAX_PATHLEN 同口径）。</summary>
		private const int MaxPathLen = 512;

		/// <summary>单条目内容指纹上限：超过则不参与内容级比对（仅比长度）。</summary>
		private const long MaxHashEntryBytes = 4L * 1024 * 1024;

		/// <summary>内容指纹总量预算：累计超过即停止继续算指纹。</summary>
		private const long MaxHashTotalBytes = 64L * 1024 * 1024;

		/// <summary>解压内容流上限：超过则不尝试解压（内容级指纹退化为无）。</summary>
		private const long MaxDecompressBytes = 64L * 1024 * 1024;

		/// <summary>目录块遍历页数上限（防环 / 防臆造头造成的死循环）。</summary>
		private const int MaxDirectoryPages = 1 << 20;

		// ---- MSCompressed 三件套 / NameList 的固定路径（元数据对象无前导 '/'） ----

		private const string NameListPath = "::DataSpace/NameList";

		private const string MsContentPath = "::DataSpace/Storage/MSCompressed/Content";

		private const string MsControlPath = "::DataSpace/Storage/MSCompressed/ControlData";

		private const string MsResetTablePath = "::DataSpace/Storage/MSCompressed/Transform/{7FC28940-9D31-11D0-9B27-00A0C91E9C7C}/InstanceData/ResetTable";

		/// <summary>成功返回文档，失败返回 null 并回填 error（error = 异常类型名 + 消息）。</summary>
		internal static ChmDocument Parse(byte[] data, out string error)
		{
			error = null;
			try
			{
				return ParseCore(data);
			}
			catch (Exception ex)
			{
				error = ex.GetType().Name + ": " + ex.Message;
				return null;
			}
		}

		// ---- 头部 ----

		private static ChmDocument ParseCore(byte[] data)
		{
			if (data == null || data.Length < ItsfV3Len)
			{
				throw new InvalidDataException("not a valid CHM: empty or too small");
			}
			if (!(data[0] == (byte)'I' && data[1] == (byte)'T' && data[2] == (byte)'S' && data[3] == (byte)'F'))
			{
				throw new InvalidDataException("not a valid CHM: missing ITSF signature");
			}

			int itsfVersion = ReadI32(data, 4);
			byte[] itsfHeader = new byte[ItsfV3Len];
			Array.Copy(data, 0, itsfHeader, 0, ItsfV3Len);
			if (!ParseItsf(itsfHeader, out int version, out uint lastModified, out uint itsfLangId, out long dirOffset, out long dirLen, out long dataOffset))
			{
				throw new InvalidDataException("not a valid CHM: bad ITSF header");
			}

			if (!ValidRange(data, dirOffset, ItspV1Len))
			{
				throw new InvalidDataException("not a valid CHM: ITSP header out of range");
			}
			int dirOff = (int)dirOffset;
			if (!(data[dirOff] == (byte)'I' && data[dirOff + 1] == (byte)'T' && data[dirOff + 2] == (byte)'S' && data[dirOff + 3] == (byte)'P'))
			{
				throw new InvalidDataException("not a valid CHM: missing ITSP signature");
			}
			int itspHeaderLen = ReadI32(data, dirOff + 8);
			int blockLen = (int)ReadU32(data, dirOff + 0x10);
			int indexHead = ReadI32(data, dirOff + 0x20);
			int indexRoot = ReadI32(data, dirOff + 0x1C);
			uint itspLangId = ReadU32(data, dirOff + 0x30);
			if (indexRoot <= -1)
			{
				indexRoot = indexHead;
			}
			if (indexHead < 0 || blockLen <= PmglLen || blockLen > 1 << 24)
			{
				throw new InvalidDataException("not a valid CHM: bad ITSP directory block length");
			}

			// 目录块区从 ITSP 头之后开始（与 chm_open 一致）。
			long dirBlockBase = dirOffset + itspHeaderLen;
			long dirBlockLen = dirLen - itspHeaderLen;

			List<ChmEntry> entries = EnumerateEntries(data, dirBlockBase, blockLen, indexHead);
			if (entries.Count == 0)
			{
				throw new InvalidDataException("not a valid CHM: no directory entries");
			}
			Dictionary<string, ChmEntry> byPath = new Dictionary<string, ChmEntry>(StringComparer.OrdinalIgnoreCase);
			foreach (ChmEntry entry in entries)
			{
				if (!string.IsNullOrEmpty(entry.Path) && !byPath.ContainsKey(entry.Path))
				{
					byPath.Add(entry.Path, entry);
				}
			}

			// 压缩存储判定：#NameList 给出 MSCompressed 的存储号；三件套不全都视为无压缩。
			int compressedSpace = -1;
			byPath.TryGetValue(MsContentPath, out ChmEntry msContent);
			byPath.TryGetValue(MsControlPath, out ChmEntry msControl);
			byPath.TryGetValue(MsResetTablePath, out ChmEntry msReset);
			if (msContent != null && msControl != null && msReset != null)
			{
				compressedSpace = FindMsCompressedSpace(data, dataOffset, byPath);
				if (compressedSpace < 0)
				{
					// 兜底：绝大多数 CHM 的 NameList 顺序是 [未压缩, MSCompressed]，即压缩存储号为 1。
					compressedSpace = 1;
				}
			}

			ChmDocument document = new ChmDocument();
			document.Entries = entries;
			foreach (ChmEntry entry in entries)
			{
				if (entry.IsDirectory)
				{
					document.DirectoryCount++;
				}
				else
				{
					document.FileCount++;
					document.TotalBytes += entry.Length;
				}
			}

			// #SYSTEM：标题 / 默认主题 / 目录与索引文件（解压前提下的内容指纹不参与本步）。
			byte[] systemBytes = GetEntryBytes(byPath, "#SYSTEM", data, dataOffset, compressedSpace, null);
			Dictionary<int, byte[]> systemRecords = ParseSystemRecords(systemBytes);
			document.Title = systemRecords.TryGetValue(3, out byte[] titleBytes) ? DecodeText(titleBytes) : null;
			string defaultTopic = systemRecords.TryGetValue(2, out byte[] topicBytes) ? DecodeText(topicBytes) : null;
			string contentsFile = systemRecords.TryGetValue(0, out byte[] tocBytes) ? DecodeText(tocBytes) : null;
			string indexFile = systemRecords.TryGetValue(1, out byte[] idxBytes) ? DecodeText(idxBytes) : null;

			// 内容流解压（仅在确有压缩存储且预算内时）：供内容级指纹使用。
			byte[] decompressed = null;
			long windowSize = 0L;
			long resetInterval = 0L;
			if (compressedSpace >= 0 && byPath.TryGetValue(MsContentPath, out ChmEntry contentEntry))
			{
				decompressed = TryDecompress(data, dataOffset, contentEntry, msReset, msControl, out windowSize, out resetInterval);
			}

			ComputeHashes(data, dataOffset, entries, compressedSpace, decompressed);

			// 预览所需的原始数据引用（「预览」模式据此按路径取默认主题页与其内联资源）。
			document.DefaultTopic = defaultTopic;
			document.Raw = data;
			document.DataOffset = dataOffset;
			document.CompressedSpace = compressedSpace;
			document.Decompressed = decompressed;
			document.ByPath = byPath;

			BuildRows(document, version, lastModified, itsfLangId, itspLangId, blockLen, defaultTopic, contentsFile, indexFile, compressedSpace >= 0, windowSize, resetInterval, decompressed != null);
			return document;
		}

		/// <summary>解析 ITSF 头（V2 无 data_offset，按 dir_offset+dir_len 推）。</summary>
		private static bool ParseItsf(byte[] header, out int version, out uint lastModified, out uint langId, out long dirOffset, out long dirLen, out long dataOffset)
		{
			version = ReadI32(header, 4);
			int headerLen = ReadI32(header, 8);
			lastModified = ReadU32(header, 0x10);
			langId = ReadU32(header, 0x14);
			dirOffset = (long)ReadU64(header, 0x48);
			dirLen = (long)ReadU64(header, 0x50);
			if (version == 3)
			{
				if (headerLen < ItsfV3Len)
				{
					dataOffset = 0L;
					return false;
				}
				dataOffset = (long)ReadU64(header, 0x58);
			}
			else if (version == 2)
			{
				if (headerLen < ItsfV2Len)
				{
					dataOffset = 0L;
					return false;
				}
				dataOffset = dirOffset + dirLen;
			}
			else
			{
				dataOffset = 0L;
				return false;
			}
			return dirOffset > 0 && dirLen > 0;
		}

		// ---- 目录块遍历 ----

		/// <summary>从 index_head 起沿 block_next 串链表遍历全部 PMGL 叶子块，解析出所有条目。</summary>
		private static List<ChmEntry> EnumerateEntries(byte[] data, long dirBlockBase, int blockLen, int indexHead)
		{
			List<ChmEntry> entries = new List<ChmEntry>();
			HashSet<int> visited = new HashSet<int>();
			int page = indexHead;
			int guard = 0;
			while (page >= 0 && guard++ < MaxDirectoryPages && visited.Add(page))
			{
				long pageOff = dirBlockBase + (long)page * blockLen;
				if (!ValidRange(data, pageOff, blockLen))
				{
					break;
				}
				if (!(data[pageOff] == (byte)'P' && data[pageOff + 1] == (byte)'M' && data[pageOff + 2] == (byte)'G' && data[pageOff + 3] == (byte)'L'))
				{
					break;
				}
				uint freeSpace = ReadU32(data, (int)pageOff + 4);
				int blockNext = ReadI32(data, (int)pageOff + 0x10);
				int pos = (int)pageOff + PmglLen;
				long endL = pageOff + blockLen - freeSpace;
				if (endL < pos || endL > pageOff + blockLen)
				{
					break;
				}
				int end = (int)endL;
				while (pos < end)
				{
					ChmEntry entry = ParsePmglEntry(data, ref pos, end);
					if (entry == null || string.IsNullOrEmpty(entry.Path))
					{
						break;
					}
					entries.Add(entry);
				}
				page = blockNext;
			}
			return entries;
		}

		/// <summary>解析一个 PMGL 条目：cword 路径长 + 路径 + cword space + cword start + cword length。</summary>
		private static ChmEntry ParsePmglEntry(byte[] data, ref int pos, int end)
		{
			ulong strLen = ParseCWord(data, ref pos, end);
			if (strLen == 0 || strLen > MaxPathLen || pos + (int)strLen > end)
			{
				return null;
			}
			string path = Encoding.UTF8.GetString(data, pos, (int)strLen);
			pos += (int)strLen;
			if (pos >= end)
			{
				return null;
			}
			int space = (int)ParseCWord(data, ref pos, end);
			long start = (long)ParseCWord(data, ref pos, end);
			long length = (long)ParseCWord(data, ref pos, end);
			return new ChmEntry
			{
				Path = path,
				Space = space,
				Start = start,
				Length = length
			};
		}

		/// <summary>CHM 的变长整数（每字节 7 位，高位置 1 表示续读；与 _chm_parse_cword 一致）。</summary>
		private static ulong ParseCWord(byte[] data, ref int pos, int end)
		{
			ulong accum = 0UL;
			byte temp = 0;
			do
			{
				if (pos >= end)
				{
					return accum;
				}
				temp = data[pos++];
				if (temp >= 0x80)
				{
					accum = (accum << 7) + (ulong)(temp & 0x7F);
				}
			}
			while (temp >= 0x80);
			return (accum << 7) + temp;
		}

		// ---- NameList / #SYSTEM ----

		/// <summary>从 ::DataSpace/NameList 找出 "MSCompressed" 的存储号；找不到返回 -1。</summary>
		private static int FindMsCompressedSpace(byte[] data, long dataOffset, Dictionary<string, ChmEntry> byPath)
		{
			if (!byPath.TryGetValue(NameListPath, out ChmEntry nameList))
			{
				return -1;
			}
			byte[] bytes = ReadRaw(data, dataOffset, nameList);
			if (bytes == null || bytes.Length < 4)
			{
				return -1;
			}
			int pos = 0;
			int count = ReadU16(bytes, pos);
			pos += 2;
			for (int i = 0; i < count; i++)
			{
				if (pos + 2 > bytes.Length)
				{
					break;
				}
				int nameChars = ReadU16(bytes, pos);
				pos += 2;
				int nameBytes = nameChars * 2;
				if (nameBytes < 0 || pos + nameBytes > bytes.Length)
				{
					break;
				}
				string name = Encoding.Unicode.GetString(bytes, pos, nameBytes);
				pos += nameBytes;
				pos += 2; // 尾随 WORD（通常为 0）
				if (string.Equals(name, "MSCompressed", StringComparison.Ordinal))
				{
					return i;
				}
			}
			return -1;
		}

		/// <summary>把 <c>/#SYSTEM</c> 解析成「记录号 → 原始字节」。
		/// 每条记录为 u16 code + u16 length + data[length]。</summary>
		private static Dictionary<int, byte[]> ParseSystemRecords(byte[] bytes)
		{
			Dictionary<int, byte[]> records = new Dictionary<int, byte[]>();
			if (bytes == null)
			{
				return records;
			}
			int pos = 0;
			while (pos + 4 <= bytes.Length)
			{
				int code = ReadU16(bytes, pos);
				int len = ReadU16(bytes, pos + 2);
				pos += 4;
				if (len < 0 || pos + len > bytes.Length)
				{
					break;
				}
				byte[] value = new byte[len];
				Array.Copy(bytes, pos, value, 0, len);
				pos += len;
				if (!records.ContainsKey(code))
				{
					records.Add(code, value);
				}
			}
			return records;
		}

		/// <summary>#SYSTEM 文本解码：容 BOM / UTF-16LE（奇数字节多为 0），否则 UTF-8；去掉尾部 NUL。</summary>
		private static string DecodeText(byte[] bytes)
		{
			if (bytes == null || bytes.Length == 0)
			{
				return null;
			}
			string text;
			if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
			{
				text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
			}
			else
			{
				int zeros = 0;
				int pairs = 0;
				for (int i = 1; i < bytes.Length; i += 2)
				{
					pairs++;
					if (bytes[i] == 0)
					{
						zeros++;
					}
				}
				text = (pairs > 0 && zeros * 2 >= pairs) ? Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes);
			}
			int end = text.Length;
			while (end > 0 && text[end - 1] == '\0')
			{
				end--;
			}
			text = text.Substring(0, end).Trim();
			return text.Length == 0 ? null : text;
		}

		// ---- 内容读取与解压 ----

		/// <summary>按路径取条目原始（已解压）字节：仅用于 #SYSTEM / NameList 等小对象。</summary>
		private static byte[] GetEntryBytes(Dictionary<string, ChmEntry> byPath, string path, byte[] data, long dataOffset, int compressedSpace, byte[] decompressed)
		{
			if (!byPath.TryGetValue(path, out ChmEntry entry) && !byPath.TryGetValue("/" + path, out entry))
			{
				return null;
			}
			if (compressedSpace >= 0 && entry.Space == compressedSpace && decompressed != null)
			{
				return SliceDecompressed(decompressed, entry);
			}
			return ReadRaw(data, dataOffset, entry);
		}

		/// <summary>
		/// 按内部路径取条目原始（已解压）字节——供「预览」模式组装默认主题页与其内联资源。
		/// 查到目录 / 查不到返回 null。
		/// </summary>
		internal static byte[] GetEntryBytes(ChmDocument document, string path)
		{
			ChmEntry entry = FindEntry(document, path);
			if (entry == null || entry.IsDirectory)
			{
				return null;
			}
			if (document.CompressedSpace >= 0 && entry.Space == document.CompressedSpace && document.Decompressed != null)
			{
				return SliceDecompressed(document.Decompressed, entry);
			}
			return ReadRaw(document.Raw, document.DataOffset, entry);
		}

		/// <summary>按内部路径在 ByPath 里取条目：不区分大小写，容错前导 '/' 与 '\' 分隔符；
		/// 查不到返回 null。</summary>
		internal static ChmEntry FindEntry(ChmDocument document, string path)
		{
			Dictionary<string, ChmEntry> byPath = document?.ByPath;
			if (byPath == null || string.IsNullOrEmpty(path))
			{
				return null;
			}
			string normalized = path.Replace('\\', '/');
			if (byPath.TryGetValue(normalized, out ChmEntry entry))
			{
				return entry;
			}
			if (normalized.Length == 0)
			{
				return null;
			}
			if (normalized[0] != '/')
			{
				if (byPath.TryGetValue("/" + normalized, out entry))
				{
					return entry;
				}
			}
			else if (byPath.TryGetValue(normalized.Substring(1), out entry))
			{
				return entry;
			}
			return null;
		}

		/// <summary>未压缩对象直读（data_offset + start，长度 length）；越界返回 null。</summary>
		private static byte[] ReadRaw(byte[] data, long dataOffset, ChmEntry entry)
		{
			long start = dataOffset + entry.Start;
			if (entry.Length < 0 || entry.Length > int.MaxValue || !ValidRange(data, start, (int)entry.Length))
			{
				return null;
			}
			byte[] bytes = new byte[(int)entry.Length];
			Array.Copy(data, start, bytes, 0, (int)entry.Length);
			return bytes;
		}

		private static byte[] SliceDecompressed(byte[] decompressed, ChmEntry entry)
		{
			if (entry.Start < 0 || entry.Length < 0 || entry.Length > int.MaxValue)
			{
				return null;
			}
			long start = entry.Start;
			if (start + entry.Length > decompressed.Length)
			{
				return null;
			}
			byte[] bytes = new byte[(int)entry.Length];
			Array.Copy(decompressed, start, bytes, 0, (int)entry.Length);
			return bytes;
		}

		/// <summary>
		/// 解压整个 MSCompressed 内容流：读重置表 / 控制块 → 逐块 LZX 解压（重置区间边界重置解码器）
		/// → 顺序拼成未压缩内容。任何一步失败返回 null（内容级指纹退化为「无」）。
		/// </summary>
		private static byte[] TryDecompress(byte[] data, long dataOffset, ChmEntry content, ChmEntry resetEntry, ChmEntry controlEntry, out long windowSize, out long resetInterval)
		{
			windowSize = 0L;
			resetInterval = 0L;
			try
			{
				byte[] resetBytes = ReadRaw(data, dataOffset, resetEntry);
				byte[] controlBytes = ReadRaw(data, dataOffset, controlEntry);
				byte[] contentBytes = ReadRaw(data, dataOffset, content);
				if (resetBytes == null || controlBytes == null || contentBytes == null)
				{
					return null;
				}
				if (resetBytes.Length < LzxcResetTableV1Len || controlBytes.Length < LzxcMinLen)
				{
					return null;
				}

				// 重置表：version(u32)=2, block_count, unknown, table_offset, uncompressed_len(u64), compressed_len(u64), block_len(u64)。
				uint resetVersion = ReadU32(resetBytes, 0);
				uint blockCount = ReadU32(resetBytes, 4);
				uint tableOffset = ReadU32(resetBytes, 0x0C);
				ulong uncompressedLen = ReadU64(resetBytes, 0x10);
				ulong compressedLen = ReadU64(resetBytes, 0x18);
				ulong blockLength = ReadU64(resetBytes, 0x20);
				if (resetVersion != 2 || blockCount == 0 || blockLength == 0 || blockLength > int.MaxValue)
				{
					return null;
				}

				// 控制块：size, "LZXC", version, resetInterval, windowSize, windowsPerReset, [unknown]。
				if (!(controlBytes[4] == (byte)'L' && controlBytes[5] == (byte)'Z' && controlBytes[6] == (byte)'X' && controlBytes[7] == (byte)'C'))
				{
					return null;
				}
				uint controlVersion = ReadU32(controlBytes, 8);
				uint resetIntervalRaw = ReadU32(controlBytes, 0x0C);
				uint windowSizeRaw = ReadU32(controlBytes, 0x10);
				uint windowsPerReset = ReadU32(controlBytes, 0x14);
				if (controlVersion == 2)
				{
					resetIntervalRaw *= 0x8000;
					windowSizeRaw *= 0x8000;
				}
				if (windowSizeRaw == 0 || resetIntervalRaw == 0 || (resetIntervalRaw % (windowSizeRaw / 2)) != 0)
				{
					return null;
				}
				windowSize = windowSizeRaw;
				resetInterval = resetIntervalRaw;

				int windowBits = Ffs(windowSizeRaw) - 1;
				if (windowBits < 15 || windowBits > 21)
				{
					return null;
				}
				uint resetBlkCount = resetIntervalRaw / (windowSizeRaw / 2) * windowsPerReset;
				if (resetBlkCount == 0)
				{
					return null;
				}

				int outTotal = (int)Math.Min(uncompressedLen, (ulong)MaxDecompressBytes);
				byte[] output = new byte[outTotal];
				byte[] paddedContent = new byte[contentBytes.Length + 16];
				Array.Copy(contentBytes, paddedContent, contentBytes.Length);

				LzxDecoder decoder = new LzxDecoder(windowBits);
				for (uint block = 0; block < blockCount; block++)
				{
					long outStart = (long)block * (long)blockLength;
					if (outStart >= outTotal)
					{
						break;
					}
					if ((block % resetBlkCount) == 0)
					{
						decoder.Reset();
					}
					if (!GetCmpBlockBounds(resetBytes, block, blockCount, tableOffset, compressedLen, out long cmpStartRel, out long cmpLen))
					{
						return null;
					}
					if (cmpStartRel < 0 || cmpLen <= 0 || cmpStartRel + cmpLen > contentBytes.Length)
					{
						return null;
					}
					long remain = outTotal - outStart;
					int outLen = (int)Math.Min((long)blockLength, remain);
					if (decoder.Decompress(paddedContent, (int)cmpStartRel, (int)cmpLen, output, (int)outStart, outLen) != 0)
					{
						return null;
					}
				}
				return output;
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>由重置表算某块的压缩区间 [start, start+len)（相对内容流起点）。</summary>
		private static bool GetCmpBlockBounds(byte[] resetBytes, uint block, uint blockCount, uint tableOffset, ulong compressedLen, out long start, out long len)
		{
			start = 0L;
			len = 0L;
			long entryPos = (long)tableOffset + (long)block * 8L;
			if (entryPos < 0 || entryPos + 8 > resetBytes.Length)
			{
				return false;
			}
			ulong startRel = ReadU64(resetBytes, (int)entryPos);
			ulong endRel;
			if (block < blockCount - 1)
			{
				if (entryPos + 16 > resetBytes.Length)
				{
					return false;
				}
				endRel = ReadU64(resetBytes, (int)entryPos + 8);
			}
			else
			{
				endRel = compressedLen;
			}
			start = (long)startRel;
			len = (long)endRel - (long)startRel;
			return len > 0;
		}

		// ---- 内容指纹 ----

		/// <summary>给「非目录、且可读、且在预算内」的条目算 SHA-256 前 8 字节指纹；
		/// 不可读或超限的条目 Hash 保持 null（比对时自动退化为仅比长度）。</summary>
		private static void ComputeHashes(byte[] data, long dataOffset, List<ChmEntry> entries, int compressedSpace, byte[] decompressed)
		{
			long budget = MaxHashTotalBytes;
			using (SHA256 sha = SHA256.Create())
			{
				foreach (ChmEntry entry in entries)
				{
					if (entry.IsDirectory || entry.Length <= 0 || entry.Length > MaxHashEntryBytes)
					{
						continue;
					}
					if (budget - entry.Length < 0)
					{
						break;
					}
					byte[] hash;
					if (compressedSpace >= 0 && entry.Space == compressedSpace)
					{
						if (decompressed == null || entry.Start + entry.Length > decompressed.Length)
						{
							continue;
						}
						hash = sha.ComputeHash(decompressed, (int)entry.Start, (int)entry.Length);
					}
					else
					{
						long start = dataOffset + entry.Start;
						if (!ValidRange(data, start, (int)entry.Length))
						{
							continue;
						}
						hash = sha.ComputeHash(data, (int)start, (int)entry.Length);
					}
					budget -= entry.Length;
					entry.Hash = ToShortHex(hash);
				}
			}
		}

		private static string ToShortHex(byte[] hash)
		{
			int take = Math.Min(8, hash.Length);
			StringBuilder builder = new StringBuilder(take * 2);
			for (int i = 0; i < take; i++)
			{
				builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
			}
			return builder.ToString();
		}

		// ---- 元数据行 ----

		private static void BuildRows(ChmDocument document, int version, uint lastModified, uint itsfLangId, uint itspLangId, int blockLen, string defaultTopic, string contentsFile, string indexFile, bool compressed, long windowSize, long resetInterval, bool decompressed)
		{
			List<ChmRow> rows = document.Rows;
			AddValue(rows, "title", document.Title);
			AddValue(rows, "default topic", defaultTopic);
			AddValue(rows, "contents file", contentsFile);
			AddValue(rows, "index file", indexFile);
			AddValue(rows, "language", FormatLanguage(itsfLangId));
			if (itspLangId != itsfLangId)
			{
				AddValue(rows, "directory language", FormatLanguage(itspLangId));
			}
			rows.Add(new ChmRow { Path = "help format version", Value = version.ToString(CultureInfo.InvariantCulture) });
			AddValue(rows, "last modified", FormatTimestamp(lastModified));
			rows.Add(new ChmRow { Path = "compressed", Value = (compressed ? "yes" : "no") });
			if (compressed)
			{
				AddValue(rows, "window size", windowSize > 0 ? PluginSizeFormat.ReadableFileSize(windowSize, false) : null);
				AddValue(rows, "reset interval", resetInterval > 0 ? PluginSizeFormat.ReadableFileSize(resetInterval, false) : null);
				AddValue(rows, "content stream", decompressed ? "decompressed" : "unavailable");
			}
			rows.Add(new ChmRow { Path = "directory block size", Value = PluginSizeFormat.ReadableFileSize(blockLen, false) });
			rows.Add(new ChmRow { Path = "files", Value = document.FileCount.ToString(CultureInfo.InvariantCulture) });
			rows.Add(new ChmRow { Path = "directories", Value = document.DirectoryCount.ToString(CultureInfo.InvariantCulture) });
			rows.Add(new ChmRow { Path = "total bytes", Value = document.TotalBytes.ToString("N0", CultureInfo.InvariantCulture) });
		}

		private static void AddValue(List<ChmRow> rows, string path, string value)
		{
			if (!string.IsNullOrEmpty(value))
			{
				rows.Add(new ChmRow { Path = path, Value = value });
			}
		}

		/// <summary>语言 ID（Windows LCID）→ "0xLLLL · culture"；解不出 culture 时只给十六进制。</summary>
		private static string FormatLanguage(uint langId)
		{
			string hex = "0x" + langId.ToString("X4", CultureInfo.InvariantCulture);
			if (langId == 0)
			{
				return hex;
			}
			try
			{
				CultureInfo culture = new CultureInfo((int)langId);
				if (!string.IsNullOrEmpty(culture.Name))
				{
					return hex + " · " + culture.Name;
				}
			}
			catch (Exception)
			{
				// 未知 LCID：仅显示十六进制。
			}
			return hex;
		}

		/// <summary>ITSF 时间戳（Unix 秒）→ UTC 日期时间；0 或越界返回 null。</summary>
		private static string FormatTimestamp(uint timestamp)
		{
			if (timestamp == 0)
			{
				return null;
			}
			try
			{
				return DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
			}
			catch (Exception)
			{
				return null;
			}
		}

		// ---- 小工具 ----

		private static int Ffs(uint value)
		{
			if (value == 0)
			{
				return 0;
			}
			int bit = 1;
			while ((value & 1u) == 0)
			{
				value >>= 1;
				bit++;
			}
			return bit;
		}

		private static bool ValidRange(byte[] data, long offset, int length)
		{
			return offset >= 0 && length >= 0 && offset + length <= data.Length;
		}

		private static int ReadU16(byte[] data, int offset)
		{
			return data[offset] | (data[offset + 1] << 8);
		}

		private static uint ReadU32(byte[] data, int offset)
		{
			return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
		}

		private static int ReadI32(byte[] data, int offset)
		{
			return (int)ReadU32(data, offset);
		}

		private static ulong ReadU64(byte[] data, int offset)
		{
			return ReadU32(data, offset) | ((ulong)ReadU32(data, offset + 4) << 32);
		}
	}
}