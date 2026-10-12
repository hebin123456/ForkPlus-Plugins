using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using ForkPlus.Plugins.Ui.HtmlPreview;

namespace ForkPlus.Plugins.Epub
{
	/// <summary>
	/// 把 EPUB 的首个阅读章节组装成**自包含 HTML**（供「预览」模式并排双 WebView 渲染）。
	///
	/// 取 spine 里第一个 linear 章节（缺失回退首个章节），从 ZIP 容器里读出其 XHTML，按
	/// 声明的编码解码，再用共享的 <see cref="HtmlResourceInliner"/> 把页面引用的图片 / 样式 /
	/// 字体换成 data URI——内容与资源都取自 EPUB 自身，无需注册自定义协议，WebView
	/// <c>NavigateToString</c> 即可正确显示。章节链接（<c>.xhtml/.html</c>）不内联，保持可读的 href。
	///
	/// 尽力而为：编码不在 .NET 内置集（如 GBK）或引用的资源在 ZIP 里找不到时，退化为原样 /
	/// 保留原引用，不抛异常（返回 null 表示该侧无章节可渲染，视图显示占位）。
	/// </summary>
	internal static class EpubHtmlComposer
	{
		/// <summary>组装一侧的自包含 HTML；无章节 / 无内容返回 null。</summary>
		internal static string Compose(EpubDocument document)
		{
			if (document?.Raw == null || document.Chapters == null || document.Chapters.Count == 0)
			{
				return null;
			}
			EpubChapter chapter = ResolveFirstChapter(document);
			if (chapter == null || string.IsNullOrEmpty(chapter.Href))
			{
				return null;
			}
			try
			{
				MemoryStream stream = new MemoryStream(document.Raw, false);
				// dispose 只关流：ZipArchive 释放时会顺带释放我们自建的 MemoryStream，无副作用。
				using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
				{
					ZipArchiveEntry entry = EpubParser.FindEntry(archive, chapter.Href);
					if (entry == null)
					{
						return null;
					}
					byte[] bytes = ReadAllBytes(entry);
					if (bytes == null)
					{
						return null;
					}
					string html = DecodeHtml(bytes);
					string baseDir = EpubParser.DirectoryOf(chapter.Href);
					return HtmlResourceInliner.Inline(html, url => Resolve(archive, baseDir, url));
				}
			}
			catch (Exception)
			{
				// 预览组装尽力而为：容器损坏 / 编码异常都退化为「无内容」（该侧显示占位）。
				return null;
			}
		}

		/// <summary>首个阅读章节：spine 里第一个 linear（linear != "no"）章节 &gt; 第一个章节 &gt; null。</summary>
		private static EpubChapter ResolveFirstChapter(EpubDocument document)
		{
			foreach (EpubChapter chapter in document.Chapters)
			{
				if (chapter.Linear)
				{
					return chapter;
				}
			}
			return document.Chapters[0];
		}

		/// <summary>解析一条引用（相对章节路径）为 ZIP 内条目的 data URI；章节链接 / 查不到返回 null。</summary>
		private static HtmlResourceInliner.InlinedResource Resolve(ZipArchive archive, string baseDir, string url)
		{
			string clean = StripQueryAndAnchor(url);
			if (string.IsNullOrEmpty(clean) || IsChapterLink(clean))
			{
				return null; // 章节链接不内联，保持可读的 href
			}
			string path = EpubParser.ResolvePath(baseDir, clean);
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}
			ZipArchiveEntry entry = EpubParser.FindEntry(archive, path);
			if (entry == null || entry.Length == 0)
			{
				return null;
			}
			byte[] bytes = ReadAllBytes(entry);
			return bytes == null ? null : new HtmlResourceInliner.InlinedResource(bytes, null);
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

		/// <summary>是否为章节文档链接（不内联）：.xhtml / .html / .htm / .xml / .opf / .ncx。</summary>
		private static bool IsChapterLink(string path)
		{
			string ext;
			try
			{
				ext = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
			}
			catch (Exception)
			{
				ext = string.Empty;
			}
			switch (ext)
			{
				case ".xhtml":
				case ".html":
				case ".htm":
				case ".xml":
				case ".opf":
				case ".ncx":
					return true;
				default:
					return false;
			}
		}

		private static byte[] ReadAllBytes(ZipArchiveEntry entry)
		{
			try
			{
				using (Stream stream = entry.Open())
				using (MemoryStream buffer = new MemoryStream())
				{
					stream.CopyTo(buffer);
					return buffer.ToArray();
				}
			}
			catch (Exception)
			{
				return null;
			}
		}

		// ---- 解码 ----

		/// <summary>按 BOM &gt; 页面声明编码（charset / encoding）&gt; UTF-8 的顺序解码 XHTML 字节。</summary>
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
				try
				{
					return Encoding.GetEncoding(name).GetString(bytes);
				}
				catch (Exception)
				{
					// 该编码不在内置集（GBK / windows-1252 需 CodePages 提供程序）：回退 UTF-8。
				}
			}
			return Encoding.UTF8.GetString(bytes);
		}

		/// <summary>从前 2 KB 的 ASCII 区里嗅探 charset=xxx / encoding="xxx" 的值；没有返回 null。</summary>
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
			string name = ValueAfter(head, "charset");
			if (string.IsNullOrEmpty(name))
			{
				// EPUB 的 XHTML 常在 <?xml ... encoding="UTF-8"?> 里声明编码。
				name = ValueAfter(head, "encoding");
			}
			return name;
		}

		/// <summary>取 ASCII 头里 keyword 之后「= 」后面的编码名（去引号）；没有返回 null。</summary>
		private static string ValueAfter(string head, string keyword)
		{
			int index = head.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
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
