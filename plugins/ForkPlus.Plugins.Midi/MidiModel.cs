using System;
using System.Collections.Generic;
using System.Linq;

namespace ForkPlus.Plugins.Midi
{
	/// <summary>一条音符：音轨 / 通道 / 音高 / 力度 + 起止毫秒（tick 经 tempo 映射换算而来）。</summary>
	internal sealed class MidiNote
	{
		/// <summary>所在音轨号（从 0 起）。</summary>
		internal int Track;

		/// <summary>MIDI 通道（0..15）。</summary>
		internal int Channel;

		/// <summary>音高（0..127）。</summary>
		internal int Pitch;

		/// <summary>力度（note on 的 velocity，1..127）。</summary>
		internal int Velocity;

		/// <summary>起始毫秒（tick → ms 换算，可能带小数）。</summary>
		internal double StartMs;

		/// <summary>时长毫秒（结束 − 起始，可能带小数）。</summary>
		internal double DurationMs;

		/// <summary>结束毫秒 = 起始 + 时长。</summary>
		internal double EndMs => StartMs + DurationMs;
	}

	/// <summary>音轨摘要：序号 + 音轨名（meta 0x03）+ 该轨音符数。</summary>
	internal sealed class MidiTrackInfo
	{
		internal int Index;

		/// <summary>meta 0x03 音轨名；未声明为 null。</summary>
		internal string Name;

		internal int NoteCount;
	}

	/// <summary>
	/// 一次解析的结果：SMF 格式名 + division 描述 + 音轨摘要 + 音符列表。
	/// 音符按 <see cref="MidiNote.StartMs"/> 稳定排序；<see cref="TotalMs"/> 取两侧音符结束的最大毫秒。
	/// </summary>
	internal sealed class MidiDocument
	{
		/// <summary>SMF 格式名："SMF 0" / "SMF 1" / "SMF 2"。</summary>
		internal string Format = "?";

		/// <summary>division 描述："384 ticks/quarter" 或 "SMPTE 30 fps × 100"。</summary>
		internal string DivisionDescribe;

		internal List<MidiTrackInfo> Tracks = new List<MidiTrackInfo>();

		/// <summary>全部音符（跨音轨合并，按 StartMs 稳定排序；超上限截断）。</summary>
		internal List<MidiNote> Notes = new List<MidiNote>();

		/// <summary>时间轴总长：全部音符结束毫秒的最大值。</summary>
		internal double TotalMs;

		/// <summary>0x51 tempo 事件个数（不含默认 500000µs 项）。</summary>
		internal int TempoChangeCount;

		/// <summary>音符收集超过 <see cref="MidiParser.MaxNotesPerSide"/> 被截断时为 true。</summary>
		internal bool NotesTruncated;
	}

	internal enum NoteState
	{
		Same,
		Changed,
		LeftOnly,
		RightOnly
	}

	/// <summary>对齐后的一行：同一位置在旧 / 新两侧的音符与变更状态。</summary>
	internal sealed class NoteRow
	{
		internal MidiNote Left;

		internal MidiNote Right;

		internal NoteState State;
	}

	internal sealed class NoteDiffResult
	{
		internal List<NoteRow> Rows = new List<NoteRow>();

		internal int Same;

		internal int Changed;

		internal int LeftOnly;

		internal int RightOnly;

		internal int Total => Rows.Count;

		internal bool HasDifferences => Changed > 0 || LeftOnly > 0 || RightOnly > 0;
	}

	/// <summary>音符名与时间格式化的小工具。</summary>
	internal static class MidiText
	{
		private static readonly string[] NoteNames = new string[12]
		{
			"C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
		};

		/// <summary>音高 → 音名（如 60 → "C4"；科学音高记法，pitch/12 − 1 为组号）。</summary>
		internal static string NoteName(int pitch)
		{
			if (pitch < 0 || pitch > 127)
			{
				return "?" + pitch;
			}
			return NoteNames[pitch % 12] + (pitch / 12 - 1);
		}

		/// <summary>毫秒 → <c>MM:SS.mmm</c>（音符起止与 ToolTip 用）。</summary>
		internal static string FormatNoteTime(double milliseconds)
		{
			long value = (long)Math.Round(milliseconds > 0.0 ? milliseconds : 0.0);
			long minutes = value / 60000L;
			long seconds = value % 60000L / 1000L;
			long millis = value % 1000L;
			return string.Format("{0:00}:{1:00}.{2:000}", minutes, seconds, millis);
		}

