using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.MlModel
{
	/// <summary>
	/// 机器学习模型对比视图：把旧（左）/ 新（右）两侧的 ONNX / SafeTensors / GGUF 解析成统一的
	/// 「键路径 → 值」行，按键路径取并集做语义 diff，逐行判「相同 / 已变 / 仅左 / 仅右」四类。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、
	/// 自带的分段模式工具条（Metadata / Tensors），最下是内容区。
	///
	/// 两个模式（diff 一次算全，模式只做展示过滤）：
	/// <list type="bullet">
	/// <item>Metadata（默认）：只显示 Kind=Meta 的行（producer / 版本 / KV 配置 / 算子直方图…）。</item>
	/// <item>Tensors：只显示 Kind=Tensor 的行（node / input / output / initializer / 张量条目…）。</item>
	/// </list>
	///
	/// 内容是一张四列表——「键路径 | 旧值 | 新值 | 状态」，逐行按四色标注底色，键路径等宽字体。
	///
	/// 解析与 diff 在后台线程执行，控件只在 UI 线程构建；每次刷新用代次 + CancellationToken 取消上一轮。
	/// 单侧声明大小 &gt; 300 MB 只给提示、不解析。
	/// </summary>
	public sealed class MlModelDiffView : IDiffView
	{
		private const string MetaMode = "metadata";

		private const string TensorsMode = "tensors";

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （tensors，其余走默认 metadata），据此逐模式取图；正常运行时该变量为空，走默认 Metadata 模式。
		/// </summary>
		private static readonly string InitialMode = ResolveInitialMode();

		private static string ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			return string.Equals(forced, TensorsMode, StringComparison.OrdinalIgnoreCase) ? TensorsMode : MetaMode;
		}

		/// <summary>单侧超过此大小不解析（与字幕 / 结构化数据插件同口径）。</summary>
		private const long MaxSideBytes = 300L * 1024L * 1024L;

		/// <summary>渲染上限：避免超大模型把 UI 拖死。</summary>
		private const int MaxRows = 3000;

		/// <summary>单元格里单值最大展示长度，超出截断（不影响 diff 比较）。</summary>
		private const int MaxValueChars = 300;

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

		private readonly Button _metaButton;

		private readonly Button _tensorsButton;

		private readonly ContentControl _content;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _generation;

		private string _mode = InitialMode;

		private MlModel _left;

		private MlModel _right;

		private MlDiffResult _diff;

		private string _error;

		/// <summary>左 / 右两侧各自的解析错误（侧存在但解析失败时用于区分「解析失败」与「不存在」）。</summary>
		private string _srcParseError;

		private string _dstParseError;

		private string _srcFormat = "?";

		private string _dstFormat = "?";

		private bool _srcAbsent;

		private bool _dstAbsent;

		private bool _tooLarge;

		public MlModelDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			_status = new TextBlock
			{
				Margin = new Thickness(12.0, 2.0, 12.0, 6.0),
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0
			};
			_metaButton = NewModeButton(MlModelStrings.T("Metadata"), delegate
			{
				SetMode(MetaMode);
			});
			_tensorsButton = NewModeButton(MlModelStrings.T("Tensors"), delegate
			{
				SetMode(TensorsMode);
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
			modes.Children.Add(_metaButton);
			modes.Children.Add(_tensorsButton);

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

			StyleModeButton(_metaButton, _mode == MetaMode);
			StyleModeButton(_tensorsButton, _mode == TensorsMode);
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
			_srcFormat = FormatOf(context?.Src) ?? "?";
			_dstFormat = FormatOf(context?.Dst) ?? "?";
			UpdateTitles();
			PluginLog.Info($"MlModelDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			Render();
		}

		public void SetMode(string modeId)
		{
			if (string.IsNullOrEmpty(modeId))
			{
				return;
			}
			if (modeId != MetaMode && modeId != TensorsMode)
			{
				return;
			}
			if (modeId == _mode)
			{
				return;
			}
			_mode = modeId;
			StyleModeButton(_metaButton, _mode == MetaMode);
			StyleModeButton(_tensorsButton, _mode == TensorsMode);
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
			_metaButton.Content = MlModelStrings.T("Metadata");
			_tensorsButton.Content = MlModelStrings.T("Tensors");
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
			string srcFormat = _srcFormat;
			string dstFormat = _dstFormat;

			Task.Run(delegate
			{
				MlModel left = null;
				MlModel right = null;
				string srcParseError = null;
				string dstParseError = null;
				try
				{
					byte[] leftBytes = ReadBytes(src, hexSrc, cts.Token);
					byte[] rightBytes = ReadBytes(dst, hexDst, cts.Token);
					cts.Token.ThrowIfCancellationRequested();
					if (src != null)
					{
						left = MlModelParser.Parse(leftBytes, srcFormat, out srcParseError);
					}
					if (dst != null)
					{
						right = MlModelParser.Parse(rightBytes, dstFormat, out dstParseError);
					}
					if (cts.Token.IsCancellationRequested)
					{
						return;
					}
					MlDiffResult diff = MlModelDiff.Compute(left, right);
					Dispatcher.UIThread.Post(delegate
					{
						if (_generation != generation || _cts != cts)
						{
							return;
						}
						_left = left;
						_right = right;
						_diff = diff;
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
					PluginLog.Error("MlModelDiffView 渲染失败", ex);
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

		/// <summary>取一侧字节：宿主 Hex 预载 &gt; 侧内容懒加载 &gt; LFS 本地缓存。</summary>
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
			return CopyBytes(data);
		}

		/// <summary>把流内容拷成 byte[]（读前保存 / 读后恢复 Position，不破坏宿主对同一流的其它使用）。</summary>
		private static byte[] CopyBytes(MemoryStream stream)
		{
			if (stream == null)
			{
				return null;
			}
			long position = stream.Position;
			try
			{
				stream.Position = 0L;
				byte[] copy = new byte[(int)stream.Length];
				int read = 0;
				while (read < copy.Length)
				{
					int block = stream.Read(copy, read, copy.Length - read);
					if (block <= 0)
					{
						break;
					}
					read += block;
				}
				if (read < copy.Length)
				{
					byte[] exact = new byte[read];
					Buffer.BlockCopy(copy, 0, exact, 0, read);
					return exact;
				}
				return copy;
			}
			finally
			{
				try
				{
					stream.Position = position;
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
				return roleText + "  ·  " + MlModelStrings.T("not present");
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

		private static string FormatOf(DiffSideContent side)
		{
			if (side?.Path == null)
			{
				return null;
			}
			string extension = Path.GetExtension(side.Path);
			if (string.IsNullOrEmpty(extension))
			{
				return "?";
			}
			return extension.TrimStart('.').ToLowerInvariant();
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
				_status.Text = MlModelStrings.T("File too large to preview");
				return;
			}
			string error = DisplayError();
			if (error != null)
			{
				_status.Text = MlModelStrings.F("Failed to parse: {0}", error);
				return;
			}
			if (_diff == null)
			{
				_status.Text = MlModelStrings.T("Analyzing…");
				return;
			}
			string head = MlModelStrings.F("ML model compare: {0} / {1}", DisplayFormat(_left, _srcFormat), DisplayFormat(_right, _dstFormat));
			head = head + "  ·  " + MlModelStrings.F("tensors: {0} / {1}", _left?.TensorCount ?? 0, _right?.TensorCount ?? 0);
			head = head + "  ·  " + MlModelStrings.F("params: {0} / {1}", FormatCount(_left?.ParamCount ?? 0L), FormatCount(_right?.ParamCount ?? 0L));
			if (!_diff.HasDifferences)
			{
				_status.Text = head + "  ·  " + MlModelStrings.T("No differences");
				return;
			}
			_status.Text = head + "  ·  " + MlModelStrings.F("Summary: {0} entries · {1} changed · {2} left only · {3} right only", _diff.Total, _diff.Changed, _diff.LeftOnly, _diff.RightOnly);
		}

		/// <summary>状态行的格式徽章：解析成功的取模型 Format，否则回退扩展名。</summary>
		private static string DisplayFormat(MlModel model, string fallback)
		{
			return model?.Format ?? fallback;
		}

		/// <summary>参数量按 N0 千分位展示。</summary>
		private static string FormatCount(long value)
		{
			return value.ToString("N0", CultureInfo.InvariantCulture);
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
				_content.Content = NoteText(MlModelStrings.T("File too large to preview"));
				return;
			}
			if (_diff == null)
			{
				_content.Content = NoteText(DisplayError() == null ? MlModelStrings.T("Analyzing…") : MlModelStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			// 两侧都存在却都解析失败（如扩展名对但内容不是模型）：给错误占位，不再渲染空表。
			if (_left == null && _right == null && DisplayError() != null)
			{
				_content.Content = NoteText(MlModelStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			_content.Content = BuildTableContent(_mode == TensorsMode ? MlRowKind.Tensor : MlRowKind.Meta);
		}

		/// <summary>四列表「键路径 | 旧值 | 新值 | 状态」；只显示与当前模式类别相同的行。</summary>
		private Control BuildTableContent(MlRowKind kind)
		{
			List<MlDiffRow> visible = new List<MlDiffRow>();
			foreach (MlDiffRow item in _diff.Rows)
			{
				if (item.Kind == kind)
				{
					visible.Add(item);
				}
			}

			Grid table = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("2*,3*,3*,Auto")
			};
			int row = 0;
			AddTableRow(table, ref row, MlModelStrings.T("Key path"), MlModelStrings.T("Old"), MlModelStrings.T("New"), MlModelStrings.T("State"), null, null, true);

			int shown = 0;
			foreach (MlDiffRow item in visible)
			{
				if (shown >= MaxRows)
				{
					break;
				}
				shown++;
				AddTableRow(table, ref row, item.Path, Display(item.LeftValue, item.LeftPresent), Display(item.RightValue, item.RightPresent), StateLabel(item.State), TintOf(item.State), StateBrush(item.State), false);
			}

			StackPanel stack = new StackPanel
			{
				Spacing = 6.0
			};
			Border card = new Border
			{
				BorderBrush = GridLine,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(6.0),
				Background = Subtle,
				Padding = new Thickness(2.0),
				Child = table
			};
			stack.Children.Add(card);
			if (visible.Count > shown)
			{
				stack.Children.Add(NoteText(MlModelStrings.F("Showing first {0} of {1} rows", shown, visible.Count)));
			}
			if (_srcAbsent || _dstAbsent)
			{
				stack.Children.Add(NoteText(MlModelStrings.T("One side is not present; values shown for the other side only.")));
			}
			return new ScrollViewer
			{
				Content = stack,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
			};
		}

		private void AddTableRow(Grid table, ref int row, string keyPath, string left, string right, string state, IBrush tint, IBrush stateBrush, bool header)
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
			AddCell(table, current, 0, keyPath, header, true, header ? null : Brushes.Gray);
			AddCell(table, current, 1, left, header, false, null);
			AddCell(table, current, 2, right, header, false, null);
			AddCell(table, current, 3, state, header, false, stateBrush);
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

		private static IBrush TintOf(MlDiffState state)
		{
			switch (state)
			{
			case MlDiffState.Changed:
				return TintChanged;
			case MlDiffState.LeftOnly:
				return TintLeftOnly;
			case MlDiffState.RightOnly:
				return TintRightOnly;
			default:
				return null;
			}
		}

		private static IBrush StateBrush(MlDiffState state)
		{
			switch (state)
			{
			case MlDiffState.Changed:
				return StateChanged;
			case MlDiffState.LeftOnly:
				return StateLeft;
			case MlDiffState.RightOnly:
				return StateRight;
			default:
				return Brushes.Gray;
			}
		}

		private static string StateLabel(MlDiffState state)
		{
			switch (state)
			{
			case MlDiffState.Changed:
				return MlModelStrings.T("changed");
			case MlDiffState.LeftOnly:
				return MlModelStrings.T("left only");
			case MlDiffState.RightOnly:
				return MlModelStrings.T("right only");
			default:
				return MlModelStrings.T("same");
			}
		}

		private static string Display(string value, bool present)
		{
			if (!present)
			{
				return MlModelStrings.T("not present");
			}
			return Truncate(value);
		}

		private static string Truncate(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}
			if (value.Length <= MaxValueChars)
			{
				return value;
			}
			return value.Substring(0, MaxValueChars) + "…";
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
