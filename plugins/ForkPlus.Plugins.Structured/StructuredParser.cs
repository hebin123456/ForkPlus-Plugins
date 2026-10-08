using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using Tomlyn;
using Tomlyn.Model;
using YamlDotNet.RepresentationModel;

namespace ForkPlus.Plugins.Structured
{
	/// <summary>
	/// 把五种格式的文本各解析成一棵统一的 <see cref="DataNode"/> 树。
	///
	/// 分工：
	/// <list type="bullet">
	/// <item>JSON / JSONC —— <c>System.Text.Json</c>（<c>JsonDocument</c>，允许注释与尾逗号）；</item>
	/// <item>YAML —— YamlDotNet 的表示模型（保序）；多文档（<c>---</c> 分隔）跳过空文档后按
	/// 非空序号合成 <c>doc[0]</c>、<c>doc[1]</c>…，单文档保持原样；</item>
	/// <item>TOML —— Tomlyn 的 <c>ToModel</c>，表与数组表递归转换，键按名排序保证顺序稳定；</item>
	/// <item>XML —— <c>System.Xml.Linq</c>：元素为键、属性前缀 <c>@</c>、文本节点为 <c>#text</c>，
	/// 同名子元素按出现次序加 <c>[i]</c>；</item>
	/// <item>INI / CFG / properties —— 自写解析：<c>[节]</c> 嵌套成映射，<c>键=值</c> / <c>键: 值</c>
	/// 支持，<c>;</c> <c>#</c> <c>!</c> 注释与反斜杠续行。</item>
	/// </list>
	///
	/// 解析失败不抛异常：返回 <c>null</c> 并回填 <paramref name="error"/>，视图按「无法解析」提示处理。
	/// </summary>
	internal static class StructuredParser
	{
	/// <summary>按扩展名分派解析器。成功返回数据树，失败返回 null 并回填 error。
	/// 扩展名带不带前导点都接受（视图侧 FormatOf 产出无点形式，此处归一成小写含点）。</summary>
	internal static DataNode Parse(string text, string extension, out string error)
	{
		error = null;
		if (text == null)
		{
			text = string.Empty;
		}
		// 去 BOM：UTF-8 BOM 会让 JSON / TOML 解析器在首字符报错。
		if (text.Length > 0 && text[0] == '\uFEFF')
		{
			text = text.Substring(1);
		}
		// 空内容（新建空文件等）按空文档处理，给空树占位而不是解析报错。
		if (text.Trim().Length == 0)
		{
			return DataNode.Map();
		}
		try
		{
			string ext = (extension ?? string.Empty).Trim().ToLowerInvariant();
			if (ext.Length > 0 && ext[0] != '.')
			{
				ext = "." + ext;
			}
			switch (ext)
			{
				case ".json":
				case ".jsonc":
					return ParseJson(text);
				case ".yaml":
				case ".yml":
					return ParseYaml(text);
				case ".toml":
					return ParseToml(text);
				case ".xml":
					return ParseXml(text);
				case ".ini":
				case ".cfg":
				case ".properties":
					return ParseIni(text);
				default:
					error = "unsupported format";
					return null;
				}
			}
			catch (Exception ex)
			{
				error = ex.GetType().Name + ": " + ex.Message;
				return null;
			}
		}

		// ---- JSON / JSONC ----

		private static DataNode ParseJson(string text)
		{
			JsonDocumentOptions options = new JsonDocumentOptions
			{
				CommentHandling = JsonCommentHandling.Skip,
				AllowTrailingCommas = true,
				MaxDepth = 128
			};
			using (JsonDocument document = JsonDocument.Parse(text, options))
			{
				return FromJson(document.RootElement);
			}
		}

		private static DataNode FromJson(JsonElement element)
		{
			switch (element.ValueKind)
			{
			case JsonValueKind.Object:
			{
				DataNode map = DataNode.Map();
				foreach (JsonProperty property in element.EnumerateObject())
				{
					map.Add(property.Name, FromJson(property.Value));
				}
				return map;
			}
			case JsonValueKind.Array:
			{
				DataNode seq = DataNode.Seq();
				foreach (JsonElement item in element.EnumerateArray())
				{
					seq.Add(FromJson(item));
				}
				return seq;
			}
			case JsonValueKind.String:
				return DataNode.Scalar(element.GetString());
			case JsonValueKind.Number:
				return DataNode.Scalar(element.GetRawText());
			case JsonValueKind.True:
				return DataNode.Scalar("true");
			case JsonValueKind.False:
				return DataNode.Scalar("false");
			default:
				return DataNode.Scalar("null");
			}
		}

