using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Dbc
{
	/// <summary>
	/// DBC（CAN 数据库）对比视图：把旧（左）/ 新（右）两侧的 DBC 文本经 <c>DbcParserLib</c>
	/// 解析成同一套数据树，再按「键路径」做语义 diff —— 不再逐字符比文本，而是逐键比
	/// 「新增 / 删除 / 改值」。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、
	/// 自带的分段模式工具条（结构化 / 原文 + 「仅差异」开关），最下是内容区。
	///
	/// 三个模式 / 开关：
	/// <list type="bullet">
	/// <item>结构化（默认）：一张四列表——「键路径 | 旧值 | 新值 | 状态」，逐行按
	/// 相同 / 已变更 / 仅左 / 仅右 四色标注底色，是看 DBC 改动的首选；表格经
	/// <see cref="VirtualizingStackPanel"/> 虚拟化，只物化视口内的行（大文件 formerly 一次性
	/// 物化数千行 TextBlock 会把 UI 线程拖住数秒）。</item>
	/// <item>「仅差异」（默认开）：Same 行不进表格（计数仍全量，状态行可见），需要全量时关掉即后台重算。</item>
	/// <item>原文：左右两栏并排展示两侧原始文本（按行拆分 + 虚拟化；单行超长截断展示，
	/// 不影响结构化 diff），便于对照上下文。</item>
	/// </list>
	///
	/// 解析与 diff 在后台线程执行，控件只在 UI 线程构建；每次刷新用代次 + CancellationToken 取消上一轮。
	/// 单侧声明大小 &gt; 300 MB 只给提示、不解析。
	/// </summary>
	public sealed class DbcDiffView : IDiffView
	{
		private const string KeysMode = "keys";

		private const string RawMode = "raw";

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （raw，其余走默认 keys），据此逐模式取图；正常运行时该变量为空，走默认结构化模式。
		/// </summary>
		private static readonly string InitialMode = ResolveInitialMode();

		private static string ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			return string.Equals(forced, RawMode, StringComparison.OrdinalIgnoreCase) ? RawMode : KeysMode;
		}

		/// <summary>单侧超过此大小不解析（与其它插件同口径）。</summary>
		private const long MaxSideBytes = 300L * 1024L * 1024L;

		/// <summary>表格行物化上限（虚拟化只是「不全部创建控件」，行对象 / 键路径串仍要占内存，
		/// 20 万行级的极端对比按此封顶，状态行提示总数）。</summary>
		private const int MaxRows = 20000;

		/// <summary>单元格里单值最大展示长度，超出截断（不影响 diff 比较）。</summary>
		private const int MaxValueChars = 300;

		/// <summary>原文模式单行展示长度上限：巨型 VAL_ 行可达百万字符，原样塞进 TextBlock
		/// 会让单次文本排版卡死 UI（截断只影响原文展示，不影响结构化 diff）。</summary>
		private const int MaxRawLineChars = 2000;

		/// <summary>状态列固定像素宽：各行独立 Grid 靠相同列定义对齐，Auto 列会逐行漂移。</summary>
		private const string TableColumns = "2*,2*,2*,96";

		private static readonly IBrush GridLine = Brushes.Gainsboro;

		private static readonly IBrush Subtle = new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x80, 0x80));

		private static readonly IBrush TintChanged = new SolidColorBrush(Color.FromArgb(0x2E, 0xE6, 0x7E, 0x22));

		private static readonly IBrush TintLeftOnly = new SolidColorBrush(Color.FromArgb(0x26, 0xC0, 0x39, 0x2B));

		private static readonly IBrush TintRightOnly = new SolidColorBrush(Color.FromArgb(0x26, 0x2E, 0x9E, 0x5B));

		private static readonly IBrush StateChanged = new SolidColorBrush(Color.FromArgb(0xFF, 0xB0, 0x5A, 0x00));

		private static readonly IBrush StateLeft = new SolidColorBrush(Color.FromArgb(0xFF, 0xC0, 0x39, 0x2B));

		private static readonly IBrush StateRight = new SolidColorBrush(Color.FromArgb(0xFF, 0x2E, 0x9E, 0x5B));

		private static readonly FontFamily MonoFont = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, Courier New, monospace");

		private static readonly FuncTemplate<Panel> VirtualizingPanel = new FuncTemplate<Panel>(delegate
		{
			return new VirtualizingStackPanel();
		});

		private readonly Grid _root;

		private readonly TextBlock _srcTitle;

		private readonly TextBlock _dstTitle;

		private readonly TextBlock _status;

		private readonly Button _keysButton;

		private readonly Button _rawButton;

		private readonly Button _onlyDiffsButton;

		private readonly ContentControl _content;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _generation;

		private string _mode = InitialMode;

		/// <summary>「仅差异」开关：开 = Same 行不进表格（计数照常）。默认开——大文件 99% 的行
		/// 是 Same，全量表格既慢又淹没真正的差异。</summary>
		private bool _onlyDiffs = true;

		private DataNode _left;

		private DataNode _right;

		private DiffSummary _summary;

		private List<string> _leftLines;

		private List<string> _rightLines;

		private string _error;

		private bool _srcAbsent;

		private bool _dstAbsent;

		private bool _tooLarge;

		private int _messageCount;

		private int _signalCount;

		public DbcDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			_status = new TextBlock
			{
				Margin = new Thickness(12.0, 2.0, 12.0, 6.0),
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0
			};
			_keysButton = NewModeButton(DbcStrings.T("Structured"), delegate
			{
				SetMode(KeysMode);
			});
			_rawButton = NewModeButton(DbcStrings.T("Raw text"), delegate
			{
				SetMode(RawMode);
			});
			_onlyDiffsButton = NewModeButton(DbcStrings.T("Only differences"), delegate
			{
				SetOnlyDiffs(!_onlyDiffs);
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
			modes.Children.Add(_keysButton);
			modes.Children.Add(_rawButton);
			modes.Children.Add(_onlyDiffsButton);

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

			StyleModeButton(_keysButton, _mode == KeysMode);
			StyleModeButton(_rawButton, _mode == RawMode);
			StyleModeButton(_onlyDiffsButton, _onlyDiffs);
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
			PluginLog.Info($"DbcDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			Render();
		}

		public void SetMode(string modeId)
		{
			if (string.IsNullOrEmpty(modeId))
			{
				return;
			}
			if (modeId != KeysMode && modeId != RawMode)
			{
				return;
			}
			if (modeId == _mode)
			{
				return;
			}
			_mode = modeId;
			StyleModeButton(_keysButton, _mode == KeysMode);
			StyleModeButton(_rawButton, _mode == RawMode);
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
			_keysButton.Content = DbcStrings.T("Structured");
			_rawButton.Content = DbcStrings.T("Raw text");
			_onlyDiffsButton.Content = DbcStrings.T("Only differences");
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
			_summary = null;
			_leftLines = null;
			_rightLines = null;
			_content.Content = null;
			_status.Text = string.Empty;
		}

		// ---- 渲染 ----

		private void Render()
		{
			CancelRender();
			_left = null;
			_right = null;
			_summary = null;
			_leftLines = null;
			_rightLines = null;
			_error = null;
			_tooLarge = false;
			_messageCount = 0;
			_signalCount = 0;
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
				DataNode left = null;
				DataNode right = null;
				List<string> leftLines = null;
				List<string> rightLines = null;
				string error = null;
				try
				{
					string leftText = ReadText(src, hexSrc, cts.Token);
					string rightText = ReadText(dst, hexDst, cts.Token);
					cts.Token.ThrowIfCancellationRequested();
					if (src != null)
					{
						left = DbcParser.Parse(leftText, cts.Token, out string leftError);
						error = leftError;
					}
					cts.Token.ThrowIfCancellationRequested();
					if (dst != null)
					{
						string rightError;
						right = DbcParser.Parse(rightText, cts.Token, out rightError);
						error = error ?? rightError;
					}
					if (cts.Token.IsCancellationRequested)
					{
						return;
					}
					// 开关状态在计算时读取：解析期间切换「仅差异」无需重算。
					DiffSummary summary = DataDiff.Compute(left, right, MaxRows, !_onlyDiffs);
					leftLines = src == null ? null : SplitLines(leftText);
					rightLines = dst == null ? null : SplitLines(rightText);
					CountDbc(right ?? left, out int messages, out int signals);
					Dispatcher.UIThread.Post(delegate
					{
						if (_generation != generation || _cts != cts)
						{
							return;
						}
						_left = left;
						_right = right;
						_leftLines = leftLines;
						_rightLines = rightLines;
						_summary = summary;
						_messageCount = messages;
						_signalCount = signals;
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
					PluginLog.Error("DbcDiffView 渲染失败", ex);
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

		/// <summary>切换「仅差异」：树已在时只重算 diff（毫秒级），不重新解析。</summary>
		private void SetOnlyDiffs(bool only)
		{
			if (only == _onlyDiffs)
			{
				return;
			}
			_onlyDiffs = only;
			StyleModeButton(_onlyDiffsButton, _onlyDiffs);
			if (_summary == null)
			{
				// 解析仍在途：渲染任务计算时会读最新开关，无需补算。
				UpdateStatus();
				return;
			}
			DataNode left = _left;
			DataNode right = _right;
			CancellationTokenSource cts = _cts;
			if (cts == null)
			{
				return;
			}
			int generation = _generation;
			Task.Run(delegate
			{
				try
				{
					DiffSummary summary = DataDiff.Compute(left, right, MaxRows, !_onlyDiffs);
					Dispatcher.UIThread.Post(delegate
					{
						if (_generation != generation || _cts != cts)
						{
							return;
						}
						_summary = summary;
						UpdateStatus();
						if (_mode == KeysMode)
						{
							BuildContent();
						}
					});
				}
				catch (Exception ex)
				{
					PluginLog.Error("DbcDiffView 重算 diff 失败", ex);
				}
			});
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

		/// <summary>按行拆分（去行尾 \r，超长行截断展示）——原文模式虚拟化列表的数据源。</summary>
		private static List<string> SplitLines(string text)
		{
			List<string> lines = new List<string>();
			if (string.IsNullOrEmpty(text))
			{
				return lines;
			}
			int start = 0;
			while (start <= text.Length)
			{
				int newline = text.IndexOf('\n', start);
				int end = newline < 0 ? text.Length : newline;
				int lineEnd = end;
				if (lineEnd > start && text[lineEnd - 1] == '\r')
				{
					lineEnd--;
				}
				lines.Add(TruncateLine(text.Substring(start, lineEnd - start)));
				if (newline < 0)
				{
					break;
				}
				start = newline + 1;
			}
			return lines;
		}

		private static string TruncateLine(string line)
		{
			if (line.Length <= MaxRawLineChars)
			{
				return line;
			}
			return line.Substring(0, MaxRawLineChars) + "…";
		}

		/// <summary>统计报文集里的报文数与信号数，供状态行展示规模。</summary>
		private static void CountDbc(DataNode root, out int messages, out int signals)
		{
			messages = 0;
			signals = 0;
			if (root == null || root.Kind != DataKind.Map)
			{
				return;
			}
			DataNode messagesNode = Find(root, "Messages");
			if (messagesNode != null && messagesNode.Kind == DataKind.Map)
			{
				messages = messagesNode.Entries.Count;
				foreach (DataEntry message in messagesNode.Entries)
				{
					DataNode signalsNode = message.Node.Kind == DataKind.Map ? Find(message.Node, "Signals") : null;
					if (signalsNode != null && signalsNode.Kind == DataKind.Map)
					{
						signals += signalsNode.Entries.Count;
					}
				}
			}
		}

		private static DataNode Find(DataNode map, string key)
		{
			foreach (DataEntry entry in map.Entries)
			{
				if (string.Equals(entry.Key, key, StringComparison.Ordinal))
				{
					return entry.Node;
				}
			}
			return null;
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
				return roleText + "  ·  " + DbcStrings.T("not present");
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

		private void UpdateStatus()
		{
			if (_tooLarge)
			{
				_status.Text = DbcStrings.T("File too large to preview");
				return;
			}
			if (_error != null)
			{
				_status.Text = DbcStrings.F("Failed to parse: {0}", _error);
				return;
			}
			if (_summary == null)
			{
				_status.Text = DbcStrings.T("Analyzing…");
				return;
			}
			string head = DbcStrings.F("DBC compare: {0} messages · {1} signals", _messageCount, _signalCount);
			if (!_summary.HasDifferences)
			{
				_status.Text = head + "  ·  " + DbcStrings.T("No differences");
				return;
			}
			_status.Text = head + "  ·  " + DbcStrings.F("Summary: {0} keys · {1} changed · {2} left only · {3} right only", _summary.Total, _summary.Changed, _summary.LeftOnly, _summary.RightOnly);
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
				_content.Content = NoteText(DbcStrings.T("File too large to preview"));
				return;
			}
			if (_summary == null)
			{
				_content.Content = NoteText(_error == null ? DbcStrings.T("Analyzing…") : DbcStrings.F("Failed to parse: {0}", _error));
				return;
			}
			if (_mode == RawMode)
			{
				_content.Content = BuildRawContent();
			}
			else
			{
				_content.Content = BuildKeyPathContent();
			}
		}

		/// <summary>表头标记行：作为列表第一项与数据行同列定义，保证跨行列对齐（各行独立 Grid）。</summary>
		private sealed class HeaderRow
		{
			internal static readonly HeaderRow Instance = new HeaderRow();
		}

		/// <summary>行模板：按需物化（仅视口内），表头 / 数据行共用一套列定义。</summary>
		private sealed class RowTemplate : IDataTemplate
		{
			public bool Match(object data)
			{
				return data is DiffRow || data is HeaderRow;
			}

			public Control Build(object param)
			{
				return param is HeaderRow ? BuildHeaderRow() : BuildDataRow((DiffRow)param);
			}
		}

		/// <summary>原文行模板：item 即行文本。</summary>
		private sealed class RawLineTemplate : IDataTemplate
		{
			public bool Match(object data)
			{
				return data is string;
			}

			public Control Build(object param)
			{
				return new TextBlock
				{
					Text = (string)param,
					FontFamily = MonoFont,
					FontSize = 12.0,
					TextWrapping = TextWrapping.NoWrap,
					Foreground = Brushes.Black
				};
			}
		}

		private Control BuildKeyPathContent()
		{
			object[] items = new object[_summary.Rows.Count + 1];
			items[0] = HeaderRow.Instance;
			for (int i = 0; i < _summary.Rows.Count; i++)
			{
				items[i + 1] = _summary.Rows[i];
			}
			ItemsControl table = new ItemsControl
			{
				ItemsSource = items,
				ItemsPanel = VirtualizingPanel,
				ItemTemplate = new RowTemplate()
			};
			ScrollViewer scroll = new ScrollViewer
			{
				Content = table,
				// 星号列宽依赖有限宽度约束；开横向滚动会退化成无限宽。
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto
			};

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
				Child = scroll
			};
			stack.Children.Add(card);
			int relevantTotal = _summary.IncludeSame
				? _summary.Total
				: _summary.Changed + _summary.LeftOnly + _summary.RightOnly;
			if (_summary.Rows.Count < relevantTotal)
			{
				stack.Children.Add(NoteText(DbcStrings.F("Showing first {0} of {1} rows", _summary.Rows.Count, relevantTotal)));
			}
			if (_srcAbsent || _dstAbsent)
			{
				stack.Children.Add(NoteText(DbcStrings.T("One side is not present; values shown for the other side only.")));
			}
			return stack;
		}

		private static Control BuildHeaderRow()
		{
			Grid header = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions(TableColumns)
			};
			AddCell(header, 0, DbcStrings.T("Key path"), true, null, false);
			AddCell(header, 1, DbcStrings.T("Old"), false, null, false);
			AddCell(header, 2, DbcStrings.T("New"), false, null, false);
			AddCell(header, 3, DbcStrings.T("State"), false, null, false);
			return header;
		}

		private static Control BuildDataRow(DiffRow row)
		{
			Grid grid = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions(TableColumns)
			};
			AddCell(grid, 0, Truncate(row.Path), true, Brushes.Gray, true);
			AddCell(grid, 1, Display(row.Left, row.LeftPresent), false, null, true);
			AddCell(grid, 2, Display(row.Right, row.RightPresent), false, null, true);
			AddCell(grid, 3, StateLabel(row.State), false, StateBrush(row.State), false);
			return new Border
			{
				Background = TintOf(row.State),
				CornerRadius = new CornerRadius(3.0),
				Child = grid
			};
		}

		private static void AddCell(Grid grid, int column, string text, bool mono, IBrush foreground, bool wrap)
		{
			TextBlock block = new TextBlock
			{
				Text = text ?? string.Empty,
				Margin = new Thickness(8.0, 3.0, 8.0, 3.0),
				TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
				FontFamily = mono ? MonoFont : FontFamily.Default,
				Foreground = foreground ?? Brushes.Black,
				VerticalAlignment = VerticalAlignment.Top
			};
			Grid.SetColumn(block, column);
			grid.Children.Add(block);
		}

		/// <summary>原文模式：左右两栏并排，各自虚拟化行列表（等宽字体、可滚动）。</summary>
		private Control BuildRawContent()
		{
			Grid panes = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,*")
			};
			Border leftCard = RawCard(_srcAbsent ? null : _leftLines);
			Border rightCard = RawCard(_dstAbsent ? null : _rightLines);
			Grid.SetColumn(leftCard, 0);
			Grid.SetColumn(rightCard, 1);
			panes.Children.Add(leftCard);
			panes.Children.Add(rightCard);
			return panes;
		}

		private static Border RawCard(List<string> lines)
		{
			Control body;
			if (lines == null)
			{
				body = NoteText(DbcStrings.T("not present"));
			}
			else if (lines.Count == 0)
			{
				body = NoteText(DbcStrings.T("empty"));
			}
			else
			{
				ItemsControl list = new ItemsControl
				{
					ItemsSource = lines,
					ItemsPanel = VirtualizingPanel,
					ItemTemplate = new RawLineTemplate()
				};
				body = list;
			}
			return new Border
			{
				Margin = new Thickness(0.0, 0.0, 6.0, 0.0),
				BorderBrush = GridLine,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(6.0),
				Background = Subtle,
				Padding = new Thickness(6.0),
				Child = new ScrollViewer
				{
					Content = body,
					HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
					VerticalScrollBarVisibility = ScrollBarVisibility.Auto
				}
			};
		}

		private static IBrush TintOf(DiffState state)
		{
			switch (state)
			{
			case DiffState.Changed:
				return TintChanged;
			case DiffState.LeftOnly:
				return TintLeftOnly;
			case DiffState.RightOnly:
				return TintRightOnly;
			default:
				return null;
			}
		}

		private static IBrush StateBrush(DiffState state)
		{
			switch (state)
			{
			case DiffState.Changed:
				return StateChanged;
			case DiffState.LeftOnly:
				return StateLeft;
			case DiffState.RightOnly:
				return StateRight;
			default:
				return Brushes.Gray;
			}
		}

		private static string StateLabel(DiffState state)
		{
			switch (state)
			{
			case DiffState.Changed:
				return DbcStrings.T("changed");
			case DiffState.LeftOnly:
				return DbcStrings.T("left only");
			case DiffState.RightOnly:
				return DbcStrings.T("right only");
			default:
				return DbcStrings.T("same");
			}
		}

		private static string Display(string value, bool present)
		{
			if (!present)
			{
				return DbcStrings.T("not present");
			}
			return Truncate(value);
		}

		private static string Truncate(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}
			if (value.Length <= MaxValueChars)
			{
				return value;
			}
			return value.Substring(0, MaxValueChars) + "…";
		}

		// ---- 小部件 ----

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
				// 宿主 Button 主题固定 Height=24，垂直 Padding 合计必须 ≤4px；要更厚用 MinHeight。
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
