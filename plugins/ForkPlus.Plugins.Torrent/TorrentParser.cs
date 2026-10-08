using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForkPlus.Plugins.Torrent
{
	/// <summary>
	/// Torrent（bencode）解析：把 .torrent 字节流解码成一棵有序对象树
	/// （字节串 <see cref="TorrentString"/> / 整数 long / 列表 List&lt;object&gt; /
	/// 字典 List&lt;KeyValuePair&lt;string, object&gt;&gt;，字典按 key 出现序保序遍历），
	/// 再递归拍平成「键路径 → 展示值」的 <see cref="KvRow"/> 列表，并抽取 info 摘要
	/// （名称 / 分片数 / 总长度）。
	///
	/// bencode 四种值：整数 <c>i&lt;digits&gt;e</c>、字符串 <c>&lt;十进制长度&gt;:&lt;原始字节&gt;</c>、
	/// 列表 <c>l…e</c>、字典 <c>d…e</c>（key 是字符串，值任意）。长度越界 / 残缺输入抛带清晰
	/// 信息的异常，由 <see cref="Parse"/> 统一捕获回填 error（视图按「解析失败」提示处理）。
	///
	/// 展示值渲染：标量直接显示；&gt; 64 字节的原始字节串渲染为 “{N} bytes”；
	/// <c>info.pieces</c> 特殊渲染为 “{N} SHA-1 hashes”（N = 字节数 / 20）；
	/// <c>creation date</c> 等 unix 秒值渲染为本地时间字符串。
	/// </summary>
	internal static class TorrentParser
	{
		/// <summary>超过此字节数的字符串值不再展示文本，渲染为 “{N} bytes”。</summary>
		private const int LargeStringBytes = 64;

		/// <summary>SHA-1 哈希固定 20 字节；info.pieces 的分片数 = 字节数 / 20。</summary>
		private const int Sha1Bytes = 20;

		/// <summary>解析 .torrent 字节。成功返回数据模型，失败返回 null 并回填 error。</summary>
		internal static TorrentData Parse(byte[] data, out string error)
		{
			error = null;
			if (data == null)
			{
				error = "no data available";
				return null;
			}
			if (data.Length == 0)
			{
				error = "empty input";
				return null;
			}
			try
			{
				int position = 0;
				object root = DecodeValue(data, ref position);
				if (position != data.Length)
				{
					error = "trailing bytes after bencode value";
					return null;
				}
				List<KeyValuePair<string, object>> dictionary = root as List<KeyValuePair<string, object>>;
				if (dictionary == null)
				{
					error = "torrent root is not a dictionary";
					return null;
				}
				TorrentData result = new TorrentData();
				Flatten(dictionary, string.Empty, result.Rows);
				FillSummary(dictionary, result);
				return result;
			}
			catch (Exception ex)
			{
				error = ex.GetType().Name + ": " + ex.Message;
				return null;
			}
		}

		// ---- bencode 解码 ----

		private static object DecodeValue(byte[] data, ref int position)
		{
			if (position >= data.Length)
			{
				throw new FormatException("unexpected end of bencode input");
			}
			byte prefix = data[position];
			if (prefix == (byte)'i')
			{
				position++;
				return DecodeInteger(data, ref position);
			}
			if (prefix == (byte)'l')
			{
				position++;
				return DecodeList(data, ref position);
			}
			if (prefix == (byte)'d')
			{
				position++;
				return DecodeDictionary(data, ref position);
			}
			if (prefix >= (byte)'0' && prefix <= (byte)'9')
			{
				return DecodeString(data, ref position);
			}
			throw new FormatException("invalid bencode value at offset " + position.ToString(CultureInfo.InvariantCulture));
		}

		private static long DecodeInteger(byte[] data, ref int position)
		{
			int start = position;
			if (position < data.Length && data[position] == (byte)'-')
			{
				position++;
			}
			int digits = position;
			while (position < data.Length && data[position] >= (byte)'0' && data[position] <= (byte)'9')
			{
				position++;
			}
			if (position == digits || position >= data.Length || data[position] != (byte)'e')
			{
				throw new FormatException("malformed bencode integer at offset " + start.ToString(CultureInfo.InvariantCulture));
			}
			string text = Encoding.ASCII.GetString(data, start, position - start);
			position++;
			if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
			{
				throw new FormatException("bencode integer out of range at offset " + start.ToString(CultureInfo.InvariantCulture));
			}
			return value;
		}

		private static TorrentString DecodeString(byte[] data, ref int position)
		{
			int start = position;
			while (position < data.Length && data[position] >= (byte)'0' && data[position] <= (byte)'9')
			{
				position++;
			}
			if (position == start || position >= data.Length || data[position] != (byte)':')
			{
				throw new FormatException("malformed bencode string length at offset " + start.ToString(CultureInfo.InvariantCulture));
			}
			string lengthText = Encoding.ASCII.GetString(data, start, position - start);
			if (!int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out int length))
			{
				throw new FormatException("bencode string length out of range at offset " + start.ToString(CultureInfo.InvariantCulture));
			}
			position++;
			if ((long)position + length > data.Length)
			{
				throw new FormatException("bencode string length " + length.ToString(CultureInfo.InvariantCulture) + " exceeds input size");
			}
			byte[] bytes = new byte[length];
			Array.Copy(data, position, bytes, 0, length);
			position += length;
			return new TorrentString(bytes);
		}

		private static List<object> DecodeList(byte[] data, ref int position)
		{
			List<object> items = new List<object>();
			while (true)
			{
				if (position >= data.Length)
				{
					throw new FormatException("unterminated bencode list");
				}
				if (data[position] == (byte)'e')
				{
					position++;
					return items;
				}
				items.Add(DecodeValue(data, ref position));
			}
		}

		private static List<KeyValuePair<string, object>> DecodeDictionary(byte[] data, ref int position)
		{
			List<KeyValuePair<string, object>> entries = new List<KeyValuePair<string, object>>();
			while (true)
			{
				if (position >= data.Length)
				{
					throw new FormatException("unterminated bencode dictionary");
				}
				if (data[position] == (byte)'e')
				{
					position++;
					return entries;
				}
				if (data[position] < (byte)'0' || data[position] > (byte)'9')
				{
					throw new FormatException("bencode dictionary key is not a string at offset " + position.ToString(CultureInfo.InvariantCulture));
				}
				string key = DecodeString(data, ref position).ToText();
				object value = DecodeValue(data, ref position);
				entries.Add(new KeyValuePair<string, object>(key, value));
			}
		}

		// ---- 拍平 ----

		private static void Flatten(object node, string path, List<KvRow> rows)
		{
			if (node is TorrentString torrentString)
			{
				rows.Add(new KvRow
				{
					Path = path,
					Value = RenderString(path, torrentString),
					Compare = "s:" + torrentString.ByteCount.ToString(CultureInfo.InvariantCulture) + ":" + HashBytes(torrentString.Bytes)
				});
				return;
			}
			if (node is long integer)
			{
				rows.Add(new KvRow
				{
					Path = path,
					Value = RenderInteger(path, integer),
					Compare = "i:" + integer.ToString(CultureInfo.InvariantCulture)
				});
				return;
			}
			if (node is List<object> list)
			{
				for (int index = 0; index < list.Count; index++)
				{
					Flatten(list[index], path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", rows);
				}
				return;
			}
			if (node is List<KeyValuePair<string, object>> dictionary)
			{
				foreach (KeyValuePair<string, object> entry in dictionary)
				{
					Flatten(entry.Value, path.Length == 0 ? entry.Key : path + "." + entry.Key, rows);
				}
			}
		}

		private static string RenderString(string path, TorrentString value)
		{
			if (string.Equals(path, "info.pieces", StringComparison.Ordinal))
			{
				return (value.ByteCount / Sha1Bytes).ToString(CultureInfo.InvariantCulture) + " SHA-1 hashes";
			}
			if (value.ByteCount > LargeStringBytes)
			{
				return value.ByteCount.ToString(CultureInfo.InvariantCulture) + " bytes";
			}
			return value.ToText();
		}

		private static string RenderInteger(string path, long value)
		{
			if (!IsUnixTimeKey(path))
			{
				return value.ToString(CultureInfo.InvariantCulture);
			}
			try
			{
				return DateTimeOffset.FromUnixTimeSeconds(value).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
			}
			catch (ArgumentOutOfRangeException)
			{
				return value.ToString(CultureInfo.InvariantCulture);
			}
		}

		/// <summary>“creation date” / “mtime”（含 info.files[i].mtime 等）等 unix 秒值的键名判定。</summary>
		private static bool IsUnixTimeKey(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return false;
			}
			int dot = path.LastIndexOf('.');
			string key = dot >= 0 ? path.Substring(dot + 1) : path;
			return string.Equals(key, "creation date", StringComparison.Ordinal) || string.Equals(key, "mtime", StringComparison.Ordinal);
		}

		/// <summary>字节串的语义指纹（FNV-1a 64 位 + 字节数），供 diff 判「同值」用，不做完整字节对比。</summary>
		private static string HashBytes(byte[] bytes)
		{
			ulong hash = 14695981039346656037UL;
			for (int index = 0; index < bytes.Length; index++)
			{
				hash ^= bytes[index];
				hash *= 1099511628211UL;
			}
			return hash.ToString("X16", CultureInfo.InvariantCulture);
		}

		// ---- info 摘要 ----

		private static void FillSummary(List<KeyValuePair<string, object>> root, TorrentData result)
		{
			if (!(TryGetValue(root, "info", out object infoValue) && infoValue is List<KeyValuePair<string, object>> info))
			{
				return;
			}
			if (TryGetValue(info, "name", out object nameValue) && nameValue is TorrentString name)
			{
				result.Name = name.ToText();
			}
			if (TryGetValue(info, "pieces", out object piecesValue) && piecesValue is TorrentString pieces)
			{
				result.PieceCount = pieces.ByteCount / Sha1Bytes;
			}
			if (TryGetValue(info, "length", out object lengthValue) && lengthValue is long length)
			{
				result.TotalLength = length;
				return;
			}
			if (TryGetValue(info, "files", out object filesValue) && filesValue is List<object> files)
			{
				long total = 0L;
				bool any = false;
				foreach (object file in files)
				{
					if (file is List<KeyValuePair<string, object>> fileDictionary && TryGetValue(fileDictionary, "length", out object fileLength) && fileLength is long fileLengthValue)
					{
						total += fileLengthValue;
						any = true;
					}
				}
				if (any)
				{
					result.TotalLength = total;
				}
			}
		}

		private static bool TryGetValue(List<KeyValuePair<string, object>> dictionary, string key, out object value)
		{
			foreach (KeyValuePair<string, object> entry in dictionary)
			{
				if (string.Equals(entry.Key, key, StringComparison.Ordinal))
				{
					value = entry.Value;
					return true;
				}
			}
			value = null;
			return false;
		}
	}
}
