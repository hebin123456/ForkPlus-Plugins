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
using SkiaSharp;

namespace ForkPlus.Plugins.Psd
{
	/// <summary>
	/// PSD 图层对比视图：把旧（左）/ 新（右）两侧 PSD / PSB 各自解析成头部字段 + 图层结构模型，
	/// 头部行按键路径取并集、图层按名称配对，得到「相同 / 已变 / 仅左 / 仅右」四类。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、
	/// 自带的分段模式工具条（缩略图 / 图层 / 文件头），最下是内容区。
	///
	/// 三个模式：
	/// <list type="bullet">
	/// <item>缩略图（默认）：内嵌缩略图（图像资源 1036 的 JFIF 数据，经 SkiaSharp 转 PNG）左右并排，
	/// 无缩略图一侧明示 <c>No embedded thumbnail</c>——看「整张图改成什么样了」。</item>
	/// <item>图层：一张四列表「图层 | 旧 | 新 | 状态」，图层属性摘要逐行四色标注，
	/// 分组图层带 ▸ / ▾ / — 前缀——看「哪个图层挪了 / 隐藏了 / 多了一层」。</item>
	/// <item>文件头：键路径表（宽 / 高 / 通道 / 位深 / 颜色模式 / 资源数 / 图层数 / 压缩），逐行四色。</item>
	/// </list>
	///
	/// 解析与缩略图解码在后台线程执行，控件只在 UI 线程构建；每次刷新用代次 + CancellationToken
	/// 取消上一轮。单侧声明大小 &gt; 300 MB 只给提示、不解析。
	/// </summary>
	public sealed class PsdDiffView : IDiffView
	{
		private const string PreviewMode = "preview";

		private const string LayersMode = "layers";

		private const string HeaderMode = "header";

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （layers / header，其余走默认 preview），据此逐模式取图；正常运行时该变量为空。
		/// </summary>
		private static readonly string InitialMode = ResolveInitialMode();

		private static string ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			if (string.Equals(forced, LayersMode, StringComparison.OrdinalIgnoreCase))
			{
				return LayersMode;
			}
			if (string.Equals(forced, HeaderMode, StringComparison.OrdinalIgnoreCase))
			{
				return HeaderMode;
			}
			return PreviewMode;
		}

		/// <summary>单侧超过此大小不解析（与字幕 / 音视频等插件同口径）。</summary>
		private const long MaxSideBytes = 300L * 1024L * 1024L;

		/// <summary>表格渲染上限：避免超大图层文件把 UI 拖死。</summary>
		private const int MaxRows = 1000;

		/// <summary>缩略图显示上限：整图缩略图通常 ≤ 1024，超出按此约束显示。</summary>
		private const double MaxPreviewSize = 640.0;

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

		private readonly Button _previewButton;

		private readonly Button _layersButton;

		private readonly Button _headerButton;

		private readonly ContentControl _content;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _generation;

		private string _mode = InitialMode;

		private PsdDocument _left;

		private PsdDocument _right;

		private PsdDiffResult _headerDiff;

		private PsdLayerDiffResult _layerDiff;

		private byte[] _srcPng;

		private byte[] _dstPng;

		private Avalonia.Media.Imaging.Bitmap _srcBitmap;

		private Avalonia.Media.Imaging.Bitmap _dstBitmap;

		private string _error;

		/// <summary>左 / 右两侧各自的解析错误（侧存在但解析失败时用于区分「解析失败」与「不存在」）。</summary>
		private string _srcParseError;

		private string _dstParseError;

		private bool _srcAbsent;

		private bool _dstAbsent;

		private bool _tooLarge;

