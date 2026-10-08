using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Midi
{
	/// <summary>
	/// MIDI 对比视图：把旧（左）/ 新（右）两侧的 SMF 解析成同一套音符模型，先按
	/// （通道, 音高, 起始毫秒）配对、再比较力度与时长，得到「相同 / 已变 / 仅左 / 仅右」四类。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、
	/// 自带的分段模式工具条（音符表 / 钢琴卷帘），最下是内容区。
	///
	/// 两个模式：
	/// <list type="bullet">
	/// <item>音符表（默认）：一张五列表——「旧时间 | 旧音符 | 新时间 | 新音符 | 状态」，
	/// 逐行按四色标注底色，是看「哪个音改了」的首选。</item>
	/// <item>钢琴卷帘时间轴：上下两条轨道（旧 / 新）按总时长等比铺开，每条音符画成一根
	/// 音高行色条（y = 音高，x = 起止时间），一眼看出音符在时间上的增删与挪动；
	/// 顶部是 MM:SS 刻度尺。</item>
	/// </list>
	///
	/// 解析与对齐在后台线程执行，控件只在 UI 线程构建；每次刷新用代次 + CancellationToken 取消上一轮。
	/// 单侧声明大小 &gt; 300 MB 只给提示、不解析；单侧音符超 <see cref="MidiParser.MaxNotesPerSide"/>
	/// 由解析器截断并在状态行注明。
	/// </summary>
	public sealed class MidiDiffView : IDiffView
	{
		private const string NotesMode = "notes";

		private const string PianoRollMode = "pianoroll";

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （pianoroll，其余走默认 notes），据此逐模式取图；正常运行时该变量为空，走默认音符表模式。
		/// </summary>
		private static readonly string InitialMode = ResolveInitialMode();

		private static string ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			return string.Equals(forced, PianoRollMode, StringComparison.OrdinalIgnoreCase) ? PianoRollMode : NotesMode;
		}

		/// <summary>单侧超过此大小不解析（与音视频 / 结构化数据插件同口径）。</summary>
		private const long MaxSideBytes = 300L * 1024L * 1024L;

		/// <summary>音符表渲染上限：避免超大 MIDI 把 UI 拖死。</summary>
		private const int MaxRows = 2000;

		/// <summary>时间轴的逻辑宽度（像素）；配合横向滚动，比例刻度稳定可预期。</summary>
		private const double TimelineWidth = 1200.0;

		/// <summary>钢琴卷帘的音高行高（色条高 = 行高 − 1）。</summary>
		private const double RowHeight = 6.0;

		/// <summary>轨道左侧标签列宽。</summary>
		private const double LabelWidth = 56.0;

		private const int RulerTicks = 10;

		private static readonly IBrush GridLine = Brushes.Gainsboro;

		private static readonly IBrush Subtle = new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x80, 0x80));

		private static readonly IBrush TintChanged = new SolidColorBrush(Color.FromArgb(0x2E, 0xE6, 0x7E, 0x22));

		private static readonly IBrush TintLeftOnly = new SolidColorBrush(Color.FromArgb(0x26, 0xC0, 0x39, 0x2B));

		private static readonly IBrush TintRightOnly = new SolidColorBrush(Color.FromArgb(0x26, 0x2E, 0x9E, 0x5B));

		private static readonly IBrush BarSame = new SolidColorBrush(Color.FromArgb(0x59, 0x80, 0x80, 0x80));

		private static readonly IBrush BarChanged = new SolidColorBrush(Color.FromArgb(0xCC, 0xE6, 0x7E, 0x22));

		private static readonly IBrush BarLeftOnly = new SolidColorBrush(Color.FromArgb(0xCC, 0xC0, 0x39, 0x2B));

		private static readonly IBrush BarRightOnly = new SolidColorBrush(Color.FromArgb(0xCC, 0x2E, 0x9E, 0x5B));

		private static readonly IBrush StateChanged = new SolidColorBrush(Color.FromArgb(0xFF, 0xB0, 0x5A, 0x00));

		private static readonly IBrush StateLeft = new SolidColorBrush(Color.FromArgb(0xFF, 0xC0, 0x39, 0x2B));

		private static readonly IBrush StateRight = new SolidColorBrush(Color.FromArgb(0xFF, 0x2E, 0x9E, 0x5B));

		private static readonly FontFamily MonoFont = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, Courier New, monospace");

		private readonly Grid _root;

		private readonly TextBlock _srcTitle;

		private readonly TextBlock _dstTitle;

		private readonly TextBlock _status;

		private readonly Button _notesButton;

		private readonly Button _pianoRollButton;

		private readonly ContentControl _content;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _generation;

		private string _mode = InitialMode;

		private MidiDocument _left;

		private MidiDocument _right;

		private NoteDiffResult _diff;

		private string _error;

		/// <summary>左 / 右两侧各自的解析错误（侧存在但解析失败时用于区分「解析失败」与「不存在」）。</summary>
		private string _srcParseError;

		private string _dstParseError;

		private bool _srcAbsent;

		private bool _dstAbsent;

		private bool _tooLarge;

		public MidiDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			_status = new TextBlock
			{
				Margin = new Thickness(12.0, 2.0, 12.0, 6.0),
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0
			};
			_notesButton = NewModeButton(MidiStrings.T("Notes"), delegate
			{
				SetMode(NotesMode);
			});
			_pianoRollButton = NewModeButton(MidiStrings.T("Piano roll"), delegate
			{
				SetMode(PianoRollMode);
			});
			_content = new ContentControl
			{
				Margin = new Thickness(12.0, 0.0, 12.0, 12.0)
			};

			Grid titleRow = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,*"),
				Margin = new Thickness(12.0, 10.0, 12.0, 4.0)
			};
			Grid.SetColumn(_srcTitle, 0);
			Grid.SetColumn(_dstTitle, 1);
			titleRow.Children.Add(_srcTitle);
			titleRow.Children.Add(_dstTitle);

			StackPanel modes = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Margin = new Thickness(12.0, 0.0, 12.0, 8.0)
			};
			modes.Children.Add(_notesButton);
			modes.Children.Add(_pianoRollButton);

			_root = new Grid
			{
				RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*")
			};
			Grid.SetRow(titleRow, 0);
			Grid.SetRow(_status, 1);
			Grid.SetRow(modes, 2);
			Grid.SetRow(_content, 3);
			_root.Children.Add(titleRow);
			_root.Children.Add(_status);
			_root.Children.Add(modes);
			_root.Children.Add(_content);

			StyleModeButton(_notesButton, _mode == NotesMode);
			StyleModeButton(_pianoRollButton, _mode == PianoRollMode);
			PluginEnvironment.ApplyLocalization(_root);
		}

		// ---- IDiffView ----

		Control IDiffView.View => _root;

		IReadOnlyList<DiffViewMode> IDiffView.Modes => Array.Empty<DiffViewMode>();

		event EventHandler<bool> IDiffView.HighlightPixelsAvailableChanged
		{
			add { }
			remove { }
		}

		public void SetContent(DiffViewContext context, IDiffViewHost host)
		{
			_context = context;
			_host = host;
			_srcAbsent = context?.Src == null;
			_dstAbsent = context?.Dst == null;
			UpdateTitles();
			PluginLog.Info($"MidiDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			Render();
		}

		public void SetMode(string modeId)
		{
			if (string.IsNullOrEmpty(modeId))
			{
				return;
			}
			if (modeId != NotesMode && modeId != PianoRollMode)
			{
				return;
			}
			if (modeId == _mode)
			{
				return;
			}
			_mode = modeId;
			StyleModeButton(_notesButton, _mode == NotesMode);
			StyleModeButton(_pianoRollButton, _mode == PianoRollMode);
			UpdateStatus();
			BuildContent();
		}

		public void Activate()
		{
		}

		public void Deactivate()
		{
		}

		public void ApplyLocalization()
		{
			_notesButton.Content = MidiStrings.T("Notes");
			_pianoRollButton.Content = MidiStrings.T("Piano roll");
			PluginEnvironment.ApplyLocalization(_root);
			UpdateTitles();
			UpdateStatus();
			BuildContent();
		}

		public void Release()
		{
			CancelRender();
			_context = null;
			_host = null;
			_left = null;
			_right = null;
			_diff = null;
			_content.Content = null;
			_status.Text = string.Empty;
		}

		// ---- 渲染 ----

		private void Render()
		{
			CancelRender();
			_left = null;
			_right = null;
			_diff = null;
			_error = null;
			_srcParseError = null;
			_dstParseError = null;
			_tooLarge = false;
			UpdateStatus();
			_content.Content = null;

			DiffViewContext context = _context;
			if (context == null)
			{
				return;
			}
			if (IsTooLarge(context.Src) || IsTooLarge(context.Dst))
			{
				_tooLarge = true;
				UpdateStatus();
				return;
			}

			CancellationTokenSource cts = new CancellationTokenSource();
			_cts = cts;
			int generation = ++_generation;
			DiffSideContent src = context.Src;
			DiffSideContent dst = context.Dst;
			MemoryStream hexSrc = context.HexSrc;
			MemoryStream hexDst = context.HexDst;

			Task.Run(delegate
			{
				MidiDocument left = null;
				MidiDocument right = null;
				string srcParseError = null;
				string dstParseError = null;
				try
				{
					byte[] leftData = ReadBytes(src, hexSrc, cts.Token);
					byte[] rightData = ReadBytes(dst, hexDst, cts.Token);
					cts.Token.ThrowIfCancellationRequested();
					if (src != null)
					{
						left = MidiParser.Parse(leftData, out srcParseError);
					}
					if (dst != null)
					{
						right = MidiParser.Parse(rightData, out dstParseError);
					}
					if (cts.Token.IsCancellationRequested)
					{
						return;
					}
					NoteDiffResult diff = MidiDiff.Compute(left?.Notes, right?.Notes);
					Dispatcher.UIThread.Post(delegate
					{
						if (_generation != generation || _cts != cts)
						{
							return;
						}
						_left = left;
						_right = right;
						_diff = diff;
						_srcParseError = srcParseError;
						_dstParseError = dstParseError;
						UpdateStatus();
						BuildContent();
					});
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex)
				{
					PluginLog.Error("MidiDiffView 渲染失败", ex);
					Dispatcher.UIThread.Post(delegate
					{
						if (_generation != generation || _cts != cts)
						{
							return;
						}
						_error = ex.GetType().Name + ": " + ex.Message;
						UpdateStatus();
					});
				}
			}, cts.Token);
		}

		private void CancelRender()
		{
			CancellationTokenSource cts = _cts;
			_cts = null;
			if (cts != null)
			{
				cts.Cancel();
				cts.Dispose();
			}
		}

		private static bool IsTooLarge(DiffSideContent side)
		{
			return side != null && side.Size.HasValue && side.Size.Value > MaxSideBytes;
		}

		/// <summary>取一侧字节：宿主 Hex 预载 &gt; 侧内容懒加载 &gt; LFS 本地缓存；拷成 byte[] 并恢复 Position。</summary>
		private byte[] ReadBytes(DiffSideContent side, MemoryStream preloaded, CancellationToken token)
		{
			if (side == null)
			{
				return null;
			}
			MemoryStream data = preloaded;
			if (data == null)
			{
				data = side.Data;
			}
			if (data == null && side.Lfs != null && _host != null)
			{
				data = _host.GetCachedLfsData(side.Lfs);
			}
			if (data == null)
			{
				return null;
			}
			token.ThrowIfCancellationRequested();
			long position = data.Position;
			try
			{
				// MemoryStream.ToArray 拷贝整个缓冲（与 Position 无关）；SMF 解析需要完整字节。
				return data.ToArray();
			}
			finally
			{
				try
				{
					data.Position = position;
				}
				catch (NotSupportedException)
				{
				}
			}
		}

		// ---- 标题 / 状态 ----

		private void UpdateTitles()
		{
			_srcTitle.Text = Describe(_context?.Src, _context?.SrcRole ?? DiffSideRole.Old);
			_dstTitle.Text = Describe(_context?.Dst, _context?.DstRole ?? DiffSideRole.New);
			_srcTitle.Foreground = _context?.SrcTitleBrush;
			_dstTitle.Foreground = _context?.DstTitleBrush;
		}

		private static string Describe(DiffSideContent side, DiffSideRole role)
		{
			string roleText = PluginEnvironment.Translate(RoleKey(role));
			if (side == null)
			{
				return roleText + "  ·  " + MidiStrings.T("not present");
			}
			string name = Path.GetFileName(side.Path) ?? string.Empty;
			string text = roleText + "  ·  " + name;
			if (side.Size.HasValue)
			{
				text = text + "  ·  " + PluginSizeFormat.ReadableFileSize(side.Size.Value);
			}
			return text;
		}

		private static string RoleKey(DiffSideRole role)
		{
			switch (role)
			{
			case DiffSideRole.Created:
				return "created";
			case DiffSideRole.Removed:
				return "removed";
			case DiffSideRole.New:
				return "new";
			default:
				return "old";
			}
		}

		/// <summary>优先展示的状态错误：意外异常 &gt; 左侧解析失败 &gt; 右侧解析失败。</summary>
		private string DisplayError()
		{
			return _error ?? _srcParseError ?? _dstParseError;
		}

		private void UpdateStatus()
		{
			if (_tooLarge)
			{
				_status.Text = MidiStrings.T("File too large to preview");
				return;
			}
			string error = DisplayError();
			if (error != null)
			{
				_status.Text = MidiStrings.F("Failed to parse: {0}", error);
				return;
			}
			if (_diff == null)
			{
				_status.Text = MidiStrings.T("Analyzing…");
				return;
			}
			string head = MidiStrings.F("MIDI compare: format {0} / {1}", DisplayFormat(_left), DisplayFormat(_right));
			head = head + "  ·  " + MidiStrings.F("tracks: {0} / {1}", _left?.Tracks.Count ?? 0, _right?.Tracks.Count ?? 0);
			head = head + "  ·  " + MidiStrings.F("notes: {0} / {1}", _left?.Notes.Count ?? 0, _right?.Notes.Count ?? 0);
			if ((_left != null && _left.NotesTruncated) || (_right != null && _right.NotesTruncated))
			{
				head = head + "  ·  " + MidiStrings.F("Notes truncated to {0} per side", MidiParser.MaxNotesPerSide);
			}
			if (!_diff.HasDifferences)
			{
				_status.Text = head + "  ·  " + MidiStrings.T("No differences");
				return;
			}
			_status.Text = head + "  ·  " + MidiStrings.F("Summary: {0} notes · {1} changed · {2} left only · {3} right only", _diff.Total, _diff.Changed, _diff.LeftOnly, _diff.RightOnly);
		}

		private static string DisplayFormat(MidiDocument document)
		{
			return document?.Format ?? "?";
		}

		// ---- 内容 ----

		private void BuildContent()
		{
			if (_content == null)
			{
				return;
			}
			if (_tooLarge)
			{
				_content.Content = NoteText(MidiStrings.T("File too large to preview"));
				return;
			}
			if (_diff == null)
			{
				_content.Content = NoteText(DisplayError() == null ? MidiStrings.T("Analyzing…") : MidiStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			// 两侧都存在却都解析失败（如扩展名对但内容不是 MIDI）：给错误占位，不再渲染空表 / 空卷帘。
			if (_left == null && _right == null && DisplayError() != null)
			{
				_content.Content = NoteText(MidiStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			if (_mode == PianoRollMode)
			{
				_content.Content = BuildPianoRollContent();
			}
			else
			{
				_content.Content = BuildNotesContent();
			}
		}

		// ---- 音符表 ----

		private Control BuildNotesContent()
		{
			Grid table = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("Auto,2*,Auto,2*,Auto")
			};
			int row = 0;
			AddTableRow(table, ref row, MidiStrings.T("Old time"), MidiStrings.T("Old"), MidiStrings.T("New time"), MidiStrings.T("New"), MidiStrings.T("State"), null, null, null, true);

			int shown = 0;
			foreach (NoteRow item in _diff.Rows)
			{
				if (shown >= MaxRows)
				{
					break;
				}
				shown++;
				string oldTime = item.Left != null ? FormatRange(item.Left) : MidiStrings.T("not present");
				string newTime = item.Right != null ? FormatRange(item.Right) : MidiStrings.T("not present");
				AddTableRow(table, ref row, oldTime, Describe(item.Left), newTime, Describe(item.Right), StateLabel(item.State), TintOf(item.State), StateBrush(item.State), OutTimeBrush(item), false);
			}

			StackPanel stack = new StackPanel
			{
				Spacing = 6.0
			};
			Border card = new Border
			{
				BorderBrush = GridLine,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(6.0),
				Background = Subtle,
				Padding = new Thickness(2.0),
				Child = table
			};
			stack.Children.Add(card);
			if (_diff.Total > shown)
			{
				stack.Children.Add(NoteText(MidiStrings.F("Showing first {0} of {1} rows", shown, _diff.Total)));
			}
			if (_srcAbsent || _dstAbsent)
			{
				stack.Children.Add(NoteText(MidiStrings.T("One side is not present; values shown for the other side only.")));
			}
			return new ScrollViewer
			{
				Content = stack,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
			};
		}

		/// <summary>音符时间范围："起 → 止"（MM:SS.mmm）。</summary>
		private static string FormatRange(MidiNote note)
		{
			return MidiText.FormatNoteTime(note.StartMs) + " → " + MidiText.FormatNoteTime(note.EndMs);
		}

		private void AddTableRow(Grid table, ref int row, string cell0, string cell1, string cell2, string cell3, string cell4, IBrush tint, IBrush stateBrush, IBrush cell4Brush, bool header)
		{
			table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
			int current = row++;
			if (tint != null)
			{
				Border background = new Border
				{
					Background = tint,
					CornerRadius = new CornerRadius(3.0)
				};
				Grid.SetRow(background, current);
				Grid.SetColumn(background, 0);
				Grid.SetColumnSpan(background, 5);
				table.Children.Add(background);
			}
			bool mono = !header;
			AddCell(table, current, 0, cell0, header, mono, header ? null : Brushes.Gray);
			AddCell(table, current, 1, cell1, header, false, null);
			AddCell(table, current, 2, cell2, header, mono, header ? null : Brushes.Gray);
			AddCell(table, current, 3, cell3, header, false, null);
			AddCell(table, current, 4, cell4, header, false, stateBrush);
		}

		private static void AddCell(Grid table, int row, int column, string text, bool header, bool mono, IBrush foreground)
		{
			TextBlock block = new TextBlock
			{
				Text = text ?? string.Empty,
				Margin = new Thickness(8.0, 3.0, 8.0, 3.0),
				TextWrapping = header ? TextWrapping.NoWrap : TextWrapping.Wrap,
				FontWeight = header ? FontWeight.SemiBold : FontWeight.Normal,
				FontFamily = mono ? MonoFont : FontFamily.Default,
				Foreground = foreground ?? Brushes.Black,
				VerticalAlignment = VerticalAlignment.Top
			};
			Grid.SetRow(block, row);
			Grid.SetColumn(block, column);
			table.Children.Add(block);
		}

		// ---- 钢琴卷帘时间轴 ----

		private Control BuildPianoRollContent()
		{
			long totalMs = TotalDuration();
			if (totalMs <= 0L || _diff.Total <= 0)
			{
				// 区分「没有音符」与「有音符但时间全为 0（无法铺开卷帘）」两种占位。
				return NoteText(MidiStrings.T("No notes"));
			}
			int minPitch;
			int maxPitch;
			PitchRange(out minPitch, out maxPitch);
			double lanes = maxPitch - minPitch + 1;
			StackPanel stack = new StackPanel
			{
				Spacing = 2.0,
				Width = LabelWidth + TimelineWidth
			};
			stack.Children.Add(BuildRuler(totalMs));
			stack.Children.Add(BuildTrack(MidiStrings.T("Old"), _diff.Rows, true, totalMs, minPitch, lanes));
			stack.Children.Add(BuildTrack(MidiStrings.T("New"), _diff.Rows, false, totalMs, minPitch, lanes));

			StackPanel outer = new StackPanel
			{
				Spacing = 6.0
			};
			outer.Children.Add(new ScrollViewer
			{
				Content = stack,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
			});
			return outer;
		}

		/// <summary>两侧音符结束的最大毫秒（时间轴总长）。</summary>
		private long TotalDuration()
		{
			double total = 0.0;
			foreach (NoteRow item in _diff.Rows)
			{
				if (item.Left != null && item.Left.EndMs > total)
				{
					total = item.Left.EndMs;
				}
				if (item.Right != null && item.Right.EndMs > total)
				{
					total = item.Right.EndMs;
				}
			}
			return (long)Math.Round(total);
		}

		/// <summary>
		/// 卷帘音高范围：两侧音符 pitch 的 min/max 各外扩 2 半音（下限钳 0、上限钳 127）；
		/// 两侧都没有音符时回退 36..84（C2..C6 一带，常见音域）。
		/// </summary>
		private void PitchRange(out int minPitch, out int maxPitch)
		{
			int min = int.MaxValue;
			int max = int.MinValue;
			bool any = false;
			foreach (NoteRow item in _diff.Rows)
			{
				if (item.Left != null)
				{
					any = true;
					if (item.Left.Pitch < min)
					{
						min = item.Left.Pitch;
					}
					if (item.Left.Pitch > max)
					{
						max = item.Left.Pitch;
					}
				}
				if (item.Right != null)
				{
					any = true;
					if (item.Right.Pitch < min)
					{
						min = item.Right.Pitch;
					}
					if (item.Right.Pitch > max)
					{
						max = item.Right.Pitch;
					}
				}
			}
			if (!any)
			{
				min = 36;
				max = 84;
			}
			minPitch = Math.Max(0, min - 2);
			maxPitch = Math.Min(127, max + 2);
		}

		private static Control BuildRuler(long totalMs)
		{
			Canvas canvas = new Canvas
			{
				Width = TimelineWidth,
				Height = 20.0
			};
			for (int i = 0; i <= RulerTicks; i++)
			{
				double ratio = (double)i / RulerTicks;
				double x = ratio * TimelineWidth;
				Border tick = new Border
				{
					Width = 1.0,
					Height = 6.0,
					Background = GridLine
				};
				Canvas.SetLeft(tick, Math.Min(x, TimelineWidth - 1.0));
				Canvas.SetTop(tick, 14.0);
				canvas.Children.Add(tick);

				TextBlock label = new TextBlock
				{
					Text = MidiText.FormatClock(totalMs * ratio),
					FontSize = 10.0,
					Foreground = Brushes.Gray,
					FontFamily = MonoFont
				};
				Canvas.SetLeft(label, Math.Min(x + 2.0, TimelineWidth - 40.0));
				Canvas.SetTop(label, 0.0);
				canvas.Children.Add(label);
			}
			return Row(string.Empty, canvas);
		}

		/// <summary>一条卷帘轨道：Canvas 高度 = 音高行数 × 行高；每个音符画一根圆角色条。</summary>
		private static Control BuildTrack(string label, List<NoteRow> rows, bool left, long totalMs, int minPitch, double lanes)
		{
			Canvas canvas = new Canvas
			{
				Width = TimelineWidth,
				Height = lanes * RowHeight,
				Background = Subtle
			};
			foreach (NoteRow item in rows)
			{
				MidiNote note = left ? item.Left : item.Right;
				if (note == null)
				{
					continue;
				}
				double start = Math.Max(0.0, note.StartMs / totalMs) * TimelineWidth;
				double end = Math.Max(note.EndMs / totalMs, 0.0) * TimelineWidth;
				double width = Math.Max(3.0, end - start);
				Border bar = new Border
				{
					Width = width,
					Height = RowHeight - 1.0,
					CornerRadius = new CornerRadius(2.0),
					Background = BarBrush(item.State)
				};
				ToolTip.SetTip(bar, MidiText.NoteName(note.Pitch) + " · " + MidiText.FormatNoteTime(note.StartMs) + " → " + MidiText.FormatNoteTime(note.EndMs) + " · vel " + note.Velocity);
				Canvas.SetLeft(bar, Math.Min(start, TimelineWidth - 3.0));
				Canvas.SetTop(bar, (note.Pitch - minPitch) * RowHeight);
				canvas.Children.Add(bar);
			}
			return Row(label, canvas);
		}

		private static Control Row(string label, Control content)
		{
			DockPanel panel = new DockPanel
			{
				Width = LabelWidth + TimelineWidth
			};
			TextBlock text = new TextBlock
			{
				Text = label,
				Width = LabelWidth,
				FontSize = 11.0,
				FontWeight = FontWeight.SemiBold,
				VerticalAlignment = VerticalAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis
			};
			DockPanel.SetDock(text, Dock.Left);
			panel.Children.Add(text);
			panel.Children.Add(content);
			return panel;
		}

		private static IBrush BarBrush(NoteState state)
		{
			switch (state)
			{
			case NoteState.Changed:
				return BarChanged;
			case NoteState.LeftOnly:
				return BarLeftOnly;
			case NoteState.RightOnly:
				return BarRightOnly;
			default:
				return BarSame;
			}
		}

		// ---- 文案 / 小部件 ----

		private static IBrush TintOf(NoteState state)
		{
			switch (state)
			{
			case NoteState.Changed:
				return TintChanged;
			case NoteState.LeftOnly:
				return TintLeftOnly;
			case NoteState.RightOnly:
				return TintRightOnly;
			default:
				return null;
			}
		}

		private static IBrush StateBrush(NoteState state)
		{
			switch (state)
			{
			case NoteState.Changed:
				return StateChanged;
			case NoteState.LeftOnly:
				return StateLeft;
			case NoteState.RightOnly:
				return StateRight;
			default:
				return Brushes.Gray;
			}
		}

		private static IBrush OutTimeBrush(NoteRow item)
		{
			if (item.Left != null && item.Right != null && Math.Abs(item.Left.StartMs - item.Right.StartMs) > 0.5)
			{
				return StateChanged;
			}
			return Brushes.Gray;
		}

		private static string StateLabel(NoteState state)
		{
			switch (state)
			{
			case NoteState.Changed:
				return MidiStrings.T("changed");
			case NoteState.LeftOnly:
				return MidiStrings.T("left only");
			case NoteState.RightOnly:
				return MidiStrings.T("right only");
			default:
				return MidiStrings.T("same");
			}
		}

		/// <summary>音符描述："C4 ch0 vel64 dur1.2s"。</summary>
		private static string Describe(MidiNote note)
		{
			if (note == null)
			{
				return MidiStrings.T("not present");
			}
			return MidiText.NoteName(note.Pitch) + " ch" + note.Channel + " vel" + note.Velocity +
				" dur" + (note.DurationMs / 1000.0).ToString("0.0##", CultureInfo.InvariantCulture) + "s";
		}

		private static TextBlock NewTitle()
		{
			return new TextBlock
			{
				FontWeight = FontWeight.SemiBold,
				TextWrapping = TextWrapping.NoWrap,
				TextTrimming = TextTrimming.CharacterEllipsis
			};
		}

		private static Button NewModeButton(string text, Action onClick)
		{
			Button button = new Button
			{
				Content = text,
				Padding = new Thickness(10.0, 0.0, 10.0, 0.0),
				MinHeight = 28.0,
				FontSize = 12.0
			};
			button.Click += delegate
			{
				onClick();
			};
			return button;
		}

		private static void StyleModeButton(Button button, bool active)
		{
			button.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
			button.Background = active ? new SolidColorBrush(Color.FromArgb(0x33, 0x2F, 0x6F, 0xED)) : null;
		}

		private static TextBlock NoteText(string text)
		{
			return new TextBlock
			{
				Text = text ?? string.Empty,
				Margin = new Thickness(0.0, 4.0, 0.0, 4.0),
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0,
				Opacity = 0.8
			};
		}
	}
}
