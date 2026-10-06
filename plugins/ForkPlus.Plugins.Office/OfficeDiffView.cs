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
	/// 字节来源：非图片二进制由宿主经 <c>HexSrc/HexDst</c> 预载（≤50MB）；LFS 侧走宿主
	/// <see cref="IDiffViewHost"/> 的缓存 / smudge。提取在后台线程进行，控件构建回到 UI 线程。
	/// </summary>
	public sealed class OfficeDiffView : IDiffView
	{
		/// <summary>表格单元格文字的最大宽度（超出换行，避免超宽单元格把布局撑破）。</summary>
		private const double CellMaxWidth = 260.0;

		private static readonly IBrush GridLine = Brushes.Gainsboro;

		private readonly Grid _root;

		private readonly TextBlock _srcTitle;

		private readonly TextBlock _dstTitle;

		private readonly TextBlock _status;

		private readonly StackPanel _srcPanel;

		private readonly StackPanel _dstPanel;

		private DiffViewContext _context;

		private IDiffViewHost _host;

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
				RowDefinitions = new RowDefinitions("Auto,Auto,*"),
			};
			Grid.SetRow(header, 0);
			_root.Children.Add(header);
			Grid.SetRow(_status, 1);
			_root.Children.Add(_status);
			Grid.SetRow(columns, 2);
			_root.Children.Add(columns);

			PluginEnvironment.ApplyLocalization(_root);
		}

		private static Border WrapPane(Control content, Thickness separator)
		{
			return new Border
			{
				BorderBrush = GridLine,
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
			CancelRender();
			_context = context;
			_host = host;
			_srcPanel.Children.Clear();
			_dstPanel.Children.Clear();
			_srcTitle.Foreground = context?.SrcTitleBrush;
			_dstTitle.Foreground = context?.DstTitleBrush;
			UpdateTitles();
			_status.Text = PluginEnvironment.Translate("Extracting Office content…");

			PluginLog.Info($"OfficeDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
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

		public void ApplyLocalization()
		{
			UpdateTitles();
			PluginEnvironment.ApplyLocalization(_root);
		}

		public void Release()
		{
			_released = true;
			CancelRender();
			_context = null;
			_host = null;
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
				PostStatus(generation, PluginEnvironment.Format("Office compare: {0} / {1} blocks", srcModel?.Blocks.Count ?? 0, dstModel?.Blocks.Count ?? 0));
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				PluginLog.Error("OfficeDiffView extract failed", ex);
				PostStatus(generation, PluginEnvironment.Translate("Failed to read Office document") + ": " + ex.Message);
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
				PostMessage(generation, column, PluginEnvironment.Translate("Failed to read Office document") + Environment.NewLine + ex.Message);
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

		private static Control BuildBlock(OfficeBlock block)
		{
			switch (block)
			{
				case OfficeHeadingBlock heading:
					return new TextBlock
					{
						Text = heading.Text,
						FontWeight = FontWeight.SemiBold,
						FontSize = heading.Level <= 1 ? 15.0 : 13.0,
						TextWrapping = TextWrapping.Wrap,
						Margin = new Thickness(0.0, heading.Level <= 1 ? 10.0 : 8.0, 0.0, 4.0),
					};
				case OfficeParagraphBlock paragraph:
					if (paragraph.Text.Length == 0)
					{
						return new Border { Height = 6.0 };
					}
					return new TextBlock
					{
						Text = paragraph.Text,
						FontSize = 13.0,
						TextWrapping = TextWrapping.Wrap,
						Margin = new Thickness(0.0, 0.0, 0.0, 2.0),
					};
				case OfficeTableBlock table:
					return BuildTable(table);
				default:
					return null;
			}
		}

		private static Control BuildTable(OfficeTableBlock table)
		{
			IReadOnlyList<IReadOnlyList<string>> rows = table.Rows;
			int columns = 0;
			for (int r = 0; r < rows.Count; r++)
			{
				columns = Math.Max(columns, rows[r]?.Count ?? 0);
			}
			if (columns == 0)
			{
				return new Border { Height = 6.0 };
			}
			Grid grid = new Grid();
			for (int c = 0; c < columns; c++)
			{
				grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
			}
			for (int r = 0; r < rows.Count; r++)
			{
				grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				IReadOnlyList<string> row = rows[r];
				for (int c = 0; c < columns; c++)
				{
					string text = (row != null && c < row.Count) ? row[c] : string.Empty;
					Border cell = new Border
					{
						BorderBrush = GridLine,
						BorderThickness = new Thickness(1.0, 1.0, 0.0, 0.0),
						Child = new TextBlock
						{
							Text = text,
							FontSize = 12.0,
							TextWrapping = TextWrapping.Wrap,
							MaxWidth = CellMaxWidth,
							Margin = new Thickness(6.0, 3.0, 6.0, 3.0),
						},
					};
					Grid.SetRow(cell, r);
					Grid.SetColumn(cell, c);
					grid.Children.Add(cell);
				}
			}
			return new Border
			{
				BorderBrush = GridLine,
				BorderThickness = new Thickness(0.0, 0.0, 1.0, 1.0),
				Margin = new Thickness(0.0, 4.0, 0.0, 10.0),
				HorizontalAlignment = HorizontalAlignment.Left,
				Child = grid,
			};
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
				PostMessage(generation, 0, PluginEnvironment.Translate("not present"));
			}
			else if (srcBytes == null)
			{
				PostMessage(generation, 0, DescribeUnavailable(context.Src));
			}
			if (context.Dst == null)
			{
				PostMessage(generation, 1, PluginEnvironment.Translate("not present"));
			}
			else if (dstBytes == null)
			{
				PostMessage(generation, 1, DescribeUnavailable(context.Dst));
			}
		}

		private static string DescribeUnavailable(DiffSideContent side)
		{
			return PluginEnvironment.Translate("Office content unavailable") + Environment.NewLine + side.Path;
		}

		/// <summary>
		/// 把一侧的提取模型挂到栏内。
		/// 控件必须在 UI 线程构建（后台线程构建的 Avalonia 控件不会渲染出来），因此这里只投递
		/// 纯数据模型，在 UI 线程里再 <see cref="BuildBlock"/> 成控件后挂载。
		/// </summary>
		private void PostBlocks(int generation, int column, OfficeDocumentModel model)
		{
			if (model == null)
			{
				return;
			}
			StackPanel panel = column == 0 ? _srcPanel : _dstPanel;
			Dispatcher.UIThread.Post(delegate
			{
				if (_released || generation != _renderGeneration)
				{
					return;
				}
				foreach (OfficeBlock block in model.Blocks)
				{
					Control control = BuildBlock(block);
					if (control != null)
					{
						panel.Children.Add(control);
					}
				}
			});
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
				return text + "  ·  " + PluginEnvironment.Translate("not present");
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