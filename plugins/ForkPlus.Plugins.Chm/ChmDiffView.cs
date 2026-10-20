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

namespace ForkPlus.Plugins.Chm
{
	/// <summary>
	/// CHM（已编译 HTML 帮助）对比视图：把旧（左）/ 新（右）两侧的 CHM 解析成
	/// 「元数据 / 统计」与「目录条目」两种口径，逐行四色标注「相同 / 已变 / 仅左 / 仅右」。
	///
	/// 解析由 <see cref="ChmParser"/> 完成（ITSF / ITSP 头部 + PMGL 目录块 + MSCompressed
	/// 内容流的纯托管 LZX 解压）；<see cref="ChmDiff"/> 负责两个口径的对齐。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、
	/// 自带的分段模式工具条（元数据 / 目录条目 / 预览），最下是内容区。
	///
	/// 三个模式：
	/// <list type="bullet">
	/// <item>元数据（默认）：一张四列表——「键路径 | 旧值 | 新值 | 状态」，逐行按四色标注底色，
	/// 是看「标题 / 默认主题 / 语言 / 格式版本 / 统计」等改动的首选。</item>
	/// <item>目录条目：一张四列表——「路径 | 旧 | 新 | 状态」，按 CHM 内部路径对齐两侧条目
	/// （目录项标注为「目录」，文件给出体积与内容指纹），一眼看出条目的增删与内容改动。</item>
	/// <item>预览：并排双 WebView 真渲染两侧默认主题页——<see cref="ChmHtmlComposer"/> 把
	/// <c>#SYSTEM</c> 记录 2 的默认主题（缺失回退第一个 HTML 条目）与它引用的图片 / 样式 /
	/// 脚本组装成自包含 HTML，交给共享的 <c>ForkPlus.Plugins.Ui.HtmlPreview.SideBySideHtmlPreview</c>
	/// 渲染；WebView 不可用（headless、缺 WebView2 运行时 / WPE WebKit / WebKitGTK）时仅给提示。</item>
	/// </list>
	///
	/// 解析与对齐在后台线程执行，控件只在 UI 线程构建；每次刷新用代次 + CancellationToken 取消上一轮。
	/// 单侧声明大小 &gt; 300 MB 只给提示、不解析。
	/// </summary>
	public sealed class ChmDiffView : IDiffView
	{
		private const string MetadataMode = "metadata";

		private const string EntriesMode = "entries";

		private const string PreviewMode = "preview";

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （entries / preview，其余走默认 metadata），据此逐模式取图；正常运行时该变量为空，
		/// 走默认元数据模式。
		/// </summary>
		private static readonly string InitialMode = ResolveInitialMode();

		private static string ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			if (string.Equals(forced, EntriesMode, StringComparison.OrdinalIgnoreCase))
			{
				return EntriesMode;
			}
			if (string.Equals(forced, PreviewMode, StringComparison.OrdinalIgnoreCase))
			{
				return PreviewMode;
			}
			return MetadataMode;
		}

		/// <summary>单侧超过此大小不解析（与 EPUB / 字幕 / 音视频插件同口径）。</summary>
		private const long MaxSideBytes = 300L * 1024L * 1024L;

		/// <summary>元数据表渲染上限：行数本就不多，防御性兜底。</summary>
		private const int MaxRows = 3000;

		/// <summary>目录条目表渲染上限：超大 CHM（条目 &gt; 2000）截断并在表下注明。</summary>
		private const int MaxEntryRows = 2000;

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

		private readonly Button _entriesButton;

		private readonly Button _previewButton;

		private readonly ContentControl _content;

		/// <summary>「预览」模式的并排双 WebView 控件（懒建、复用；WebView 不可用时显示提示占位）。</summary>
		private SideBySideHtmlPreview _preview;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _generation;

		private string _mode = InitialMode;

		private ChmDocument _left;

		private ChmDocument _right;

		/// <summary>元数据 diff（状态行汇总用的 Metadata 口径）。</summary>
		private ChmRowDiffResult _metaDiff;

		/// <summary>目录条目 diff（按内部路径对齐）。</summary>
		private ChmEntryDiffResult _entryDiff;

		/// <summary>「预览」模式两侧的自包含 HTML（后台线程组装；无默认主题 / 该侧不存在为 null）。</summary>
		private string _leftHtml;

		private string _rightHtml;

		private string _error;

		/// <summary>左 / 右两侧各自的解析错误（侧存在但解析失败时用于区分「解析失败」与「不存在」）。</summary>
		private string _srcParseError;

		private string _dstParseError;

