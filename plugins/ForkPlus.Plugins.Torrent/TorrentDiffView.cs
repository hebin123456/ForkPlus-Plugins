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

namespace ForkPlus.Plugins.Torrent
{
	/// <summary>
	/// Torrent 种子文件对比视图：把旧（左）/ 新（右）两侧 .torrent 的 bencode 各自解码拍平成
	/// 「键路径 → 值」列表，取并集逐行四色标注（相同 / 已变 / 仅左 / 仅右），并附名称 / 分片数摘要。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、内容区。
	/// 单一模式（键路径表），不需要模式工具条，故 <see cref="IDiffView.Modes"/> 返回空数组。
	///
	/// 内容是一张四列表——「Key path | Old | New | State」：键路径列等宽字体，数据行按状态四色
	/// 铺底，状态列彩色文字；外层卡片 + 双向滚动，行数上限 4000，超出在底部注明截断。
	///
	/// 解码与 diff 在后台线程执行，控件只在 UI 线程构建；每次刷新用代次 + CancellationToken
	/// 取消上一轮。单侧声明大小 &gt; 300 MB 只给提示、不解析。
	/// </summary>
	public sealed class TorrentDiffView : IDiffView
	{
		/// <summary>单侧超过此大小不解析（与字幕 / 音视频 / 结构化数据插件同口径）。</summary>
		private const long MaxSideBytes = 300L * 1024L * 1024L;

		/// <summary>渲染上限：避免长 announce-list 等超大种子把 UI 拖死。</summary>
		private const int MaxRows = 4000;

		private static readonly IBrush GridLine = Brushes.Gainsboro;

