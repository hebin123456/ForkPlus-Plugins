using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Archive
{
	/// <summary>
	/// 压缩包条目树对比视图：左右两栏并排，各自把旧（左）/ 新（右）压缩包展开成条目树
	/// （目录 + 文件 + 大小，加密条目标注），只对比「压缩包里有什么」，不读取解压后的内容。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、
	/// 一行密码输入（<see cref="TextBox"/> + 「应用」按钮），再下是两列各自独立滚动的条目树。
	/// 带密码的压缩包（如做了头部加密的 7z / rar）解不动时，状态行提示需要 / 密码错误，
	/// 用户在密码框输入后点「应用」即可重新展开。
	///
	/// 字节来源：非图片二进制由宿主经 <c>HexSrc/HexDst</c> 预载（≤50MB）；LFS 侧走宿主
	/// <see cref="IDiffViewHost"/> 的缓存 / smudge。展开在后台线程进行，控件构建回到 UI 线程。
	/// </summary>
	public sealed class ArchiveDiffView : IDiffView
	{
		private static readonly IBrush GridLine = Brushes.Gainsboro;

		private readonly Grid _root;

		private readonly TextBlock _srcTitle;

		private readonly TextBlock _dstTitle;

		private readonly TextBlock _status;

		private readonly TextBox _passwordBox;

		private readonly TextBlock _passwordHint;

		private readonly StackPanel _srcPanel;

		private readonly StackPanel _dstPanel;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private string _password = string.Empty;

		private int _renderGeneration;

		private bool _released;

		public ArchiveDiffView()
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

			_passwordBox = new TextBox
			{
				Width = 200.0,
				PasswordChar = '●',
				VerticalAlignment = VerticalAlignment.Center,
				PlaceholderText = PluginEnvironment.Translate("archive password (optional)"),
			};
			Button apply = new Button
			{
				Content = PluginEnvironment.Translate("Apply"),
				VerticalAlignment = VerticalAlignment.Center,
			};
			apply.Click += OnApplyPassword;
			_passwordHint = new TextBlock
			{
				FontSize = 12.0,
				Opacity = 0.75,
				VerticalAlignment = VerticalAlignment.Center,
				TextWrapping = TextWrapping.Wrap,
			};
			StackPanel passwordRow = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 8.0,
				Margin = new Thickness(12.0, 0.0, 12.0, 6.0),
			};
			passwordRow.Children.Add(new TextBlock
			{
				Text = PluginEnvironment.Translate("Password"),
				FontSize = 12.0,
				Opacity = 0.75,
				VerticalAlignment = VerticalAlignment.Center,
			});
			passwordRow.Children.Add(_passwordBox);
			passwordRow.Children.Add(apply);
			passwordRow.Children.Add(_passwordHint);

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
				RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
			};
			Grid.SetRow(header, 0);
			_root.Children.Add(header);
			Grid.SetRow(_status, 1);
			_root.Children.Add(_status);
			Grid.SetRow(passwordRow, 2);
			_root.Children.Add(passwordRow);
			Grid.SetRow(columns, 3);
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
			_context = context;
			_host = host;
			_srcTitle.Foreground = context?.SrcTitleBrush;
			_dstTitle.Foreground = context?.DstTitleBrush;
			_passwordHint.Text = string.Empty;
			PluginLog.Info($"ArchiveDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			StartRender();
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
			_passwordHint.Text = string.Empty;
		}

		private void OnApplyPassword(object sender, RoutedEventArgs e)
		{
			_password = _passwordBox.Text ?? string.Empty;
			_passwordHint.Text = string.Empty;
			StartRender();
		}

		// ---- 展开管线 ----

		private void StartRender()
		{
			CancelRender();
			_srcPanel.Children.Clear();
			_dstPanel.Children.Clear();
			UpdateTitles();
			if (_context == null)
			{
				return;
			}
			_status.Text = PluginEnvironment.Translate("Reading archive…");
			CancellationTokenSource cts = new CancellationTokenSource();
			_cts = cts;
			int generation = _renderGeneration;
			string password = _password;
			DiffViewContext context = _context;
			IDiffViewHost host = _host;
			Task.Run(() => ReadAsync(context, host, password, cts.Token, generation));
		}

		private async Task ReadAsync(DiffViewContext context, IDiffViewHost host, string password, CancellationToken token, int generation)
		{
			try
			{
				byte[] srcBytes = await ResolveSideAsync(context?.Src, context?.HexSrc, host, token).ConfigureAwait(false);
				byte[] dstBytes = await ResolveSideAsync(context?.Dst, context?.HexDst, host, token).ConfigureAwait(false);
				if (token.IsCancellationRequested)
				{
					return;
				}
				ReportMissing(generation, context, srcBytes, dstBytes);

				ArchiveModel srcModel = srcBytes == null ? null : ArchiveContentExtractor.Extract(context?.Src?.Path, srcBytes, password);
				ArchiveModel dstModel = dstBytes == null ? null : ArchiveContentExtractor.Extract(context?.Dst?.Path, dstBytes, password);
				if (token.IsCancellationRequested)
				{
					return;
				}

				if (srcModel != null)
				{
					PostModel(generation, 0, srcModel, password);
				}
				if (dstModel != null)
				{
					PostModel(generation, 1, dstModel, password);
				}

				int srcCount = srcModel?.EntryCount ?? 0;
				int dstCount = dstModel?.EntryCount ?? 0;
				PostStatus(generation, PluginEnvironment.Format("Archive compare: {0} / {1} entries", srcCount, dstCount));
				PostPasswordHint(generation, DescribePasswordHint(srcModel, dstModel, password));
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				PluginLog.Error("ArchiveDiffView read failed", ex);
				PostStatus(generation, PluginEnvironment.Translate("Failed to read archive") + ": " + ex.Message);
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
				PluginLog.Warn("Archive: failed to load side bytes for '" + side.Path + "'", ex);
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
				PluginLog.Warn("Archive: LFS cache lookup failed", ex);
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

		// ---- 条目树 → 控件 ----

		/// <summary>
		/// 把一侧的展开结果挂到栏内。
		/// 控件必须在 UI 线程构建（后台线程构建的 Avalonia 控件不会渲染出来），因此这里只投递
		/// 纯数据模型，在 UI 线程里再 <see cref="BuildSummary"/> / <see cref="BuildRow"/> 成控件后挂载。
		/// </summary>
		private void PostModel(int generation, int column, ArchiveModel model, string password)
		{
			if (model.Error != ArchiveError.None)
			{
				PostMessage(generation, column, DescribeError(model, password));
				return;
			}
			StackPanel panel = column == 0 ? _srcPanel : _dstPanel;
			Dispatcher.UIThread.Post(delegate
			{
				if (_released || generation != _renderGeneration)
				{
					return;
				}
				panel.Children.Add(BuildSummary(model));
				foreach (ArchiveEntryNode entry in model.Entries)
				{
					panel.Children.Add(BuildRow(entry));
				}
				if (model.Truncated)
				{
					panel.Children.Add(BuildNote(PluginEnvironment.Format("Showing first {0} entries only.", model.Entries.Count)));
				}
			});
		}

		private static Control BuildSummary(ArchiveModel model)
		{
			string line = model.Format;
			line = line + "  ·  " + PluginEnvironment.Format("{0} files, {1} folders", model.FileCount, model.DirectoryCount);
			if (model.TotalFileSize > 0L)
			{
				line = line + "  ·  " + PluginSizeFormat.ReadableFileSize(model.TotalFileSize, false);
			}
			if (model.HasEncrypted)
			{
				line = line + "  ·  " + PluginEnvironment.Translate("encrypted");
			}
			return new TextBlock
			{
				Text = line,
				FontSize = 12.0,
				Opacity = 0.7,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, 2.0, 0.0, 6.0),
			};
		}

		private static Control BuildRow(ArchiveEntryNode entry)
		{
			Grid row = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,Auto"),
				Margin = new Thickness(2.0 + entry.Depth * 14.0, 1.0, 4.0, 1.0),
			};
			string label = entry.IsDirectory ? entry.Name + "/" : entry.Name;
			TextBlock name = new TextBlock
			{
				Text = label,
				FontSize = 12.5,
				FontWeight = entry.IsDirectory ? FontWeight.SemiBold : FontWeight.Normal,
				TextWrapping = TextWrapping.NoWrap,
				TextTrimming = TextTrimming.CharacterEllipsis,
			};
			Grid.SetColumn(name, 0);
			row.Children.Add(name);
			if (entry.IsEncrypted)
			{
				TextBlock locked = new TextBlock
				{
					Text = "[" + PluginEnvironment.Translate("encrypted") + "]",
					FontSize = 11.0,
					Opacity = 0.7,
					Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
				};
				Grid.SetColumn(locked, 1);
				row.Children.Add(locked);
			}
			else if (!entry.IsDirectory)
			{
				TextBlock size = new TextBlock
				{
					Text = entry.Size.HasValue ? PluginSizeFormat.ReadableFileSize(entry.Size.Value, false) : string.Empty,
					FontSize = 12.0,
					Opacity = 0.6,
					HorizontalAlignment = HorizontalAlignment.Right,
					Margin = new Thickness(12.0, 0.0, 0.0, 0.0),
				};
				Grid.SetColumn(size, 1);
				row.Children.Add(size);
			}
			return row;
		}

		private static Control BuildNote(string text)
		{
			return new TextBlock
			{
				Text = text,
				FontSize = 12.0,
				Opacity = 0.7,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
			};
		}

		private static string DescribeError(ArchiveModel model, string password)
		{
			switch (model.Error)
			{
				case ArchiveError.PasswordRequired:
					return PluginEnvironment.Translate("Password required") + Environment.NewLine
						+ PluginEnvironment.Translate("Enter the archive password and press Apply.");
				case ArchiveError.PasswordIncorrect:
					return PluginEnvironment.Translate("Password incorrect") + Environment.NewLine
						+ PluginEnvironment.Translate("Enter the archive password and press Apply.");
				case ArchiveError.Unsupported:
					return PluginEnvironment.Translate("Unsupported archive format")
						+ (string.IsNullOrEmpty(model.ErrorDetail) ? string.Empty : Environment.NewLine + model.ErrorDetail);
				default:
					return PluginEnvironment.Translate("Failed to read archive")
						+ (string.IsNullOrEmpty(model.ErrorDetail) ? string.Empty : Environment.NewLine + model.ErrorDetail);
			}
		}

		private static string DescribePasswordHint(ArchiveModel src, ArchiveModel dst, string password)
		{
			ArchiveError error = FirstPasswordError(src, dst);
			switch (error)
			{
				case ArchiveError.PasswordRequired:
					return PluginEnvironment.Translate("This archive is encrypted. Enter the password and press Apply.");
				case ArchiveError.PasswordIncorrect:
					return PluginEnvironment.Translate("Incorrect password. Try again.");
				default:
					return string.Empty;
			}
		}

		private static ArchiveError FirstPasswordError(ArchiveModel src, ArchiveModel dst)
		{
			ArchiveModel[] models = new ArchiveModel[2] { src, dst };
			foreach (ArchiveModel model in models)
			{
				if (model != null && (model.Error == ArchiveError.PasswordRequired || model.Error == ArchiveError.PasswordIncorrect))
				{
					return model.Error;
				}
			}
			return ArchiveError.None;
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
			return PluginEnvironment.Translate("Archive content unavailable") + Environment.NewLine + side.Path;
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

		private void PostPasswordHint(int generation, string text)
		{
			Dispatcher.UIThread.Post(delegate
			{
				if (!_released && generation == _renderGeneration)
				{
					_passwordHint.Text = text;
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