		// ---- YAML ----

		/// <summary>多文档 YAML（<c>---</c> 分隔，如 cert-manager.crds.yaml 一类的 CRD 合集）
		/// 不能只取首文档——首个常是纯注释段（表示模型里是 null 标量），实际内容全在后续文档里。
		/// 这里跳过空文档后按「非空序号」合成 <c>doc[0]</c>、<c>doc[1]</c>…：注释段增删不影响
		/// 两侧文档序号对齐，diff 语义稳定；单文档文件保持原样不加包装。</summary>
		private static DataNode ParseYaml(string text)
		{
			YamlStream stream = new YamlStream();
			using (StringReader reader = new StringReader(text))
			{
				stream.Load(reader);
			}
			List<DataNode> docs = new List<DataNode>();
			foreach (YamlDocument document in stream.Documents)
			{
				DataNode node = FromYaml(document.RootNode);
				if (node.Kind == DataKind.Scalar && string.IsNullOrEmpty(node.Value))
				{
					continue;
				}
				docs.Add(node);
			}
			if (docs.Count == 0)
			{
				return DataNode.Map();
			}
			if (docs.Count == 1)
			{
				return docs[0];
			}
			DataNode root = DataNode.Map();
			for (int index = 0; index < docs.Count; index++)
			{
				root.Add("doc[" + index + "]", docs[index]);
			}
			return root;
		}

		private static DataNode FromYaml(YamlNode node)
		{
			if (node is YamlMappingNode mapping)
			{
				DataNode map = DataNode.Map();
				foreach (KeyValuePair<YamlNode, YamlNode> pair in mapping.Children)
				{
					map.Add(YamlText(pair.Key), FromYaml(pair.Value));
				}
				return map;
			}
			if (node is YamlSequenceNode sequence)
			{
				DataNode seq = DataNode.Seq();
				foreach (YamlNode item in sequence.Children)
				{
					seq.Add(FromYaml(item));
				}
				return seq;
			}
			return DataNode.Scalar(YamlText(node));
		}

		private static string YamlText(YamlNode node)
		{
			if (node is YamlScalarNode scalar)
			{
				return scalar.Value ?? string.Empty;
			}
			return node?.ToString() ?? string.Empty;
		}

		// ---- TOML ----

		private static DataNode ParseToml(string text)
		{
			TomlTable table = Toml.ToModel(text);
			return FromTomlTable(table);
		}

		private static DataNode FromTomlTable(TomlTable table)
		{
			DataNode map = DataNode.Map();
			// Tomlyn 的表是字典，顺序不保证；按键名排序，保证同一文件多次解析顺序一致。
			List<string> keys = new List<string>(table.Keys);
			keys.Sort(StringComparer.Ordinal);
			foreach (string key in keys)
			{
				map.Add(key, FromTomlValue(table[key]));
			}
			return map;
		}

		private static DataNode FromTomlValue(object value)
		{
			switch (value)
			{
			case null:
				return DataNode.Scalar(string.Empty);
			case TomlTable table:
				return FromTomlTable(table);
			case TomlArray array:
			{
				DataNode seq = DataNode.Seq();
				foreach (object item in array)
				{
					seq.Add(FromTomlValue(item));
				}
				return seq;
			}
			case TomlTableArray tableArray:
			{
				DataNode seq = DataNode.Seq();
				foreach (TomlTable item in tableArray)
				{
					seq.Add(FromTomlTable(item));
				}
				return seq;
			}
			case bool boolean:
				return DataNode.Scalar(boolean ? "true" : "false");
			case string text:
				return DataNode.Scalar(text);
			case double number:
				return DataNode.Scalar(number.ToString("R", CultureInfo.InvariantCulture));
			case float single:
				return DataNode.Scalar(single.ToString("R", CultureInfo.InvariantCulture));
			case DateTime dateTime:
				return DataNode.Scalar(dateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture));
			default:
				return DataNode.Scalar(Convert.ToString(value, CultureInfo.InvariantCulture));
			}
		}

		// ---- XML ----

