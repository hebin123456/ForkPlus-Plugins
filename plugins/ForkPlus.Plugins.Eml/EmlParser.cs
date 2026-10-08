using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ForkPlus.Plugins.Eml
{
	/// <summary>
	/// 邮件（.eml）解析：纯托管自解析 RFC 5322 头 + MIME（RFC 2045/2046）。
	///
	/// 步骤：
	/// <list type="number">
	/// <item>把原始字节按 Latin-1 还原成字符串（1 字节 = 1 字符，字节值不丢），
	/// 以首个空行（CRLFCRLF / LFLF）切出头块与正文。</item>
	/// <item>头块按「折叠行展开」解析成 name/value 列表（续行以空格 / 制表符开头）。</item>
	/// <item>Content-Type 为 <c>multipart/*</c> 时按 boundary 切分正文，逐个子实体递归解析，
	/// 形成 MIME 部件树。</item>
	/// <item>叶部件按 Content-Transfer-Encoding（base64 / quoted-printable）解码，
	/// 文本部件再按 charset 解成字符串。</item>
	/// </list>
	///
	/// 产出三套键路径行：邮件头（header.*）、部件（part[i].*）、正文（part[i].line[n]）。
	/// 展示值统一做 RFC 2047 编码字解码（<c>=?UTF-8?B?…?=</c> / <c>=?GBK?Q?…?=</c>）。
	///
	/// 防御：无有效头块、或不含任何已知邮件头（From / To / Subject / Date / Message-ID /
	/// MIME-Version / Content-Type / Received）时抛 <see cref="InvalidDataException"/>，
	/// 归类为「不是有效邮件」。头 / 部件 / 正文行数设上限，超出置 <see cref="EmlDocument.Truncated"/>。
	///
	/// 对外入口 <see cref="Parse(byte[], out string)"/> 不抛异常：失败返回 null 并回填 error。
	/// </summary>
	internal static class EmlParser
	{
		/// <summary>邮件头读取上限。</summary>
		private const int MaxHeaders = 200;

		/// <summary>MIME 部件读取上限（含嵌套）。</summary>
		private const int MaxParts = 200;

		/// <summary>正文行读取上限（各文本部件合计）。</summary>
		private const int MaxBodyLines = 2000;

		/// <summary>单条头展示值截断长度。</summary>
		private const int MaxHeaderValueChars = 400;

		/// <summary>MIME 递归深度上限（防御畸形 / 恶意的深层嵌套）。</summary>
		private const int MaxDepth = 12;

		private static readonly string[] RecognizedHeaders =
		{
			"from", "to", "cc", "bcc", "subject", "date", "message-id",
			"mime-version", "content-type", "content-transfer-encoding", "received", "reply-to", "sender"
		};

		/// <summary>成功返回文档，失败返回 null 并回填 error（error = 异常类型名 + 消息）。</summary>
		internal static EmlDocument Parse(byte[] data, out string error)
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

		private static EmlDocument ParseCore(byte[] data)
		{
			if (data == null || data.Length == 0)
			{
				throw new InvalidDataException("not a valid EML: empty file");
			}
			string text = Encoding.Latin1.GetString(data);
			SplitHeaderBody(text, out string headerText, out string bodyText);
			List<KeyValuePair<string, string>> headers = ParseHeaders(headerText);
			if (headers.Count == 0)
			{
				throw new InvalidDataException("not a valid EML: no RFC 5322 header block");
			}
			if (!HasRecognizedHeader(headers))
			{
				throw new InvalidDataException("not a valid EML: no recognizable mail header");
			}

			EmlDocument document = new EmlDocument();
			document.TotalBytes = data.Length;
			document.HeaderCount = headers.Count;
			BuildHeaderRows(document, headers);

			int partCount = 0;
			WalkPart(document, "message", headers, bodyText, 0, ref partCount);

			BuildPartRows(document);
			BuildBodyRows(document);
			return document;
		}

		// ---- 头 / 正文切分 ----

		/// <summary>以首个空行（CRLFCRLF 优先，其次 LFLF）切出头块与正文；无空行时正文为空。</summary>
		private static void SplitHeaderBody(string text, out string headerText, out string bodyText)
		{
			int separator = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
			int length = 4;
			if (separator < 0)
			{
				separator = text.IndexOf("\n\n", StringComparison.Ordinal);
				length = 2;
			}
			if (separator < 0)
			{
				headerText = text;
				bodyText = string.Empty;
				return;
			}
			headerText = text.Substring(0, separator);
			bodyText = text.Substring(separator + length);
		}

		/// <summary>解析头块：续行（空格 / 制表符开头）并入上一条头的值。</summary>
		private static List<KeyValuePair<string, string>> ParseHeaders(string headerText)
		{
			List<KeyValuePair<string, string>> headers = new List<KeyValuePair<string, string>>();
			string[] lines = headerText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
			string name = null;
			StringBuilder value = null;
			foreach (string line in lines)
			{
				if (line.Length == 0)
				{
					continue;
				}
				if ((line[0] == ' ' || line[0] == '\t') && name != null)
				{
					value.Append(' ').Append(line.Trim());
					continue;
				}
				int colon = line.IndexOf(':');
				if (colon <= 0)
				{
					continue;
				}
				if (name != null)
				{
					headers.Add(new KeyValuePair<string, string>(name, value.ToString().Trim()));
				}
				name = line.Substring(0, colon).Trim();
				value = new StringBuilder(line.Substring(colon + 1).Trim());
			}
			if (name != null)
			{
				headers.Add(new KeyValuePair<string, string>(name, value.ToString().Trim()));
			}
			return headers;
		}

		private static bool HasRecognizedHeader(List<KeyValuePair<string, string>> headers)
		{
			foreach (KeyValuePair<string, string> header in headers)
			{
				string lower = header.Key.ToLowerInvariant();
				foreach (string known in RecognizedHeaders)
				{
					if (string.Equals(lower, known, StringComparison.Ordinal))
					{
						return true;
					}
				}
			}
			return false;
		}

		// ---- MIME 树 ----

		/// <summary>递归解析一个 MIME 实体：登记自身，若为 multipart 则切分正文并递归子部件。</summary>
		private static void WalkPart(EmlDocument document, string path, List<KeyValuePair<string, string>> headers, string bodyText, int depth, ref int count)
		{
			if (count >= MaxParts)
			{
				document.Truncated = true;
				return;
			}
			count++;
			document.PartCount++;

			ContentTypeInfo info = ParseContentType(GetHeader(headers, "content-type"));
			string encoding = (GetHeader(headers, "content-transfer-encoding") ?? string.Empty).Trim().ToLowerInvariant();
			DecodeHeaderParams(headers, info);

			EmlPart part = new EmlPart
			{
				Path = path,
				ContentType = string.IsNullOrEmpty(info.MediaType) ? "(unspecified)" : info.MediaType,
				Encoding = encoding,
				Charset = info.Charset,
				FileName = DecodeEncodedWords(info.FileName),
				Disposition = info.Disposition,
				IsMultipart = info.MediaType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase)
			};
			document.Parts.Add(part);

			if (part.IsMultipart && depth < MaxDepth && !string.IsNullOrEmpty(info.Boundary))
			{
				List<string> segments = SplitMultipart(bodyText, info.Boundary);
				int index = 0;
				foreach (string segment in segments)
				{
					if (count >= MaxParts)
					{
						document.Truncated = true;
						break;
					}
					index++;
					SplitHeaderBody(segment, out string childHeaderText, out string childBodyText);
					List<KeyValuePair<string, string>> childHeaders = ParseHeaders(childHeaderText);
					string childPath = path + ".part[" + index.ToString(CultureInfo.InvariantCulture) + "]";
					WalkPart(document, childPath, childHeaders, childBodyText, depth + 1, ref count);
				}
				return;
			}

			// 叶部件：解码正文（文本部件保留可读正文）。
			byte[] decoded = DecodeBodyBytes(bodyText, encoding);
			part.Size = decoded.Length;
			if (part.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
			{
				part.Text = DecodeText(decoded, info.Charset);
			}
		}

		/// <summary>按 RFC 2046 的 boundary 切分 multipart 正文为若干子实体文本。</summary>
		private static List<string> SplitMultipart(string body, string boundary)
		{
			List<string> parts = new List<string>();
			if (string.IsNullOrEmpty(body) || string.IsNullOrEmpty(boundary))
			{
				return parts;
			}
			string delimiter = "--" + boundary;
			int index = body.IndexOf(delimiter, StringComparison.Ordinal);
			if (index < 0)
			{
				return parts;
			}
			while (index >= 0 && parts.Count < MaxParts + 1)
			{
				int start = index + delimiter.Length;
				// 结束定界符 "--boundary--"
				if (start + 2 <= body.Length && body[start] == '-' && body[start + 1] == '-')
				{
					break;
				}
				start = SkipToNextLine(body, start);
				int next = body.IndexOf(delimiter, start, StringComparison.Ordinal);
				int end = next < 0 ? body.Length : next;
				string segment = body.Substring(start, end - start);
				parts.Add(TrimTrailingNewline(segment));
				if (next < 0)
				{
					break;
				}
				index = next;
			}
			return parts;
		}

		/// <summary>跳过行尾的 CRLF / LF，返回下一行起点（没有换行则返回末尾）。</summary>
		private static int SkipToNextLine(string text, int position)
		{
			while (position < text.Length && text[position] != '\n')
			{
				position++;
			}
			return position < text.Length ? position + 1 : text.Length;
		}

		private static string TrimTrailingNewline(string text)
		{
			int end = text.Length;
			if (end > 0 && text[end - 1] == '\n')
			{
				end--;
			}
			if (end > 0 && text[end - 1] == '\r')
			{
				end--;
			}
			return text.Substring(0, end);
		}

		// ---- 行构建 ----

		/// <summary>邮件头口径：按名字分组，单值用 header.name，多值用 header.name[i]，另加 headers.count。</summary>
		private static void BuildHeaderRows(EmlDocument document, List<KeyValuePair<string, string>> headers)
		{
			List<EmlRow> rows = document.HeaderRows;
			Dictionary<string, int> order = new Dictionary<string, int>(StringComparer.Ordinal);
			Dictionary<string, int> totals = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (KeyValuePair<string, string> header in headers)
			{
				string lower = header.Key.ToLowerInvariant();
				totals.TryGetValue(lower, out int count);
				totals[lower] = count + 1;
			}

			int emitted = 0;
			foreach (KeyValuePair<string, string> header in headers)
			{
				if (emitted >= MaxHeaders)
				{
					document.Truncated = true;
					break;
				}
				emitted++;
				string lower = header.Key.ToLowerInvariant();
				order.TryGetValue(lower, out int seen);
				order[lower] = seen + 1;
				string path = totals[lower] > 1
					? "header." + lower + "[" + (seen + 1).ToString(CultureInfo.InvariantCulture) + "]"
					: "header." + lower;
				rows.Add(new EmlRow { Path = path, Value = Truncate(DecodeEncodedWords(header.Value), MaxHeaderValueChars) });
				if (string.Equals(lower, "subject", StringComparison.Ordinal) && document.Subject == null)
				{
					document.Subject = DecodeEncodedWords(header.Value);
				}
			}
			rows.Add(new EmlRow { Path = "headers.count", Value = headers.Count.ToString(CultureInfo.InvariantCulture) });
		}

		/// <summary>部件口径：每个部件的内容类型 / 编码 / 字符集 / 文件名 / 处置 / 体积，另加 parts.count。</summary>
		private static void BuildPartRows(EmlDocument document)
		{
			List<EmlRow> rows = document.PartRows;
			foreach (EmlPart part in document.Parts)
			{
				rows.Add(new EmlRow { Path = part.Path + ".content-type", Value = part.ContentType });
				if (!string.IsNullOrEmpty(part.Encoding))
				{
					rows.Add(new EmlRow { Path = part.Path + ".encoding", Value = part.Encoding });
				}
				if (!string.IsNullOrEmpty(part.Charset))
				{
					rows.Add(new EmlRow { Path = part.Path + ".charset", Value = part.Charset });
				}
				if (!string.IsNullOrEmpty(part.Disposition))
				{
					rows.Add(new EmlRow { Path = part.Path + ".disposition", Value = part.Disposition });
				}
				if (!string.IsNullOrEmpty(part.FileName))
				{
					rows.Add(new EmlRow { Path = part.Path + ".filename", Value = part.FileName });
				}
				rows.Add(new EmlRow
				{
					Path = part.Path + ".size",
					Value = part.Size.ToString("N0", CultureInfo.InvariantCulture) + " B"
				});
			}
			rows.Add(new EmlRow { Path = "parts.count", Value = document.PartCount.ToString(CultureInfo.InvariantCulture) });
		}

		/// <summary>正文口径：各文本部件逐行，键路径 "part[i].line[n]"；空行显示为 "(blank)"。</summary>
		private static void BuildBodyRows(EmlDocument document)
		{
			List<EmlRow> rows = document.BodyRows;
			int total = 0;
			foreach (EmlPart part in document.Parts)
			{
				if (part.Text == null)
				{
					continue;
				}
				string[] lines = part.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
				rows.Add(new EmlRow { Path = part.Path + ".type", Value = part.ContentType });
				int lineNo = 0;
				foreach (string line in lines)
				{
					if (total >= MaxBodyLines)
					{
						document.Truncated = true;
						return;
					}
					total++;
					lineNo++;
					rows.Add(new EmlRow
					{
						Path = part.Path + ".line[" + lineNo.ToString(CultureInfo.InvariantCulture) + "]",
						Value = line.Length == 0 ? "(blank)" : Truncate(line, MaxHeaderValueChars)
					});
				}
			}
		}

		// ---- Content-Type / 头参数 ----

		/// <summary>Content-Type 解析结果。</summary>
		private sealed class ContentTypeInfo
		{
			internal string MediaType = string.Empty;

			internal string Boundary;

			internal string Charset;

			internal string FileName;

			internal string Disposition;
		}

		/// <summary>解析 Content-Type：主类型 + 参数（boundary / charset / name）。</summary>
		private static ContentTypeInfo ParseContentType(string value)
		{
			ContentTypeInfo info = new ContentTypeInfo();
			if (string.IsNullOrEmpty(value))
			{
				return info;
			}
			string[] segments = value.Split(';');
			info.MediaType = segments[0].Trim().ToLowerInvariant();
			for (int i = 1; i < segments.Length; i++)
			{
				string[] pair = segments[i].Split(new[] { '=' }, 2);
				if (pair.Length != 2)
				{
					continue;
				}
				string key = pair[0].Trim().ToLowerInvariant();
				string val = Unquote(pair[1].Trim());
				switch (key)
				{
				case "boundary":
					info.Boundary = val;
					break;
				case "charset":
					info.Charset = val;
					break;
				case "name":
					info.FileName = val;
					break;
				}
			}
			return info;
		}

		/// <summary>从 Content-Disposition 头补 filename / disposition（Content-Type 的 name 参数优先级次之）。</summary>
		private static void DecodeHeaderParams(List<KeyValuePair<string, string>> headers, ContentTypeInfo info)
		{
			string disposition = GetHeader(headers, "content-disposition");
			if (string.IsNullOrEmpty(disposition))
			{
				return;
			}
			string[] segments = disposition.Split(';');
			info.Disposition = segments[0].Trim().ToLowerInvariant();
			for (int i = 1; i < segments.Length; i++)
			{
				string[] pair = segments[i].Split(new[] { '=' }, 2);
				if (pair.Length != 2)
				{
					continue;
				}
				if (string.Equals(pair[0].Trim(), "filename", StringComparison.OrdinalIgnoreCase))
				{
					info.FileName = Unquote(pair[1].Trim());
				}
			}
		}

		private static string Unquote(string value)
		{
			if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
			{
				return value.Substring(1, value.Length - 2);
			}
			return value;
		}

		private static string GetHeader(List<KeyValuePair<string, string>> headers, string name)
		{
			foreach (KeyValuePair<string, string> header in headers)
			{
				if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
				{
					return header.Value;
				}
			}
			return null;
		}

		// ---- CTE / 编码字 / 文本解码 ----

		/// <summary>按 Content-Transfer-Encoding 把正文段解成字节。</summary>
		private static byte[] DecodeBodyBytes(string body, string encoding)
		{
			if (string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
			{
				string compact = RemoveWhitespace(body);
				try
				{
					return Convert.FromBase64String(compact);
				}
				catch (FormatException)
				{
					return Encoding.Latin1.GetBytes(body);
				}
			}
			if (string.Equals(encoding, "quoted-printable", StringComparison.OrdinalIgnoreCase))
			{
				return DecodeQuotedPrintable(body);
			}
			return Encoding.Latin1.GetBytes(body);
		}

		private static string RemoveWhitespace(string value)
		{
			StringBuilder builder = new StringBuilder(value.Length);
			foreach (char c in value)
			{
				if (!char.IsWhiteSpace(c))
				{
					builder.Append(c);
				}
			}
			return builder.ToString();
		}

		/// <summary>quoted-printable 解码：=XX 十六进制还原，行尾 "=" 是软换行需丢弃。</summary>
		private static byte[] DecodeQuotedPrintable(string value)
		{
			List<byte> bytes = new List<byte>(value.Length);
			for (int i = 0; i < value.Length; i++)
			{
				char c = value[i];
				if (c != '=')
				{
					bytes.Add((byte)c);
					continue;
				}
				// 软换行："=" 后直接换行（CRLF / LF）
				if (i + 1 < value.Length && (value[i + 1] == '\n' || value[i + 1] == '\r'))
				{
					i++;
					if (i < value.Length && value[i] == '\r' && i + 1 < value.Length && value[i + 1] == '\n')
					{
						i++;
					}
					continue;
				}
				if (i + 2 < value.Length && IsHex(value[i + 1]) && IsHex(value[i + 2]))
				{
					bytes.Add((byte)((HexValue(value[i + 1]) << 4) | HexValue(value[i + 2])));
					i += 2;
					continue;
				}
				bytes.Add((byte)c);
			}
			return bytes.ToArray();
		}

		private static bool IsHex(char c)
		{
			return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
		}

		private static int HexValue(char c)
		{
			if (c >= '0' && c <= '9')
			{
				return c - '0';
			}
			if (c >= 'a' && c <= 'f')
			{
				return c - 'a' + 10;
			}
			return c - 'A' + 10;
		}

		/// <summary>按 charset 解码字节为文本；charset 缺失 / 未知回退 UTF-8（再退 Latin-1）。</summary>
		private static string DecodeText(byte[] bytes, string charset)
		{
			if (bytes == null)
			{
				return null;
			}
			if (!string.IsNullOrEmpty(charset))
			{
				try
				{
					return Encoding.GetEncoding(charset).GetString(bytes);
				}
				catch (ArgumentException)
				{
				}
			}
			try
			{
				return new UTF8Encoding(false, true).GetString(bytes);
			}
			catch (DecoderFallbackException)
			{
				return Encoding.Latin1.GetString(bytes);
			}
		}

		/// <summary>解码 RFC 2047 编码字（=?charset?B|Q?payload?=），逐段替换、非编码字原样保留。</summary>
		private static string DecodeEncodedWords(string value)
		{
			if (string.IsNullOrEmpty(value) || value.IndexOf("=?", StringComparison.Ordinal) < 0)
			{
				return value;
			}
			StringBuilder output = new StringBuilder(value.Length);
			int index = 0;
			while (index < value.Length)
			{
				int start = value.IndexOf("=?", index, StringComparison.Ordinal);
				if (start < 0)
				{
					output.Append(value, index, value.Length - index);
					break;
				}
				output.Append(value, index, start - index);
				int q1 = value.IndexOf('?', start + 2);
				int q2 = q1 < 0 ? -1 : value.IndexOf('?', q1 + 1);
				int end = q2 < 0 ? -1 : value.IndexOf("?=", q2 + 1, StringComparison.Ordinal);
				if (q1 < 0 || q2 < 0 || end < 0)
				{
					output.Append(value, start, value.Length - start);
					break;
				}
				string charset = value.Substring(start + 2, q1 - start - 2);
				string encoding = value.Substring(q1 + 1, q2 - q1 - 1);
				string payload = value.Substring(q2 + 1, end - q2 - 1);
				string decoded = TryDecodeWord(charset, encoding, payload);
				output.Append(decoded ?? value.Substring(start, end + 2 - start));
				index = end + 2;
			}
			return output.ToString();
		}

		private static string TryDecodeWord(string charset, string encoding, string payload)
		{
			try
			{
				if (string.Equals(encoding, "B", StringComparison.OrdinalIgnoreCase))
				{
					return DecodeText(Convert.FromBase64String(payload), charset);
				}
				if (string.Equals(encoding, "Q", StringComparison.OrdinalIgnoreCase))
				{
					return DecodeText(DecodeQuotedPrintable(payload.Replace('_', ' ')), charset);
				}
			}
			catch (FormatException)
			{
			}
			return null;
		}

		private static string Truncate(string value, int maxChars)
		{
			if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
			{
				return value;
			}
			return value.Substring(0, maxChars) + "…";
		}
	}
}