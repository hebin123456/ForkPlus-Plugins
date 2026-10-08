using System;
using System.Collections.Generic;
using System.Globalization;

namespace ForkPlus.Plugins.Subtitle
{
	/// <summary>
	/// 字幕解析：把 SRT / WebVTT / ASS / SSA / MicroDVD(.sub，含 SubViewer 兜底) 各解析成同一套
	/// <see cref="SubtitleCue"/> 列表。
	///
	/// 分工：
	/// <list type="bullet">
	/// <item>SRT / SubViewer —— 逐块扫 <c>start --&gt; end</c>（SubViewer 用 <c>start,end</c>）行，
	/// 其后到空行为文本；序号行可选。</item>
	/// <item>WebVTT —— 与 SRT 同构，时间用 <c>.</c> 作小数位，跳过 <c>WEBVTT</c> / <c>NOTE</c> /
	/// <c>STYLE</c> / <c>REGION</c> 头，忽略时间行尾的对齐设置。</item>
	/// <item>ASS / SSA —— 只读 <c>[Events]</c> 段，按其中的 <c>Format:</c> 行定位
	/// <c>Start</c> / <c>End</c> / <c>Style</c> / <c>Name</c> / <c>Text</c> 列（Text 恒为最后一列，
	/// 允许其中含逗号）。</item>
	/// <item>MicroDVD —— <c>{起始帧}{结束帧}文本</c> 按帧率换算时间；首行若为
	/// <c>{1}{1}&lt;帧率&gt;</c> 则用它声明帧率，缺省 25 fps。</item>
	/// </list>
	///
	/// 解析失败不抛异常：返回 <c>null</c> 并回填 <paramref name="error"/>，视图按「无法解析」提示处理。
	/// </summary>
	internal static class SubtitleParser
	{
		/// <summary>MicroDVD 未声明帧率时的缺省值。</summary>
		private const double DefaultFps = 25.0;

		/// <summary>按扩展名分派解析器。成功返回文档，失败返回 null 并回填 error。
		/// 扩展名带不带前导点都接受（视图侧 FormatOf 产出无点形式，此处归一成小写含点）。</summary>
		internal static SubtitleDocument Parse(string text, string extension, out string error)
		{
			error = null;
			if (text == null)
			{
				text = string.Empty;
			}
			if (text.Length > 0 && text[0] == '\uFEFF')
			{
				text = text.Substring(1);
			}
			try
			{
				SubtitleDocument document;
				string ext = (extension ?? string.Empty).Trim().ToLowerInvariant();
				if (ext.Length > 0 && ext[0] != '.')
				{
					ext = "." + ext;
				}
				switch (ext)
				{
				case ".srt":
					document = ParseSrt(text, "SRT");
					break;
				case ".vtt":
					document = ParseWebVtt(text);
					break;
				case ".ass":
					document = ParseAss(text, "ASS");
					break;
				case ".ssa":
					document = ParseAss(text, "SSA");
					break;
				case ".sub":
					document = ParseSub(text);
					break;
				default:
					error = "unsupported format";
					return null;
				}
				if (document == null || document.Cues.Count == 0)
				{
					error = "no cues parsed";
					return null;
				}
				return document;
			}
			catch (Exception ex)
			{
				error = ex.GetType().Name + ": " + ex.Message;
				return null;
			}
		}

		// ---- SRT / SubViewer ----

