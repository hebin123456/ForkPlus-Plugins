using System;
using System.Collections.Generic;
using System.Text;

namespace ForkPlus.Plugins.Patch
{
	/// <summary>
	/// unified diff / git diff 补丁解析器（纯托管，容错优先）。
	///
	/// 识别：git 扩展头（new file mode / deleted file mode / rename from-to / index /
	/// similarity），<c>GIT binary patch</c> 与 <c>Binary files … differ</c> 二进制标记，
	/// 传统 unified diff 的 <c>--- / +++</c> 头与 <c>@@</c> hunk；hunk 体识别
	/// 空格（上下文）/ + / - / <c>\ No newline at end of file</c>，空行按上下文处理。
	/// 无法识别的行一律跳过——解析的目标是「尽量还原可视化所需的结构」，不是严格校验。
	/// </summary>
	internal static class PatchParser
	{
		/// <summary>解析补丁字节；没有任何可识别的 diff 内容时返回 null 并给出 error。</summary>
		internal static PatchDocument Parse(byte[] bytes, out string error)
		{
			error = null;
			if (bytes == null || bytes.Length == 0)
			{
				error = "empty input";
				return null;
			}
			List<string> lines = SplitLines(Decode(bytes));
			List<PatchFile> files = new List<PatchFile>();
			int i = 0;
			while (i < lines.Count)
			{
				string line = lines[i];
				if (line.StartsWith("diff --git ", StringComparison.Ordinal))
				{
					i = ParseGitFile(lines, i, files);
					continue;
				}
				// 传统 unified diff：--- 头只有在下一行是 +++ 时才算文件头（否则可能是补丁正文）。
				if (line.StartsWith("--- ", StringComparison.Ordinal) && i + 1 < lines.Count
					&& lines[i + 1].StartsWith("+++ ", StringComparison.Ordinal))
				{
					i = ParsePlainFile(lines, i, files);
					continue;
				}
				i++;
			}
			if (files.Count == 0)
			{
				error = "no unified diff content found";
				return null;
			}
			return new PatchDocument(files);
		}

		// ---- git diff 块 ----

		/// <summary>从 <c>diff --git</c> 行开始解析一个文件的块，返回下一个未消费的行号。</summary>
		private static int ParseGitFile(List<string> lines, int start, List<PatchFile> files)
		{
			SplitGitHeaderPaths(lines[start], out string aPath, out string bPath);
			// rename from/to 会覆盖默认路径；new file mode / deleted file mode 决定增删。
			string oldPath = aPath;
			string newPath = bPath;
			bool added = false;
			bool deleted = false;
			bool renamed = false;
			bool binary = false;
			PatchFile file = null;
			int i = start + 1;
			while (i < lines.Count)
			{
				string line = lines[i];
				if (line.StartsWith("diff --git ", StringComparison.Ordinal))
				{
					break; // 下一块
				}
				if (line.StartsWith("old mode ", StringComparison.Ordinal) || line.StartsWith("new mode ", StringComparison.Ordinal)
					|| line.StartsWith("index ", StringComparison.Ordinal)
					|| line.StartsWith("similarity index ", StringComparison.Ordinal)
					|| line.StartsWith("dissimilarity index ", StringComparison.Ordinal)
					|| line.StartsWith("copy from ", StringComparison.Ordinal) || line.StartsWith("copy to ", StringComparison.Ordinal))
				{
					i++;
					continue;
				}
				if (line.StartsWith("new file mode", StringComparison.Ordinal))
				{
					added = true;
					oldPath = null;
					i++;
					continue;
				}
				if (line.StartsWith("deleted file mode", StringComparison.Ordinal))
				{
					deleted = true;
					newPath = null;
					i++;
					continue;
				}
				if (line.StartsWith("rename from ", StringComparison.Ordinal))
				{
					oldPath = CleanPath(line.Substring("rename from ".Length));
					renamed = true;
					i++;
					continue;
				}
				if (line.StartsWith("rename to ", StringComparison.Ordinal))
				{
					newPath = CleanPath(line.Substring("rename to ".Length));
					renamed = true;
					i++;
					continue;
				}
				if (line.StartsWith("GIT binary patch", StringComparison.Ordinal))
				{
					// 二进制补丁体（literal / zlib85）对可视化无意义：跳过到下一块 / 分隔符 / 结尾。
					binary = true;
					i = SkipBinaryBody(lines, i + 1);
					continue;
				}
				if (line.StartsWith("Binary files ", StringComparison.Ordinal))
				{
					binary = true;
					i++;
					continue;
				}
				if (line.StartsWith("@@ ", StringComparison.Ordinal) && TryParseHunkHeader(line, out PatchHunk hunk))
				{
					if (file == null)
					{
						file = NewFile(oldPath, newPath, added, deleted, renamed, binary);
						files.Add(file);
					}
					i = ParseHunkBody(lines, i + 1, hunk);
					file.Hunks.Add(hunk);
					continue;
				}
				i++;
			}
			if (file == null)
			{
				// 无 hunk 也登记：纯模式改动 / rename 头 / 二进制标记等同样是有意义的文件级条目。
				files.Add(NewFile(oldPath, newPath, added, deleted, renamed, binary));
			}
			return i;
		}

