using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Linq;

namespace ForkPlus.Plugins.Svg
{
	/// <summary>
	/// SVG（XML）解析器：把文档读成一棵统一的 <see cref="SvgNode"/> 树，并解析视口（viewBox）。
	///
	/// 零第三方依赖：直接用 BCL 的 <see cref="XDocument"/>（System.Xml.Linq）解析 XML，
	/// 命名空间只取本地名（<c>&lt;svg:rect&gt;</c> 与 <c>&lt;rect&gt;</c> 等价），因此对
	/// Inkscape / Illustrator 等带命名空间前缀导出的文件同样有效。
	/// </summary>
	internal static class SvgParser
	{
		/// <summary>SVG 默认替换尺寸（未给 width/height/viewBox 时按 300×150 渲染）。</summary>
		private const double DefaultWidth = 300.0;

		private const double DefaultHeight = 150.0;

		internal static SvgDocument Parse(string text, out string error)
		{
			error = null;
			if (string.IsNullOrWhiteSpace(text))
			{
				error = "empty document";
				return null;
			}
			try
			{
				XDocument document = XDocument.Parse(text, LoadOptions.None);
				XElement root = document.Root;
				if (root == null)
				{
					error = "empty document";
					return null;
				}
				if (!string.Equals(root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
				{
					error = "root element is <" + root.Name.LocalName + ">, not <svg>";
					return null;
				}
				SvgDocument result = new SvgDocument
				{
					Root = Convert(root)
				};
				result.ElementCount = Count(result.Root);
				ResolveViewport(result, root);
				return result;
			}
			catch (XmlException ex)
			{
				error = "XML: " + ex.Message;
				return null;
			}
			catch (Exception ex)
			{
				error = ex.GetType().Name + ": " + ex.Message;
				return null;
			}
		}

		private static SvgNode Convert(XElement element)
		{
			SvgNode node = new SvgNode
			{
				Tag = element.Name.LocalName
			};
			foreach (XAttribute attribute in element.Attributes())
			{
				if (attribute.IsNamespaceDeclaration)
				{
					continue;
				}
				node.Add(attribute.Name.LocalName, attribute.Value);
			}
			string id = node.Get("id");
			node.Id = string.IsNullOrEmpty(id) ? null : id;

			// 直接文本内容（<text> / <tspan> 的可见文字）挂成伪属性 "#text"：
			// 既让渲染器有字可画，也让结构 diff 能报出文字改动。
			System.Text.StringBuilder text = new System.Text.StringBuilder();
			foreach (XNode child in element.Nodes())
			{
				if (child is XText textNode)
				{
					text.Append(textNode.Value);
				}
			}
			string content = text.ToString().Trim();
			if (content.Length > 0)
			{
				node.Add("#text", content);
			}

			Dictionary<string, int> ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (XElement child in element.Elements())
			{
				SvgNode childNode = Convert(child);
				if (!ordinals.TryGetValue(childNode.Tag, out int ordinal))
				{
					ordinal = 0;
				}
				ordinals[childNode.Tag] = ordinal + 1;
				childNode.Ordinal = ordinal;
				node.Children.Add(childNode);
			}
			return node;
		}

		private static int Count(SvgNode node)
		{
			if (node == null)
			{
				return 0;
			}
			int total = 1;
			foreach (SvgNode child in node.Children)
			{
				total += Count(child);
			}
			return total;
		}

		/// <summary>
		/// 视口解析：优先 viewBox（x y w h）；没有 viewBox 时退到 width/height 并令起点为 0；
		/// 两者都没有则用 SVG 规范的默认替换尺寸 300×150。
		/// </summary>
		private static void ResolveViewport(SvgDocument document, XElement root)
		{
			List<double> viewBox = SvgNumbers.ParseList(root.Attribute("viewBox")?.Value);
			if (viewBox.Count >= 4 && viewBox[2] > 0.0 && viewBox[3] > 0.0)
			{
				document.ViewX = viewBox[0];
				document.ViewY = viewBox[1];
				document.ViewWidth = viewBox[2];
				document.ViewHeight = viewBox[3];
				return;
			}

			double? width = SvgNumbers.ParseLength(root.Attribute("width")?.Value);
			double? height = SvgNumbers.ParseLength(root.Attribute("height")?.Value);
			document.ViewX = 0.0;
			document.ViewY = 0.0;
			document.ViewWidth = width.HasValue && width.Value > 0.0 ? width.Value : DefaultWidth;
			document.ViewHeight = height.HasValue && height.Value > 0.0 ? height.Value : DefaultHeight;
		}

		/// <summary>
		/// 建立 id → 节点索引（供 <c>&lt;use&gt;</c> 引用解析）。
		/// 同名取首次出现者，避免后出现的定义覆盖语义。
		/// </summary>
		internal static Dictionary<string, SvgNode> IndexIds(SvgNode root)
		{
			Dictionary<string, SvgNode> map = new Dictionary<string, SvgNode>(StringComparer.Ordinal);
			Collect(root, map);
			return map;
		}

		private static void Collect(SvgNode node, Dictionary<string, SvgNode> map)
		{
			if (node == null)
			{
				return;
			}
			if (!string.IsNullOrEmpty(node.Id) && !map.ContainsKey(node.Id))
			{
				map.Add(node.Id, node);
			}
			foreach (SvgNode child in node.Children)
			{
				Collect(child, map);
			}
		}
	}
}
