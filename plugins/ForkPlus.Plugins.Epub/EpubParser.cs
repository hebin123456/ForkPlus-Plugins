using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace ForkPlus.Plugins.Epub
{
	/// <summary>
	/// EPUB 解析：EPUB 是 ZIP 容器（首条目 <c>mimetype</c> = <c>application/epub+zip</c>），用共享框架
	/// 内置的 <see cref="ZipArchive"/> 解开，经 <c>META-INF/container.xml</c> 找到 OPF
	/// （<c>urn:oasis:names:tc:opendocument:xmlns:container</c> 命名空间），再解析 OPF
	/// （<c>http://www.idpf.org/2007/opf</c> 命名空间，元数据走 Dublin Core
	/// <c>http://purl.org/dc/elements/1.1/</c>）的 metadata / manifest / spine。
	///
	/// 产出（见 <see cref="EpubDocument"/>）：
	/// <list type="bullet">
	/// <item>元数据 + 统计 Rows——"title"、"creator"（多个 → "creator[1]"…）、"language"、
	/// "identifier"（多个同理）、"date"、"publisher"、"description"（截断 120 字符）、"modified"
	/// （meta[@property=dcterms:modified]）、"mimetype"、"manifest items"、"spine items"、
	/// "files"、"total bytes"，键路径稳定排序。</item>
	/// <item>Chapters——spine 的 itemref 按 manifest 映射到 href / media-type；章节标题尽力而为：
	/// 优先 EPUB 3 nav（properties 含 "nav" 的 manifest 条目，收集其中所有 &lt;a href&gt; 文本），
	/// 其次 EPUB 2 NCX（navMap/navPoint/navLabel/text + content[@src]），都拿不到就空字典
	/// （章节显示 href 文件名）；体积取 zip 条目未压缩大小。</item>
	/// </list>
	///
	/// 防御：缺 container.xml / OPF / spine、单个 XML 解析失败都抛 <see cref="InvalidDataException"/>
	/// （"not a valid EPUB: …"）；zip 条目名大小写容错（不区分大小写找 mimetype /
	/// META-INF/container.xml 等）。
	///
	/// 对外入口 <see cref="Parse(byte[], out string)"/> 不抛异常：失败返回 null 并回填 error，
	/// 视图按「无法解析」提示处理。
	/// </summary>
	internal static class EpubParser
	{
		/// <summary>container.xml 的命名空间。</summary>
		private static readonly XNamespace ContainerNs = "urn:oasis:names:tc:opendocument:xmlns:container";

		/// <summary>OPF 包文档的命名空间。</summary>
		private static readonly XNamespace OpfNs = "http://www.idpf.org/2007/opf";

		/// <summary>OPF 元数据里 dc: 前缀（Dublin Core）的命名空间。</summary>
		private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";

		/// <summary>nav / NCX 之外的 XHTML 章节常见的 XHTML 命名空间（仅注释说明，解析按任意命名空间取局部名）。</summary>
		private const string EpubMediaType = "application/epub+zip";

		/// <summary>EPUB 2 的 NCX media-type。</summary>
		private const string NcxMediaType = "application/x-dtbncx+xml";

		/// <summary>description 展示截断长度。</summary>
		private const int MaxDescriptionChars = 120;

		/// <summary>成功返回文档，失败返回 null 并回填 error（error = 异常类型名 + 消息）。</summary>
		internal static EpubDocument Parse(byte[] data, out string error)
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

		// ---- 容器 → OPF ----

		private static EpubDocument ParseCore(byte[] data)
		{
			if (data == null || data.Length == 0)
			{
				throw new InvalidDataException("not a valid EPUB: empty file");
			}
			MemoryStream stream = new MemoryStream(data, false);
			// dispose 只关流：ZipArchive 释放时会顺带释放我们自建的 MemoryStream（字节数组的副本），无副作用。
			using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
			{
				// mimetype 不符不致命：照样作为一行 Meta 行加入 Rows，便于两侧对比。
				ZipArchiveEntry mimetypeEntry = FindEntry(archive, "mimetype");
				string mimetype = mimetypeEntry != null ? ReadEntryText(mimetypeEntry).Trim() : string.Empty;
				if (string.Equals(mimetype, EpubMediaType, StringComparison.Ordinal))
				{
					mimetype = EpubMediaType;
				}

				ZipArchiveEntry containerEntry = FindEntry(archive, "META-INF/container.xml");
				if (containerEntry == null)
				{
					throw new InvalidDataException("not a valid EPUB: missing META-INF/container.xml");
				}
				XDocument containerXml = LoadXml(containerEntry, "META-INF/container.xml");
				// rootfiles 下取第一个 rootfile 的 full-path 即 OPF 路径。
				string opfPath = containerXml?.Root?.Element(ContainerNs + "rootfiles")?.Element(ContainerNs + "rootfile")?.Attribute("full-path")?.Value?.Trim();
				if (string.IsNullOrEmpty(opfPath))
				{
					throw new InvalidDataException("not a valid EPUB: no rootfile full-path in container.xml");
				}
				ZipArchiveEntry opfEntry = FindEntry(archive, opfPath);
				if (opfEntry == null)
				{
					throw new InvalidDataException("not a valid EPUB: OPF not found in zip: " + opfPath);
				}
				XDocument opf = LoadXml(opfEntry, opfPath);
				return ParseOpf(archive, opf, opfPath, mimetype);
			}
		}

		// ---- OPF：metadata / manifest / spine ----

		private static EpubDocument ParseOpf(ZipArchive archive, XDocument opf, string opfPath, string mimetype)
		{
			XElement package = opf?.Root;
			if (package == null)
			{
				throw new InvalidDataException("not a valid EPUB: empty OPF document");
			}
			XElement metadata = package.Element(OpfNs + "metadata");
			XElement manifestElement = package.Element(OpfNs + "manifest");
			XElement spineElement = package.Element(OpfNs + "spine");
			if (spineElement == null)
			{
				throw new InvalidDataException("not a valid EPUB: no spine in OPF");
			}

			// manifest：id → href / media-type / properties。
			Dictionary<string, ManifestItem> manifest = new Dictionary<string, ManifestItem>(StringComparer.Ordinal);
			if (manifestElement != null)
			{
				foreach (XElement item in manifestElement.Elements(OpfNs + "item"))
				{
					string id = item.Attribute("id")?.Value;
					if (string.IsNullOrEmpty(id) || manifest.ContainsKey(id))
					{
						continue;
					}
					manifest.Add(id, new ManifestItem
					{
						Href = item.Attribute("href")?.Value ?? string.Empty,
						MediaType = item.Attribute("media-type")?.Value ?? string.Empty,
						Properties = item.Attribute("properties")?.Value ?? string.Empty
					});
				}
			}

			string opfDir = DirectoryOf(opfPath);

			// 章节标题字典：href（zip 根相对，去锚点）→ 标题。
			Dictionary<string, string> titles = LoadChapterTitles(archive, manifest, spineElement, opfDir);

			EpubDocument document = new EpubDocument();
			if (metadata != null)
			{
				document.Title = Clean(metadata.Element(DcNs + "title")?.Value);
			}

			// spine：itemref 顺序即阅读顺序；经 manifest 映射到 href / media-type；
			// 非 XHTML 的 spine 项也列出（media-type 照实显示）；linear="no" 保留但标注。
			foreach (XElement itemref in spineElement.Elements(OpfNs + "itemref"))
			{
				string idref = itemref.Attribute("idref")?.Value;
				if (string.IsNullOrEmpty(idref) || !manifest.TryGetValue(idref, out ManifestItem item))
				{
					continue;
				}
				string href = ResolvePath(opfDir, item.Href);
				ZipArchiveEntry entry = FindEntry(archive, href);
				string title;
				titles.TryGetValue(href, out title);
				document.Chapters.Add(new EpubChapter
				{
					Index = document.Chapters.Count + 1,
					Href = href,
					Title = title,
					MediaType = item.MediaType,
					SizeBytes = entry != null ? entry.Length : 0L,
					Linear = !string.Equals(itemref.Attribute("linear")?.Value, "no", StringComparison.OrdinalIgnoreCase)
				});
			}

			// 先统计（files / total bytes 两行要用），再组装 Rows。
			document.FileCount = archive.Entries.Count;
			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				document.TotalBytes += entry.Length;
			}
			BuildRows(document, metadata, manifest, mimetype);
			return document;
		}

		/// <summary>组装元数据 / 统计 Rows（键路径稳定排序，见类注释的清单顺序）。</summary>
		private static void BuildRows(EpubDocument document, XElement metadata, Dictionary<string, ManifestItem> manifest, string mimetype)
		{
			List<EpubRow> rows = document.Rows;
			// mimetype 永远占一行（值可能为空 / 不符），便于两侧对比。
			rows.Add(new EpubRow { Path = "mimetype", Value = mimetype });
			if (metadata != null)
			{
				AddValue(rows, "title", Clean(metadata.Element(DcNs + "title")?.Value));
				AddList(rows, "creator", metadata.Elements(DcNs + "creator"));
				AddValue(rows, "language", Clean(metadata.Element(DcNs + "language")?.Value));
				AddList(rows, "identifier", metadata.Elements(DcNs + "identifier"));
				AddValue(rows, "date", Clean(metadata.Element(DcNs + "date")?.Value));
				AddValue(rows, "publisher", Clean(metadata.Element(DcNs + "publisher")?.Value));
				AddValue(rows, "description", Truncate(Clean(metadata.Element(DcNs + "description")?.Value), MaxDescriptionChars));
				// EPUB 3 的最后修改时间在 meta[@property="dcterms:modified"]。
				string modified = null;
				foreach (XElement meta in metadata.Elements(OpfNs + "meta"))
				{
					if (string.Equals(meta.Attribute("property")?.Value, "dcterms:modified", StringComparison.Ordinal))
					{
						modified = Clean(meta.Value);
						break;
					}
				}
				AddValue(rows, "modified", modified);
			}
			rows.Add(new EpubRow { Path = "manifest items", Value = manifest.Count.ToString(CultureInfo.InvariantCulture) });
			rows.Add(new EpubRow { Path = "spine items", Value = document.Chapters.Count.ToString(CultureInfo.InvariantCulture) });
			rows.Add(new EpubRow { Path = "files", Value = document.FileCount.ToString(CultureInfo.InvariantCulture) });
			rows.Add(new EpubRow { Path = "total bytes", Value = document.TotalBytes.ToString("N0", CultureInfo.InvariantCulture) });
		}

		/// <summary>单值字段：有值才占一行（缺省不占，让 diff 把「一侧有一侧无」显成 left/right only）。</summary>
		private static void AddValue(List<EpubRow> rows, string path, string value)
		{
			if (!string.IsNullOrEmpty(value))
			{
				rows.Add(new EpubRow { Path = path, Value = value });
			}
		}

		/// <summary>多值字段：1 个用裸键（"creator"），多个用 "creator[1]"…"creator[n]"。</summary>
		private static void AddList(List<EpubRow> rows, string path, IEnumerable<XElement> elements)
		{
			List<string> values = new List<string>();
			foreach (XElement element in elements)
			{
				string value = Clean(element.Value);
				if (!string.IsNullOrEmpty(value))
				{
					values.Add(value);
				}
			}
			if (values.Count == 1)
			{
				AddValue(rows, path, values[0]);
				return;
			}
			for (int i = 0; i < values.Count; i++)
			{
				AddValue(rows, path + "[" + (i + 1).ToString(CultureInfo.InvariantCulture) + "]", values[i]);
			}
		}

		// ---- 章节标题（nav / NCX，尽力而为） ----

		/// <summary>
		/// 收集 href（zip 根相对，去锚点）→ 标题 字典：优先 EPUB 3 nav（properties 含 "nav" 的条目，
		/// 解析其中所有 &lt;a href&gt; 的文本），其次 EPUB 2 NCX（spine@toc 或 media-type 指向的条目，
		/// 解析 navMap/navPoint）。都拿不到就空字典。
		/// </summary>
		private static Dictionary<string, string> LoadChapterTitles(ZipArchive archive, Dictionary<string, ManifestItem> manifest, XElement spineElement, string opfDir)
		{
			// EPUB 3：properties 含 "nav" 的 manifest 条目（目录页）。
			foreach (KeyValuePair<string, ManifestItem> pair in manifest)
			{
				if (HasProperty(pair.Value.Properties, "nav"))
				{
					Dictionary<string, string> navTitles = LoadNavTitles(archive, ResolvePath(opfDir, pair.Value.Href));
					if (navTitles != null)
					{
						return navTitles;
					}
				}
			}
			// EPUB 2：spine@toc 指向的条目，或 media-type 为 NCX 的条目。
			string tocId = spineElement.Attribute("toc")?.Value;
			if (!string.IsNullOrEmpty(tocId) && manifest.TryGetValue(tocId, out ManifestItem tocItem))
			{
				Dictionary<string, string> ncxTitles = LoadNcxTitles(archive, ResolvePath(opfDir, tocItem.Href));
				if (ncxTitles != null)
				{
					return ncxTitles;
				}
			}
			foreach (KeyValuePair<string, ManifestItem> pair in manifest)
			{
				if (string.Equals(pair.Value.MediaType, NcxMediaType, StringComparison.OrdinalIgnoreCase))
				{
					Dictionary<string, string> ncxTitles = LoadNcxTitles(archive, ResolvePath(opfDir, pair.Value.Href));
					if (ncxTitles != null)
					{
						return ncxTitles;
					}
				}
			}
			return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>EPUB 3 nav 文档：收集所有 &lt;a href&gt; 文本 → href(去锚点) → 标题；解析失败返回 null。</summary>
		private static Dictionary<string, string> LoadNavTitles(ZipArchive archive, string navPath)
		{
			ZipArchiveEntry entry = FindEntry(archive, navPath);
			if (entry == null)
			{
				return null;
			}
			try
			{
				XDocument nav = LoadXml(entry, navPath);
				if (nav?.Root == null)
				{
					return null;
				}
				Dictionary<string, string> titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (XElement anchor in nav.DescendsLocalName("a"))
				{
					string href = anchor.Attribute("href")?.Value;
					if (string.IsNullOrEmpty(href))
					{
						continue;
					}
					string title = Clean(anchor.Value);
					if (string.IsNullOrEmpty(title))
					{
						continue;
					}
					string target = ResolvePath(DirectoryOf(navPath), StripAnchor(href));
					if (!titles.ContainsKey(target))
					{
						titles.Add(target, title);
					}
				}
				return titles;
			}
			catch (InvalidDataException)
			{
				// 单个目录页解析失败不致命：回退 NCX / 空字典。
				return null;
			}
		}

		/// <summary>EPUB 2 NCX：navMap/navPoint 的 navLabel/text 与 content[@src] 配对；解析失败返回 null。</summary>
		private static Dictionary<string, string> LoadNcxTitles(ZipArchive archive, string ncxPath)
		{
			ZipArchiveEntry entry = FindEntry(archive, ncxPath);
			if (entry == null)
			{
				return null;
			}
			try
			{
				XDocument ncx = LoadXml(entry, ncxPath);
				if (ncx?.Root == null)
				{
					return null;
				}
				Dictionary<string, string> titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (XElement navPoint in ncx.DescendsLocalName("navPoint"))
				{
					string title = Clean(navPoint.ElementLocalName("navLabel")?.ElementLocalName("text")?.Value);
					string src = navPoint.ElementLocalName("content")?.Attribute("src")?.Value;
					if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(src))
					{
						continue;
					}
					string target = ResolvePath(DirectoryOf(ncxPath), StripAnchor(src));
					if (!titles.ContainsKey(target))
					{
						titles.Add(target, title);
					}
				}
				return titles;
			}
			catch (InvalidDataException)
			{
				return null;
			}
		}

		// ---- zip / 路径 / XML 小工具 ----

		/// <summary>manifest 条目的三要素。</summary>
		private sealed class ManifestItem
		{
			internal string Href;

			internal string MediaType;

			/// <summary>空格分隔的 properties（如 "nav"、"cover-image"）。</summary>
			internal string Properties;
		}

		/// <summary>zip 条目名大小写容错：不区分大小写按全名匹配（分隔符统一成 '/'）。</summary>
		private static ZipArchiveEntry FindEntry(ZipArchive archive, string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}
			string wanted = NormalizeZipPath(path);
			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				if (string.Equals(NormalizeZipPath(entry.FullName), wanted, StringComparison.OrdinalIgnoreCase))
				{
					return entry;
				}
			}
			return null;
		}

		private static string NormalizeZipPath(string path)
		{
			return (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
		}

		/// <summary>读 zip 条目全文（UTF-8，容 BOM）；mimetype 等要求未压缩原文。</summary>
		private static string ReadEntryText(ZipArchiveEntry entry)
		{
			using (Stream stream = entry.Open())
			using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
			{
				return reader.ReadToEnd();
			}
		}

		/// <summary>解析 XML；失败抛 InvalidDataException（"not a valid EPUB: …"）。</summary>
		private static XDocument LoadXml(ZipArchiveEntry entry, string name)
		{
			try
			{
				using (Stream stream = entry.Open())
				{
					return XDocument.Load(stream, LoadOptions.None);
				}
			}
			catch (Exception ex)
			{
				throw new InvalidDataException("not a valid EPUB: failed to parse XML '" + name + "': " + ex.Message);
			}
		}

		/// <summary>href / src 去掉 '#锚点' 部分。</summary>
		private static string StripAnchor(string href)
		{
			if (string.IsNullOrEmpty(href))
			{
				return href;
			}
			int hash = href.IndexOf('#');
			return hash >= 0 ? href.Substring(0, hash) : href;
		}

		/// <summary>取 zip 根相对路径的目录部分（不含末尾 '/'；根目录为空串）。</summary>
		private static string DirectoryOf(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return string.Empty;
			}
			int slash = path.LastIndexOf('/');
			return slash < 0 ? string.Empty : path.Substring(0, slash);
		}

		/// <summary>把 OPF（或 nav / NCX）目录与相对 href 合成 zip 根相对路径，处理 "." / ".." 段。</summary>
		private static string ResolvePath(string baseDir, string href)
		{
			if (string.IsNullOrEmpty(href))
			{
				return NormalizeZipPath(baseDir);
			}
			string combined = href.Replace('\\', '/');
			if (combined.Length > 0 && combined[0] == '/')
			{
				return NormalizeZipPath(combined);
			}
			if (!string.IsNullOrEmpty(baseDir))
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
			return string.Join("/", parts);
		}

		/// <summary>properties（空格分隔）里是否含某个词（如 "nav"）。</summary>
		private static bool HasProperty(string properties, string word)
		{
			if (string.IsNullOrEmpty(properties))
			{
				return false;
			}
			foreach (string token in properties.Split(' '))
			{
				if (string.Equals(token.Trim(), word, StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>展示值清洗：折叠空白为单空格（XML 值常带换行 / 缩进）。</summary>
		private static string Clean(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return null;
			}
			StringBuilder builder = new StringBuilder(value.Length);
			bool previousSpace = false;
			foreach (char c in value)
			{
				if (char.IsWhiteSpace(c))
				{
					if (!previousSpace && builder.Length > 0)
					{
						builder.Append(' ');
					}
					previousSpace = true;
					continue;
				}
				previousSpace = false;
				builder.Append(c);
			}
			string result = builder.ToString().Trim();
			return result.Length == 0 ? null : result;
		}

		/// <summary>超长展示值截断（如 description）。</summary>
		private static string Truncate(string value, int maxChars)
		{
			if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
			{
				return value;
			}
			return value.Substring(0, maxChars) + "…";
		}
	}

	/// <summary>XDocument 上的「按局部名取元素」小工具：nav / NCX 的命名空间版本混杂（XHTML 1.x / XHTML5 /
	/// 无命名空间），目录提取按局部名匹配更稳。</summary>
	internal static class XmlLocalNameExtensions
	{
		/// <summary>文档里所有局部名为 <paramref name="localName"/> 的后代元素（不区分命名空间）。</summary>
		internal static IEnumerable<XElement> DescendsLocalName(this XDocument document, string localName)
		{
			foreach (XElement element in document.Descendants())
			{
				if (string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal))
				{
					yield return element;
				}
			}
		}

		/// <summary>元素的第一个局部名匹配的直接子元素（不区分命名空间）。</summary>
		internal static XElement ElementLocalName(this XElement element, string localName)
		{
			foreach (XElement child in element.Elements())
			{
				if (string.Equals(child.Name.LocalName, localName, StringComparison.Ordinal))
				{
					return child;
				}
			}
			return null;
		}
	}
}
