using System;
using System.Collections.Generic;
using System.Text;
using ForkPlus.Plugins.Ui.HtmlPreview;

namespace ForkPlus.Plugins.Chm
{
	/// <summary>
	/// 把 CHM 的默认主题页组装成**自包含 HTML**（供「预览」模式并排双 WebView 渲染）。
	///
	/// 取 <c>#SYSTEM</c> 记录 2 的默认主题（缺失时回退第一个 <c>.htm/.html</c> 条目），按页面
	/// 声明的编码解码，再用共享的 <see cref="HtmlResourceInliner"/> 把页面引用的图片 / 样式 /
	/// 脚本换成 data URI——内容与资源都取自 CHM 自身，无需注册自定义协议，WebView
	/// <c>NavigateToString</c> 即可正确显示。文档链接（<c>.htm/.html</c>）不内联，保持可读的 href。
	///
	/// 内联受预算约束：Windows WebView2 的 <c>NavigateToString</c> 有 2MB 硬限制，超限导航直接
	/// 失败且无任何提示（预览区一片空白）。因此只内联网页资源（图片 / 样式 / 脚本 / 字体 / 媒体，
	/// 排除 exe / zip / pdf 等附件），并按 base64 膨胀记账，逼近预算的资源保留原引用——页面其余
	/// 部分照常渲染，宁可图片缺失也不让整页挂掉。
	///
	/// 尽力而为：编码不可解析（拼错 / 极冷门）或引用的资源在 CHM 里找不到时，退化为
	/// ANSI 回退解码 / 保留原引用，不抛异常（返回 null 表示该侧无默认主题可渲染，视图显示占位）。
	/// </summary>
	internal static class ChmHtmlComposer
	{
		/// <summary>组装一侧的自包含 HTML；无默认主题 / 无内容返回 null。</summary>
		internal static string Compose(ChmDocument document)
		{
			if (document == null)
			{
				return null;
			}
			ChmEntry topic = ResolveTopic(document);
			if (topic == null)
			{
				return null;
			}
			byte[] bytes = ChmParser.GetEntryBytes(document, topic.Path);
			if (bytes == null)
			{
				return null;
			}
			string html = DecodeHtml(bytes);
			// 字节已按页面声明解码成正确文本，声明必须同步改写为 utf-8：WebView 的
			// NavigateToString 按自身规则装载文档，若页面仍声明 gb2312 等旧编码，
			// 可能按声明把已是 UTF-8 的字节再解码一次 → 乱码回潮。
			html = ForceUtf8Declaration(html);
			string baseDir = DirectoryOf(topic.Path);
			InlineBudget budget = new InlineBudget(html.Length);
			return HtmlResourceInliner.Inline(html, url => Resolve(document, baseDir, url, budget));
		}

		/// <summary>默认主题页：<c>#SYSTEM</c> 记录 2 &gt; 第一个 HTML 文件条目；都没有返回 null。</summary>
		private static ChmEntry ResolveTopic(ChmDocument document)
		{
			string topic = document.DefaultTopic;
			if (!string.IsNullOrEmpty(topic))
			{
				ChmEntry entry = ChmParser.FindEntry(document, topic);
				if (entry != null && !entry.IsDirectory)
				{
					return entry;
				}
			}
			foreach (ChmEntry entry in document.Entries)
			{
				if (!entry.IsDirectory && IsHtmlPath(entry.Path))
				{
					return entry;
				}
			}
			return null;
		}

		/// <summary>解析一条引用（相对默认主题页目录）为 CHM 内条目的 data URI；HTML 链接 /
		/// 非网页资源 / 超内联预算 / 查不到返回 null。</summary>
		private static HtmlResourceInliner.InlinedResource Resolve(ChmDocument document, string baseDir, string url, InlineBudget budget)
		{
			string clean = StripQueryAndAnchor(url);
			if (string.IsNullOrEmpty(clean) || IsHtmlPath(clean) || !IsWebResourcePath(clean))
			{
				return null; // 文档链接 / 附件等非网页资源不内联，保持可读的 href
			}
			string path = Combine(baseDir, clean);
			if (path == null)
			{
				return null;
			}
			ChmEntry entry = ChmParser.FindEntry(document, path);
			if (entry == null || entry.IsDirectory)
			{
				return null;
			}
			byte[] bytes = ChmParser.GetEntryBytes(document, entry.Path);
			if (bytes == null || !budget.TryReserve(bytes.LongLength))
			{
				return null; // 超预算：保留原引用，保住整页可渲染
			}
			return new HtmlResourceInliner.InlinedResource(bytes, null);
		}