		public PsdDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			_status = new TextBlock
			{
				Margin = new Thickness(12.0, 2.0, 12.0, 6.0),
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0
			};
			_previewButton = NewModeButton(PsdStrings.T("Preview"), delegate
			{
				SetMode(PreviewMode);
			});
			_layersButton = NewModeButton(PsdStrings.T("Layers"), delegate
			{
				SetMode(LayersMode);
			});
			_headerButton = NewModeButton(PsdStrings.T("Header"), delegate
			{
				SetMode(HeaderMode);
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
			modes.Children.Add(_previewButton);
			modes.Children.Add(_layersButton);
			modes.Children.Add(_headerButton);

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

			StyleModeButton(_previewButton, _mode == PreviewMode);
			StyleModeButton(_layersButton, _mode == LayersMode);
			StyleModeButton(_headerButton, _mode == HeaderMode);
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
			PluginLog.Info($"PsdDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			Render();
		}

		public void SetMode(string modeId)
		{
			if (string.IsNullOrEmpty(modeId))
			{
				return;
			}
			if (modeId != PreviewMode && modeId != LayersMode && modeId != HeaderMode)
			{
				return;
			}
			if (modeId == _mode)
			{
				return;
			}
			_mode = modeId;
			StyleModeButton(_previewButton, _mode == PreviewMode);
			StyleModeButton(_layersButton, _mode == LayersMode);
			StyleModeButton(_headerButton, _mode == HeaderMode);
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
			_previewButton.Content = PsdStrings.T("Preview");
			_layersButton.Content = PsdStrings.T("Layers");
			_headerButton.Content = PsdStrings.T("Header");
			PluginEnvironment.ApplyLocalization(_root);
			UpdateTitles();
			BuildContent();
		}

		public void Release()
		{
			CancelRender();
			_context = null;
			_host = null;
			_left = null;
			_right = null;
			_headerDiff = null;
			_layerDiff = null;
			_srcPng = null;
			_dstPng = null;
			ReleaseBitmaps();
			_content.Content = null;
			_status.Text = string.Empty;
		}

		// ---- 渲染 ----

		private void Render()
		{
			CancelRender();
			_left = null;
			_right = null;
			_headerDiff = null;
			_layerDiff = null;
			_srcPng = null;
			_dstPng = null;
			ReleaseBitmaps();
			_error = null;
			_srcParseError = null;
			_dstParseError = null;
			_tooLarge = false;
			_status.Text = PsdStrings.T("Analyzing…");
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
				PsdDocument left = null;
				PsdDocument right = null;
				string srcParseError = null;
				string dstParseError = null;
				try
				{
					byte[] srcBytes = ReadBytes(src, hexSrc);
					byte[] dstBytes = ReadBytes(dst, hexDst);
					cts.Token.ThrowIfCancellationRequested();
					if (srcBytes != null)
					{
						left = PsdParser.Parse(srcBytes, out srcParseError);
					}
					if (dstBytes != null)
					{
						right = PsdParser.Parse(dstBytes, out dstParseError);
					}
					cts.Token.ThrowIfCancellationRequested();
					byte[] srcPng = DecodeThumbnailPng(left?.ThumbnailJfif);
					byte[] dstPng = DecodeThumbnailPng(right?.ThumbnailJfif);
					PsdDiffResult headerDiff = PsdDiff.ComputeHeader(left?.Rows, right?.Rows);
					PsdLayerDiffResult layerDiff = PsdDiff.ComputeLayers(left?.Layers, right?.Layers);
					Dispatcher.UIThread.Post(delegate
					{
						if (_generation != generation || _cts != cts)
						{
							return;
						}
						_left = left;
						_right = right;
						_headerDiff = headerDiff;
						_layerDiff = layerDiff;
						_srcPng = srcPng;
						_dstPng = dstPng;
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
					PluginLog.Error("PsdDiffView 渲染失败", ex);
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
		private byte[] ReadBytes(DiffSideContent side, MemoryStream preloaded)
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
			long position = data.Position;
			try
			{
				data.Position = 0L;
				byte[] buffer = new byte[data.Length];
				int read = 0;
				while (read < buffer.Length)
				{
					int count = data.Read(buffer, read, buffer.Length - read);
					if (count <= 0)
					{
						break;
					}
					read += count;
				}
				if (read < buffer.Length)
				{
					byte[] exact = new byte[read];
					Buffer.BlockCopy(buffer, 0, exact, 0, read);
					return exact;
				}
				return buffer;
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

		/// <summary>后台线程把内嵌缩略图的 JFIF 字节解码并转成 PNG（SkiaSharp，宿主共享程序集）。</summary>
		private static byte[] DecodeThumbnailPng(byte[] jfif)
		{
			if (jfif == null || jfif.Length == 0)
			{
				return null;
			}
			try
			{
				using (SKBitmap bitmap = SKBitmap.Decode(jfif))
				{
					if (bitmap == null)
					{
						return null;
					}
					using (SKImage image = SKImage.FromBitmap(bitmap))
					using (SKData data = image.Encode(SKEncodedImageFormat.Png, 100))
					{
						return data?.ToArray();
					}
				}
			}
			catch (Exception ex)
			{
				PluginLog.Warn("PsdDiffView: failed to decode embedded thumbnail", ex);
				return null;
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
				return roleText + "  ·  " + PsdStrings.T("not present");
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
				_status.Text = PsdStrings.T("File too large to preview");
				return;
			}
			string error = DisplayError();
			if (error != null)
			{
				_status.Text = PsdStrings.F("Failed to parse: {0}", error);
				return;
			}
			if (_headerDiff == null || _layerDiff == null)
			{
				_status.Text = PsdStrings.T("Analyzing…");
				return;
			}
			string head = PsdStrings.F("PSD compare: {0} / {1}", FormatValue(_left), FormatValue(_right));
			head = head + "  ·  " + PsdStrings.F("layers: {0} / {1}", _left?.Layers.Count ?? 0, _right?.Layers.Count ?? 0);
			if (!_headerDiff.HasDifferences && !_layerDiff.HasDifferences)
			{
				_status.Text = head + "  ·  " + PsdStrings.T("No differences");
				return;
			}
			_status.Text = head + "  ·  " + PsdStrings.F("Summary: {0} entries · {1} changed · {2} left only · {3} right only", _layerDiff.Total, _layerDiff.Changed, _layerDiff.LeftOnly, _layerDiff.RightOnly);
		}

		private string FormatValue(PsdDocument document)
		{
			return document != null ? document.FormatValue() : PsdStrings.T("not present");
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
				_content.Content = NoteText(PsdStrings.T("File too large to preview"));
				return;
			}
			if (_headerDiff == null || _layerDiff == null)
			{
				_content.Content = NoteText(DisplayError() == null ? PsdStrings.T("Analyzing…") : PsdStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			// 两侧都存在却都解析失败（如扩展名对但内容不是 PSD）：给错误占位，不再渲染空表。
			if (_left == null && _right == null && DisplayError() != null)
			{
				_content.Content = NoteText(PsdStrings.F("Failed to parse: {0}", DisplayError()));
				return;
			}
			switch (_mode)
			{
			case LayersMode:
				_content.Content = BuildLayersContent();
				break;
			case HeaderMode:
				_content.Content = BuildHeaderContent();
				break;
			default:
				_content.Content = BuildPreviewContent();
				break;
			}
		}

		// ---- 缩略图模式 ----

		private Control BuildPreviewContent()
		{
			ReleaseBitmaps();
			_srcBitmap = DecodeBitmap(_srcPng);
			_dstBitmap = DecodeBitmap(_dstPng);

			Grid columns = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,*")
			};
			Control left = PreviewCard(_context?.Src, _context?.SrcRole ?? DiffSideRole.Old, _srcBitmap, _left);
			Grid.SetColumn(left, 0);
			columns.Children.Add(left);
			Control right = PreviewCard(_context?.Dst, _context?.DstRole ?? DiffSideRole.New, _dstBitmap, _right);
			Grid.SetColumn(right, 1);
			columns.Children.Add(right);

			StackPanel stack = new StackPanel
			{
				Spacing = 6.0
			};
			stack.Children.Add(columns);
			if (_srcAbsent || _dstAbsent)
			{
				stack.Children.Add(NoteText(PsdStrings.T("One side is not present; values shown for the other side only.")));
			}
			return stack;
		}

		private Control PreviewCard(DiffSideContent side, DiffSideRole role, Avalonia.Media.Imaging.Bitmap bitmap, PsdDocument document)
		{
			StackPanel panel = new StackPanel
			{
				Spacing = 6.0
			};
			if (side == null)
			{
				panel.Children.Add(NoteText(PsdStrings.T("not present")));
			}
			else if (bitmap == null)
			{
				panel.Children.Add(NoteText(PsdStrings.T("No embedded thumbnail")));
			}
			else
			{
				Image image = new Image
				{
					Source = bitmap,
					Stretch = Stretch.Uniform,
					MaxWidth = MaxPreviewSize,
					MaxHeight = MaxPreviewSize,
					HorizontalAlignment = HorizontalAlignment.Left,
					VerticalAlignment = VerticalAlignment.Top
				};
				panel.Children.Add(image);
			}
			if (document != null && document.ThumbnailSizeText != null)
			{
				TextBlock size = new TextBlock
				{
					Text = document.ThumbnailSizeText,
					FontSize = 11.0,
					FontFamily = MonoFont,
					Foreground = Brushes.Gray,
					Opacity = 0.8
				};
				panel.Children.Add(size);
			}
			return new Border
			{
				BorderBrush = GridLine,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(6.0),
				Background = Subtle,
				Padding = new Thickness(10.0),
				Margin = new Thickness(0.0, 0.0, 6.0, 0.0),
				Child = panel
			};
		}

		private void ReleaseBitmaps()
		{
			_srcBitmap?.Dispose();
			_srcBitmap = null;
			_dstBitmap?.Dispose();
			_dstBitmap = null;
		}

		/// <summary>UI 线程把 PNG 字节解码成 Avalonia 位图（失败记日志返回 null）。</summary>
		private static Avalonia.Media.Imaging.Bitmap DecodeBitmap(byte[] png)
		{
			if (png == null || png.Length == 0)
			{
				return null;
			}
			try
			{
				using (MemoryStream stream = new MemoryStream(png, false))
				{
					return new Avalonia.Media.Imaging.Bitmap(stream);
				}
			}
			catch (Exception ex)
			{
				PluginLog.Warn("PsdDiffView: failed to decode thumbnail bitmap", ex);
				return null;
			}
		}

		// ---- 图层模式 ----

		private Control BuildLayersContent()
		{
			Grid table = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("2*,3*,3*,Auto")
			};
			int row = 0;
			AddTableRow(table, ref row, PsdStrings.T("Layer"), PsdStrings.T("Old"), PsdStrings.T("New"), PsdStrings.T("State"), null, null, true);

			int shown = 0;
			foreach (PsdLayerRow item in _layerDiff.Rows)
			{
				if (shown >= MaxRows)
				{
					break;
				}
				shown++;
				AddTableRow(table, ref row, LayerName(item), LayerSummary(item.Left), LayerSummary(item.Right), StateLabel(item.State), TintOf(item.State), StateBrush(item.State), false);
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
			if (_layerDiff.Total > shown)
			{
				stack.Children.Add(NoteText(PsdStrings.F("Showing first {0} of {1} rows", shown, _layerDiff.Total)));
			}
			if (_srcAbsent || _dstAbsent)
			{
				stack.Children.Add(NoteText(PsdStrings.T("One side is not present; values shown for the other side only.")));
			}
			return new ScrollViewer
			{
				Content = stack,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
			};
		}

		/// <summary>图层单元文案：分组前缀 + 名称（优先左侧，仅右侧时用右侧名称）。</summary>
		private static string LayerName(PsdLayerRow row)
		{
			string name = row.Left != null ? row.Left.Name : row.Right?.Name;
			PsdSectionType section = (row.Left ?? row.Right)?.SectionType ?? PsdSectionType.None;
			string prefix = string.Empty;
			switch (section)
			{
			case PsdSectionType.OpenFolder:
				prefix = "▸ ";
				break;
			case PsdSectionType.ClosedFolder:
				prefix = "▾ ";
				break;
			case PsdSectionType.Divider:
				prefix = "— ";
				break;
			}
			return prefix + (name ?? string.Empty);
		}

		private static string LayerSummary(PsdLayer layer)
		{
			return layer != null ? layer.SummaryText() : PsdStrings.T("not present");
		}

		// ---- 文件头模式 ----

		private Control BuildHeaderContent()
		{
			Grid table = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("2*,3*,3*,Auto")
			};
			int row = 0;
			AddTableRow(table, ref row, PsdStrings.T("Key path"), PsdStrings.T("Old"), PsdStrings.T("New"), PsdStrings.T("State"), null, null, true);

			int shown = 0;
			foreach (PsdDiffRow item in _headerDiff.Rows)
			{
				if (shown >= MaxRows)
				{
					break;
				}
				shown++;
				string left = item.LeftValue ?? PsdStrings.T("not present");
				string right = item.RightValue ?? PsdStrings.T("not present");
				AddTableRow(table, ref row, item.Path, left, right, StateLabel(item.State), TintOf(item.State), StateBrush(item.State), false);
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
			if (_headerDiff.Total > shown)
			{
				stack.Children.Add(NoteText(PsdStrings.F("Showing first {0} of {1} rows", shown, _headerDiff.Total)));
			}
			if (_srcAbsent || _dstAbsent)
			{
				stack.Children.Add(NoteText(PsdStrings.T("One side is not present; values shown for the other side only.")));
			}
			return new ScrollViewer
			{
				Content = stack,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
			};
		}

		// ---- 文案 / 小部件 ----

		/// <summary>四列表通用行：可选整行四色底 + 状态列彩色文字；首列与数值列等宽。</summary>
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
			AddCell(table, current, 0, cell0, header, true, header ? null : Brushes.Gray);
			AddCell(table, current, 1, cell1, header, true, null);
			AddCell(table, current, 2, cell2, header, true, null);
			AddCell(table, current, 3, cell3, header, false, header ? null : stateBrush);
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

		private static IBrush TintOf(PsdState state)
		{
			switch (state)
			{
			case PsdState.Changed:
				return TintChanged;
			case PsdState.LeftOnly:
				return TintLeftOnly;
			case PsdState.RightOnly:
				return TintRightOnly;
			default:
				return null;
			}
		}

		private static IBrush StateBrush(PsdState state)
		{
			switch (state)
			{
			case PsdState.Changed:
				return StateChanged;
			case PsdState.LeftOnly:
				return StateLeft;
			case PsdState.RightOnly:
				return StateRight;
			default:
				return Brushes.Gray;
			}
		}

		private static string StateLabel(PsdState state)
		{
			switch (state)
			{
			case PsdState.Changed:
				return PsdStrings.T("changed");
			case PsdState.LeftOnly:
				return PsdStrings.T("left only");
			case PsdState.RightOnly:
				return PsdStrings.T("right only");
			default:
				return PsdStrings.T("same");
			}
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
