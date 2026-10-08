using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ForkPlus.Plugins.Abstractions;
using SkiaSharp;

namespace ForkPlus.Plugins.Font
{
	/// <summary>
	/// 字体对比视图：左右两栏并排，把旧（左）/ 新（右）字体的差异呈现出来。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行，
	/// 再下是自建模式工具栏（Sample / Metadata / Codepoints 三段式切换 + 集合字体的「第几套」
	/// 选择），最下是内容区。
	///
	/// 三个模式：
	/// - 样张：用 SkiaSharp（宿主共享程序集）把固定样张按 12 / 18 / 28 / 48 多字号直接排版绘制成
	///   位图，左右两栏同一字号同一 Grid 行 → 天然对齐（样张文本刻意全用拉丁 / 数字 / 标点，
	///   避免 CI 无 CJK 字体渲染成豆腐块）。
	/// - 元数据：两栏各自分组卡片（Identity / Vertical metrics / Weight & width / Tables），
	///   逐行「名 : 值」，按 相同 / 变了 / 仅左 / 仅右 四色标注。
	/// - 码位：顶部两条水平占比条，下面给出「共有 / 仅左 / 仅右」统计与示例码位，再给 Unicode
	///   block 归类表（block 名 + 左右数量 + 四色标注）。
	///
	/// 字节来源：非图片二进制由宿主经 <c>HexSrc/HexDst</c> 预载；LFS 侧走宿主
	/// <see cref="IDiffViewHost"/> 的缓存 / smudge。解析与样张渲染在后台线程进行，
	/// 控件只在 UI 线程构建。
	/// </summary>
	public sealed class FontDiffView : IDiffView
	{
		private enum ViewMode
		{
			Sample,
			Metadata,
			Codepoints
		}

		private enum MetaState
		{
			Same,
			Changed,
			LeftOnly,
			RightOnly
		}

		/// <summary>一行元数据对比计划（左右取值 + 差异状态）。</summary>
		private sealed class MetaRow
		{
			public MetaRow(string group, string label, string left, string right, MetaState state)
			{
				Group = group;
				Label = label;
				Left = left;
				Right = right;
				State = state;
			}

			public string Group { get; }

			public string Label { get; }

			public string Left { get; }

			public string Right { get; }

			public MetaState State { get; }
		}

		private static readonly IBrush GridLine = Brushes.Gainsboro;