		/// <summary>毫秒 → <c>MM:SS</c>（时间轴刻度用，短一些）。</summary>
		internal static string FormatClock(double milliseconds)
		{
			long value = (long)Math.Round(milliseconds > 0.0 ? milliseconds : 0.0);
			long minutes = value / 60000L;
			long seconds = value % 60000L / 1000L;
			return string.Format("{0:00}:{1:00}", minutes, seconds);
		}
	}

	/// <summary>
	/// 音符对齐：不按出现次序硬配，而是按 key =（通道, 音高, 起始毫秒取整）建字典配对；
	/// 同 key 多音符（同刻重叠音）时逐个消耗。配对后比较力度与时长
	/// （容差 ≤1 / ≤3ms 视为相同），不等判「已变」；只有一侧有则「仅左 / 仅右」。
	/// </summary>
	internal static class MidiDiff
	{
		/// <summary>时长容差（毫秒）：不大于此差值视为相同。</summary>
		private const double DurationToleranceMs = 3.0;

		/// <summary>力度容差：不大于此差值视为相同。</summary>
		private const int VelocityTolerance = 1;

		internal static NoteDiffResult Compute(List<MidiNote> left, List<MidiNote> right)
		{
			NoteDiffResult result = new NoteDiffResult();
			Dictionary<NoteKey, Queue<MidiNote>> rightMap = new Dictionary<NoteKey, Queue<MidiNote>>();
			if (right != null)
			{
				foreach (MidiNote note in right)
				{
					GetQueue(rightMap, new NoteKey(note)).Enqueue(note);
				}
			}
			if (left != null)
			{
				foreach (MidiNote note in left)
				{
					Queue<MidiNote> queue;
					if (rightMap.TryGetValue(new NoteKey(note), out queue) && queue.Count > 0)
					{
						MidiNote match = queue.Dequeue();
						Add(result, note, match, ValuesEqual(note, match) ? NoteState.Same : NoteState.Changed);
					}
					else
					{
						Add(result, note, null, NoteState.LeftOnly);
					}
				}
			}
			// 右侧剩余未配对的音符（按插入次序枚举各队列，最后统一排序）。
			foreach (Queue<MidiNote> queue in rightMap.Values)
			{
				foreach (MidiNote note in queue)
				{
					Add(result, null, note, NoteState.RightOnly);
				}
			}
			// 按左 / 右 StartMs 稳定排序（OrderBy 为稳定排序）。
			result.Rows = result.Rows.OrderBy(r => EffectiveStart(r)).ToList();
			return result;
		}

		private static double EffectiveStart(NoteRow row)
		{
			return row.Left != null ? row.Left.StartMs : row.Right.StartMs;
		}

		private static bool ValuesEqual(MidiNote a, MidiNote b)
		{
			return Math.Abs(a.DurationMs - b.DurationMs) <= DurationToleranceMs &&
				Math.Abs(a.Velocity - b.Velocity) <= VelocityTolerance;
		}

		private static Queue<MidiNote> GetQueue(Dictionary<NoteKey, Queue<MidiNote>> map, NoteKey key)
		{
			if (!map.TryGetValue(key, out Queue<MidiNote> queue))
			{
				queue = new Queue<MidiNote>();
				map.Add(key, queue);
			}
			return queue;
		}

		private static void Add(NoteDiffResult result, MidiNote left, MidiNote right, NoteState state)
		{
			result.Rows.Add(new NoteRow
			{
				Left = left,
				Right = right,
				State = state
			});
			switch (state)
			{
			case NoteState.Same:
				result.Same++;
				break;
			case NoteState.Changed:
				result.Changed++;
				break;
			case NoteState.LeftOnly:
				result.LeftOnly++;
				break;
			default:
				result.RightOnly++;
				break;
			}
		}

		/// <summary>配对 key：通道 + 音高 + 起始毫秒取整（<see cref="MidiNote.StartMs"/> 带小数，取整后精确相等）。</summary>
		private readonly struct NoteKey : IEquatable<NoteKey>
		{
			private readonly int _channel;

			private readonly int _pitch;

			private readonly long _startMs;

			internal NoteKey(MidiNote note)
			{
				_channel = note.Channel;
				_pitch = note.Pitch;
				_startMs = (long)Math.Round(note.StartMs);
			}

			public bool Equals(NoteKey other)
			{
				return _channel == other._channel && _pitch == other._pitch && _startMs == other._startMs;
			}

			public override bool Equals(object obj)
			{
				return obj is NoteKey other && Equals(other);
			}

			public override int GetHashCode()
			{
				return (_channel * 397 ^ _pitch) * 397 ^ _startMs.GetHashCode();
			}
		}
	}
}
