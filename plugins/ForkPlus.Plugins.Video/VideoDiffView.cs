using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
using ForkPlus.Plugins.Media;

namespace ForkPlus.Plugins.Video
{
	/// <summary>
	/// 视频对比视图：左右两栏并排，把旧（左）/ 新（右）视频的差异呈现出来。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行，
	/// 再下是自建模式工具栏（Metadata / Filmstrip / Frame compare 三段式切换），
	/// 最下是内容区；单帧对比模式另有一条常驻的时间轴拖动条（切换模式时显隐，不随内容重建）。
	///
	/// 三个模式：
	/// - 元数据：两栏各自分组卡片（容器 / 视频流 / 音频流 / 字幕流 / 标签），逐行「名 : 值」，
	///   按 相同 / 变了 / 仅左 / 仅右 四色标注。
	/// - 帧条：两侧按同一时间刻度抽关键帧（每段中点），上下两条横向可滚动的缩略图带，
	///   逐位置给出像素差异比，看画面在哪几段变了。
	/// - 单帧对比：定位到同一时间戳各取一帧，做像素级差异；「高亮差异像素」偏好开启时，
	///   把变更像素在右侧帧上染色（订阅偏好变更实时生效）。
	///
	/// 解码（探测 / 取帧）在后台线程执行，控件只在 UI 线程构建；每次刷新用代次 +
	/// CancellationToken 取消上一轮。单侧声明大小 &gt; 300 MB 只给提示、不渲染媒体内容。
	/// </summary>
	public sealed class VideoDiffView : IDiffView
	{
		private enum ViewMode
		{
			Metadata,
			Filmstrip,
			FrameCompare
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

		/// <summary>一侧的加载 + 分析结果。</summary>
		private sealed class Side
		{
			public DiffSideContent Content;

			public byte[] Bytes;

			public bool TooLarge;

			public MediaInfo Info;

			public MediaFailure Failure;

			/// <summary>帧条模式：均匀抽出的若干帧。</summary>
			public List<VideoFrame> Filmstrip;

			/// <summary>单帧对比模式：定位到同一时间戳的那一帧。</summary>
			public VideoFrame Frame;

			/// <summary>与另一侧的像素差异比例（0–1）；尺寸不同为 -1；未比较为 -2。</summary>
			public double DiffRatio = -2.0;
		}

		private static readonly IBrush GridLine = Brushes.Gainsboro;

		private static readonly IBrush Subtle = new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x80, 0x80));

		private static readonly IBrush ChipTint = new SolidColorBrush(Color.FromArgb(0x1F, 0x80, 0x80, 0x80));

		private static readonly IBrush ActiveTint = new SolidColorBrush(Color.FromArgb(0x33, 0x4A, 0x90, 0xE2));

		private static readonly IBrush DiffChanged = new SolidColorBrush(Color.FromArgb(0x2E, 0xE6, 0xA2, 0x3C));

		private static readonly IBrush DiffLeft = new SolidColorBrush(Color.FromArgb(0x26, 0x4A, 0x90, 0xE2));

		private static readonly IBrush DiffRight = new SolidColorBrush(Color.FromArgb(0x26, 0xE0, 0x5A, 0x5A));

