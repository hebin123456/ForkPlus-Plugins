using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ForkPlus.Plugins.Abstractions;
using ForkPlus.Plugins.Ui.HtmlPreview;

namespace ForkPlus.Plugins.Patch
{
	/// <summary>
	/// 补丁（unified diff / git diff）对比视图：把旧（左）/ 新（右）两侧的补丁文本容错解析成
	/// 文件条目（路径 / 改动类型 / hunk），按「新路径优先」的键对齐成
	/// 「相同 / 已变 / 仅左 / 仅右」四态。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、
	/// 自带的分段模式工具条（文件 / 预览），最下是内容区。
	///
	/// 两个模式：
	/// <list type="bullet">
	/// <item>文件（默认）：一张四列表——「文件 | 旧 | 新 | 状态」，路径用等宽字体，单元格给
	/// 「改动类型 · +增 −删」，逐行按四色标注底色，一眼看出两版补丁间的文件增删与改动变化。</item>
	/// <item>预览：并排双 WebView 真渲染两侧补丁内容——<see cref="PatchHtmlComposer"/> 按文件分
	/// 卡片组装自包含 HTML（内容与样式全部内联，跟随系统明暗主题，逼近 90 万字符预算即截断），
	/// 交给共享的 <c>ForkPlus.Plugins.Ui.HtmlPreview.SideBySideHtmlPreview</c> 渲染；
	/// WebView 不可用（headless、缺 WebView2 运行时 / WPE WebKit / WebKitGTK）时仅给提示。</item>
	/// </list>
	///
	/// 解析与对齐在后台线程执行，控件只在 UI 线程构建；每次刷新用代次 + CancellationToken 取消上一轮。
	/// 单侧声明大小 &gt; 300 MB 只给提示、不解析。
	/// </summary>
	public sealed class PatchDiffView : IDiffView
	{
		private const string FilesMode = "files";

		private const string PreviewMode = "preview";

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （preview，其余走默认 files），据此逐模式取图；正常运行时该变量为空，走默认文件模式。
		/// </summary>
		private static readonly string InitialMode = ResolveInitialMode();

		private static string ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			if (string.Equals(forced, PreviewMode, StringComparison.OrdinalIgnoreCase))
			{
				return PreviewMode;
			}
			return FilesMode;
		}

		/// <summary>单侧超过此大小不解析（与 CHM / EPUB / 字幕插件同口径）。</summary>
		private const long MaxSideBytes = 300L * 1024L * 1024L;

		/// <summary>文件表渲染上限：超大补丁（文件 &gt; 3000）截断并在表下注明。</summary>
		private const int MaxRows = 3000;

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

		private readonly Button _filesButton;

		private readonly Button _previewButton;

		private readonly ContentControl _content;

		/// <summary>「预览」模式的并排双 WebView 控件（懒建、复用；WebView 不可用时显示提示占位）。</summary>
		private SideBySideHtmlPreview _preview;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _generation;

		private string _mode = InitialMode;

		private PatchDocument _left;

		private PatchDocument _right;

		/// <summary>文件级对齐结果（四态 + 汇总计数）。</summary>
		private PatchFileDiffResult _diff;

		/// <summary>「预览」模式两侧的自包含 HTML（后台线程组装；该侧不存在 / 解析失败为 null）。</summary>
		private string _leftHtml;

		private string _rightHtml;

		private string _error;

		/// <summary>左 / 右两侧各自的解析错误（侧存在但解析失败时用于区分「解析失败」与「不存在」）。</summary>
		private string _srcParseError;

		private string _dstParseError;

		private bool _srcAbsent;

		private bool _dstAbsent;

		private bool _tooLarge;

