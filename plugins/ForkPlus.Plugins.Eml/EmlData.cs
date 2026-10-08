using System;
using System.Collections.Generic;

namespace ForkPlus.Plugins.Eml
{
	/// <summary>键路径对比表的一行：稳定键路径 + 展示值（三种口径共用同一行模型）。</summary>
	internal sealed class EmlRow
	{
		internal string Path;

		internal string Value;
	}

	/// <summary>MIME 树里的一个部件：路径 + 内容类型 / 传输编码 / 字符集 / 文件名 / 处置 + 解码后正文。</summary>
	internal sealed class EmlPart
	{
		/// <summary>部件路径，如 "part[1]" / "part[1].part[2]"（根部件为 "message"）。</summary>
		internal string Path;

		internal string ContentType;

		internal string Encoding;

		internal string Charset;

		internal string FileName;

		internal string Disposition;

		/// <summary>解码后正文的字节数（无法解码时为原始长度）。</summary>
		internal long Size;

		/// <summary>文本部件（text/*）解码后的正文；非文本或解码失败为 null。</summary>
		internal string Text;

		/// <summary>是否有子部件（multipart 容器）。</summary>
		internal bool IsMultipart;
	}

	/// <summary>一次 EML 解析的结果：邮件摘要 + 三种口径的键路径行 + MIME 部件清单。</summary>
	internal sealed class EmlDocument
	{
		/// <summary>邮件头口径：Subject / From / To / Cc / Date / Message-ID / MIME 相关头等。</summary>
		internal List<EmlRow> HeaderRows = new List<EmlRow>();

		/// <summary>部件口径：MIME 树每个部件的内容类型 / 编码 / 字符集 / 文件名 / 体积。</summary>
		internal List<EmlRow> PartRows = new List<EmlRow>();

		/// <summary>正文口径：各文本部件的逐行正文（键路径 "part[i].line[n]"）。</summary>
		internal List<EmlRow> BodyRows = new List<EmlRow>();

		/// <summary>MIME 部件清单（平铺，含嵌套路径）。</summary>
		internal List<EmlPart> Parts = new List<EmlPart>();

		/// <summary>主题（解码后；缺失为 null，状态行展示用）。</summary>
		internal string Subject;

		/// <summary>邮件头条数（状态行展示用）。</summary>
		internal int HeaderCount;

		internal int PartCount;

		/// <summary>原始字节数。</summary>
		internal long TotalBytes;

		/// <summary>是否因超阈值截断（头 / 部件 / 正文行数上限）。</summary>
		internal bool Truncated;
	}

	internal enum EmlState
	{
		Same,
		Changed,
		LeftOnly,
		RightOnly
	}

	/// <summary>键路径对齐后的一行：键路径 + 旧 / 新两侧的值 + 变更状态。</summary>
	internal sealed class EmlRowDiff
	{
		internal string Path;

		internal string Left;

		internal string Right;

		internal EmlState State;
	}

	/// <summary>键路径 diff 结果：Rows 为键路径并集的四态行，计数用于状态行汇总。</summary>
	internal sealed class EmlDiffResult
	{
		internal List<EmlRowDiff> Rows = new List<EmlRowDiff>();

		internal int Changed;

		internal int LeftOnly;

		internal int RightOnly;

		internal int Total => Rows.Count;

		internal bool HasDifferences => Changed > 0 || LeftOnly > 0 || RightOnly > 0;
	}

	/// <summary>
	/// EML 对齐：两侧键路径行取并集——两侧都有且值相等 = same、都有但不等 = changed、
	/// 仅左侧有 = left only、仅右侧有 = right only；行序保持左侧解析顺序，再追加仅右侧的键。
	/// </summary>
	internal static class EmlDiff
	{
		internal static EmlDiffResult ComputeRows(List<EmlRow> left, List<EmlRow> right)
		{
			EmlDiffResult result = new EmlDiffResult();
			Dictionary<string, EmlRow> rightMap = new Dictionary<string, EmlRow>(StringComparer.Ordinal);
			if (right != null)
			{
				foreach (EmlRow row in right)
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
				foreach (EmlRow row in left)
				{
					if (string.IsNullOrEmpty(row.Path) || consumed.Contains(row.Path))
					{
						continue;
					}
					consumed.Add(row.Path);
					if (rightMap.TryGetValue(row.Path, out EmlRow match))
					{
						bool same = string.Equals(row.Value ?? string.Empty, match.Value ?? string.Empty, StringComparison.Ordinal);
						Add(result, row.Path, row.Value, match.Value, same ? EmlState.Same : EmlState.Changed);
					}
					else
					{
						Add(result, row.Path, row.Value, null, EmlState.LeftOnly);
					}
				}
			}
			if (right != null)
			{
				foreach (EmlRow row in right)
				{
					if (string.IsNullOrEmpty(row.Path) || consumed.Contains(row.Path))
					{
						continue;
					}
					consumed.Add(row.Path);
					Add(result, row.Path, null, row.Value, EmlState.RightOnly);
				}
			}
			return result;
		}

		private static void Add(EmlDiffResult result, string path, string left, string right, EmlState state)
		{
			result.Rows.Add(new EmlRowDiff
			{
				Path = path,
				Left = left,
				Right = right,
				State = state
			});
			switch (state)
			{
			case EmlState.Changed:
				result.Changed++;
				break;
			case EmlState.LeftOnly:
				result.LeftOnly++;
				break;
			case EmlState.RightOnly:
				result.RightOnly++;
				break;
			}
		}
	}
}