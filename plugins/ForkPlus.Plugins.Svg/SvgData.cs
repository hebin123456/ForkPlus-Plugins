using System;
using System.Collections.Generic;
using System.Globalization;

namespace ForkPlus.Plugins.Svg
{
	/// <summary>SVG 元素上的一个属性（保序保存，diff 时按属性名逐项比较）。</summary>
	internal sealed class SvgAttribute
	{
		internal string Name;

		internal string Value;

		internal SvgAttribute(string name, string value)
		{
			Name = name;
			Value = value ?? string.Empty;
		}
	}

	/// <summary>
	/// 与具体标签无关的统一元素模型：标签名 + 属性 + 子节点。
	/// 解析器把 SVG（XML）压平成这棵树，渲染与结构 diff 都只认这一套。
	/// </summary>
	internal sealed class SvgNode
	{
		internal string Tag = string.Empty;

		internal string Id;

		/// <summary>属性（按文档顺序，保序便于 diff 阅读）。</summary>
		internal List<SvgAttribute> Attributes = new List<SvgAttribute>();

		internal List<SvgNode> Children = new List<SvgNode>();

		/// <summary>同一父节点下同类标签的出现序号（从 0 起），用于生成稳定的元素路径。</summary>
		internal int Ordinal;

		internal string Get(string name)
		{
			foreach (SvgAttribute attribute in Attributes)
			{
				if (string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase))
				{
					return attribute.Value;
				}
			}
			return null;
		}

