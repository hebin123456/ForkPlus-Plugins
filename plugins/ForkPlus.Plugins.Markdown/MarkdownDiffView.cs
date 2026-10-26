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
using ForkPlus.Plugins.Ui.HtmlPreview;

namespace ForkPlus.Plugins.Markdown
{
	/// <summary>
	/// Markdown 对比视图：把旧（左）/ 新（右）两侧的 .md / .markdown 文档按三种口径对比。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、
	/// 自带的分段模式工具条（渲染 / 大纲 / 源码），最下是内容区。
	///
	/// 三个模式：
	/// <list type="bullet">
	/// <item>渲染（默认）：并排双 WebView 预览两侧渲好的样式化 HTML——<see cref="MarkdownHtmlComposer"/>
	/// 用 Markdig 把 Markdown 转成自包含页面（明暗主题跟随系统），交给共享的
	/// <c>ForkPlus.Plugins.Ui.HtmlPreview.SideBySideHtmlPreview</c> 渲染。WebView 不可用
	/// （headless、缺 WebView2 运行时 / WPE WebKit / WebKitGTK）时仅给提示，可切「大纲」/「源码」。</item>
	/// <item>大纲：标题层级语义 diff——「标题 | 旧 | 新 | 状态」四列表，按标题文本对齐，
	/// 逐行四色标注（相同 / 层级已变 / 仅左 / 仅右），一眼看出章节增删与升降级。</item>
	/// <item>源码：左右两栏并排看两侧原始文本（等宽、只读、可选中，带行号列），是前两个模式的兜底。</item>
	/// </list>
	///
	/// 已知限制（如实标注）：插件只拿到仓库相对路径，拿不到工作区绝对路径，文档里以相对路径引用的
	/// **外部**图片无法解析；「渲染」模式的并排预览无跨侧滚动同步（共享组件未提供脚本桥）。
	///
	/// 读取、解析与组装在后台线程执行（Markdig 解析一次，渲染 HTML 与标题大纲两用），
	/// 控件只在 UI 线程构建；每次刷新用代次 + CancellationToken 取消上一轮。
	/// 单侧声明大小 &gt; 300 MB 只给提示、不解析。
	/// </summary>
	public sealed class MarkdownDiffView : IDiffView
	{
		private const string RenderMode = "render";

		private const string OutlineMode = "outline";

		private const string SourceMode = "source";

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （outline / source，其余走默认 render），据此逐模式取图；正常运行时该变量为空，走默认渲染模式。
		/// </summary>
		private static readonly string InitialMode = ResolveInitialMode();

		private static string ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			if (string.Equals(forced, OutlineMode, StringComparison.OrdinalIgnoreCase))
			{
				return OutlineMode;
			}
			if (string.Equals(forced, SourceMode, StringComparison.OrdinalIgnoreCase))
			{
				return SourceMode;
			}
			return RenderMode;
		}

		/// <summary>单侧超过此大小不解析（与 CHM / EPUB / 前端插件同口径）。</summary>
		private const long MaxSideBytes = 300L * 1024L * 1024L;

		/// <summary>大纲表渲染上限：超大文档（标题 &gt; 500）截断并在表下注明。</summary>
		private const int MaxRows = 500;

		/// <summary>「源码」模式单侧展示的字符上限，超出截断并在栏下注明（防超长文件拖死 UI）。</summary>
		private const int MaxSourceChars = 200_000;

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

		private readonly Button _renderButton;

		private readonly Button _outlineButton;

		private readonly Button _sourceButton;

		private readonly ContentControl _content;

		/// <summary>「渲染」模式的并排双 WebView 控件（懒建、复用；WebView 不可用时显示提示占位）。</summary>
		private SideBySideHtmlPreview _preview;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _generation;

		private string _mode = InitialMode;

		/// <summary>两侧原始 Markdown 文本（后台解码；用于「源码」模式与整体差异判定）。</summary>
		private string _leftText;

		private string _rightText;

		/// <summary>「渲染」模式两侧的自包含 HTML（后台组装；该侧不存在 / 无内容为 null）。</summary>
		private string _leftHtml;

		private string _rightHtml;

		/// <summary>两侧标题大纲与语义 diff 结果（后台解析）。</summary>
		private List<MarkdownHeading> _leftHeadings;

		private List<MarkdownHeading> _rightHeadings;

		private MarkdownOutlineDiff _outlineDiff;

