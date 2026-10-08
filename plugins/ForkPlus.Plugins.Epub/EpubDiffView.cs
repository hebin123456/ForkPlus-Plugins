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

namespace ForkPlus.Plugins.Epub
{
	/// <summary>
	/// EPUB 电子书对比视图：把旧（左）/ 新（右）两侧的 ZIP 容器解开（ZipArchive），解析
	/// container.xml → OPF（元数据 / manifest / spine），按「元数据」与「章节」两种口径对比。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、
	/// 自带的分段模式工具条（元数据 / 章节），最下是内容区。
	///
	/// 两个模式：
	/// <list type="bullet">
	/// <item>元数据（默认）：一张四列表——「键路径 | 旧值 | 新值 | 状态」，逐行按四色标注底色，
	/// 是看「改了哪条元数据 / 统计」的首选。</item>
	/// <item>章节：一张四列表——「# | 旧 | 新 | 状态」，按 spine 序号对齐两侧章节
	/// （第 i 章 · 标题（href 文件名 · 体积）），一眼看出章节的增删与挪动。</item>
	/// </list>
	///
	/// 解析与对齐在后台线程执行，控件只在 UI 线程构建；每次刷新用代次 + CancellationToken 取消上一轮。
	/// 单侧声明大小 &gt; 300 MB 只给提示、不解析。
	/// </summary>
	public sealed class EpubDiffView : IDiffView
	{
		private const string MetadataMode = "metadata";

		private const string ChaptersMode = "chapters";

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （chapters，其余走默认 metadata），据此逐模式取图；正常运行时该变量为空，走默认元数据模式。
		/// </summary>
		private static readonly string InitialMode = ResolveInitialMode();

		private static string ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			return string.Equals(forced, ChaptersMode, StringComparison.OrdinalIgnoreCase) ? ChaptersMode : MetadataMode;
		}

		/// <summary>单侧超过此大小不解析（与字幕 / 音视频 / 结构化数据插件同口径）。</summary>
		private const long MaxSideBytes = 300L * 1024L * 1024L;

		/// <summary>元数据表渲染上限：行数本就不多，防御性兜底。</summary>
		private const int MaxRows = 3000;

		/// <summary>章节表渲染上限：超大 EPUB（章节 &gt; 2000）截断并在表下注明。</summary>
		private const int MaxChapterRows = 2000;

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

		private readonly Button _metadataButton;

		private readonly Button _chaptersButton;

		private readonly ContentControl _content;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _generation;

		private string _mode = InitialMode;

		private EpubDocument _left;

		private EpubDocument _right;

		/// <summary>元数据 diff（状态行汇总用的 Metadata 口径）。</summary>
		private EpubDiffResult _metaDiff;

		/// <summary>章节 diff（按 spine 序号对齐）。</summary>
		private ChapterDiffResult _chapterDiff;

		private string _error;

		/// <summary>左 / 右两侧各自的解析错误（侧存在但解析失败时用于区分「解析失败」与「不存在」）。</summary>
		private string _srcParseError;

		private string _dstParseError;

		private bool _srcAbsent;

		private bool _dstAbsent;

		private bool _tooLarge;