		private static DataNode ParseXml(string text)
		{
			XDocument document = XDocument.Parse(text, LoadOptions.None);
			if (document.Root == null)
			{
				return DataNode.Map();
			}
			// 根元素名作为最外层键，便于两侧一眼对上是不是同一种文档。
			DataNode map = DataNode.Map();
			map.Add(document.Root.Name.LocalName, FromXmlElement(document.Root));
			return map;
		}

		private static DataNode FromXmlElement(XElement element)
		{
			DataNode map = DataNode.Map();
			// 属性：@name，命名空间前缀保留在名字里（如 @xmlns:ns）。
			foreach (XAttribute attribute in element.Attributes())
			{
				map.Add("@" + attribute.Name.LocalName, DataNode.Scalar(attribute.Value));
			}
			// 文本：非纯空白的直接文本（含 CDATA）为 #text。
			bool hasElementChild = false;
			foreach (XNode node in element.Nodes())
			{
				if (node is XElement)
				{
					hasElementChild = true;
					break;
				}
			}
			if (!hasElementChild)
			{
				string text = element.Value ?? string.Empty;
				if (text.Trim().Length > 0)
				{
					map.Add("#text", DataNode.Scalar(text.Trim()));
				}
				return map;
			}
			// 子元素：同名多个时按出现次序加 [i]。
			Dictionary<string, int> totals = new Dictionary<string, int>();
			foreach (XElement child in element.Elements())
			{
				string name = child.Name.LocalName;
				totals.TryGetValue(name, out int count);
				totals[name] = count + 1;
			}
			Dictionary<string, int> seen = new Dictionary<string, int>();
			foreach (XElement child in element.Elements())
			{
				string name = child.Name.LocalName;
				seen.TryGetValue(name, out int index);
				seen[name] = index + 1;
				string key = totals[name] > 1 ? name + "[" + index + "]" : name;
				map.Add(key, FromXmlElement(child));
			}
			return map;
		}

		// ---- INI / CFG / properties ----

		private static DataNode ParseIni(string text)
		{
			DataNode root = DataNode.Map();
			DataNode current = root;
			string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
			string pending = null;
			foreach (string raw in lines)
			{
				string line = raw;
				// 续行：行尾反斜杠把下一行接上。
				if (pending != null)
				{
					line = pending + line.TrimStart();
					pending = null;
				}
				string trimmed = line.Trim();
				if (trimmed.Length == 0)
				{
					continue;
				}
				if (trimmed.EndsWith("\\", StringComparison.Ordinal))
				{
					pending = trimmed.Substring(0, trimmed.Length - 1);
					continue;
				}
				char first = trimmed[0];
				if (first == ';' || first == '#' || first == '!')
				{
					continue;
				}
				if (first == '[' && trimmed.EndsWith("]", StringComparison.Ordinal))
				{
					current = EnsureSection(root, trimmed.Substring(1, trimmed.Length - 2));
					continue;
				}
				int separator = IndexOfSeparator(trimmed);
				if (separator > 0)
				{
					string key = trimmed.Substring(0, separator).Trim();
					string value = trimmed.Substring(separator + 1).Trim();
					if (key.Length > 0)
					{
						current.Add(key, DataNode.Scalar(value));
					}
				}
				else
				{
					// 无分隔符的裸行（如 .properties 里的 flag）：当作布尔键。
					current.Add(trimmed, DataNode.Scalar("true"));
				}
			}
			return root;
		}

		/// <summary>节名可含点（<c>[a.b]</c>），逐级嵌套成映射。</summary>
		private static DataNode EnsureSection(DataNode root, string name)
		{
			DataNode current = root;
			string[] segments = name.Split('.');
			foreach (string rawSegment in segments)
			{
				string segment = rawSegment.Trim();
				if (segment.Length == 0)
				{
					continue;
				}
				DataNode child = null;
				foreach (DataEntry entry in current.Entries)
				{
					if (string.Equals(entry.Key, segment, StringComparison.Ordinal) && entry.Node.Kind == DataKind.Map)
					{
						child = entry.Node;
						break;
					}
				}
				if (child == null)
				{
					child = DataNode.Map();
					current.Add(segment, child);
				}
				current = child;
			}
			return current;
		}

		private static int IndexOfSeparator(string line)
		{
			int equals = line.IndexOf('=');
			int colon = line.IndexOf(':');
			if (equals < 0)
			{
				return colon;
			}
			if (colon < 0)
			{
				return equals;
			}
			return Math.Min(equals, colon);
		}
	}
}