		private string _error;

		private bool _srcAbsent;

		private bool _dstAbsent;

		private bool _tooLarge;

		private bool _analyzed;

		private bool _changed;

		public MarkdownDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			_status = new TextBlock
			{
				Margin = new Thickness(12.0, 2.0, 12.0, 6.0),
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0
			};
			_renderButton = NewModeButton(MarkdownStrings.T("Render"), delegate
			{
				SetMode(RenderMode);
			});
			_outlineButton = NewModeButton(MarkdownStrings.T("Outline"), delegate
			{
				SetMode(OutlineMode);
			});
			_sourceButton = NewModeButton(MarkdownStrings.T("Source"), delegate
			{
				SetMode(SourceMode);
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
			modes.Children.Add(_renderButton);
			modes.Children.Add(_outlineButton);
			modes.Children.Add(_sourceButton);

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

			StyleModeButton(_renderButton, _mode == RenderMode);
			StyleModeButton(_outlineButton, _mode == OutlineMode);
			StyleModeButton(_sourceButton, _mode == SourceMode);
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
			PluginLog.Info($"MarkdownDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			Render();
		}

		public void SetMode(string modeId)
		{
			if (string.IsNullOrEmpty(modeId))
			{
				return;
			}
			if (modeId != RenderMode && modeId != OutlineMode && modeId != SourceMode)
			{
				return;
			}
			if (modeId == _mode)
			{
				return;
			}
			_mode = modeId;
			StyleModeButton(_renderButton, _mode == RenderMode);
			StyleModeButton(_outlineButton, _mode == OutlineMode);
			StyleModeButton(_sourceButton, _mode == SourceMode);
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
			_renderButton.Content = MarkdownStrings.T("Render");
			_outlineButton.Content = MarkdownStrings.T("Outline");
			_sourceButton.Content = MarkdownStrings.T("Source");
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
			_leftText = null;
			_rightText = null;
			_leftHtml = null;
			_rightHtml = null;
			_leftHeadings = null;
			_rightHeadings = null;
			_outlineDiff = null;
			_content.Content = null;
			_status.Text = string.Empty;
		}

		// ---- 渲染 ----

		private void Render()
		{
			CancelRender();
			_leftText = null;
			_rightText = null;
			_leftHtml = null;
			_rightHtml = null;
			_leftHeadings = null;
			_rightHeadings = null;
			_outlineDiff = null;
			_error = null;
			_tooLarge = false;
			_analyzed = false;
			_changed = false;
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
				string leftText = null;
				string rightText = null;
				string leftHtml = null;
				string rightHtml = null;
				List<MarkdownHeading> leftHeadings = null;
				List<MarkdownHeading> rightHeadings = null;
				bool changed = false;
				try
				{
					leftText = ReadText(src, hexSrc, cts.Token);
					rightText = ReadText(dst, hexDst, cts.Token);
					cts.Token.ThrowIfCancellationRequested();
					// 一份解析两用：渲染 HTML（「渲染」模式）与标题大纲（「大纲」模式）共用同一 Markdig 管线。
					leftHtml = MarkdownHtmlComposer.Compose(leftText);
					rightHtml = MarkdownHtmlComposer.Compose(rightText);
					leftHeadings = MarkdownOutline.Extract(leftText);
					rightHeadings = MarkdownOutline.Extract(rightText);
					changed = !string.Equals(leftText, rightText, StringComparison.Ordinal);
					MarkdownOutlineDiff outlineDiff = MarkdownOutline.Compute(leftHeadings, rightHeadings);
					Dispatcher.UIThread.Post(delegate
					{
						if (_generation != generation || _cts != cts)
						{
							return;
						}
						_leftText = leftText;
						_rightText = rightText;
						_leftHtml = leftHtml;
						_rightHtml = rightHtml;
						_leftHeadings = leftHeadings;
						_rightHeadings = rightHeadings;
						_outlineDiff = outlineDiff;
						_analyzed = true;
						_changed = changed;
						UpdateStatus();
						BuildContent();
					});
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex)
				{
					PluginLog.Error("MarkdownDiffView 渲染失败", ex);
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
		/// 取一侧文本：宿主 Hex 预载 &gt; 侧内容懒加载 &gt; LFS 本地缓存；侧不存在返回 null。
		/// 保存 / 恢复流 Position，不动宿主的流。
		/// </summary>
		private string ReadText(DiffSideContent side, MemoryStream preloaded, CancellationToken token)
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
				return string.Empty;
			}
			token.ThrowIfCancellationRequested();
			long position = data.Position;
			try
			{
				data.Position = 0L;
				using (StreamReader reader = new StreamReader(data, Encoding.UTF8, true, 4096, true))
				{
					return reader.ReadToEnd();
				}
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
				return roleText + "  ·  " + MarkdownStrings.T("not present");
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
				_status.Text = MarkdownStrings.T("File too large to render");
				return;
			}
			if (_error != null)
			{
				_status.Text = MarkdownStrings.F("Failed to read: {0}", _error);
				return;
			}
			if (!_analyzed)
			{
				_status.Text = MarkdownStrings.T("Analyzing…");
				return;
			}
			string head = MarkdownStrings.F("Markdown compare: {0} / {1}", DisplayTitle(_context?.Src), DisplayTitle(_context?.Dst));
			head = head + "  ·  " + MarkdownStrings.F("headings: {0} / {1}", _leftHeadings?.Count ?? 0, _rightHeadings?.Count ?? 0);
			if (_srcAbsent || _dstAbsent)
			{
				_status.Text = head + "  ·  " + MarkdownStrings.T("One side is not present; only the other side is shown.");
				return;
			}
			if (_outlineDiff != null && _outlineDiff.HasDifferences)
			{
				_status.Text = head + "  ·  " + MarkdownStrings.F("Outline: {0} changed · {1} left only · {2} right only", _outlineDiff.Changed, _outlineDiff.LeftOnly, _outlineDiff.RightOnly)
					+ "  ·  " + MarkdownStrings.T(_changed ? "Changed" : "No differences");
				return;
			}
			_status.Text = head + "  ·  " + MarkdownStrings.T(_changed ? "Changed" : "No differences");
		}

		/// <summary>状态行标题：文件名 &gt; not present（侧不存在）。</summary>
		private static string DisplayTitle(DiffSideContent side)
		{
			if (side != null)
			{
				string name = Path.GetFileName(side.Path);
				if (!string.IsNullOrEmpty(name))
				{
					return name;
				}
			}
			return MarkdownStrings.T("not present");
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
				_content.Content = NoteText(MarkdownStrings.T("File too large to render"));
				return;
			}
			if (!_analyzed)
			{
				_content.Content = NoteText(_error == null ? MarkdownStrings.T("Analyzing…") : MarkdownStrings.F("Failed to read: {0}", _error));
				return;
			}
			if (_mode == OutlineMode)
			{
				_content.Content = BuildOutlineContent();
			}
			else if (_mode == SourceMode)
			{
				_content.Content = BuildSourceContent();
			}
			else
			{
				BuildRenderContent();
			}
		}

		/// <summary>
		/// 「渲染」模式：并排双 WebView 预览两侧样式化 HTML（后台组装好的自包含页面）。
		/// 某侧不存在 / 无内容 → 该侧占位；WebView 不可用（headless、缺运行时）→ 两侧仅给提示文案（降级，不报错）。
		/// </summary>
		private void BuildRenderContent()
		{
			Grid grid = new Grid
			{
				RowDefinitions = new RowDefinitions("Auto,*")
			};
			TextBlock note = NoteText(MarkdownStrings.T("Relative image references cannot be resolved and may be missing."));
			Grid.SetRow(note, 0);
			grid.Children.Add(note);

			if (_preview == null)
			{
				// 宿主把插件视图挂进「提交页」的 ItemsControl → Expander → ScrollViewer 链
				// （见 RevisionSummaryUserControl.axaml），纵向测量是无界的：星行按 Auto 解析，
				// 原生 WebView 的自然高度近 0，会被折叠成 1px 导致预览空白。给预览控件一个最小
				// 高度，让它在无界测量下仍拿到确定高度；宿主给出确定高度时星行照常填满，不受影响。
				_preview = new SideBySideHtmlPreview
				{
					MinHeight = 420.0
				};
			}
			// 占位 / 降级文案随界面语言（shared 组件默认中文，这里按插件译文覆盖）。
			_preview.AbsentText = MarkdownStrings.T("not present");
			_preview.UnavailableText = MarkdownStrings.T("Preview not available in this environment");
			_preview.SetContent(
				DisplayTitle(_context?.Src),
				_leftHtml,
				DisplayTitle(_context?.Dst),
				_rightHtml);
			Grid.SetRow(_preview, 1);
			grid.Children.Add(_preview);
			_content.Content = grid;
		}

		/// <summary>大纲表：「标题 | 旧 | 新 | 状态」四列，按标题文本对齐；层级 = H1-H6，四色标注底色。</summary>
		private Control BuildOutlineContent()
		{
			MarkdownOutlineDiff diff = _outlineDiff;
			if (diff == null)
			{
				return NoteText(MarkdownStrings.T("Analyzing…"));
			}
			Grid table = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("3*,1*,1*,Auto")
			};
			int row = 0;
			AddTableRow(table, ref row, MarkdownStrings.T("Heading"), MarkdownStrings.T("Old"), MarkdownStrings.T("New"), MarkdownStrings.T("State"), null, null, true);

			int shown = 0;
			foreach (MarkdownOutlineRow item in diff.Rows)
			{
				if (shown >= MaxRows)
				{
					break;
				}
				shown++;
				AddTableRow(table, ref row, item.Text, DescribeLevel(item.LeftPresent, item.LeftLevel), DescribeLevel(item.RightPresent, item.RightLevel), StateLabel(item.State), TintOf(item.State), StateBrush(item.State), false);
			}

			StackPanel stack = new StackPanel
			{
				Spacing = 6.0
			};
			stack.Children.Add(WrapInCard(table));
			if (diff.Total > shown)
			{
				stack.Children.Add(NoteText(MarkdownStrings.F("Showing first {0} of {1} rows", shown, diff.Total)));
			}
			if (_srcAbsent || _dstAbsent)
			{
				stack.Children.Add(NoteText(MarkdownStrings.T("One side is not present; values shown for the other side only.")));
			}
			return new ScrollViewer
			{
				Content = stack,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
			};
		}

