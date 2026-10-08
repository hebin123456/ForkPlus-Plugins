using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ForkPlus.Plugins.Midi
{
	/// <summary>
	/// SMF（Standard MIDI File，.mid / .midi）解析：块结构 "MThd"（format / ntrks / division，大端）
	/// + 若干 "MTrk" 音轨块，读成同一套 <see cref="MidiDocument"/>（跨音轨合并的音符列表）。
	///
	/// 要点：
	/// <list type="bullet">
	/// <item>delta 时间为 varint（每 7 位一组，最高位延续）；通道消息支持 running status
	/// （状态字节最高位为 0 时复用上一状态）。</item>
	/// <item>note on（vel&gt;0）压栈、note off 弹栈配对（同 (通道, 音高) LIFO），产出
	/// <see cref="MidiNote"/>；音轨结束仍未关的音符按音轨末尾收束（防御）。</item>
	/// <item>tempo：收集所有音轨的 0x51 事件（绝对 tick, 微秒/quarter），与默认 500000µs 一起按
	/// tick 排序（同 tick 后者胜出），tick → 毫秒按分段积分；SMPTE division（最高位为 1）直接
	/// 线性换算（每 tick 毫秒 = 1000/(fps×ticksPerFrame)），不走 tempo。</item>
	/// <item>未知块类型读长度走人；0xF0/0xF7 sysex 与 0xFF meta 按 varint 长度跳过；
	/// 其它 0xFn 系统消息按 MIDI 规范跳过合适数据字节。</item>
	/// </list>
	///
	/// 解析失败不抛异常：返回 <c>null</c> 并回填 <paramref name="error"/>，视图按「无法解析」提示处理。
	/// 防御：截断数据、长度越界抛清晰异常（在内部捕获转 error）；格式 0 / 1 / 2 均支持；
	/// 音符超 <see cref="MaxNotesPerSide"/> 截断。
	/// </summary>
	internal static class MidiParser
	{
		/// <summary>单侧收集的音符上限：超出截断（防超大文件拖死 UI），文档上以 <c>NotesTruncated</c> 标注。</summary>
		internal const int MaxNotesPerSide = 20000;

		/// <summary>缺省 tempo：500000 微秒 / quarter（即 120 bpm）。</summary>
		private const int DefaultTempoUs = 500000;

		/// <summary>解析入口。成功返回文档，失败返回 null 并回填 error。</summary>
		internal static MidiDocument Parse(byte[] data, out string error)
		{
			error = null;
			if (data == null || data.Length < 14)
			{
				error = "not a standard MIDI file (data too short)";
				return null;
			}
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

		private static MidiDocument ParseCore(byte[] data)
		{
			Reader reader = new Reader(data);
			if (!reader.MatchTag("MThd"))
			{
				throw new FormatException("not a standard MIDI file (missing MThd chunk)");
			}
			reader.Skip(4);
			uint headerLength = reader.ReadUInt32();
			if (headerLength < 6u)
			{
				throw new FormatException("invalid MThd chunk length " + headerLength);
			}
			int format = reader.ReadUInt16();
			// 声明音轨数仅读取：以实际解析到的 MTrk 块为准（防御头部长度不实）。
			reader.ReadUInt16();
			int division = reader.ReadUInt16();
			reader.Skip((int)(headerLength - 6u));

			List<TempoEvent> tempos = new List<TempoEvent> { new TempoEvent(0L, DefaultTempoUs) };
			List<RawNote> rawNotes = new List<RawNote>();
			List<MidiTrackInfo> tracks = new List<MidiTrackInfo>();
			int trackIndex = 0;
			// 块循环：MTrk 逐个解析；未知块类型读长度走人（读不到长度即到末尾为止）。
			while (reader.HasBytes(8))
			{
				string tag = reader.ReadTag();
				uint length = reader.ReadUInt32();
				if (string.Equals(tag, "MTrk", StringComparison.Ordinal))
				{
					string name = ParseTrack(reader, length, trackIndex, rawNotes, tempos);
					tracks.Add(new MidiTrackInfo { Index = trackIndex, Name = name });
					trackIndex++;
				}
				else
				{
					reader.Skip((int)length);
				}
			}

			// tick → 毫秒换算（SMPTE division 线性；否则按 tempo 分段积分）。
			TempoMap map = new TempoMap(tempos, division);
			List<MidiNote> notes = new List<MidiNote>(rawNotes.Count);
			foreach (RawNote raw in rawNotes)
			{
				double startMs = map.EvaluateMs(raw.StartTick);
				double endMs = map.EvaluateMs(raw.EndTick);
				if (endMs < startMs)
				{
					endMs = startMs;
				}
				notes.Add(new MidiNote
				{
					Track = raw.Track,
					Channel = raw.Channel,
					Pitch = raw.Pitch,
					Velocity = raw.Velocity,
					StartMs = startMs,
					DurationMs = endMs - startMs
				});
			}
			// 按 StartMs 稳定排序（OrderBy 为稳定排序，同刻音符保持出现次序）。
			List<MidiNote> sorted = notes.OrderBy(n => n.StartMs).ToList();
			bool truncated = sorted.Count > MaxNotesPerSide;
			if (truncated)
			{
				sorted.RemoveRange(MaxNotesPerSide, sorted.Count - MaxNotesPerSide);
			}
			// 每音轨音符数（截断后实际保留的）。
			int[] perTrack = new int[trackIndex];
			foreach (MidiNote note in sorted)
			{
				if (note.Track >= 0 && note.Track < perTrack.Length)
				{
					perTrack[note.Track]++;
				}
			}
			foreach (MidiTrackInfo info in tracks)
			{
				info.NoteCount = perTrack[info.Index];
			}

			MidiDocument document = new MidiDocument
			{
				Format = "SMF " + format,
				DivisionDescribe = DescribeDivision(division),
				Tracks = tracks,
				Notes = sorted,
				NotesTruncated = truncated,
				TempoChangeCount = tempos.Count - 1
			};
			document.TotalMs = ComputeTotalMs(sorted);
			return document;
		}

		// ---- 音轨 ----

		/// <summary>解析一个 MTrk 块：事件循环（delta varint + running status），收集音符与 tempo，返回音轨名。</summary>
		private static string ParseTrack(Reader reader, uint length, int trackIndex, List<RawNote> notes, List<TempoEvent> tempos)
		{
			Reader track = reader.Slice(length);
			long absTick = 0L;
			int runningStatus = 0;
			string name = null;
			Dictionary<int, Stack<RawNote>> open = new Dictionary<int, Stack<RawNote>>();
			while (track.HasMore)
			{
				absTick += track.ReadVarint();
				int status = track.PeekByte();
				if (status < 0x80)
				{
					// running status：数据字节复用上一状态字节（自身不消费）。
					if (runningStatus == 0)
					{
						throw new FormatException("track " + trackIndex + ": running status without a prior status byte");
					}
					status = runningStatus;
				}
				else
				{
					track.ReadByte();
					if (status < 0xF0)
					{
						runningStatus = status;
					}
					else
					{
						runningStatus = 0;
					}
				}

				if (status < 0xF0)
				{
					// 通道消息（0x8n..0xEn）。
					int channel = status & 0x0F;
					switch (status & 0xF0)
					{
					case 0x80: // note off
						{
							int pitch = track.ReadByte();
							track.ReadByte();
							CloseNote(open, channel, pitch, absTick, notes);
							break;
						}
					case 0x90: // note on（vel = 0 视为 off）
						{
							int pitch = track.ReadByte();
							int velocity = track.ReadByte();
							if (velocity == 0)
							{
								CloseNote(open, channel, pitch, absTick, notes);
							}
							else
							{
								GetStack(open, channel, pitch).Push(new RawNote
								{
									Track = trackIndex,
									Channel = channel,
									Pitch = pitch,
									Velocity = velocity,
									StartTick = absTick,
									EndTick = absTick
								});
							}
							break;
						}
					case 0xA0: // poly pressure
					case 0xB0: // controller
					case 0xE0: // pitch bend
						track.Skip(2);
						break;
					default: // 0xC0 program / 0xD0 channel pressure
						track.Skip(1);
						break;
					}
					continue;
				}

				if (status == 0xFF)
				{
					// meta：类型字节 + varint 长度 + 数据。
					int type = track.ReadByte();
					int metaLength = (int)track.ReadVarint();
					switch (type)
					{
					case 0x51: // tempo（微秒 / quarter，3 字节）
						if (metaLength >= 3)
						{
							int usPerQuarter = (track.ReadByte() << 16) | (track.ReadByte() << 8) | track.ReadByte();
							tempos.Add(new TempoEvent(absTick, usPerQuarter));
							track.Skip(metaLength - 3);
						}
						else
						{
							track.Skip(metaLength);
						}
						break;
					case 0x03: // 音轨名
						name = SanitizeName(track.ReadBytes(metaLength));
						break;
					case 0x2F: // end of track
						track.Skip(metaLength);
						goto EndOfTrack;
					default:
						// 0x58 拍号 / 0x21 端口 / 其它：跳过。
						track.Skip(metaLength);
						break;
					}
					continue;
				}

				if (status == 0xF0 || status == 0xF7)
				{
					// sysex：varint 长度 + 数据。
					track.Skip((int)track.ReadVarint());
					continue;
				}

				switch (status)
				{
				case 0xF1: // quarter frame
				case 0xF3: // song select
					track.Skip(1);
					break;
				case 0xF2: // song position
					track.Skip(2);
					break;
				default:
					// 0xF6 / 0xF8..0xFE：零数据字节；0xF4 / 0xF5 未定义，同样零字节容错。
					break;
				}
			}
			EndOfTrack:
			// 防御：音轨结束仍未收到 note off 的音符，按音轨末尾绝对 tick 收束。
			foreach (Stack<RawNote> stack in open.Values)
			{
				foreach (RawNote note in stack)
				{
					note.EndTick = absTick;
					notes.Add(note);
				}
				stack.Clear();
			}
			return name;
		}

		/// <summary>note off 配对：同 (通道, 音高) 的栈 LIFO 弹出；孤儿 off（无对应 on）忽略。</summary>
		private static void CloseNote(Dictionary<int, Stack<RawNote>> open, int channel, int pitch, long tick, List<RawNote> notes)
		{
			int key = (channel << 8) | pitch;
			if (!open.TryGetValue(key, out Stack<RawNote> stack) || stack.Count == 0)
			{
				return;
			}
			RawNote note = stack.Pop();
			note.EndTick = tick;
			notes.Add(note);
		}

		private static Stack<RawNote> GetStack(Dictionary<int, Stack<RawNote>> open, int channel, int pitch)
		{
			int key = (channel << 8) | pitch;
			if (!open.TryGetValue(key, out Stack<RawNote> stack))
			{
				stack = new Stack<RawNote>();
				open.Add(key, stack);
			}
			return stack;
		}

		/// <summary>音轨名清洗：UTF-8 解码，控制字符换空格并去首尾空白；全空返回 null。</summary>
		private static string SanitizeName(byte[] raw)
		{
			if (raw == null || raw.Length == 0)
			{
				return null;
			}
			string text = Encoding.UTF8.GetString(raw);
			StringBuilder builder = new StringBuilder(text.Length);
			foreach (char c in text)
			{
				builder.Append(c < ' ' ? ' ' : c);
			}
			string cleaned = builder.ToString().Trim();
			return cleaned.Length == 0 ? null : cleaned;
		}

		/// <summary>division 描述："384 ticks/quarter" 或 "SMPTE 30 fps × 100"。</summary>
		private static string DescribeDivision(int division)
		{
			if ((division & 0x8000) == 0)
			{
				return (division & 0x7FFF) + " ticks/quarter";
			}
			// SMPTE：高字节为负的 fps（256 − 高字节），低字节为每帧 tick 数。
			int fps = 256 - ((division >> 8) & 0xFF);
			int ticksPerFrame = division & 0xFF;
			return "SMPTE " + fps + " fps × " + ticksPerFrame;
		}

		private static double ComputeTotalMs(List<MidiNote> notes)
		{
			double total = 0.0;
			foreach (MidiNote note in notes)
			{
				if (note.EndMs > total)
				{
					total = note.EndMs;
				}
			}
			return total;
		}

		// ---- tempo 映射 ----

		/// <summary>一个 tempo 变化点：绝对 tick + 微秒 / quarter。</summary>
		private readonly struct TempoEvent
		{
			internal readonly long Tick;

			internal readonly int UsPerQuarter;

			internal TempoEvent(long tick, int usPerQuarter)
			{
				Tick = tick;
				UsPerQuarter = usPerQuarter;
			}
		}

		/// <summary>
		/// tick → 毫秒换算：SMPTE division 线性换算（每 tick 毫秒 = 1000/(fps×ticksPerFrame)）；
		/// ticks/quarter division 按 tempo 分段积分（段内每 tick 微秒 = tempo/division）。
		/// </summary>
		private sealed class TempoMap
		{
			private readonly bool _smpte;

			private readonly int _fps;

			private readonly int _ticksPerFrame;

			private readonly int _division;

			private readonly List<TempoEvent> _segments;

			internal TempoMap(List<TempoEvent> events, int division)
			{
				if ((division & 0x8000) != 0)
				{
					_smpte = true;
					_fps = 256 - ((division >> 8) & 0xFF);
					_ticksPerFrame = division & 0xFF;
					_segments = null;
					return;
				}
				_division = division > 0 ? division : 1;
				// 排序（稳定）+ 同 tick 去重：后出现者胜出，作为该 tick 起的段 tempo。
				List<TempoEvent> sorted = events.OrderBy(e => e.Tick).ToList();
				_segments = new List<TempoEvent>(sorted.Count);
				foreach (TempoEvent item in sorted)
				{
					if (_segments.Count > 0 && _segments[_segments.Count - 1].Tick == item.Tick)
					{
						_segments[_segments.Count - 1] = item;
					}
					else
					{
						_segments.Add(item);
					}
				}
			}

			internal double EvaluateMs(long tick)
			{
				if (tick <= 0L)
				{
					return 0.0;
				}
				if (_smpte)
				{
					if (_fps <= 0 || _ticksPerFrame <= 0)
					{
						return (double)tick;
					}
					return (double)tick * 1000.0 / ((double)_fps * (double)_ticksPerFrame);
				}
				double milliseconds = 0.0;
				for (int i = 0; i < _segments.Count; i++)
				{
					long segmentStart = _segments[i].Tick;
					if (tick <= segmentStart)
					{
						break;
					}
					long segmentEnd = i + 1 < _segments.Count ? _segments[i + 1].Tick : long.MaxValue;
					long span = Math.Min(tick, segmentEnd) - segmentStart;
					int tempo = _segments[i].UsPerQuarter > 0 ? _segments[i].UsPerQuarter : DefaultTempoUs;
					milliseconds += (double)span * tempo / _division / 1000.0;
					if (tick < segmentEnd)
					{
						break;
					}
				}
				return milliseconds;
			}
		}

		// ---- 字节读取 ----

		/// <summary>解析中收集的原始音符（tick 域，待 tempo 映射换算成毫秒）。</summary>
		private sealed class RawNote
		{
			internal int Track;

			internal int Channel;

			internal int Pitch;

			internal int Velocity;

			internal long StartTick;

			internal long EndTick;
		}

		/// <summary>大端字节读取器：越界抛带偏移的清晰异常；Skip 对截断数据钳到末尾（防御）。</summary>
		private sealed class Reader
		{
			private readonly byte[] _data;

			private readonly int _end;

			private int _offset;

			internal Reader(byte[] data)
			{
				_data = data;
				_offset = 0;
				_end = data.Length;
			}

			private Reader(byte[] data, int start, int end)
			{
				_data = data;
				_offset = start;
				_end = end;
			}

			internal bool HasMore => _offset < _end;

			internal bool HasBytes(int count)
			{
				return _end - _offset >= count;
			}

			internal byte ReadByte()
			{
				if (_offset >= _end)
				{
					throw new FormatException("unexpected end of MIDI data at offset " + _offset);
				}
				return _data[_offset++];
			}

			internal int PeekByte()
			{
				if (_offset >= _end)
				{
					throw new FormatException("unexpected end of MIDI data at offset " + _offset);
				}
				return _data[_offset];
			}

			internal ushort ReadUInt16()
			{
				return (ushort)((ReadByte() << 8) | ReadByte());
			}

			internal uint ReadUInt32()
			{
				return ((uint)ReadByte() << 24) | ((uint)ReadByte() << 16) | ((uint)ReadByte() << 8) | ReadByte();
			}

			/// <summary>SMF delta / 长度 varint：每 7 位一组，最高位延续，最多 4 组。</summary>
			internal long ReadVarint()
			{
				long value = 0L;
				for (int i = 0; i < 4; i++)
				{
					int b = ReadByte();
					value = (value << 7) | (long)(b & 0x7F);
					if ((b & 0x80) == 0)
					{
						return value;
					}
				}
				throw new FormatException("invalid MIDI variable-length quantity at offset " + _offset);
			}

			internal bool MatchTag(string tag)
			{
				if (!HasBytes(4))
				{
					return false;
				}
				for (int i = 0; i < 4; i++)
				{
					if (_data[_offset + i] != (byte)tag[i])
					{
						return false;
					}
				}
				return true;
			}

			internal string ReadTag()
			{
				return string.Concat((char)ReadByte(), (char)ReadByte(), (char)ReadByte(), (char)ReadByte());
			}

			internal byte[] ReadBytes(int count)
			{
				if (count < 0)
				{
					count = 0;
				}
				if (_end - _offset < count)
				{
					throw new FormatException("unexpected end of MIDI data at offset " + _offset);
				}
				byte[] result = new byte[count];
				Array.Copy(_data, _offset, result, 0, count);
				_offset += count;
				return result;
			}

			internal void Skip(int count)
			{
				if (count <= 0)
				{
					return;
				}
				// 截断数据：声明长度超过剩余时钳到末尾。
				_offset = _end - _offset < count ? _end : _offset + count;
			}

			/// <summary>切出限定范围的子读取器（MTrk 块）；声明长度超过剩余时钳到末尾。</summary>
			internal Reader Slice(uint length)
			{
				int count = (int)Math.Min(length, (uint)(_end - _offset));
				Reader slice = new Reader(_data, _offset, _offset + count);
				_offset += count;
				return slice;
			}
		}
	}
}