		/// <summary>
		/// 内联预算：自包含文档的字符数必须低于 Windows WebView2 <c>NavigateToString</c> 的
		/// 2MB 硬限制，否则导航静默失败、预览一片空白。base64 内联按 4/3 膨胀计入；
		/// 超预算的资源不再内联（保留原引用）。
		/// </summary>
		private sealed class InlineBudget
		{
			// 目标 90 万字符：内联产物（base64 / HTML）以 ASCII 为主，字符数与 UTF-16 字节数
			// 两条口径都稳低于 2MB 限制。
			private const long MaxChars = 900_000;

			private long _usedChars;

			public InlineBudget(long initialChars)
			{
				_usedChars = initialChars;
			}

			/// <summary>预估把 data（base64 后）内联进文档是否仍在预算内；在则记账并返回 true。</summary>
			public bool TryReserve(long dataBytes)
			{
				long chars = (dataBytes * 4 + 2) / 3 + 64; // base64 膨胀 + "data:…;base64," 前缀开销
				if (_usedChars + chars > MaxChars)
				{
					return false;
				}
				_usedChars += chars;
				return true;
			}
		}

		// ---- 路径 ----

		/// <summary>取内部路径的目录部分（保留前导 '/'；根目录为空串）。</summary>
		private static string DirectoryOf(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return string.Empty;
			}
			int slash = path.LastIndexOf('/');
			return slash <= 0 ? string.Empty : path.Substring(0, slash);
		}

		/// <summary>把目录 + 相对引用合成 CHM 内部绝对路径（'/' 起头，处理 "." / ".." 段）；空返回 null。</summary>
		private static string Combine(string baseDir, string href)
		{
			string combined = href.Replace('\\', '/');
			if (combined.Length == 0)
			{
				return null;
			}
			if (combined[0] != '/' && !string.IsNullOrEmpty(baseDir))
			{
				combined = baseDir + "/" + combined;
			}
			List<string> parts = new List<string>();
			foreach (string segment in combined.Split('/'))
			{
				if (segment.Length == 0 || segment == ".")
				{
					continue;
				}
				if (segment == "..")
				{
					if (parts.Count > 0)
					{
						parts.RemoveAt(parts.Count - 1);
					}
					continue;
				}
				parts.Add(segment);
			}
			return parts.Count == 0 ? null : "/" + string.Join("/", parts);
		}

		/// <summary>去掉 URL 的查询串与锚点。</summary>
		private static string StripQueryAndAnchor(string url)
		{
			if (string.IsNullOrEmpty(url))
			{
				return url;
			}
			int cut = url.Length;
			int hash = url.IndexOf('#');
			if (hash >= 0 && hash < cut)
			{
				cut = hash;
			}
			int query = url.IndexOf('?');
			if (query >= 0 && query < cut)
			{
				cut = query;
			}
			return url.Substring(0, cut).Trim();
		}

		private static bool IsHtmlPath(string path)
		{
			string ext;
			try
			{
				ext = System.IO.Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
			}
			catch (Exception)
			{
				ext = string.Empty;
			}
			return ext == ".htm" || ext == ".html" || ext == ".xhtml";
		}

		/// <summary>是否为可内联的网页资源（图片 / 样式 / 脚本 / 字体 / 媒体）。exe / zip / pdf
		/// 等附件链接不内联，避免 data URI 把文档撑爆预算。</summary>
		private static bool IsWebResourcePath(string path)
		{
			string ext;
			try
			{
				ext = System.IO.Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
			}
			catch (Exception)
			{
				return false;
			}
			switch (ext)
			{
				case ".png":
				case ".jpg":
				case ".jpeg":
				case ".gif":
				case ".webp":
				case ".bmp":
				case ".svg":
				case ".ico":
				case ".avif":
				case ".apng":
				case ".css":
				case ".js":
				case ".mjs":
				case ".woff":
				case ".woff2":
				case ".ttf":
				case ".otf":
				case ".eot":
				case ".mp3":
				case ".wav":
				case ".ogg":
				case ".mp4":
				case ".webm":
				case ".m4v":
					return true;
				default:
					return false;
			}
		}

