using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ForkPlus.Plugins.Abstractions;
using ForkPlus.Plugins.Ui.HtmlPreview;

namespace ForkPlus.Plugins.Frontend
{
	/// <summary>
	/// 前端代码对比视图：把旧（左）/ 新（右）两侧的 .html / .htm / .vue 源码组装成**自包含 HTML**，
	/// 提供「渲染」与「源码」两种口径。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、
	/// 自带的分段模式工具条（渲染 / 源码），最下是内容区。
	///
	/// 两个模式：
	/// <list type="bullet">
	/// <item>渲染（默认）：并排双 WebView 真渲染两侧页面——.html / .htm 归一 charset 后原样呈现，
	/// .vue 交给 <see cref="VueComponentCompiler"/> 用随包内嵌的 Vue 3 运行时做运行期模板编译再
	/// 组装成自包含页面；两侧交给共享的
	/// <c>ForkPlus.Plugins.Ui.HtmlPreview.SideBySideHtmlPreview</c> 渲染。WebView 不可用
	/// （headless、缺 WebView2 运行时 / WPE WebKit / WebKitGTK）时仅给提示，可切「源码」模式。</item>
	/// <item>源码：左右两栏并排看两侧原始代码（等宽、只读、可选中，带行号列），是「渲染」看不出差异时的兜底。</item>
	/// </list>
	///
	/// 已知限制（如实标注）：插件只拿到仓库相对路径，拿不到工作区绝对路径，因此页面里以相对路径
	/// 引用的**外部** css / js / 图片无法解析，只有自包含页面能完整渲染——「渲染」模式会在内容区顶部注明。
	///
	/// 读取与组装在后台线程执行，控件只在 UI 线程构建；每次刷新用代次 + CancellationToken 取消上一轮。
	/// 单侧声明大小 &gt; 300 MB 只给提示、不组装。
	/// </summary>
	public sealed class FrontendDiffView : IDiffView
	{
		private const string RenderMode = "render";

		private const string SourceMode = "source";

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （source，其余走默认 render），据此逐模式取图；正常运行时该变量为空，走默认渲染模式。
		/// </summary>
		private static readonly string InitialMode = ResolveInitialMode();

		private static string ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			return string.Equals(forced, SourceMode, StringComparison.OrdinalIgnoreCase) ? SourceMode : RenderMode;
		}

		/// <summary>单侧超过此大小不处理（与 EPUB / CHM / 字幕插件同口径）。</summary>
		private const long MaxSideBytes = 300L * 1024L * 1024L;

		/// <summary>「源码」模式单侧展示的字符上限，超出截断并在栏下注明（防超长文件拖死 UI）。</summary>
		private const int MaxSourceChars = 200_000;

		/// <summary>已有 charset 声明（用于归一为 UTF-8）。</summary>
		private static readonly Regex CharsetMetaRegex = new Regex(
			"<meta\\b[^>]*\\bcharset\\s*=\\s*['\"]?[^'\"\\s/>]+['\"]?[^>]*>",
			RegexOptions.IgnoreCase | RegexOptions.Compiled);

		/// <summary>&lt;head&gt; 开标签（用于在缺失 charset 时插入声明）。</summary>
		private static readonly Regex HeadOpenRegex = new Regex(
			"<head\\b[^>]*>",
			RegexOptions.IgnoreCase | RegexOptions.Compiled);

		private const string Utf8CharsetMeta = "<meta charset=\"utf-8\">";

		private static readonly FontFamily MonoFont = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, Courier New, monospace");

		private readonly Grid _root;

		private readonly TextBlock _srcTitle;

		private readonly TextBlock _dstTitle;

		private readonly TextBlock _status;

		private readonly Button _renderButton;

		private readonly Button _sourceButton;

		private readonly ContentControl _content;

		/// <summary>「渲染」模式的并排双 WebView 控件（懒建、复用；WebView 不可用时显示提示占位）。</summary>
		private SideBySideHtmlPreview _preview;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _generation;

		private string _mode = InitialMode;

		/// <summary>两侧原始源码（后台解码；用于「源码」模式与差异判定）。</summary>
		private string _leftText;

		private string _rightText;

		/// <summary>「渲染」模式两侧的自包含 HTML（后台组装；该侧不存在 / 无内容为 null）。</summary>
		private string _leftHtml;

		private string _rightHtml;

		private string _error;

		private bool _srcAbsent;

		private bool _dstAbsent;

		private bool _tooLarge;

		private bool _analyzed;

		private bool _changed;

