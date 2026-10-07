using System;
using System.Collections.Generic;
using System.Text;

namespace ForkPlus.Plugins.Subtitle
{
	/// <summary>一条字幕：起止时间 + 文本（多行以 <c>\n</c> 连接）+ 附加信息（ASS 的样式 / 说话人）。</summary>
	internal sealed class SubtitleCue
	{
		/// <summary>源文件中的序号（展示用；MicroDVD 等无序号格式按出现次序补）。</summary>
		internal int Index;

		internal long StartMs;

		internal long EndMs;

		internal string Text = string.Empty;

		/// <summary>ASS / SSA 的 <c>Style</c>（与 <c>Name</c>）等附加信息；其它格式为空。</summary>
		internal string Extra;
	}

	/// <summary>一次解析的结果：格式名 + 字幕列表（+ MicroDVD 的时间换算帧率）。</summary>
	internal sealed class SubtitleDocument
	{
		internal string Format = "?";

		internal List<SubtitleCue> Cues = new List<SubtitleCue>();

		/// <summary>MicroDVD 用帧号记时，此处记录换算时间轴所用的帧率；其它格式为 null。</summary>
		internal double? AssumedFps;
	}

	internal enum CueState
	{
		Same,
		Changed,
		LeftOnly,
		RightOnly
	}

	/// <summary>对齐后的一行：同一位置在旧 / 新两侧的字幕与变更状态。</summary>
	internal sealed class CueRow
	{
		internal SubtitleCue Left;

		internal SubtitleCue Right;

		internal CueState State;
	}

	internal sealed class SubtitleDiffResult
	{
		internal List<CueRow> Rows = new List<CueRow>();

		internal int Same;

		internal int Changed;

		internal int LeftOnly;

		internal int RightOnly;

		internal int Total => Rows.Count;

		internal bool HasDifferences => Changed > 0 || LeftOnly > 0 || RightOnly > 0;
	}

	/// <summary>文本归一与时间格式化的小工具。</summary>
	internal static class SubtitleText
	{
		/// <summary>
		/// 归一化字幕文本用于比较：去掉 SRT / VTT 的 <c>&lt;...&gt;</c> 标签与 ASS 的 <c>{...}</c>
		/// 覆盖块，把 <c>\N</c> / <c>\n</c> 换算行，折叠空白并转小写。
		/// </summary>
		internal static string Normalize(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return string.Empty;
			}
			StringBuilder builder = new StringBuilder(text.Length);
			bool inTag = false;
			for (int i = 0; i < text.Length; i++)
			{
				char c = text[i];
				if (c == '<')
				{
					inTag = true;
					continue;
				}
				if (c == '>')
				{
					inTag = false;
					continue;
				}
				if (c == '{')
				{
					inTag = true;
					continue;
				}
				if (c == '}')
				{
					inTag = false;
					continue;
				}
				if (inTag)
				{
					continue;
				}
				if (c == '\\' && i + 1 < text.Length)
				{
					char next = text[i + 1];
					if (next == 'N' || next == 'n')
					{
						builder.Append(' ');
						i++;
						continue;
					}
				}
				if (char.IsWhiteSpace(c))
				{
					builder.Append(' ');
					continue;
				}
				builder.Append(char.ToLowerInvariant(c));
			}
			// 折叠连续空白。
			string collapsed = builder.ToString();
			StringBuilder compact = new StringBuilder(collapsed.Length);
			bool previousSpace = false;
			foreach (char c in collapsed)
			{
				if (c == ' ')
				{
					if (!previousSpace && compact.Length > 0)
					{
						compact.Append(' ');
					}
					previousSpace = true;
					continue;
				}
				previousSpace = false;
				compact.Append(c);
			}
			return compact.ToString().Trim();
		}

		/// <summary>毫秒 → <c>HH:MM:SS.mmm</c>（小时不折行，便于超长视频）。</summary>
		internal static string FormatTime(long milliseconds)
		{
			if (milliseconds < 0L)
			{
				milliseconds = 0L;
			}
			long hours = milliseconds / 3600000L;
			long minutes = milliseconds % 3600000L / 60000L;
			long seconds = milliseconds % 60000L / 1000L;
			long millis = milliseconds % 1000L;
			return string.Format("{0:00}:{1:00}:{2:00}.{3:000}", hours, minutes, seconds, millis);
		}

