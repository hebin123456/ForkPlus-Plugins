using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Pcap
{
	/// <summary>
	/// 网络抓包对比视图：把旧（左）/ 新（右）两侧的 pcap / pcapng 各自聚合出统计
	/// （包数 / 字节数 / 时长 / 协议分布 / 会话 Top）与包列表，再做语义 diff，
	/// 得到「相同 / 已变 / 仅左 / 仅右」四类。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、
	/// 自带的分段模式工具条（统计 / 包列表），最下是内容区。
	///
	/// 两个模式：
	/// <list type="bullet">
	/// <item>统计（默认）：一张四列表——「键路径 | 旧 | 新 | 状态」，逐行按四色标注底色，
	/// 是看「哪项统计变了」的首选。</item>
	/// <item>包列表：一张四列表——「# | 旧 | 新 | 状态」，两侧同序号的包对齐成一行，
	/// 每格给出「相对秒 · 长度 · 协议与五元组摘要」。</item>
	/// </list>
	///
	/// 解析与聚合在后台线程执行，控件只在 UI 线程构建；每次刷新用代次 + CancellationToken 取消上一轮。
	/// 单侧声明大小 &gt; 300 MB 只给提示、不解析。
	/// </summary>
	public sealed class PcapDiffView : IDiffView
	{
		private const string StatisticsMode = "statistics";

		private const string PacketsMode = "packets";

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （packets，其余走默认 statistics），据此逐模式取图；正常运行时该变量为空。
		/// </summary>
		private static readonly string InitialMode = ResolveInitialMode();

		private static string ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			return string.Equals(forced, PacketsMode, StringComparison.OrdinalIgnoreCase) ? PacketsMode : StatisticsMode;
		}

		/// <summary>单侧超过此大小不解析（与字幕 / 结构化数据插件同口径）。</summary>
		private const long MaxSideBytes = 300L * 1024L * 1024L;

		/// <summary>包列表渲染上限：避免超大抓包把 UI 拖死。</summary>
		private const int MaxPacketRows = 1000;

		/// <summary>统计表四列宽度：键路径 / 旧 / 新按比例伸缩，状态列固定星号（原 Auto 列在虚拟化下每行独立测宽会错列，改星号保证表头与行对齐）。</summary>
		private const string StatsTableColumns = "2*,3*,3*,1*";

		/// <summary>包列表四列宽度：# 列固定像素（序号最多 4 位、宽度可预知；原 Auto 在虚拟化下逐行错列），状态列固定星号。</summary>
		private const string PacketTableColumns = "64,3*,3*,1*";

		/// <summary>数据行面板：VirtualizingStackPanel 只物化视口内的行（配合 VirtualTableTemplate 的内置 ScrollViewer）。</summary>
		private static readonly FuncTemplate<Panel> VirtualizingPanel = new FuncTemplate<Panel>(delegate
		{
			return new VirtualizingStackPanel();
		});

		/// <summary>虚拟列表模板：ScrollViewer 必须长在控件模板内部——外层再包 ScrollViewer 会把
		/// ItemsControl 撑成无限高，VirtualizingStackPanel 拿不到有界视口就退化成全量物化。</summary>
		private static readonly FuncControlTemplate<ItemsControl> VirtualTableTemplate = new FuncControlTemplate<ItemsControl>(delegate(ItemsControl owner, INameScope nameScope)
		{
			ScrollViewer scroll = new ScrollViewer
			{
				// 星号列宽依赖有限宽度约束；开横向滚动会退化成无限宽（长文本走换行）。
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto
			};
			ItemsPresenter presenter = new ItemsPresenter();
			nameScope.Register("PART_ItemsPresenter", presenter);
			scroll.Content = presenter;
			return scroll;
		});

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

		private readonly Button _statsButton;

		private readonly Button _packetsButton;

		private readonly ContentControl _content;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _generation;

		private string _mode = InitialMode;

		private PcapDocument _left;

		private PcapDocument _right;

		private PcapDiffResult _statsDiff;

		private PacketDiffResult _packetDiff;

		private string _error;

		/// <summary>左 / 右两侧各自的解析错误（侧存在但解析失败时用于区分「解析失败」与「不存在」）。</summary>
		private string _srcParseError;

		private string _dstParseError;

		private string _srcFormat = "?";

		private string _dstFormat = "?";

		private bool _srcAbsent;

		private bool _dstAbsent;

		private bool _tooLarge;

		public PcapDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			_status = new TextBlock
			{
				Margin = new Thickness(12.0, 2.0, 12.0, 6.0),
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0
			};
			_statsButton = NewModeButton(PcapStrings.T("Statistics"), delegate
			{
				SetMode(StatisticsMode);
			});
			_packetsButton = NewModeButton(PcapStrings.T("Packets"), delegate
			{
				SetMode(PacketsMode);
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
			modes.Children.Add(_statsButton);
			modes.Children.Add(_packetsButton);

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

			StyleModeButton(_statsButton, _mode == StatisticsMode);
			StyleModeButton(_packetsButton, _mode == PacketsMode);
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
			PluginLog.Info($"PcapDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			Render();
		}

		public void SetMode(string modeId)
		{
			if (string.IsNullOrEmpty(modeId))
			{
				return;
			}
			if (modeId != StatisticsMode && modeId != PacketsMode)
			{
				return;
			}
			if (modeId == _mode)
			{
				return;
			}
			_mode = modeId;
			StyleModeButton(_statsButton, _mode == StatisticsMode);
			StyleModeButton(_packetsButton, _mode == PacketsMode);
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
			_statsButton.Content = PcapStrings.T("Statistics");
			_packetsButton.Content = PcapStrings.T("Packets");
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
			_statsDiff = null;
			_packetDiff = null;
			_content.Content = null;
			_status.Text = string.Empty;
		}

		// ---- 渲染 ----

		private void Render()
		{
			CancelRender();
			_left = null;
			_right = null;
			_statsDiff = null;
			_packetDiff = null;
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
				PcapDocument left = null;
				PcapDocument right = null;
				string srcParseError = null;
				string dstParseError = null;
				try
				{
					byte[] leftBytes = ReadBytes(src, hexSrc, cts.Token);
					byte[] rightBytes = ReadBytes(dst, hexDst, cts.Token);
					cts.Token.ThrowIfCancellationRequested();
					if (src != null)
					{
						left = PcapParser.Parse(leftBytes, out srcParseError);
					}
					if (dst != null)
					{
						right = PcapParser.Parse(rightBytes, out dstParseError);
					}
					if (cts.Token.IsCancellationRequested)
					{
						return;
					}
					PcapDiffResult statsDiff = PcapDiff.ComputeRows(left, right);
					PacketDiffResult packetDiff = PcapDiff.ComputePackets(left, right);
					Dispatcher.UIThread.Post(delegate
					{
						if (_generation != generation || _cts != cts)
						{
							return;
						}
						_left = left;
						_right = right;
						_statsDiff = statsDiff;
						_packetDiff = packetDiff;
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
					PluginLog.Error("PcapDiffView 渲染失败", ex);
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

		/// <summary>取一侧字节：宿主 Hex 预载 &gt; 侧内容懒加载 &gt; LFS 本地缓存；拷成 byte[] 并还原 Position。</summary>
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
				return roleText + "  ·  " + PcapStrings.T("not present");
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

		/// <summary>优先展示的状态错误：意外异常 &gt; 左侧解析失败 &gt; 右侧解析失败。</summary>
		private string DisplayError()
		{
			return _error ?? _srcParseError ?? _dstParseError;
		}

		private void UpdateStatus()
		{
			if (_tooLarge)
			{
				_status.Text = PcapStrings.T("File too large to preview");
				return;
			}
			string error = DisplayError();
			if (error != null)
			{
				_status.Text = PcapStrings.F("Failed to parse: {0}", error);
				return;
			}
			if (_statsDiff == null)
			{
				_status.Text = PcapStrings.T("Analyzing…");
				return;
			}
			string head = PcapStrings.F("Capture compare: {0} / {1}", DisplayFormat(_left, _srcFormat), DisplayFormat(_right, _dstFormat));
			head = head + "  ·  " + PcapStrings.F("packets: {0} / {1}", _left?.PacketCount ?? 0, _right?.PacketCount ?? 0);
			string truncation = PacketTruncationNote();
			if (truncation != null)
			{
				head = head + "  ·  " + truncation;
			}
			if (!_statsDiff.HasDifferences)
			{
				_status.Text = head + "  ·  " + PcapStrings.T("No differences");
				return;
			}
			_status.Text = head + "  ·  " + PcapStrings.F("Summary: {0} entries · {1} changed · {2} left only · {3} right only", _statsDiff.Total, _statsDiff.Changed, _statsDiff.LeftOnly, _statsDiff.RightOnly);
		}

		private static string DisplayFormat(PcapDocument document, string fallback)
		{
			return document?.Format ?? fallback;
		}

		/// <summary>包列表被截断（超过解析保留上限）时在状态行注明保留 / 总数。</summary>
		private string PacketTruncationNote()
		{
			if (_left != null && _left.Truncated)
			{
				return PcapStrings.F("Showing first {0} of {1} packets", _left.Packets.Count, _left.PacketCount);
			}
			if (_right != null && _right.Truncated)
			{
				return PcapStrings.F("Showing first {0} of {1} packets", _right.Packets.Count, _right.PacketCount);
			}
			return null;
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
				_content.Content = NoteText(PcapStrings.T("File too large to preview"));
				return;
			}
			if (_statsDiff == null)
			{
				_content.Content = NoteText(DisplayError() == null ? PcapStrings.T("Analyzing…") : PcapStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			// 两侧都存在却都解析失败（如扩展名对但内容不是抓包）：给错误占位，不再渲染空表。
			if (_left == null && _right == null && DisplayError() != null)
			{
				_content.Content = NoteText(PcapStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			if (_mode == PacketsMode)
			{
				_content.Content = BuildPacketsContent();
			}
			else
			{
				_content.Content = BuildStatisticsContent();
			}
		}

		// ---- 统计表 ----

		/// <summary>统计表：「键路径 | 旧 | 新 | 状态」四列，逐行四色底 + 状态列彩色。
		/// UI 线程只做一次轻量行模型映射，行控件由虚拟化列表按可见范围惰性创建（超大统计不再卡 UI）。</summary>
		private Control BuildStatisticsContent()
		{
			List<TableRow> rows = new List<TableRow>();
			foreach (PcapDiffRow item in _statsDiff.Rows)
			{
				rows.Add(new TableRow
				{
					C0 = item.Path,
					C1 = item.Left ?? PcapStrings.T("not present"),
					C2 = item.Right ?? PcapStrings.T("not present"),
					C3 = StateLabel(item.State),
					Tint = TintOf(item.State),
					StateForeground = StateBrush(item.State)
				});
			}
			TableRow header = new TableRow
			{
				IsHeader = true,
				C0 = PcapStrings.T("Key path"),
				C1 = PcapStrings.T("Old"),
				C2 = PcapStrings.T("New"),
				C3 = PcapStrings.T("State")
			};
			List<Control> notes = new List<Control>();
			if (_srcAbsent || _dstAbsent)
			{
				notes.Add(NoteText(PcapStrings.T("One side is not present; values shown for the other side only.")));
			}
			return NewTableCard(header, rows, notes, StatsTableColumns);
		}

		// ---- 包列表 ----

		/// <summary>包列表：「# | 旧 | 新 | 状态」四列，两侧同序号的包对齐成一行；超过
		/// <see cref="MaxPacketRows"/> 截断并在卡片下方注明。行控件虚拟化。</summary>
		private Control BuildPacketsContent()
		{
			List<TableRow> rows = new List<TableRow>();
			int shown = 0;
			foreach (PacketDiffRow item in _packetDiff.Rows)
			{
				if (shown >= MaxPacketRows)
				{
					break;
				}
				shown++;
				int index = item.Left != null ? item.Left.Index : item.Right.Index;
				rows.Add(new TableRow
				{
					C0 = index.ToString(CultureInfo.InvariantCulture),
					C1 = item.Left != null ? item.Left.Summary : PcapStrings.T("not present"),
					C2 = item.Right != null ? item.Right.Summary : PcapStrings.T("not present"),
					C3 = StateLabel(item.State),
					Tint = TintOf(item.State),
					StateForeground = StateBrush(item.State)
				});
			}
			TableRow header = new TableRow
			{
				IsHeader = true,
				C0 = "#",
				C1 = PcapStrings.T("Old"),
				C2 = PcapStrings.T("New"),
				C3 = PcapStrings.T("State")
			};
			List<Control> notes = new List<Control>();
			if (_packetDiff.Total > shown)
			{
				notes.Add(NoteText(PcapStrings.F("Showing first {0} of {1} rows", shown, _packetDiff.Total)));
			}
			if (_srcAbsent || _dstAbsent)
			{
				notes.Add(NoteText(PcapStrings.T("One side is not present; values shown for the other side only.")));
			}
			return NewTableCard(header, rows, notes, PacketTableColumns);
		}

		// ---- 虚拟化表 ----

		/// <summary>虚拟化表卡片：表头固定首行（不随列表滚动），数据行交给 ItemsControl +
		/// VirtualizingStackPanel 按可见范围惰性物化。滚动条长在控件模板内部——外层不能再包
		/// ScrollViewer，否则视口无限高，虚拟化退化成全量物化（那就白改了）。
		/// 截断提示与口径说明固定在卡片下方，不随表体滚动。</summary>
		private static Control NewTableCard(TableRow header, IList<TableRow> rows, List<Control> notes, string columns)
		{
			TableTemplate template = new TableTemplate(columns);
			Grid inner = new Grid
			{
				RowDefinitions = new RowDefinitions("Auto,*")
			};
			inner.Children.Add(template.Build(header));
			ItemsControl table = new ItemsControl
			{
				ItemsSource = rows,
				ItemsPanel = VirtualizingPanel,
				ItemTemplate = template,
				Template = VirtualTableTemplate
			};
			Grid.SetRow(table, 1);
			inner.Children.Add(table);

			Border card = new Border
			{
				BorderBrush = GridLine,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(6.0),
				Background = Subtle,
				Padding = new Thickness(2.0),
				Child = inner
			};

			Grid root = new Grid
			{
				RowDefinitions = new RowDefinitions("*,Auto")
			};
			root.Children.Add(card);
			StackPanel noteStack = new StackPanel
			{
				Spacing = 6.0
			};
			foreach (Control note in notes)
			{
				noteStack.Children.Add(note);
			}
			Grid.SetRow(noteStack, 1);
			root.Children.Add(noteStack);
			return root;
		}

		/// <summary>四列行模型：四格文本 + 整行四色底 + 状态列前景色；非表头的键路径 / 摘要列用等宽字体。
		/// 构建时一次映射，行控件由 <see cref="TableTemplate"/> 在虚拟化物化时才创建。</summary>
		private sealed class TableRow
		{
			internal string C0;
			internal string C1;
			internal string C2;
			internal string C3;
			internal IBrush Tint;
			internal IBrush StateForeground;
			internal bool IsHeader;
		}

		/// <summary>四列行模板：整行四色底（Border 跨四列）+ 状态列彩色；数据行等宽字体；表头半粗不换行。</summary>
		private sealed class TableTemplate : IDataTemplate
		{
			private readonly string _columns;

			internal TableTemplate(string columns)
			{
				_columns = columns;
			}

			public bool Match(object data)
			{
				return data is TableRow;
			}

			public Control Build(object param)
			{
				TableRow row = (TableRow)param;
				Grid grid = new Grid
				{
					ColumnDefinitions = new ColumnDefinitions(_columns)
				};
				if (row.Tint != null)
				{
					Border background = new Border
					{
						Background = row.Tint,
						CornerRadius = new CornerRadius(3.0)
					};
					Grid.SetColumnSpan(background, 4);
					grid.Children.Add(background);
				}
				bool mono = !row.IsHeader;
				AddCell(grid, 0, row.C0, row.IsHeader, mono, null);
				AddCell(grid, 1, row.C1, row.IsHeader, mono, null);
				AddCell(grid, 2, row.C2, row.IsHeader, mono, null);
				AddCell(grid, 3, row.C3, row.IsHeader, false, row.StateForeground);
				return grid;
			}
		}

		/// <summary>往行 Grid 里加一格文本。</summary>
		private static void AddCell(Grid grid, int column, string text, bool header, bool mono, IBrush foreground)
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
			Grid.SetColumn(block, column);
			grid.Children.Add(block);
		}

		// ---- 文案 / 小部件 ----

		private static IBrush TintOf(PcapState state)
		{
			switch (state)
			{
			case PcapState.Changed:
				return TintChanged;
			case PcapState.LeftOnly:
				return TintLeftOnly;
			case PcapState.RightOnly:
				return TintRightOnly;
			default:
				return null;
			}
		}

		private static IBrush StateBrush(PcapState state)
		{
			switch (state)
			{
			case PcapState.Changed:
				return StateChanged;
			case PcapState.LeftOnly:
				return StateLeft;
			case PcapState.RightOnly:
				return StateRight;
			default:
				return Brushes.Gray;
			}
		}

		private static string StateLabel(PcapState state)
		{
			switch (state)
			{
			case PcapState.Changed:
				return PcapStrings.T("changed");
			case PcapState.LeftOnly:
				return PcapStrings.T("left only");
			case PcapState.RightOnly:
				return PcapStrings.T("right only");
			default:
				return PcapStrings.T("same");
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