		/// <summary>「源码」模式：左右两栏并排看两侧原始文本（等宽、只读、可选中）。</summary>
		private Control BuildSourceContent()
		{
			Grid panes = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,*")
			};
			Border leftCard = SourceCard(DisplayTitle(_context?.Src), _leftText, _srcAbsent);
			Border rightCard = SourceCard(DisplayTitle(_context?.Dst), _rightText, _dstAbsent);
			Grid.SetColumn(leftCard, 0);
			Grid.SetColumn(rightCard, 1);
			panes.Children.Add(leftCard);
			panes.Children.Add(rightCard);
			return panes;
		}

		private Border SourceCard(string title, string text, bool absent)
		{
			// 行 1 预留给截断注明（无则空行高为 0），行 2 是可滚动的「行号列 + 代码列」。
			// 注意不能把 ScrollViewer 塞进 StackPanel：StackPanel 纵向给子控件无限高度，
			// ScrollViewer 视口随之无限大，竖向永远不会滚动（此前「无法滚动」的根源）。
			Grid card = new Grid
			{
				RowDefinitions = new RowDefinitions("Auto,Auto,*")
			};
			TextBlock header = new TextBlock
			{
				Text = string.IsNullOrEmpty(title) ? string.Empty : title,
				Margin = new Thickness(8.0, 6.0, 8.0, 6.0),
				FontWeight = FontWeight.SemiBold,
				TextTrimming = TextTrimming.CharacterEllipsis
			};
			Grid.SetRow(header, 0);
			card.Children.Add(header);

			if (absent)
			{
				TextBlock placeholder = new TextBlock
				{
					Text = MarkdownStrings.T("not present"),
					Margin = new Thickness(12.0),
					TextWrapping = TextWrapping.Wrap,
					Opacity = 0.7
				};
				Grid.SetRow(placeholder, 1);
				card.Children.Add(placeholder);
			}
			else
			{
				// 展示前把 CRLF / 孤立 CR 归一成 LF：行号列与代码列都按 '\n' 切行，逐行严格对齐。
				string shown = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
				if (shown.Length > MaxSourceChars)
				{
					TextBlock note = NoteText(MarkdownStrings.F("Showing first {0} of {1} characters", MaxSourceChars, shown.Length));
					Grid.SetRow(note, 1);
					card.Children.Add(note);
					shown = shown.Substring(0, MaxSourceChars);
				}

				// 行号列：与代码同字体同字号右对齐；隐藏滚动条、关闭命中测试，自身不可独立滚动，
				// 只随代码列的垂直偏移同步（代码横向滚动时行号保持可见）。
				ScrollViewer gutterHost = new ScrollViewer
				{
					Content = new TextBlock
					{
						Text = LineNumbers(shown),
						Margin = new Thickness(0.0, 4.0, 8.0, 4.0),
						FontFamily = MonoFont,
						FontSize = 12.0,
						TextWrapping = TextWrapping.NoWrap,
						TextAlignment = TextAlignment.Right,
						Opacity = 0.45
					},
					VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
					HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
					IsHitTestVisible = false
				};
				Grid.SetColumn(gutterHost, 0);

				SelectableTextBlock code = new SelectableTextBlock
				{
					Text = shown,
					Margin = new Thickness(4.0),
					FontFamily = MonoFont,
					FontSize = 12.0,
					TextWrapping = TextWrapping.NoWrap
				};
				ScrollViewer codeHost = new ScrollViewer
				{
					Content = code,
					HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
					VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
				};
				codeHost.ScrollChanged += delegate
				{
					// 单向同步、无回路：行号列不可交互，只跟随代码列的纵向偏移。
					if (Math.Abs(gutterHost.Offset.Y - codeHost.Offset.Y) > 0.5)
					{
						gutterHost.Offset = gutterHost.Offset.WithY(codeHost.Offset.Y);
					}
				};
				Grid.SetColumn(codeHost, 1);

				Grid body = new Grid
				{
					ColumnDefinitions = new ColumnDefinitions("Auto,*")
				};
				body.Children.Add(gutterHost);
				body.Children.Add(codeHost);
				Grid.SetRow(body, 2);
				card.Children.Add(body);
			}
			return new Border
			{
				Margin = new Thickness(0.0, 0.0, 6.0, 0.0),
				BorderBrush = Brushes.Gainsboro,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(6.0),
				Background = new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x80, 0x80)),
				Child = card
			};
		}

		// ---- 大纲表小部件 ----

		private static IBrush TintOf(MarkdownOutlineState state)
		{
			switch (state)
			{
			case MarkdownOutlineState.Changed:
				return TintChanged;
			case MarkdownOutlineState.LeftOnly:
				return TintLeftOnly;
			case MarkdownOutlineState.RightOnly:
				return TintRightOnly;
			default:
				return null;
			}
		}

		private static IBrush StateBrush(MarkdownOutlineState state)
		{
			switch (state)
			{
			case MarkdownOutlineState.Changed:
				return StateChanged;
			case MarkdownOutlineState.LeftOnly:
				return StateLeft;
			case MarkdownOutlineState.RightOnly:
				return StateRight;
			default:
				return Brushes.Gray;
			}
		}

		private static string StateLabel(MarkdownOutlineState state)
		{
			switch (state)
			{
			case MarkdownOutlineState.Changed:
				return MarkdownStrings.T("changed");
			case MarkdownOutlineState.LeftOnly:
				return MarkdownStrings.T("left only");
			case MarkdownOutlineState.RightOnly:
				return MarkdownStrings.T("right only");
			default:
				return MarkdownStrings.T("same");
			}
		}

		/// <summary>旧 / 新层级单元格：H1-H6；无此侧为 not present。</summary>
		private static string DescribeLevel(bool present, int level)
		{
			return present ? ("H" + level) : MarkdownStrings.T("not present");
		}

		/// <summary>四列表加一行：可带四色底（Border 跨四列）+ 状态列彩色；首列为标题文本（换行展示）。</summary>
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

		/// <summary>生成「源码」模式的行号列：1..N 逐行编号，换行结构与展示文本（按 '\n' 切行）严格一致，
		/// 保证行号列与代码列在两种「行尾换行是否成行」的排版实现下行数都相同、逐行对齐。</summary>
		private static string LineNumbers(string shown)
		{
			if (string.IsNullOrEmpty(shown))
			{
				return string.Empty;
			}
			int lines = 1;
			for (int i = 0; i < shown.Length; i++)
			{
				if (shown[i] == '\n')
				{
					lines++;
				}
			}
			StringBuilder gutter = new StringBuilder();
			for (int i = 1; i <= lines; i++)
			{
				if (i > 1)
				{
					gutter.Append('\n');
				}
				gutter.Append(i);
			}
			if (shown[shown.Length - 1] == '\n')
			{
				gutter.Append('\n');
			}
			return gutter.ToString();
		}
	}
}
