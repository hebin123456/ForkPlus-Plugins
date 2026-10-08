using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Structured
{
	/// <summary>
	/// 结构化数据对比视图：把旧（左）/ 新（右）两侧的 JSON / YAML / TOML / XML / INI 解析成同一套
	/// 数据树，再按「键路径」做语义 diff —— 不再逐字符比文本，而是逐键比「新增 / 删除 / 改值」。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、
	/// 自带的分段模式工具条（键路径 / 结构树），最下是内容区。
	///
	/// 两个模式：
	/// <list type="bullet">
	/// <item>键路径（默认）：一张四列表——「键路径 | 旧值 | 新值 | 状态」，逐行按
	/// 相同 / 已变更 / 仅左 / 仅右 四色标注底色，是看配置改动的首选。</item>
	/// <item>结构树：左右两栏按原文档结构呈现。v5.0.4 起为宿主树控件同款的可折叠树——
	/// 16×16 chevron（复用宿主 <c>ExpandCollapseToggleStyle</c> 主题）、10px/层缩进、行高 20、
	/// 主题前景色与悬停高亮；子级懒构建（首次展开才实化，超大文档只建可见部分），
	/// 命中变更的分支自动展开，另附「全部展开 / 全部折叠」工具按钮。逐节点按同一套四色标注，
	/// 便于顺着嵌套层级看改动落在哪一支。</item>
	/// </list>
	///
	/// 解析与 diff 在后台线程执行，控件只在 UI 线程构建；每次刷新用代次 + CancellationToken 取消上一轮。
	/// 单侧声明大小 &gt; 300 MB 只给提示、不解析。
	/// </summary>
	public sealed class StructuredDiffView : IDiffView
	{
		private const string KeysMode = "keys";

		private const string TreeMode = "tree";

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （tree，其余走默认 keys），据此逐模式取图；正常运行时该变量为空，走默认键路径模式。
		/// </summary>
		private static readonly string InitialMode = ResolveInitialMode();

		private static string ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			return string.Equals(forced, TreeMode, StringComparison.OrdinalIgnoreCase) ? TreeMode : KeysMode;
		}

		/// <summary>单侧超过此大小不解析（与音视频插件同口径）。</summary>
		private const long MaxSideBytes = 300L * 1024L * 1024L;

		/// <summary>渲染上限：键路径表是非虚拟化 Grid（每行多个控件一次性布局），
		/// 行数过万的多文档合集直接铺 4000 行会拖死 UI——上限压到 500，
		/// 底部「仅显示前 N 行」提示兜底，完整导航走结构树（懒展开 + 节点预算）。</summary>
		private const int MaxRows = 500;

		private const int MaxTreeNodes = 4000;

		/// <summary>单元格里单值最大展示长度，超出截断（不影响 diff 比较）。</summary>
		private const int MaxValueChars = 300;

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

		private readonly Button _keysButton;

		private readonly Button _treeButton;

		private readonly ContentControl _content;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _generation;

		private string _mode = InitialMode;

		private DataNode _left;

		private DataNode _right;

		private DiffSummary _summary;

		private string _error;

		/// <summary>左 / 右两侧各自的解析错误（侧存在但解析失败时用于区分「解析失败」与「不存在」）。</summary>
		private string _srcParseError;

		private string _dstParseError;

		private string _srcFormat = "?";

		private string _dstFormat = "?";

		private bool _srcAbsent;

		private bool _dstAbsent;

		private bool _tooLarge;

		private readonly Button _expandAllButton;

		private readonly Button _collapseAllButton;

		/// <summary>树模式当前实化的全部行（两侧合计），供「全部展开 / 全部折叠」遍历；
		/// 懒展开会向里追加，遍历必须用下标 for。</summary>
		private readonly List<TreeRow> _treeRows = new List<TreeRow>();

		public StructuredDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			_status = new TextBlock
			{
				Margin = new Thickness(12.0, 2.0, 12.0, 6.0),
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0
			};
			_keysButton = NewModeButton(StructuredStrings.T("Key path"), delegate
			{
				SetMode(KeysMode);
			});
			_treeButton = NewModeButton(StructuredStrings.T("Structure tree"), delegate
			{
				SetMode(TreeMode);
			});
		_expandAllButton = NewModeButton(StructuredStrings.T("Expand all"), delegate
			{
				ExpandAllTreeRows();
			});
		_collapseAllButton = NewModeButton(StructuredStrings.T("Collapse all"), delegate
			{
				CollapseAllTreeRows();
			});
		_expandAllButton.Margin = new Thickness(10.0, 2.0, 4.0, 2.0);
		_expandAllButton.IsVisible = _mode == TreeMode;
		_collapseAllButton.IsVisible = _mode == TreeMode;
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
			modes.Children.Add(_treeButton);
			modes.Children.Add(_expandAllButton);
			modes.Children.Add(_collapseAllButton);

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
			StyleModeButton(_treeButton, _mode == TreeMode);
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
			PluginLog.Info($"StructuredDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			Render();
		}

		public void SetMode(string modeId)
		{
			if (string.IsNullOrEmpty(modeId))
			{
				return;
			}
			if (modeId != KeysMode && modeId != TreeMode)
			{
				return;
			}
			if (modeId == _mode)
			{
				return;
			}
			_mode = modeId;
			StyleModeButton(_keysButton, _mode == KeysMode);
			StyleModeButton(_treeButton, _mode == TreeMode);
			_expandAllButton.IsVisible = modeId == TreeMode;
			_collapseAllButton.IsVisible = modeId == TreeMode;
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
			_keysButton.Content = StructuredStrings.T("Key path");
			_treeButton.Content = StructuredStrings.T("Structure tree");
			_expandAllButton.Content = StructuredStrings.T("Expand all");
			_collapseAllButton.Content = StructuredStrings.T("Collapse all");
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
			_treeRows.Clear();
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
			string srcFormat = _srcFormat;
			string dstFormat = _dstFormat;

			Task.Run(delegate
			{
				DataNode left = null;
				DataNode right = null;
				string srcParseError = null;
				string dstParseError = null;
				try
				{
					string leftText = ReadText(src, hexSrc, cts.Token);
					string rightText = ReadText(dst, hexDst, cts.Token);
					cts.Token.ThrowIfCancellationRequested();
					if (src != null)
					{
						left = StructuredParser.Parse(leftText, srcFormat, out srcParseError);
					}
					if (dst != null)
					{
						right = StructuredParser.Parse(rightText, dstFormat, out dstParseError);
					}
					if (cts.Token.IsCancellationRequested)
					{
						return;
					}
					DiffSummary summary = DataDiff.Compute(left, right);
					Dispatcher.UIThread.Post(delegate
					{
						if (_generation != generation || _cts != cts)
						{
							return;
						}
						_left = left;
						_right = right;
						_summary = summary;
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
					PluginLog.Error("StructuredDiffView 渲染失败", ex);
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
				return roleText + "  ·  " + StructuredStrings.T("not present");
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
				_status.Text = StructuredStrings.T("File too large to preview");
				return;
			}
			string error = DisplayError();
			if (error != null)
			{
				_status.Text = StructuredStrings.F("Failed to parse: {0}", error);
				return;
			}
			if (_summary == null)
			{
				_status.Text = StructuredStrings.T("Analyzing…");
				return;
			}
			string head = StructuredStrings.F("Structured compare: {0} / {1}", _srcFormat, _dstFormat);
			if (!_summary.HasDifferences)
			{
				_status.Text = head + "  ·  " + StructuredStrings.T("No differences");
				return;
			}
			_status.Text = head + "  ·  " + StructuredStrings.F("Summary: {0} keys · {1} changed · {2} left only · {3} right only", _summary.Total, _summary.Changed, _summary.LeftOnly, _summary.RightOnly);
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
				_content.Content = NoteText(StructuredStrings.T("File too large to preview"));
				return;
			}
		if (_summary == null)
		{
			_content.Content = NoteText(DisplayError() == null ? StructuredStrings.T("Analyzing…") : StructuredStrings.F("Failed to parse: {0}", DisplayError()));
			return;
		}
			if (_mode == TreeMode)
			{
				_content.Content = BuildTreeContent();
			}
			else
			{
				_content.Content = BuildKeyPathContent();
			}
		}

		private Control BuildKeyPathContent()
		{
			Grid table = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("2*,2*,2*,Auto")
			};
			int row = 0;
			AddTableRow(table, ref row, StructuredStrings.T("Key path"), StructuredStrings.T("Old"), StructuredStrings.T("New"), StructuredStrings.T("State"), null, null, true);

			int shown = 0;
			foreach (DiffRow item in _summary.Rows)
			{
				if (shown >= MaxRows)
				{
					break;
				}
				shown++;
				AddTableRow(table, ref row, item.Path, Display(item.Left, item.LeftPresent), Display(item.Right, item.RightPresent), StateLabel(item.State), TintOf(item.State), StateBrush(item.State), false);
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
			if (_summary.Total > shown)
			{
				stack.Children.Add(NoteText(StructuredStrings.F("Showing first {0} of {1} rows", shown, _summary.Total)));
			}
			if (_srcAbsent || _dstAbsent)
			{
				stack.Children.Add(NoteText(StructuredStrings.T("One side is not present; values shown for the other side only.")));
			}
			return new ScrollViewer
			{
				Content = stack,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
			};
		}

		private void AddTableRow(Grid table, ref int row, string keyPath, string left, string right, string state, IBrush tint, IBrush stateBrush, bool header)
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
			AddCell(table, current, 0, keyPath, header, true, header ? null : Brushes.Gray);
			AddCell(table, current, 1, left, header, false, null);
			AddCell(table, current, 2, right, header, false, null);
			AddCell(table, current, 3, state, header, false, stateBrush);
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

		private Control BuildTreeContent()
		{
			Dictionary<string, DiffState> states = new Dictionary<string, DiffState>();
			foreach (DiffRow item in _summary.Rows)
			{
				if (!states.ContainsKey(item.Path))
				{
					states.Add(item.Path, item.State);
				}
			}
			StackPanel leftColumn = new StackPanel
			{
				Spacing = 0.0
			};
			StackPanel rightColumn = new StackPanel
			{
				Spacing = 0.0
			};
			_treeRows.Clear();
			TreeSide leftSide = new TreeSide
			{
				Panel = leftColumn,
				Budget = MaxTreeNodes
			};
			TreeSide rightSide = new TreeSide
			{
				Panel = rightColumn,
				Budget = MaxTreeNodes
			};
		// 占位三态：解析成功 → 折叠树；侧存在但解析失败 → 错误占位；侧缺失 → 「不存在」。
		if (_left != null)
		{
			AddChildRows(leftSide, _left, string.Empty, 0, states, null);
		}
		else if (_context?.Src != null)
		{
			leftColumn.Children.Add(NoteText(StructuredStrings.F("Failed to parse: {0}", _srcParseError ?? "?")));
		}
		else
		{
			leftColumn.Children.Add(NoteText(StructuredStrings.T("not present")));
		}
		if (_right != null)
		{
			AddChildRows(rightSide, _right, string.Empty, 0, states, null);
		}
		else if (_context?.Dst != null)
		{
			rightColumn.Children.Add(NoteText(StructuredStrings.F("Failed to parse: {0}", _dstParseError ?? "?")));
		}
		else
		{
			rightColumn.Children.Add(NoteText(StructuredStrings.T("not present")));
		}
			Grid panes = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,*")
			};
			Border leftCard = Card(leftColumn);
			Border rightCard = Card(rightColumn);
			Grid.SetColumn(leftCard, 0);
			Grid.SetColumn(rightCard, 1);
			panes.Children.Add(leftCard);
			panes.Children.Add(rightCard);
			return new ScrollViewer
			{
				Content = panes,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
			};
		}

		private static Border Card(Control content)
		{
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
					Content = content,
					HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
				}
			};
		}

		/// <summary>把 node 的**一层**子项各建一行（根层传 parentRow = null，直接挂侧栏面板）；
		/// 容器行的子级懒构建——首次展开才实化，超大文档只建可见部分。
		/// 命中变更的分支自动展开，顺着改动脉络一路亮到改动点。</summary>
		private void AddChildRows(TreeSide side, DataNode node, string path, int depth, Dictionary<string, DiffState> states, TreeRow parentRow)
		{
			if (node == null)
			{
				return;
			}
			Panel panel = ((parentRow != null && parentRow.Children != null) ? parentRow.Children : side.Panel);
			if (node.Kind == DataKind.Map)
			{
				foreach (DataEntry entry in node.Entries)
				{
					if (!TryBudgetTreeRow(side, panel))
					{
						return;
					}
					string childPath = string.IsNullOrEmpty(path) ? entry.Key : path + "." + entry.Key;
					TreeRow row = NewTreeRow(entry.Key, entry.Node, childPath, depth, states);
					AddTreeRow(side, panel, row, entry.Node, childPath, depth + 1, states);
				}
				return;
			}
			if (node.Kind == DataKind.Seq)
			{
				for (int index = 0; index < node.Items.Count; index++)
				{
					if (!TryBudgetTreeRow(side, panel))
					{
						return;
					}
					DataNode item = node.Items[index];
					string childPath = path + "[" + index + "]";
					TreeRow row = NewTreeRow("[" + index + "]", item, childPath, depth, states);
					AddTreeRow(side, panel, row, item, childPath, depth + 1, states);
				}
			}
		}

		private void AddTreeRow(TreeSide side, Panel panel, TreeRow row, DataNode childNode, string childPath, int childDepth, Dictionary<string, DiffState> states)
		{
			panel.Children.Add(row.RowBorder);
			// 子级容器必须与行本体同挂到面板（行下方、下一兄弟行之前），
			// 否则懒构建出的子行落在游离面板里——展开后什么都看不见
			if (row.Children != null)
			{
				panel.Children.Add(row.Children);
			}
			_treeRows.Add(row);
			if (row.Children != null)
			{
				row.BuildChildren = delegate
				{
					AddChildRows(side, childNode, childPath, childDepth, states, row);
				};
				if (row.State != DiffState.Same)
				{
					// 命中变更的分支自动展开（触发懒构建，改动脉络逐级亮下去）
					row.Chevron.IsChecked = true;
				}
			}
		}

		/// <summary>预算扣减；耗尽时在当前面板补一条截断提示（每侧至多一次）。</summary>
		private static bool TryBudgetTreeRow(TreeSide side, Panel panel)
		{
			if (side.Budget > 0)
			{
				side.Budget--;
				return true;
			}
			if (!side.NotedTruncation)
			{
				side.NotedTruncation = true;
				panel.Children.Add(NoteText(StructuredStrings.F("Node limit reached ({0}); not all nodes are shown.", MaxTreeNodes)));
			}
			return false;
		}

		/// <summary>建一行树节点：宿主 TreeViewControlItem 同款视觉——行高 20、10px/层缩进、
		/// 16×16 chevron（复用宿主 <c>ExpandCollapseToggleStyle</c> 主题）、主题前景色；
		/// 相同行悬停取宿主 <c>TreeViewItem.MouseOver.Background</c>，diff 四色行保持底色不悬停。
		/// 双击整行亦可展开 / 折叠。叶子行 chevron 隐藏但保留占位列，键名纵向对齐。</summary>
		private TreeRow NewTreeRow(string key, DataNode node, string path, int depth, Dictionary<string, DiffState> states)
		{
			DiffState state = ResolveState(path, states);
			string value;
			if (node.Kind == DataKind.Scalar)
			{
				value = " : " + Truncate(node.Value);
			}
			else if (node.Kind == DataKind.Seq)
			{
				value = " [" + node.Items.Count + "]";
			}
			else
			{
				value = " {" + node.Entries.Count + "}";
			}
			TextBlock keyBlock = new TextBlock
			{
				Text = key ?? string.Empty,
				FontFamily = MonoFont,
				FontWeight = FontWeight.SemiBold,
				TextTrimming = TextTrimming.CharacterEllipsis,
				VerticalAlignment = VerticalAlignment.Center
			};
			BindThemeBrush(keyBlock, TextBlock.ForegroundProperty, "ForegroundBrush", Brushes.Black);
			TextBlock valueBlock = new TextBlock
			{
				Text = value,
				FontFamily = MonoFont,
				Opacity = 0.75,
				TextTrimming = TextTrimming.CharacterEllipsis,
				Margin = new Thickness(4.0, 0.0, 0.0, 0.0),
				VerticalAlignment = VerticalAlignment.Center
			};
			BindThemeBrush(valueBlock, TextBlock.ForegroundProperty, "ForegroundBrush", Brushes.Black);
			StackPanel line = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(2.0, 0.0, 0.0, 0.0)
			};
			line.Children.Add(keyBlock);
			line.Children.Add(valueBlock);

			TreeRow row = new TreeRow
			{
				State = state
			};
			bool isContainer = node.Kind == DataKind.Map || node.Kind == DataKind.Seq;
			ToggleButton chevron = NewChevron();
			chevron.IsVisible = isContainer;
			chevron.IsCheckedChanged += delegate
			{
				ApplyExpanded(row);
			};
			row.Chevron = chevron;

			Grid rowGrid = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("16,*"),
				Margin = new Thickness(depth * 10.0, 0.0, 0.0, 0.0)
			};
			Grid.SetColumn(chevron, 0);
			Grid.SetColumn(line, 1);
			rowGrid.Children.Add(chevron);
			rowGrid.Children.Add(line);

			Border border = new Border
			{
				MinHeight = 20.0,
				Padding = new Thickness(0.0, 1.0, 4.0, 1.0),
				CornerRadius = new CornerRadius(3.0),
				Background = TintOf(state),
				Child = rowGrid
			};
			if (state == DiffState.Same)
			{
				border.PointerEntered += delegate
				{
					IBrush hover = FindThemeBrush(border, "TreeViewItem.MouseOver.Background");
					if (hover != null)
					{
						border.Background = hover;
					}
				};
				border.PointerExited += delegate
				{
					border.Background = null;
				};
			}
			border.DoubleTapped += delegate
			{
				if (row.Chevron != null)
				{
					row.Chevron.IsChecked = !(row.Chevron.IsChecked == true);
				}
			};
			row.RowBorder = border;
			if (isContainer)
			{
				row.Children = new StackPanel
				{
					IsVisible = false
				};
			}
			return row;
		}

		/// <summary>宿主同款 chevron：直接复用宿主 <c>ExpandCollapseToggleStyle</c> ControlTheme
		///（16×16、右向箭头，选中时下指——几何切换由该主题的 :checked 样式完成）。
		/// 资源不可达时（无宿主样式的测试环境）退回默认 ToggleButton 外观，功能不受影响。</summary>
		private static ToggleButton NewChevron()
		{
			ToggleButton toggle = new ToggleButton
			{
				Width = 16.0,
				Height = 16.0,
				Focusable = false,
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Center
			};
			toggle.Bind(TemplatedControl.ThemeProperty, new CastSource<ControlTheme>(toggle.GetResourceObservable("ExpandCollapseToggleStyle")));
			return toggle;
		}

		/// <summary>按 chevron 勾选态切换子级：首次展开才实化（懒构建），折叠只藏不销毁。</summary>
		private static void ApplyExpanded(TreeRow row)
		{
			if (row.Chevron == null || row.Children == null)
			{
				return;
			}
			bool expanded = row.Chevron.IsChecked == true;
			if (expanded && !row.Built)
			{
				row.Built = true;
				row.BuildChildren?.Invoke();
			}
			row.Children.IsVisible = expanded;
		}

		/// <summary>全部展开。懒展开会向 _treeRows 追加新行，必须用下标 for 遍历。</summary>
		private void ExpandAllTreeRows()
		{
			for (int i = 0; i < _treeRows.Count; i++)
			{
				TreeRow row = _treeRows[i];
				if (row.Chevron != null)
				{
					row.Chevron.IsChecked = true;
				}
			}
		}

		private void CollapseAllTreeRows()
		{
			for (int i = _treeRows.Count - 1; i >= 0; i--)
			{
				TreeRow row = _treeRows[i];
				if (row.Chevron != null)
				{
					row.Chevron.IsChecked = false;
				}
			}
		}

		/// <summary>把控件属性绑到宿主应用级主题资源（插件与宿主同进程，DynamicResource 语义可达）；
		/// 资源缺失时落到 fallback（无宿主样式的测试环境）。</summary>
		private static void BindThemeBrush(StyledElement element, AvaloniaProperty property, string resourceKey, IBrush fallback)
		{
			element.Bind(property, new CastSource<IBrush>(element.GetResourceObservable(resourceKey), fallback));
		}

		/// <summary>事件时点取宿主主题资源（悬停高亮这类一次性取用，不做常驻绑定）。</summary>
		private static IBrush FindThemeBrush(StyledElement element, string resourceKey)
		{
			if (element.TryFindResource(resourceKey, element.ActualThemeVariant, out object value))
			{
				return value as IBrush;
			}
			return null;
		}

		/// <summary>IObservable&lt;object&gt;（宿主资源，可能为 null）→ 类型化取值的小适配器；
		/// Avalonia 的响应式链没有 LINQ 组合子，这里手写 cast + fallback。</summary>
		private sealed class CastSource<T> : IObservable<T> where T : class
		{
			private readonly IObservable<object> _source;

			private readonly T _fallback;

			public CastSource(IObservable<object> source, T fallback = null)
			{
				_source = source;
				_fallback = fallback;
			}

			public IDisposable Subscribe(IObserver<T> observer)
			{
				return _source.Subscribe(new CastObserver<T>(observer, _fallback));
			}
		}

		private sealed class CastObserver<T> : IObserver<object> where T : class
		{
			private readonly IObserver<T> _target;

			private readonly T _fallback;

			public CastObserver(IObserver<T> target, T fallback)
			{
				_target = target;
				_fallback = fallback;
			}

			void IObserver<object>.OnNext(object value)
			{
				_target.OnNext((value as T) ?? _fallback);
			}

			void IObserver<object>.OnError(Exception error)
			{
				_target.OnError(error);
			}

			void IObserver<object>.OnCompleted()
			{
				_target.OnCompleted();
			}
		}

		/// <summary>树模式的一行：行控件 + chevron + 懒构建的子级容器（叶子行 Children 为 null）。</summary>
		private sealed class TreeRow
		{
			public Border RowBorder;

			public ToggleButton Chevron;

			public Panel Children;

			public Action BuildChildren;

			public bool Built;

			public DiffState State;
		}

		/// <summary>一侧的构建上下文：容器面板 + 节点预算（懒展开持续扣减）。</summary>
		private sealed class TreeSide
		{
			public Panel Panel;

			public int Budget;

			public bool NotedTruncation;
		}

		/// <summary>取该路径的状态；容器节点只要任一后代有变更就按变更显示。</summary>
		private static DiffState ResolveState(string path, Dictionary<string, DiffState> states)
		{
			if (states.TryGetValue(path, out DiffState direct))
			{
				return direct;
			}
			string prefix = path + ".";
			string indexPrefix = path + "[";
			foreach (KeyValuePair<string, DiffState> pair in states)
			{
				if (pair.Value == DiffState.Same)
				{
					continue;
				}
				if (pair.Key.StartsWith(prefix, StringComparison.Ordinal) || pair.Key.StartsWith(indexPrefix, StringComparison.Ordinal))
				{
					return pair.Value;
				}
			}
			return DiffState.Same;
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
				return StructuredStrings.T("changed");
			case DiffState.LeftOnly:
				return StructuredStrings.T("left only");
			case DiffState.RightOnly:
				return StructuredStrings.T("right only");
			default:
				return StructuredStrings.T("same");
			}
		}

		private static string Display(string value, bool present)
		{
			if (!present)
			{
				return StructuredStrings.T("not present");
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