		private static readonly IBrush DiffSame = new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00));

		/// <summary>常见标签的本地化标签名（查不到就用原始 key）。</summary>
		private static readonly Dictionary<string, string> TagLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			{ "title", "Title" },
			{ "artist", "Artist" },
			{ "album", "Album" },
			{ "comment", "Comment" },
			{ "description", "Comment" },
			{ "encoder", "Encoder" },
			{ "encoded_by", "Encoder" },
			{ "copyright", "Copyright" },
			{ "language", "Language" },
			{ "date", "Year" },
			{ "year", "Year" }
		};

		private readonly Grid _root;

		private readonly TextBlock _srcTitle;

		private readonly TextBlock _dstTitle;

		private readonly TextBlock _status;

		private readonly Button _metadataButton;

		private readonly Button _filmstripButton;

		private readonly Button _frameButton;

		private readonly Grid _scrubRow;

		private readonly Slider _scrubSlider;

		private readonly TextBlock _scrubLabel;

		private readonly ContentControl _content;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private ViewMode _mode;

		private Side _src;

		private Side _dst;

		private int _renderGeneration;

		private bool _released;

		private double _position = 0.5;

		private bool _highlightAvailable;

		/// <summary>「高亮差异像素」能力变化（单帧对比可否做像素高亮）。</summary>
		private event EventHandler<bool> HighlightAvailableChanged;

		public VideoDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			Grid header = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,*")
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
				TextWrapping = TextWrapping.Wrap
			};

			_metadataButton = NewModeButton("Metadata", ViewMode.Metadata);
			_filmstripButton = NewModeButton("Filmstrip", ViewMode.Filmstrip);
			_frameButton = NewModeButton("Frame compare", ViewMode.FrameCompare);
			StackPanel modes = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Margin = new Thickness(12.0, 0.0, 12.0, 8.0)
			};
			modes.Children.Add(_metadataButton);
			modes.Children.Add(_filmstripButton);
			modes.Children.Add(_frameButton);

			_scrubSlider = new Slider
			{
				Minimum = 0.0,
				Maximum = 100.0,
				Value = _position * 100.0,
				VerticalAlignment = VerticalAlignment.Center,
				Width = 320.0,
				TickFrequency = 10.0,
				IsSnapToTickEnabled = false
			};
			_scrubSlider.ValueChanged += OnScrubChanged;
			_scrubLabel = Label(string.Empty, 11.5, FontWeight.Normal, 0.7);
			_scrubLabel.Margin = new Thickness(10.0, 0.0, 0.0, 0.0);
			_scrubRow = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"),
				Margin = new Thickness(12.0, 0.0, 12.0, 8.0)
			};
			TextBlock scrubTitle = Label(VideoStrings.T("Position"), 11.5, FontWeight.SemiBold, 0.75);
			scrubTitle.Margin = new Thickness(0.0, 0.0, 10.0, 0.0);
			Grid.SetColumn(scrubTitle, 0);
			_scrubRow.Children.Add(scrubTitle);
			Grid.SetColumn(_scrubSlider, 1);
			_scrubRow.Children.Add(_scrubSlider);
			Grid.SetColumn(_scrubLabel, 2);
			_scrubRow.Children.Add(_scrubLabel);

			_content = new ContentControl
			{
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				VerticalContentAlignment = VerticalAlignment.Stretch
			};
			ScrollViewer scroll = new ScrollViewer
			{
				Content = _content,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto
			};
			Border contentHost = new Border
			{
				BorderBrush = GridLine,
				BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
				Child = scroll
			};

			_root = new Grid
			{
				RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*")
			};
			Grid.SetRow(header, 0);
			_root.Children.Add(header);
			Grid.SetRow(_status, 1);
			_root.Children.Add(_status);
			Grid.SetRow(modes, 2);
			_root.Children.Add(modes);
			Grid.SetRow(_scrubRow, 3);
			_root.Children.Add(_scrubRow);
			Grid.SetRow(contentHost, 4);
			_root.Children.Add(contentHost);

			PluginEnvironment.ImageDiffHighlightPixelsChanged += OnHighlightPreferenceChanged;
			UpdateStaticTexts();
			PluginEnvironment.ApplyLocalization(_root);
		}

		// ---- IDiffView ----

		Control IDiffView.View => _root;

		IReadOnlyList<DiffViewMode> IDiffView.Modes => Array.Empty<DiffViewMode>();

		event EventHandler<bool> IDiffView.HighlightPixelsAvailableChanged
		{
			add { HighlightAvailableChanged += value; }
			remove { HighlightAvailableChanged -= value; }
		}

		public void SetContent(DiffViewContext context, IDiffViewHost host)
		{
			_context = context;
			_host = host;
			_srcTitle.Foreground = context?.SrcTitleBrush;
			_dstTitle.Foreground = context?.DstTitleBrush;
			_src = null;
			_dst = null;
			// CI 截图定位用：日志里必须出现 VideoDiffView.SetContent。
			PluginLog.Info($"VideoDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			StartRender();
		}

		public void SetMode(string modeId)
		{
			ViewMode mode;
			switch (modeId)
			{
				case "metadata":
					mode = ViewMode.Metadata;
					break;
				case "filmstrip":
					mode = ViewMode.Filmstrip;
					break;
				case "framecompare":
				case "frame":
					mode = ViewMode.FrameCompare;
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

		public void ApplyLocalization()
		{
			PluginEnvironment.ApplyLocalization(_root);
			UpdateStaticTexts();
			StartRender();
		}

		public void Release()
		{
			_released = true;
			PluginEnvironment.ImageDiffHighlightPixelsChanged -= OnHighlightPreferenceChanged;
			CancelRender();
			_context = null;
			_host = null;
			_src = null;
			_dst = null;
			_content.Content = null;
			_srcTitle.Text = string.Empty;
			_dstTitle.Text = string.Empty;
			_status.Text = string.Empty;
			SetHighlightAvailable(false);
		}

		// ---- 工具栏 / 拖动条 ----

		private Button NewModeButton(string key, ViewMode mode)
		{
			Button button = new Button
			{
				Content = VideoStrings.T(key),
				Padding = new Thickness(12.0, 4.0, 12.0, 4.0),
				Tag = mode,
				VerticalAlignment = VerticalAlignment.Center
			};
			button.Click += OnModeClicked;
			return button;
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

		private void OnScrubChanged(object sender, RangeBaseValueChangedEventArgs e)
		{
			_position = Math.Max(0.0, Math.Min(1.0, e.NewValue / 100.0));
			if (_mode == ViewMode.FrameCompare)
			{
				StartRender();
			}
		}

		/// <summary>「高亮差异像素」偏好实时变更：单帧对比模式无需重新解码，仅重绘内容。</summary>
		private void OnHighlightPreferenceChanged(bool enabled)
		{
			Dispatcher.UIThread.Post(delegate
			{
				if (!_released && _mode == ViewMode.FrameCompare && _src != null && _dst != null)
				{
					BuildContent(ViewMode.FrameCompare, false);
				}
			});
		}

		private void UpdateStaticTexts()
		{
			UpdateToolbar();
			UpdateTitles();
			_scrubLabel.Text = FrameLabel(_src, _dst);
		}

		private void UpdateToolbar()
		{
			_metadataButton.Content = VideoStrings.T("Metadata");
			_filmstripButton.Content = VideoStrings.T("Filmstrip");
			_frameButton.Content = VideoStrings.T("Frame compare");
			StyleModeButton(_metadataButton, _mode == ViewMode.Metadata);
			StyleModeButton(_filmstripButton, _mode == ViewMode.Filmstrip);
			StyleModeButton(_frameButton, _mode == ViewMode.FrameCompare);
			_scrubRow.IsVisible = _mode == ViewMode.FrameCompare;
		}

		private static void StyleModeButton(Button button, bool active)
		{
			button.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
			button.Background = active ? ActiveTint : Brushes.Transparent;
		}

		private void SetHighlightAvailable(bool value)
		{
			if (_highlightAvailable == value)
			{
				return;
			}
			_highlightAvailable = value;
			HighlightAvailableChanged?.Invoke(this, value);
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
			_status.Text = VideoStrings.T("Analyzing…");
			CancellationTokenSource cts = new CancellationTokenSource();
			_cts = cts;
			int generation = _renderGeneration;
			ViewMode mode = _mode;
			double position = _position;
			DiffViewContext context = _context;
			IDiffViewHost host = _host;
			Task.Run(() => RenderAsync(context, host, mode, position, cts.Token, generation));
		}

		private async Task RenderAsync(DiffViewContext context, IDiffViewHost host, ViewMode mode, double position, CancellationToken token, int generation)
		{
			try
			{
				Side src = await LoadSideAsync(context?.Src, context?.HexSrc, host, token).ConfigureAwait(false);
				Side dst = await LoadSideAsync(context?.Dst, context?.HexDst, host, token).ConfigureAwait(false);
				if (token.IsCancellationRequested)
				{
					return;
				}
				bool blocked = src.TooLarge || dst.TooLarge;
				if (!blocked)
				{
					double seconds = SharedSeconds(src, dst, position);
					Analyze(src, mode, seconds, token);
					Analyze(dst, mode, seconds, token);
					if (token.IsCancellationRequested)
					{
						return;
					}
					if (mode == ViewMode.FrameCompare)
					{
						ComputeFrameDiff(src, dst);
					}
				}
				string status = BuildStatus(src, dst);
				Dispatcher.UIThread.Post(delegate
				{
					if (_released || generation != _renderGeneration)
					{
						return;
					}
					_src = src;
					_dst = dst;
					_status.Text = status;
					_scrubLabel.Text = FrameLabel(src, dst);
					BuildContent(mode, blocked);
				});
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				PluginLog.Error("VideoDiffView render failed", ex);
				PostStatus(generation, VideoStrings.T("Failed to decode media") + ": " + ex.Message);
			}
		}

		/// <summary>取一侧字节：宿主 Hex 预载 &gt; 侧内容懒加载 &gt; LFS 缓存 / smudge。并做 300 MB 阈值判定。</summary>
		private static async Task<Side> LoadSideAsync(DiffSideContent side, MemoryStream hex, IDiffViewHost host, CancellationToken token)
		{
			Side result = new Side
			{
				Content = side
			};
			if (side == null)
			{
				return result;
			}
			if (side.Size.HasValue && side.Size.Value > MediaLimits.MaxSideBytes)
			{
				result.TooLarge = true;
				return result;
			}
			if (hex != null && hex.Length > 0)
			{
				result.Bytes = hex.ToArray();
				return result;
			}
			MemoryStream data = null;
			try
			{
				data = side.Data;
			}
			catch (Exception ex)
			{
				PluginLog.Warn("Video: failed to load side bytes for '" + side.Path + "'", ex);
			}
			if (data != null && data.Length > 0)
			{
				result.Bytes = data.ToArray();
				return result;
			}
			if (side.Lfs == null || host == null)
			{
				return result;
			}
			MemoryStream cached = null;
			try
			{
				cached = host.GetCachedLfsData(side.Lfs);
			}
			catch (Exception ex)
			{
				PluginLog.Warn("Video: LFS cache lookup failed", ex);
			}
			if (cached != null && cached.Length > 0)
			{
				result.Bytes = cached.ToArray();
				return result;
			}
			TaskCompletionSource<byte[]> tcs = new TaskCompletionSource<byte[]>();
			IDisposable subscription = null;
			CancellationTokenRegistration registration = token.Register(delegate
			{
				subscription?.Dispose();
				tcs.TrySetCanceled();
			});
			try
			{
				subscription = host.RunLfsSmudge(side.Lfs, null, delegate(LfsSmudgeResult smudge)
				{
					tcs.TrySetResult(smudge != null && smudge.Succeeded && smudge.Data != null ? smudge.Data.ToArray() : null);
				});
				result.Bytes = await tcs.Task.ConfigureAwait(false);
				return result;
			}
			finally
			{
				registration.Dispose();
				subscription?.Dispose();
			}
		}

		/// <summary>后台分析一侧：探测元数据；帧条 / 单帧对比模式再按需取帧。</summary>
		private static void Analyze(Side side, ViewMode mode, double seconds, CancellationToken token)
		{
			if (side == null || side.Bytes == null || side.Bytes.Length == 0)
			{
				return;
			}
			try
			{
				using (MemoryStream stream = new MemoryStream(side.Bytes, false))
				{
					side.Info = MediaProbe.Probe(stream, side.Content?.Size ?? side.Bytes.Length, out side.Failure);
					if (mode == ViewMode.Filmstrip)
					{
						side.Filmstrip = MediaVideo.ExtractFilmstrip(stream, MediaLimits.MaxFilmstripFrames, token, out MediaFailure stripFailure);
						if (side.Filmstrip == null && side.Failure == null)
						{
							side.Failure = stripFailure;
						}
					}
					else if (mode == ViewMode.FrameCompare)
					{
						side.Frame = MediaVideo.ExtractFrame(stream, seconds, MediaLimits.FrameCompareWidth, MediaLimits.FrameCompareHeight, token, out MediaFailure frameFailure);
						if (side.Frame == null && side.Failure == null)
						{
							side.Failure = frameFailure;
						}
					}
				}
			}
			catch (Exception ex)
			{
				side.Failure = new MediaFailure(MediaErrorKind.Failed, ex.GetType().Name + ": " + ex.Message);
				PluginLog.Warn("Video: analysis failed", ex);
			}
		}

		/// <summary>单帧对比：两侧帧都取到且尺寸一致时算像素差异比例（否则标记尺寸不同）。</summary>
		private static void ComputeFrameDiff(Side src, Side dst)
		{
			ImageData left = src?.Frame?.Image;
			ImageData right = dst?.Frame?.Image;
			if (left == null || right == null)
			{
				return;
			}
			double ratio = MediaRender.ComputeDiffRatio(left, right);
			src.DiffRatio = ratio;
			dst.DiffRatio = ratio;
		}

		/// <summary>共用的目标时间戳：两侧都有效时取较短时长，否则取较长者；按比例定位。</summary>
		private static double SharedSeconds(Side src, Side dst, double position)
		{
			double a = src?.Info?.DurationSeconds ?? 0.0;
			double b = dst?.Info?.DurationSeconds ?? 0.0;
			double duration = a > 0.0 && b > 0.0 ? Math.Min(a, b) : Math.Max(a, b);
			if (duration <= 0.0)
			{
				return 0.0;
			}
			double seconds = Math.Max(0.0, Math.Min(1.0, position)) * duration;
			double max = duration - 1e-3;
			return seconds > max ? max : seconds;
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

		private void BuildContent(ViewMode mode, bool blocked)
		{
			if (blocked)
			{
				SetHighlightAvailable(false);
				_content.Content = new StackPanel
				{
					Margin = new Thickness(12.0),
					Children =
					{
						NoteText(VideoStrings.T("File too large to preview"), new Thickness(0.0))
					}
				};
				return;
			}
			switch (mode)
			{
				case ViewMode.Metadata:
					SetHighlightAvailable(false);
					_content.Content = BuildMetadataContent();
					break;
				case ViewMode.Filmstrip:
					SetHighlightAvailable(false);
					_content.Content = BuildFilmstripContent();
					break;
				default:
					_content.Content = BuildFrameCompareContent();
					break;
			}
		}

		// ---- 元数据模式 ----

		private Control BuildMetadataContent()
		{
			List<MetaRow> plan = BuildMetaPlan();
			Grid columns = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,*")
			};
			Control left = BuildMetaColumn(plan, true);
			Grid.SetColumn(left, 0);
			columns.Children.Add(left);
			Control right = BuildMetaColumn(plan, false);
			Grid.SetColumn(right, 1);
			columns.Children.Add(right);

			StackPanel root = new StackPanel
			{
				Margin = new Thickness(12.0, 0.0, 12.0, 12.0)
			};
			AddSideNotes(root);
			root.Children.Add(columns);
			return root;
		}

		private List<MetaRow> BuildMetaPlan()
		{
			MediaInfo left = _src?.Info;
			MediaInfo right = _dst?.Info;
			List<MetaRow> rows = new List<MetaRow>();

			// 容器
			AddRow(rows, "Container", "Format", Fmt(left?.FormatName), Fmt(right?.FormatName));
			AddRow(rows, "Container", "Long name", Fmt(left?.FormatLongName), Fmt(right?.FormatLongName));
			AddRow(rows, "Container", "Duration", Duration(left?.DurationSeconds ?? 0.0), Duration(right?.DurationSeconds ?? 0.0));
			AddRow(rows, "Container", "Bit rate", BitRate(left?.BitRate ?? 0L), BitRate(right?.BitRate ?? 0L));
			AddRow(rows, "Container", "Size", Size(left?.SizeBytes ?? 0L), Size(right?.SizeBytes ?? 0L));
			AddRow(rows, "Container", "Streams", Count(left?.Streams.Count), Count(right?.Streams.Count));

			// 视频流：逐条对照（最多 3 条，避免超长）。
			MediaStreamInfo[] leftVideo = StreamsOf(left, "video");
			MediaStreamInfo[] rightVideo = StreamsOf(right, "video");
			int videoCount = Math.Max(leftVideo.Length, rightVideo.Length);
			for (int i = 0; i < Math.Min(videoCount, 3); i++)
			{
				MediaStreamInfo lv = i < leftVideo.Length ? leftVideo[i] : null;
				MediaStreamInfo rv = i < rightVideo.Length ? rightVideo[i] : null;
				string prefix = VideoStrings.T("Stream") + " #" + (i + 1) + " · ";
				AddRow(rows, "Video streams", prefix + VideoStrings.T("Codec"), Fmt(lv?.CodecName), Fmt(rv?.CodecName));
				AddRow(rows, "Video streams", prefix + VideoStrings.T("Resolution"), Resolution(lv), Resolution(rv));
				AddRow(rows, "Video streams", prefix + VideoStrings.T("Pixel format"), Fmt(lv?.PixelFormat), Fmt(rv?.PixelFormat));
				AddRow(rows, "Video streams", prefix + VideoStrings.T("Color space"), Fmt(lv?.ColorSpace), Fmt(rv?.ColorSpace));
				AddRow(rows, "Video streams", prefix + VideoStrings.T("Frame rate"), FrameRate(lv), FrameRate(rv));
				AddRow(rows, "Video streams", prefix + VideoStrings.T("Bit rate"), BitRate(lv?.BitRate ?? 0L), BitRate(rv?.BitRate ?? 0L));
			}

			// 音频流：逐条对照（最多 2 条）。
			MediaStreamInfo[] leftAudio = StreamsOf(left, "audio");
			MediaStreamInfo[] rightAudio = StreamsOf(right, "audio");
			int audioCount = Math.Max(leftAudio.Length, rightAudio.Length);
			for (int i = 0; i < Math.Min(audioCount, 2); i++)
			{
				MediaStreamInfo la = i < leftAudio.Length ? leftAudio[i] : null;
				MediaStreamInfo ra = i < rightAudio.Length ? rightAudio[i] : null;
				string prefix = VideoStrings.T("Stream") + " #" + (i + 1) + " · ";
				AddRow(rows, "Audio streams", prefix + VideoStrings.T("Codec"), Fmt(la?.CodecName), Fmt(ra?.CodecName));
				AddRow(rows, "Audio streams", prefix + VideoStrings.T("Sample rate"), SampleRate(la), SampleRate(ra));
				AddRow(rows, "Audio streams", prefix + VideoStrings.T("Channels"), Channels(la), Channels(ra));
				AddRow(rows, "Audio streams", prefix + VideoStrings.T("Channel layout"), Fmt(la?.ChannelLayout), Fmt(ra?.ChannelLayout));
				AddRow(rows, "Audio streams", prefix + VideoStrings.T("Language"), StreamLanguage(la), StreamLanguage(ra));
			}

			// 字幕流：逐条对照（最多 3 条）。
			MediaStreamInfo[] leftSubs = StreamsOf(left, "subtitle");
			MediaStreamInfo[] rightSubs = StreamsOf(right, "subtitle");
			int subCount = Math.Max(leftSubs.Length, rightSubs.Length);
			for (int i = 0; i < Math.Min(subCount, 3); i++)
			{
				MediaStreamInfo ls = i < leftSubs.Length ? leftSubs[i] : null;
				MediaStreamInfo rs = i < rightSubs.Length ? rightSubs[i] : null;
				string prefix = VideoStrings.T("Stream") + " #" + (i + 1) + " · ";
				AddRow(rows, "Subtitles", prefix + VideoStrings.T("Codec"), Fmt(ls?.CodecName), Fmt(rs?.CodecName));
				AddRow(rows, "Subtitles", prefix + VideoStrings.T("Language"), StreamLanguage(ls), StreamLanguage(rs));
			}

			// 标签：并集后按 key 排序。
			SortedDictionary<string, string> tagKeys = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			AddTagKeys(tagKeys, left);
			AddTagKeys(tagKeys, right);
			foreach (string key in tagKeys.Keys)
			{
				AddRow(rows, "Tags", TagLabel(key), TagValue(left, key), TagValue(right, key));
			}
			return rows;
		}

		private static void AddTagKeys(SortedDictionary<string, string> target, MediaInfo info)
		{
			if (info == null)
			{
				return;
			}
			foreach (KeyValuePair<string, string> pair in info.Tags)
			{
				if (!target.ContainsKey(pair.Key))
				{
					target[pair.Key] = pair.Key;
				}
			}
		}

		private static string TagLabel(string key)
		{
			return TagLabels.TryGetValue(key, out string label) ? VideoStrings.T(label) : key;
		}

		private static string TagValue(MediaInfo info, string key)
		{
			if (info == null)
			{
				return null;
			}
			return info.Tags.TryGetValue(key, out string value) ? Fmt(value) : null;
		}

		private static MediaStreamInfo[] StreamsOf(MediaInfo info, string kind)
		{
			if (info == null)
			{
				return Array.Empty<MediaStreamInfo>();
			}
			return info.Streams.Where(delegate(MediaStreamInfo s)
			{
				return string.Equals(s.Kind, kind, StringComparison.Ordinal);
			}).ToArray();
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
				Margin = isLeft ? new Thickness(0.0, 0.0, 10.0, 0.0) : new Thickness(10.0, 0.0, 0.0, 0.0)
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
						Child = card
					};
					card.Children.Add(new TextBlock
					{
						Text = VideoStrings.T(group),
						FontWeight = FontWeight.SemiBold,
						FontSize = 12.0,
						Margin = new Thickness(0.0, 0.0, 0.0, 5.0)
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
				Margin = new Thickness(0.0, 1.0, 0.0, 1.0)
			};
			TextBlock text = new TextBlock
			{
				Text = row.Label + " : " + (present ? value : VideoStrings.T("not present")),
				FontSize = 11.5,
				Opacity = present ? 0.9 : 0.55,
				TextWrapping = TextWrapping.Wrap,
				VerticalAlignment = VerticalAlignment.Center
			};
			Grid.SetColumn(text, 0);
			grid.Children.Add(text);
			Border chip = Chip(Label(VideoStrings.T(StateKey(row.State)), 10.5, FontWeight.Normal, 0.8), ChipTint);
			chip.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
			Grid.SetColumn(chip, 1);
			grid.Children.Add(chip);
			return grid;
		}

		// ---- 帧条模式 ----

		private Control BuildFilmstripContent()
		{
			StackPanel root = new StackPanel
			{
				Margin = new Thickness(12.0, 0.0, 12.0, 12.0)
			};
			AddSideNotes(root);
			root.Children.Add(SectionTitle(VideoStrings.T("Filmstrip comparison")));
			root.Children.Add(FilmstripSection(_context?.SrcRole ?? DiffSideRole.Old, _src, _dst));
			root.Children.Add(FilmstripSection(_context?.DstRole ?? DiffSideRole.New, _dst, _src));
			int count = Math.Max(_src?.Filmstrip?.Count ?? 0, _dst?.Filmstrip?.Count ?? 0);
			if (count > 0)
			{
				root.Children.Add(NoteText(VideoStrings.F("{0} frames", count.ToString(CultureInfo.InvariantCulture)), new Thickness(0.0, 8.0, 0.0, 0.0)));
			}
			return root;
		}

		private Control FilmstripSection(DiffSideRole role, Side side, Side other)
		{
			StackPanel panel = new StackPanel();
			panel.Children.Add(Label(PluginEnvironment.Translate(RoleKey(role)), 11.5, FontWeight.SemiBold, 0.7));
			if (side == null || side.Bytes == null)
			{
				panel.Children.Add(NoteText(side == null ? VideoStrings.T("not present") : VideoStrings.T("Media content unavailable"), new Thickness(0.0, 4.0, 0.0, 8.0)));
				return panel;
			}
			if (side.Filmstrip == null || side.Filmstrip.Count == 0)
			{
				panel.Children.Add(NoteText(DescribeFailure(side, "No frames available"), new Thickness(0.0, 4.0, 0.0, 8.0)));
				return panel;
			}
			StackPanel strip = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 8.0
			};
			for (int i = 0; i < side.Filmstrip.Count; i++)
			{
				VideoFrame frame = side.Filmstrip[i];
				VideoFrame counterpart = other?.Filmstrip != null && i < other.Filmstrip.Count ? other.Filmstrip[i] : null;
				strip.Children.Add(FilmstripThumb(frame, counterpart));
			}
			ScrollViewer scroller = new ScrollViewer
			{
				Content = strip,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
				Margin = new Thickness(0.0, 4.0, 0.0, 10.0)
			};
			panel.Children.Add(scroller);
			return panel;
		}

		private static Control FilmstripThumb(VideoFrame frame, VideoFrame counterpart)
		{
			StackPanel cell = new StackPanel
			{
				Spacing = 2.0
			};
			Bitmap bitmap = MediaImage.FromImageData(frame?.Image);
			Border host = new Border
			{
				BorderBrush = GridLine,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(3.0),
				Child = bitmap != null
					? new Image
					{
						Source = bitmap,
						Stretch = Stretch.Uniform,
						MaxWidth = MediaLimits.FilmstripWidth
					}
					: (Control)new TextBlock { Text = VideoStrings.T("No frames available"), FontSize = 10.5, Opacity = 0.6 }
			};
			cell.Children.Add(host);
			cell.Children.Add(Label(VideoStrings.F("Frame at {0} s", frame.Timestamp.ToString("0.#", CultureInfo.InvariantCulture)), 10.5, FontWeight.Normal, 0.65));
			if (frame?.Image != null && counterpart?.Image != null)
			{
				double ratio = MediaRender.ComputeDiffRatio(frame.Image, counterpart.Image);
				if (ratio >= 0.0)
				{
					cell.Children.Add(Chip(Label(VideoStrings.F("{0}% pixels changed", (ratio * 100.0).ToString("0.#", CultureInfo.InvariantCulture)), 10.0, FontWeight.Normal, 0.85), ratio > 0.0 ? DiffChanged : ChipTint));
				}
			}
			return cell;
		}

		// ---- 单帧对比模式 ----

		private Control BuildFrameCompareContent()
		{
			StackPanel root = new StackPanel
			{
				Margin = new Thickness(12.0, 0.0, 12.0, 12.0)
			};
			AddSideNotes(root);
			root.Children.Add(SectionTitle(VideoStrings.T("Frame comparison")));

			bool srcReady = _src?.Frame?.Image != null;
			bool dstReady = _dst?.Frame?.Image != null;
			bool comparable = srcReady && dstReady
				&& _src.Frame.Image.Width == _dst.Frame.Image.Width
				&& _src.Frame.Image.Height == _dst.Frame.Image.Height;
			SetHighlightAvailable(comparable);

			ImageData leftImage = _src?.Frame?.Image;
			ImageData rightImage = _dst?.Frame?.Image;
			root.Children.Add(TwoColumn(
				SidePanel(_context?.SrcRole ?? DiffSideRole.Old, FramePanel(_src, leftImage, null)),
				SidePanel(_context?.DstRole ?? DiffSideRole.New, FramePanel(_dst, leftImage, rightImage))));

			if (srcReady && dstReady)
			{
				if (comparable)
				{
					double ratio = _src.DiffRatio >= -1.0 ? _src.DiffRatio : MediaRender.ComputeDiffRatio(_src.Frame.Image, _dst.Frame.Image);
					root.Children.Add(SectionTitle(VideoStrings.T("Pixel difference")));
					root.Children.Add(Label(VideoStrings.F("{0}% pixels changed", (Math.Max(0.0, ratio) * 100.0).ToString("0.#", CultureInfo.InvariantCulture)), 12.0, FontWeight.SemiBold, 0.9));
					if (!PluginEnvironment.HighlightImageDiff)
					{
						root.Children.Add(NoteText(VideoStrings.T("Highlighting is off"), new Thickness(0.0, 4.0, 0.0, 0.0)));
					}
				}
				else
				{
					root.Children.Add(NoteText(VideoStrings.T("Frame sizes differ"), new Thickness(0.0, 8.0, 0.0, 0.0)));
				}
			}
			return root;
		}

		/// <summary>
		/// 一帧的展示单元。<paramref name="left"/> / <paramref name="right"/> 为两侧的帧（右侧高亮需要成对），
		/// 只有右侧（且尺寸一致、偏好开启）才把变更像素染色。
		/// </summary>
		private static Control FramePanel(Side side, ImageData left, ImageData right)
		{
			if (side == null || side.Bytes == null)
			{
				return NoteText(side == null ? VideoStrings.T("not present") : VideoStrings.T("Media content unavailable"), new Thickness(0.0, 4.0, 0.0, 8.0));
			}
			if (side.Frame?.Image == null)
			{
				return NoteText(DescribeFailure(side, "No frame available"), new Thickness(0.0, 4.0, 0.0, 8.0));
			}
			StackPanel panel = new StackPanel();
			Bitmap bitmap = null;
			bool comparable = left != null && right != null
				&& left.Width == right.Width && left.Height == right.Height;
			if (side.Frame.Image == right && comparable && PluginEnvironment.HighlightImageDiff)
			{
				byte[] highlight = MediaRender.RenderHighlight(left, right);
				bitmap = highlight != null ? MediaImage.FromBgra(highlight, right.Width, right.Height) : null;
			}
			bitmap ??= MediaImage.FromImageData(side.Frame.Image);
			if (bitmap != null)
			{
				panel.Children.Add(new Image
				{
					Source = bitmap,
					Stretch = Stretch.Uniform,
					HorizontalAlignment = HorizontalAlignment.Stretch,
					Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
				});
			}
			panel.Children.Add(Label(VideoStrings.F("Frame at {0} s", side.Frame.Timestamp.ToString("0.#", CultureInfo.InvariantCulture)), 11.0, FontWeight.Normal, 0.7));
			return panel;
		}

		// ---- 描述 / 辅助 ----

		private void AddSideNotes(StackPanel root)
		{
			string srcNote = DescribeSide(_src);
			if (srcNote != null)
			{
				root.Children.Add(NoteText(PluginEnvironment.Translate(RoleKey(_context?.SrcRole ?? DiffSideRole.Old)) + ": " + srcNote, new Thickness(0.0, 0.0, 0.0, 6.0)));
			}
			string dstNote = DescribeSide(_dst);
			if (dstNote != null)
			{
				root.Children.Add(NoteText(PluginEnvironment.Translate(RoleKey(_context?.DstRole ?? DiffSideRole.New)) + ": " + dstNote, new Thickness(0.0, 0.0, 0.0, 6.0)));
			}
		}

		private static string DescribeSide(Side side)
		{
			if (side == null || side.Content == null)
			{
				return VideoStrings.T("not present");
			}
			if (side.Bytes == null)
			{
				return VideoStrings.T("Media content unavailable");
			}
			if (side.Info == null)
			{
				return DescribeFailure(side, "Unsupported media format");
			}
			if (!side.Info.HasVideo)
			{
				return VideoStrings.T("No video stream");
			}
			return null;
		}

		private static string DescribeFailure(Side side, string fallback)
		{
			if (side?.Failure == null)
			{
				return VideoStrings.T(fallback);
			}
			switch (side.Failure.Kind)
			{
				case MediaErrorKind.Unavailable:
					return VideoStrings.T("FFmpeg decoding unavailable");
				case MediaErrorKind.NoData:
					return VideoStrings.T("Media content unavailable");
				case MediaErrorKind.Unsupported:
					return VideoStrings.T("Unsupported media format");
				case MediaErrorKind.TooLarge:
					return VideoStrings.T("File too large to preview");
				default:
					return VideoStrings.T("Failed to decode media");
			}
		}

		private static Control SidePanel(DiffSideRole role, Control content)
		{
			StackPanel panel = new StackPanel();
			panel.Children.Add(Label(PluginEnvironment.Translate(RoleKey(role)), 11.5, FontWeight.SemiBold, 0.7));
			panel.Children.Add(content);
			return panel;
		}

		private static Control TwoColumn(Control left, Control right)
		{
			Grid grid = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,*")
			};
			Border leftHost = new Border
			{
				Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
				Child = left
			};
			Grid.SetColumn(leftHost, 0);
			grid.Children.Add(leftHost);
			Border rightHost = new Border
			{
				Margin = new Thickness(10.0, 0.0, 0.0, 0.0),
				Child = right
			};
			Grid.SetColumn(rightHost, 1);
			grid.Children.Add(rightHost);
			return grid;
		}

		private static TextBlock SectionTitle(string text)
		{
			return new TextBlock
			{
				Text = text,
				FontWeight = FontWeight.SemiBold,
				FontSize = 12.0,
				Margin = new Thickness(0.0, 10.0, 0.0, 4.0)
			};
		}

		private string BuildStatus(Side src, Side dst)
		{
			string srcName = src?.Info != null ? Fmt(src.Info.FormatName) : DescribeSide(src);
			string dstName = dst?.Info != null ? Fmt(dst.Info.FormatName) : DescribeSide(dst);
			return VideoStrings.F("Video compare: {0}", (srcName ?? "-") + " / " + (dstName ?? "-"));
		}

		private static string FrameLabel(Side src, Side dst)
		{
			double a = src?.Info?.DurationSeconds ?? 0.0;
			double b = dst?.Info?.DurationSeconds ?? 0.0;
			double duration = a > 0.0 && b > 0.0 ? Math.Min(a, b) : Math.Max(a, b);
			double seconds = duration > 0.0 ? src?.Frame?.Timestamp ?? dst?.Frame?.Timestamp ?? 0.0 : 0.0;
			return VideoStrings.F("Frame at {0} s", seconds.ToString("0.#", CultureInfo.InvariantCulture));
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

		private static string Fmt(string value)
		{
			return string.IsNullOrWhiteSpace(value) ? null : value;
		}

		private static string Normalize(string value)
		{
			return string.IsNullOrWhiteSpace(value) ? null : value;
		}

		private static string Count(int? value)
		{
			return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : null;
		}

		private static string Duration(double seconds)
		{
			if (seconds <= 0.0)
			{
				return null;
			}
			TimeSpan span = TimeSpan.FromSeconds(seconds);
			return span.TotalHours >= 1.0
				? span.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
				: span.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
		}

		private static string BitRate(long bitsPerSecond)
		{
			return bitsPerSecond > 0L
				? VideoStrings.F("{0} kbps", (bitsPerSecond / 1000L).ToString(CultureInfo.InvariantCulture))
				: null;
		}

		private static string Size(long bytes)
		{
			return bytes > 0L ? PluginSizeFormat.ReadableFileSize(bytes, false) : null;
		}

		private static string Resolution(MediaStreamInfo stream)
		{
			return stream != null && stream.Width > 0 && stream.Height > 0
				? VideoStrings.F("{0} × {1}", stream.Width.ToString(CultureInfo.InvariantCulture), stream.Height.ToString(CultureInfo.InvariantCulture))
				: null;
		}

		private static string FrameRate(MediaStreamInfo stream)
		{
			return stream != null && stream.FrameRate > 0.0
				? VideoStrings.F("{0} fps", stream.FrameRate.ToString("0.###", CultureInfo.InvariantCulture))
				: null;
		}

		private static string SampleRate(MediaStreamInfo stream)
		{
			return stream != null && stream.SampleRate > 0 ? VideoStrings.F("{0} Hz", stream.SampleRate.ToString(CultureInfo.InvariantCulture)) : null;
		}

		private static string Channels(MediaStreamInfo stream)
		{
			return stream != null && stream.Channels > 0 ? VideoStrings.F("{0} ch", stream.Channels.ToString(CultureInfo.InvariantCulture)) : null;
		}

		private static string StreamLanguage(MediaStreamInfo stream)
		{
			if (stream == null)
			{
				return null;
			}
			return stream.Tags.TryGetValue("language", out string language) ? Fmt(language) : null;
		}

		// ---- 通用控件辅助 ----

		private static TextBlock NewTitle()
		{
			return new TextBlock
			{
				Margin = new Thickness(12.0, 8.0, 12.0, 4.0),
				FontWeight = FontWeight.SemiBold,
				TextTrimming = TextTrimming.CharacterEllipsis
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
				Child = child
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
				VerticalAlignment = VerticalAlignment.Center
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
				Margin = margin
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
				return text + "  ·  " + VideoStrings.T("not present");
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
	}
}
