using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace ForkPlus.Plugins.Svg
{
	/// <summary>
	/// 绘制状态：SVG 里可继承的呈现属性（fill / stroke / stroke-width / fill-opacity / stroke-opacity）。
	/// 逐层向下继承，子节点未写就沿用父节点的取值——与 SVG 的继承语义一致。
	/// </summary>
	internal sealed class SvgStyle
	{
		internal string Fill;

		internal string Stroke;

		internal double? StrokeWidth;

		internal double? FillOpacity;

		internal double? StrokeOpacity;

		internal static readonly SvgStyle Default = new SvgStyle();

		/// <summary>由父状态继承后再套本元素自己的呈现属性与内联 <c>style</c>（内联优先）。</summary>
		internal static SvgStyle Derive(SvgNode node, SvgStyle parent)
		{
			SvgStyle style = new SvgStyle
			{
				Fill = parent?.Fill,
				Stroke = parent?.Stroke,
				StrokeWidth = parent?.StrokeWidth,
				FillOpacity = parent?.FillOpacity,
				StrokeOpacity = parent?.StrokeOpacity
			};
			Apply(style, "fill", node.Get("fill"));
			Apply(style, "stroke", node.Get("stroke"));
			Apply(style, "stroke-width", node.Get("stroke-width"));
			Apply(style, "fill-opacity", node.Get("fill-opacity"));
			Apply(style, "stroke-opacity", node.Get("stroke-opacity"));
			ParseInline(style, node.Get("style"));
			return style;
		}

		private static void ParseInline(SvgStyle style, string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			foreach (string part in text.Split(';'))
			{
				int colon = part.IndexOf(':');
				if (colon <= 0)
				{
					continue;
				}
				Apply(style, part.Substring(0, colon).Trim(), part.Substring(colon + 1).Trim());
			}
		}

		private static void Apply(SvgStyle style, string name, string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return;
			}
			switch (name.ToLowerInvariant())
			{
			case "fill":
				style.Fill = value;
				break;
			case "stroke":
				style.Stroke = value;
				break;
			case "stroke-width":
				if (SvgNumbers.ParseLength(value) is double width)
				{
					style.StrokeWidth = width;
				}
				break;
			case "fill-opacity":
				if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double fillOpacity))
				{
					style.FillOpacity = fillOpacity;
				}
				break;
			case "stroke-opacity":
				if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double strokeOpacity))
				{
					style.StrokeOpacity = strokeOpacity;
				}
				break;
			}
		}
	}

	/// <summary>
	/// SVG 渲染器：把统一元素树画成一块 Avalonia <see cref="Canvas"/>，外层套 <see cref="Viewbox"/>
	/// 按 viewBox 等比缩放，因此两侧并排时天然同尺度可比。
	///
	/// 支持的标签：<c>svg</c> / <c>g</c> / <c>a</c> / <c>switch</c>（容器），<c>rect</c> /
	/// <c>circle</c> / <c>ellipse</c> / <c>line</c> / <c>polyline</c> / <c>polygon</c> /
	/// <c>path</c> / <c>text</c> / <c>tspan</c>（图形），<c>use</c>（引用 <c>#id</c>）。
	/// <c>defs</c> / <c>metadata</c> / <c>style</c> 等非呈现节点跳过，不直接绘制。
	///
	/// transform 按 SVG 语义合成（translate / scale / rotate / matrix / skewX / skewY），
	/// 逐层累乘后一次性写进每个图形的 <see cref="Visual.RenderTransform"/>；
	/// 渐变 / 图案填充（<c>url(#…)</c>）不解析，退回中性灰，避免整块丢失。
	/// </summary>
	internal static class SvgRenderer
	{
		/// <summary>单次渲染的图形数上限，避免超大文件把 UI 拖死。</summary>
		private const int MaxShapes = 4000;

		/// <summary>递归深度上限（同时兜住 <c>&lt;use&gt;</c> 自引用造成的死循环）。</summary>
		private const int MaxDepth = 32;

		private static readonly Regex TransformPattern = new Regex(
			@"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		/// <summary>不直接绘制的标签（定义 / 元数据 / 脚本 / 滤镜等）。</summary>
		private static readonly HashSet<string> NotRendered = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"defs",
			"metadata",
			"title",
			"desc",
			"style",
			"script",
			"symbol",
			"marker",
			"pattern",
			"clippath",
			"mask",
			"lineargradient",
			"radialgradient",
			"filter",
			"animate",
			"animatetransform",
			"animatemotion",
			"set",
			"foreignobject",
			"image"
		};

		private static readonly Dictionary<string, uint> NamedColors = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
		{
			{ "black", 0x000000u },
			{ "white", 0xFFFFFFu },
			{ "red", 0xFF0000u },
			{ "green", 0x008000u },
			{ "blue", 0x0000FFu },
			{ "yellow", 0xFFFF00u },
			{ "orange", 0xFFA500u },
			{ "purple", 0x800080u },
			{ "gray", 0x808080u },
			{ "grey", 0x808080u },
			{ "silver", 0xC0C0C0u },
			{ "maroon", 0x800000u },
			{ "navy", 0x000080u },
			{ "teal", 0x008080u },
			{ "olive", 0x808000u },
			{ "lime", 0x00FF00u },
			{ "aqua", 0x00FFFFu },
			{ "cyan", 0x00FFFFu },
			{ "fuchsia", 0xFF00FFu },
			{ "magenta", 0xFF00FFu },
			{ "brown", 0xA52A2Au },
			{ "pink", 0xFFC0CBu },
			{ "gold", 0xFFD700u }
		};

		internal static Control Render(SvgDocument document)
		{
			double width = document != null && document.ViewWidth > 0.0 ? document.ViewWidth : 300.0;
			double height = document != null && document.ViewHeight > 0.0 ? document.ViewHeight : 150.0;
			Canvas canvas = new Canvas
			{
				Width = width,
				Height = height,
				Background = Brushes.Transparent,
				ClipToBounds = false
			};
			if (document?.Root != null)
			{
				Matrix toDevice = Matrix.CreateTranslation(-document.ViewX, -document.ViewY);
				Dictionary<string, SvgNode> ids = SvgParser.IndexIds(document.Root);
				int budget = MaxShapes;
				Add(canvas, document.Root, toDevice, SvgStyle.Default, 1.0, ids, 0, ref budget);
			}
			return new Viewbox
			{
				Child = canvas,
				Stretch = Stretch.Uniform,
				StretchDirection = StretchDirection.Both
			};
		}

		private static void Add(Canvas canvas, SvgNode node, Matrix parentToDevice, SvgStyle inherited, double inheritedOpacity, Dictionary<string, SvgNode> ids, int depth, ref int budget)
		{
			if (node == null || depth > MaxDepth || budget <= 0 || !IsVisible(node))
			{
				return;
			}
			string tag = (node.Tag ?? string.Empty).ToLowerInvariant();
			if (NotRendered.Contains(tag))
			{
				return;
			}
			SvgStyle style = SvgStyle.Derive(node, inherited);
			double opacity = Clamp(inheritedOpacity * Number(node.Get("opacity"), 1.0));
			Matrix own = TransformOf(node) * parentToDevice;

			if (tag == "use")
			{
				string reference = node.Get("href");
				if (string.IsNullOrEmpty(reference))
				{
					reference = node.Get("xlink:href");
				}
				SvgNode target = Resolve(ids, reference);
				if (target != null && !ReferenceEquals(target, node))
				{
					double x = Number(node.Get("x"), 0.0);
					double y = Number(node.Get("y"), 0.0);
					Add(canvas, target, Matrix.CreateTranslation(x, y) * own, style, opacity, ids, depth + 1, ref budget);
				}
				return;
			}

			if (TryBuildShape(node, tag, style, opacity, own, out Control shape))
			{
				budget--;
				canvas.Children.Add(shape);
			}

			foreach (SvgNode child in node.Children)
			{
				if (budget <= 0)
				{
					return;
				}
				Add(canvas, child, own, style, opacity, ids, depth + 1, ref budget);
			}
		}

		private static bool TryBuildShape(SvgNode node, string tag, SvgStyle style, double opacity, Matrix transform, out Control shape)
		{
			shape = null;
			switch (tag)
			{
			case "rect":
			{
				double width = Number(node.Get("width"), 0.0);
				double height = Number(node.Get("height"), 0.0);
				if (width <= 0.0 || height <= 0.0)
				{
					return false;
				}
				Rectangle rectangle = new Rectangle
				{
					Width = width,
					Height = height,
					RadiusX = Math.Max(0.0, Number(node.Get("rx"), 0.0)),
					RadiusY = Math.Max(0.0, Number(node.Get("ry"), 0.0))
				};
				shape = Finish(rectangle, style, opacity, Matrix.CreateTranslation(Number(node.Get("x"), 0.0), Number(node.Get("y"), 0.0)) * transform);
				return true;
			}
			case "circle":
			{
				double radius = Number(node.Get("r"), 0.0);
				if (radius <= 0.0)
				{
					return false;
				}
				double cx = Number(node.Get("cx"), 0.0);
				double cy = Number(node.Get("cy"), 0.0);
				Ellipse ellipse = new Ellipse
				{
					Width = radius * 2.0,
					Height = radius * 2.0
				};
				shape = Finish(ellipse, style, opacity, Matrix.CreateTranslation(cx - radius, cy - radius) * transform);
				return true;
			}
			case "ellipse":
			{
				double rx = Number(node.Get("rx"), 0.0);
				double ry = Number(node.Get("ry"), 0.0);
				if (rx <= 0.0 || ry <= 0.0)
				{
					return false;
				}
				double cx = Number(node.Get("cx"), 0.0);
				double cy = Number(node.Get("cy"), 0.0);
				Ellipse ellipse = new Ellipse
				{
					Width = rx * 2.0,
					Height = ry * 2.0
				};
				shape = Finish(ellipse, style, opacity, Matrix.CreateTranslation(cx - rx, cy - ry) * transform);
				return true;
			}
			case "line":
			{
				Line line = new Line
				{
					StartPoint = new Point(Number(node.Get("x1"), 0.0), Number(node.Get("y1"), 0.0)),
					EndPoint = new Point(Number(node.Get("x2"), 0.0), Number(node.Get("y2"), 0.0))
				};
				shape = Finish(line, style, opacity, transform);
				return true;
			}
			case "polyline":
			case "polygon":
			{
				List<Point> points = ToPoints(SvgNumbers.ParseList(node.Get("points")));
				if (points.Count < 2)
				{
					return false;
				}
				if (tag == "polyline")
				{
					shape = Finish(new Polyline { Points = points }, style, opacity, transform);
				}
				else
				{
					shape = Finish(new Polygon { Points = points }, style, opacity, transform);
				}
				return true;
			}
			case "path":
			{
				string data = node.Get("d");
				if (string.IsNullOrWhiteSpace(data))
				{
					return false;
				}
				Geometry geometry;
				try
				{
					geometry = Geometry.Parse(data);
				}
				catch (Exception)
				{
					return false;
				}
				if (geometry == null)
				{
					return false;
				}
				shape = Finish(new Path { Data = geometry }, style, opacity, transform);
				return true;
			}
			case "text":
			case "tspan":
			{
				string text = node.Get("#text");
				if (string.IsNullOrEmpty(text))
				{
					return false;
				}
				double size = Number(node.Get("font-size"), 16.0);
				if (size <= 0.0)
				{
					size = 16.0;
				}
				TextBlock block = new TextBlock
				{
					Text = text,
					FontSize = size,
					Foreground = Paint(style.Fill, Color.FromRgb(0, 0, 0), Clamp(opacity * (style.FillOpacity ?? 1.0)))
				};
				// SVG 的 y 是基线，TextBlock 定位的是左上角：近似上移一个字高。
				double x = Number(node.Get("x"), 0.0);
				double y = Number(node.Get("y"), 0.0);
				shape = Finish(block, style, opacity, Matrix.CreateTranslation(x , y - size) * transform);
				return true;
			}
			default:
				return false;
			}
		}

		private static T Finish<T>(T control, SvgStyle style, double opacity, Matrix transform) where T : Control
		{
			control.RenderTransform = new MatrixTransform(transform);
			control.RenderTransformOrigin = RelativePoint.TopLeft;
			control.Opacity = Clamp(opacity);
			if (control is Shape shape)
			{
				shape.Fill = Paint(style.Fill, Color.FromRgb(0, 0, 0), Clamp(opacity * (style.FillOpacity ?? 1.0)));
				shape.Stroke = Paint(style.Stroke, null, Clamp(opacity * (style.StrokeOpacity ?? 1.0)));
				shape.StrokeThickness = style.StrokeWidth ?? 1.0;
			}
			return control;
		}

		private static List<Point> ToPoints(List<double> values)
		{
			List<Point> points = new List<Point>();
			for (int index = 0; index + 1 < values.Count; index += 2)
			{
				points.Add(new Point(values[index], values[index + 1]));
			}
			return points;
		}

		private static SvgNode Resolve(Dictionary<string, SvgNode> ids, string reference)
		{
			if (ids == null || string.IsNullOrEmpty(reference))
			{
				return null;
			}
			string key = reference.Trim();
			if (key.StartsWith("#", StringComparison.Ordinal))
			{
				key = key.Substring(1);
			}
			return ids.TryGetValue(key, out SvgNode node) ? node : null;
		}

		/// <summary>合成 SVG transform 列表：列表里后写的先作用到点，故逐项左乘累积。</summary>
		private static Matrix TransformOf(SvgNode node)
		{
			string text = node.Get("transform");
			if (string.IsNullOrWhiteSpace(text))
			{
				return Matrix.Identity;
			}
			Matrix result = Matrix.Identity;
			foreach (Match match in TransformPattern.Matches(text))
			{
				Matrix step = BuildTransform(match.Groups[1].Value, SvgNumbers.ParseList(match.Groups[2].Value));
				result = step * result;
			}
			return result;
		}

		private static Matrix BuildTransform(string name, List<double> args)
		{
			switch (name.ToLowerInvariant())
			{
			case "matrix":
				if (args.Count >= 6)
				{
					// SVG matrix(a b c d e f) 是列向量写法，Avalonia 的 Matrix 是其转置。
					return new Matrix(args[0], args[1], args[2], args[3], args[4], args[5]);
				}
				break;
			case "translate":
				if (args.Count >= 2)
				{
					return Matrix.CreateTranslation(args[0], args[1]);
				}
				if (args.Count == 1)
				{
					return Matrix.CreateTranslation(args[0], 0.0);
				}
				break;
			case "scale":
				if (args.Count >= 2)
				{
					return Matrix.CreateScale(args[0], args[1]);
				}
				if (args.Count == 1)
				{
					return Matrix.CreateScale(args[0], args[0]);
				}
				break;
			case "rotate":
				if (args.Count >= 3)
				{
					double radians = args[0] * Math.PI / 180.0;
					return Matrix.CreateTranslation(-args[1], -args[2]) * Matrix.CreateRotation(radians) * Matrix.CreateTranslation(args[1], args[2]);
				}
				if (args.Count >= 1)
				{
					return Matrix.CreateRotation(args[0] * Math.PI / 180.0);
				}
				break;
			case "skewx":
				if (args.Count >= 1)
				{
					return Matrix.CreateSkew(args[0] * Math.PI / 180.0, 0.0);
				}
				break;
			case "skewy":
				if (args.Count >= 1)
				{
					return Matrix.CreateSkew(0.0, args[0] * Math.PI / 180.0);
				}
				break;
			}
			return Matrix.Identity;
		}

		private static bool IsVisible(SvgNode node)
		{
			string display = Property(node, "display");
			if (string.Equals(display, "none", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			string visibility = Property(node, "visibility");
			return !string.Equals(visibility, "hidden", StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(visibility, "collapse", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>取属性：先看同名属性，再看内联 <c>style</c> 声明。</summary>
		private static string Property(SvgNode node, string name)
		{
			string direct = node.Get(name);
			if (!string.IsNullOrEmpty(direct))
			{
				return direct;
			}
			string style = node.Get("style");
			if (string.IsNullOrEmpty(style))
			{
				return null;
			}
			foreach (string part in style.Split(';'))
			{
				int colon = part.IndexOf(':');
				if (colon <= 0)
				{
					continue;
				}
				if (string.Equals(part.Substring(0, colon).Trim(), name, StringComparison.OrdinalIgnoreCase))
				{
					return part.Substring(colon + 1).Trim();
				}
			}
			return null;
		}

		private static IBrush Paint(string value, Color? fallback, double alpha)
		{
			double opacity = Clamp(alpha);
			if (string.IsNullOrEmpty(value))
			{
				return fallback.HasValue ? new SolidColorBrush(fallback.Value, opacity) : null;
			}
			string trimmed = value.Trim();
			if (string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(trimmed, "transparent", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			if (trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
			{
				// 渐变 / 图案：不解析，退回中性灰，至少保留形状轮廓与位置。
				return new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99), opacity);
			}
			if (!TryColor(trimmed, out Color color))
			{
				return fallback.HasValue ? new SolidColorBrush(fallback.Value, opacity) : null;
			}
			return new SolidColorBrush(color, opacity);
		}

		private static bool TryColor(string text, out Color color)
		{
			color = default(Color);
			if (text.StartsWith("#", StringComparison.Ordinal))
			{
				string hex = text.Substring(1);
				if (hex.Length == 3 || hex.Length == 4)
				{
					char r = hex[0];
					char g = hex[1];
					char b = hex[2];
					string expanded = new string(new[] { r, r, g, g, b, b });
					return TryHex(expanded, out color);
				}
				return TryHex(hex, out color);
			}
			if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
			{
				int open = text.IndexOf('(');
				int close = text.LastIndexOf(')');
				if (open >= 0 && close > open)
				{
					string[] parts = text.Substring(open + 1, close - open - 1).Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length >= 3 && TryChannel(parts[0], out byte r) && TryChannel(parts[1], out byte g) && TryChannel(parts[2], out byte b))
					{
						byte a = 255;
						if (parts.Length >= 4 && double.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double alpha))
						{
							a = (byte)Math.Round(Clamp(alpha) * 255.0);
						}
						color = Color.FromArgb(a, r, g, b);
						return true;
					}
				}
				return false;
			}
			if (NamedColors.TryGetValue(text, out uint named))
			{
				color = Color.FromRgb((byte)(named >> 16), (byte)(named >> 8), (byte)named);
				return true;
			}
			return false;
		}

		private static bool TryHex(string hex, out Color color)
		{
			color = default(Color);
			if (hex.Length != 6 && hex.Length != 8)
			{
				return false;
			}
			if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
			{
				return false;
			}
			if (hex.Length == 6)
			{
				color = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
			}
			else
			{
				color = Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
			}
			return true;
		}

		private static bool TryChannel(string text, out byte channel)
		{
			channel = 0;
			string trimmed = text.Trim();
			if (trimmed.EndsWith("%", StringComparison.Ordinal)
				&& double.TryParse(trimmed.Substring(0, trimmed.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double percent))
			{
				channel = (byte)Math.Round(Clamp(percent / 100.0) * 255.0);
				return true;
			}
			if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
			{
				channel = (byte)Math.Min(255.0, Math.Max(0.0, Math.Round(value)));
				return true;
			}
			return false;
		}

		private static double Number(string text, double fallback)
		{
			double? value = SvgNumbers.ParseLength(text);
			return value.HasValue ? value.Value : fallback;
		}

		private static double Clamp(double value)
		{
			if (double.IsNaN(value))
			{
				return 1.0;
			}
			return Math.Min(1.0, Math.Max(0.0, value));
		}
	}
}
