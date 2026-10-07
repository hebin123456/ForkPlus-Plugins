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

namespace ForkPlus.Plugins.Audio
{
	/// <summary>
	/// 音频对比视图：左右两栏并排，把旧（左）/ 新（右）音频的差异呈现出来。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行，
	/// 再下是自建模式工具栏（Metadata / Waveform / Spectrum / Cover 四段式切换），最下是内容区。
	///
	/// 四个模式：
	/// - 元数据：两栏各自分组卡片（容器 / 音频流 / 标签），逐行「名 : 值」，按 相同 / 变了 /
	///   仅左 / 仅右 四色标注。
	/// - 波形：两侧同一时间轴对齐的包络图，下接一条「差异带」——两侧 RMS 逐桶求差、越暖越不同。
	/// - 频谱：STFT 声谱图并排（低频在下，冷→暖渐变）。
	/// - 封面：内嵌封面（ID3 APIC / mp4 covr）并排。
	///
	/// 解码（探测 / 波形 / 频谱）在后台线程执行，控件只在 UI 线程构建；每次刷新用代次 +
	/// CancellationToken 取消上一轮。单侧声明大小 &gt; 300 MB 只给提示、不渲染媒体内容。
	/// </summary>
	public sealed class AudioDiffView : IDiffView
	{
		private enum ViewMode
		{
			Metadata,
			Waveform,
			Spectrum,
			Cover
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

			public WaveformData Wave;

			public SpectrogramData Spectrum;

			public byte[] WaveBgra;

			public byte[] SpectrumBgra;
		}

		private static readonly IBrush GridLine = Brushes.Gainsboro;