		public EpubDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			_status = new TextBlock
			{
				Margin = new Thickness(12.0, 2.0, 12.0, 6.0),
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0
			};
			_metadataButton = NewModeButton(EpubStrings.T("Metadata"), delegate
			{
				SetMode(MetadataMode);
			});
			_chaptersButton = NewModeButton(EpubStrings.T("Chapters"), delegate
			{
				SetMode(ChaptersMode);
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
			modes.Children.Add(_metadataButton);
			modes.Children.Add(_chaptersButton);

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

			StyleModeButton(_metadataButton, _mode == MetadataMode);
			StyleModeButton(_chaptersButton, _mode == ChaptersMode);
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
			PluginLog.Info($"EpubDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			Render();
		}

		public void SetMode(string modeId)
		{
			if (string.IsNullOrEmpty(modeId))
			{
				return;
			}
			if (modeId != MetadataMode && modeId != ChaptersMode)
			{
				return;
			}
			if (modeId == _mode)
			{
				return;
			}
			_mode = modeId;
			StyleModeButton(_metadataButton, _mode == MetadataMode);
			StyleModeButton(_chaptersButton, _mode == ChaptersMode);
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
			_metadataButton.Content = EpubStrings.T("Metadata");
			_chaptersButton.Content = EpubStrings.T("Chapters");
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
			_metaDiff = null;
			_chapterDiff = null;
			_content.Content = null;
			_status.Text = string.Empty;
		}

		// ---- 渲染 ----

		private void Render()
		{
			CancelRender();
			_left = null;
			_right = null;
			_metaDiff = null;
			_chapterDiff = null;
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
				EpubDocument left = null;
				EpubDocument right = null;
				string srcParseError = null;
				string dstParseError = null;
				try
				{
					byte[] leftBytes = ReadBytes(src, hexSrc, cts.Token);
					byte[] rightBytes = ReadBytes(dst, hexDst, cts.Token);
					cts.Token.ThrowIfCancellationRequested();
					if (leftBytes != null)
					{
						left = EpubParser.Parse(leftBytes, out srcParseError);
					}
					if (rightBytes != null)
					{
						right = EpubParser.Parse(rightBytes, out dstParseError);
					}
					if (cts.Token.IsCancellationRequested)
					{
						return;
					}
					EpubDiffResult metaDiff = EpubDiff.ComputeRows(left?.Rows, right?.Rows);
					ChapterDiffResult chapterDiff = EpubDiff.ComputeChapters(left?.Chapters, right?.Chapters);
					Dispatcher.UIThread.Post(delegate
					{
						if (_generation != generation || _cts != cts)
						{
							return;
						}
						_left = left;
						_right = right;
						_metaDiff = metaDiff;
						_chapterDiff = chapterDiff;
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
					PluginLog.Error("EpubDiffView 渲染失败", ex);
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
		/// 保存 / 恢复流 Position，拷成 byte[]（ZipArchive 自建 MemoryStream 用，不动宿主的流）。
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
				return roleText + "  ·  " + EpubStrings.T("not present");
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
				_status.Text = EpubStrings.T("File too large to preview");
				return;
			}
			string error = DisplayError();
			if (error != null)
			{
				_status.Text = EpubStrings.F("Failed to parse: {0}", error);
				return;
			}
			if (_metaDiff == null)
			{
				_status.Text = EpubStrings.T("Analyzing…");
				return;
			}
			// 书名取 dc:title，缺失回退文件名；侧不存在显示 not present。
			string head = EpubStrings.F("EPUB compare: {0} / {1}", DisplayTitle(_left, _context?.Src), DisplayTitle(_right, _context?.Dst));
			head = head + "  ·  " + EpubStrings.F("chapters: {0} / {1}", _left?.Chapters.Count ?? 0, _right?.Chapters.Count ?? 0);
			// 汇总走 Metadata 口径（章节差异直接看章节表）。
			if (!_metaDiff.HasDifferences)
			{
				_status.Text = head + "  ·  " + EpubStrings.T("No differences");
				return;
			}
			_status.Text = head + "  ·  " + EpubStrings.F("Summary: {0} entries · {1} changed · {2} left only · {3} right only", _metaDiff.Total, _metaDiff.Changed, _metaDiff.LeftOnly, _metaDiff.RightOnly);
		}

		/// <summary>状态行书名：dc:title &gt; 文件名 &gt; not present（侧不存在）。</summary>
		private static string DisplayTitle(EpubDocument document, DiffSideContent side)
		{
			if (!string.IsNullOrEmpty(document?.Title))
			{
				return document.Title;
			}
			if (side != null)
			{
				string name = Path.GetFileName(side.Path);
				if (!string.IsNullOrEmpty(name))
				{
					return name;
				}
			}
			return EpubStrings.T("not present");
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
				_content.Content = NoteText(EpubStrings.T("File too large to preview"));
				return;
			}
			if (_metaDiff == null)
			{
				_content.Content = NoteText(DisplayError() == null ? EpubStrings.T("Analyzing…") : EpubStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			// 两侧都存在却都解析失败（如扩展名对但内容不是 EPUB）：给错误占位，不再渲染空表。
			if (_left == null && _right == null && DisplayError() != null)
			{
				_content.Content = NoteText(EpubStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			if (_mode == ChaptersMode)
			{
				_content.Content = BuildChaptersContent();
			}
			else
			{
				_content.Content = BuildMetadataContent();
			}
		}

		/// <summary>元数据表：「键路径 | 旧值 | 新值 | 状态」四列，逐行四色底 + 状态列彩色。</summary>
		private Control BuildMetadataContent()
		{
			Grid table = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("2*,3*,3*,Auto")
			};
			int row = 0;
			AddTableRow(table, ref row, EpubStrings.T("Key path"), EpubStrings.T("Old"), EpubStrings.T("New"), EpubStrings.T("State"), null, null, true);

			int shown = 0;
			foreach (EpubRowDiff item in _metaDiff.Rows)
			{
				if (shown >= MaxRows)
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
			if (_metaDiff.Total > shown)
			{
				stack.Children.Add(NoteText(EpubStrings.F("Showing first {0} of {1} rows", shown, _metaDiff.Total)));
			}
			if (_srcAbsent || _dstAbsent)
			{
				stack.Children.Add(NoteText(EpubStrings.T("One side is not present; values shown for the other side only.")));
			}
			return new ScrollViewer
			{
				Content = stack,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
			};
		}

		/// <summary>章节表：「# | 旧 | 新 | 状态」四列，按 spine 序号对齐；单元格 =
		/// 「第 i 章 · 标题（href 文件名 · 体积）」，无标题回退 href 文件名。</summary>
		private Control BuildChaptersContent()
		{
			Grid table = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("Auto,3*,3*,Auto")
			};
			int row = 0;
			AddTableRow(table, ref row, EpubStrings.T("#"), EpubStrings.T("Old"), EpubStrings.T("New"), EpubStrings.T("State"), null, null, true);

			int shown = 0;
			foreach (EpubChapterDiff item in _chapterDiff.Rows)
			{
				if (shown >= MaxChapterRows)
				{
					break;
				}
				shown++;
				string index = (item.Left ?? item.Right).Index.ToString();
				AddTableRow(table, ref row, index, DisplayChapter(item.Left), DisplayChapter(item.Right), StateLabel(item.State), TintOf(item.State), StateBrush(item.State), false);
			}

			StackPanel stack = new StackPanel
			{
				Spacing = 6.0
			};
			stack.Children.Add(WrapInCard(table));
			// 章节超 2000 截断注明。
			if (_chapterDiff.Total > shown)
			{
				stack.Children.Add(NoteText(EpubStrings.F("Showing first {0} of {1} rows", shown, _chapterDiff.Total)));
			}
			if (_srcAbsent || _dstAbsent)
			{
				stack.Children.Add(NoteText(EpubStrings.T("One side is not present; values shown for the other side only.")));
			}
			return new ScrollViewer
			{
				Content = stack,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
			};
		}

		/// <summary>四列表加一行：可带四色底（Border 跨四列）+ 状态列彩色；首列为等宽字体（键路径 / 序号）。</summary>
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

		private static IBrush TintOf(EpubState state)
		{
			switch (state)
			{
			case EpubState.Changed:
				return TintChanged;
			case EpubState.LeftOnly:
				return TintLeftOnly;
			case EpubState.RightOnly:
				return TintRightOnly;
			default:
				return null;
			}
		}

		private static IBrush StateBrush(EpubState state)
		{
			switch (state)
			{
			case EpubState.Changed:
				return StateChanged;
			case EpubState.LeftOnly:
				return StateLeft;
			case EpubState.RightOnly:
				return StateRight;
			default:
				return Brushes.Gray;
			}
		}

		private static string StateLabel(EpubState state)
		{
			switch (state)
			{
			case EpubState.Changed:
				return EpubStrings.T("changed");
			case EpubState.LeftOnly:
				return EpubStrings.T("left only");
			case EpubState.RightOnly:
				return EpubStrings.T("right only");
			default:
				return EpubStrings.T("same");
			}
		}

		/// <summary>元数据值：无此侧的值为 not present。</summary>
		private static string DisplayValue(string value)
		{
			return value ?? EpubStrings.T("not present");
		}

		/// <summary>章节单元格：「第 i 章 · 标题（href 文件名 · 体积）」；无标题回退 href 文件名；
		/// linear="no" 的项保留但标注；体积用 zip 条目未压缩大小。</summary>
		private static string DisplayChapter(EpubChapter chapter)
		{
			if (chapter == null)
			{
				return EpubStrings.T("not present");
			}
			string fileName = FileNameOf(chapter.Href);
			string title = string.IsNullOrEmpty(chapter.Title) ? fileName : chapter.Title;
			string text = EpubStrings.F("Chapter {0} · {1} ({2} · {3})", chapter.Index, title, fileName, PluginSizeFormat.ReadableFileSize(chapter.SizeBytes));
			if (!chapter.Linear)
			{
				text = text + "  [" + EpubStrings.T("non-linear") + "]";
			}
			return text;
		}

		/// <summary>href 的文件名部分（最后一个 '/' 之后）。</summary>
		private static string FileNameOf(string href)
		{
			if (string.IsNullOrEmpty(href))
			{
				return string.Empty;
			}
			int slash = href.LastIndexOf('/');
			return slash < 0 ? href : href.Substring(slash + 1);
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