		/// <summary>跳过 GIT binary patch 的 body（literal / zlib85 块），停在下一块或结尾。</summary>
		private static int SkipBinaryBody(List<string> lines, int start)
		{
			int i = start;
			while (i < lines.Count)
			{
				string line = lines[i];
				if (line.StartsWith("diff --git ", StringComparison.Ordinal) || line == "-- ")
				{
					break;
				}
				i++;
			}
			return i;
		}

		private static PatchFile NewFile(string oldPath, string newPath, bool added, bool deleted, bool renamed, bool binary)
		{
			PatchFileChange change = binary
				? PatchFileChange.Binary
				: added ? PatchFileChange.Added
				: deleted ? PatchFileChange.Deleted
				: renamed ? PatchFileChange.Renamed
				: PatchFileChange.Modified;
			return new PatchFile(oldPath, newPath, change, binary);
		}

		// ---- 传统 unified diff 块 ----

		/// <summary>从 <c>--- </c> 行开始解析传统 unified diff，返回下一个未消费的行号。</summary>
		private static int ParsePlainFile(List<string> lines, int start, List<PatchFile> files)
		{
			string oldPath = CleanPath(lines[start].Substring(4));
			string newPath = CleanPath(lines[start + 1].Substring(4));
			PatchFile file = new PatchFile(NullToNull(oldPath), NullToNull(newPath), ChangeOf(NullToNull(oldPath), NullToNull(newPath)), false);
			files.Add(file);
			int i = start + 2;
			while (i < lines.Count && lines[i].StartsWith("@@ ", StringComparison.Ordinal)
				&& TryParseHunkHeader(lines[i], out PatchHunk hunk))
			{
				i = ParseHunkBody(lines, i + 1, hunk);
				file.Hunks.Add(hunk);
			}
			return i;
		}

		private static string NullToNull(string path)
		{
			return string.Equals(path, "/dev/null", StringComparison.Ordinal) ? null : path;
		}

		private static PatchFileChange ChangeOf(string oldPath, string newPath)
		{
			if (oldPath == null)
			{
				return PatchFileChange.Added;
			}
			if (newPath == null)
			{
				return PatchFileChange.Deleted;
			}
			return PatchFileChange.Modified;
		}

		// ---- hunk ----

		/// <summary>解析 <c>@@ -l[,c] +l[,c] @@ section</c>；计数缺省为 1。</summary>
		private static bool TryParseHunkHeader(string line, out PatchHunk hunk)
		{
			hunk = null;
			// @@ -a[,b] +c[,d] @@ section
			int pos = 3; // 跳过 "@@ "
			if (pos >= line.Length || line[pos] != '-')
			{
				return false;
			}
			pos = ParseCount(line, pos + 1, out int oldStart, out int oldCount);
			if (pos < 0 || pos >= line.Length || line[pos] != '+')
			{
				return false;
			}
			pos = ParseCount(line, pos + 1, out int newStart, out int newCount);
			if (pos < 0 || pos + 1 >= line.Length || line[pos] != '@' || line[pos + 1] != '@')
			{
				return false;
			}
			// 定位第二组 "@@" 之后的区段名
			int second = line.IndexOf("@@", pos + 2, StringComparison.Ordinal);
			string section = second >= 0 ? line.Substring(second + 2).Trim() : string.Empty;
			hunk = new PatchHunk(oldStart, oldCount, newStart, newCount, section);
			return true;
		}

		/// <summary>从 from 起解析 "数字[,数字]"，返回下一位置；负数表示失败。</summary>
		private static int ParseCount(string line, int from, out int first, out int second)
		{
			first = 0;
			second = 1;
			int i = from;
			int start = i;
			while (i < line.Length && line[i] >= '0' && line[i] <= '9')
			{
				i++;
			}
			if (i == start)
			{
				return -1;
			}
			first = int.Parse(line.Substring(start, i - start));
			if (i < line.Length && line[i] == ',')
			{
				int s2 = ++i;
				while (i < line.Length && line[i] >= '0' && line[i] <= '9')
				{
					i++;
				}
				if (i == s2)
				{
					return -1;
				}
				second = int.Parse(line.Substring(s2, i - s2));
			}
			// 跳过空格，让调用方直接看下一个符号
			while (i < line.Length && line[i] == ' ')
			{
				i++;
			}
			return i;
		}

