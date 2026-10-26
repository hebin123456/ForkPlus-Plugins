using System;
using System.Collections.Generic;
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

namespace ForkPlus.Plugins.ManagedAssembly
{
	/// <summary>
	/// 托管程序集对比视图：把旧（左）/ 新（右）两侧的 .dll / .exe 各自纯托管解析成
	/// ECMA-335 元数据模型（程序集标识 + 类型 → 方法 / 字段 + AssemblyRef 引用），
	/// 按「标识」「类型」「引用」三种口径对比。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、
	/// 自带的分段模式工具条（标识 / 类型 / 引用），最下是内容区。
	///
	/// 三个模式共用同一份解析结果（<see cref="ManagedDocument"/>），切换模式不重新解析、只重建内容区。
	/// 每一模式都是一张四列表——「键路径 | 旧值 | 新值 | 状态」，键路径是稳定路径：
	/// <list type="bullet">
	/// <item>标识：<c>assembly.name</c> / <c>target framework</c> / <c>machine</c> …</item>
	/// <item>类型：<c>type.NS.T</c> 及其下的 <c>type.NS.T.method.M(int)</c> / <c>type.NS.T.field.F</c></item>
	/// <item>引用：<c>reference.System.Runtime</c>（值 = 版本 · 区域 · 公钥标记）</item>
	/// </list>
	///
	/// 解析与对齐在后台线程执行，控件只在 UI 线程构建；每次刷新用代次 + CancellationToken 取消上一轮。
	/// 单侧声明大小 &gt; 300 MB 只给提示、不解析。
	/// </summary>
	public sealed class ManagedAssemblyDiffView : IDiffView
	{
		private const string IdentityMode = "identity";

		private const string TypesMode = "types";

		private const string ReferencesMode = "references";

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （types / references，其余走默认 identity），据此逐模式取图；正常运行时该变量为空，走默认标识模式。
		/// </summary>
		private static readonly string InitialMode = ResolveInitialMode();

		private static string ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			if (string.Equals(forced, TypesMode, StringComparison.OrdinalIgnoreCase))
			{
				return TypesMode;
			}
			if (string.Equals(forced, ReferencesMode, StringComparison.OrdinalIgnoreCase))
			{
				return ReferencesMode;
			}
			return IdentityMode;
		}

		/// <summary>单侧超过此大小不解析（与其它二进制插件同口径）。</summary>
		private const long MaxSideBytes = 300L * 1024L * 1024L;

		/// <summary>内容区渲染上限：防止超大程序集卡住 UI。</summary>
		private const int MaxRows = 4000;

		/// <summary>四列宽度：键路径 / 旧 / 新按比例伸缩，状态列随内容等比（原来 Auto 在虚拟化下会逐行错列，改星号保证表头与行对齐）。</summary>
		private const string TableColumns = "2*,3*,3*,1*";

		/// <summary>数据行面板：VirtualizingStackPanel 只物化视口内的行（配合 VirtualTableTemplate 的内置 ScrollViewer）。</summary>
		private static readonly FuncTemplate<Panel> VirtualizingPanel = new FuncTemplate<Panel>(delegate
		{
			return new VirtualizingStackPanel();
		});

		/// <summary>虚拟列表模板：ScrollViewer 必须长在控件模板内部——外层 ScrollViewer 会把
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

		private readonly Button _identityButton;

		private readonly Button _typesButton;

		private readonly Button _referencesButton;

		private readonly ContentControl _content;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _generation;

		private string _mode = InitialMode;

		private ManagedDocument _left;

		private ManagedDocument _right;

		private ManagedDiffResult _identityDiff;

		private ManagedDiffResult _typesDiff;

		private ManagedDiffResult _referencesDiff;

		private string _error;

		private string _srcParseError;

		private string _dstParseError;

		private bool _srcAbsent;

		private bool _dstAbsent;

		private bool _tooLarge;

