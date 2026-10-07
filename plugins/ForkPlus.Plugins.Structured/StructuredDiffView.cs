using System;
using System.Collections.Generic;
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
	/// <item>结构树：左右两栏按原文档结构缩进展开，逐节点按同一套四色标注，
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

		/// <summary>渲染上限：避免超大文件把 UI 拖死。</summary>
		private const int MaxRows = 4000;

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

		private string _srcFormat = "?";

		private string _dstFormat = "?";

		private bool _srcAbsent;

		private bool _dstAbsent;

		private bool _tooLarge;

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
				string error = null;
				try
				{
					string leftText = ReadText(src, hexSrc, cts.Token);
					string rightText = ReadText(dst, hexDst, cts.Token);
					cts.Token.ThrowIfCancellationRequested();
					if (src != null)
					{
						left = StructuredParser.Parse(leftText, srcFormat, out string leftError);
						error = leftError;
					}
					if (dst != null)
					{
						string rightError;
						right = StructuredParser.Parse(rightText, dstFormat, out rightError);
						error = error ?? rightError;
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

		private void UpdateStatus()
		{
			if (_tooLarge)
			{
				_status.Text = StructuredStrings.T("File too large to preview");
				return;
			}
			if (_error != null)
			{
				_status.Text = StructuredStrings.F("Failed to parse: {0}", _error);
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
				_content.Content = NoteText(_error == null ? StructuredStrings.T("Analyzing…") : StructuredStrings.F("Failed to parse: {0}", _error));
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
			int budget = MaxTreeNodes;
			if (_left != null)
			{
				AddTreeNodes(leftColumn, _left, string.Empty, string.Empty, 0, true, states, ref budget);
			}
			else
			{
				leftColumn.Children.Add(NoteText(StructuredStrings.T("not present")));
			}
			if (_right != null)
			{
				AddTreeNodes(rightColumn, _right, string.Empty, string.Empty, 0, false, states, ref budget);
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

		private void AddTreeNodes(Panel panel, DataNode node, string key, string path, int depth, bool isLeft, Dictionary<string, DiffState> states, ref int budget)
		{
			if (node == null)
			{
				return;
			}
			if (node.Kind == DataKind.Map)
			{
				foreach (DataEntry entry in node.Entries)
				{
					if (budget <= 0)
					{
						return;
					}
					budget--;
					string childPath = string.IsNullOrEmpty(path) ? entry.Key : path + "." + entry.Key;
					panel.Children.Add(BuildTreeRow(entry.Key, entry.Node, childPath, depth, states));
					if (entry.Node.Kind == DataKind.Map || entry.Node.Kind == DataKind.Seq)
					{
						AddTreeNodes(panel, entry.Node, entry.Key, childPath, depth + 1, isLeft, states, ref budget);
					}
				}
				return;
			}
			if (node.Kind == DataKind.Seq)
			{
				for (int index = 0; index < node.Items.Count; index++)
				{
					if (budget <= 0)
					{
						return;
					}
					budget--;
					DataNode item = node.Items[index];
					string childPath = path + "[" + index + "]";
					panel.Children.Add(BuildTreeRow("[" + index + "]", item, childPath, depth, states));
					if (item.Kind == DataKind.Map || item.Kind == DataKind.Seq)
					{
						AddTreeNodes(panel, item, null, childPath, depth + 1, isLeft, states, ref budget);
					}
				}
			}
		}

		private Control BuildTreeRow(string key, DataNode node, string path, int depth, Dictionary<string, DiffState> states)
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
			StackPanel line = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Margin = new Thickness(depth * 14.0, 1.0, 0.0, 1.0)
			};
			if (key != null)
			{
				line.Children.Add(new TextBlock
				{
					Text = key,
					FontFamily = MonoFont,
					FontWeight = FontWeight.SemiBold,
					Foreground = Brushes.Black
				});
			}
			line.Children.Add(new TextBlock
			{
				Text = value,
				FontFamily = MonoFont,
				Foreground = Brushes.Black
			});
			Border row = new Border
			{
				Background = TintOf(state),
				CornerRadius = new CornerRadius(3.0),
				Padding = new Thickness(4.0, 1.0, 4.0, 1.0),
				Child = line
			};
			return row;
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