		private static SubtitleDocument ParseSrt(string text, string format)
		{
			SubtitleDocument document = new SubtitleDocument
			{
				Format = format
			};
			string[] lines = SplitLines(text);
			int index = 0;
			while (index < lines.Length)
			{
				if (lines[index].Trim().Length == 0)
				{
					index++;
					continue;
				}
				string current = lines[index];
				int cueNumber = document.Cues.Count + 1;
				if (!IsTimingLine(current) && index + 1 < lines.Length && IsTimingLine(lines[index + 1]))
				{
					// 序号行（SRT 有，SubViewer 无）。
					if (int.TryParse(current.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
					{
						cueNumber = parsed;
					}
					index++;
					current = lines[index];
				}
				if (!IsTimingLine(current))
				{
					index++;
					continue;
				}
				long start;
				long end;
				if (!TryParseRange(current, out start, out end))
				{
					index++;
					continue;
				}
				index++;
				List<string> body = new List<string>();
				while (index < lines.Length && lines[index].Trim().Length > 0)
				{
					body.Add(lines[index]);
					index++;
				}
				document.Cues.Add(new SubtitleCue
				{
					Index = cueNumber,
					StartMs = start,
					EndMs = end,
					Text = string.Join("\n", body)
				});
			}
			return document;
		}

		private static bool IsTimingLine(string line)
		{
			return line.IndexOf("-->", StringComparison.Ordinal) >= 0 || IsSubViewerRange(line);
		}

		/// <summary>SubViewer 用 <c>start,end</c>（两侧都是完整时间戳，逗号分隔）。</summary>
		private static bool IsSubViewerRange(string line)
		{
			string trimmed = line.Trim();
			int comma = trimmed.IndexOf(',');
			return comma > 0 && trimmed.IndexOf(':') >= 0 && trimmed.IndexOf(':') < comma;
		}

		private static bool TryParseRange(string line, out long start, out long end)
		{
			start = -1L;
			end = -1L;
			int arrow = line.IndexOf("-->", StringComparison.Ordinal);
			string leftText;
			string rightText;
			if (arrow >= 0)
			{
				leftText = line.Substring(0, arrow);
				rightText = line.Substring(arrow + 3);
			}
			else
			{
				int comma = line.IndexOf(',');
				if (comma <= 0)
				{
					return false;
				}
				leftText = line.Substring(0, comma);
				rightText = line.Substring(comma + 1);
				// SubViewer 右侧以逗号接下一段时取第一段。
				int nextComma = rightText.IndexOf(',');
				if (nextComma >= 0)
				{
					rightText = rightText.Substring(0, nextComma);
				}
			}
			// WebVTT 时间行尾可带对齐设置（align:start position:10%），取第一个空白前的部分。
			rightText = FirstToken(rightText);
			start = ParseTime(leftText);
			end = ParseTime(rightText);
			return start >= 0L && end >= 0L;
		}

		private static string FirstToken(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}
			string trimmed = value.Trim();
			int space = trimmed.IndexOfAny(new char[2] { ' ', '\t' });
			return space >= 0 ? trimmed.Substring(0, space) : trimmed;
		}

		// ---- WebVTT ----

		private static SubtitleDocument ParseWebVtt(string text)
		{
			SubtitleDocument document = ParseSrt(text, "WebVTT");
			// WEBVTT / NOTE / STYLE / REGION 这些块不含 "-->"，ParseSrt 会顺带跳过；
			// 唯一例外是 STYLE / REGION 块内可能出现的冒号行，因其不构成时间行也不会误收。
			return document;
		}

		// ---- ASS / SSA ----

		private static SubtitleDocument ParseAss(string text, string format)
		{
			SubtitleDocument document = new SubtitleDocument
			{
				Format = format
			};
			string[] lines = SplitLines(text);
			bool inEvents = false;
			string[] fields = null;
			int index = 0;
			foreach (string raw in lines)
			{
				string line = raw.Trim();
				if (line.Length == 0)
				{
					continue;
				}
				if (line[0] == '[' && line[line.Length - 1] == ']')
				{
					inEvents = string.Equals(line, "[Events]", StringComparison.OrdinalIgnoreCase);
					fields = null;
					continue;
				}
				if (!inEvents)
				{
					continue;
				}
				if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
				{
					fields = SplitAssFields(line.Substring("Format:".Length));
					continue;
				}
				if (!line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string[] values = SplitAssValues(line.Substring("Dialogue:".Length), fields);
				if (values == null)
				{
					continue;
				}
				Dictionary<string, string> map = MapFields(fields, values);
				if (map == null)
				{
					continue;
				}
				long start = ParseTime(Get(map, "start"));
				long end = ParseTime(Get(map, "end"));
				if (start < 0L || end < 0L)
				{
					continue;
				}
				index++;
				string style = Get(map, "style");
				string name = Get(map, "name");
				string extra = null;
				if (!string.IsNullOrEmpty(style))
				{
					extra = style;
				}
				if (!string.IsNullOrEmpty(name))
				{
					extra = string.IsNullOrEmpty(extra) ? name : (extra + " · " + name);
				}
				document.Cues.Add(new SubtitleCue
				{
					Index = index,
					StartMs = start,
					EndMs = end,
					Text = (Get(map, "text") ?? string.Empty).Replace("\\N", "\n"),
					Extra = extra
				});
			}
			return document;
		}

		private static string[] SplitAssFields(string value)
		{
			string[] parts = value.Split(',');
			for (int i = 0; i < parts.Length; i++)
			{
				parts[i] = parts[i].Trim().ToLowerInvariant();
			}
			return parts;
		}

		/// <summary>按 Format 的列数切分；Text 是最后一列，其中可含逗号。</summary>
		private static string[] SplitAssValues(string value, string[] fields)
		{
			if (fields == null || fields.Length == 0)
			{
				return null;
			}
			string trimmed = value.Trim();
			string[] parts = trimmed.Split(new char[1] { ',' }, fields.Length);
			if (parts.Length < fields.Length)
			{
				return null;
			}
			for (int i = 0; i < parts.Length; i++)
			{
				parts[i] = parts[i].Trim();
			}
			return parts;
		}

		private static Dictionary<string, string> MapFields(string[] fields, string[] values)
		{
			Dictionary<string, string> map = new Dictionary<string, string>();
			for (int i = 0; i < fields.Length; i++)
			{
				if (i >= values.Length)
				{
					break;
				}
				if (!map.ContainsKey(fields[i]))
				{
					map.Add(fields[i], values[i]);
				}
			}
			return map;
		}

		private static string Get(Dictionary<string, string> map, string key)
		{
			return map.TryGetValue(key, out string value) ? value : null;
		}

		// ---- MicroDVD / SubViewer ----

		private static SubtitleDocument ParseSub(string text)
		{
			SubtitleDocument microDvd = ParseMicroDvd(text);
			if (microDvd.Cues.Count > 0)
			{
				return microDvd;
			}
			// 不是 MicroDVD 的 .sub 多为 SubViewer（时间行用逗号分隔）。
			return ParseSrt(text, "SubViewer");
		}

		private static SubtitleDocument ParseMicroDvd(string text)
		{
			SubtitleDocument document = new SubtitleDocument
			{
				Format = "MicroDVD"
			};
			double fps = DefaultFps;
			bool declaredFps = false;
			string[] lines = SplitLines(text);
			foreach (string raw in lines)
			{
				string line = raw.Trim();
				if (line.Length == 0 || line[0] != '{')
				{
					continue;
				}
				int firstClose = line.IndexOf('}');
				if (firstClose <= 1)
				{
					continue;
				}
				int secondOpen = line.IndexOf('{', firstClose);
				if (secondOpen < 0)
				{
					continue;
				}
				int secondClose = line.IndexOf('}', secondOpen);
				if (secondClose < 0)
				{
					continue;
				}
				if (!int.TryParse(line.Substring(1, firstClose - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int startFrame))
				{
					continue;
				}
				if (!int.TryParse(line.Substring(secondOpen + 1, secondClose - secondOpen - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int endFrame))
				{
					continue;
				}
				string body = line.Substring(secondClose + 1);
				// MicroDVD 约定：首行 {1}{1}<帧率> 用于声明帧率，不是字幕。
				if (startFrame == 1 && endFrame == 1 && !declaredFps)
				{
					if (double.TryParse(body.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedFps) && parsedFps > 0.0)
					{
						fps = parsedFps;
					}
					declaredFps = true;
					continue;
				}
				if (body.Length == 0)
				{
					continue;
				}
				document.Cues.Add(new SubtitleCue
				{
					Index = document.Cues.Count + 1,
					StartMs = FrameToMs(startFrame, fps),
					EndMs = FrameToMs(endFrame, fps),
					Text = body.Replace("|", "\n")
				});
			}
			if (document.Cues.Count > 0)
			{
				document.AssumedFps = fps;
			}
			return document;
		}

		private static long FrameToMs(int frame, double fps)
		{
			if (fps <= 0.0)
			{
				fps = DefaultFps;
			}
			return (long)Math.Round(frame / fps * 1000.0);
		}

		// ---- 公共 ----

		/// <summary>时间戳解析：<c>H:MM:SS.mmm</c> / <c>MM:SS,mmm</c> 都接受，失败返回 -1。</summary>
		internal static long ParseTime(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return -1L;
			}
			string trimmed = value.Trim();
			int colon = trimmed.LastIndexOf(':');
			if (colon < 0)
			{
				return -1L;
			}
			string head = trimmed.Substring(0, colon);
			string tail = trimmed.Substring(colon + 1).Replace(',', '.');
			if (!double.TryParse(tail, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
			{
				return -1L;
			}
			string[] parts = head.Split(':');
			if (parts.Length > 2)
			{
				return -1L;
			}
			int hours = 0;
			int minutes;
			if (parts.Length == 1)
			{
				if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes))
				{
					return -1L;
				}
			}
			else
			{
				if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hours) ||
					!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes))
				{
					return -1L;
				}
			}
			long milliseconds = ((long)hours * 3600L + (long)minutes * 60L) * 1000L + (long)Math.Round(seconds * 1000.0);
			return milliseconds < 0L ? -1L : milliseconds;
		}

		private static string[] SplitLines(string text)
		{
			return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		}
	}
}