		/// <summary>毫秒 → <c>MM:SS</c>（时间轴刻度用，短一些）。</summary>
		internal static string FormatClock(long milliseconds)
		{
			if (milliseconds < 0L)
			{
				milliseconds = 0L;
			}
			long minutes = milliseconds / 60000L;
			long seconds = milliseconds % 60000L / 1000L;
			return string.Format("{0:00}:{1:00}", minutes, seconds);
		}
	}

	/// <summary>
	/// 字幕对齐：不按行号硬配，而是「先认文本相同的、再看时间靠近的」，把两侧 cue 配成
	/// 相同 / 已变 / 仅左 / 仅右 四类。
	///
	/// 走双指针 + 有限前瞻：当前两条文本相同即判「相同」；否则在窗口内找「右侧多了一条」
	/// （左指针这条与右侧后面某条相同）或「左侧少了一条」，都不成立再判「已变」。这样
	/// 中间插入 / 删除一条字幕不会把后面全部错位成「已变」。
	/// </summary>
	internal static class SubtitleDiff
	{
		/// <summary>前瞻窗口：最多向前找这么多条来判定插入 / 删除。</summary>
		private const int Lookahead = 8;

		/// <summary>判定「已变」而非「一增一删」的起始时间接近阈值。</summary>
		private const long PairToleranceMs = 4000L;

		internal static SubtitleDiffResult Compute(List<SubtitleCue> left, List<SubtitleCue> right)
		{
			SubtitleDiffResult result = new SubtitleDiffResult();
			int li = 0;
			int ri = 0;
			int leftCount = left?.Count ?? 0;
			int rightCount = right?.Count ?? 0;

			while (li < leftCount && ri < rightCount)
			{
				SubtitleCue leftCue = left[li];
				SubtitleCue rightCue = right[ri];
				if (SameText(leftCue, rightCue))
				{
					Add(result, leftCue, rightCue, CueState.Same);
					li++;
					ri++;
					continue;
				}

				int rightAhead = FindAhead(left, li, right, ri + 1, rightCount);
				int leftAhead = FindAhead(right, ri, left, li + 1, leftCount);

				// 两侧都能找到：取更近的那个，避免把「一增一删」误判成连续「已变」。
				if (rightAhead >= 0 && (leftAhead < 0 || rightAhead - ri <= leftAhead - li))
				{
					for (int k = ri; k < rightAhead; k++)
					{
						Add(result, null, right[k], CueState.RightOnly);
					}
					ri = rightAhead;
					continue;
				}
				if (leftAhead >= 0)
				{
					for (int k = li; k < leftAhead; k++)
					{
						Add(result, left[k], null, CueState.LeftOnly);
					}
					li = leftAhead;
					continue;
				}

				// 时间差得远、文本也完全不同：当成「一增一删」比当成「已变」更诚实。
				if (Math.Abs(leftCue.StartMs - rightCue.StartMs) > PairToleranceMs)
				{
					Add(result, leftCue, null, CueState.LeftOnly);
					Add(result, null, rightCue, CueState.RightOnly);
					li++;
					ri++;
					continue;
				}

				Add(result, leftCue, rightCue, CueState.Changed);
				li++;
				ri++;
			}

			for (; li < leftCount; li++)
			{
				Add(result, left[li], null, CueState.LeftOnly);
			}
			for (; ri < rightCount; ri++)
			{
				Add(result, null, right[ri], CueState.RightOnly);
			}
			return result;
		}

		/// <summary>在 <paramref name="probe"/> 的 <paramref name="from"/> 起窗口内找与给定 cue 文本相同的一条。</summary>
		private static int FindAhead(List<SubtitleCue> target, int targetIndex, List<SubtitleCue> probe, int from, int probeCount)
		{
			if (target == null || probe == null || targetIndex >= target.Count)
			{
				return -1;
			}
			int limit = Math.Min(probeCount, from + Lookahead);
			for (int k = from; k < limit; k++)
			{
				if (SameText(target[targetIndex], probe[k]))
				{
					return k;
				}
			}
			return -1;
		}

		private static bool SameText(SubtitleCue a, SubtitleCue b)
		{
			return string.Equals(SubtitleText.Normalize(a.Text), SubtitleText.Normalize(b.Text), StringComparison.Ordinal);
		}

		private static void Add(SubtitleDiffResult result, SubtitleCue left, SubtitleCue right, CueState state)
		{
			result.Rows.Add(new CueRow
			{
				Left = left,
				Right = right,
				State = state
			});
			switch (state)
			{
			case CueState.Same:
				result.Same++;
				break;
			case CueState.Changed:
				result.Changed++;
				break;
			case CueState.LeftOnly:
				result.LeftOnly++;
				break;
			default:
				result.RightOnly++;
				break;
			}
		}
	}
}
