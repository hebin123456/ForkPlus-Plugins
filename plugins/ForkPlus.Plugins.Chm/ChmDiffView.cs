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

		/// <summary>左 / 右两侧各自的 LFS 加载错误（侧是 LFS 文件、缓存未命中且 smudge 失败时记录；
		/// 用于在状态行给出真实原因，而不是笼统的「内容不可用」）。</summary>
		private string _srcLfsError;

		private string _dstLfsError;

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
			_srcLfsError = null;
			_dstLfsError = null;
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
			_srcLfsError = null;
			_dstLfsError = null;
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

			Task.Run(async delegate
			{
				ChmDocument left = null;
				ChmDocument right = null;
				string srcParseError = null;
				string dstParseError = null;
				try
				{
					(byte[] leftBytes, string srcLfsError) = await ResolveSideAsync(src, hexSrc, cts.Token).ConfigureAwait(false);
					(byte[] rightBytes, string dstLfsError) = await ResolveSideAsync(dst, hexDst, cts.Token).ConfigureAwait(false);
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
						// 供给诊断：侧存在却拿不到字节时记一条日志（宿主 Hex 预载 / 侧懒加载 / LFS 缓存与 smudge 全空）。
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
						_srcLfsError = srcLfsError;
						_dstLfsError = dstLfsError;
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
		/// 取一侧原始字节：宿主 Hex 预载 &gt; 侧内容懒加载 &gt; LFS 本地缓存 &gt; LFS smudge 下载。
		/// 拷成 byte[]（解析器自建 MemoryStream 用，不动宿主的流——ToArray 不改变宿主流 Position）。
		/// 侧是 LFS 文件且缓存未命中时启动 smudge（宿主在 UI 线程回调，TCS 桥回后台线程），
		/// 成功返回真身字节；失败返回错误描述（供状态行展示真实原因，而非笼统的「内容不可用」）。
		/// 模式与 Archive / Pdf / Video 等插件一致（此前 Chm 只查缓存、从不触发下载，
		/// 导致未 smudge 的 LFS chm 恒显示「内容不可用」）。
		/// </summary>
		private async Task<(byte[] bytes, string lfsError)> ResolveSideAsync(DiffSideContent side, MemoryStream preloaded, CancellationToken token)
		{
			if (side == null)
			{
				return (null, null);
			}
			if (preloaded != null && preloaded.Length > 0)
			{
				return (preloaded.ToArray(), null);
			}
			MemoryStream data = null;
			try
			{
				data = side.Data;
			}
			catch (Exception ex)
			{
				PluginLog.Warn("Chm: failed to load side bytes for '" + side.Path + "'", ex);
			}
			if (data != null && data.Length > 0)
			{
				return (data.ToArray(), null);
			}
			if (side.Lfs == null || _host == null)
			{
				return (null, null);
			}
			MemoryStream cached = null;
			try
			{
				cached = _host.GetCachedLfsData(side.Lfs);
			}
			catch (Exception ex)
			{
				PluginLog.Warn("Chm: LFS cache lookup failed", ex);
			}
			if (cached != null && cached.Length > 0)
			{
				return (cached.ToArray(), null);
			}
			// 缓存未命中：启动 LFS smudge，宿主在 UI 线程回调，用 TCS 桥回后台线程。
			TaskCompletionSource<LfsSmudgeResult> tcs = new TaskCompletionSource<LfsSmudgeResult>();
			IDisposable subscription = null;
			CancellationTokenRegistration registration = token.Register(delegate
			{
				subscription?.Dispose();
				tcs.TrySetCanceled();
			});
			try
			{
				subscription = _host.RunLfsSmudge(side.Lfs, null, delegate(LfsSmudgeResult result)
				{
					tcs.TrySetResult(result);
				});
				LfsSmudgeResult smudge = await tcs.Task.ConfigureAwait(false);
				if (smudge != null && smudge.Succeeded && smudge.Data != null && smudge.Data.Length > 0)
				{
					return (smudge.Data.ToArray(), null);
				}
				PluginLog.Warn("Chm: LFS smudge failed for '" + side.Path + "': " + (smudge?.Error ?? "unknown"));
				return (null, smudge?.Error ?? "LFS smudge failed");
			}
			finally
			{
				registration.Dispose();
				subscription?.Dispose();
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

		/// <summary>优先展示的状态错误：意外异常 &gt; 左侧解析失败 &gt; 右侧解析失败 &gt; LFS 加载失败。</summary>
		private string DisplayError()
		{
			return _error ?? _srcParseError ?? _dstParseError;
		}

		/// <summary>LFS smudge 失败的可读原因（左优先）；两侧都不是 LFS 下载失败时为 null。</summary>
		private string DisplayLfsError()
		{
			return _srcLfsError ?? _dstLfsError;
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
			string lfsError = DisplayLfsError();
			if (lfsError != null)
			{
				_status.Text = ChmStrings.F("Failed to load LFS content: {0}", lfsError);
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
			// LFS smudge 失败时给出真实原因，其余给「内容不可用」占位——空表只有表头，什么都说明不了。
			if (_left == null && _right == null && DisplayError() == null)
			{
				string lfsError = DisplayLfsError();
				_content.Content = NoteText(lfsError != null
					? ChmStrings.F("Failed to load LFS content: {0}", lfsError)
					: ChmStrings.T("content unavailable"));
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

		/// <summary>元数据表：「键路径 | 旧值 | 新值 | 状态」四列，逐行四色底 + 状态列彩色。
		/// UI 线程只做一次轻量行模型映射，行控件由虚拟化列表按可见范围惰性创建（大文档不再卡 UI）。</summary>
		private Control BuildMetadataContent()
		{
			bool srcResolvable = _srcAbsent || _left != null;
			bool dstResolvable = _dstAbsent || _right != null;
			List<TableRow> rows = new List<TableRow>();
			int shown = 0;
			foreach (ChmRowDiff item in _metaDiff.Rows)
			{
				if (shown >= MaxRows)
				{
					break;
				}
				shown++;
				rows.Add(new TableRow
				{
					C0 = item.Path,
					C1 = DisplayValue(item.Left, srcResolvable),
					C2 = DisplayValue(item.Right, dstResolvable),
					C3 = StateLabel(item.State),
					Tint = TintOf(item.State),
					StateForeground = StateBrush(item.State),
					Mono0 = true
				});
			}
			TableRow header = new TableRow
			{
				IsHeader = true,
				C0 = ChmStrings.T("Key path"),
				C1 = ChmStrings.T("Old"),
				C2 = ChmStrings.T("New"),
				C3 = ChmStrings.T("State")
			};
			return NewTableCard(header, rows, TableNotes(shown, _metaDiff.Total));
		}

		/// <summary>目录条目表：「路径 | 旧 | 新 | 状态」四列，按内部路径对齐；
		/// 单元格：目录项标注「目录」，文件给「体积 · 内容指纹」。行控件虚拟化。</summary>
		private Control BuildEntriesContent()
		{
			bool srcResolvable = _srcAbsent || _left != null;
			bool dstResolvable = _dstAbsent || _right != null;
			List<TableRow> rows = new List<TableRow>();
			int shown = 0;
			foreach (ChmEntryDiff item in _entryDiff.Rows)
			{
				if (shown >= MaxEntryRows)
				{
					break;
				}
				shown++;
				string path = (item.Left ?? item.Right)?.Path ?? string.Empty;
				rows.Add(new TableRow
				{
					C0 = path,
					C1 = DisplayEntry(item.Left, srcResolvable),
					C2 = DisplayEntry(item.Right, dstResolvable),
					C3 = StateLabel(item.State),
					Tint = TintOf(item.State),
					StateForeground = StateBrush(item.State),
					Mono0 = true
				});
			}
			TableRow header = new TableRow
			{
				IsHeader = true,
				C0 = ChmStrings.T("Path"),
				C1 = ChmStrings.T("Old"),
				C2 = ChmStrings.T("New"),
				C3 = ChmStrings.T("State")
			};
			return NewTableCard(header, rows, TableNotes(shown, _entryDiff.Total));
		}

		/// <summary>表下方备注：截断提示 + 单侧缺失/单侧无字节的口径说明（固定在卡片下，不随表体滚动）。</summary>
		private List<Control> TableNotes(int shown, int total)
		{
			List<Control> notes = new List<Control>();
			if (total > shown)
			{
				notes.Add(NoteText(ChmStrings.F("Showing first {0} of {1} rows", shown, total)));
			}
			if (_srcAbsent || _dstAbsent)
			{
				notes.Add(NoteText(ChmStrings.T("One side is not present; values shown for the other side only.")));
			}
			else if (_srcNoBytes || _dstNoBytes)
			{
				notes.Add(NoteText(ChmStrings.T("One side's content is unavailable; values shown for the other side only.")));
			}
			return notes;
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