		public ManagedAssemblyDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			_status = new TextBlock
			{
				Margin = new Thickness(12.0, 2.0, 12.0, 6.0),
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0
			};
			_identityButton = NewModeButton(ManagedAssemblyStrings.T("Identity"), delegate
			{
				SetMode(IdentityMode);
			});
			_typesButton = NewModeButton(ManagedAssemblyStrings.T("Types"), delegate
			{
				SetMode(TypesMode);
			});
			_referencesButton = NewModeButton(ManagedAssemblyStrings.T("References"), delegate
			{
				SetMode(ReferencesMode);
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
			modes.Children.Add(_identityButton);
			modes.Children.Add(_typesButton);
			modes.Children.Add(_referencesButton);

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

			StyleModeButton(_identityButton, _mode == IdentityMode);
			StyleModeButton(_typesButton, _mode == TypesMode);
			StyleModeButton(_referencesButton, _mode == ReferencesMode);
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
			PluginLog.Info($"ManagedAssemblyDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			Render();
		}

		public void SetMode(string modeId)
		{
			if (string.IsNullOrEmpty(modeId))
			{
				return;
			}
			if (modeId != IdentityMode && modeId != TypesMode && modeId != ReferencesMode)
			{
				return;
			}
			if (modeId == _mode)
			{
				return;
			}
			_mode = modeId;
			StyleModeButton(_identityButton, _mode == IdentityMode);
			StyleModeButton(_typesButton, _mode == TypesMode);
			StyleModeButton(_referencesButton, _mode == ReferencesMode);
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
			_identityButton.Content = ManagedAssemblyStrings.T("Identity");
			_typesButton.Content = ManagedAssemblyStrings.T("Types");
			_referencesButton.Content = ManagedAssemblyStrings.T("References");
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
			_identityDiff = null;
			_typesDiff = null;
			_referencesDiff = null;
			_content.Content = null;
			_status.Text = string.Empty;
		}

		// ---- 渲染 ----

		private void Render()
		{
			CancelRender();
			_left = null;
			_right = null;
			_identityDiff = null;
			_typesDiff = null;
			_referencesDiff = null;
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
				ManagedDocument left = null;
				ManagedDocument right = null;
				string srcParseError = null;
				string dstParseError = null;
				try
				{
					byte[] leftBytes = ReadBytes(src, hexSrc, cts.Token);
					byte[] rightBytes = ReadBytes(dst, hexDst, cts.Token);
					cts.Token.ThrowIfCancellationRequested();
					if (leftBytes != null)
					{
						left = ManagedAssemblyParser.Parse(leftBytes, out srcParseError);
					}
					if (rightBytes != null)
					{
						right = ManagedAssemblyParser.Parse(rightBytes, out dstParseError);
					}
					if (cts.Token.IsCancellationRequested)
					{
						return;
					}
					ManagedDiffResult identityDiff = ManagedDiff.ComputeRows(left?.IdentityRows, right?.IdentityRows);
					ManagedDiffResult typesDiff = ManagedDiff.ComputeRows(left?.TypeRows, right?.TypeRows);
					ManagedDiffResult referencesDiff = ManagedDiff.ComputeRows(left?.ReferenceRows, right?.ReferenceRows);
					Dispatcher.UIThread.Post(delegate
					{
						if (_generation != generation || _cts != cts)
						{
							return;
						}
						_left = left;
						_right = right;
						_identityDiff = identityDiff;
						_typesDiff = typesDiff;
						_referencesDiff = referencesDiff;
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
					PluginLog.Error("ManagedAssemblyDiffView 渲染失败", ex);
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

		/// <summary>
		/// 取一侧原始字节：宿主 Hex 预载 &gt; 侧内容懒加载 &gt; LFS 本地缓存；
		/// 保存 / 恢复流 Position，拷成 byte[]（解析器自建视图用，不动宿主的流）。
		/// </summary>
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
				byte[] bytes = new byte[data.Length];
				int offset = 0;
				while (offset < bytes.Length)
				{
					int read = data.Read(bytes, offset, bytes.Length - offset);
					if (read <= 0)
					{
						break;
					}
					offset += read;
				}
				if (offset < bytes.Length)
				{
					byte[] exact = new byte[offset];
					Array.Copy(bytes, exact, offset);
					bytes = exact;
				}
				return bytes;
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
				return roleText + "  ·  " + ManagedAssemblyStrings.T("not present");
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

		/// <summary>当前模式的 diff 结果（未解析完成为 null）。</summary>
		private ManagedDiffResult CurrentDiff()
		{
			return _mode == TypesMode ? _typesDiff : (_mode == ReferencesMode ? _referencesDiff : _identityDiff);
		}

		private void UpdateStatus()
		{
			if (_tooLarge)
			{
				_status.Text = ManagedAssemblyStrings.T("File too large to preview");
				return;
			}
			string error = DisplayError();
			if (error != null)
			{
				_status.Text = ManagedAssemblyStrings.F("Failed to parse: {0}", error);
				return;
			}
			if (_identityDiff == null)
			{
				_status.Text = ManagedAssemblyStrings.T("Analyzing…");
				return;
			}
			string head = ManagedAssemblyStrings.F("Managed assembly compare: {0} / {1}", DisplayTitle(_left, _context?.Src), DisplayTitle(_right, _context?.Dst));
			head = head + "  ·  " + ManagedAssemblyStrings.F("types: {0} / {1}", _left?.TypeCount ?? 0, _right?.TypeCount ?? 0);
			head = head + "  ·  " + ManagedAssemblyStrings.F("members: {0} / {1}", (_left?.MethodCount ?? 0) + (_left?.FieldCount ?? 0), (_right?.MethodCount ?? 0) + (_right?.FieldCount ?? 0));
			ManagedDiffResult diff = CurrentDiff();
			if (diff == null || !diff.HasDifferences)
			{
				_status.Text = head + "  ·  " + ManagedAssemblyStrings.T("No differences");
				return;
			}
			_status.Text = head + "  ·  " + ManagedAssemblyStrings.F("Summary: {0} entries · {1} changed · {2} left only · {3} right only", diff.Total, diff.Changed, diff.LeftOnly, diff.RightOnly);
		}

		/// <summary>状态行标题：程序集名（有）&gt; 文件名 &gt; not present。</summary>
		private static string DisplayTitle(ManagedDocument document, DiffSideContent side)
		{
			if (document != null && !string.IsNullOrEmpty(document.AssemblyName))
			{
				string version = string.IsNullOrEmpty(document.AssemblyVersion) ? string.Empty : " v" + document.AssemblyVersion;
				return document.AssemblyName + version;
			}
			if (side != null)
			{
				string name = Path.GetFileName(side.Path);
				if (!string.IsNullOrEmpty(name))
				{
					return name;
				}
			}
			return ManagedAssemblyStrings.T("not present");
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
				_content.Content = NoteText(ManagedAssemblyStrings.T("File too large to preview"));
				return;
			}
			if (_identityDiff == null)
			{
				_content.Content = NoteText(DisplayError() == null ? ManagedAssemblyStrings.T("Analyzing…") : ManagedAssemblyStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			// 两侧都存在却都解析失败（如扩展名对但内容不是托管程序集）：给错误占位，不再渲染空表。
			if (_left == null && _right == null && DisplayError() != null)
			{
				_content.Content = NoteText(ManagedAssemblyStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}

			ManagedDiffResult diff = CurrentDiff() ?? new ManagedDiffResult();
			bool truncated = (_left?.Truncated ?? false) || (_right?.Truncated ?? false);

			// UI 线程只做一次轻量行模型映射，行控件由虚拟化列表按可见范围惰性创建（大程序集不再卡 UI）。
			List<TableRow> rows = new List<TableRow>();
			int shown = 0;
			foreach (ManagedRowDiff item in diff.Rows)
			{
				if (shown >= MaxRows)
				{
					break;
				}
				shown++;
				rows.Add(new TableRow
				{
					C0 = item.Path,
					C1 = DisplayValue(item.Left),
					C2 = DisplayValue(item.Right),
					C3 = StateLabel(item.State),
					Tint = TintOf(item.State),
					StateForeground = StateBrush(item.State),
					Mono0 = true
				});
			}
			TableRow header = new TableRow
			{
				IsHeader = true,
				C0 = ManagedAssemblyStrings.T("Key path"),
				C1 = ManagedAssemblyStrings.T("Old"),
				C2 = ManagedAssemblyStrings.T("New"),
				C3 = ManagedAssemblyStrings.T("State")
			};
			_content.Content = NewTableCard(header, rows, TableNotes(shown, diff.Total, truncated));
		}

		/// <summary>表下方备注：截断提示 + 单侧缺失口径说明（固定在卡片下，不随表体滚动）。</summary>
		private List<Control> TableNotes(int shown, int total, bool truncated)
		{
			List<Control> notes = new List<Control>();
			if (total > shown)
			{
				notes.Add(NoteText(ManagedAssemblyStrings.F("Showing first {0} of {1} rows", shown, total)));
			}
			if (truncated)
			{
				notes.Add(NoteText(ManagedAssemblyStrings.T("Metadata truncated: only the first types / members / references are shown.")));
			}
			if (_srcAbsent || _dstAbsent)
			{
				notes.Add(NoteText(ManagedAssemblyStrings.T("One side is not present; values shown for the other side only.")));
			}
			return notes;
		}

		/// <summary>虚拟化表卡片：表头固定首行，数据行交给 ItemsControl + VirtualizingStackPanel
		/// 按可见范围惰性物化。滚动条长在控件模板内部——外层不能再包 ScrollViewer，
		/// 否则视口无限高，虚拟化退化成全量物化（那就白改了）。
		/// 截断提示与口径说明固定在卡片下方，不随表体滚动。</summary>
		private static Control NewTableCard(TableRow header, IList<TableRow> rows, List<Control> notes)
		{
			TableTemplate template = new TableTemplate();
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

		/// <summary>四列行模型：四格文本 + 整行四色底 + 状态列前景色；首列可等宽。
		/// 构建时一次映射，行控件由 <see cref="TableTemplate"/> 在虚拟化物化时才创建。</summary>
		private sealed class TableRow
		{
			internal string C0;
			internal string C1;
			internal string C2;
			internal string C3;
			internal IBrush Tint;
			internal IBrush StateForeground;
			internal bool Mono0;
			internal bool IsHeader;
		}

		/// <summary>四列行模板：整行四色底（Border 跨四列）+ 状态列彩色；首列可等宽；表头半粗不换行。</summary>
		private sealed class TableTemplate : IDataTemplate
		{
			public bool Match(object data)
			{
				return data is TableRow;
			}

			public Control Build(object param)
			{
				TableRow row = (TableRow)param;
				Grid grid = new Grid
				{
					ColumnDefinitions = new ColumnDefinitions(TableColumns)
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
				AddCell(grid, 0, row.C0, row.IsHeader, row.Mono0 && !row.IsHeader, null);
				AddCell(grid, 1, row.C1, row.IsHeader, false, null);
				AddCell(grid, 2, row.C2, row.IsHeader, false, null);
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

		private static IBrush TintOf(ManagedState state)
		{
			switch (state)
			{
			case ManagedState.Changed:
				return TintChanged;
			case ManagedState.LeftOnly:
				return TintLeftOnly;
			case ManagedState.RightOnly:
				return TintRightOnly;
			default:
				return null;
			}
		}

		private static IBrush StateBrush(ManagedState state)
		{
			switch (state)
			{
			case ManagedState.Changed:
				return StateChanged;
			case ManagedState.LeftOnly:
				return StateLeft;
			case ManagedState.RightOnly:
				return StateRight;
			default:
				return Brushes.Gray;
			}
		}

		private static string StateLabel(ManagedState state)
		{
			switch (state)
			{
			case ManagedState.Changed:
				return ManagedAssemblyStrings.T("changed");
			case ManagedState.LeftOnly:
				return ManagedAssemblyStrings.T("left only");
			case ManagedState.RightOnly:
				return ManagedAssemblyStrings.T("right only");
			default:
				return ManagedAssemblyStrings.T("same");
			}
		}

		/// <summary>取值：无此侧的值为 not present。</summary>
		private static string DisplayValue(string value)
		{
			return value ?? ManagedAssemblyStrings.T("not present");
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