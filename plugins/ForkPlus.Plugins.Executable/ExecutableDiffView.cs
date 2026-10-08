using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Executable
{
	/// <summary>逐项对比的四色标注：相同 / 变了 / 仅左 / 仅右。</summary>
	internal enum DiffMark
	{
		Same,
		Changed,
		LeftOnly,
		RightOnly
	}

	/// <summary>
	/// 可执行文件 / 库对比视图：左右两栏并排，各自把旧（左）/ 新（右）二进制解析成结构模型，
	/// 逐项标注「相同 / 变了 / 仅左 / 仅右」。两栏标题取宿主注入的主题画刷；模式工具栏自建
	/// 分段按钮，在「结构摘要 / 段·节表 / 导入导出 / 体积构成」间切换。
	///
	/// 布局：<c>Auto,Auto,Auto,*</c> = 两栏标题行 / 状态行 / 模式工具栏 / 内容区（左右两栏，
	/// 各套一层 <see cref="ScrollViewer"/>）。解析在后台线程进行，控件只在 UI 线程构建；
	/// 代次（generation）+ <see cref="CancellationToken"/> 保证上一轮解析被取消且过期结果被丢弃。
	///
	/// 字节来源与 <c>ArchiveDiffView</c> 一致：<c>HexSrc/HexDst</c> 预载 &gt; 侧内容懒加载 &gt;
	/// 宿主 LFS 缓存 / smudge。
	/// </summary>
	public sealed class ExecutableDiffView : IDiffView
	{
		private static readonly IBrush GridLine = Brushes.Gainsboro;

		/// <summary>中性色都用带透明度的灰，浅色 / 深色主题下都保持低对比。</summary>
		private static readonly IBrush Subtle = new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x80, 0x80));

		private static readonly IBrush ChipTint = new SolidColorBrush(Color.FromArgb(0x1F, 0x80, 0x80, 0x80));

		/// <summary>十六进制 / 符号等定宽文本用等宽字体；按可用性回退。</summary>
		private static readonly FontFamily MonoFont = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, Courier New, monospace");

		// ---- 四色标注 ----
		private static readonly Color SameColor = Color.FromArgb(0xB8, 0x8A, 0x8A, 0x8A);

		private static readonly Color ChangedColor = Color.FromArgb(0xFF, 0xE0, 0x9B, 0x2E);

		private static readonly Color LeftOnlyColor = Color.FromArgb(0xFF, 0xD9, 0x54, 0x4F);

		private static readonly Color RightOnlyColor = Color.FromArgb(0xFF, 0x4C, 0x9A, 0x52);

		private static readonly IBrush SameBrush = new SolidColorBrush(SameColor);

		private static readonly IBrush ChangedBrush = new SolidColorBrush(ChangedColor);

		private static readonly IBrush LeftOnlyBrush = new SolidColorBrush(LeftOnlyColor);

		private static readonly IBrush RightOnlyBrush = new SolidColorBrush(RightOnlyColor);

		/// <summary>体积构成色块调色板（循环取用）。</summary>
		private static readonly Color[] SegmentColors = new Color[12]
		{
			Color.FromArgb(0xFF, 0x4E, 0x79, 0xA7),
			Color.FromArgb(0xFF, 0xF2, 0x8E, 0x2B),
			Color.FromArgb(0xFF, 0xE1, 0x57, 0x59),
			Color.FromArgb(0xFF, 0x76, 0xB7, 0xB2),
			Color.FromArgb(0xFF, 0x59, 0xA1, 0x4F),
			Color.FromArgb(0xFF, 0xED, 0xC9, 0x48),
			Color.FromArgb(0xFF, 0xB0, 0x7A, 0xA1),
			Color.FromArgb(0xFF, 0xFF, 0x9D, 0xA7),
			Color.FromArgb(0xFF, 0x9C, 0x75, 0x5F),
			Color.FromArgb(0xFF, 0xBA, 0xB0, 0xAC),
			Color.FromArgb(0xFF, 0x86, 0xBC, 0xB6),
			Color.FromArgb(0xFF, 0xD3, 0x72, 0x95)
		};

		private static readonly Color OtherColor = Color.FromArgb(0xFF, 0xC9, 0xC9, 0xC9);

		private static readonly string[] ModeIds = new string[4] { "summary", "sections", "symbols", "size" };

		private static readonly string[] ModeKeys = new string[4] { "Summary", "Sections", "Symbols", "Size" };

		/// <summary>体积构成条形图最多切多少块（超出折叠进「其它」，避免超多段撑爆列数）。</summary>
		private const int MaxCompositionSegments = 24;

		private readonly Grid _root;

		private readonly TextBlock _srcTitle;

		private readonly TextBlock _dstTitle;

		private readonly TextBlock _status;

		private readonly StackPanel _toolbar;

		private readonly Dictionary<string, ToggleButton> _modeButtons = new Dictionary<string, ToggleButton>(StringComparer.Ordinal);

		private readonly ContentControl _srcPane;

		private readonly ContentControl _dstPane;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _renderGeneration;

		private bool _released;

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （sections / symbols / size，其余走默认摘要），据此逐模式取图；正常运行时该变量为空。
		/// </summary>
		private static readonly string InitialMode = ResolveInitialMode();

		private static string ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			foreach (string id in ModeIds)
			{
				if (string.Equals(id, forced, StringComparison.OrdinalIgnoreCase))
				{
					return id;
				}
			}
			return "summary";
		}

		private string _mode = InitialMode;

		private ExecutableModel _srcModel;

		private ExecutableModel _dstModel;

		public ExecutableDiffView()
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

			_toolbar = BuildToolbar();

			_srcPane = new ContentControl
			{
				Margin = new Thickness(12.0, 2.0, 10.0, 12.0),
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				VerticalContentAlignment = VerticalAlignment.Top,
			};
			_dstPane = new ContentControl
			{
				Margin = new Thickness(10.0, 2.0, 12.0, 12.0),
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				VerticalContentAlignment = VerticalAlignment.Top,
			};

			Grid columns = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,*"),
			};
			Border srcScroll = WrapPane(_srcPane, new Thickness(0.0));
			Grid.SetColumn(srcScroll, 0);
			columns.Children.Add(srcScroll);
			Border dstScroll = WrapPane(_dstPane, new Thickness(1.0, 0.0, 0.0, 0.0));
			Grid.SetColumn(dstScroll, 1);
			columns.Children.Add(dstScroll);

			_root = new Grid
			{
				RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
			};
			Grid.SetRow(header, 0);
			_root.Children.Add(header);
			Grid.SetRow(_status, 1);
			_root.Children.Add(_status);
			Grid.SetRow(_toolbar, 2);
			_root.Children.Add(_toolbar);
			Grid.SetRow(columns, 3);
			_root.Children.Add(columns);

			PluginEnvironment.ApplyLocalization(_root);
			UpdateStaticTexts();
			UpdateModeButtons();
		}

		// ---- IDiffView ----

		Control IDiffView.View => _root;

		/// <summary>本视图自带模式工具栏，不需要宿主渲染模式切换工具条。</summary>
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
			// CI 截图定位 marker：字符串须精确包含 ExecutableDiffView.SetContent。
			PluginLog.Info($"ExecutableDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			StartRender();
		}

		public void SetMode(string modeId)
		{
			if (string.IsNullOrEmpty(modeId) || !IsValidMode(modeId) || string.Equals(modeId, _mode, StringComparison.Ordinal))
			{
				return;
			}
			_mode = modeId;
			UpdateModeButtons();
			RenderContent();
		}

		public void Activate()
		{
		}

		public void Deactivate()
		{
		}

		/// <summary>
		/// 应用当前语言（宿主在语言切换时广播）：宿主 key 重刷 + 插件自带静态文案重算 +
		/// 复用同一渲染入口（沿用代次 + CancellationToken 取消上一轮）重跑解析与渲染。
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
			_srcPane.Content = null;
			_dstPane.Content = null;
			_srcTitle.Text = string.Empty;
			_dstTitle.Text = string.Empty;
			_status.Text = string.Empty;
		}

		// ---- 工具栏 / 静态文案 ----

		private StackPanel BuildToolbar()
		{
			StackPanel toolbar = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Margin = new Thickness(12.0, 0.0, 12.0, 6.0),
			};
			for (int i = 0; i < ModeIds.Length; i++)
			{
				ToggleButton button = new ToggleButton
				{
					Tag = ModeIds[i],
					Content = ExecutableStrings.T(ModeKeys[i]),
					FontSize = 12.0,
					Padding = new Thickness(10.0, 3.0, 10.0, 3.0),
				};
				button.Click += OnModeClicked;
				_modeButtons[ModeIds[i]] = button;
				toolbar.Children.Add(button);
			}
			return toolbar;
		}

		private void OnModeClicked(object sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			if (sender is ToggleButton button && button.Tag is string mode && IsValidMode(mode))
			{
				_mode = mode;
				UpdateModeButtons();
				// 若尚未解析完（模型为空）则只切按钮；解析完成后 PostModels 会重渲染。
				RenderContent();
			}
		}

		private void UpdateModeButtons()
		{
			foreach (KeyValuePair<string, ToggleButton> pair in _modeButtons)
			{
				pair.Value.IsChecked = string.Equals(pair.Key, _mode, StringComparison.Ordinal);
			}
		}

		private void UpdateStaticTexts()
		{
			for (int i = 0; i < ModeIds.Length; i++)
			{
				if (_modeButtons.TryGetValue(ModeIds[i], out ToggleButton button))
				{
					button.Content = ExecutableStrings.T(ModeKeys[i]);
				}
			}
		}

		private static bool IsValidMode(string modeId)
		{
			for (int i = 0; i < ModeIds.Length; i++)
			{
				if (string.Equals(ModeIds[i], modeId, StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		// ---- 解析管线 ----

		private void StartRender()
		{
			CancelRender();
			_srcModel = null;
			_dstModel = null;
			_srcPane.Content = null;
			_dstPane.Content = null;
			UpdateTitles();
			if (_context == null)
			{
				_status.Text = string.Empty;
				return;
			}
			_status.Text = ExecutableStrings.T("Parsing binary…");
			CancellationTokenSource cts = new CancellationTokenSource();
			_cts = cts;
			int generation = _renderGeneration;
			DiffViewContext context = _context;
			IDiffViewHost host = _host;
			Task.Run(() => ReadAsync(context, host, cts.Token, generation));
		}

		private async Task ReadAsync(DiffViewContext context, IDiffViewHost host, CancellationToken token, int generation)
		{
			try
			{
				byte[] srcBytes = await ResolveSideAsync(context?.Src, context?.HexSrc, host, token).ConfigureAwait(false);
				byte[] dstBytes = await ResolveSideAsync(context?.Dst, context?.HexDst, host, token).ConfigureAwait(false);
				if (token.IsCancellationRequested)
				{
					return;
				}
				ExecutableModel srcModel = srcBytes == null ? null : SafeParse(context?.Src?.Path, srcBytes);
				ExecutableModel dstModel = dstBytes == null ? null : SafeParse(context?.Dst?.Path, dstBytes);
				if (token.IsCancellationRequested)
				{
					return;
				}
				PostModels(generation, srcModel, dstModel);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				PluginLog.Error("ExecutableDiffView read failed", ex);
				PostStatus(generation, ExecutableStrings.T("Failed to parse binary") + ": " + ex.Message);
			}
		}

		/// <summary>解析一侧二进制；解析器自身已吞异常，这里再兜一层防止意外冒泡。</summary>
		private static ExecutableModel SafeParse(string path, byte[] bytes)
		{
			try
			{
				return ExecutableParser.Parse(path, bytes);
			}
			catch (Exception ex)
			{
				return ExecutableModel.Failed(ExecutableFormat.Unknown, ExecutableError.Corrupt, ex.Message, bytes.Length);
			}
		}

		/// <summary>取一侧字节：宿主 Hex 预载 &gt; 侧内容懒加载 &gt; LFS 缓存 / smudge（与 ArchiveDiffView 一致）。</summary>
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
				PluginLog.Warn("Executable: failed to load side bytes for '" + side.Path + "'", ex);
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
				PluginLog.Warn("Executable: LFS cache lookup failed", ex);
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

		private void PostModels(int generation, ExecutableModel src, ExecutableModel dst)
		{
			Dispatcher.UIThread.Post(delegate
			{
				if (_released || generation != _renderGeneration)
				{
					return;
				}
				_srcModel = src;
				_dstModel = dst;
				RenderContent();
			});
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

		private void UpdateTitles()
		{
			_srcTitle.Text = DescribeSide(_context?.Src, _context?.SrcRole ?? DiffSideRole.Old);
			_dstTitle.Text = DescribeSide(_context?.Dst, _context?.DstRole ?? DiffSideRole.New);
		}

		private static string DescribeSide(DiffSideContent side, DiffSideRole role)
		{
			string roleKey = role switch
			{
				DiffSideRole.Old => "old",
				DiffSideRole.New => "new",
				DiffSideRole.Created => "created",
				_ => "removed",
			};
			string text = PluginEnvironment.Translate(roleKey);
			if (side == null)
			{
				return text + "  ·  " + ExecutableStrings.T("not present");
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

		// ---- 渲染 ----

		private void RenderContent()
		{
			_srcPane.Content = BuildSideContent(0);
			_dstPane.Content = BuildSideContent(1);
			int srcCount = _srcModel?.SectionCount ?? 0;
			int dstCount = _dstModel?.SectionCount ?? 0;
			_status.Text = ExecutableStrings.F("Binary compare: {0} / {1} sections", srcCount, dstCount);
		}

		private Control BuildSideContent(int side)
		{
			DiffSideContent content = side == 0 ? _context?.Src : _context?.Dst;
			ExecutableModel model = side == 0 ? _srcModel : _dstModel;
			if (content == null)
			{
				return Message(ExecutableStrings.T("not present"));
			}
			if (model == null)
			{
				return Message(ExecutableStrings.T("Binary content unavailable") + Environment.NewLine + content.Path);
			}
			if (model.Error != ExecutableError.None)
			{
				return Message(DescribeError(model) + Environment.NewLine + content.Path);
			}
			return BuildMode(side, model);
		}

		private static string DescribeError(ExecutableModel model)
		{
			string text = model.Error == ExecutableError.Unsupported
				? ExecutableStrings.T("Unsupported binary format")
				: ExecutableStrings.T("Failed to parse binary");
			return string.IsNullOrEmpty(model.ErrorDetail) ? text : text + Environment.NewLine + model.ErrorDetail;
		}

		private Control BuildMode(int side, ExecutableModel model)
		{
			ExecutableModel other = side == 0 ? _dstModel : _srcModel;
			bool isSource = side == 0;
			switch (_mode)
			{
				case "sections":
					return BuildSections(model, other, isSource);
				case "symbols":
					return BuildSymbols(model, other, isSource);
				case "size":
					return BuildSize(model, other, isSource);
				default:
					return BuildSummary(model, other, isSource);
			}
		}

		// ---- 结构摘要 ----

		private Control BuildSummary(ExecutableModel model, ExecutableModel other, bool isSource)
		{
			StackPanel panel = NewSidePanel();
			panel.Children.Add(FormatBadge(model));
			panel.Children.Add(Chip(Label(PluginSizeFormat.ReadableFileSize(model.FileSize, false), 11.5, FontWeight.Normal, 0.75), Subtle));
			if (model.IsFat)
			{
				panel.Children.Add(Note(ExecutableStrings.F("fat binary ({0} architectures)", FatArchitectureCount(model))));
			}
			panel.Children.Add(GroupTitle(ExecutableStrings.T("Header"), model.Fields.Count));
			foreach (ExecField field in model.Fields)
			{
				DiffMark mark = FieldMark(model, other, field, isSource);
				panel.Children.Add(BuildFieldRow(field, mark));
			}
			panel.Children.Add(GroupTitle(ExecutableStrings.T("Assembly references"), model.AssemblyRefs.Count));
			if (model.AssemblyRefs.Count == 0)
			{
				panel.Children.Add(Note(ExecutableStrings.T("No assembly references")));
			}
			else
			{
				foreach (ExecAssemblyRef reference in model.AssemblyRefs)
				{
					DiffMark mark = AssemblyRefMark(other, reference, isSource);
					panel.Children.Add(BuildNamedRow(reference.Name, reference.Version, mark));
				}
			}
			if (!string.IsNullOrEmpty(model.Warning))
			{
				panel.Children.Add(Note(model.Warning));
			}
			return panel;
		}

		private static int FatArchitectureCount(ExecutableModel model)
		{
			ExecField field = model.FindField("Architecture");
			if (field == null || !field.Value.StartsWith("fat (", StringComparison.Ordinal))
			{
				return 0;
			}
			string inner = field.Value.Substring(5).TrimEnd(')');
			if (inner.Length == 0)
			{
				return 0;
			}
			return inner.Split(',').Length;
		}

		private static DiffMark FieldMark(ExecutableModel self, ExecutableModel other, ExecField field, bool isSource)
		{
			if (other == null)
			{
				return DiffMark.Same;
			}
			ExecField counterpart = other.FindField(field.Key);
			if (counterpart == null)
			{
				return isSource ? DiffMark.LeftOnly : DiffMark.RightOnly;
			}
			return string.Equals(counterpart.Value, field.Value, StringComparison.Ordinal) ? DiffMark.Same : DiffMark.Changed;
		}

		private Control BuildFieldRow(ExecField field, DiffMark mark)
		{
			StackPanel row = new StackPanel
			{
				Margin = new Thickness(0.0, 2.0, 0.0, 2.0),
			};
			StackPanel line = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
			};
			line.Children.Add(MarkDot(mark));
			line.Children.Add(Label(ExecutableStrings.T(field.Key), 12.0, FontWeight.SemiBold, 0.8));
			line.Children.Add(new SelectableTextBlock
			{
				Text = field.Value,
				FontFamily = MonoFont,
				FontSize = 11.5,
				Opacity = 0.85,
				TextWrapping = TextWrapping.NoWrap,
				VerticalAlignment = VerticalAlignment.Center,
			});
			row.Children.Add(line);
			if (field.Badges.Count > 0)
			{
				StackPanel badges = new StackPanel
				{
					Orientation = Orientation.Horizontal,
					Spacing = 4.0,
					Margin = new Thickness(18.0, 2.0, 0.0, 0.0),
				};
				foreach (string badge in field.Badges)
				{
					badges.Children.Add(Chip(Label(badge, 10.0, FontWeight.SemiBold, 0.9), Tint(MarkBrushColor(mark))));
				}
				row.Children.Add(badges);
			}
			return row;
		}

		// ---- 段 / 节表 ----

		private Control BuildSections(ExecutableModel model, ExecutableModel other, bool isSource)
		{
			StackPanel panel = NewSidePanel();
			panel.Children.Add(GroupTitle(ExecutableStrings.T("Sections"), model.Sections.Count));
			if (model.Sections.Count == 0)
			{
				panel.Children.Add(Note(ExecutableStrings.T("No entries")));
				return panel;
			}
			bool isAr = model.Format == ExecutableFormat.Ar;
			foreach (ExecSection section in model.Sections)
			{
				DiffMark mark = SectionMark(other, section, isSource);
				panel.Children.Add(BuildSectionRow(section, mark, isAr));
			}
			return panel;
		}

		private Control BuildSectionRow(ExecSection section, DiffMark mark, bool isAr)
		{
			StackPanel row = new StackPanel
			{
				Margin = new Thickness(0.0, 1.0, 0.0, 1.0),
			};
			Grid line = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
			};
			Border dot = MarkDot(mark);
			Grid.SetColumn(dot, 0);
			line.Children.Add(dot);
			TextBlock name = Label(section.Name, 12.0, FontWeight.Normal, 0.9);
			name.TextTrimming = TextTrimming.CharacterEllipsis;
			Grid.SetColumn(name, 1);
			line.Children.Add(name);
			TextBlock size = Label(PluginSizeFormat.ReadableFileSize(section.Size, false), 11.5, FontWeight.Normal, 0.7);
			size.FontFamily = MonoFont;
			size.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
			Grid.SetColumn(size, 2);
			line.Children.Add(size);
			Grid.SetColumn(BuildPermissionBadges(section), 3);
			line.Children.Add(BuildPermissionBadges(section));
			row.Children.Add(line);
			if (!string.IsNullOrEmpty(section.Label))
			{
				row.Children.Add(Indented(section.Label.TrimStart('/')));
			}
			if (isAr && section.Offset >= 0L)
			{
				string detail = ExecutableStrings.T("Offset") + " 0x" + section.Offset.ToString("X")
					+ "  ·  " + ExecutableStrings.T("Modified") + " " + FormatUnixTime(section.Timestamp);
				row.Children.Add(Indented(detail));
			}
			return row;
		}

		private Control BuildPermissionBadges(ExecSection section)
		{
			// 注意：先调用一次 BuildPermissionBadges 会新建控件——这里复用同一实例需一次性构建。
			StackPanel badges = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 4.0,
				Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
				VerticalAlignment = VerticalAlignment.Center,
			};
			if (section.Read)
			{
				badges.Children.Add(Chip(Label(ExecutableStrings.T("read"), 10.0, FontWeight.Normal, 0.85), Subtle));
			}
			if (section.Write)
			{
				badges.Children.Add(Chip(Label(ExecutableStrings.T("write"), 10.0, FontWeight.Normal, 0.85), Subtle));
			}
			if (section.Execute)
			{
				badges.Children.Add(Chip(Label(ExecutableStrings.T("execute"), 10.0, FontWeight.Normal, 0.85), Subtle));
			}
			if (!section.Read && !section.Write && !section.Execute)
			{
				badges.Children.Add(Label("—", 11.0, FontWeight.Normal, 0.5));
			}
			return badges;
		}

		private static DiffMark SectionMark(ExecutableModel other, ExecSection section, bool isSource)
		{
			if (other == null)
			{
				return DiffMark.Same;
			}
			ExecSection counterpart = other.FindSection(section.Name);
			if (counterpart == null)
			{
				return isSource ? DiffMark.LeftOnly : DiffMark.RightOnly;
			}
			return counterpart.Size == section.Size
				&& counterpart.Read == section.Read
				&& counterpart.Write == section.Write
				&& counterpart.Execute == section.Execute
				? DiffMark.Same
				: DiffMark.Changed;
		}

		// ---- 导入 / 导出符号 ----

		private Control BuildSymbols(ExecutableModel model, ExecutableModel other, bool isSource)
		{
			StackPanel panel = NewSidePanel();
			List<string> dependencyNames = new List<string>();
			foreach (ExecDependency dependency in model.Dependencies)
			{
				dependencyNames.Add(dependency.Name);
			}
			List<string> imports = CollectImports(model);
			List<string> exports = new List<string>(model.Exports);

			AddSymbolCategory(panel, ExecutableStrings.T("Dependencies"), Distinct(dependencyNames), OtherDistinct(other, true), isSource);
			AddSymbolCategory(panel, ExecutableStrings.T("Imports"), Distinct(imports), OtherDistinct(other, false, true), isSource);
			AddSymbolCategory(panel, ExecutableStrings.T("Exports"), Distinct(exports), OtherDistinct(other, false, false), isSource);
			return panel;
		}

		private static List<string> CollectImports(ExecutableModel model)
		{
			List<string> list = new List<string>(model.Imports);
			foreach (ExecDependency dependency in model.Dependencies)
			{
				foreach (string symbol in dependency.Symbols)
				{
					list.Add(symbol);
				}
			}
			return list;
		}

		private static List<string> OtherDistinct(ExecutableModel other, bool dependencies, bool imports = false)
		{
			if (other == null)
			{
				return new List<string>();
			}
			if (dependencies)
			{
				List<string> names = new List<string>();
				foreach (ExecDependency dependency in other.Dependencies)
				{
					names.Add(dependency.Name);
				}
				return Distinct(names);
			}
			if (imports)
			{
				return Distinct(CollectImports(other));
			}
			return Distinct(other.Exports);
		}

		/// <summary>每个分类下按「共有 / 仅左 / 仅右」三色分组呈现（分组标题 + 计数）。</summary>
		private void AddSymbolCategory(StackPanel panel, string category, List<string> self, List<string> other, bool isSource)
		{
			panel.Children.Add(GroupTitle(category, self.Count));
			HashSet<string> otherSet = new HashSet<string>(other, StringComparer.Ordinal);
			HashSet<string> selfSet = new HashSet<string>(self, StringComparer.Ordinal);
			List<string> shared = new List<string>();
			List<string> selfOnly = new List<string>();
			foreach (string value in self)
			{
				if (otherSet.Contains(value))
				{
					shared.Add(value);
				}
				else
				{
					selfOnly.Add(value);
				}
			}
			List<string> otherOnly = new List<string>();
			foreach (string value in other)
			{
				if (!selfSet.Contains(value))
				{
					otherOnly.Add(value);
				}
			}
			AddSymbolGroup(panel, ExecutableStrings.T("shared"), shared, DiffMark.Same);
			AddSymbolGroup(panel, ExecutableStrings.T(isSource ? "left only" : "right only"), selfOnly, isSource ? DiffMark.LeftOnly : DiffMark.RightOnly);
			AddSymbolGroup(panel, ExecutableStrings.T(isSource ? "right only" : "left only"), otherOnly, isSource ? DiffMark.RightOnly : DiffMark.LeftOnly);
		}

		private void AddSymbolGroup(StackPanel panel, string title, List<string> values, DiffMark mark)
		{
			if (values.Count == 0)
			{
				return;
			}
			StackPanel header = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Margin = new Thickness(6.0, 4.0, 0.0, 1.0),
			};
			header.Children.Add(MarkDot(mark));
			header.Children.Add(Label(title + "  ·  " + values.Count, 11.0, FontWeight.SemiBold, 0.7));
			panel.Children.Add(header);
			foreach (string value in values)
			{
				panel.Children.Add(BuildNamedRow(value, null, mark));
			}
		}

		// ---- 体积构成 ----

		private Control BuildSize(ExecutableModel model, ExecutableModel other, bool isSource)
		{
			StackPanel panel = NewSidePanel();
			panel.Children.Add(GroupTitle(ExecutableStrings.T("Size composition"), model.Sections.Count));
			if (model.Sections.Count == 0)
			{
				panel.Children.Add(Note(ExecutableStrings.T("No entries")));
				return panel;
			}
			List<Segment> segments = BuildSegments(model);
			panel.Children.Add(BuildStackedBar(segments));
			panel.Children.Add(BuildLegend(segments));
			panel.Children.Add(GroupTitle(ExecutableStrings.T("Sections"), model.Sections.Count));
			foreach (ExecSection section in model.Sections)
			{
				DiffMark mark = SectionMark(other, section, isSource);
				panel.Children.Add(BuildSizeRow(section, mark, model.FileSize));
			}
			return panel;
		}

		private static Control BuildStackedBar(List<Segment> segments)
		{
			Grid bar = new Grid
			{
				Height = 18.0,
				Margin = new Thickness(0.0, 2.0, 0.0, 4.0),
			};
			long total = TotalSize(segments);
			for (int i = 0; i < segments.Count; i++)
			{
				double weight = total > 0L ? Math.Max((double)segments[i].Size, 1.0) : 1.0;
				ColumnDefinition column = new ColumnDefinition();
				column.Width = new GridLength(weight, GridUnitType.Star);
				bar.ColumnDefinitions.Add(column);
				Border block = new Border
				{
					Background = new SolidColorBrush(segments[i].Color),
				};
				Grid.SetColumn(block, i);
				bar.Children.Add(block);
			}
			return bar;
		}

		private static Control BuildLegend(List<Segment> segments)
		{
			WrapPanel legend = new WrapPanel
			{
				Orientation = Orientation.Horizontal,
				Margin = new Thickness(0.0, 0.0, 0.0, 6.0),
			};
			foreach (Segment segment in segments)
			{
				StackPanel item = new StackPanel
				{
					Orientation = Orientation.Horizontal,
					Spacing = 4.0,
					Margin = new Thickness(0.0, 2.0, 12.0, 2.0),
				};
				item.Children.Add(new Border
				{
					Width = 9.0,
					Height = 9.0,
					CornerRadius = new CornerRadius(2.0),
					Background = new SolidColorBrush(segment.Color),
					VerticalAlignment = VerticalAlignment.Center,
				});
				item.Children.Add(Label(segment.Name, 10.5, FontWeight.Normal, 0.75));
				legend.Children.Add(item);
			}
			return legend;
		}

		private Control BuildSizeRow(ExecSection section, DiffMark mark, long fileSize)
		{
			Grid row = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
				Margin = new Thickness(0.0, 1.0, 0.0, 1.0),
			};
			Border dot = MarkDot(mark);
			Grid.SetColumn(dot, 0);
			row.Children.Add(dot);
			TextBlock name = Label(section.Name, 12.0, FontWeight.Normal, 0.9);
			name.TextTrimming = TextTrimming.CharacterEllipsis;
			Grid.SetColumn(name, 1);
			row.Children.Add(name);
			TextBlock size = Label(PluginSizeFormat.ReadableFileSize(section.Size, false), 11.5, FontWeight.Normal, 0.7);
			size.FontFamily = MonoFont;
			size.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
			Grid.SetColumn(size, 2);
			row.Children.Add(size);
			TextBlock percent = Label(FormatPercent(section.Size, fileSize), 11.5, FontWeight.Normal, 0.6);
			percent.FontFamily = MonoFont;
			percent.Margin = new Thickness(10.0, 0.0, 0.0, 0.0);
			Grid.SetColumn(percent, 3);
			row.Children.Add(percent);
			return row;
		}

		private static string FormatPercent(long size, long total)
		{
			double percent = total > 0L ? (double)size * 100.0 / total : 0.0;
			return percent.ToString("F1") + "%";
		}

		private static List<Segment> BuildSegments(ExecutableModel model)
		{
			List<ExecSection> sections = new List<ExecSection>(model.Sections);
			sections.Sort(delegate (ExecSection a, ExecSection b) { return b.Size.CompareTo(a.Size); });
			List<Segment> segments = new List<Segment>();
			long accounted = 0L;
			for (int i = 0; i < sections.Count && segments.Count < MaxCompositionSegments; i++)
			{
				segments.Add(new Segment(sections[i].Name, sections[i].Size, SegmentColors[i % SegmentColors.Length]));
				accounted += sections[i].Size;
			}
			long rest = model.FileSize - accounted;
			if (rest > 0L || (sections.Count == 0 && model.FileSize > 0L))
			{
				segments.Add(new Segment(ExecutableStrings.T("Other"), rest > 0L ? rest : model.FileSize, OtherColor));
			}
			return segments;
		}

		private static long TotalSize(List<Segment> segments)
		{
			long total = 0L;
			foreach (Segment segment in segments)
			{
				total += segment.Size;
			}
			return total;
		}

		private sealed class Segment
		{
			public Segment(string name, long size, Color color)
			{
				Name = name;
				Size = size;
				Color = color;
			}

			public string Name { get; }

			public long Size { get; }

			public Color Color { get; }
		}

		// ---- 通用 UI 辅助 ----

		/// <summary>栏容器：一道分隔线 + 内容占位；内套 <see cref="ScrollViewer"/> 让长内容可滚动。</summary>
		private static Border WrapPane(Control content, Thickness separator)
		{
			ScrollViewer scroll = new ScrollViewer
			{
				Content = content,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
			};
			return new Border
			{
				BorderBrush = GridLine,
				BorderThickness = separator,
				Child = scroll,
			};
		}

		private static StackPanel NewSidePanel()
		{
			return new StackPanel
			{
				Margin = new Thickness(0.0, 2.0, 6.0, 8.0),
			};
		}

		private static TextBlock NewTitle()
		{
			return new TextBlock
			{
				Margin = new Thickness(12.0, 8.0, 12.0, 4.0),
				FontWeight = FontWeight.SemiBold,
				TextTrimming = TextTrimming.CharacterEllipsis,
			};
		}

		private static Control Message(string text)
		{
			return new TextBlock
			{
				Text = text,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, 4.0, 0.0, 8.0),
				Opacity = 0.7,
			};
		}

		private static Border FormatBadge(ExecutableModel model)
		{
			return new Border
			{
				BorderThickness = new Thickness(3.0, 0.0, 0.0, 0.0),
				BorderBrush = new SolidColorBrush(AccentColor(model.Format)),
				Background = ChipTint,
				CornerRadius = new CornerRadius(4.0),
				Padding = new Thickness(8.0, 3.0, 9.0, 3.0),
				Margin = new Thickness(0.0, 0.0, 0.0, 6.0),
				HorizontalAlignment = HorizontalAlignment.Left,
				Child = Label(ExecutableStrings.T(ExecutableParser.FormatName(model.Format)), 12.0, FontWeight.SemiBold, 1.0),
			};
		}

		private static Color AccentColor(ExecutableFormat format)
		{
			switch (format)
			{
				case ExecutableFormat.PE:
					return Color.FromArgb(0xFF, 0x3B, 0x78, 0xCC);
				case ExecutableFormat.ELF:
					return Color.FromArgb(0xFF, 0xE0, 0x8A, 0x2B);
				case ExecutableFormat.MachO:
					return Color.FromArgb(0xFF, 0x8E, 0x6B, 0xC8);
				case ExecutableFormat.WebAssembly:
					return Color.FromArgb(0xFF, 0x3F, 0xA4, 0x6A);
				default:
					return Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A);
			}
		}

		private static TextBlock GroupTitle(string text, int count)
		{
			return new TextBlock
			{
				Text = text + "  ·  " + count,
				FontSize = 12.5,
				FontWeight = FontWeight.SemiBold,
				Opacity = 0.85,
				Margin = new Thickness(0.0, 10.0, 0.0, 4.0),
				TextWrapping = TextWrapping.NoWrap,
			};
		}

		private static TextBlock Note(string text)
		{
			return new TextBlock
			{
				Text = text,
				FontSize = 11.0,
				Opacity = 0.6,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, 3.0, 0.0, 0.0),
			};
		}

		private static TextBlock Indented(string text)
		{
			return new TextBlock
			{
				Text = text,
				FontSize = 10.5,
				Opacity = 0.55,
				Margin = new Thickness(18.0, 0.0, 0.0, 0.0),
				TextWrapping = TextWrapping.NoWrap,
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
				Margin = new Thickness(0.0, 0.0, 0.0, 4.0),
				HorizontalAlignment = HorizontalAlignment.Left,
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

		private static Border MarkDot(DiffMark mark)
		{
			return new Border
			{
				Width = 8.0,
				Height = 8.0,
				CornerRadius = new CornerRadius(4.0),
				Background = MarkBrush(mark),
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0.0, 0.0, 6.0, 0.0),
			};
		}

		private static Control BuildNamedRow(string name, string detail, DiffMark mark)
		{
			StackPanel line = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 1.0,
				Margin = new Thickness(18.0, 1.0, 0.0, 1.0),
			};
			line.Children.Add(MarkDot(mark));
			TextBlock nameLabel = Label(name, 11.5, FontWeight.Normal, 0.85);
			nameLabel.FontFamily = MonoFont;
			nameLabel.TextTrimming = TextTrimming.CharacterEllipsis;
			line.Children.Add(nameLabel);
			if (!string.IsNullOrEmpty(detail))
			{
				TextBlock detailLabel = Label(detail, 11.0, FontWeight.Normal, 0.55);
				detailLabel.FontFamily = MonoFont;
				detailLabel.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
				line.Children.Add(detailLabel);
			}
			return line;
		}

		private static IBrush MarkBrush(DiffMark mark)
		{
			switch (mark)
			{
				case DiffMark.Changed:
					return ChangedBrush;
				case DiffMark.LeftOnly:
					return LeftOnlyBrush;
				case DiffMark.RightOnly:
					return RightOnlyBrush;
				default:
					return SameBrush;
			}
		}

		private static Color MarkBrushColor(DiffMark mark)
		{
			switch (mark)
			{
				case DiffMark.Changed:
					return ChangedColor;
				case DiffMark.LeftOnly:
					return LeftOnlyColor;
				case DiffMark.RightOnly:
					return RightOnlyColor;
				default:
					return SameColor;
			}
		}

		private static IBrush Tint(Color color)
		{
			return new SolidColorBrush(Color.FromArgb(0x28, color.R, color.G, color.B));
		}

		private static List<string> Distinct(IReadOnlyList<string> values)
		{
			List<string> list = new List<string>();
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (string value in values)
			{
				if (!string.IsNullOrEmpty(value) && seen.Add(value))
				{
					list.Add(value);
				}
			}
			list.Sort(StringComparer.Ordinal);
			return list;
		}

		private static ExecAssemblyRef FindAssemblyRef(ExecutableModel model, string name)
		{
			foreach (ExecAssemblyRef reference in model.AssemblyRefs)
			{
				if (string.Equals(reference.Name, name, StringComparison.Ordinal))
				{
					return reference;
				}
			}
			return null;
		}

		private static DiffMark AssemblyRefMark(ExecutableModel other, ExecAssemblyRef reference, bool isSource)
		{
			if (other == null)
			{
				return DiffMark.Same;
			}
			ExecAssemblyRef counterpart = FindAssemblyRef(other, reference.Name);
			if (counterpart == null)
			{
				return isSource ? DiffMark.LeftOnly : DiffMark.RightOnly;
			}
			return string.Equals(counterpart.Version, reference.Version, StringComparison.Ordinal) ? DiffMark.Same : DiffMark.Changed;
		}

		private static string FormatUnixTime(long seconds)
		{
			if (seconds <= 0L)
			{
				return "-";
			}
			try
			{
				return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") + "Z";
			}
			catch (ArgumentOutOfRangeException)
			{
				return "-";
			}
		}
	}
}