		// ---- 解码 ----

		/// <summary>按 BOM &gt; 页面声明编码 &gt;（严格 UTF-8 → ANSI 回退）的顺序解码 HTML 字节。
		/// 页面声明的 gb2312 / gbk / big5 / shift_jis 等经 CodePages 提供程序解析（此前不在
		/// .NET 默认编码集，声明 GBK 的中文页面被错误地按 UTF-8 解码成乱码）；无声明时按
		/// 「严格 UTF-8 失败回退系统 ANSI 代码页」（无 meta 的 GBK 页面同此恢复）。</summary>
		private static string DecodeHtml(byte[] bytes)
		{
			if (bytes == null || bytes.Length == 0)
			{
				return string.Empty;
			}
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
			string name = SniffCharset(bytes);
			if (!string.IsNullOrEmpty(name))
			{
				Encoding declared = ChmTextEncoding.TryGetEncoding(name);
				if (declared != null)
				{
					return declared.GetString(bytes);
				}
				// 声明的编码不可解析（拼错 / 极冷门）：落到下面的探测回退，而不是无脑 UTF-8。
			}
			return ChmTextEncoding.Decode(bytes, 0, bytes.Length);
		}

		/// <summary>把 HTML 里的编码声明统一改写为 utf-8（&lt;meta charset=…&gt;、
		/// &lt;meta http-equiv=Content-Type content="…; charset=…"&gt;、&lt;?xml encoding=…?&gt;）。
		/// 字节已按原声明解码成正确的 .NET 字符串，再交给 WebView 之前声明必须与实际一致。</summary>
		private static string ForceUtf8Declaration(string html)
		{
			if (string.IsNullOrEmpty(html))
			{
				return html;
			}
			string result = MetaCharsetPattern.Replace(html, "${prefix}utf-8");
			return XmlEncodingPattern.Replace(result, "${prefix}utf-8");
		}

		/// <summary>匹配 meta 里的 charset 声明（HTML 4 的 http-equiv=Content-Type 与 HTML 5 的
		/// charset= 两种形态都在内；捕获组 prefix 保留声明之前的全部原文，只替换编码名）。</summary>
		private static readonly System.Text.RegularExpressions.Regex MetaCharsetPattern = new System.Text.RegularExpressions.Regex(
			"(?<prefix><meta[^>]*?charset\\s*=\\s*[\"']?)(?<charset>[^\"'\\s;>/]+)",
			System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

		/// <summary>匹配 XHTML 的 &lt;?xml version="…" encoding="…" ?&gt; 声明（只替换 encoding 值）。</summary>
		private static readonly System.Text.RegularExpressions.Regex XmlEncodingPattern = new System.Text.RegularExpressions.Regex(
			"(?<prefix><\\?xml[^>]*?encoding\\s*=\\s*[\"']?)(?<encoding>[^\"'\\s]+)",
			System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

		/// <summary>从前 2 KB 的 ASCII 区里嗅探 charset=xxx 的值；没有返回 null。</summary>
		private static string SniffCharset(byte[] bytes)
		{
			int scan = Math.Min(bytes.Length, 2048);
			StringBuilder ascii = new StringBuilder(scan);
			for (int i = 0; i < scan; i++)
			{
				byte b = bytes[i];
				ascii.Append(b < 0x80 ? (char)b : ' ');
			}
			string head = ascii.ToString();
			int index = head.IndexOf("charset", StringComparison.OrdinalIgnoreCase);
			if (index < 0)
			{
				return null;
			}
			int equals = head.IndexOf('=', index);
			if (equals < 0)
			{
				return null;
			}
			int start = equals + 1;
			while (start < head.Length && (head[start] == ' ' || head[start] == '"' || head[start] == '\''))
			{
				start++;
			}
			int end = start;
			while (end < head.Length && (char.IsLetterOrDigit(head[end]) || head[end] == '-' || head[end] == '_' || head[end] == '.'))
			{
				end++;
			}
			return end > start ? head.Substring(start, end - start) : null;
		}
	}
}