		private static readonly IBrush Subtle = new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x80, 0x80));

		private static readonly IBrush ChipTint = new SolidColorBrush(Color.FromArgb(0x1F, 0x80, 0x80, 0x80));

		private static readonly IBrush ActiveTint = new SolidColorBrush(Color.FromArgb(0x33, 0x4A, 0x90, 0xE2));

		private static readonly IBrush DiffChanged = new SolidColorBrush(Color.FromArgb(0x2E, 0xE6, 0xA2, 0x3C));

		private static readonly IBrush DiffLeft = new SolidColorBrush(Color.FromArgb(0x26, 0x4A, 0x90, 0xE2));

		private static readonly IBrush DiffRight = new SolidColorBrush(Color.FromArgb(0x26, 0xE0, 0x5A, 0x5A));

		private static readonly IBrush DiffSame = new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00));

		private static readonly FontFamily MonoFont = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, Courier New, monospace");

		/// <summary>常见标签的本地化标签名（查不到就用原始 key）。</summary>
		private static readonly Dictionary<string, string> TagLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			{ "title", "Title" },
			{ "artist", "Artist" },
			{ "album", "Album" },
			{ "album_artist", "Album artist" },
			{ "albumartist", "Album artist" },
			{ "track", "Track" },
			{ "genre", "Genre" },
			{ "date", "Year" },
			{ "year", "Year" },
			{ "comment", "Comment" },
			{ "description", "Comment" },
			{ "composer", "Composer" },
			{ "encoder", "Encoder" },
			{ "encoded_by", "Encoder" },
			{ "copyright", "Copyright" },
			{ "language", "Language" }
		};

		private readonly Grid _root;

		private readonly TextBlock _srcTitle;

		private readonly TextBlock _dstTitle;

		private readonly TextBlock _status;

		private readonly Button _metadataButton;

		private readonly Button _waveformButton;

		private readonly Button _spectrumButton;

		private readonly Button _coverButton;

		private readonly ContentControl _content;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private ViewMode _mode;

		private Side _src;

		private Side _dst;

		private int _renderGeneration;

		private bool _released;

		public AudioDiffView()
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
			_waveformButton = NewModeButton("Waveform", ViewMode.Waveform);
			_spectrumButton = NewModeButton("Spectrum", ViewMode.Spectrum);
			_coverButton = NewModeButton("Cover", ViewMode.Cover);
			StackPanel modes = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Margin = new Thickness(12.0, 0.0, 12.0, 8.0)
			};
			modes.Children.Add(_metadataButton);
			modes.Children.Add(_waveformButton);
			modes.Children.Add(_spectrumButton);
			modes.Children.Add(_coverButton);

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
				RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*")
			};
			Grid.SetRow(header, 0);
			_root.Children.Add(header);
			Grid.SetRow(_status, 1);
			_root.Children.Add(_status);
			Grid.SetRow(modes, 2);
			_root.Children.Add(modes);
			Grid.SetRow(contentHost, 3);
			_root.Children.Add(contentHost);

			UpdateStaticTexts();
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
			_srcTitle.Foreground = context?.SrcTitleBrush;
			_dstTitle.Foreground = context?.DstTitleBrush;
			_src = null;
			_dst = null;
			// CI 截图定位用：日志里必须出现 AudioDiffView.SetContent。
			PluginLog.Info($"AudioDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
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
				case "waveform":
					mode = ViewMode.Waveform;
					break;
				case "spectrum":
					mode = ViewMode.Spectrum;
					break;
				case "cover":
					mode = ViewMode.Cover;
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
			CancelRender();
			_context = null;
			_host = null;
			_src = null;
			_dst = null;
			_content.Content = null;
			_srcTitle.Text = string.Empty;
			_dstTitle.Text = string.Empty;
			_status.Text = string.Empty;
		}

		// ---- 工具栏 ----

		private Button NewModeButton(string key, ViewMode mode)
		{
			Button button = new Button
			{
				Content = AudioStrings.T(key),
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

		private void UpdateStaticTexts()
		{
			UpdateToolbar();
			UpdateTitles();
		}

		private void UpdateToolbar()
		{
			_metadataButton.Content = AudioStrings.T("Metadata");
			_waveformButton.Content = AudioStrings.T("Waveform");
			_spectrumButton.Content = AudioStrings.T("Spectrum");
			_coverButton.Content = AudioStrings.T("Cover");
			StyleModeButton(_metadataButton, _mode == ViewMode.Metadata);
			StyleModeButton(_waveformButton, _mode == ViewMode.Waveform);
			StyleModeButton(_spectrumButton, _mode == ViewMode.Spectrum);
			StyleModeButton(_coverButton, _mode == ViewMode.Cover);
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
			_status.Text = AudioStrings.T("Analyzing…");
			CancellationTokenSource cts = new CancellationTokenSource();
			_cts = cts;
			int generation = _renderGeneration;
			ViewMode mode = _mode;
			DiffViewContext context = _context;
			IDiffViewHost host = _host;
			Task.Run(() => RenderAsync(context, host, mode, cts.Token, generation));
		}

		private async Task RenderAsync(DiffViewContext context, IDiffViewHost host, ViewMode mode, CancellationToken token, int generation)
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
					Analyze(src, mode, false, token);
					Analyze(dst, mode, true, token);
				}
				if (token.IsCancellationRequested)
				{
					return;
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
					BuildContent(mode, blocked);
				});
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				PluginLog.Error("AudioDiffView render failed", ex);
				PostStatus(generation, AudioStrings.T("Failed to decode media") + ": " + ex.Message);
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
				PluginLog.Warn("Audio: failed to load side bytes for '" + side.Path + "'", ex);
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
				PluginLog.Warn("Audio: LFS cache lookup failed", ex);
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

		/// <summary>后台分析一侧：探测元数据（含封面）；波形 / 频谱模式再做对应分析并渲染成裸 BGRA。</summary>
		private static void Analyze(Side side, ViewMode mode, bool right, CancellationToken token)
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
					if (mode == ViewMode.Waveform)
					{
						side.Wave = MediaAudio.AnalyzeWaveform(stream, MediaRender.WaveWidth, MediaLimits.MaxAudioSeconds, token, out MediaFailure waveFailure);
						if (side.Wave == null && side.Failure == null)
						{
							side.Failure = waveFailure;
						}
						if (side.Wave != null)
						{
							side.WaveBgra = MediaRender.RenderWaveform(side.Wave, MediaRender.WaveWidth, MediaRender.WaveHeight, right);
						}
					}
					else if (mode == ViewMode.Spectrum)
					{
						side.Spectrum = MediaAudio.AnalyzeSpectrum(stream, MediaLimits.MaxSpectrumSeconds, token, out MediaFailure spectrumFailure);
						if (side.Spectrum == null && side.Failure == null)
						{
							side.Failure = spectrumFailure;
						}
						if (side.Spectrum != null)
						{
							side.SpectrumBgra = MediaRender.RenderSpectrum(side.Spectrum, MediaRender.SpectrumWidth, MediaRender.SpectrumHeight);
						}
					}
				}
			}
			catch (Exception ex)
			{
				side.Failure = new MediaFailure(MediaErrorKind.Failed, ex.GetType().Name + ": " + ex.Message);
				PluginLog.Warn("Audio: analysis failed", ex);
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

		private void BuildContent(ViewMode mode, bool blocked)
		{
			if (blocked)
			{
				_content.Content = new StackPanel
				{
					Margin = new Thickness(12.0),
					Children =
					{
						NoteText(AudioStrings.T("File too large to preview"), new Thickness(0.0))
					}
				};
				return;
			}
			switch (mode)
			{
				case ViewMode.Metadata:
					_content.Content = BuildMetadataContent();
					break;
				case ViewMode.Waveform:
					_content.Content = BuildWaveformContent();
					break;
				case ViewMode.Spectrum:
					_content.Content = BuildSpectrumContent();
					break;
				default:
					_content.Content = BuildCoverContent();
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

			// 音频流：逐条音频流对照（最多 4 条，避免超长）。
			MediaStreamInfo[] leftAudio = AudioStreams(left);
			MediaStreamInfo[] rightAudio = AudioStreams(right);
			int streamCount = Math.Max(leftAudio.Length, rightAudio.Length);
			for (int i = 0; i < Math.Min(streamCount, 4); i++)
			{
				MediaStreamInfo la = i < leftAudio.Length ? leftAudio[i] : null;
				MediaStreamInfo ra = i < rightAudio.Length ? rightAudio[i] : null;
				string prefix = AudioStrings.F("Stream") + " #" + (i + 1) + " · ";
				AddRow(rows, "Audio streams", prefix + AudioStrings.T("Codec"), Fmt(la?.CodecName), Fmt(ra?.CodecName));
				AddRow(rows, "Audio streams", prefix + AudioStrings.T("Sample rate"), SampleRate(la), SampleRate(ra));
				AddRow(rows, "Audio streams", prefix + AudioStrings.T("Channels"), Channels(la), Channels(ra));
				AddRow(rows, "Audio streams", prefix + AudioStrings.T("Channel layout"), Fmt(la?.ChannelLayout), Fmt(ra?.ChannelLayout));
				AddRow(rows, "Audio streams", prefix + AudioStrings.T("Bit rate"), BitRate(la?.BitRate ?? 0L), BitRate(ra?.BitRate ?? 0L));
			}

			// 标签：并集后按 key 排序。
			SortedDictionary<string, string> tagKeys = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			AddTagKeys(tagKeys, left);
			AddTagKeys(tagKeys, right);
			foreach (string key in tagKeys.Keys)
			{
				AddRow(rows, "Tags", TagLabel(key), TagValue(left, key), TagValue(right, key));
			}

			// 封面：有无 + 张数。
			AddRow(rows, "Tags", "Cover art", left?.Cover != null ? AudioStrings.T("Embedded") : null, right?.Cover != null ? AudioStrings.T("Embedded") : null);
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
			return TagLabels.TryGetValue(key, out string label) ? AudioStrings.T(label) : key;
		}

		private static string TagValue(MediaInfo info, string key)
		{
			if (info == null)
			{
				return null;
			}
			return info.Tags.TryGetValue(key, out string value) ? Fmt(value) : null;
		}

		private static MediaStreamInfo[] AudioStreams(MediaInfo info)
		{
			if (info == null)
			{
				return Array.Empty<MediaStreamInfo>();
			}
			return info.Streams.Where(delegate(MediaStreamInfo s)
			{
				return string.Equals(s.Kind, "audio", StringComparison.Ordinal);
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
						Text = AudioStrings.T(group),
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
				Text = row.Label + " : " + (present ? value : AudioStrings.T("not present")),
				FontSize = 11.5,
				Opacity = present ? 0.9 : 0.55,
				TextWrapping = TextWrapping.Wrap,
				VerticalAlignment = VerticalAlignment.Center
			};
			Grid.SetColumn(text, 0);
			grid.Children.Add(text);
			Border chip = Chip(Label(AudioStrings.T(StateKey(row.State)), 10.5, FontWeight.Normal, 0.8), ChipTint);
			chip.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
			Grid.SetColumn(chip, 1);
			grid.Children.Add(chip);
			return grid;
		}

		// ---- 波形模式 ----

		private Control BuildWaveformContent()
		{
			StackPanel root = new StackPanel
			{
				Margin = new Thickness(12.0, 0.0, 12.0, 12.0)
			};
			AddSideNotes(root);
			root.Children.Add(SectionTitle(AudioStrings.T("Waveform comparison")));
			root.Children.Add(TwoColumn(
				SidePanel(_context?.SrcRole ?? DiffSideRole.Old, _src, WavePanel(_src)),
				SidePanel(_context?.DstRole ?? DiffSideRole.New, _dst, WavePanel(_dst))));

			if (_src?.Wave != null && _dst?.Wave != null)
			{
				root.Children.Add(SectionTitle(AudioStrings.T("Difference")));
				byte[] diff = MediaRender.RenderDifference(_src.Wave, _dst.Wave, MediaRender.WaveWidth, MediaRender.DiffHeight);
				root.Children.Add(WideImage(diff, MediaRender.WaveWidth, MediaRender.DiffHeight));
			}
			AddAnalyzedNote(root, _src?.Wave?.Truncated == true || _dst?.Wave?.Truncated == true);
			return root;
		}

		private Control WavePanel(Side side)
		{
			if (side == null || side.Bytes == null)
			{
				return NoteText(side == null ? AudioStrings.T("not present") : AudioStrings.T("Media content unavailable"), new Thickness(0.0, 4.0, 0.0, 8.0));
			}
			if (side.Wave == null || side.WaveBgra == null)
			{
				return NoteText(DescribeFailure(side, "No waveform data"), new Thickness(0.0, 4.0, 0.0, 8.0));
			}
			StackPanel panel = new StackPanel();
			Bitmap bitmap = MediaImage.FromBgra(side.WaveBgra, MediaRender.WaveWidth, MediaRender.WaveHeight);
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
			panel.Children.Add(Label(WaveCaption(side), 11.0, FontWeight.Normal, 0.7));
			return panel;
		}

		private static string WaveCaption(Side side)
		{
			List<string> parts = new List<string>();
			if (side?.Wave != null)
			{
				parts.Add(AudioStrings.F("{0} Hz", side.Wave.SampleRate));
				parts.Add(AudioStrings.F("{0} ch", side.Wave.Channels));
			}
			if (side?.Info != null && side.Info.DurationSeconds > 0.0)
			{
				parts.Add(Duration(side.Info.DurationSeconds));
			}
			return string.Join("  ·  ", parts);
		}

		// ---- 频谱模式 ----

		private Control BuildSpectrumContent()
		{
			StackPanel root = new StackPanel
			{
				Margin = new Thickness(12.0, 0.0, 12.0, 12.0)
			};
			AddSideNotes(root);
			root.Children.Add(SectionTitle(AudioStrings.T("Spectrum comparison")));
			root.Children.Add(TwoColumn(
				SidePanel(_context?.SrcRole ?? DiffSideRole.Old, _src, SpectrumPanel(_src)),
				SidePanel(_context?.DstRole ?? DiffSideRole.New, _dst, SpectrumPanel(_dst))));
			AddAnalyzedNote(root, _src?.Spectrum?.Truncated == true || _dst?.Spectrum?.Truncated == true);
			return root;
		}

		private Control SpectrumPanel(Side side)
		{
			if (side == null || side.Bytes == null)
			{
				return NoteText(side == null ? AudioStrings.T("not present") : AudioStrings.T("Media content unavailable"), new Thickness(0.0, 4.0, 0.0, 8.0));
			}
			if (side.SpectrumBgra == null)
			{
				return NoteText(DescribeFailure(side, "No spectrum data"), new Thickness(0.0, 4.0, 0.0, 8.0));
			}
			Bitmap bitmap = MediaImage.FromBgra(side.SpectrumBgra, MediaRender.SpectrumWidth, MediaRender.SpectrumHeight);
			StackPanel panel = new StackPanel();
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
			panel.Children.Add(Label(AudioStrings.F("{0} Hz", side.Spectrum.SampleRate), 11.0, FontWeight.Normal, 0.7));
			return panel;
		}

		// ---- 封面模式 ----

		private Control BuildCoverContent()
		{
			StackPanel root = new StackPanel
			{
				Margin = new Thickness(12.0, 0.0, 12.0, 12.0)
			};
			AddSideNotes(root);
			root.Children.Add(SectionTitle(AudioStrings.T("Cover comparison")));
			root.Children.Add(TwoColumn(
				SidePanel(_context?.SrcRole ?? DiffSideRole.Old, _src, CoverPanel(_src)),
				SidePanel(_context?.DstRole ?? DiffSideRole.New, _dst, CoverPanel(_dst))));
			return root;
		}

		private Control CoverPanel(Side side)
		{
			if (side == null || side.Bytes == null)
			{
				return NoteText(side == null ? AudioStrings.T("not present") : AudioStrings.T("Media content unavailable"), new Thickness(0.0, 4.0, 0.0, 8.0));
			}
			ImageData cover = side.Info?.Cover;
			if (cover == null)
			{
				return NoteText(AudioStrings.T("No embedded cover"), new Thickness(0.0, 4.0, 0.0, 12.0));
			}
			Bitmap bitmap = MediaImage.FromImageData(cover);
			StackPanel panel = new StackPanel();
			if (bitmap != null)
			{
				panel.Children.Add(new Image
				{
					Source = bitmap,
					Stretch = Stretch.Uniform,
					MaxWidth = 360.0,
					MaxHeight = 360.0,
					HorizontalAlignment = HorizontalAlignment.Left,
					Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
				});
			}
			panel.Children.Add(Label(cover.Width + " × " + cover.Height, 11.0, FontWeight.Normal, 0.7));
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
				return AudioStrings.T("not present");
			}
			if (side.Bytes == null)
			{
				return AudioStrings.T("Media content unavailable");
			}
			if (side.Info == null)
			{
				return DescribeFailure(side, "Unsupported media format");
			}
			return null;
		}

		private static string DescribeFailure(Side side, string fallback)
		{
			if (side?.Failure == null)
			{
				return AudioStrings.T(fallback);
			}
			switch (side.Failure.Kind)
			{
				case MediaErrorKind.Unavailable:
					return AudioStrings.T("FFmpeg decoding unavailable");
				case MediaErrorKind.NoData:
					return AudioStrings.T("Media content unavailable");
				case MediaErrorKind.Unsupported:
					return AudioStrings.T("Unsupported media format");
				case MediaErrorKind.TooLarge:
					return AudioStrings.T("File too large to preview");
				default:
					return AudioStrings.T("Failed to decode media");
			}
		}

		private void AddAnalyzedNote(StackPanel root, bool truncated)
		{
			if (!truncated)
			{
				return;
			}
			double analyzed = _src?.Wave?.AnalyzedSeconds ?? 0.0;
			if (analyzed <= 0.0)
			{
				analyzed = _dst?.Wave?.AnalyzedSeconds ?? 0.0;
			}
			double total = _src?.Wave?.TotalSeconds ?? 0.0;
			if (total <= 0.0)
			{
				total = _dst?.Wave?.TotalSeconds ?? 0.0;
			}
			root.Children.Add(NoteText(AudioStrings.F("Analyzed first {0} of {1} s", analyzed.ToString("0.#", CultureInfo.InvariantCulture), total.ToString("0.#", CultureInfo.InvariantCulture)), new Thickness(0.0, 6.0, 0.0, 0.0)));
		}

		private Control SidePanel(DiffSideRole role, Side side, Control content)
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

		private static Control WideImage(byte[] bgra, int width, int height)
		{
			Bitmap bitmap = MediaImage.FromBgra(bgra, width, height);
			if (bitmap == null)
			{
				return NoteText(AudioStrings.T("No waveform data"), new Thickness(0.0, 4.0, 0.0, 0.0));
			}
			return new Image
			{
				Source = bitmap,
				Stretch = Stretch.Uniform,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
			};
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
			return AudioStrings.F("Audio compare: {0}", (srcName ?? "-") + " / " + (dstName ?? "-"));
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
				? AudioStrings.F("{0} kbps", (bitsPerSecond / 1000L).ToString(CultureInfo.InvariantCulture))
				: null;
		}

		private static string Size(long bytes)
		{
			return bytes > 0L ? PluginSizeFormat.ReadableFileSize(bytes, false) : null;
		}

		private static string SampleRate(MediaStreamInfo stream)
		{
			return stream != null && stream.SampleRate > 0 ? AudioStrings.F("{0} Hz", stream.SampleRate.ToString(CultureInfo.InvariantCulture)) : null;
		}

		private static string Channels(MediaStreamInfo stream)
		{
			return stream != null && stream.Channels > 0 ? AudioStrings.F("{0} ch", stream.Channels.ToString(CultureInfo.InvariantCulture)) : null;
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
				return text + "  ·  " + AudioStrings.T("not present");
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
