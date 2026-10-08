using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Office
{
	/// <summary>
	/// Office 内容对比视图：左右两栏并排，各自渲染旧（左）/ 新（右）文档提取出的内容块。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下一条状态行
	/// （进度 / 错误），再下是两列各自独立滚动的 <see cref="StackPanel"/>——内容长度两侧往往不同，
	/// 独立滚动比强制对齐更好读。内容块见 <see cref="OfficeContentExtractor"/>：Word 段落 / 表格、
	/// Excel 工作表网格、PPT 幻灯片文字。
	///
	/// 呈现上尽量「像文档」而不是一坨纯文字：每栏顶部一个类型徽章（Word / Excel / PowerPoint），
	/// 标题做成左侧色条卡片，段落按 run 保留加粗 / 斜体 / 下划线 / 删除线 / 前景色 / 字号
	/// （字符级样式经 docDefaults → 段落样式 → 字符样式 → 直接 rPr 逐层解析，行内混排各自生效），
	/// 表格首行做表头、隔行浅底色，Excel 网格额外补上列字母与行号；Excel 多工作表时顶部出现
	/// sheet 标签页，两侧联动切换（某一侧没有对应工作表时显示占位提示）。
	///
	/// 字节来源：非图片二进制由宿主经 <c>HexSrc/HexDst</c> 预载（v5.0.4 起 ≤100MB）；LFS 侧走宿主
	/// <see cref="IDiffViewHost"/> 的缓存 / smudge。提取在后台线程进行，控件构建回到 UI 线程。
	/// </summary>
	public sealed class OfficeDiffView : IDiffView
	{
		/// <summary>表格单元格文字的最大宽度（超出换行，避免超宽单元格把布局撑破）。</summary>
		private const double CellMaxWidth = 260.0;

		/// <summary>中性色都用带透明度的灰，浅色 / 深色主题下都能保持低对比的「纸面」质感。</summary>
		private static readonly IBrush SubtleBorder = new SolidColorBrush(Color.FromArgb(0x38, 0x80, 0x80, 0x80));

		private static readonly IBrush HeaderTint = new SolidColorBrush(Color.FromArgb(0x1F, 0x80, 0x80, 0x80));

		private static readonly IBrush ZebraTint = new SolidColorBrush(Color.FromArgb(0x0D, 0x80, 0x80, 0x80));

		private static readonly IBrush CardTint = new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x80, 0x80));

		/// <summary>宿主没给标题画刷时的强调色兜底（左右两栏都用中性蓝）。</summary>
		private static readonly IBrush AccentFallback = new SolidColorBrush(Color.Parse("#FF3B82F6"));

		private readonly Grid _root;

		private readonly TextBlock _srcTitle;

		private readonly TextBlock _dstTitle;

		private readonly TextBlock _status;

		private readonly StackPanel _srcPanel;

		private readonly StackPanel _dstPanel;

		/// <summary>sheet 标签条（Excel 多工作表时可见）：一排切换按钮，两侧联动切换。</summary>
		private readonly StackPanel _sheetButtons;

		private readonly ScrollViewer _sheetStrip;

		/// <summary>当前渲染轮次的标签按钮（与 _sheetButtons 同步维护，供切换时重刷选中态）。</summary>
		private readonly List<Button> _sheetTabs = new List<Button>();

		private OfficeDocumentModel _srcModel;

		private OfficeDocumentModel _dstModel;

		private int _sheetIndex;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private IBrush _srcAccent = AccentFallback;

		private IBrush _dstAccent = AccentFallback;

		private CancellationTokenSource _cts;

		private int _renderGeneration;

		private bool _released;

		public OfficeDiffView()
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
				Margin = new Thickness(12.0, 2.0, 12.0, 6.0),
				FontSize = 12.0,
				Opacity = 0.75,
				TextWrapping = TextWrapping.Wrap,
			};

			_srcPanel = new StackPanel
			{
				Margin = new Thickness(12.0, 0.0, 10.0, 12.0),
			};
			_dstPanel = new StackPanel
			{
				Margin = new Thickness(10.0, 0.0, 12.0, 12.0),
			};

			_sheetButtons = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 4.0,
			};
			_sheetStrip = new ScrollViewer
			{
				Content = _sheetButtons,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
				Margin = new Thickness(12.0, 0.0, 12.0, 6.0),
				IsVisible = false,
			};

			Grid columns = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,*"),
			};
			Border srcScroll = WrapPane(_srcPanel, new Thickness(0.0));
			Grid.SetColumn(srcScroll, 0);
			columns.Children.Add(srcScroll);
			Border dstScroll = WrapPane(_dstPanel, new Thickness(1.0, 0.0, 0.0, 0.0));
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
			Grid.SetRow(_sheetStrip, 2);
			_root.Children.Add(_sheetStrip);
			Grid.SetRow(columns, 3);
			_root.Children.Add(columns);

			PluginEnvironment.ApplyLocalization(_root);
		}

		private static Border WrapPane(Control content, Thickness separator)
		{
			return new Border
			{
				BorderBrush = SubtleBorder,
				BorderThickness = separator,
				Child = new ScrollViewer
				{
					Content = content,
					HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
					VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				},
			};
		}

		// ---- IDiffView ----

		Control IDiffView.View => _root;

		/// <summary>本视图自带两栏布局，不需要宿主渲染模式切换工具条。</summary>
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
			_srcAccent = context?.SrcTitleBrush ?? AccentFallback;
			_dstAccent = context?.DstTitleBrush ?? AccentFallback;
			_srcTitle.Foreground = context?.SrcTitleBrush;
			_dstTitle.Foreground = context?.DstTitleBrush;
			PluginLog.Info($"OfficeDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			StartRender();
		}

		/// <summary>
		/// 启动一轮渲染：取消上一轮、清空两栏、按当前语言重排标题与状态行，再投递后台提取。
		/// <see cref="SetContent"/> 与语言热切换（<see cref="ApplyLocalization"/>）复用本入口，
		/// 沿用「代次 + CancellationToken」机制，避免把上一轮内容画到本轮。
		/// </summary>
		private void StartRender()
		{
			CancelRender();
			_srcPanel.Children.Clear();
			_dstPanel.Children.Clear();
			_srcModel = null;
			_dstModel = null;
			_sheetIndex = 0;
			_sheetTabs.Clear();
			_sheetButtons.Children.Clear();
			_sheetStrip.IsVisible = false;
			UpdateTitles();
			_status.Text = OfficeStrings.T("Extracting Office content…");

			DiffViewContext context = _context;
			IDiffViewHost host = _host;
			CancellationTokenSource cts = new CancellationTokenSource();
			_cts = cts;
			int generation = _renderGeneration;
			Task.Run(() => ExtractAsync(context, host, cts.Token, generation));
		}

		public void SetMode(string modeId)
		{
		}

		public void Activate()
		{
		}

		public void Deactivate()
		{
		}

		/// <summary>
		/// 应用当前语言（宿主在语言切换时广播）：宿主 key 重刷 + 插件自带文案重算。
		/// 复用缓存的 context / 宿主重跑同一渲染入口，使内容区的类型徽章、Slide N 小标题、
		/// (N/M rows) 注记、not present / Office content unavailable 占位即时更新。
		/// </summary>
		public void ApplyLocalization()
		{
			PluginEnvironment.ApplyLocalization(_root);
			if (_context != null)
			{
				StartRender();
			}
		}

		public void Release()
		{
			_released = true;
			CancelRender();
			_context = null;
			_host = null;
			_srcModel = null;
			_dstModel = null;
			_sheetIndex = 0;
			_sheetTabs.Clear();
			_sheetButtons.Children.Clear();
			_sheetStrip.IsVisible = false;
			_srcPanel.Children.Clear();
			_dstPanel.Children.Clear();
			_srcTitle.Text = string.Empty;
			_dstTitle.Text = string.Empty;
			_status.Text = string.Empty;
		}

		// ---- 提取管线 ----

		private async Task ExtractAsync(DiffViewContext context, IDiffViewHost host, CancellationToken token, int generation)
		{
			try
			{
				byte[] srcBytes = await ResolveSideAsync(context?.Src, context?.HexSrc, host, token, generation).ConfigureAwait(false);
				byte[] dstBytes = await ResolveSideAsync(context?.Dst, context?.HexDst, host, token, generation).ConfigureAwait(false);
				if (token.IsCancellationRequested)
				{
					return;
				}
				ReportMissing(generation, context, srcBytes, dstBytes);

				// 提取是纯数据操作，放在后台线程；控件构建必须回到 UI 线程（见 PostBlocks）。
				OfficeDocumentModel srcModel = ExtractSide(generation, 0, context?.Src?.Path, srcBytes);
				OfficeDocumentModel dstModel = ExtractSide(generation, 1, context?.Dst?.Path, dstBytes);
				if (token.IsCancellationRequested)
				{
					return;
				}
			PostBlocks(generation, 0, srcModel);
			PostBlocks(generation, 1, dstModel);
				PostStatus(generation, OfficeStrings.F("Office compare: {0} / {1} blocks", srcModel?.Blocks.Count ?? 0, dstModel?.Blocks.Count ?? 0));
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				PluginLog.Error("OfficeDiffView extract failed", ex);
				PostStatus(generation, OfficeStrings.T("Failed to read Office document") + ": " + ex.Message);
			}
		}

		/// <summary>提取一侧内容为纯数据模型（可在后台线程执行）；解析失败只影响该侧（栏内给出错误文案，不中断另一侧）。</summary>
		private OfficeDocumentModel ExtractSide(int generation, int column, string path, byte[] bytes)
		{
			if (bytes == null)
			{
				return null;
			}
			try
			{
				return OfficeContentExtractor.Extract(path, bytes);
			}
			catch (Exception ex)
			{
				PluginLog.Warn("Office: failed to parse '" + path + "'", ex);
				PostMessage(generation, column, OfficeStrings.T("Failed to read Office document") + Environment.NewLine + ex.Message);
				return null;
			}
		}

		/// <summary>取一侧字节：宿主 Hex 预载 &gt; 侧内容懒加载 &gt; LFS 缓存 / smudge。</summary>
		private static async Task<byte[]> ResolveSideAsync(DiffSideContent side, MemoryStream hex, IDiffViewHost host, CancellationToken token, int generation)
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
				PluginLog.Warn("Office: failed to load side bytes for '" + side.Path + "'", ex);
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
				PluginLog.Warn("Office: LFS cache lookup failed", ex);
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

		// ---- 内容块 → 控件 ----

		private static Control BuildBlock(OfficeBlock block, IBrush accent, string kind)
		{
			switch (block)
			{
				case OfficeHeadingBlock heading:
					return BuildHeading(heading, accent);
				case OfficeParagraphBlock paragraph:
					return BuildParagraph(paragraph);
				case OfficeTableBlock table:
					return BuildTable(table, kind);
				default:
					return null;
			}
		}

		/// <summary>标题卡片：左缘一道强调色条 + 分级字号；一级标题再垫一层浅底。</summary>
		private static Control BuildHeading(OfficeHeadingBlock heading, IBrush accent)
		{
			bool major = heading.Level <= 1;
			return new Border
			{
				Background = major ? CardTint : null,
				BorderBrush = accent,
				BorderThickness = new Thickness(major ? 3.0 : 2.0, 0.0, 0.0, 0.0),
				Padding = new Thickness(10.0, major ? 7.0 : 4.0, 8.0, major ? 7.0 : 4.0),
				Margin = new Thickness(0.0, major ? 14.0 : 10.0, 0.0, 6.0),
				Child = new SelectableTextBlock
				{
					Text = heading.Text,
					FontWeight = FontWeight.Bold,
					FontSize = major ? 15.5 : 13.5,
					TextWrapping = TextWrapping.Wrap,
				},
			};
		}

		/// <summary>段落：有 run 格式时走 Inlines 逐 run 保留加粗 / 斜体 / 下划线 / 删除线 / 前景色 / 字号
		///（行内混排各自生效），否则直接渲染纯文本。段落存在放大 / 缩小的 run 时放开固定行高，
		/// 避免大字号 run 被 19.5px 行槽裁掉。</summary>
		private static Control BuildParagraph(OfficeParagraphBlock paragraph)
		{
			if (paragraph.Text.Length == 0)
			{
				return new Border { Height = 9.0 };
			}
			SelectableTextBlock text = new SelectableTextBlock
			{
				FontSize = 13.0,
				LineHeight = 19.5,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, 0.0, 0.0, 4.0),
			};
			if (paragraph.HasFormatting)
			{
				bool mixedSizes = false;
				double baseSizePt = paragraph.BaseSizePt ?? 0.0;
				foreach (OfficeInline inline in paragraph.Runs)
				{
					if (inline.SizePt != null)
					{
						mixedSizes = true;
					}
					text.Inlines.Add(ToRun(inline, baseSizePt));
				}
				if (mixedSizes)
				{
					text.LineHeight = double.NaN;
				}
			}
			else
			{
				text.Text = paragraph.Text;
			}
			return text;
		}

		/// <summary>run → Avalonia <see cref="Run"/>：前景色按十六进制上色；字号按「run 磅值 / 文档默认磅值」
		/// 的比例缩放视图基准字号（13px），保持行内相对大小关系且与无格式段落的观感衔接。</summary>
		private static Run ToRun(OfficeInline inline, double baseSizePt)
		{
			Run run = new Run
			{
				Text = inline.Text,
				FontWeight = inline.Bold ? FontWeight.Bold : FontWeight.Normal,
				FontStyle = inline.Italic ? FontStyle.Italic : FontStyle.Normal,
			};
			TextDecorationCollection decorations = null;
			if (inline.Underline)
			{
				decorations = new TextDecorationCollection
				{
					new TextDecoration { Location = TextDecorationLocation.Underline },
				};
			}
			if (inline.Strike)
			{
				decorations ??= new TextDecorationCollection();
				decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
			}
			if (decorations != null)
			{
				run.TextDecorations = decorations;
			}
			IBrush foreground = ParseColorBrush(inline.ColorHex);
			if (foreground != null)
			{
				run.Foreground = foreground;
			}
			if (inline.SizePt != null && baseSizePt > 0.0)
			{
				run.FontSize = 13.0 * inline.SizePt.Value / baseSizePt;
			}
			return run;
		}

		/// <summary>RRGGBB / AARRGGBB 十六进制 → 画刷；null 或无效返回 null（跟随视图默认前景色）。</summary>
		private static IBrush ParseColorBrush(string hex)
		{
			if (string.IsNullOrEmpty(hex))
			{
				return null;
			}
			string value = hex.TrimStart('#');
			if (value.Length == 6)
			{
				value = "FF" + value;
			}
			if (value.Length != 8)
			{
				return null;
			}
			byte a = ParseByteHex(value, 0);
			byte r = ParseByteHex(value, 2);
			byte g = ParseByteHex(value, 4);
			byte b = ParseByteHex(value, 6);
			return new SolidColorBrush(Color.FromArgb(a, r, g, b));
		}

		/// <summary>两位十六进制 → 字节；输入保证已通过十六进制校验（提取端 NormalizeColor）。</summary>
		private static byte ParseByteHex(string value, int offset)
		{
			return byte.Parse(value.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// 表格：首行做表头（加粗 + 浅底），其余行隔行浅底，单元格只画右 / 下细线。
		/// Excel 网格额外补一行列字母与一列行号，读起来更像表格软件。
		/// </summary>
		private static Control BuildTable(OfficeTableBlock table, string kind)
		{
			IReadOnlyList<IReadOnlyList<string>> rows = table.Rows;
			int dataColumns = 0;
			for (int r = 0; r < rows.Count; r++)
			{
				dataColumns = Math.Max(dataColumns, rows[r]?.Count ?? 0);
			}
			if (dataColumns == 0)
			{
				return new Border { Height = 9.0 };
			}

			bool spreadsheet = string.Equals(kind, "Excel", StringComparison.Ordinal);
			int columnOffset = spreadsheet ? 1 : 0;
			int rowOffset = spreadsheet ? 1 : 0;
			int totalColumns = dataColumns + columnOffset;
			int totalRows = rows.Count + rowOffset;

			Grid grid = new Grid();
			for (int c = 0; c < totalColumns; c++)
			{
				ColumnDefinition definition = new ColumnDefinition(GridLength.Auto);
				if (spreadsheet)
				{
					definition.MinWidth = c == 0 ? 34.0 : 56.0;
				}
				grid.ColumnDefinitions.Add(definition);
			}
			for (int r = 0; r < totalRows; r++)
			{
				grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
			}

			if (spreadsheet)
			{
				AddCell(grid, string.Empty, 0, 0, header: true, gutter: true, bold: false, zebra: false);
				for (int c = 0; c < dataColumns; c++)
				{
					AddCell(grid, ColumnLetter(c), 0, c + 1, header: true, gutter: true, bold: true, zebra: false);
				}
			}

			for (int r = 0; r < rows.Count; r++)
			{
				// Excel 首行是数据（当表头加粗）；Word 首行本就是表头。
				bool headerRow = spreadsheet ? r == 0 : r == 0;
				bool zebra = !headerRow && r % 2 == 1;
				if (spreadsheet)
				{
					AddCell(grid, (r + 1).ToString(CultureInfo.InvariantCulture), r + rowOffset, 0, header: false, gutter: true, bold: false, zebra: false);
				}
				IReadOnlyList<string> row = rows[r];
				for (int c = 0; c < dataColumns; c++)
				{
					string text = (row != null && c < row.Count) ? row[c] : string.Empty;
					AddCell(grid, text, r + rowOffset, c + columnOffset, header: headerRow, gutter: false, bold: headerRow, zebra: zebra);
				}
			}

			return new Border
			{
				Margin = new Thickness(0.0, 6.0, 0.0, 12.0),
				HorizontalAlignment = HorizontalAlignment.Left,
				Child = grid,
			};
		}

		private static void AddCell(Grid grid, string text, int row, int column, bool header, bool gutter, bool bold, bool zebra)
		{
			IBrush background = header ? HeaderTint : (zebra ? ZebraTint : null);
			SelectableTextBlock label = new SelectableTextBlock
			{
				Text = text,
				FontSize = gutter ? 11.0 : 12.5,
				FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
				TextWrapping = TextWrapping.Wrap,
				TextAlignment = gutter ? TextAlignment.Center : TextAlignment.Left,
				Margin = new Thickness(7.0, 4.0, 7.0, 4.0),
				Opacity = gutter ? 0.6 : 1.0,
			};
			if (!gutter)
			{
				label.MaxWidth = CellMaxWidth;
			}
			Border cell = new Border
			{
				Background = background,
				BorderBrush = SubtleBorder,
				BorderThickness = new Thickness(column == 0 ? 1.0 : 0.0, row == 0 ? 1.0 : 0.0, 1.0, 1.0),
				Child = label,
			};
			Grid.SetRow(cell, row);
			Grid.SetColumn(cell, column);
			grid.Children.Add(cell);
		}

		/// <summary>0 基列号 → 电子表列字母（0→A、25→Z、26→AA）。</summary>
		private static string ColumnLetter(int index)
		{
			StringBuilder builder = new StringBuilder(3);
			int value = index + 1;
			while (value > 0)
			{
				int remainder = (value - 1) % 26;
				builder.Insert(0, (char)('A' + remainder));
				value = (value - 1) / 26;
			}
			return builder.ToString();
		}

		/// <summary>栏顶徽章：文档类型 + 块数 / 字数，让「这是 Word 还是 Excel」一眼可辨。</summary>
		private static Control BuildKindBadge(OfficeDocumentModel model, IBrush accent)
		{
			StackPanel badge = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 8.0,
				Margin = new Thickness(0.0, 2.0, 0.0, 12.0),
			};
			badge.Children.Add(new Border
			{
				Background = CardTint,
				BorderBrush = accent,
				BorderThickness = new Thickness(2.0, 0.0, 0.0, 0.0),
				Padding = new Thickness(8.0, 3.0, 8.0, 3.0),
				Child = new TextBlock
				{
					Text = model.Kind,
					FontSize = 11.5,
					FontWeight = FontWeight.SemiBold,
				},
			});
			badge.Children.Add(new TextBlock
			{
				Text = OfficeStrings.F("{0} blocks · {1} chars", model.Blocks.Count, model.TextLength),
				FontSize = 11.5,
				Opacity = 0.6,
				VerticalAlignment = VerticalAlignment.Center,
			});
			return badge;
		}

		/// <summary>某侧没有内容时给出可读提示：整侧缺失（新增 / 删除）或拿不到字节（超阈值 / LFS 不可用）。</summary>
		private void ReportMissing(int generation, DiffViewContext context, byte[] srcBytes, byte[] dstBytes)
		{
			if (context == null)
			{
				return;
			}
			if (context.Src == null)
			{
				PostMessage(generation, 0, OfficeStrings.T("not present"));
			}
			else if (srcBytes == null)
			{
				PostMessage(generation, 0, DescribeUnavailable(context.Src));
			}
			if (context.Dst == null)
			{
				PostMessage(generation, 1, OfficeStrings.T("not present"));
			}
			else if (dstBytes == null)
			{
				PostMessage(generation, 1, DescribeUnavailable(context.Dst));
			}
		}

		private static string DescribeUnavailable(DiffSideContent side)
		{
			return OfficeStrings.T("Office content unavailable") + Environment.NewLine + side.Path;
		}

		/// <summary>
		/// 把一侧的提取模型挂到栏内。
		/// 控件必须在 UI 线程构建（后台线程构建的 Avalonia 控件不会渲染出来），因此这里只投递
		/// 纯数据模型，在 UI 线程里再 <see cref="BuildBlock"/> 成控件后挂载。模型先存字段再走
		/// <see cref="RenderSides"/>：Excel 多工作表时两侧按当前 sheet 标签页联动重挂。
		/// </summary>
		private void PostBlocks(int generation, int column, OfficeDocumentModel model)
		{
			if (model == null)
			{
				return;
			}
			Dispatcher.UIThread.Post(delegate
			{
				if (_released || generation != _renderGeneration)
				{
					return;
				}
				if (column == 0)
				{
					_srcModel = model;
				}
				else
				{
					_dstModel = model;
				}
				RenderSides();
			});
		}

		/// <summary>
		/// 按当前 sheet 标签页重挂两侧内容（UI 线程）：非 Excel 或单工作表时平铺全部块；
		/// 多工作表时只挂两侧当前索引的工作表，某一侧没有该索引（两侧 sheet 数不同）时给占位提示。
		/// 标签条按两侧 sheet 名的并集构建（两侧重名工作表时以新侧名字为准）。
		/// </summary>
		private void RenderSides()
		{
			int sheetCount = Math.Max(SheetCount(_srcModel), SheetCount(_dstModel));
			bool tabbed = sheetCount > 1;
			_sheetTabs.Clear();
			_sheetButtons.Children.Clear();
			_sheetStrip.IsVisible = tabbed;
			if (tabbed)
			{
				if (_sheetIndex < 0 || _sheetIndex >= sheetCount)
				{
					_sheetIndex = 0;
				}
				for (int i = 0; i < sheetCount; i++)
				{
					string name = SheetName(_dstModel, i) ?? SheetName(_srcModel, i) ?? (i + 1).ToString(CultureInfo.InvariantCulture);
					Button tab = NewSheetTab(name, i);
					_sheetTabs.Add(tab);
					_sheetButtons.Children.Add(tab);
				}
				UpdateTabStyles();
			}
			RenderSide(0, _srcModel, _srcAccent, tabbed);
			RenderSide(1, _dstModel, _dstAccent, tabbed);
		}

		/// <summary>重挂一侧：模型为 null 时保留原样（缺失 / 解析失败的提示由 PostMessage 挂上，别被切换清掉）。</summary>
		private void RenderSide(int column, OfficeDocumentModel model, IBrush accent, bool tabbed)
		{
			if (model == null)
			{
				return;
			}
			StackPanel panel = column == 0 ? _srcPanel : _dstPanel;
			panel.Children.Clear();
			panel.Children.Add(BuildKindBadge(model, accent));
			if (tabbed && model.Sheets != null)
			{
				if (_sheetIndex >= model.Sheets.Count)
				{
					panel.Children.Add(new TextBlock
					{
						Text = OfficeStrings.T("not present"),
						TextWrapping = TextWrapping.Wrap,
						Margin = new Thickness(0.0, 4.0, 0.0, 8.0),
						Opacity = 0.7,
					});
					return;
				}
				AddBlocks(panel, model.Sheets[_sheetIndex].Blocks, accent, model.Kind);
				return;
			}
			AddBlocks(panel, model.Blocks, accent, model.Kind);
		}

		private static void AddBlocks(StackPanel panel, IReadOnlyList<OfficeBlock> blocks, IBrush accent, string kind)
		{
			foreach (OfficeBlock block in blocks)
			{
				Control control = BuildBlock(block, accent, kind);
				if (control != null)
				{
					panel.Children.Add(control);
				}
			}
		}

		private static int SheetCount(OfficeDocumentModel model)
		{
			return model?.Sheets?.Count ?? 0;
		}

		private static string SheetName(OfficeDocumentModel model, int index)
		{
			IReadOnlyList<OfficeSheetSection> sheets = model?.Sheets;
			if (sheets != null && index < sheets.Count)
			{
				return sheets[index].Name;
			}
			return null;
		}

		/// <summary>sheet 标签按钮：观感沿用各视图的模式切换按钮；垂直 Padding 为 0——宿主按钮主题
		/// 固定 Height=24，垂直 Padding 会把内容槽挤到文字行高以下裁掉文字。</summary>
		private Button NewSheetTab(string text, int index)
		{
			Button button = new Button
			{
				Content = text,
				Padding = new Thickness(10.0, 0.0, 10.0, 0.0),
				FontSize = 12.0,
				MaxWidth = 160.0,
			};
			button.Click += delegate
			{
				SelectSheet(index);
			};
			return button;
		}

		private void SelectSheet(int index)
		{
			if (index == _sheetIndex)
			{
				return;
			}
			_sheetIndex = index;
			UpdateTabStyles();
			RenderSides();
		}

		private void UpdateTabStyles()
		{
			for (int i = 0; i < _sheetTabs.Count; i++)
			{
				StyleSheetTab(_sheetTabs[i], i == _sheetIndex);
			}
		}

		private static void StyleSheetTab(Button button, bool active)
		{
			button.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
			button.Background = active ? new SolidColorBrush(Color.FromArgb(0x33, 0x2F, 0x6F, 0xED)) : null;
		}

		private void PostMessage(int generation, int column, string text)
		{
			StackPanel panel = column == 0 ? _srcPanel : _dstPanel;
			Dispatcher.UIThread.Post(delegate
			{
				if (_released || generation != _renderGeneration)
				{
					return;
				}
				panel.Children.Add(new TextBlock
				{
					Text = text,
					TextWrapping = TextWrapping.Wrap,
					Margin = new Thickness(0.0, 4.0, 0.0, 8.0),
					Opacity = 0.7,
				});
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
				// 该侧在本次对比里不存在（新增 / 删除），标题也点明，避免空栏看起来像解析失败。
				return text + "  ·  " + OfficeStrings.T("not present");
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

		private static TextBlock NewTitle()
		{
			return new TextBlock
			{
				Margin = new Thickness(12.0, 8.0, 12.0, 4.0),
				FontWeight = FontWeight.SemiBold,
				TextTrimming = TextTrimming.CharacterEllipsis,
			};
		}
	}
}