		internal void Add(string name, string value)
		{
			Attributes.Add(new SvgAttribute(name, value));
		}
	}

	/// <summary>解析后的 SVG 文档：根元素 + 视口（viewBox）+ 元素计数。</summary>
	internal sealed class SvgDocument
	{
		internal SvgNode Root;

		internal double ViewX;

		internal double ViewY;

		internal double ViewWidth = 300.0;

		internal double ViewHeight = 150.0;

		/// <summary>元素总数（含根，不含纯文本 / 注释）。</summary>
		internal int ElementCount;
	}

	/// <summary>SVG 数字 / 长度解析（viewBox、points、transform 参数、polyline 坐标共用）。</summary>
	internal static class SvgNumbers
	{
		/// <summary>
		/// SVG 数字串：以空白 / 逗号分隔，允许 "1-2" 这类省略分隔符的负号紧邻写法。
		/// 解析失败的位置直接跳过，不影响其余数字。
		/// </summary>
		internal static List<double> ParseList(string text)
		{
			List<double> values = new List<double>();
			if (string.IsNullOrEmpty(text))
			{
				return values;
			}
			int index = 0;
			int length = text.Length;
			while (index < length)
			{
				char c = text[index];
				if (char.IsWhiteSpace(c) || c == ',')
				{
					index++;
					continue;
				}
				int start = index;
				if (c == '+' || c == '-')
				{
					index++;
				}
				while (index < length && (char.IsDigit(text[index]) || text[index] == '.'))
				{
					index++;
				}
				if (index < length && (text[index] == 'e' || text[index] == 'E'))
				{
					int exponent = index + 1;
					if (exponent < length && (text[exponent] == '+' || text[exponent] == '-'))
					{
						exponent++;
					}
					if (exponent < length && char.IsDigit(text[exponent]))
					{
						index = exponent;
						while (index < length && char.IsDigit(text[index]))
						{
							index++;
						}
					}
				}
				if (index > start && double.TryParse(text.Substring(start, index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
				{
					values.Add(value);
				}
				else
				{
					index = start + 1;
				}
			}
			return values;
		}

		/// <summary>
		/// 解析长度值（如 <c>12</c> / <c>12px</c> / <c>1.5em</c>）。百分比与无法识别的单位返回 null。
		/// </summary>
		internal static double? ParseLength(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return null;
			}
			string trimmed = text.Trim();
			if (trimmed.EndsWith("%", StringComparison.Ordinal))
			{
				return null;
			}
			int end = 0;
			while (end < trimmed.Length && (char.IsDigit(trimmed[end]) || trimmed[end] == '.' || trimmed[end] == '-' || trimmed[end] == '+' || trimmed[end] == 'e' || trimmed[end] == 'E'))
			{
				end++;
			}
			if (end == 0)
			{
				return null;
			}
			if (!double.TryParse(trimmed.Substring(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
			{
				return null;
			}
			string unit = trimmed.Substring(end).Trim().ToLowerInvariant();
			switch (unit)
			{
			case "":
			case "px":
				return value;
			case "pt":
				return value * 96.0 / 72.0;
			case "pc":
				return value * 16.0;
			case "in":
				return value * 96.0;
			case "cm":
				return value * 96.0 / 2.54;
			case "mm":
				return value * 96.0 / 25.4;
			case "em":
			case "rem":
				return value * 16.0;
			default:
				return value;
			}
		}

		/// <summary>取列表第 n 个数字，越界返回 <paramref name="fallback"/>。</summary>
		internal static double At(List<double> values, int index, double fallback)
		{
			return values != null && index >= 0 && index < values.Count ? values[index] : fallback;
		}
	}

	// ---- 结构 diff ----

	internal enum SvgState
	{
		Same,
		Changed,
		LeftOnly,
		RightOnly
	}

	/// <summary>结构 diff 的一行：同一「元素路径 @ 属性」在旧 / 新两侧的值与变更状态。</summary>
	internal sealed class SvgDiffRow
	{
		internal string Path;

		internal string Left;

		internal string Right;

		internal bool LeftPresent;

		internal bool RightPresent;

		internal SvgState State;
	}

	internal sealed class SvgDiffResult
	{
		internal List<SvgDiffRow> Rows = new List<SvgDiffRow>();

		internal int Same;

		internal int Changed;

		internal int LeftOnly;

		internal int RightOnly;

		internal int Total => Rows.Count;

		internal bool HasDifferences => Changed > 0 || LeftOnly > 0 || RightOnly > 0;
	}

	/// <summary>拍平后的一行：元素路径 + 展示值。</summary>
	internal sealed class SvgFlatEntry
	{
		internal string Path;

		internal string Value;

		/// <summary>该行代表一个元素本身（值为 <c>&lt;tag&gt;</c>），而不是某个属性。</summary>
		internal bool Element;
	}

	/// <summary>
	/// 把元素树深度优先拍平成「元素路径 → 值」列表：每个元素先落一行「元素存在」，
	/// 再逐个属性落一行。路径形如 <c>svg/g[0]/rect[1]@fill</c>——同级同类标签用序号区分，
	/// 因此两侧结构只要同序就能一一对齐。
	/// </summary>
	internal static class SvgFlatten
	{
		internal static List<SvgFlatEntry> Flatten(SvgNode root)
		{
			List<SvgFlatEntry> list = new List<SvgFlatEntry>();
			if (root != null)
			{
				Walk(root, root.Tag, list);
			}
			return list;
		}

		private static void Walk(SvgNode node, string path, List<SvgFlatEntry> list)
		{
			list.Add(new SvgFlatEntry
			{
				Path = path,
				Value = "<" + node.Tag + ">",
				Element = true
			});
			foreach (SvgAttribute attribute in node.Attributes)
			{
				list.Add(new SvgFlatEntry
				{
					Path = path + "@" + attribute.Name,
					Value = attribute.Value
				});
			}
			foreach (SvgNode child in node.Children)
			{
				Walk(child, path + "/" + child.Tag + "[" + child.Ordinal + "]", list);
			}
		}
	}

	/// <summary>
	/// 结构 diff：把两侧元素树拍平成路径集合，按路径求并集后逐行判定 相同 / 已变更 / 仅左 / 仅右。
	/// 顺序取「先左后右」——左侧原有路径保持原序，右侧独有的路径追加在后，便于顺着文档结构读。
	/// </summary>
	internal static class SvgDiff
	{
		internal static SvgDiffResult Compute(SvgNode left, SvgNode right)
		{
			List<SvgFlatEntry> leftList = SvgFlatten.Flatten(left);
			List<SvgFlatEntry> rightList = SvgFlatten.Flatten(right);

			Dictionary<string, SvgFlatEntry> leftMap = Index(leftList);
			Dictionary<string, SvgFlatEntry> rightMap = Index(rightList);

			SvgDiffResult result = new SvgDiffResult();
			HashSet<string> emitted = new HashSet<string>();
			Emit(leftList, leftMap, rightMap, result, emitted);
			Emit(rightList, leftMap, rightMap, result, emitted);
			return result;
		}

		private static void Emit(List<SvgFlatEntry> order, Dictionary<string, SvgFlatEntry> leftMap, Dictionary<string, SvgFlatEntry> rightMap, SvgDiffResult result, HashSet<string> emitted)
		{
			foreach (SvgFlatEntry entry in order)
			{
				if (!emitted.Add(entry.Path))
				{
					continue;
				}
				bool hasLeft = leftMap.TryGetValue(entry.Path, out SvgFlatEntry left);
				bool hasRight = rightMap.TryGetValue(entry.Path, out SvgFlatEntry right);
				SvgDiffRow row = new SvgDiffRow
				{
					Path = entry.Path,
					LeftPresent = hasLeft,
					RightPresent = hasRight,
					Left = hasLeft ? left.Value : null,
					Right = hasRight ? right.Value : null
				};
				if (hasLeft && hasRight)
				{
					if (string.Equals(left.Value, right.Value, StringComparison.Ordinal))
					{
						row.State = SvgState.Same;
						result.Same++;
					}
					else
					{
						row.State = SvgState.Changed;
						result.Changed++;
					}
				}
				else if (hasLeft)
				{
					row.State = SvgState.LeftOnly;
					result.LeftOnly++;
				}
				else
				{
					row.State = SvgState.RightOnly;
					result.RightOnly++;
				}
				result.Rows.Add(row);
			}
		}

		private static Dictionary<string, SvgFlatEntry> Index(List<SvgFlatEntry> list)
		{
			Dictionary<string, SvgFlatEntry> map = new Dictionary<string, SvgFlatEntry>();
			foreach (SvgFlatEntry entry in list)
			{
				if (!map.ContainsKey(entry.Path))
				{
					map.Add(entry.Path, entry);
				}
			}
			return map;
		}
	}
}
