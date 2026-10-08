using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Sqlite
{
	/// <summary>
	/// SQLite 数据库对比视图：把旧（左）/ 新（右）两侧的 .db / .sqlite 文件各自纯托管解析成
	/// 结构模型（文件头统计 + sqlite_master 对象 + 各表 B 树记录），按「表结构」与「数据」两种口径对比。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、
	/// 自带的分段模式工具条（表结构 / 数据），最下是内容区。
	///
	/// 两个模式：
	/// <list type="bullet">
	/// <item>表结构（默认）：一张四列表——「键路径 | 旧值 | 新值 | 状态」。键路径是稳定路径
	/// （<c>table.users.column.email</c> / <c>index.idx_users_email</c> / <c>view.v_active</c>），
	/// 一眼看出改了哪张表 / 哪个列 / 哪个索引。</item>
	/// <item>数据：同构四列表，键路径是 <c>data.users[1]</c> 逐行，值是 <c>col=val, …</c>；
	/// 另有 <c>rows.users</c> 汇总每张表的行数。</item>
	/// </list>
	///
	/// 解析与对齐在后台线程执行，控件只在 UI 线程构建；每次刷新用代次 + CancellationToken 取消上一轮。
	/// 单侧声明大小 &gt; 300 MB 只给提示、不解析。
	/// </summary>
	public sealed class SqliteDiffView : IDiffView
	{
		private const string SchemaMode = "schema";

		private const string DataMode = "data";

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （data，其余走默认 schema），据此逐模式取图；正常运行时该变量为空，走默认表结构模式。
		/// </summary>
		private static readonly string InitialMode = ResolveInitialMode();

		private static string ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			return string.Equals(forced, DataMode, StringComparison.OrdinalIgnoreCase) ? DataMode : SchemaMode;
		}

		/// <summary>单侧超过此大小不解析（与其它二进制插件同口径）。</summary>
		private const long MaxSideBytes = 300L * 1024L * 1024L;

		/// <summary>表结构表渲染上限：行数本就不多，防御性兜底。</summary>
		private const int MaxSchemaRows = 4000;

		/// <summary>数据表渲染上限：超大库截断并在表下注明。</summary>
		private const int MaxDataRows = 3000;

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

		private readonly Button _schemaButton;

		private readonly Button _dataButton;

		private readonly ContentControl _content;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _generation;

		private string _mode = InitialMode;

		private SqliteDocument _left;

		private SqliteDocument _right;

		/// <summary>表结构 diff。</summary>
		private SqliteDiffResult _schemaDiff;

		/// <summary>数据 diff。</summary>
		private SqliteDiffResult _dataDiff;

		private string _error;

		/// <summary>左 / 右两侧各自的解析错误（侧存在但解析失败时用于区分「解析失败」与「不存在」）。</summary>
		private string _srcParseError;

		private string _dstParseError;

		private bool _srcAbsent;

		private bool _dstAbsent;

		private bool _tooLarge;

		public SqliteDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			_status = new TextBlock
			{
				Margin = new Thickness(12.0, 2.0, 12.0, 6.0),
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0
			};
			_schemaButton = NewModeButton(SqliteStrings.T("Schema"), delegate
			{
				SetMode(SchemaMode);
			});
			_dataButton = NewModeButton(SqliteStrings.T("Data"), delegate
			{
				SetMode(DataMode);
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
			modes.Children.Add(_schemaButton);
			modes.Children.Add(_dataButton);

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

			StyleModeButton(_schemaButton, _mode == SchemaMode);
			StyleModeButton(_dataButton, _mode == DataMode);
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
			PluginLog.Info($"SqliteDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			Render();
		}

		public void SetMode(string modeId)
		{
			if (string.IsNullOrEmpty(modeId))
			{
				return;
			}
			if (modeId != SchemaMode && modeId != DataMode)
			{
				return;
			}
			if (modeId == _mode)
			{
				return;
			}
			_mode = modeId;
			StyleModeButton(_schemaButton, _mode == SchemaMode);
			StyleModeButton(_dataButton, _mode == DataMode);
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
			_schemaButton.Content = SqliteStrings.T("Schema");
			_dataButton.Content = SqliteStrings.T("Data");
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
			_schemaDiff = null;
			_dataDiff = null;
			_content.Content = null;
			_status.Text = string.Empty;
		}

		// ---- 渲染 ----

		private void Render()
		{
			CancelRender();
			_left = null;
			_right = null;
			_schemaDiff = null;
			_dataDiff = null;
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
				SqliteDocument left = null;
				SqliteDocument right = null;
				string srcParseError = null;
				string dstParseError = null;
				try
				{
					byte[] leftBytes = ReadBytes(src, hexSrc, cts.Token);
					byte[] rightBytes = ReadBytes(dst, hexDst, cts.Token);
					cts.Token.ThrowIfCancellationRequested();
					// readData: true —— 一次解析同时产出表结构与数据两口径，模式切换无需重解析。
					if (leftBytes != null)
					{
						left = SqliteParser.Parse(leftBytes, true, out srcParseError);
					}
					if (rightBytes != null)
					{
						right = SqliteParser.Parse(rightBytes, true, out dstParseError);
					}
					if (cts.Token.IsCancellationRequested)
					{
						return;
					}
					SqliteDiffResult schemaDiff = SqliteDiff.ComputeRows(left?.SchemaRows, right?.SchemaRows);
					SqliteDiffResult dataDiff = SqliteDiff.ComputeRows(left?.DataRows, right?.DataRows);
					Dispatcher.UIThread.Post(delegate
					{
						if (_generation != generation || _cts != cts)
						{
							return;
						}
						_left = left;
						_right = right;
						_schemaDiff = schemaDiff;
						_dataDiff = dataDiff;
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
					PluginLog.Error("SqliteDiffView 渲染失败", ex);
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
				return roleText + "  ·  " + SqliteStrings.T("not present");
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
		private SqliteDiffResult CurrentDiff()
		{
			return _mode == DataMode ? _dataDiff : _schemaDiff;
		}

		private void UpdateStatus()
		{
			if (_tooLarge)
			{
				_status.Text = SqliteStrings.T("File too large to preview");
				return;
			}
			string error = DisplayError();
			if (error != null)
			{
				_status.Text = SqliteStrings.F("Failed to parse: {0}", error);
				return;
			}
			if (_schemaDiff == null)
			{
				_status.Text = SqliteStrings.T("Analyzing…");
				return;
			}
			string head = SqliteStrings.F("SQLite compare: {0} / {1}", DisplayTitle(_left, _context?.Src), DisplayTitle(_right, _context?.Dst));
			head = head + "  ·  " + SqliteStrings.F("tables: {0} / {1}", _left?.TableCount ?? 0, _right?.TableCount ?? 0);
			head = head + "  ·  " + SqliteStrings.F("rows: {0} / {1}", _left?.TotalDataRows ?? 0, _right?.TotalDataRows ?? 0);
			SqliteDiffResult diff = CurrentDiff();
			if (diff == null || !diff.HasDifferences)
			{
				_status.Text = head + "  ·  " + SqliteStrings.T("No differences");
				return;
			}
			_status.Text = head + "  ·  " + SqliteStrings.F("Summary: {0} entries · {1} changed · {2} left only · {3} right only", diff.Total, diff.Changed, diff.LeftOnly, diff.RightOnly);
		}

		/// <summary>状态行标题：文件名 &gt; not present（侧不存在）。</summary>
		private static string DisplayTitle(SqliteDocument document, DiffSideContent side)
		{
			if (side != null)
			{
				string name = Path.GetFileName(side.Path);
				if (!string.IsNullOrEmpty(name))
				{
					return name;
				}
			}
			return SqliteStrings.T("not present");
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
				_content.Content = NoteText(SqliteStrings.T("File too large to preview"));
				return;
			}
			if (_schemaDiff == null)
			{
				_content.Content = NoteText(DisplayError() == null ? SqliteStrings.T("Analyzing…") : SqliteStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			// 两侧都存在却都解析失败（如扩展名对但内容不是 SQLite）：给错误占位，不再渲染空表。
			if (_left == null && _right == null && DisplayError() != null)
			{
				_content.Content = NoteText(SqliteStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}

			SqliteDiffResult diff = CurrentDiff() ?? new SqliteDiffResult();
			int limit = _mode == DataMode ? MaxDataRows : MaxSchemaRows;
			bool truncated = _mode == DataMode && ((_left?.DataTruncated ?? false) || (_right?.DataTruncated ?? false));

			Grid table = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("2*,3*,3*,Auto")
			};
			int row = 0;
			AddTableRow(table, ref row, SqliteStrings.T("Key path"), SqliteStrings.T("Old"), SqliteStrings.T("New"), SqliteStrings.T("State"), null, null, true);

			int shown = 0;
			foreach (SqliteRowDiff item in diff.Rows)
			{
				if (shown >= limit)
				{
					break;
				}
				shown++;
				AddTableRow(table, ref row, item.Path, DisplayValue(item.Left), DisplayValue(item.Right), StateLabel(item.State), TintOf(item.State), StateBrush(item.State), false);
			}

			StackPanel stack = new StackPanel
			{
				Spacing = 6.0
			};
			stack.Children.Add(WrapInCard(table));
			if (diff.Total > shown)
			{
				stack.Children.Add(NoteText(SqliteStrings.F("Showing first {0} of {1} rows", shown, diff.Total)));
			}
			if (truncated)
			{
				stack.Children.Add(NoteText(SqliteStrings.T("Data truncated: only the first tables and rows are shown.")));
			}
			if (_srcAbsent || _dstAbsent)
			{
				stack.Children.Add(NoteText(SqliteStrings.T("One side is not present; values shown for the other side only.")));
			}
			_content.Content = new ScrollViewer
			{
				Content = stack,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto
			};
		}

		/// <summary>四列表加一行：可带四色底（Border 跨四列）+ 状态列彩色；首列为等宽字体（键路径）。</summary>
		private static void AddTableRow(Grid table, ref int row, string cell0, string cell1, string cell2, string cell3, IBrush tint, IBrush stateBrush, bool header)
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

		private static IBrush TintOf(SqliteState state)
		{
			switch (state)
			{
			case SqliteState.Changed:
				return TintChanged;
			case SqliteState.LeftOnly:
				return TintLeftOnly;
			case SqliteState.RightOnly:
				return TintRightOnly;
			default:
				return null;
			}
		}

		private static IBrush StateBrush(SqliteState state)
		{
			switch (state)
			{
			case SqliteState.Changed:
				return StateChanged;
			case SqliteState.LeftOnly:
				return StateLeft;
			case SqliteState.RightOnly:
				return StateRight;
			default:
				return Brushes.Gray;
			}
		}

		private static string StateLabel(SqliteState state)
		{
			switch (state)
			{
			case SqliteState.Changed:
				return SqliteStrings.T("changed");
			case SqliteState.LeftOnly:
				return SqliteStrings.T("left only");
			case SqliteState.RightOnly:
				return SqliteStrings.T("right only");
			default:
				return SqliteStrings.T("same");
			}
		}

		/// <summary>取值：无此侧的值为 not present。</summary>
		private static string DisplayValue(string value)
		{
			return value ?? SqliteStrings.T("not present");
		}

		/// <summary>表格外层卡片（Border + 细边框）。</summary>
		private static Border WrapInCard(Grid table)
		{
			return new Border
			{
				BorderBrush = GridLine,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(6.0),
				Background = Subtle,
				Padding = new Thickness(2.0),
				Child = table
			};
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