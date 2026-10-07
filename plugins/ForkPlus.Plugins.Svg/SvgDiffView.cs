using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Svg
{
	/// <summary>
	/// SVG 矢量图对比视图：两侧 .svg 各自解析成元素树，既提供<strong>并排渲染</strong>
	/// （按各自 viewBox 等比缩放，肉眼直接比图形），也提供<strong>元素结构 diff</strong>
	/// （按「元素路径 @ 属性」逐项标出新增 / 删除 / 改值）。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、
	/// 自带的分段模式工具条（渲染 / 结构），最下是内容区。
	///
	/// 解析与 diff 在后台线程执行，控件只在 UI 线程构建；每次刷新用代次 + CancellationToken 取消上一轮。
	/// 单侧声明大小 &gt; 300 MB 只给提示、不解析。
	/// </summary>
	public sealed class SvgDiffView : IDiffView
	{
		private const string RenderMode = "render";

		private const string StructureMode = "structure";

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （structure，其余走默认 render），据此逐模式取图；正常运行时该变量为空，走默认并排渲染模式。
		/// </summary>
		private static readonly string InitialMode = ResolveInitialMode();

		private static string ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			return string.Equals(forced, StructureMode, StringComparison.OrdinalIgnoreCase) ? StructureMode : RenderMode;
		}

		/// <summary>单侧超过此大小不解析（与其它插件同口径）。</summary>
		private const long MaxSideBytes = 300L * 1024L * 1024L;

		/// <summary>结构表渲染上限：避免超大文件把 UI 拖死。</summary>
		private const int MaxRows = 4000;

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

		private readonly Button _renderButton;

		private readonly Button _structureButton;

		private readonly ContentControl _content;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private int _generation;

		private string _mode = InitialMode;

		private SvgDocument _left;

		private SvgDocument _right;

		private SvgDiffResult _diff;

		private string _error;

		private bool _srcAbsent;

		private bool _dstAbsent;

		private bool _tooLarge;

		public SvgDiffView()
		{
			_srcTitle = NewTitle();
			_dstTitle = NewTitle();
			_status = new TextBlock
			{
				Margin = new Thickness(12.0, 2.0, 12.0, 6.0),
				TextWrapping = TextWrapping.Wrap,
				FontSize = 12.0
			};
			_renderButton = NewModeButton(SvgStrings.T("Rendered"), delegate
			{
				SetMode(RenderMode);
			});
			_structureButton = NewModeButton(SvgStrings.T("Structure"), delegate
			{
				SetMode(StructureMode);
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
			modes.Children.Add(_renderButton);
			modes.Children.Add(_structureButton);

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

			StyleModeButton(_renderButton, _mode == RenderMode);
			StyleModeButton(_structureButton, _mode == StructureMode);
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
			PluginLog.Info($"SvgDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			Render();
		}

		public void SetMode(string modeId)
		{
			if (string.IsNullOrEmpty(modeId))
			{
				return;
			}
			if (modeId != RenderMode && modeId != StructureMode)
			{
				return;
			}
			if (modeId == _mode)
			{
				return;
			}
			_mode = modeId;
			StyleModeButton(_renderButton, _mode == RenderMode);
			StyleModeButton(_structureButton, _mode == StructureMode);
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
			_renderButton.Content = SvgStrings.T("Rendered");
			_structureButton.Content = SvgStrings.T("Structure");
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

			Task.Run(delegate
			{
				SvgDocument left = null;
				SvgDocument right = null;
				string error = null;
				try
				{
					string leftText = ReadText(src, hexSrc, cts.Token);
					string rightText = ReadText(dst, hexDst, cts.Token);
					cts.Token.ThrowIfCancellationRequested();
					if (src != null)
					{
						left = SvgParser.Parse(leftText, out string leftError);
						error = leftError;
					}
					if (dst != null)
					{
						right = SvgParser.Parse(rightText, out string rightError);
						error = error ?? rightError;
					}
					if (cts.Token.IsCancellationRequested)
					{
						return;
					}
					SvgDiffResult diff = SvgDiff.Compute(left?.Root, right?.Root);
					Dispatcher.UIThread.Post(delegate
					{
						if (_generation != generation || _cts != cts)
						{
							return;
						}
						_left = left;
						_right = right;
						_diff = diff;
						_error = error;
						UpdateStatus();
						BuildContent();
					});
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex)
				{
					PluginLog.Error("SvgDiffView 渲染失败", ex);
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

		/// <summary>取一侧文本：宿主 Hex 预载 &gt; 侧内容懒加载 &gt; LFS 本地缓存。</summary>
		private string ReadText(DiffSideContent side, MemoryStream preloaded, CancellationToken token)
		{
			if (side == null)
			{
				return string.Empty;
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
				return string.Empty;
			}
			token.ThrowIfCancellationRequested();
			return DecodeText(data);
		}

		private static string DecodeText(MemoryStream stream)
		{
			if (stream == null)
			{
				return string.Empty;
			}
			long position = stream.Position;
			try
			{
				stream.Position = 0L;
				using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true))
				{
					return reader.ReadToEnd();
				}
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
				return roleText + "  ·  " + SvgStrings.T("not present");
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

		private void UpdateStatus()
		{
			if (_tooLarge)
			{
				_status.Text = SvgStrings.T("File too large to preview");
				return;
			}
			if (_error != null)
			{
				_status.Text = SvgStrings.F("Failed to parse: {0}", _error);
				return;
			}
			if (_diff == null)
			{
				_status.Text = SvgStrings.T("Analyzing…");
				return;
			}
			string head = SvgStrings.F("SVG compare: {0} / {1} elements", Count(_left), Count(_right));
			if (!_diff.HasDifferences)
			{
				_status.Text = head + "  ·  " + SvgStrings.T("No differences");
				return;
			}
			_status.Text = head + "  ·  " + SvgStrings.F("Summary: {0} properties · {1} changed · {2} left only · {3} right only", _diff.Total, _diff.Changed, _diff.LeftOnly, _diff.RightOnly);
		}

		private static int Count(SvgDocument document)
		{
			return document?.ElementCount ?? 0;
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
				_content.Content = NoteText(SvgStrings.T("File too large to preview"));
				return;
			}
			if (_diff == null)
			{
				_content.Content = NoteText(_error == null ? SvgStrings.T("Analyzing…") : SvgStrings.F("Failed to parse: {0}", _error));
				return;
			}
			if (_mode == StructureMode)
			{
				_content.Content = BuildStructureContent();
			}
			else
			{
				_content.Content = BuildRenderContent();
			}
		}

		/// <summary>并排渲染：两列等宽，各侧按自身 viewBox 等比缩放，白底便于看清图形。</summary>
		private Control BuildRenderContent()
		{
			Grid panes = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,*")
			};
			Border leftCard = RenderCard(_left, _srcAbsent, SvgStrings.T("not present"));
			Border rightCard = RenderCard(_right, _dstAbsent, SvgStrings.T("not present"));
			Grid.SetColumn(leftCard, 0);
			Grid.SetColumn(rightCard, 1);
			panes.Children.Add(leftCard);
			panes.Children.Add(rightCard);
			return new ScrollViewer
			{
				Content = panes,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
			};
		}

		private Border RenderCard(SvgDocument document, bool absent, string absentText)
		{
			StackPanel stack = new StackPanel
			{
				Spacing = 6.0
			};
			if (absent || document?.Root == null)
			{
				stack.Children.Add(NoteText(absent ? absentText : SvgStrings.T("Content unavailable")));
			}
			else
			{
				stack.Children.Add(NoteText(SvgStrings.F("Elements: {0}  ·  viewBox {1}×{2}", document.ElementCount, Round(document.ViewWidth), Round(document.ViewHeight))));
				Border frame = new Border
				{
					Background = Brushes.White,
					BorderBrush = GridLine,
					BorderThickness = new Thickness(1.0),
					CornerRadius = new CornerRadius(4.0),
					Padding = new Thickness(8.0),
					MinHeight = 180.0,
					Child = SvgRenderer.Render(document)
				};
				stack.Children.Add(frame);
			}
			return new Border
			{
				Margin = new Thickness(0.0, 0.0, 6.0, 0.0),
				BorderBrush = GridLine,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(6.0),
				Background = Subtle,
				Padding = new Thickness(8.0),
				Child = new ScrollViewer
				{
					Content = stack,
					HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
					VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
				}
			};
		}

		private static string Round(double value)
		{
			return Math.Round(value, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
		}

		/// <summary>结构 diff：一张四列表——「元素 / 属性 | 旧 | 新 | 状态」，逐行四色标注底色。</summary>
		private Control BuildStructureContent()
		{
			Grid table = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("3*,2*,2*,Auto")
			};
			int row = 0;
			AddTableRow(table, ref row, SvgStrings.T("Element / attribute"), SvgStrings.T("Old"), SvgStrings.T("New"), SvgStrings.T("State"), null, null, true);

			int shown = 0;
			foreach (SvgDiffRow item in _diff.Rows)
			{
				if (shown >= MaxRows)
				{
					break;
				}
				shown++;
				AddTableRow(table, ref row, item.Path, Display(item.Left, item.LeftPresent), Display(item.Right, item.RightPresent), StateLabel(item.State), TintOf(item.State), StateBrush(item.State), false);
			}

			StackPanel stack = new StackPanel
			{
				Spacing = 6.0
			};
			stack.Children.Add(new Border
			{
				BorderBrush = GridLine,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(6.0),
				Background = Subtle,
				Padding = new Thickness(2.0),
				Child = table
			});
			if (_diff.Total > shown)
			{
				stack.Children.Add(NoteText(SvgStrings.F("Showing first {0} of {1} rows", shown, _diff.Total)));
			}
			if (_srcAbsent || _dstAbsent)
			{
				stack.Children.Add(NoteText(SvgStrings.T("One side is not present; values shown for the other side only.")));
			}
			return new ScrollViewer
			{
				Content = stack,
				HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
			};
		}

		private void AddTableRow(Grid table, ref int row, string path, string left, string right, string state, IBrush tint, IBrush stateBrush, bool header)
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
			AddCell(table, current, 0, path, header, true, header ? null : Brushes.Gray);
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

		private static IBrush TintOf(SvgState state)
		{
			switch (state)
			{
			case SvgState.Changed:
				return TintChanged;
			case SvgState.LeftOnly:
				return TintLeftOnly;
			case SvgState.RightOnly:
				return TintRightOnly;
			default:
				return null;
			}
		}

		private static IBrush StateBrush(SvgState state)
		{
			switch (state)
			{
			case SvgState.Changed:
				return StateChanged;
			case SvgState.LeftOnly:
				return StateLeft;
			case SvgState.RightOnly:
				return StateRight;
			default:
				return Brushes.Gray;
			}
		}

		private static string StateLabel(SvgState state)
		{
			switch (state)
			{
			case SvgState.Changed:
				return SvgStrings.T("changed");
			case SvgState.LeftOnly:
				return SvgStrings.T("left only");
			case SvgState.RightOnly:
				return SvgStrings.T("right only");
			default:
				return SvgStrings.T("same");
			}
		}

		private static string Display(string value, bool present)
		{
			if (!present)
			{
				return SvgStrings.T("not present");
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

		// ---- 小部件 ----

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
				Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
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
