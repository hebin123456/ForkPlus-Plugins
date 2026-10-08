using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Docnet.Core;
using Docnet.Core.Converters;
using Docnet.Core.Models;
using Docnet.Core.Readers;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Pdf
{
	/// <summary>
	/// PDF 对比视图：左右两栏并排，按页号对齐渲染旧（左）/ 新（右）PDF 的每一页。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下一条状态行
	/// （进度 / 错误），再下是单一 <see cref="ScrollViewer"/> —— 内部是一个两列、逐页一行的
	/// <see cref="Grid"/>，因此两侧纵向滚动天然同步、同页号的两页顶对齐，正是 PDF 对比要看的形态。
	///
	/// 字节来源：非图片二进制由宿主经 <c>HexSrc/HexDst</c> 预载（v5.0.4 起 ≤100MB）；LFS 侧走宿主
	/// <see cref="IDiffViewHost"/> 的缓存 / smudge。渲染在后台线程逐页进行，位图创建回到 UI 线程。
	/// </summary>
	public sealed class PdfDiffView : IDiffView
	{
		/// <summary>每页缩放到的渲染视口（保持纵横比，列宽不够时由 Image 再等比缩放）。</summary>
		private const int RenderViewportWidth = 900;

		private const int RenderViewportHeight = 1300;

		private readonly Grid _root;

		private readonly TextBlock _srcTitle;

		private readonly TextBlock _dstTitle;

		private readonly TextBlock _status;

		private readonly Grid _pages;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _renderGeneration;

		private bool _released;

		public PdfDiffView()
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

			_pages = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,*"),
			};
			ScrollViewer scroll = new ScrollViewer
			{
				Content = _pages,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			};

			_root = new Grid
			{
				RowDefinitions = new RowDefinitions("Auto,Auto,*"),
			};
			Grid.SetRow(header, 0);
			_root.Children.Add(header);
			Grid.SetRow(_status, 1);
			_root.Children.Add(_status);
			Grid.SetRow(scroll, 2);
			_root.Children.Add(scroll);

			PluginEnvironment.ApplyLocalization(_root);
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
			_pages.Children.Clear();
			_pages.RowDefinitions.Clear();
			_srcTitle.Foreground = context?.SrcTitleBrush;
			_dstTitle.Foreground = context?.DstTitleBrush;
			UpdateTitles();
			_status.Text = PdfStrings.T("Rendering PDF…");

			PdfNativeLibrary.EnsureRegistered();
			PluginLog.Info($"PdfDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			CancellationTokenSource cts = new CancellationTokenSource();
			_cts = cts;
			int generation = _renderGeneration;
			Task.Run(() => RenderAsync(context, host, cts.Token, generation));
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

		/// <summary>应用当前语言（宿主在语言切换时广播）：宿主 key 重刷 + 插件自带文案重算 + 内容按缓存 context 重渲染。</summary>
		public void ApplyLocalization()
		{
			PluginEnvironment.ApplyLocalization(_root);
			UpdateTitles();
			if (_released || _context == null)
			{
				return;
			}
			// 内容区的「Page N」小标题、缺失 / 不可用占位都取自插件译文表，仅重刷静态文案不够，
			// 需按缓存的 context / host 复用 SetContent 渲染入口重跑整页；SetContent 内部沿用原有的
			// 「代次 + CancellationToken」取消机制，先取消上一轮再开新代次，避免把旧语言内容画到本轮。
			SetContent(_context, _host);
		}

		public void Release()
		{
			_released = true;
			CancelRender();
			_context = null;
			_host = null;
			_pages.Children.Clear();
			_pages.RowDefinitions.Clear();
			_srcTitle.Text = string.Empty;
			_dstTitle.Text = string.Empty;
			_status.Text = string.Empty;
		}

		// ---- 渲染管线 ----

		private async Task RenderAsync(DiffViewContext context, IDiffViewHost host, CancellationToken token, int generation)
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

				IDocReader srcDoc = OpenDocument(srcBytes, generation);
				IDocReader dstDoc = OpenDocument(dstBytes, generation);
				try
				{
					int srcCount = GetPageCount(srcDoc);
					int dstCount = GetPageCount(dstDoc);
					int total = Math.Max(srcCount, dstCount);
					if (total == 0)
					{
						PostStatus(generation, PdfStrings.T("No pages to display"));
						return;
					}
					for (int index = 0; index < total; index++)
					{
						token.ThrowIfCancellationRequested();
						if (index < srcCount)
						{
							RenderPage(srcDoc, index, 0, token, generation);
						}
						if (index < dstCount)
						{
							RenderPage(dstDoc, index, 1, token, generation);
						}
						PostStatus(generation, PdfStrings.F("Rendered {0}/{1} pages", index + 1, total));
					}
					PostStatus(generation, PdfStrings.F("PDF compare: {0} / {1} pages", srcCount, dstCount));
				}
				finally
				{
					srcDoc?.Dispose();
					dstDoc?.Dispose();
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				PluginLog.Error("PdfDiffView render failed", ex);
				PostStatus(generation, PdfStrings.T("Failed to render PDF") + ": " + ex.Message);
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
				PluginLog.Warn("Pdf: failed to load side bytes for '" + side.Path + "'", ex);
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
				PluginLog.Warn("Pdf: LFS cache lookup failed", ex);
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

		private IDocReader OpenDocument(byte[] bytes, int generation)
		{
			if (bytes == null || bytes.Length == 0)
			{
				return null;
			}
			try
			{
				return DocLib.Instance.GetDocReader(bytes, new PageDimensions(RenderViewportWidth, RenderViewportHeight));
			}
			catch (Exception ex)
			{
				PluginLog.Warn("Pdf: failed to open PDF document", ex);
				PostStatus(generation, PdfStrings.T("Failed to render PDF") + ": " + ex.Message);
				return null;
			}
		}

		private static int GetPageCount(IDocReader reader)
		{
			if (reader == null)
			{
				return 0;
			}
			try
			{
				return reader.GetPageCount();
			}
			catch (Exception ex)
			{
				PluginLog.Warn("Pdf: failed to read page count", ex);
				return 0;
			}
		}

		private void RenderPage(IDocReader reader, int pageIndex, int column, CancellationToken token, int generation)
		{
			using (IPageReader page = reader.GetPageReader(pageIndex))
			{
				int width = page.GetPageWidth();
				int height = page.GetPageHeight();
				// PDFium 默认在透明底上渲染；用白色填充把整页合成到不透明白底，
				// 否则透明像素在亮色主题下会露出下层、暗色主题下发黑。
				byte[] bgra = page.GetImage(new NaiveTransparencyRemover(255, 255, 255));
				AddPage(generation, pageIndex, column, bgra, width, height, token);
			}
		}

		/// <summary>把一页的 BGRA 字节转成位图并挂到 UI（须在 UI 线程执行）。</summary>
		private void AddPage(int generation, int pageIndex, int column, byte[] bgra, int width, int height, CancellationToken token)
		{
			Dispatcher.UIThread.Post(delegate
			{
				if (_released || generation != _renderGeneration || token.IsCancellationRequested)
				{
					return;
				}
				while (_pages.RowDefinitions.Count <= pageIndex)
				{
					_pages.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				}
				StackPanel stack = new StackPanel();
				stack.Children.Add(new TextBlock
				{
					Text = PdfStrings.F("Page {0}", pageIndex + 1),
					FontSize = 11.0,
					Opacity = 0.7,
					Margin = new Thickness(8.0, 4.0, 8.0, 2.0),
				});
				stack.Children.Add(new Image
				{
					Source = CreateBitmap(bgra, width, height),
					Stretch = Stretch.Uniform,
					HorizontalAlignment = HorizontalAlignment.Stretch,
					Margin = new Thickness(8.0, 0.0, 8.0, 8.0),
				});
				Border card = new Border
				{
					BorderBrush = Brushes.Gainsboro,
					BorderThickness = new Thickness(1.0),
					CornerRadius = new CornerRadius(4.0),
					Margin = new Thickness(6.0, 0.0, 6.0, 12.0),
					Child = stack,
				};
				Grid.SetRow(card, pageIndex);
				Grid.SetColumn(card, column);
				_pages.Children.Add(card);
			});
		}

		private static WriteableBitmap CreateBitmap(byte[] bgra, int width, int height)
		{
			WriteableBitmap bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96.0, 96.0), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
			using (ILockedFramebuffer buffer = bitmap.Lock())
			{
				int rowBytes = width * 4;
				if (buffer.RowBytes == rowBytes)
				{
					Marshal.Copy(bgra, 0, buffer.Address, bgra.Length);
				}
				else
				{
					for (int y = 0; y < height; y++)
					{
						Marshal.Copy(bgra, y * rowBytes, IntPtr.Add(buffer.Address, y * buffer.RowBytes), rowBytes);
					}
				}
			}
			return bitmap;
		}

		/// <summary>某侧没有内容时给出可读提示：整侧缺失（新增 / 删除）或拿不到字节（超阈值 / LFS 不可用）。</summary>
		private void ReportMissing(int generation, DiffViewContext context, byte[] srcBytes, byte[] dstBytes)
		{
			if (context == null)
			{
				return;
			}
			// 该侧在本次对比里根本不存在：新增时左（旧）侧缺失，删除时右（新）侧缺失。
			if (context.Src == null)
			{
				PostMessage(generation, 0, PdfStrings.T("not present"));
			}
			else if (srcBytes == null)
			{
				PostMessage(generation, 0, DescribeUnavailable(context.Src));
			}
			if (context.Dst == null)
			{
				PostMessage(generation, 1, PdfStrings.T("not present"));
			}
			else if (dstBytes == null)
			{
				PostMessage(generation, 1, DescribeUnavailable(context.Dst));
			}
		}

		private static string DescribeUnavailable(DiffSideContent side)
		{
			return PdfStrings.T("PDF content unavailable") + Environment.NewLine + side.Path;
		}

		private void PostMessage(int generation, int column, string text)
		{
			Dispatcher.UIThread.Post(delegate
			{
				if (_released || generation != _renderGeneration)
				{
					return;
				}
				while (_pages.RowDefinitions.Count < 1)
				{
					_pages.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
				}
				TextBlock block = new TextBlock
				{
					Text = text,
					TextWrapping = TextWrapping.Wrap,
					Margin = new Thickness(12.0),
					Opacity = 0.7,
				};
				Border card = new Border
				{
					BorderBrush = Brushes.Gainsboro,
					BorderThickness = new Thickness(1.0),
					CornerRadius = new CornerRadius(4.0),
					Margin = new Thickness(6.0, 0.0, 6.0, 12.0),
					Child = block,
				};
				Grid.SetRow(card, 0);
				Grid.SetColumn(card, column);
				_pages.Children.Add(card);
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
				// 该侧在本次对比里不存在（新增 / 删除），标题也点明，避免空栏看起来像渲染失败。
				return text + "  ·  " + PdfStrings.T("not present");
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