		private bool _srcAbsent;

		private bool _dstAbsent;

		/// <summary>
		/// 左 / 右两侧「侧存在却拿不到字节」（宿主未供给 Hex 预载、侧无懒加载、无 LFS 缓存）。
		/// 典型成因：宿主 Hex 预载失败（如 Windows 下文件被预览器/杀软占用）。与「侧不存在」
		/// 区分展示，避免把还在的文件误标成「不存在」。
		/// </summary>
		private bool _srcNoBytes;

		private bool _dstNoBytes;

		private bool _tooLarge;

		public ChmDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			_status = new TextBlock
			{
				Margin = new Thickness(12.0, 2.0, 12.0, 6.0),
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0
			};
			_metadataButton = NewModeButton(ChmStrings.T("Metadata"), delegate
			{
				SetMode(MetadataMode);
			});
			_entriesButton = NewModeButton(ChmStrings.T("Entries"), delegate
			{
				SetMode(EntriesMode);
			});
			_previewButton = NewModeButton(ChmStrings.T("Preview"), delegate
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
			modes.Children.Add(_metadataButton);
			modes.Children.Add(_entriesButton);
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

			StyleModeButton(_metadataButton, _mode == MetadataMode);
			StyleModeButton(_entriesButton, _mode == EntriesMode);
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
			PluginLog.Info($"ChmDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			Render();
		}

		public void SetMode(string modeId)
		{
			if (string.IsNullOrEmpty(modeId))
			{
				return;
			}
			if (modeId != MetadataMode && modeId != EntriesMode && modeId != PreviewMode)
			{
				return;
			}
			if (modeId == _mode)
			{
				return;
			}
			_mode = modeId;
			StyleModeButton(_metadataButton, _mode == MetadataMode);
			StyleModeButton(_entriesButton, _mode == EntriesMode);
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
			_metadataButton.Content = ChmStrings.T("Metadata");
			_entriesButton.Content = ChmStrings.T("Entries");
			_previewButton.Content = ChmStrings.T("Preview");
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
			_entryDiff = null;
			_leftHtml = null;
			_rightHtml = null;
			_srcNoBytes = false;
			_dstNoBytes = false;
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
			_entryDiff = null;
			_leftHtml = null;
			_rightHtml = null;
			_error = null;
			_srcParseError = null;
			_dstParseError = null;
			_srcNoBytes = false;
			_dstNoBytes = false;
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
				ChmDocument left = null;
				ChmDocument right = null;
				string srcParseError = null;
				string dstParseError = null;
				try
				{
					byte[] leftBytes = ReadBytes(src, hexSrc, cts.Token);
					byte[] rightBytes = ReadBytes(dst, hexDst, cts.Token);
					cts.Token.ThrowIfCancellationRequested();
					if (leftBytes != null)
					{
						left = ChmParser.Parse(leftBytes, out srcParseError);
					}
					if (rightBytes != null)
					{
						right = ChmParser.Parse(rightBytes, out dstParseError);
					}
					if (cts.Token.IsCancellationRequested)
					{
						return;
					}
					ChmRowDiffResult metaDiff = ChmDiff.ComputeRows(left?.Rows, right?.Rows);
					ChmEntryDiffResult entryDiff = ChmDiff.ComputeEntries(left?.Entries, right?.Entries);
					// 「预览」模式：在后台把两侧默认主题页组装成自包含 HTML（取内容 + 内联资源）。
					string leftHtml = left != null ? ChmHtmlComposer.Compose(left) : null;
					string rightHtml = right != null ? ChmHtmlComposer.Compose(right) : null;
					Dispatcher.UIThread.Post(delegate
					{
						if (_generation != generation || _cts != cts)
						{
							return;
						}
						// 供给诊断：侧存在却拿不到字节时记一条日志（宿主 Hex 预载 / 侧懒加载 / LFS 缓存全空）。
						_srcNoBytes = src != null && leftBytes == null;
						_dstNoBytes = dst != null && rightBytes == null;
						if (_srcNoBytes || _dstNoBytes)
						{
							PluginLog.Info($"ChmDiffView content supply incomplete: src(hex:{hexSrc != null}, side:{src != null}) dst(hex:{hexDst != null}, side:{dst != null})");
						}
						_left = left;
						_right = right;
						_metaDiff = metaDiff;
						_entryDiff = entryDiff;
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
					PluginLog.Error("ChmDiffView 渲染失败", ex);
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
		/// 保存 / 恢复流 Position，拷成 byte[]（解析器自建 MemoryStream 用，不动宿主的流）。
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
				return roleText + "  ·  " + ChmStrings.T("not present");
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
				_status.Text = ChmStrings.T("File too large to preview");
				return;
			}
			string error = DisplayError();
			if (error != null)
			{
				_status.Text = ChmStrings.F("Failed to parse: {0}", error);
				return;
			}
			if (_metaDiff == null)
			{
				_status.Text = ChmStrings.T("Analyzing…");
				return;
			}
			// 标题取 #SYSTEM 的 title，缺失回退文件名；侧不存在显示 not present。
			string head = ChmStrings.F("CHM compare: {0} / {1}", DisplayTitle(_left, _context?.Src), DisplayTitle(_right, _context?.Dst));
			head = head + "  ·  " + ChmStrings.F("entries: {0} / {1}", _left?.Entries.Count ?? 0, _right?.Entries.Count ?? 0);
			// 汇总走 Metadata 口径（目录条目差异直接看条目表）。
			if (!_metaDiff.HasDifferences)
			{
				_status.Text = head + "  ·  " + ChmStrings.T("No differences");
				return;
			}
			_status.Text = head + "  ·  " + ChmStrings.F("Summary: {0} entries · {1} changed · {2} left only · {3} right only", _metaDiff.Total, _metaDiff.Changed, _metaDiff.LeftOnly, _metaDiff.RightOnly);
		}

		/// <summary>状态行标题：#SYSTEM 的 title &gt; 文件名 &gt; not present（侧不存在）。</summary>
		private static string DisplayTitle(ChmDocument document, DiffSideContent side)
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
			return ChmStrings.T("not present");
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
				_content.Content = NoteText(ChmStrings.T("File too large to preview"));
				return;
			}
			if (_metaDiff == null)
			{
				_content.Content = NoteText(DisplayError() == null ? ChmStrings.T("Analyzing…") : ChmStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			// 两侧都存在却都解析失败（如扩展名对但内容不是 CHM）：给错误占位，不再渲染空表。
			if (_left == null && _right == null && DisplayError() != null)
			{
				_content.Content = NoteText(ChmStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			// 两侧都没解析出文档又没有解析错误 = 两侧字节都没拿到（宿主 Hex 预载失败等）：
			// 直接给「内容不可用」占位——空表只有表头，什么都说明不了。
			if (_left == null && _right == null && DisplayError() == null)
			{
				_content.Content = NoteText(ChmStrings.T("content unavailable"));
				return;
			}
			if (_mode == PreviewMode)
			{
				BuildPreviewContent();
			}
			else if (_mode == EntriesMode)
			{
				_content.Content = BuildEntriesContent();
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
			AddTableRow(table, ref row, ChmStrings.T("Key path"), ChmStrings.T("Old"), ChmStrings.T("New"), ChmStrings.T("State"), null, null, true);

			int shown = 0;
			bool srcResolvable = _srcAbsent || _left != null;
			bool dstResolvable = _dstAbsent || _right != null;
			foreach (ChmRowDiff item in _metaDiff.Rows)
			{
				if (shown >= MaxRows)
				{
					break;
				}
				shown++;
				AddTableRow(table, ref row, item.Path, DisplayValue(item.Left, srcResolvable), DisplayValue(item.Right, dstResolvable), StateLabel(item.State), TintOf(item.State), StateBrush(item.State), false);
			}

			StackPanel stack = new StackPanel
			{
				Spacing = 6.0
			};
			stack.Children.Add(WrapInCard(table));
			if (_metaDiff.Total > shown)
			{
				stack.Children.Add(NoteText(ChmStrings.F("Showing first {0} of {1} rows", shown, _metaDiff.Total)));
			}
			AddSideNote(stack);
			return new ScrollViewer
			{
				Content = stack,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
			};
		}

		/// <summary>目录条目表：「路径 | 旧 | 新 | 状态」四列，按内部路径对齐；
		/// 单元格：目录项标注「目录」，文件给「体积 · 内容指纹」。</summary>
		private Control BuildEntriesContent()
		{
			Grid table = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("2*,3*,3*,Auto")
			};
			int row = 0;
			AddTableRow(table, ref row, ChmStrings.T("Path"), ChmStrings.T("Old"), ChmStrings.T("New"), ChmStrings.T("State"), null, null, true);

			int shown = 0;
			bool srcResolvable = _srcAbsent || _left != null;
			bool dstResolvable = _dstAbsent || _right != null;
			foreach (ChmEntryDiff item in _entryDiff.Rows)
			{
				if (shown >= MaxEntryRows)
				{
					break;
				}
				shown++;
				string path = (item.Left ?? item.Right)?.Path ?? string.Empty;
				AddTableRow(table, ref row, path, DisplayEntry(item.Left, srcResolvable), DisplayEntry(item.Right, dstResolvable), StateLabel(item.State), TintOf(item.State), StateBrush(item.State), false);
			}

			StackPanel stack = new StackPanel
			{
				Spacing = 6.0
			};
			stack.Children.Add(WrapInCard(table));
			// 条目超 2000 截断注明。
			if (_entryDiff.Total > shown)
			{
				stack.Children.Add(NoteText(ChmStrings.F("Showing first {0} of {1} rows", shown, _entryDiff.Total)));
			}
			AddSideNote(stack);
			return new ScrollViewer
			{
				Content = stack,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
			};
		}

		/// <summary>
		/// 「预览」模式：并排双 WebView 真渲染两侧默认主题页（后台组装好的自包含 HTML）。
		/// 某侧不存在 / 无默认主题 → 该侧占位；WebView 不可用（headless、缺 WebView2 运行时 /
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
			// 两侧都「不存在」用 not present；两侧都在却拿不到字节（或混合）用 content unavailable。
			_preview.AbsentText = (_srcAbsent || _dstAbsent) ? ChmStrings.T("not present") : ChmStrings.T("content unavailable");
			_preview.UnavailableText = ChmStrings.T("Preview not available in this environment");
			_preview.SetContent(
				DisplayTitle(_left, _context?.Src),
				_leftHtml,
				DisplayTitle(_right, _context?.Dst),
				_rightHtml);
			_content.Content = _preview;
		}

		/// <summary>表格下方单侧说明：一侧不存在（add / delete）与一侧内容不可用（字节没拿到）
		/// 分开口径，不再把「内容不可用」误述成「不存在」。</summary>
		private void AddSideNote(StackPanel stack)
		{
			if (_srcAbsent || _dstAbsent)
			{
				stack.Children.Add(NoteText(ChmStrings.T("One side is not present; values shown for the other side only.")));
			}
			else if (_srcNoBytes || _dstNoBytes)
			{
				stack.Children.Add(NoteText(ChmStrings.T("One side's content is unavailable; values shown for the other side only.")));
			}
		}

		/// <summary>四列表加一行：可带四色底（Border 跨四列）+ 状态列彩色；首列为等宽字体（键路径 / 路径）。</summary>
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

		private static IBrush TintOf(ChmState state)
		{
			switch (state)
			{
			case ChmState.Changed:
				return TintChanged;
			case ChmState.LeftOnly:
				return TintLeftOnly;
			case ChmState.RightOnly:
				return TintRightOnly;
			default:
				return null;
			}
		}

		private static IBrush StateBrush(ChmState state)
		{
			switch (state)
			{
			case ChmState.Changed:
				return StateChanged;
			case ChmState.LeftOnly:
				return StateLeft;
			case ChmState.RightOnly:
				return StateRight;
			default:
				return Brushes.Gray;
			}
		}

		private static string StateLabel(ChmState state)
		{
			switch (state)
			{
			case ChmState.Changed:
				return ChmStrings.T("changed");
			case ChmState.LeftOnly:
				return ChmStrings.T("left only");
			case ChmState.RightOnly:
				return ChmStrings.T("right only");
			default:
				return ChmStrings.T("same");
			}
		}

		/// <summary>元数据值：null 值区分两种口径——该侧文档存在（键在该侧缺失）或该侧本身
		/// 不存在时为 not present；该侧存在但字节没拿到 / 解析失败时为 content unavailable。</summary>
		private string DisplayValue(string value, bool sideResolvable)
		{
			if (value != null)
			{
				return value;
			}
			return sideResolvable ? ChmStrings.T("not present") : ChmStrings.T("content unavailable");
		}

		/// <summary>目录条目单元格：目录项标注「目录」；文件给「体积 · 内容指纹」，无指纹时只给体积；
		/// null 条目按 <see cref="DisplayValue"/> 同口径区分 not present / content unavailable。</summary>
		private string DisplayEntry(ChmEntry entry, bool sideResolvable)
		{
			if (entry == null)
			{
				return sideResolvable ? ChmStrings.T("not present") : ChmStrings.T("content unavailable");
			}
			if (entry.IsDirectory)
			{
				return ChmStrings.T("directory");
			}
			string text = PluginSizeFormat.ReadableFileSize(entry.Length);
			if (!string.IsNullOrEmpty(entry.Hash))
			{
				text = text + "  ·  " + entry.Hash;
			}
			return text;
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