		public FrontendDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			_status = new TextBlock
			{
				Margin = new Thickness(12.0, 2.0, 12.0, 6.0),
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0
			};
			_renderButton = NewModeButton(FrontendStrings.T("Render"), delegate
			{
				SetMode(RenderMode);
			});
			_sourceButton = NewModeButton(FrontendStrings.T("Source"), delegate
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
			PluginLog.Info($"FrontendDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			Render();
		}

		public void SetMode(string modeId)
		{
			if (string.IsNullOrEmpty(modeId))
			{
				return;
			}
			if (modeId != RenderMode && modeId != SourceMode)
			{
				return;
			}
			if (modeId == _mode)
			{
				return;
			}
			_mode = modeId;
			StyleModeButton(_renderButton, _mode == RenderMode);
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
			_renderButton.Content = FrontendStrings.T("Render");
			_sourceButton.Content = FrontendStrings.T("Source");
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
				bool changed = false;
				try
				{
					leftText = ReadText(src, hexSrc, cts.Token);
					rightText = ReadText(dst, hexDst, cts.Token);
					cts.Token.ThrowIfCancellationRequested();
					// 「渲染」模式：在后台把两侧源码组装成自包含 HTML（.html/.htm 归一 charset；.vue 运行期编译）。
					leftHtml = ComposeHtml(leftText, ExtensionOf(src));
					rightHtml = ComposeHtml(rightText, ExtensionOf(dst));
					changed = !string.Equals(leftText, rightText, StringComparison.Ordinal);
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
					PluginLog.Error("FrontendDiffView 渲染失败", ex);
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

		/// <summary>一侧的扩展名（小写含点）；侧不存在返回空串。</summary>
		private static string ExtensionOf(DiffSideContent side)
		{
			if (side == null)
			{
				return string.Empty;
			}
			string ext = Path.GetExtension(side.Path);
			return string.IsNullOrEmpty(ext) ? string.Empty : ext.ToLowerInvariant();
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

		/// <summary>
		/// 把一侧源码组装成自包含 HTML（供「渲染」模式）。source 为 null / 空 → null（该侧占位）；
		/// <c>.vue</c> 交给 <see cref="VueComponentCompiler"/> 运行期编译；<c>.html</c> / <c>.htm</c> 归一 charset。
		/// </summary>
		private static string ComposeHtml(string source, string extension)
		{
			if (string.IsNullOrEmpty(source))
			{
				return null;
			}
			if (extension == ".vue")
			{
				return VueComponentCompiler.Compose(source);
			}
			return NormalizeHtmlCharset(source);
		}

		/// <summary>
		/// 归一 HTML 的字符集声明为 UTF-8（宿主按 UTF-8 解码后交给 WebView，故须与解码一致）；
		/// 已有 &lt;meta charset&gt; 就地替换，缺失则插入 &lt;head&gt; 之后，无 &lt;head&gt; 时前置声明。
		/// </summary>
		private static string NormalizeHtmlCharset(string html)
		{
			if (CharsetMetaRegex.IsMatch(html))
			{
				return CharsetMetaRegex.Replace(html, Utf8CharsetMeta, 1);
			}
			Match head = HeadOpenRegex.Match(html);
			if (head.Success)
			{
				return html.Substring(0, head.Index + head.Length) + "\n" + Utf8CharsetMeta
					+ html.Substring(head.Index + head.Length);
			}
			return Utf8CharsetMeta + "\n" + html;
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
				return roleText + "  ·  " + FrontendStrings.T("not present");
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
				_status.Text = FrontendStrings.T("File too large to render");
				return;
			}
			if (_error != null)
			{
				_status.Text = FrontendStrings.F("Failed to read: {0}", _error);
				return;
			}
			if (!_analyzed)
			{
				_status.Text = FrontendStrings.T("Analyzing…");
				return;
			}
			string head = FrontendStrings.F("Frontend compare: {0} / {1}", DisplayTitle(_context?.Src), DisplayTitle(_context?.Dst));
			if (_srcAbsent || _dstAbsent)
			{
				_status.Text = head + "  ·  " + FrontendStrings.T("One side is not present; only the other side is shown.");
				return;
			}
			_status.Text = head + "  ·  " + FrontendStrings.T(_changed ? "Changed" : "No differences");
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
			return FrontendStrings.T("not present");
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
				_content.Content = NoteText(FrontendStrings.T("File too large to render"));
				return;
			}
			if (!_analyzed)
			{
				_content.Content = NoteText(_error == null ? FrontendStrings.T("Analyzing…") : FrontendStrings.F("Failed to read: {0}", _error));
				return;
			}
			if (_mode == SourceMode)
			{
				_content.Content = BuildSourceContent();
			}
			else
			{
				_content.Content = BuildRenderContent();
			}
		}

		/// <summary>
		/// 「渲染」模式：并排双 WebView 真渲染两侧自包含 HTML（后台组装）。
		/// 某侧不存在 / 无内容 → 该侧占位；WebView 不可用（headless、缺运行时）→ 两侧仅给提示文案（降级，不报错）。
		/// </summary>
		private Control BuildRenderContent()
		{
			Grid grid = new Grid
			{
				RowDefinitions = new RowDefinitions("Auto,*")
			};
			TextBlock note = NoteText(FrontendStrings.T("External relative assets (css / js / images) cannot be resolved and may be missing."));
			Grid.SetRow(note, 0);
			grid.Children.Add(note);

			if (_preview == null)
			{
				_preview = new SideBySideHtmlPreview();
			}
			// 占位 / 降级文案随界面语言（shared 组件默认中文，这里按插件译文覆盖）。
			_preview.AbsentText = FrontendStrings.T("not present");
			_preview.UnavailableText = FrontendStrings.T("Preview not available in this environment");
			_preview.SetContent(
				DisplayTitle(_context?.Src),
				_leftHtml,
				DisplayTitle(_context?.Dst),
				_rightHtml);
			Grid.SetRow(_preview, 1);
			grid.Children.Add(_preview);
			return grid;
		}

		/// <summary>「源码」模式：左右两栏并排看两侧原始代码（等宽、只读、可选中）。</summary>
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
					Text = FrontendStrings.T("not present"),
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
					TextBlock note = NoteText(FrontendStrings.F("Showing first {0} of {1} characters", MaxSourceChars, shown.Length));
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