		/// <summary>吃掉 hunk 体：前缀 空格 / + / - / \ 与空行（视作上下文），返回下一个未消费的行号。</summary>
		private static int ParseHunkBody(List<string> lines, int start, PatchHunk hunk)
		{
			int i = start;
			while (i < lines.Count)
			{
				string line = lines[i];
				if (line.Length == 0)
				{
					hunk.Lines.Add(new PatchLine(PatchLineKind.Context, string.Empty));
					i++;
					continue;
				}
				char head = line[0];
				if (head == ' ')
				{
					hunk.Lines.Add(new PatchLine(PatchLineKind.Context, line.Substring(1)));
				}
				else if (head == '+')
				{
					hunk.Lines.Add(new PatchLine(PatchLineKind.Add, line.Substring(1)));
				}
				else if (head == '-')
				{
					hunk.Lines.Add(new PatchLine(PatchLineKind.Delete, line.Substring(1)));
				}
				else if (head == '\\')
				{
					hunk.Lines.Add(new PatchLine(PatchLineKind.NoNewline, line));
				}
				else
				{
					break;
				}
				i++;
			}
			return i;
		}

		// ---- 路径 / 文本 ----

		/// <summary>
		/// 拆分 <c>diff --git a/X b/Y</c> 的两个路径：优先按两个带引号路径切，
		/// 否则按 " b/" 启发式切（与 git 自身口径一致，路径含 " b/" 的极端情况不保证）。
		/// </summary>
		private static void SplitGitHeaderPaths(string line, out string aPath, out string bPath)
		{
			string text = line.Length > "diff --git ".Length ? line.Substring("diff --git ".Length) : string.Empty;
			aPath = string.Empty;
			bPath = string.Empty;
			if (text.StartsWith("\"", StringComparison.Ordinal))
			{
				int firstEnd = text.IndexOf('"', 1);
				int secondStart = firstEnd >= 0 ? text.IndexOf('"', firstEnd + 1) : -1;
				if (secondStart >= 0)
				{
					int secondEnd = text.IndexOf('"', secondStart + 1);
					if (secondEnd > secondStart)
					{
						aPath = CleanPath(Unescape(text.Substring(1, firstEnd - 1)));
						bPath = CleanPath(Unescape(text.Substring(secondStart + 1, secondEnd - secondStart - 1)));
						return;
					}
				}
			}
			int split = text.IndexOf(" b/", StringComparison.Ordinal);
			if (split > 0)
			{
				aPath = CleanPath(text.Substring(0, split));
				bPath = CleanPath(text.Substring(split + 1));
				return;
			}
			// 兜底：整个文本按单路径处理（rename 头会随后覆盖）。
			aPath = CleanPath(text);
			bPath = aPath;
		}

		/// <summary>清理路径：去掉 <c>a/</c> / <c>b/</c> 前缀、裁掉制表符后的时间戳、去引号。</summary>
		private static string CleanPath(string raw)
		{
			string text = raw?.Trim() ?? string.Empty;
			if (text.Length == 0)
			{
				return string.Empty;
			}
			if (text.StartsWith("\"", StringComparison.Ordinal))
			{
				// 带引号的路径（含转义）：去引号并反转义常见序列；不做时间戳裁剪。
				int end = text.IndexOf('"', 1);
				if (end > 0)
				{
					return Unescape(text.Substring(1, end - 1));
				}
			}
			int tab = text.IndexOf('\t');
			if (tab >= 0)
			{
				text = text.Substring(0, tab).TrimEnd();
			}
			if (text.StartsWith("a/", StringComparison.Ordinal) || text.StartsWith("b/", StringComparison.Ordinal))
			{
				text = text.Substring(2);
			}
			return text;
		}

		private static string Unescape(string text)
		{
			StringBuilder sb = new StringBuilder(text.Length);
			for (int i = 0; i < text.Length; i++)
			{
				if (text[i] == '\\' && i + 1 < text.Length)
				{
					char next = text[++i];
					switch (next)
					{
					case 'n': sb.Append('\n'); break;
					case 't': sb.Append('\t'); break;
					case 'r': sb.Append('\r'); break;
					default: sb.Append(next); break;
					}
					continue;
				}
				sb.Append(text[i]);
			}
			return sb.ToString();
		}

		/// <summary>按 BOM &gt; UTF-8 解码补丁文本（补丁没有 charset 声明；非法字节替换为 U+FFFD）。</summary>
		private static string Decode(byte[] bytes)
		{
			if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
			{
				return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
			}
			if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
			{
				return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
			}
			if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
			{
				return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
			}
			return Encoding.UTF8.GetString(bytes);
		}

		/// <summary>按 \n 切行并去掉行尾 \r（兼容 CRLF / LF 补丁）。</summary>
		private static List<string> SplitLines(string text)
		{
			List<string> lines = new List<string>();
			int start = 0;
			for (int i = 0; i < text.Length; i++)
			{
				if (text[i] != '\n')
				{
					continue;
				}
				int end = i;
				if (end > start && text[end - 1] == '\r')
				{
					end--;
				}
				lines.Add(text.Substring(start, end - start));
				start = i + 1;
			}
			if (start < text.Length)
			{
				int end = text.Length;
				if (end > start && text[end - 1] == '\r')
				{
					end--;
				}
				lines.Add(text.Substring(start, end - start));
			}
			return lines;
		}
	}
}
