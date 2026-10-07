using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Subtitle
{
	/// <summary>
	/// 字幕 / 时间轴对比视图：把旧（左）/ 新（右）两侧的 SRT / WebVTT / ASS / SSA / MicroDVD 解析成
	/// 同一套 cue 列表，先按文本对齐、再按时间配对，得到「相同 / 已变 / 仅左 / 仅右」四类。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、
	/// 自带的分段模式工具条（字幕行 / 时间轴），最下是内容区。
	///
	/// 两个模式：
	/// <list type="bullet">
	/// <item>字幕行（默认）：一张五列表——「旧时间 | 旧文本 | 新时间 | 新文本 | 状态」，
	/// 逐行按四色标注底色，是看「哪句台词改了」的首选。</item>
	/// <item>时间轴：上下两条轨道（旧 / 新）按总时长等比铺开，每条字幕画成一根色条，
	/// 一眼看出字幕在时间上的增删与挪动；顶部是 MM:SS 刻度尺。</item>
	/// </list>
	///
	/// 解析与对齐在后台线程执行，控件只在 UI 线程构建；每次刷新用代次 + CancellationToken 取消上一轮。
	/// 单侧声明大小 &gt; 300 MB 只给提示、不解析。
	/// </summary>
	public sealed class SubtitleDiffView : IDiffView
	{
		private const string CuesMode = "cues";

		private const string TimelineMode = "timeline";

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （timeline，其余走默认 cues），据此逐模式取图；正常运行时该变量为空，走默认字幕行表模式。
		/// </summary>
		private static readonly string InitialMode = ResolveInitialMode();

		private static string ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			return string.Equals(forced, TimelineMode, StringComparison.OrdinalIgnoreCase) ? TimelineMode : CuesMode;
		}

		/// <summary>单侧超过此大小不解析（与音视频 / 结构化数据插件同口径）。</summary>
		private const long MaxSideBytes = 300L * 1024L * 1024L;

		/// <summary>渲染上限：避免超大字幕把 UI 拖死。</summary>
		private const int MaxRows = 3000;

		/// <summary>时间轴的逻辑宽度（像素）；配合横向滚动，比例刻度稳定可预期。</summary>
		private const double TimelineWidth = 1200.0;

		/// <summary>时间轴轨道高度与色条高度。</summary>
		private const double TrackHeight = 46.0;

		private const double BarHeight = 24.0;

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

		private readonly Button _cuesButton;

		private readonly Button _timelineButton;

		private readonly ContentControl _content;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _generation;

		private string _mode = InitialMode;

		private SubtitleDocument _left;

		private SubtitleDocument _right;

		private SubtitleDiffResult _diff;

		private string _error;

		private string _srcFormat = "?";

		private string _dstFormat = "?";

		private bool _srcAbsent;

		private bool _dstAbsent;

		private bool _tooLarge;

		public SubtitleDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			_status = new TextBlock
			{
				Margin = new Thickness(12.0, 2.0, 12.0, 6.0),
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0
			};
			_cuesButton = NewModeButton(SubtitleStrings.T("Cue list"), delegate
			{
				SetMode(CuesMode);
			});
			_timelineButton = NewModeButton(SubtitleStrings.T("Timeline"), delegate
			{
				SetMode(TimelineMode);
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
			modes.Children.Add(_cuesButton);
			modes.Children.Add(_timelineButton);

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

			StyleModeButton(_cuesButton, _mode == CuesMode);
			StyleModeButton(_timelineButton, _mode == TimelineMode);
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
			_srcFormat = FormatOf(context?.Src) ?? "?";
			_dstFormat = FormatOf(context?.Dst) ?? "?";
			UpdateTitles();
			PluginLog.Info($"SubtitleDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			Render();
		}

		public void SetMode(string modeId)
		{
			if (string.IsNullOrEmpty(modeId))
			{
				return;
			}
			if (modeId != CuesMode && modeId != TimelineMode)
			{
				return;
			}
			if (modeId == _mode)
			{
				return;
			}
			_mode = modeId;
			StyleModeButton(_cuesButton, _mode == CuesMode);
			StyleModeButton(_timelineButton, _mode == TimelineMode);
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
			_cuesButton.Content = SubtitleStrings.T("Cue list");
			_timelineButton.Content = SubtitleStrings.T("Timeline");
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
			string srcFormat = _srcFormat;
			string dstFormat = _dstFormat;

			Task.Run(delegate
			{
				SubtitleDocument left = null;
				SubtitleDocument right = null;
				string error = null;
				try
				{
					string leftText = ReadText(src, hexSrc, cts.Token);
					string rightText = ReadText(dst, hexDst, cts.Token);
					cts.Token.ThrowIfCancellationRequested();
					if (src != null)
					{
						left = SubtitleParser.Parse(leftText, srcFormat, out string leftError);
						error = leftError;
					}
					if (dst != null)
					{
						right = SubtitleParser.Parse(rightText, dstFormat, out string rightError);
						error = error ?? rightError;
					}
					if (cts.Token.IsCancellationRequested)
					{
						return;
					}
					SubtitleDiffResult diff = SubtitleDiff.Compute(left?.Cues, right?.Cues);
					Dispatcher.UIThread.Post(delegate
					{
						if (_generation != generation || _cts != cts)
						{
							return;
						}
						_left = left;
						_right = right;
						_diff = diff;
						_error = error;
						UpdateStatus();
						BuildContent();
					});
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex)
				{
					PluginLog.Error("SubtitleDiffView 渲染失败", ex);
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

		/// <summary>取一侧文本：宿主 Hex 预载 &gt; 侧内容懒加载 &gt; LFS 本地缓存。</summary>
		private string ReadText(DiffSideContent side, MemoryStream preloaded, CancellationToken token)
		{
			if (side == null)
			{
				return string.Empty;
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
				return string.Empty;
			}
			token.ThrowIfCancellationRequested();
			return DecodeText(data);
		}

		private static string DecodeText(MemoryStream stream)
		{
			if (stream == null)
			{
				return string.Empty;
			}
			long position = stream.Position;
			try
			{
				stream.Position = 0L;
				// 字幕常见 UTF-8；缺 BOM 的 GBK 等编码由 detectEncodingFromByteOrderMarks 尽量识别，
				// 认不出时按 UTF-8 宽松解码，不因个别坏字节整体失败。
				using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true))
				{
					return reader.ReadToEnd();
				}
			}
			finally
			{
				try
				{
					stream.Position = position;
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
				return roleText + "  ·  " + SubtitleStrings.T("not present");
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

		private static string FormatOf(DiffSideContent side)
		{
			if (side?.Path == null)
			{
				return null;
			}
			string extension = Path.GetExtension(side.Path);
			if (string.IsNullOrEmpty(extension))
			{
				return "?";
			}
			return extension.TrimStart('.').ToLowerInvariant();
		}

		private void UpdateStatus()
		{
			if (_tooLarge)
			{
				_status.Text = SubtitleStrings.T("File too large to preview");
				return;
			}
			if (_error != null)
			{
				_status.Text = SubtitleStrings.F("Failed to parse: {0}", _error);
				return;
			}
			if (_diff == null)
			{
				_status.Text = SubtitleStrings.T("Analyzing…");
				return;
			}
			string head = SubtitleStrings.F("Subtitle compare: {0} / {1}", DisplayFormat(_left, _srcFormat), DisplayFormat(_right, _dstFormat));
			head = head + "  ·  " + SubtitleStrings.F("cues: {0} / {1}", _left?.Cues.Count ?? 0, _right?.Cues.Count ?? 0);
			string fps = FpsNote(_left) ?? FpsNote(_right);
			if (fps != null)
			{
				head = head + "  ·  " + fps;
			}
			if (!_diff.HasDifferences)
			{
				_status.Text = head + "  ·  " + SubtitleStrings.T("No differences");
				return;
			}
			_status.Text = head + "  ·  " + SubtitleStrings.F("Summary: {0} cues · {1} changed · {2} left only · {3} right only", _diff.Total, _diff.Changed, _diff.LeftOnly, _diff.RightOnly);
		}

		private static string DisplayFormat(SubtitleDocument document, string fallback)
		{
			return document?.Format ?? fallback;
		}

		private static string FpsNote(SubtitleDocument document)
		{
			if (document?.AssumedFps == null)
			{
				return null;
			}
			return SubtitleStrings.F("MicroDVD frames converted at {0} fps", document.AssumedFps.Value.ToString("0.###", CultureInfo.InvariantCulture));
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
				_content.Content = NoteText(SubtitleStrings.T("File too large to preview"));
				return;
			}
			if (_diff == null)
			{
				_content.Content = NoteText(_error == null ? SubtitleStrings.T("Analyzing…") : SubtitleStrings.F("Failed to parse: {0}", _error));
				return;
			}
			if (_mode == TimelineMode)
			{
				_content.Content = BuildTimelineContent();
			}
			else
			{
				_content.Content = BuildCuesContent();
			}
		}

		private Control BuildCuesContent()
		{
			Grid table = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("Auto,2*,Auto,2*,Auto")
			};
			int row = 0;
			AddTableRow(table, ref row, SubtitleStrings.T("Old time"), SubtitleStrings.T("Old"), SubtitleStrings.T("New time"), SubtitleStrings.T("New"), SubtitleStrings.T("State"), null, null, null, true);

			int shown = 0;
			foreach (CueRow item in _diff.Rows)
			{
				if (shown >= MaxRows)
				{
					break;
				}
				shown++;
				string oldTime = item.Left != null ? SubtitleText.FormatTime(item.Left.StartMs) : SubtitleStrings.T("not present");
				string newTime = item.Right != null ? SubtitleText.FormatTime(item.Right.StartMs) : SubtitleStrings.T("not present");
				AddTableRow(table, ref row, oldTime, Display(item.Left), newTime, Display(item.Right), StateLabel(item.State), TintOf(item.State), StateBrush(item.State), OutTimeBrush(item), false);
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
				stack.Children.Add(NoteText(SubtitleStrings.F("Showing first {0} of {1} rows", shown, _diff.Total)));
			}
			if (_srcAbsent || _dstAbsent)
			{
				stack.Children.Add(NoteText(SubtitleStrings.T("One side is not present; values shown for the other side only.")));
			}
			return new ScrollViewer
			{
				Content = stack,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
			};
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

		// ---- 时间轴 ----

		private Control BuildTimelineContent()
		{
			long totalMs = TotalDuration();
			if (totalMs <= 0L)
			{
				return NoteText(SubtitleStrings.T("No cues"));
			}
			StackPanel stack = new StackPanel
			{
				Spacing = 2.0,
				Width = LabelWidth + TimelineWidth
			};
			stack.Children.Add(BuildRuler(totalMs));
			stack.Children.Add(BuildTrack(SubtitleStrings.T("Old"), _diff.Rows, true, totalMs));
			stack.Children.Add(BuildTrack(SubtitleStrings.T("New"), _diff.Rows, false, totalMs));
			stack.Children.Add(NoteText(SubtitleStrings.F("Timeline spans {0}", SubtitleText.FormatTime(totalMs))));

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

		private long TotalDuration()
		{
			long total = 0L;
			foreach (CueRow item in _diff.Rows)
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
			return total;
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
					Text = SubtitleText.FormatClock((long)(totalMs * ratio)),
					FontSize = 10.0,
					Foreground = Brushes.Gray,
					FontFamily = MonoFont
				};
				Canvas.SetLeft(label, Math.Min(x + 2.0, TimelineWidth - 40.0));
				Canvas.SetTop(label, 0.0);
				canvas.Children.Add(label);
			}
			return Row(SubtitleStrings.T("Timeline"), canvas);
		}

		private static Control BuildTrack(string label, List<CueRow> rows, bool left, long totalMs)
		{
			Canvas canvas = new Canvas
			{
				Width = TimelineWidth,
				Height = TrackHeight,
				Background = Subtle
			};
			foreach (CueRow item in rows)
			{
				SubtitleCue cue = left ? item.Left : item.Right;
				if (cue == null)
				{
					continue;
				}
				double start = Math.Max(0.0, (double)cue.StartMs / totalMs) * TimelineWidth;
				double end = Math.Max((double)cue.EndMs / totalMs, 0.0) * TimelineWidth;
				double width = Math.Max(3.0, end - start);
				Border bar = new Border
				{
					Width = width,
					Height = BarHeight,
					CornerRadius = new CornerRadius(3.0),
					Background = BarBrush(item.State)
				};
				ToolTip.SetTip(bar, SubtitleText.FormatTime(cue.StartMs) + "  " + FirstLine(cue.Text));
				if (width > 48.0)
				{
					bar.Child = new TextBlock
					{
						Text = Truncate(FirstLine(cue.Text), width < 160.0 ? 14 : 40),
						FontSize = 10.0,
						Foreground = Brushes.White,
						Margin = new Thickness(4.0, 0.0, 4.0, 0.0),
						VerticalAlignment = VerticalAlignment.Center,
						TextTrimming = TextTrimming.CharacterEllipsis
					};
				}
				Canvas.SetLeft(bar, Math.Min(start, TimelineWidth - 3.0));
				Canvas.SetTop(bar, (TrackHeight - BarHeight) / 2.0);
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

		private static IBrush BarBrush(CueState state)
		{
			switch (state)
			{
			case CueState.Changed:
				return BarChanged;
			case CueState.LeftOnly:
				return BarLeftOnly;
			case CueState.RightOnly:
				return BarRightOnly;
			default:
				return BarSame;
			}
		}

		// ---- 文案 / 小部件 ----

		private static IBrush TintOf(CueState state)
		{
			switch (state)
			{
			case CueState.Changed:
				return TintChanged;
			case CueState.LeftOnly:
				return TintLeftOnly;
			case CueState.RightOnly:
				return TintRightOnly;
			default:
				return null;
			}
		}

		private static IBrush StateBrush(CueState state)
		{
			switch (state)
			{
			case CueState.Changed:
				return StateChanged;
			case CueState.LeftOnly:
				return StateLeft;
			case CueState.RightOnly:
				return StateRight;
			default:
				return Brushes.Gray;
			}
		}

		private static IBrush OutTimeBrush(CueRow item)
		{
			if (item.Left != null && item.Right != null && item.Left.StartMs != item.Right.StartMs)
			{
				return StateChanged;
			}
			return Brushes.Gray;
		}

		private static string StateLabel(CueState state)
		{
			switch (state)
			{
			case CueState.Changed:
				return SubtitleStrings.T("changed");
			case CueState.LeftOnly:
				return SubtitleStrings.T("left only");
			case CueState.RightOnly:
				return SubtitleStrings.T("right only");
			default:
				return SubtitleStrings.T("same");
			}
		}

		private static string Display(SubtitleCue cue)
		{
			if (cue == null)
			{
				return SubtitleStrings.T("not present");
			}
			string text = cue.Text ?? string.Empty;
			if (!string.IsNullOrEmpty(cue.Extra))
			{
				text = "[" + cue.Extra + "]  " + text;
			}
			return text;
		}

		private static string FirstLine(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return string.Empty;
			}
			int newline = text.IndexOf('\n');
			return newline < 0 ? text : text.Substring(0, newline);
		}

		private static string Truncate(string value, int maxChars)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}
			return value.Length <= maxChars ? value : value.Substring(0, maxChars) + "…";
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
				Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
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
