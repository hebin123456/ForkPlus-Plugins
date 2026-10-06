using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ForkPlus.Plugins.Font
{
	/// <summary>字体解析过程中的边界 / 结构错误（解析入口一律 try/catch，绝不抛到宿主）。</summary>
	internal sealed class FontParseException : Exception
	{
		public FontParseException(string message)
			: base(message)
		{
		}
	}

	/// <summary>
	/// 纯自研 sfnt / WOFF 解析器（零第三方依赖）。支持：
	/// TrueType（0x00010000 / "true" / "typ1"）、OpenType CFF（"OTTO"）、
	/// 集合（"ttcf"）、WOFF（"wOFF"，DeflateStream 解压各表）。
	///
	/// 解析的表：head / name / OS/2 / maxp / hhea / cmap（format 4 与 12）/ kern / GPOS / GSUB。
	/// 字体是不可信输入：所有偏移与长度都做边界校验，越界即抛 <see cref="FontParseException"/>；
	/// 单表损坏只降级该表（记入 <see cref="FontFace.Warnings"/>），不因一表损坏丢掉整套字体。
	/// </summary>
	internal static class FontParser
	{
		/// <summary>cmap 码位集合上限（CJK 字体可上万，超过即截断并标记）。</summary>
		private const int MaxCodepoints = 65536;

		private const int MaxTables = 4096;

		/// <summary>1904-01-01（sfnt head 表 LONGDATETIME 的起点）。</summary>
		private static readonly DateTime MacEpoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		/// <summary>入口：按魔数判定格式并解析。任何异常都降级为带错误分类的模型。</summary>
		public static FontModel Parse(string path, byte[] bytes)
		{
			if (bytes == null || bytes.Length == 0)
			{
				return FontModel.Failure(path, FontError.Empty);
			}
			try
			{
				Reader reader = new Reader(bytes);
				long magic = reader.U32(0);
				switch (magic)
				{
					case 0x00010000L: // TrueType 轮廓
					case 0x74727565L: // "true"
					case 0x74797031L: // "typ1"
						return ParseSfnt(path, reader, FontFormat.TrueType, 0L);
					case 0x4F54544FL: // "OTTO"
						return ParseSfnt(path, reader, FontFormat.OpenTypeCff, 0L);
					case 0x74746366L: // "ttcf"
						return ParseCollection(path, reader);
					case 0x774F4646L: // "wOFF"
						return ParseWoff(path, reader);
					case 0x774F4632L: // "wOF2"
						return FontModel.Failure(path, FontError.Unsupported, "WOFF2");
					default:
						return FontModel.Failure(path, FontError.BadMagic, null);
				}
			}
			catch (FontParseException ex)
			{
				return FontModel.Failure(path, FontError.Failed, ex.Message);
			}
			catch (Exception ex)
			{
				return FontModel.Failure(path, FontError.Failed, ex.Message);
			}
		}

		// ---- 容器 ----

		private static FontModel ParseSfnt(string path, Reader reader, FontFormat format, long faceOffset)
		{
			FontModel model = new FontModel
			{
				Path = path,
				Format = format
			};
			Dictionary<string, byte[]> tables = ReadSfntTables(reader, faceOffset, null);
			if (tables.Count == 0)
			{
				return FontModel.Failure(path, FontError.Failed, "no tables");
			}
			model.Faces.Add(ParseFace(0, tables));
			return model;
		}

		private static FontModel ParseCollection(string path, Reader reader)
		{
			FontModel model = new FontModel
			{
				Path = path,
				Format = FontFormat.TrueTypeCollection,
				IsCollection = true
			};
			reader.Ensure(0L, 12L);
			long count = reader.U32(8);
			if (count <= 0 || count > MaxTables)
			{
				throw new FontParseException("bad collection face count");
			}
			reader.Ensure(12L, count * 4L);
			List<string> warnings = new List<string>();
			for (int i = 0; i < (int)count; i++)
			{
				long faceOffset = reader.U32(12L + (long)i * 4L);
				try
				{
					reader.Ensure(faceOffset, 12L);
					Dictionary<string, byte[]> tables = ReadSfntTables(reader, faceOffset, warnings);
					if (tables.Count == 0)
					{
						continue;
					}
					FontFace face = ParseFace(i, tables);
					face.Warnings.AddRange(warnings);
					warnings.Clear();
					model.Faces.Add(face);
				}
				catch (FontParseException)
				{
					// 单套损坏（偏移越界）跳过该套，继续解析其余套。
					warnings.Clear();
				}
			}
			if (model.Faces.Count == 0)
			{
				return FontModel.Failure(path, FontError.Failed, "no faces");
			}
			return model;
		}

		private static FontModel ParseWoff(string path, Reader reader)
		{
			FontModel model = new FontModel
			{
				Path = path,
				Format = FontFormat.Woff
			};
			reader.Ensure(0L, 44L);
			int count = reader.U16(12);
			if (count <= 0 || count > MaxTables)
			{
				throw new FontParseException("bad WOFF table count");
			}
			reader.Ensure(44L, (long)count * 20L);
			Dictionary<string, byte[]> tables = new Dictionary<string, byte[]>(StringComparer.Ordinal);
			for (int i = 0; i < count; i++)
			{
				long record = 44L + (long)i * 20L;
				string tag = reader.Tag(record);
				long offset = reader.U32(record + 4L);
				long compLength = reader.U32(record + 8L);
				long origLength = reader.U32(record + 12L);
				try
				{
					// 偏移量与 compressed length 都做边界校验。
					reader.Ensure(offset, compLength);
					byte[] raw = reader.Slice(offset, (int)compLength);
					byte[] data = compLength == origLength ? raw : Inflate(raw, (int)origLength);
					if (!tables.ContainsKey(tag))
					{
						tables[tag] = data;
					}
				}
				catch (FontParseException)
				{
					// 单表越界：跳过该表。
				}
				catch (InvalidDataException)
				{
					// 单表解压失败：跳过该表。
				}
			}
			if (tables.Count == 0)
			{
				return FontModel.Failure(path, FontError.Failed, "no WOFF tables");
			}
			model.Faces.Add(ParseFace(0, tables));
			return model;
		}

		/// <summary>
		/// WOFF 表数据是 zlib 封装（2 字节头 + deflate 载荷 + 4 字节 adler32）；DeflateStream 只认
		/// 裸 deflate，这里按 zlib 头（CM=8 且校验通过）跳过 2 字节再解压，尾部 adler32 不影响
		/// （deflate 结束标记后即停止读取）。
		/// </summary>
		private static byte[] Inflate(byte[] raw, int expectedLength)
		{
			if (raw.Length == 0)
			{
				return raw;
			}
			int start = 0;
			if (raw.Length >= 2 && (raw[0] & 0x0F) == 0x08 && (raw[1] & 0x20) == 0 && ((raw[0] << 8) | raw[1]) % 31 == 0)
			{
				start = 2;
			}
			using (MemoryStream input = new MemoryStream(raw, start, raw.Length - start, false))
			using (DeflateStream deflate = new DeflateStream(input, CompressionMode.Decompress))
			using (MemoryStream output = expectedLength > 0 ? new MemoryStream(expectedLength) : new MemoryStream())
			{
				deflate.CopyTo(output);
				return output.ToArray();
			}
		}

		/// <summary>
		/// 读 sfnt 表目录（相对 <paramref name="faceOffset"/> 的表记录）；偏移与长度都校验，
		/// 单表越界只跳过该表并把原因记入 warnings。
		/// </summary>
		private static Dictionary<string, byte[]> ReadSfntTables(Reader reader, long faceOffset, List<string> warnings)
		{
			reader.Ensure(faceOffset, 12L);
			int count = reader.U16(faceOffset + 4L);
			if (count <= 0)
			{
				throw new FontParseException("empty table directory");
			}
			if (count > MaxTables)
			{
				throw new FontParseException("too many tables");
			}
			reader.Ensure(faceOffset + 12L, (long)count * 16L);
			Dictionary<string, byte[]> tables = new Dictionary<string, byte[]>(StringComparer.Ordinal);
			for (int i = 0; i < count; i++)
			{
				long record = faceOffset + 12L + (long)i * 16L;
				string tag = reader.Tag(record);
				long offset = reader.U32(record + 8L);
				long length = reader.U32(record + 12L);
				try
				{
					reader.Ensure(offset, length);
					byte[] data = reader.Slice(offset, (int)length);
					if (!tables.ContainsKey(tag))
					{
						tables[tag] = data;
					}
				}
				catch (FontParseException ex)
				{
					warnings?.Add(tag + ": " + ex.Message);
				}
			}
			return tables;
		}

		// ---- 表解析 ----

		private static FontFace ParseFace(int index, Dictionary<string, byte[]> tables)
		{
			FontFace face = new FontFace
			{
				Index = index
			};
			foreach (KeyValuePair<string, byte[]> entry in tables)
			{
				face.TableSizes[entry.Key] = entry.Value.Length;
			}
			TryParse(face, tables, "name", ParseName);
			TryParse(face, tables, "head", ParseHead);
			TryParse(face, tables, "OS/2", ParseOs2);
			TryParse(face, tables, "maxp", ParseMaxp);
			TryParse(face, tables, "hhea", ParseHhea);
			TryParse(face, tables, "cmap", ParseCmap);
			TryParse(face, tables, "GSUB", ParseGsub);
			face.DisplayName = BuildDisplayName(face);
			return face;
		}

		private static void TryParse(FontFace face, Dictionary<string, byte[]> tables, string tag, Action<FontFace, byte[]> parse)
		{
			if (!tables.TryGetValue(tag, out byte[] data))
			{
				return;
			}
			try
			{
				parse(face, data);
			}
			catch (FontParseException ex)
			{
				face.Warnings.Add(tag + ": " + ex.Message);
			}
		}

		private static void ParseName(FontFace face, byte[] data)
		{
			Reader r = new Reader(data);
			r.Ensure(0L, 6L);
			int count = r.U16(2);
			int stringOffset = r.U16(4);
			if (count <= 0 || count > MaxTables)
			{
				throw new FontParseException("bad name record count");
			}
			r.Ensure(6L, (long)count * 12L);
			Dictionary<int, string> unicode = new Dictionary<int, string>();
			Dictionary<int, string> legacy = new Dictionary<int, string>();
			for (int i = 0; i < count; i++)
			{
				long record = 6L + (long)i * 12L;
				int platformId = r.U16(record);
				int nameId = r.U16(record + 6L);
				int length = r.U16(record + 8L);
				int offset = r.U16(record + 10L);
				// nameID 0 版权 / 1 字体族 / 2 子族 / 3 唯一标识 / 4 全名 / 5 版本 / 6 PostScript 名 / 7 商标。
				if (nameId < 0 || nameId > 7)
				{
					continue;
				}
				long stringPointer = stringOffset + (long)offset;
				r.Ensure(stringPointer, length);
				string value = DecodeName(r, stringPointer, length, platformId);
				if (string.IsNullOrEmpty(value))
				{
					continue;
				}
				Dictionary<int, string> target = (platformId == 3 || platformId == 0) ? unicode : legacy;
				if (!target.ContainsKey(nameId))
				{
					target[nameId] = value;
				}
			}
			face.Copyright = Pick(unicode, legacy, 0);
			face.Family = Pick(unicode, legacy, 1);
			face.Subfamily = Pick(unicode, legacy, 2);
			face.UniqueId = Pick(unicode, legacy, 3);
			face.FullName = Pick(unicode, legacy, 4);
			face.Version = Pick(unicode, legacy, 5);
			face.PostScriptName = Pick(unicode, legacy, 6);
			face.Trademark = Pick(unicode, legacy, 7);
		}

		private static string Pick(Dictionary<int, string> unicode, Dictionary<int, string> legacy, int nameId)
		{
			if (unicode.TryGetValue(nameId, out string value))
			{
				return value;
			}
			return legacy.TryGetValue(nameId, out value) ? value : null;
		}

		private static string DecodeName(Reader r, long offset, int length, int platformId)
		{
			byte[] raw = r.Slice(offset, length);
			if (platformId == 3 || platformId == 0)
			{
				return DecodeUtf16Be(raw);
			}
			// Mac / 其它平台：常见为 MacRoman / ASCII，降级为 Latin-1 逐字节映射。
			StringBuilder builder = new StringBuilder(raw.Length);
			foreach (byte b in raw)
			{
				builder.Append((char)b);
			}
			return builder.ToString().Trim();
		}

		private static string DecodeUtf16Be(byte[] raw)
		{
			int pairs = raw.Length / 2;
			if (pairs == 0)
			{
				return string.Empty;
			}
			StringBuilder builder = new StringBuilder(pairs);
			for (int i = 0; i < pairs; i++)
			{
				char c = (char)((raw[i * 2] << 8) | raw[i * 2 + 1]);
				if (c != '\0')
				{
					builder.Append(c);
				}
			}
			return builder.ToString().Trim();
		}

		private static void ParseHead(FontFace face, byte[] data)
		{
			Reader r = new Reader(data);
			r.Ensure(0L, 54L);
			face.UnitsPerEm = r.U16(18);
			face.Created = ToDateTime(r.I64(20L));
			face.Modified = ToDateTime(r.I64(28L));
			face.MacStyle = r.U16(44);
		}

		private static DateTime? ToDateTime(long seconds)
		{
			try
			{
				DateTime value = MacEpoch.AddSeconds(seconds);
				return value.Year < 1904 || value.Year > 9999 ? (DateTime?)null : value;
			}
			catch (ArgumentOutOfRangeException)
			{
				return null;
			}
		}

		private static void ParseOs2(FontFace face, byte[] data)
		{
			Reader r = new Reader(data);
			r.Ensure(0L, 78L);
			face.WeightClass = r.U16(4);
			face.WidthClass = r.U16(6);
			face.Selection = r.U16(62);
			face.TypoAscender = r.I16(68);
			face.TypoDescender = r.I16(70);
			face.TypoLineGap = r.I16(72);
			face.WinAscent = r.U16(74);
			face.WinDescent = r.U16(76);
		}

		private static void ParseMaxp(FontFace face, byte[] data)
		{
			Reader r = new Reader(data);
			r.Ensure(0L, 6L);
			face.NumGlyphs = r.U16(4);
		}

		private static void ParseHhea(FontFace face, byte[] data)
		{
			Reader r = new Reader(data);
			r.Ensure(0L, 36L);
			face.HheaAscender = r.I16(4);
			face.HheaDescender = r.I16(6);
			face.HheaLineGap = r.I16(8);
		}

		private static void ParseCmap(FontFace face, byte[] data)
		{
			Reader r = new Reader(data);
			r.Ensure(0L, 4L);
			int count = r.U16(2);
			if (count <= 0 || count > 1024)
			{
				throw new FontParseException("bad cmap subtable count");
			}
			r.Ensure(4L, (long)count * 8L);
			bool parsed = false;
			for (int i = 0; i < count; i++)
			{
				long record = 4L + (long)i * 8L;
				long subOffset = r.U32(record + 4L);
				if (subOffset + 2L > data.Length)
				{
					continue;
				}
				int format = r.U16(subOffset);
				try
				{
					if (format == 4)
					{
						ParseCmapFormat4(face, r, subOffset);
						parsed = true;
					}
					else if (format == 12)
					{
						ParseCmapFormat12(face, r, subOffset);
						parsed = true;
					}
				}
				catch (FontParseException)
				{
					// 单个子表损坏不影响其它子表。
				}
				if (face.CodepointsTruncated)
				{
					break;
				}
			}
			face.CmapParsed = parsed;
		}

		private static void ParseCmapFormat4(FontFace face, Reader r, long start)
		{
			r.Ensure(start, 14L);
			int segCountX2 = r.U16(start + 6L);
			int segCount = segCountX2 / 2;
			if (segCount <= 0 || segCount > 32768)
			{
				throw new FontParseException("bad cmap format 4 segment count");
			}
			long endCodes = start + 14L;
			long startCodes = endCodes + segCountX2 + 2L;
			long idDeltas = startCodes + segCountX2;
			long idRangeOffsets = idDeltas + segCountX2;
			r.Ensure(idRangeOffsets, segCountX2);
			for (int i = 0; i < segCount; i++)
			{
				int end = r.U16(endCodes + (long)i * 2L);
				int segmentStart = r.U16(startCodes + (long)i * 2L);
				int delta = r.I16(idDeltas + (long)i * 2L);
				int rangeOffset = r.U16(idRangeOffsets + (long)i * 2L);
				if (segmentStart > end || segmentStart == 0xFFFF)
				{
					continue;
				}
				for (int c = segmentStart; c <= end; c++)
				{
					int glyph;
					if (rangeOffset == 0)
					{
						glyph = (c + delta) & 0xFFFF;
					}
					else
					{
						long address = idRangeOffsets + (long)i * 2L + rangeOffset + (long)(c - segmentStart) * 2L;
						if (address + 2L > r.Length)
						{
							continue;
						}
						glyph = r.U16(address);
						if (glyph != 0)
						{
							glyph = (glyph + delta) & 0xFFFF;
						}
					}
					if (glyph != 0)
					{
						AddCodepoint(face, c);
					}
					if (face.CodepointsTruncated)
					{
						return;
					}
				}
			}
		}

		private static void ParseCmapFormat12(FontFace face, Reader r, long start)
		{
			r.Ensure(start, 16L);
			long groups = r.U32(start + 12L);
			if (groups < 0 || groups > 1000000L)
			{
				throw new FontParseException("bad cmap format 12 group count");
			}
			r.Ensure(start + 16L, groups * 12L);
			for (int g = 0; g < (int)groups; g++)
			{
				long group = start + 16L + (long)g * 12L;
				long groupStart = r.U32(group);
				long groupEnd = r.U32(group + 4L);
				long startGlyph = r.U32(group + 8L);
				if (startGlyph == 0 || groupStart > groupEnd)
				{
					continue;
				}
				for (long c = groupStart; c <= groupEnd && c <= 0x10FFFFL; c++)
				{
					AddCodepoint(face, (int)c);
					if (face.CodepointsTruncated)
					{
						return;
					}
				}
			}
		}

		private static void AddCodepoint(FontFace face, int codepoint)
		{
			if (codepoint < 0 || codepoint > 0x10FFFF)
			{
				return;
			}
			if (face.Codepoints.Count >= MaxCodepoints && !face.Codepoints.Contains(codepoint))
			{
				face.CodepointsTruncated = true;
				return;
			}
			face.Codepoints.Add(codepoint);
		}

		private static void ParseGsub(FontFace face, byte[] data)
		{
			Reader r = new Reader(data);
			r.Ensure(0L, 10L);
			int featureListOffset = r.U16(6);
			if (featureListOffset <= 0 || featureListOffset >= data.Length)
			{
				return;
			}
			r.Ensure(featureListOffset, 2L);
			int featureCount = r.U16(featureListOffset);
			if (featureCount < 0 || featureCount > MaxTables)
			{
				throw new FontParseException("bad GSUB feature count");
			}
			r.Ensure(featureListOffset + 2L, (long)featureCount * 6L);
			for (int i = 0; i < featureCount; i++)
			{
				string tag = r.Tag(featureListOffset + 2L + (long)i * 6L);
				if (!face.GsubFeatures.Contains(tag))
				{
					face.GsubFeatures.Add(tag);
				}
			}
			face.GsubFeatures.Sort(StringComparer.Ordinal);
		}

		private static string BuildDisplayName(FontFace face)
		{
			string name = face.Family;
			if (!string.IsNullOrEmpty(face.Subfamily) && !string.Equals(face.Subfamily, "Regular", StringComparison.OrdinalIgnoreCase))
			{
				name = string.IsNullOrEmpty(name) ? face.Subfamily : name + " " + face.Subfamily;
			}
			if (string.IsNullOrEmpty(name))
			{
				name = face.FullName;
			}
			if (string.IsNullOrEmpty(name))
			{
				name = face.PostScriptName;
			}
			return name;
		}

		// ---- 字节读取器（全部访问先做边界校验） ----

		private sealed class Reader
		{
			private readonly byte[] _data;

			public Reader(byte[] data)
			{
				_data = data;
			}

			public int Length => _data.Length;

			/// <summary>校验 [offset, offset + length) 落在数据范围内；越界即抛。</summary>
			public void Ensure(long offset, long length)
			{
				if (offset < 0L || length < 0L || offset > _data.Length || offset + length > _data.Length)
				{
					throw new FontParseException("offset out of range");
				}
			}

			public int U16(long offset)
			{
				Ensure(offset, 2L);
				int index = (int)offset;
				return (_data[index] << 8) | _data[index + 1];
			}

			public short I16(long offset)
			{
				return (short)U16(offset);
			}

			public long U32(long offset)
			{
				Ensure(offset, 4L);
				int index = (int)offset;
				return ((long)_data[index] << 24) | ((long)_data[index + 1] << 16) | ((long)_data[index + 2] << 8) | _data[index + 3];
			}

			public long I64(long offset)
			{
				Ensure(offset, 8L);
				int index = (int)offset;
				long value = 0L;
				for (int i = 0; i < 8; i++)
				{
					value = (value << 8) | _data[index + i];
				}
				return value;
			}

			public string Tag(long offset)
			{
				Ensure(offset, 4L);
				int index = (int)offset;
				char[] chars = new char[4];
				for (int i = 0; i < 4; i++)
				{
					chars[i] = (char)_data[index + i];
				}
				return new string(chars);
			}

			public byte[] Slice(long offset, int length)
			{
				Ensure(offset, length);
				byte[] result = new byte[length];
				Buffer.BlockCopy(_data, (int)offset, result, 0, length);
				return result;
			}
		}
	}
}