		private static readonly IBrush Subtle = new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x80, 0x80));

		private static readonly IBrush TintChanged = new SolidColorBrush(Color.FromArgb(0x2E, 0xE6, 0x7E, 0x22));

		private static readonly IBrush TintLeftOnly = new SolidColorBrush(Color.FromArgb(0x26, 0xC0, 0x39, 0x2B));

		private static readonly IBrush TintRightOnly = new SolidColorBrush(Color.FromArgb(0x26, 0x2E, 0x9E, 0x5B));

		private static readonly IBrush StateChanged = new SolidColorBrush(Color.FromArgb(0xFF, 0xB0, 0x5A, 0x00));

		private static readonly IBrush StateLeft = new SolidColorBrush(Color.FromArgb(0xFF, 0xC0, 0x39, 0x2B));

		private static readonly IBrush StateRight = new SolidColorBrush(Color.FromArgb(0xFF, 0x2E, 0x9E, 0x5B));

		private static readonly FontFamily MonoFont = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, Courier New, monospace");

		private readonly Grid _root;

		private readonly TextBlock _srcTitle;

		private readonly TextBlock _dstTitle;

		private readonly TextBlock _status;

		private readonly ContentControl _content;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _generation;

		private TorrentData _left;

		private TorrentData _right;

		private TorrentDiffResult _diff;

		private string _error;

		/// <summary>左 / 右两侧各自的解析错误（侧存在但解析失败时用于区分「解析失败」与「不存在」）。</summary>
		private string _srcParseError;

		private string _dstParseError;

		private bool _srcAbsent;

		private bool _dstAbsent;

		private bool _tooLarge;

		public TorrentDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			_status = new TextBlock
			{
				Margin = new Thickness(12.0, 2.0, 12.0, 6.0),
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0
			};
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

			_root = new Grid
			{
				RowDefinitions = new RowDefinitions("Auto,Auto,*")
			};
			Grid.SetRow(titleRow, 0);
			Grid.SetRow(_status, 1);
			Grid.SetRow(_content, 2);
			_root.Children.Add(titleRow);
			_root.Children.Add(_status);
			_root.Children.Add(_content);

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
			PluginLog.Info($"TorrentDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			Render();
		}

		/// <summary>本视图单一模式（键路径表），不使用宿主模式工具条，一律忽略。</summary>
		public void SetMode(string modeId)
		{
		}

		public void Activate()
		{
		}

		public void Deactivate()
		{
		}

		public void ApplyLocalization()
		{
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
				TorrentData left = null;
				TorrentData right = null;
				string srcParseError = null;
				string dstParseError = null;
				try
				{
					byte[] leftBytes = ReadBytes(src, hexSrc, cts.Token);
					byte[] rightBytes = ReadBytes(dst, hexDst, cts.Token);
					cts.Token.ThrowIfCancellationRequested();
					if (src != null)
					{
						left = TorrentParser.Parse(leftBytes, out srcParseError);
					}
					if (dst != null)
					{
						right = TorrentParser.Parse(rightBytes, out dstParseError);
					}
					if (cts.Token.IsCancellationRequested)
					{
						return;
					}
					TorrentDiffResult diff = TorrentDiff.Compute(left?.Rows, right?.Rows);
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
					PluginLog.Error("TorrentDiffView 渲染失败", ex);
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

		/// <summary>取一侧字节：宿主 Hex 预载 &gt; 侧内容懒加载 &gt; LFS 本地缓存；侧为 null 跳过。</summary>
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
				data.Position = 0L;
				byte[] buffer = new byte[data.Length];
				int offset = 0;
				while (offset < buffer.Length)
				{
					int read = data.Read(buffer, offset, buffer.Length - offset);
					if (read <= 0)
					{
						break;
					}
					offset += read;
				}
				if (offset < buffer.Length)
				{
					Array.Resize(ref buffer, offset);
				}
				return buffer;
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
				return roleText + "  ·  " + TorrentStrings.T("not present");
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
				_status.Text = TorrentStrings.T("File too large to preview");
				return;
			}
			string error = DisplayError();
			if (error != null)
			{
				_status.Text = TorrentStrings.F("Failed to parse: {0}", error);
				return;
			}
			if (_diff == null)
			{
				_status.Text = TorrentStrings.T("Analyzing…");
				return;
			}
			string head = TorrentStrings.F("Torrent compare: {0} / {1}", DisplayName(_left, _context?.Src), DisplayName(_right, _context?.Dst));
			head = head + "  ·  " + TorrentStrings.F("pieces: {0} / {1}", Pieces(_left), Pieces(_right));
			if (!_diff.HasDifferences)
			{
				_status.Text = head + "  ·  " + TorrentStrings.T("No differences");
				return;
			}
			_status.Text = head + "  ·  " + TorrentStrings.F("Summary: {0} entries · {1} changed · {2} left only · {3} right only", _diff.Total, _diff.Changed, _diff.LeftOnly, _diff.RightOnly);
		}

		/// <summary>状态行名称：优先 info.name，解析不出用文件名，侧不存在标「不存在」。</summary>
		private static string DisplayName(TorrentData data, DiffSideContent side)
		{
			if (!string.IsNullOrEmpty(data?.Name))
			{
				return data.Name;
			}
			if (side != null)
			{
				return Path.GetFileName(side.Path) ?? string.Empty;
			}
			return TorrentStrings.T("not present");
		}

		private static string Pieces(TorrentData data)
		{
			return data != null && data.PieceCount.HasValue
				? data.PieceCount.Value.ToString(CultureInfo.InvariantCulture)
				: "?";
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
				_content.Content = NoteText(TorrentStrings.T("File too large to preview"));
				return;
			}
			if (_diff == null)
			{
				_content.Content = NoteText(DisplayError() == null ? TorrentStrings.T("Analyzing…") : TorrentStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			// 两侧都存在却都解析失败（如扩展名对但内容不是 bencode）：给错误占位，不再渲染空表。
			if (_left == null && _right == null && DisplayError() != null)
			{
				_content.Content = NoteText(TorrentStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			_content.Content = BuildTableContent();
		}

		private Control BuildTableContent()
		{
			Grid table = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("2*,3*,3*,Auto")
			};
			int row = 0;
			AddTableRow(table, ref row, TorrentStrings.T("Key path"), TorrentStrings.T("Old"), TorrentStrings.T("New"), TorrentStrings.T("State"), null, null, true);

			int shown = 0;
			foreach (TorrentDiffRow item in _diff.Rows)
			{
				if (shown >= MaxRows)
				{
					break;
				}
				shown++;
				AddTableRow(table, ref row, item.Path, item.LeftValue ?? TorrentStrings.T("not present"), item.RightValue ?? TorrentStrings.T("not present"), StateLabel(item.State), TintOf(item.State), StateBrush(item.State), false);
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
				stack.Children.Add(NoteText(TorrentStrings.F("Showing first {0} of {1} rows", shown, _diff.Total)));
			}
			if (_srcAbsent || _dstAbsent)
			{
				stack.Children.Add(NoteText(TorrentStrings.T("One side is not present; values shown for the other side only.")));
			}
			return new ScrollViewer
			{
				Content = stack,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
			};
		}

		private void AddTableRow(Grid table, ref int row, string cell0, string cell1, string cell2, string cell3, IBrush tint, IBrush stateBrush, bool header)
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
				Grid.SetColumnSpan(background, 4);
				table.Children.Add(background);
			}
			AddCell(table, current, 0, cell0, header, !header, null);
			AddCell(table, current, 1, cell1, header, false, null);
			AddCell(table, current, 2, cell2, header, false, null);
			AddCell(table, current, 3, cell3, header, false, stateBrush);
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

		// ---- 文案 / 小部件 ----

		private static IBrush TintOf(TorrentState state)
		{
			switch (state)
			{
			case TorrentState.Changed:
				return TintChanged;
			case TorrentState.LeftOnly:
				return TintLeftOnly;
			case TorrentState.RightOnly:
				return TintRightOnly;
			default:
				return null;
			}
		}

		private static IBrush StateBrush(TorrentState state)
		{
			switch (state)
			{
			case TorrentState.Changed:
				return StateChanged;
			case TorrentState.LeftOnly:
				return StateLeft;
			case TorrentState.RightOnly:
				return StateRight;
			default:
				return Brushes.Gray;
			}
		}

		private static string StateLabel(TorrentState state)
		{
			switch (state)
			{
			case TorrentState.Changed:
				return TorrentStrings.T("changed");
			case TorrentState.LeftOnly:
				return TorrentStrings.T("left only");
			case TorrentState.RightOnly:
				return TorrentStrings.T("right only");
			default:
				return TorrentStrings.T("same");
			}
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