		/// <summary>中性色都用带透明度的灰，浅色 / 深色主题下都保持低对比。</summary>
		private static readonly IBrush Subtle = new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x80, 0x80));

		private static readonly IBrush ChipTint = new SolidColorBrush(Color.FromArgb(0x1F, 0x80, 0x80, 0x80));

		/// <summary>当前模式按钮高亮。</summary>
		private static readonly IBrush ActiveTint = new SolidColorBrush(Color.FromArgb(0x33, 0x4A, 0x90, 0xE2));

		/// <summary>占比条填充色。</summary>
		private static readonly IBrush BarFill = new SolidColorBrush(Color.FromArgb(0x80, 0x4A, 0x90, 0xE2));

		/// <summary>四色差异标注：低对比、带透明度，浅深主题都可读。</summary>
		private static readonly IBrush DiffChanged = new SolidColorBrush(Color.FromArgb(0x2E, 0xE6, 0xA2, 0x3C));

		private static readonly IBrush DiffLeft = new SolidColorBrush(Color.FromArgb(0x26, 0x4A, 0x90, 0xE2));

		private static readonly IBrush DiffRight = new SolidColorBrush(Color.FromArgb(0x26, 0xE0, 0x5A, 0x5A));

		private static readonly IBrush DiffSame = new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00));

		/// <summary>等宽字体（表名 / 码位等定宽文本用），按可用性回退。</summary>
		private static readonly FontFamily MonoFont = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, Courier New, monospace");

		private static readonly int[] SampleSizes = new int[4] { 12, 18, 28, 48 };

		/// <summary>固定样张文本（刻意不含中文，CI 无 CJK 字体会渲染成豆腐块）。</summary>
		private static readonly string[] SampleLines = new string[3]
		{
			"Hamburger 0123 ABCabc",
			"The quick brown fox jumps",
			"0O1lI .,;:!?"
		};

		/// <summary>与解析器一致的码位上限（截断说明用）。</summary>
		private const int MaxCodepoints = 65536;

		private static readonly string[] TableTags = new string[9]
		{
			"head",
			"name",
			"OS/2",
			"maxp",
			"hhea",
			"cmap",
			"kern",
			"GPOS",
			"GSUB"
		};

		private readonly Grid _root;

		private readonly TextBlock _srcTitle;

		private readonly TextBlock _dstTitle;

		private readonly TextBlock _status;

		private readonly Button _sampleButton;

		private readonly Button _metadataButton;

		private readonly Button _codepointsButton;

		private readonly TextBlock _srcFaceLabel;

		private readonly TextBlock _dstFaceLabel;

		private readonly ComboBox _srcFaceBox;

		private readonly ComboBox _dstFaceBox;

		private readonly ContentControl _content;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （metadata / codepoints，其余走默认样张），据此逐模式取图；正常运行时该变量为空。
		/// </summary>
		private static readonly ViewMode InitialMode = ResolveInitialMode();

		private static ViewMode ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			if (string.Equals(forced, "metadata", StringComparison.OrdinalIgnoreCase))
			{
				return ViewMode.Metadata;
			}
			if (string.Equals(forced, "codepoints", StringComparison.OrdinalIgnoreCase))
			{
				return ViewMode.Codepoints;
			}
			return ViewMode.Sample;
		}

		private ViewMode _mode = InitialMode;

		private FontModel _srcModel;

		private FontModel _dstModel;

		private int _srcFaceIndex;

		private int _dstFaceIndex;

		private int _renderGeneration;

		private bool _released;

		private bool _updatingSelectors;

		public FontDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			Grid header = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,*"),
			};
			Grid.SetColumn(_srcTitle, 0);
			header.Children.Add(_srcTitle);
			Grid.SetColumn(_dstTitle, 1);
			header.Children.Add(_dstTitle);

			_status = new TextBlock
			{
				Margin = new Thickness(12.0, 2.0, 12.0, 4.0),
				FontSize = 12.0,
				Opacity = 0.75,
				TextWrapping = TextWrapping.Wrap,
			};

			// 模式工具栏：自建三段式切换（当前项高亮）+ 集合字体的「第几套」选择。
			_sampleButton = NewModeButton("Sample", ViewMode.Sample);
			_metadataButton = NewModeButton("Metadata", ViewMode.Metadata);
			_codepointsButton = NewModeButton("Codepoints", ViewMode.Codepoints);
			StackPanel modes = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				VerticalAlignment = VerticalAlignment.Center,
			};
			modes.Children.Add(_sampleButton);
			modes.Children.Add(_metadataButton);
			modes.Children.Add(_codepointsButton);

			_srcFaceLabel = NewFaceLabel();
			_dstFaceLabel = NewFaceLabel();
			_srcFaceBox = NewFaceBox();
			_dstFaceBox = NewFaceBox();
			_srcFaceBox.SelectionChanged += OnSrcFaceChanged;
			_dstFaceBox.SelectionChanged += OnDstFaceChanged;
			StackPanel faces = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Margin = new Thickness(18.0, 0.0, 0.0, 0.0),
				VerticalAlignment = VerticalAlignment.Center,
			};
			faces.Children.Add(_srcFaceLabel);
			faces.Children.Add(_srcFaceBox);
			faces.Children.Add(_dstFaceLabel);
			faces.Children.Add(_dstFaceBox);

			StackPanel toolbar = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Margin = new Thickness(12.0, 0.0, 12.0, 8.0),
			};
			toolbar.Children.Add(modes);
			toolbar.Children.Add(faces);

			_content = new ContentControl
			{
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				VerticalContentAlignment = VerticalAlignment.Stretch,
			};
			ScrollViewer scroll = new ScrollViewer
			{
				Content = _content,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			};
			Border contentHost = new Border
			{
				BorderBrush = GridLine,
				BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
				Child = scroll,
			};

			_root = new Grid
			{
				RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
			};
			Grid.SetRow(header, 0);
			_root.Children.Add(header);
			Grid.SetRow(_status, 1);
			_root.Children.Add(_status);
			Grid.SetRow(toolbar, 2);
			_root.Children.Add(toolbar);
			Grid.SetRow(contentHost, 3);
			_root.Children.Add(contentHost);

			UpdateStaticTexts();
			PluginEnvironment.ApplyLocalization(_root);
		}

		// ---- IDiffView ----

		Control IDiffView.View => _root;

		/// <summary>本视图自带模式工具条，不需要宿主渲染模式切换工具条。</summary>
		IReadOnlyList<DiffViewMode> IDiffView.Modes => Array.Empty<DiffViewMode>();

		/// <summary>无像素级差异高亮能力（显式空实现，避免 CS0067）。</summary>
		event EventHandler<bool> IDiffView.HighlightPixelsAvailableChanged
		{
			add { }
			remove { }
		}

		public void SetContent(DiffViewContext context, IDiffViewHost host)
		{
			_context = context;
			_host = host;
			_srcTitle.Foreground = context?.SrcTitleBrush;
			_dstTitle.Foreground = context?.DstTitleBrush;
			_srcFaceIndex = 0;
			_dstFaceIndex = 0;
			_srcModel = null;
			_dstModel = null;
			// CI 截图定位用：日志里必须出现 FontDiffView.SetContent。
			PluginLog.Info($"FontDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			StartRender();
		}

		/// <summary>切换视图模式（支持 sample / metadata / codepoints；其余忽略）。</summary>
		public void SetMode(string modeId)
		{
			ViewMode mode;
			switch (modeId)
			{
				case "sample":
					mode = ViewMode.Sample;
					break;
				case "metadata":
					mode = ViewMode.Metadata;
					break;
				case "codepoints":
					mode = ViewMode.Codepoints;
					break;
				default:
					return;
			}
			if (mode == _mode)
			{
				return;
			}
			_mode = mode;
			UpdateToolbar();
			StartRender();
		}

		public void Activate()
		{
		}

		public void Deactivate()
		{
		}

		/// <summary>
		/// 应用当前语言（宿主在语言切换时广播）：宿主 key 重刷 + 插件自带静态文案重算 +
		/// 复用同一渲染入口（沿用代次 + CancellationToken 取消上一轮）重跑内容区文案。
		/// </summary>
		public void ApplyLocalization()
		{
			PluginEnvironment.ApplyLocalization(_root);
			UpdateStaticTexts();
			StartRender();
		}

		public void Release()
		{
			_released = true;
			CancelRender();
			_context = null;
			_host = null;
			_srcModel = null;
			_dstModel = null;
			_content.Content = null;
			_srcTitle.Text = string.Empty;
			_dstTitle.Text = string.Empty;
			_status.Text = string.Empty;
			_srcFaceBox.ItemsSource = null;
			_dstFaceBox.ItemsSource = null;
		}

		// ---- 工具栏 ----

		private Button NewModeButton(string key, ViewMode mode)
		{
			Button button = new Button
			{
				Content = FontStrings.T(key),
				// 宿主 Button 主题固定 Height=24，垂直 padding 合计须 ≤4px；加厚的观感交给 MinHeight 抬高。
				Padding = new Thickness(12.0, 0.0, 12.0, 0.0),
				MinHeight = 28.0,
				Tag = mode,
				VerticalAlignment = VerticalAlignment.Center,
			};
			button.Click += OnModeClicked;
			return button;
		}

		private static TextBlock NewFaceLabel()
		{
			return new TextBlock
			{
				FontSize = 12.0,
				Opacity = 0.7,
				VerticalAlignment = VerticalAlignment.Center,
				IsVisible = false,
			};
		}

		private static ComboBox NewFaceBox()
		{
			return new ComboBox
			{
				MinWidth = 180.0,
				VerticalAlignment = VerticalAlignment.Center,
				IsVisible = false,
			};
		}

		private void OnModeClicked(object sender, RoutedEventArgs e)
		{
			if (sender is Button { Tag: ViewMode mode } && mode != _mode)
			{
				_mode = mode;
				UpdateToolbar();
				StartRender();
			}
		}

		private void OnSrcFaceChanged(object sender, SelectionChangedEventArgs e)
		{
			if (_updatingSelectors || _srcFaceBox.SelectedIndex < 0 || _srcFaceBox.SelectedIndex == _srcFaceIndex)
			{
				return;
			}
			_srcFaceIndex = _srcFaceBox.SelectedIndex;
			StartRender();
		}

		private void OnDstFaceChanged(object sender, SelectionChangedEventArgs e)
		{
			if (_updatingSelectors || _dstFaceBox.SelectedIndex < 0 || _dstFaceBox.SelectedIndex == _dstFaceIndex)
			{
				return;
			}
			_dstFaceIndex = _dstFaceBox.SelectedIndex;
			StartRender();
		}

		/// <summary>按当前语言重算工具栏与标题等静态文案（构造后、语言切换都会走）。</summary>
		private void UpdateStaticTexts()
		{
			UpdateToolbar();
			UpdateTitles();
		}

		private void UpdateToolbar()
		{
			_sampleButton.Content = FontStrings.T("Sample");
			_metadataButton.Content = FontStrings.T("Metadata");
			_codepointsButton.Content = FontStrings.T("Codepoints");
			_srcFaceLabel.Text = PluginEnvironment.Translate(RoleKey(_context?.SrcRole ?? DiffSideRole.Old));
			_dstFaceLabel.Text = PluginEnvironment.Translate(RoleKey(_context?.DstRole ?? DiffSideRole.New));
			StyleModeButton(_sampleButton, _mode == ViewMode.Sample);
			StyleModeButton(_metadataButton, _mode == ViewMode.Metadata);
			StyleModeButton(_codepointsButton, _mode == ViewMode.Codepoints);
		}

		private static void StyleModeButton(Button button, bool active)
		{
			button.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
			button.Background = active ? ActiveTint : Brushes.Transparent;
		}

		// ---- 渲染管线 ----

		private void StartRender()
		{
			CancelRender();
			_content.Content = null;
			UpdateTitles();
			UpdateToolbar();
			if (_context == null)
			{
				return;
			}
			// 样张模式才提示「正在渲染样张」，其它模式留空由内容区自述。
			_status.Text = _mode == ViewMode.Sample ? FontStrings.T("Rendering samples…") : string.Empty;
			CancellationTokenSource cts = new CancellationTokenSource();
			_cts = cts;
			int generation = _renderGeneration;
			ViewMode mode = _mode;
			int srcFace = _srcFaceIndex;
			int dstFace = _dstFaceIndex;
			DiffViewContext context = _context;
			IDiffViewHost host = _host;
			Task.Run(() => RenderAsync(context, host, mode, srcFace, dstFace, cts.Token, generation));
		}

		private async Task RenderAsync(DiffViewContext context, IDiffViewHost host, ViewMode mode, int srcFace, int dstFace, CancellationToken token, int generation)
		{
			try
			{
				byte[] srcBytes = await ResolveSideAsync(context?.Src, context?.HexSrc, host, token).ConfigureAwait(false);
				byte[] dstBytes = await ResolveSideAsync(context?.Dst, context?.HexDst, host, token).ConfigureAwait(false);
				if (token.IsCancellationRequested)
				{
					return;
				}
				FontModel srcModel = srcBytes == null ? null : FontParser.Parse(context?.Src?.Path ?? context?.FilePath, srcBytes);
				FontModel dstModel = dstBytes == null ? null : FontParser.Parse(context?.Dst?.Path ?? context?.FilePath, dstBytes);
				if (token.IsCancellationRequested)
				{
					return;
				}
				int srcIndex = ClampFace(srcModel, srcFace);
				int dstIndex = ClampFace(dstModel, dstFace);
				Dictionary<int, byte[]> srcPng = null;
				Dictionary<int, byte[]> dstPng = null;
				if (mode == ViewMode.Sample)
				{
					if (srcBytes != null)
					{
						srcPng = RenderSamples(srcBytes, srcIndex);
					}
					if (dstBytes != null)
					{
						dstPng = RenderSamples(dstBytes, dstIndex);
					}
				}
				if (token.IsCancellationRequested)
				{
					return;
				}
				string status = BuildStatus(context, srcModel, dstModel, srcBytes, dstBytes);
				Dispatcher.UIThread.Post(delegate
				{
					if (_released || generation != _renderGeneration)
					{
						return;
					}
					_srcModel = srcModel;
					_dstModel = dstModel;
					_srcFaceIndex = srcIndex;
					_dstFaceIndex = dstIndex;
					UpdateFaceSelectors();
					_status.Text = status;
					BuildContent(mode, srcBytes, dstBytes, srcPng, dstPng);
				});
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				PluginLog.Error("FontDiffView render failed", ex);
				PostStatus(generation, FontStrings.T("Failed to parse font") + ": " + ex.Message);
			}
		}

		/// <summary>取一侧字节：宿主 Hex 预载 &gt; 侧内容懒加载 &gt; LFS 缓存 / smudge。</summary>
		private static async Task<byte[]> ResolveSideAsync(DiffSideContent side, MemoryStream hex, IDiffViewHost host, CancellationToken token)
		{
			if (side == null)
			{
				return null;
			}
			if (hex != null && hex.Length > 0)
			{
				return hex.ToArray();
			}
			MemoryStream data = null;
			try
			{
				data = side.Data;
			}
			catch (Exception ex)
			{
				PluginLog.Warn("Font: failed to load side bytes for '" + side.Path + "'", ex);
			}
			if (data != null && data.Length > 0)
			{
				return data.ToArray();
			}
			if (side.Lfs == null || host == null)
			{
				return null;
			}
			MemoryStream cached = null;
			try
			{
				cached = host.GetCachedLfsData(side.Lfs);
			}
			catch (Exception ex)
			{
				PluginLog.Warn("Font: LFS cache lookup failed", ex);
			}
			if (cached != null && cached.Length > 0)
			{
				return cached.ToArray();
			}
			// 缓存未命中：启动 LFS smudge，宿主在 UI 线程回调，用 TCS 桥回后台线程。
			TaskCompletionSource<byte[]> tcs = new TaskCompletionSource<byte[]>();
			IDisposable subscription = null;
			CancellationTokenRegistration registration = token.Register(delegate
			{
				subscription?.Dispose();
				tcs.TrySetCanceled();
			});
			try
			{
				subscription = host.RunLfsSmudge(side.Lfs, null, delegate(LfsSmudgeResult result)
				{
					if (result != null && result.Succeeded && result.Data != null)
					{
						tcs.TrySetResult(result.Data.ToArray());
					}
					else
					{
						tcs.TrySetResult(null);
					}
				});
				return await tcs.Task.ConfigureAwait(false);
			}
			finally
			{
				registration.Dispose();
				subscription?.Dispose();
			}
		}

		private void PostStatus(int generation, string text)
		{
			Dispatcher.UIThread.Post(delegate
			{
				if (!_released && generation == _renderGeneration)
				{
					_status.Text = text;
				}
			});
		}

		private void CancelRender()
		{
			_renderGeneration++;
			CancellationTokenSource cts = _cts;
			_cts = null;
			if (cts != null)
			{
				try
				{
					cts.Cancel();
				}
				catch (ObjectDisposedException)
				{
				}
				cts.Dispose();
			}
		}

		// ---- 内容区装配 ----

		private void BuildContent(ViewMode mode, byte[] srcBytes, byte[] dstBytes, Dictionary<int, byte[]> srcPng, Dictionary<int, byte[]> dstPng)
		{
			switch (mode)
			{
				case ViewMode.Sample:
					_content.Content = BuildSampleContent(srcBytes, dstBytes, srcPng, dstPng);
					break;
				case ViewMode.Metadata:
					_content.Content = BuildMetadataContent(srcBytes, dstBytes);
					break;
				default:
					_content.Content = BuildCodepointsContent(srcBytes, dstBytes);
					break;
			}
		}

		// ---- 样张模式 ----

		private Control BuildSampleContent(byte[] srcBytes, byte[] dstBytes, Dictionary<int, byte[]> srcPng, Dictionary<int, byte[]> dstPng)
		{
			string srcNote = SideNote(_context?.Src, srcBytes);
			string dstNote = SideNote(_context?.Dst, dstBytes);
			Grid grid = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("Auto,*,*"),
				RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
				Margin = new Thickness(12.0, 0.0, 12.0, 12.0),
			};
			TextBlock srcHeader = Label(PluginEnvironment.Translate(RoleKey(_context?.SrcRole ?? DiffSideRole.Old)), 11.5, FontWeight.SemiBold, 0.7);
			Grid.SetRow(srcHeader, 0);
			Grid.SetColumn(srcHeader, 1);
			grid.Children.Add(srcHeader);
			TextBlock dstHeader = Label(PluginEnvironment.Translate(RoleKey(_context?.DstRole ?? DiffSideRole.New)), 11.5, FontWeight.SemiBold, 0.7);
			Grid.SetRow(dstHeader, 0);
			Grid.SetColumn(dstHeader, 2);
			grid.Children.Add(dstHeader);

			for (int i = 0; i < SampleSizes.Length; i++)
			{
				int size = SampleSizes[i];
				int row = i + 1;
				TextBlock sizeLabel = Label(FontStrings.F("{0} px", size), 11.5, FontWeight.Normal, 0.6);
				sizeLabel.FontFamily = MonoFont;
				sizeLabel.Margin = new Thickness(0.0, 0.0, 12.0, 0.0);
				Grid.SetRow(sizeLabel, row);
				Grid.SetColumn(sizeLabel, 0);
				grid.Children.Add(sizeLabel);

				Control srcCell = SampleCell(srcNote, srcPng, size);
				Grid.SetRow(srcCell, row);
				Grid.SetColumn(srcCell, 1);
				grid.Children.Add(srcCell);

				Control dstCell = SampleCell(dstNote, dstPng, size);
				Grid.SetRow(dstCell, row);
				Grid.SetColumn(dstCell, 2);
				grid.Children.Add(dstCell);
			}
			return grid;
		}

		private static Control SampleCell(string note, Dictionary<int, byte[]> png, int size)
		{
			if (note != null)
			{
				return NoteText(note, new Thickness(0.0, 2.0, 12.0, 8.0));
			}
			if (png == null || !png.TryGetValue(size, out byte[] bytes) || bytes == null || bytes.Length == 0)
			{
				return NoteText(FontStrings.T("Failed to render samples"), new Thickness(0.0, 2.0, 12.0, 8.0));
			}
			Bitmap bitmap = DecodeBitmap(bytes);
			if (bitmap == null)
			{
				return NoteText(FontStrings.T("Failed to render samples"), new Thickness(0.0, 2.0, 12.0, 8.0));
			}
			return new Image
			{
				Source = bitmap,
				Stretch = Stretch.None,
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(0.0, 2.0, 12.0, 8.0),
			};
		}

		/// <summary>后台线程用 SkiaSharp 渲染某套字体在给定字号的样张位图（PNG 字节）。</summary>
		private static Dictionary<int, byte[]> RenderSamples(byte[] bytes, int faceIndex)
		{
			Dictionary<int, byte[]> result = new Dictionary<int, byte[]>();
			foreach (int size in SampleSizes)
			{
				try
				{
					result[size] = RenderSamplePng(bytes, faceIndex, SampleLines, size);
				}
				catch (Exception ex)
				{
					PluginLog.Warn("FontDiffView: sample render failed at size " + size, ex);
					result[size] = null;
				}
			}
			return result;
		}

		private static byte[] RenderSamplePng(byte[] bytes, int faceIndex, string[] lines, int size)
		{
			using (MemoryStream stream = new MemoryStream(bytes, false))
			using (SKTypeface typeface = SKTypeface.FromStream(stream, faceIndex))
			{
				if (typeface == null)
				{
					return null;
				}
				using (SKFont font = new SKFont(typeface, size))
				using (SKPaint paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
				{
					SKFontMetrics metrics = font.Metrics;
					float lineHeight = (float)size * 1.35f;
					float padding = (float)size * 0.25f + 2f;
					float width = 0f;
					foreach (string line in lines)
					{
						width = Math.Max(width, font.MeasureText(line, paint));
					}
					float textHeight = -metrics.Ascent + metrics.Descent + lineHeight * (lines.Length - 1);
					int bitmapWidth = Math.Max(1, (int)Math.Ceiling(width + padding * 2f));
					int bitmapHeight = Math.Max(1, (int)Math.Ceiling(textHeight + padding * 2f));
					SKImageInfo info = new SKImageInfo(bitmapWidth, bitmapHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
					using (SKBitmap bitmap = new SKBitmap(info))
					{
						using (SKCanvas canvas = new SKCanvas(bitmap))
						{
							canvas.Clear(SKColors.White);
							float baseline = padding - metrics.Ascent;
							for (int i = 0; i < lines.Length; i++)
							{
								canvas.DrawText(lines[i], padding, baseline + lineHeight * i, font, paint);
							}
						}
						using (SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
						{
							return data?.ToArray();
						}
					}
				}
			}
		}

		private static Bitmap DecodeBitmap(byte[] png)
		{
			try
			{
				using (MemoryStream stream = new MemoryStream(png, false))
				{
					return new Bitmap(stream);
				}
			}
			catch (Exception ex)
			{
				PluginLog.Warn("FontDiffView: failed to decode sample bitmap", ex);
				return null;
			}
		}

		// ---- 元数据模式 ----

		private Control BuildMetadataContent(byte[] srcBytes, byte[] dstBytes)
		{
			List<MetaRow> plan = BuildMetaPlan();
			Grid columns = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,*"),
			};
			Control left = BuildMetaColumn(plan, true);
			Grid.SetColumn(left, 0);
			columns.Children.Add(left);
			Control right = BuildMetaColumn(plan, false);
			Grid.SetColumn(right, 1);
			columns.Children.Add(right);

			StackPanel root = new StackPanel
			{
				Margin = new Thickness(12.0, 0.0, 12.0, 12.0),
			};
			string srcNote = SideNote(_context?.Src, srcBytes);
			if (srcNote != null)
			{
				root.Children.Add(NoteText(PluginEnvironment.Translate(RoleKey(_context?.SrcRole ?? DiffSideRole.Old)) + ": " + srcNote, new Thickness(0.0, 0.0, 0.0, 6.0)));
			}
			string dstNote = SideNote(_context?.Dst, dstBytes);
			if (dstNote != null)
			{
				root.Children.Add(NoteText(PluginEnvironment.Translate(RoleKey(_context?.DstRole ?? DiffSideRole.New)) + ": " + dstNote, new Thickness(0.0, 0.0, 0.0, 6.0)));
			}
			root.Children.Add(columns);
			return root;
		}

		private List<MetaRow> BuildMetaPlan()
		{
			FontFace left = CurrentFace(_srcModel, _srcFaceIndex);
			FontFace right = CurrentFace(_dstModel, _dstFaceIndex);
			List<MetaRow> rows = new List<MetaRow>();

			// Identity
			AddRow(rows, "Identity", "Format", FormatValue(_srcModel), FormatValue(_dstModel));
			AddRow(rows, "Identity", "Family", left?.Family, right?.Family);
			AddRow(rows, "Identity", "Subfamily", left?.Subfamily, right?.Subfamily);
			AddRow(rows, "Identity", "Full name", left?.FullName, right?.FullName);
			AddRow(rows, "Identity", "PostScript name", left?.PostScriptName, right?.PostScriptName);
			AddRow(rows, "Identity", "Version", left?.Version, right?.Version);
			AddRow(rows, "Identity", "Unique ID", left?.UniqueId, right?.UniqueId);
			AddRow(rows, "Identity", "Copyright", left?.Copyright, right?.Copyright);
			AddRow(rows, "Identity", "Trademark", left?.Trademark, right?.Trademark);

			// Vertical metrics
			AddRow(rows, "Vertical metrics", "Units per em", IntValue(left?.UnitsPerEm), IntValue(right?.UnitsPerEm));
			AddRow(rows, "Vertical metrics", "Typo ascender", IntValue(left?.TypoAscender), IntValue(right?.TypoAscender));
			AddRow(rows, "Vertical metrics", "Typo descender", IntValue(left?.TypoDescender), IntValue(right?.TypoDescender));
			AddRow(rows, "Vertical metrics", "Typo line gap", IntValue(left?.TypoLineGap), IntValue(right?.TypoLineGap));
			AddRow(rows, "Vertical metrics", "Win ascent", IntValue(left?.WinAscent), IntValue(right?.WinAscent));
			AddRow(rows, "Vertical metrics", "Win descent", IntValue(left?.WinDescent), IntValue(right?.WinDescent));
			AddRow(rows, "Vertical metrics", "hhea ascender", IntValue(left?.HheaAscender), IntValue(right?.HheaAscender));
			AddRow(rows, "Vertical metrics", "hhea descender", IntValue(left?.HheaDescender), IntValue(right?.HheaDescender));
			AddRow(rows, "Vertical metrics", "hhea line gap", IntValue(left?.HheaLineGap), IntValue(right?.HheaLineGap));

			// Weight & width
			AddRow(rows, "Weight & width", "Weight class", IntValue(left?.WeightClass), IntValue(right?.WeightClass));
			AddRow(rows, "Weight & width", "Width class", IntValue(left?.WidthClass), IntValue(right?.WidthClass));
			AddRow(rows, "Weight & width", "Selection", HexValue(left?.Selection), HexValue(right?.Selection));
			AddRow(rows, "Weight & width", "Mac style", HexValue(left?.MacStyle), HexValue(right?.MacStyle));

			// Tables
			AddRow(rows, "Tables", "Glyphs", IntValue(left?.NumGlyphs), IntValue(right?.NumGlyphs));
			AddRow(rows, "Tables", "Codepoints count", CodepointCount(left), CodepointCount(right));
			AddRow(rows, "Tables", "GSUB features", FeatureValue(left), FeatureValue(right));
			foreach (string tag in TableTags)
			{
				AddRow(rows, "Tables", tag, TableSize(left, tag), TableSize(right, tag));
			}
			AddRow(rows, "Tables", "Created", DateValue(left?.Created), DateValue(right?.Created));
			AddRow(rows, "Tables", "Modified", DateValue(left?.Modified), DateValue(right?.Modified));
			return rows;
		}

		private static void AddRow(List<MetaRow> rows, string group, string label, string left, string right)
		{
			string l = Normalize(left);
			string r = Normalize(right);
			MetaState state;
			if (l == null && r == null)
			{
				state = MetaState.Same;
			}
			else if (l == null)
			{
				state = MetaState.RightOnly;
			}
			else if (r == null)
			{
				state = MetaState.LeftOnly;
			}
			else
			{
				state = string.Equals(l, r, StringComparison.Ordinal) ? MetaState.Same : MetaState.Changed;
			}
			rows.Add(new MetaRow(group, label, left, right, state));
		}

		private Control BuildMetaColumn(List<MetaRow> plan, bool isLeft)
		{
			StackPanel panel = new StackPanel
			{
				Margin = isLeft ? new Thickness(0.0, 0.0, 10.0, 0.0) : new Thickness(10.0, 0.0, 0.0, 0.0),
			};
			StackPanel card = null;
			string group = null;
			foreach (MetaRow row in plan)
			{
				if (row.Group != group)
				{
					group = row.Group;
					card = new StackPanel();
					Border border = new Border
					{
						Background = Subtle,
						CornerRadius = new CornerRadius(6.0),
						Padding = new Thickness(10.0, 8.0, 10.0, 8.0),
						Margin = new Thickness(0.0, 0.0, 0.0, 10.0),
						Child = card,
					};
					card.Children.Add(new TextBlock
					{
						Text = FontStrings.T(group),
						FontWeight = FontWeight.SemiBold,
						FontSize = 12.0,
						Margin = new Thickness(0.0, 0.0, 0.0, 5.0),
					});
					panel.Children.Add(border);
				}
				card.Children.Add(BuildMetaRowView(row, isLeft));
			}
			return panel;
		}

		private static Control BuildMetaRowView(MetaRow row, bool isLeft)
		{
			string value = isLeft ? row.Left : row.Right;
			bool present = !string.IsNullOrEmpty(value);
			Grid grid = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,Auto"),
				Background = StateBrush(row.State),
				Margin = new Thickness(0.0, 1.0, 0.0, 1.0),
			};
			TextBlock text = new TextBlock
			{
				Text = FontStrings.T(row.Label) + " : " + (present ? value : FontStrings.T("not present")),
				FontSize = 11.5,
				Opacity = present ? 0.9 : 0.55,
				TextWrapping = TextWrapping.Wrap,
				VerticalAlignment = VerticalAlignment.Center,
			};
			Grid.SetColumn(text, 0);
			grid.Children.Add(text);
			Border chip = Chip(Label(FontStrings.T(StateKey(row.State)), 10.5, FontWeight.Normal, 0.8), ChipTint);
			chip.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
			Grid.SetColumn(chip, 1);
			grid.Children.Add(chip);
			return grid;
		}

		// ---- 码位模式 ----

		private Control BuildCodepointsContent(byte[] srcBytes, byte[] dstBytes)
		{
			FontFace left = CurrentFace(_srcModel, _srcFaceIndex);
			FontFace right = CurrentFace(_dstModel, _dstFaceIndex);
			List<int> leftAll = Sorted(left?.Codepoints);
			List<int> rightAll = Sorted(right?.Codepoints);
			HashSet<int> leftSet = new HashSet<int>(leftAll);
			HashSet<int> rightSet = new HashSet<int>(rightAll);
			int leftCount = leftAll.Count;
			int rightCount = rightAll.Count;
			int max = Math.Max(Math.Max(leftCount, rightCount), 1);

			StackPanel root = new StackPanel
			{
				Margin = new Thickness(12.0, 0.0, 12.0, 12.0),
			};
			string srcNote = SideNote(_context?.Src, srcBytes);
			if (srcNote != null)
			{
				root.Children.Add(NoteText(PluginEnvironment.Translate(RoleKey(_context?.SrcRole ?? DiffSideRole.Old)) + ": " + srcNote, new Thickness(0.0, 0.0, 0.0, 6.0)));
			}
			string dstNote = SideNote(_context?.Dst, dstBytes);
			if (dstNote != null)
			{
				root.Children.Add(NoteText(PluginEnvironment.Translate(RoleKey(_context?.DstRole ?? DiffSideRole.New)) + ": " + dstNote, new Thickness(0.0, 0.0, 0.0, 6.0)));
			}

			root.Children.Add(BuildCoverageBar(PluginEnvironment.Translate(RoleKey(_context?.SrcRole ?? DiffSideRole.Old)), leftCount, max));
			root.Children.Add(BuildCoverageBar(PluginEnvironment.Translate(RoleKey(_context?.DstRole ?? DiffSideRole.New)), rightCount, max));

			List<int> common = new List<int>();
			List<int> leftOnly = new List<int>();
			foreach (int cp in leftAll)
			{
				if (rightSet.Contains(cp))
				{
					common.Add(cp);
				}
				else
				{
					leftOnly.Add(cp);
				}
			}
			List<int> rightOnly = new List<int>();
			foreach (int cp in rightAll)
			{
				if (!leftSet.Contains(cp))
				{
					rightOnly.Add(cp);
				}
			}
			root.Children.Add(StatLine(FontStrings.F("Common: {0}", common.Count), common));
			root.Children.Add(StatLine(FontStrings.F("Only in left: {0}", leftOnly.Count), leftOnly));
			root.Children.Add(StatLine(FontStrings.F("Only in right: {0}", rightOnly.Count), rightOnly));
			if (left?.CodepointsTruncated == true || right?.CodepointsTruncated == true)
			{
				root.Children.Add(NoteText(FontStrings.F("Showing first {0} codepoints.", MaxCodepoints), new Thickness(0.0, 4.0, 0.0, 0.0)));
			}

			root.Children.Add(new TextBlock
			{
				Text = FontStrings.T("Unicode blocks"),
				FontWeight = FontWeight.SemiBold,
				FontSize = 12.0,
				Margin = new Thickness(0.0, 12.0, 0.0, 5.0),
			});
			root.Children.Add(BuildBlockTable(leftSet, rightSet));
			return root;
		}

		private static Control BuildCoverageBar(string label, int count, int max)
		{
			Grid track = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions(count + "*, " + (max - count) + "*"),
			};
			Border fill = new Border
			{
				Background = BarFill,
				CornerRadius = new CornerRadius(3.0),
			};
			Grid.SetColumn(fill, 0);
			track.Children.Add(fill);
			Border bar = new Border
			{
				Height = 20.0,
				BorderBrush = GridLine,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(4.0),
				Child = track,
			};
			StackPanel panel = new StackPanel
			{
				Spacing = 3.0,
				Margin = new Thickness(0.0, 0.0, 0.0, 6.0),
			};
			panel.Children.Add(Label(label + "  ·  " + FontStrings.F("Coverage: {0} codepoints", count), 12.0, FontWeight.SemiBold, 0.85));
			panel.Children.Add(bar);
			return panel;
		}

		private static Control StatLine(string text, List<int> examples)
		{
			StackPanel line = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 8.0,
				Margin = new Thickness(0.0, 1.0, 0.0, 1.0),
			};
			line.Children.Add(Label(text, 12.0, FontWeight.Normal, 0.85));
			if (examples.Count > 0)
			{
				TextBlock samples = new TextBlock
				{
					Text = FormatCodepoints(examples, 16),
					FontFamily = MonoFont,
					FontSize = 11.0,
					Opacity = 0.6,
					TextWrapping = TextWrapping.Wrap,
					VerticalAlignment = VerticalAlignment.Center,
				};
				line.Children.Add(samples);
			}
			return line;
		}

		private Control BuildBlockTable(HashSet<int> leftSet, HashSet<int> rightSet)
		{
			string[] order = UnicodeBlocks.Order;
			int count = order.Length;
			Dictionary<string, int> index = new Dictionary<string, int>();
			for (int i = 0; i < count; i++)
			{
				index[order[i]] = i;
			}
			int[] leftCounts = new int[count];
			int[] rightCounts = new int[count];
			foreach (int cp in leftSet)
			{
				leftCounts[index[UnicodeBlocks.Classify(cp)]]++;
			}
			foreach (int cp in rightSet)
			{
				rightCounts[index[UnicodeBlocks.Classify(cp)]]++;
			}
			string[] rows = new string[count + 1];
			for (int i = 0; i < rows.Length; i++)
			{
				rows[i] = "Auto";
			}
			Grid grid = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
				RowDefinitions = new RowDefinitions(string.Join(",", rows)),
			};
			string leftRole = PluginEnvironment.Translate(RoleKey(_context?.SrcRole ?? DiffSideRole.Old));
			string rightRole = PluginEnvironment.Translate(RoleKey(_context?.DstRole ?? DiffSideRole.New));
			AddBlockCell(grid, 0, 0, FontStrings.T("Block"), FontWeight.SemiBold, Subtle);
			AddBlockCell(grid, 0, 1, leftRole, FontWeight.SemiBold, Subtle);
			AddBlockCell(grid, 0, 2, rightRole, FontWeight.SemiBold, Subtle);
			AddBlockCell(grid, 0, 3, string.Empty, FontWeight.SemiBold, Subtle);
			for (int i = 0; i < count; i++)
			{
				int leftValue = leftCounts[i];
				int rightValue = rightCounts[i];
				MetaState state = CountState(leftValue, rightValue);
				IBrush brush = StateBrush(state);
				int row = i + 1;
				AddBlockCell(grid, row, 0, FontStrings.T(order[i]), FontWeight.Normal, brush);
				AddBlockCell(grid, row, 1, leftValue.ToString(CultureInfo.InvariantCulture), FontWeight.Normal, brush);
				AddBlockCell(grid, row, 2, rightValue.ToString(CultureInfo.InvariantCulture), FontWeight.Normal, brush);
				Control chip = Chip(Label(FontStrings.T(StateKey(state)), 10.5, FontWeight.Normal, 0.8), ChipTint);
				Grid.SetRow(chip, row);
				Grid.SetColumn(chip, 3);
				grid.Children.Add(chip);
			}
			return grid;
		}

		private static void AddBlockCell(Grid grid, int row, int column, string text, FontWeight weight, IBrush background)
		{
			Border cell = new Border
			{
				Background = background,
				Padding = new Thickness(6.0, 3.0, 6.0, 3.0),
				Child = new TextBlock
				{
					Text = text,
					FontSize = 11.5,
					FontWeight = weight,
					TextWrapping = TextWrapping.NoWrap,
					VerticalAlignment = VerticalAlignment.Center,
				},
			};
			Grid.SetRow(cell, row);
			Grid.SetColumn(cell, column);
			grid.Children.Add(cell);
		}

		private static MetaState CountState(int left, int right)
		{
			if (left == right)
			{
				return MetaState.Same;
			}
			if (left == 0)
			{
				return MetaState.RightOnly;
			}
			if (right == 0)
			{
				return MetaState.LeftOnly;
			}
			return MetaState.Changed;
		}

		private static string StateKey(MetaState state)
		{
			switch (state)
			{
				case MetaState.Changed:
					return "changed";
				case MetaState.LeftOnly:
					return "left only";
				case MetaState.RightOnly:
					return "right only";
				default:
					return "same";
			}
		}

		private static IBrush StateBrush(MetaState state)
		{
			switch (state)
			{
				case MetaState.Changed:
					return DiffChanged;
				case MetaState.LeftOnly:
					return DiffLeft;
				case MetaState.RightOnly:
					return DiffRight;
				default:
					return DiffSame;
			}
		}

		// ---- 取值 / 描述辅助 ----

		private static FontFace CurrentFace(FontModel model, int index)
		{
			if (model == null || model.Faces.Count == 0)
			{
				return null;
			}
			if (index < 0 || index >= model.Faces.Count)
			{
				return model.Faces[0];
			}
			return model.Faces[index];
		}

		private static int ClampFace(FontModel model, int index)
		{
			int count = model?.Faces.Count ?? 0;
			if (count <= 0)
			{
				return 0;
			}
			return index < 0 || index >= count ? 0 : index;
		}

		private static string FormatValue(FontModel model)
		{
			return model == null ? null : FontStrings.T(model.FormatKey);
		}

		private static string IntValue(int? value)
		{
			return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : null;
		}

		private static string HexValue(int? value)
		{
			return value.HasValue ? "0x" + value.Value.ToString("X4", CultureInfo.InvariantCulture) : null;
		}

		private static string DateValue(DateTime? value)
		{
			return value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : null;
		}

		private static string CodepointCount(FontFace face)
		{
			return face == null ? null : face.Codepoints.Count.ToString(CultureInfo.InvariantCulture);
		}

		private static string TableSize(FontFace face, string tag)
		{
			if (face == null)
			{
				return null;
			}
			return face.TableSizes.TryGetValue(tag, out int size) ? size.ToString(CultureInfo.InvariantCulture) + " B" : null;
		}

		private static string FeatureValue(FontFace face)
		{
			if (face == null || !face.HasTable("GSUB"))
			{
				return null;
			}
			return face.GsubFeatures.Count == 0 ? "-" : string.Join(", ", face.GsubFeatures);
		}

		private static string Normalize(string value)
		{
			return string.IsNullOrWhiteSpace(value) ? null : value;
		}

		private static List<int> Sorted(ICollection<int> source)
		{
			List<int> list = source == null ? new List<int>() : new List<int>(source);
			list.Sort();
			return list;
		}

		private static string FormatCodepoints(List<int> values, int limit)
		{
			int count = Math.Min(values.Count, limit);
			List<string> parts = new List<string>(count + 1);
			for (int i = 0; i < count; i++)
			{
				int cp = values[i];
				parts.Add(cp <= 0xFFFF ? "U+" + cp.ToString("X4", CultureInfo.InvariantCulture) : "U+" + cp.ToString("X5", CultureInfo.InvariantCulture));
			}
			string text = string.Join(" ", parts);
			return values.Count > limit ? text + " …" : text;
		}

		/// <summary>某侧不可用时的提示（整侧缺失 / 拿不到字节）；可用返回 null。</summary>
		private static string SideNote(DiffSideContent side, byte[] bytes)
		{
			if (side == null)
			{
				return FontStrings.T("not present");
			}
			if (bytes == null)
			{
				return FontStrings.T("Font content unavailable") + Environment.NewLine + side.Path;
			}
			return null;
		}

		private static string DescribeMissing(DiffViewContext context, byte[] srcBytes, byte[] dstBytes)
		{
			List<string> notes = new List<string>();
			if (context?.Src == null || srcBytes == null)
			{
				notes.Add(PluginEnvironment.Translate(RoleKey(context?.SrcRole ?? DiffSideRole.Old)) + ": " + DescribeSide(context?.Src, srcBytes));
			}
			if (context?.Dst == null || dstBytes == null)
			{
				notes.Add(PluginEnvironment.Translate(RoleKey(context?.DstRole ?? DiffSideRole.New)) + ": " + DescribeSide(context?.Dst, dstBytes));
			}
			return string.Join("  ·  ", notes);
		}

		private static string DescribeSide(DiffSideContent side, byte[] bytes)
		{
			if (side == null)
			{
				return FontStrings.T("not present");
			}
			if (bytes == null)
			{
				return FontStrings.T("Font content unavailable");
			}
			return string.Empty;
		}

		private static string DescribeModelError(FontModel model)
		{
			switch (model.Error)
			{
				case FontError.Empty:
					return FontStrings.T("Empty font file");
				case FontError.BadMagic:
					return FontStrings.T("Unsupported font format");
				case FontError.Unsupported:
					return model.ErrorDetail == "WOFF2" ? FontStrings.T("WOFF2 not supported") : FontStrings.T("Unsupported font format");
				case FontError.Failed:
					return FontStrings.T("Failed to parse font")
						+ (string.IsNullOrEmpty(model.ErrorDetail) ? string.Empty : Environment.NewLine + model.ErrorDetail);
				default:
					return null;
			}
		}

		private static string BuildStatus(DiffViewContext context, FontModel srcModel, FontModel dstModel, byte[] srcBytes, byte[] dstBytes)
		{
			string text = FontStrings.F("Font compare: {0} / {1} faces", srcModel?.Faces.Count ?? 0, dstModel?.Faces.Count ?? 0);
			if (srcModel != null)
			{
				string srcFormat = DescribeModelError(srcModel) ?? FontStrings.T(srcModel.FormatKey);
				string dstFormat = dstModel == null ? "-" : (DescribeModelError(dstModel) ?? FontStrings.T(dstModel.FormatKey));
				text = text + "  ·  " + srcFormat + " / " + dstFormat;
			}
			string missing = DescribeMissing(context, srcBytes, dstBytes);
			if (!string.IsNullOrEmpty(missing))
			{
				text = text + "  ·  " + missing;
			}
			return text;
		}

		// ---- 通用控件辅助 ----

		private static TextBlock NewTitle()
		{
			return new TextBlock
			{
				Margin = new Thickness(12.0, 8.0, 12.0, 4.0),
				FontWeight = FontWeight.SemiBold,
				TextTrimming = TextTrimming.CharacterEllipsis,
			};
		}

		private static Border Chip(Control child, IBrush background)
		{
			return new Border
			{
				Background = background,
				CornerRadius = new CornerRadius(4.0),
				Padding = new Thickness(7.0, 2.0, 7.0, 2.0),
				VerticalAlignment = VerticalAlignment.Center,
				Child = child,
			};
		}

		private static TextBlock Label(string text, double size, FontWeight weight, double opacity)
		{
			return new TextBlock
			{
				Text = text,
				FontSize = size,
				FontWeight = weight,
				Opacity = opacity,
				TextWrapping = TextWrapping.NoWrap,
				VerticalAlignment = VerticalAlignment.Center,
			};
		}

		private static TextBlock NoteText(string text, Thickness margin)
		{
			return new TextBlock
			{
				Text = text,
				FontSize = 12.0,
				Opacity = 0.7,
				TextWrapping = TextWrapping.Wrap,
				Margin = margin,
			};
		}

		private void UpdateTitles()
		{
			_srcTitle.Text = DescribeSideTitle(_context?.Src, _context?.SrcRole ?? DiffSideRole.Old);
			_dstTitle.Text = DescribeSideTitle(_context?.Dst, _context?.DstRole ?? DiffSideRole.New);
		}

		private static string DescribeSideTitle(DiffSideContent side, DiffSideRole role)
		{
			string text = PluginEnvironment.Translate(RoleKey(role));
			if (side == null)
			{
				// 该侧在本次对比里不存在（新增 / 删除），标题也点明，避免空栏看起来像解析失败。
				return text + "  ·  " + FontStrings.T("not present");
			}
			if (!string.IsNullOrEmpty(side.Path))
			{
				text = text + "  ·  " + Path.GetFileName(side.Path);
			}
			if (side.Size.HasValue)
			{
				text = text + "  ·  " + PluginSizeFormat.ReadableFileSize(side.Size.Value, false);
			}
			return text;
		}

		private static string RoleKey(DiffSideRole role)
		{
			switch (role)
			{
				case DiffSideRole.New:
					return "new";
				case DiffSideRole.Created:
					return "created";
				case DiffSideRole.Removed:
					return "removed";
				default:
					return "old";
			}
		}

		private void UpdateFaceSelectors()
		{
			_updatingSelectors = true;
			try
			{
				PopulateFaceBox(_srcFaceBox, _srcModel, _srcFaceIndex);
				PopulateFaceBox(_dstFaceBox, _dstModel, _dstFaceIndex);
			}
			finally
			{
				_updatingSelectors = false;
			}
			bool showSrc = CollectionFaceCount(_srcModel) > 1;
			bool showDst = CollectionFaceCount(_dstModel) > 1;
			_srcFaceBox.IsVisible = showSrc;
			_srcFaceLabel.IsVisible = showSrc;
			_dstFaceBox.IsVisible = showDst;
			_dstFaceLabel.IsVisible = showDst;
		}

		private static void PopulateFaceBox(ComboBox box, FontModel model, int selected)
		{
			int count = CollectionFaceCount(model);
			List<string> items = new List<string>(count);
			for (int i = 0; i < count; i++)
			{
				string label = FontStrings.F("Face {0}", i + 1);
				string name = model.Faces[i].DisplayName;
				if (!string.IsNullOrEmpty(name))
				{
					label = label + " · " + name;
				}
				items.Add(label);
			}
			box.ItemsSource = items;
			box.SelectedIndex = items.Count > 0 ? Math.Min(Math.Max(selected, 0), items.Count - 1) : -1;
		}

		/// <summary>该侧可作为「第几套」选择的套数：仅 .ttc / .otc 集合计入，其余为 0。</summary>
		private static int CollectionFaceCount(FontModel model)
		{
			if (model == null)
			{
				return 0;
			}
			string extension = Path.GetExtension(model.Path ?? string.Empty);
			bool collection = model.IsCollection
				|| string.Equals(extension, ".ttc", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(extension, ".otc", StringComparison.OrdinalIgnoreCase);
			return collection ? model.Faces.Count : 0;
		}
	}
}