		public PatchDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			_status = new TextBlock
			{
				Margin = new Thickness(12.0, 2.0, 12.0, 6.0),
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0
			};
			_filesButton = NewModeButton(PatchStrings.T("Files"), delegate
			{
				SetMode(FilesMode);
			});
			_previewButton = NewModeButton(PatchStrings.T("Preview"), delegate
			{
				SetMode(PreviewMode);
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
			modes.Children.Add(_filesButton);
			modes.Children.Add(_previewButton);

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

			StyleModeButton(_filesButton, _mode == FilesMode);
			StyleModeButton(_previewButton, _mode == PreviewMode);
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
			PluginLog.Info($"PatchDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			Render();
		}

		public void SetMode(string modeId)
		{
			if (string.IsNullOrEmpty(modeId))
			{
				return;
			}
			if (modeId != FilesMode && modeId != PreviewMode)
			{
				return;
			}
			if (modeId == _mode)
			{
				return;
			}
			_mode = modeId;
			StyleModeButton(_filesButton, _mode == FilesMode);
			StyleModeButton(_previewButton, _mode == PreviewMode);
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
			_filesButton.Content = PatchStrings.T("Files");
			_previewButton.Content = PatchStrings.T("Preview");
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
			_leftHtml = null;
			_rightHtml = null;
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
			_leftHtml = null;
			_rightHtml = null;
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
				PatchDocument left = null;
				PatchDocument right = null;
				string srcParseError = null;
				string dstParseError = null;
				try
				{
					byte[] leftBytes = ReadBytes(src, hexSrc, cts.Token);
					byte[] rightBytes = ReadBytes(dst, hexDst, cts.Token);
					cts.Token.ThrowIfCancellationRequested();
					if (leftBytes != null)
					{
						left = PatchParser.Parse(leftBytes, out srcParseError);
					}
					if (rightBytes != null)
					{
						right = PatchParser.Parse(rightBytes, out dstParseError);
					}
					if (cts.Token.IsCancellationRequested)
					{
						return;
					}
					PatchFileDiffResult diff = PatchFileDiffResult.Compute(left?.Files, right?.Files);
					// 「预览」模式：在后台把两侧补丁组装成自包含 HTML（预算截断在组装器内完成）。
					string leftHtml = left != null ? PatchHtmlComposer.Compose(left) : null;
					string rightHtml = right != null ? PatchHtmlComposer.Compose(right) : null;
					Dispatcher.UIThread.Post(delegate
					{
						if (_generation != generation || _cts != cts)
						{
							return;
						}
						_left = left;
						_right = right;
						_diff = diff;
						_leftHtml = leftHtml;
						_rightHtml = rightHtml;
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
					PluginLog.Error("PatchDiffView 渲染失败", ex);
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
		/// 保存 / 恢复流 Position，拷成 byte[]（解析自建缓冲，不动宿主的流）。
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
				return roleText + "  ·  " + PatchStrings.T("not present");
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
				_status.Text = PatchStrings.T("File too large to preview");
				return;
			}
			string error = DisplayError();
			if (error != null)
			{
				_status.Text = PatchStrings.F("Failed to parse: {0}", error);
				return;
			}
			if (_diff == null)
			{
				_status.Text = PatchStrings.T("Analyzing…");
				return;
			}
			string head = PatchStrings.F("Patch compare: {0} / {1}", DisplayTitle(_left, _context?.Src), DisplayTitle(_right, _context?.Dst));
			head = head + "  ·  " + PatchStrings.F("files: {0} / {1}", _left?.Files.Count ?? 0, _right?.Files.Count ?? 0);
			if (!_diff.HasDifferences)
			{
				_status.Text = head + "  ·  " + PatchStrings.T("No differences");
				return;
			}
			_status.Text = head + "  ·  " + PatchStrings.F("Summary: {0} files · {1} changed · {2} left only · {3} right only", _diff.Total, _diff.Changed, _diff.LeftOnly, _diff.RightOnly);
		}

		/// <summary>状态行文件名：侧文件名 &gt; not present（侧不存在 / 解析失败）。</summary>
		private static string DisplayTitle(PatchDocument document, DiffSideContent side)
		{
			if (side != null)
			{
				string name = Path.GetFileName(side.Path);
				if (!string.IsNullOrEmpty(name))
				{
					return name;
				}
			}
			return PatchStrings.T("not present");
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
				_content.Content = NoteText(PatchStrings.T("File too large to preview"));
				return;
			}
			if (_diff == null)
			{
				_content.Content = NoteText(DisplayError() == null ? PatchStrings.T("Analyzing…") : PatchStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			// 两侧都存在却都解析失败（如扩展名对但内容不是补丁）：给错误占位，不再渲染空表。
			if (_left == null && _right == null && DisplayError() != null)
			{
				_content.Content = NoteText(PatchStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			if (_mode == PreviewMode)
			{
				BuildPreviewContent();
			}
			else
			{
				_content.Content = BuildFilesContent();
			}
		}

		/// <summary>
		/// 「预览」模式：并排双 WebView 真渲染两侧补丁内容（后台组装好的自包含 HTML）。
		/// 某侧不存在 / 解析失败 → 该侧占位；WebView 不可用（headless、缺 WebView2 运行时 /
		/// WPE WebKit / WebKitGTK）→ 两侧仅给提示文案（降级，不报错）。
		/// </summary>
		private void BuildPreviewContent()
		{
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
			_preview.AbsentText = PatchStrings.T("not present");
			_preview.UnavailableText = PatchStrings.T("Preview not available in this environment");
			_preview.SetContent(
				DisplayTitle(_left, _context?.Src),
				_leftHtml,
				DisplayTitle(_right, _context?.Dst),
				_rightHtml);
			_content.Content = _preview;
		}

		/// <summary>文件表：「文件 | 旧 | 新 | 状态」四列，按路径对齐；单元格 = 「改动类型 · +增 −删」。</summary>
		private Control BuildFilesContent()
		{
			Grid table = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("3*,2*,2*,Auto")
			};
			int row = 0;
			AddTableRow(table, ref row, PatchStrings.T("File"), PatchStrings.T("Old"), PatchStrings.T("New"), PatchStrings.T("State"), null, null, true);

			int shown = 0;
			foreach (PatchFileDiffRow item in _diff.Rows)
			{
				if (shown >= MaxRows)
				{
					break;
				}
				shown++;
				AddTableRow(table, ref row, item.Key, DescribeFile(item.Left), DescribeFile(item.Right), StateLabel(item.State), TintOf(item.State), StateBrush(item.State), false);
			}

			StackPanel stack = new StackPanel
			{
				Spacing = 6.0
			};
			stack.Children.Add(WrapInCard(table));
			if (_diff.Total > shown)
			{
				stack.Children.Add(NoteText(PatchStrings.F("Showing first {0} of {1} rows", shown, _diff.Total)));
			}
			if (_srcAbsent || _dstAbsent)
			{
				stack.Children.Add(NoteText(PatchStrings.T("One side is not present; values shown for the other side only.")));
			}
			return new ScrollViewer
			{
				Content = stack,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
			};
		}

		/// <summary>四列表加一行：可带四色底（Border 跨四列）+ 状态列彩色；首列为等宽字体（文件路径）。</summary>
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

		private static IBrush TintOf(PatchFileState state)
		{
			switch (state)
			{
			case PatchFileState.Changed:
				return TintChanged;
			case PatchFileState.LeftOnly:
				return TintLeftOnly;
			case PatchFileState.RightOnly:
				return TintRightOnly;
			default:
				return null;
			}
		}

		private static IBrush StateBrush(PatchFileState state)
		{
			switch (state)
			{
			case PatchFileState.Changed:
				return StateChanged;
			case PatchFileState.LeftOnly:
				return StateLeft;
			case PatchFileState.RightOnly:
				return StateRight;
			default:
				return Brushes.Gray;
			}
		}

		private static string StateLabel(PatchFileState state)
		{
			switch (state)
			{
			case PatchFileState.Changed:
				return PatchStrings.T("changed");
			case PatchFileState.LeftOnly:
				return PatchStrings.T("left only");
			case PatchFileState.RightOnly:
				return PatchStrings.T("right only");
			default:
				return PatchStrings.T("same");
			}
		}

		/// <summary>旧 / 新单元格：「改动类型 · +增 −删」；二进制文件只标注；无此侧为 not present。</summary>
		private static string DescribeFile(PatchFile file)
		{
			if (file == null)
			{
				return PatchStrings.T("not present");
			}
			if (file.IsBinary)
			{
				return PatchStrings.T("binary file");
			}
			return PatchStrings.F("{0} · +{1} −{2}", ChangeLabel(file.Change), file.Additions, file.Deletions);
		}

		private static string ChangeLabel(PatchFileChange change)
		{
			switch (change)
			{
			case PatchFileChange.Added:
				return PatchStrings.T("added");
			case PatchFileChange.Deleted:
				return PatchStrings.T("deleted");
			case PatchFileChange.Renamed:
				return PatchStrings.T("renamed");
			default:
				return PatchStrings.T("modified");
			}
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
