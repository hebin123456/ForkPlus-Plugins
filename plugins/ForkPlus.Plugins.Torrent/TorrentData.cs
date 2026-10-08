using System;
using System.Collections.Generic;

namespace ForkPlus.Plugins.Torrent
{
	/// <summary>
	/// bencode 字节串：保留原始字节（长度 / 哈希比较用），文本仅为 UTF-8 展示解码。
	/// 种子里的 pieces 等字段是任意二进制，不能无伤折成 System.String（会丢字节长度），故单独成节点类型。
	/// </summary>
	internal sealed class TorrentString
	{
		internal readonly byte[] Bytes;

		private string _text;

		internal TorrentString(byte[] bytes)
		{
			Bytes = bytes;
		}

		/// <summary>原始字节数。</summary>
		internal int ByteCount => Bytes.Length;

		/// <summary>UTF-8 展示文本（无效字节替换为 U+FFFD；仅用于短字符串展示）。</summary>
		internal string ToText()
		{
			if (_text == null)
			{
				_text = System.Text.Encoding.UTF8.GetString(Bytes);
			}
			return _text;
		}
	}

	/// <summary>拍平后的一个键值：Path（字典键以点号相连、列表下标 [i]）、Value（展示值）、Compare（语义比较值）。</summary>
	internal sealed class KvRow
	{
		internal string Path;

		internal string Value;

		/// <summary>语义比较值（整数原值 / 字节串长度 + 哈希），与展示值解耦——“{N} bytes” 字面相同但内容不同仍判「已变」。</summary>
		internal string Compare;
	}

	/// <summary>一次 .torrent 解析结果：info 摘要 + 拍平的键值行。</summary>
	internal sealed class TorrentData
	{
		/// <summary>info.name；解析不出为 null（视图回退文件名）。</summary>
		internal string Name;

		/// <summary>分片数（info.pieces 字节数 / 20）；无 pieces 为 null。</summary>
		internal int? PieceCount;

		/// <summary>总长度（info.length 或 files[].length 之和）；取不到为 null。</summary>
		internal long? TotalLength;

		internal List<KvRow> Rows = new List<KvRow>();
	}

	/// <summary>键路径行的四态。</summary>
	internal enum TorrentState
	{
		Same,
		Changed,
		LeftOnly,
		RightOnly
	}

	/// <summary>对比后的一行：键路径在旧 / 新两侧的展示值与四态（一侧缺失时对应值为 null）。</summary>
	internal sealed class TorrentDiffRow
	{
		internal string Path;

		internal string LeftValue;

		internal string RightValue;

		internal TorrentState State;
	}

	internal sealed class TorrentDiffResult
	{
		internal List<TorrentDiffRow> Rows = new List<TorrentDiffRow>();

		internal int Changed;

		internal int LeftOnly;

		internal int RightOnly;

		internal int Total => Rows.Count;

		internal bool HasDifferences => Changed > 0 || LeftOnly > 0 || RightOnly > 0;
	}

	/// <summary>
	/// Torrent 键路径 diff：两侧拍平后按 Path 取并集，值（Compare 语义）相同为 same、
	/// 都存在但不同为 changed、仅左为 left only、仅右为 right only；行按 Path 序数排序，
	/// 保证两侧行序稳定、增删键不打乱其余行的相对位置。
	/// </summary>
	internal static class TorrentDiff
	{
		internal static TorrentDiffResult Compute(List<KvRow> left, List<KvRow> right)
		{
			TorrentDiffResult result = new TorrentDiffResult();
			Dictionary<string, KvRow> rightRows = new Dictionary<string, KvRow>(StringComparer.Ordinal);
			if (right != null)
			{
				foreach (KvRow row in right)
				{
					rightRows[row.Path] = row;
				}
			}
			if (left != null)
			{
				foreach (KvRow row in left)
				{
					rightRows.TryGetValue(row.Path, out KvRow rightRow);
					result.Rows.Add(MakeRow(row.Path, row.Value, rightRow?.Value, StateOf(row, rightRow)));
					if (rightRow != null)
					{
						rightRows.Remove(row.Path);
					}
				}
			}
			foreach (KvRow row in rightRows.Values)
			{
				result.Rows.Add(MakeRow(row.Path, null, row.Value, TorrentState.RightOnly));
			}
			result.Rows.Sort(delegate (TorrentDiffRow a, TorrentDiffRow b)
			{
				return string.CompareOrdinal(a.Path, b.Path);
			});
			foreach (TorrentDiffRow row in result.Rows)
			{
				switch (row.State)
				{
				case TorrentState.Changed:
					result.Changed++;
					break;
				case TorrentState.LeftOnly:
					result.LeftOnly++;
					break;
				case TorrentState.RightOnly:
					result.RightOnly++;
					break;
				}
			}
			return result;
		}

		private static TorrentState StateOf(KvRow left, KvRow right)
		{
			if (right == null)
			{
				return TorrentState.LeftOnly;
			}
			return string.Equals(left.Compare, right.Compare, StringComparison.Ordinal) ? TorrentState.Same : TorrentState.Changed;
		}

		private static TorrentDiffRow MakeRow(string path, string leftValue, string rightValue, TorrentState state)
		{
			return new TorrentDiffRow
			{
				Path = path,
				LeftValue = leftValue,
				RightValue = rightValue,
				State = state
			};
		}
	}
}
