using System;
using System.Collections.Generic;
using System.Globalization;
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

namespace ForkPlus.Plugins.Certificate
{
	/// <summary>
	/// 证书对比视图：左右两栏并排，各自把旧（左）/ 新（右）证书容器解析成可读字段。
	///
	/// 布局：顶部两栏标题（角色 + 文件名 + 大小，取宿主注入的主题画刷着色），其下状态行、
	/// 一行密码输入（<see cref="TextBox"/> + 「应用」按钮，仅加密的 .p12 / .pfx 相关）、
	/// 自带的分段模式工具条（详情 / 证书链），再下是两列内容区。
	///
	/// 模式：
	/// <list type="bullet">
	/// <item>证书详情：各自分组卡片（身份 / 有效期 / 密钥与签名 / 用途 / 扩展 / 指纹），
	/// 逐行「名 : 值」，并按 相同 / 变了 / 仅左 / 仅右 四色标注行底色；有效期用水平时间轴
	/// （按当前时间比例画「现在」标记，过期整条变红、即将过期变橙、有效变绿），SAN / KeyUsage /
	/// EKU 用彩色徽章呈现。</item>
	/// <item>证书链：容器含多张证书时逐张卡片列出，并标注两侧数量差异。</item>
	/// </list>
	///
	/// 字节来源与 <c>ArchiveDiffView</c> 一致：宿主 Hex 预载 &gt; 侧内容懒加载 &gt; LFS 缓存 / smudge。
	/// 解析在后台线程进行（全程 try/catch，畸形 DER 降级为「无法解析」），控件构建回到 UI 线程。
	///
	/// 安全边界：不导入 / 不导出私钥，不解密任何内容；.p12 / .pfx 只读证书链与别名。
	/// </summary>
	public sealed class CertificateDiffView : IDiffView
	{
		private enum RowTint
		{
			Same,
			Changed,
			LeftOnly,
			RightOnly
		}

		private const string DetailsMode = "details";

		private const string ChainMode = "chain";

		private static readonly IBrush GridLine = Brushes.Gainsboro;

		/// <summary>中性色都用带透明度的灰，浅色 / 深色主题下都保持低对比。</summary>
		private static readonly IBrush Subtle = new SolidColorBrush(Color.FromArgb(0x14, 0x80, 0x80, 0x80));

		private static readonly IBrush ChipTint = new SolidColorBrush(Color.FromArgb(0x1F, 0x80, 0x80, 0x80));

		/// <summary>行底色：相同（无底色）/ 变了（琥珀）/ 仅左（红）/ 仅右（绿）。</summary>
		private static readonly IBrush TintChanged = new SolidColorBrush(Color.FromArgb(0x2E, 0xE6, 0x7E, 0x22));

		private static readonly IBrush TintLeftOnly = new SolidColorBrush(Color.FromArgb(0x26, 0xC0, 0x39, 0x2B));

		private static readonly IBrush TintRightOnly = new SolidColorBrush(Color.FromArgb(0x26, 0x2E, 0x9E, 0x5B));

		/// <summary>有效期时间轴 / 状态色：有效绿、即将过期橙、已过期红。</summary>
		private static readonly IBrush StatusValid = new SolidColorBrush(Color.FromArgb(0xFF, 0x2E, 0x9E, 0x5B));

		private static readonly IBrush StatusSoon = new SolidColorBrush(Color.FromArgb(0xFF, 0xE0, 0x8A, 0x1E));

		private static readonly IBrush StatusExpired = new SolidColorBrush(Color.FromArgb(0xFF, 0xC0, 0x39, 0x2B));

		private static readonly IBrush NowMarker = new SolidColorBrush(Color.FromArgb(0xE6, 0x40, 0x40, 0x40));

		/// <summary>SAN 徽章按类型配色。</summary>
		private static readonly IBrush SanDns = new SolidColorBrush(Color.FromArgb(0x2E, 0x2F, 0x6F, 0xED));

		private static readonly IBrush SanIp = new SolidColorBrush(Color.FromArgb(0x2E, 0x8E, 0x44, 0xAD));

		private static readonly IBrush SanUri = new SolidColorBrush(Color.FromArgb(0x2E, 0x0E, 0x9F, 0x8E));

		private static readonly IBrush SanEmail = new SolidColorBrush(Color.FromArgb(0x2E, 0xD9, 0x7A, 0x06));

		/// <summary>指纹这类定宽文本用等宽字体，跨行对齐更好读；按可用性回退。</summary>
		private static readonly FontFamily MonoFont = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, Courier New, monospace");

		private readonly Grid _root;

		private readonly TextBlock _srcTitle;

		private readonly TextBlock _dstTitle;

		private readonly TextBlock _status;

		private readonly TextBlock _passwordLabel;

		private readonly TextBox _passwordBox;

		private readonly Button _applyButton;

		private readonly TextBlock _passwordHint;

		private readonly Button _detailsButton;

		private readonly Button _chainButton;

		private readonly ContentControl _srcPane;

		private readonly ContentControl _dstPane;

		private DiffViewContext _context;

		private IDiffViewHost _host;

		private CancellationTokenSource _cts;

		private string _password = string.Empty;

		/// <summary>
		/// CI 截图用的初始模式：无头截图脚本经环境变量 FORKPLUS_PLUGIN_VIEW_MODE 指定
		/// （chain，其余走默认详情），据此逐模式取图；正常运行时该变量为空。
		/// </summary>
		private static readonly string InitialMode = ResolveInitialMode();

		private static string ResolveInitialMode()
		{
			string forced = (Environment.GetEnvironmentVariable("FORKPLUS_PLUGIN_VIEW_MODE") ?? string.Empty).Trim();
			return string.Equals(forced, ChainMode, StringComparison.OrdinalIgnoreCase) ? ChainMode : DetailsMode;
		}

		private string _mode = InitialMode;

		private int _renderGeneration;

		private bool _released;

		private bool _contentReady;

		private CertificateSideModel _srcModel;

		private CertificateSideModel _dstModel;

		private string _srcPlaceholder;

		private string _dstPlaceholder;

		public CertificateDiffView()
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
				PlaceholderText = CertificateStrings.T("certificate password (optional)"),
			};
			// 「应用」是宿主已有 8 语言译文的通用词，继续走宿主翻译。
			_applyButton = new Button
			{
				Content = PluginEnvironment.Translate("Apply"),
				VerticalAlignment = VerticalAlignment.Center,
			};
			_applyButton.Click += OnApplyPassword;
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
			// 「Password」同样为宿主通用词。
			_passwordLabel = new TextBlock
			{
				Text = PluginEnvironment.Translate("Password"),
				FontSize = 12.0,
				Opacity = 0.75,
				VerticalAlignment = VerticalAlignment.Center,
			};
			passwordRow.Children.Add(_passwordLabel);
			passwordRow.Children.Add(_passwordBox);
			passwordRow.Children.Add(_applyButton);
			passwordRow.Children.Add(_passwordHint);

			// 自带分段模式工具条：[证书详情 | 证书链]。
			_detailsButton = NewModeButton(OnDetailsClick);
			_chainButton = NewModeButton(OnChainClick);
			StackPanel segments = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 2.0,
			};
			segments.Children.Add(_detailsButton);
			segments.Children.Add(_chainButton);
			Border toolbar = new Border
			{
				BorderBrush = GridLine,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(6.0),
				Padding = new Thickness(2.0),
				HorizontalAlignment = HorizontalAlignment.Left,
				Margin = new Thickness(12.0, 0.0, 12.0, 6.0),
				Child = segments,
			};

			_srcPane = new ContentControl
			{
				Margin = new Thickness(12.0, 0.0, 10.0, 12.0),
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				VerticalContentAlignment = VerticalAlignment.Stretch,
			};
			_dstPane = new ContentControl
			{
				Margin = new Thickness(10.0, 0.0, 12.0, 12.0),
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				VerticalContentAlignment = VerticalAlignment.Stretch,
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
				RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*"),
			};
			Grid.SetRow(header, 0);
			_root.Children.Add(header);
			Grid.SetRow(_status, 1);
			_root.Children.Add(_status);
			Grid.SetRow(passwordRow, 2);
			_root.Children.Add(passwordRow);
			Grid.SetRow(toolbar, 3);
			_root.Children.Add(toolbar);
			Grid.SetRow(columns, 4);
			_root.Children.Add(columns);

			UpdateStaticTexts();
			PluginEnvironment.ApplyLocalization(_root);
		}

		/// <summary>栏容器：一道分隔线 + 内容占位。内容自身的滚动由 <see cref="Scroll"/> 提供。</summary>
		private static Border WrapPane(Control content, Thickness separator)
		{
			return new Border
			{
				BorderBrush = GridLine,
				BorderThickness = separator,
				Child = content,
			};
		}

		private static Button NewModeButton(EventHandler<RoutedEventArgs> handler)
		{
			Button button = new Button
			{
				BorderThickness = new Thickness(0.0),
				// 宿主 Button 主题固定 Height=24，垂直 padding 合计须 ≤4px；加厚的观感交给 MinHeight 抬高。
				Padding = new Thickness(12.0, 0.0, 12.0, 0.0),
				MinHeight = 28.0,
				CornerRadius = new CornerRadius(4.0),
			};
			button.Click += handler;
			return button;
		}

		// ---- IDiffView ----

		Control IDiffView.View => _root;

		/// <summary>本视图自带模式工具条，不需要宿主渲染模式切换工具条。</summary>
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
			PluginLog.Info($"CertificateDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
			StartRender();
		}

		public void SetMode(string modeId)
		{
			if (string.IsNullOrEmpty(modeId))
			{
				return;
			}
			if (modeId != DetailsMode && modeId != ChainMode)
			{
				return;
			}
			if (_mode == modeId)
			{
				return;
			}
			_mode = modeId;
			UpdateModeButtons();
			if (_contentReady)
			{
				BuildPanes();
			}
		}

		public void Activate()
		{
		}

		public void Deactivate()
		{
		}

		/// <summary>
		/// 应用当前语言（宿主在语言切换时广播）：宿主 key 重刷 + 插件自带静态文案重算 +
		/// 内容区按缓存的 context / 宿主 / 密码重跑渲染。
		/// </summary>
		public void ApplyLocalization()
		{
			PluginEnvironment.ApplyLocalization(_root);
			UpdateStaticTexts();
			// 复用同一渲染入口（沿用代次 + CancellationToken 取消上一轮），
			// 使分组名、字段名、状态词等内容区文案即时切换语言。
			StartRender();
		}

		public void Release()
		{
			_released = true;
			CancelRender();
			_context = null;
			_host = null;
			_contentReady = false;
			_srcModel = null;
			_dstModel = null;
			_srcPlaceholder = null;
			_dstPlaceholder = null;
			_srcPane.Content = null;
			_dstPane.Content = null;
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

		private void OnDetailsClick(object sender, RoutedEventArgs e)
		{
			SetMode(DetailsMode);
		}

		private void OnChainClick(object sender, RoutedEventArgs e)
		{
			SetMode(ChainMode);
		}

		// ---- 渲染管线 ----

		/// <summary>按当前语言重算工具条、密码行等静态文案（构造后、语言切换都会走）。</summary>
		private void UpdateStaticTexts()
		{
			_passwordBox.PlaceholderText = CertificateStrings.T("certificate password (optional)");
			_passwordLabel.Text = PluginEnvironment.Translate("Password");
			_applyButton.Content = PluginEnvironment.Translate("Apply");
			_detailsButton.Content = CertificateStrings.T("Details");
			_chainButton.Content = CertificateStrings.T("Chain");
			UpdateModeButtons();
		}

		private void UpdateModeButtons()
		{
			bool details = _mode == DetailsMode;
			_detailsButton.Background = details ? Subtle : Brushes.Transparent;
			_detailsButton.FontWeight = details ? FontWeight.SemiBold : FontWeight.Normal;
			_chainButton.Background = details ? Brushes.Transparent : Subtle;
			_chainButton.FontWeight = details ? FontWeight.Normal : FontWeight.SemiBold;
		}

		private void StartRender()
		{
			CancelRender();
			_srcPane.Content = null;
			_dstPane.Content = null;
			_contentReady = false;
			_srcModel = null;
			_dstModel = null;
			_srcPlaceholder = null;
			_dstPlaceholder = null;
			UpdateTitles();
			if (_context == null)
			{
				return;
			}
			_status.Text = CertificateStrings.T("Parsing certificate…");
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
				CertificateSideModel srcModel = srcBytes == null ? null : CertificateParser.Parse(context?.Src?.Path, srcBytes, password);
				CertificateSideModel dstModel = dstBytes == null ? null : CertificateParser.Parse(context?.Dst?.Path, dstBytes, password);
				string srcPlaceholder = DescribePlaceholder(context?.Src, srcBytes);
				string dstPlaceholder = DescribePlaceholder(context?.Dst, dstBytes);
				if (token.IsCancellationRequested)
				{
					return;
				}
				PostParsed(generation, srcModel, dstModel, srcPlaceholder, dstPlaceholder);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				PluginLog.Error("CertificateDiffView read failed", ex);
				PostStatus(generation, CertificateStrings.T("Failed to parse certificate") + ": " + ex.Message);
			}
		}

		/// <summary>取一侧字节：宿主 Hex 预载 &gt; 侧内容懒加载 &gt; LFS 缓存 / smudge（与 ArchiveDiffView 完全一致）。</summary>
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
				PluginLog.Warn("Certificate: failed to load side bytes for '" + side.Path + "'", ex);
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
				PluginLog.Warn("Certificate: LFS cache lookup failed", ex);
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

		/// <summary>把两侧解析结果投递到 UI 线程，缓存后构建两栏内容。</summary>
		private void PostParsed(int generation, CertificateSideModel srcModel, CertificateSideModel dstModel, string srcPlaceholder, string dstPlaceholder)
		{
			Dispatcher.UIThread.Post(delegate
			{
				if (_released || generation != _renderGeneration)
				{
					return;
				}
				_srcModel = srcModel;
				_dstModel = dstModel;
				_srcPlaceholder = srcPlaceholder;
				_dstPlaceholder = dstPlaceholder;
				_contentReady = true;
				BuildPanes();
				_status.Text = CertificateStrings.F("Certificate compare: {0} / {1} certificates", srcModel?.Certificates.Count ?? 0, dstModel?.Certificates.Count ?? 0);
				_passwordHint.Text = DescribePasswordHint(srcModel, dstModel);
			});
		}

		/// <summary>按当前模式重建两栏（模式切换复用缓存的模型，不重新解析）。</summary>
		private void BuildPanes()
		{
			_srcPane.Content = BuildSide(_srcModel, _dstModel, _srcPlaceholder, isLeft: true);
			_dstPane.Content = BuildSide(_dstModel, _srcModel, _dstPlaceholder, isLeft: false);
		}

		/// <summary>构建一侧内容：占位提示 → CRL 降级 → 错误文案 → 详情 / 证书链。</summary>
		private Control BuildSide(CertificateSideModel model, CertificateSideModel other, string placeholder, bool isLeft)
		{
			if (placeholder != null)
			{
				return Scroll(Message(placeholder));
			}
			if (model == null)
			{
				return Scroll(Message(CertificateStrings.T("not present")));
			}
			if (model.Container == CertificateContainer.Crl)
			{
				return Scroll(BuildCrl(model));
			}
			if (model.Error != CertificateError.None)
			{
				return Scroll(Message(DescribeError(model)));
			}
			if (model.Certificates.Count == 0)
			{
				return Scroll(Message(CertificateStrings.T("Unsupported certificate container")));
			}
			return Scroll(_mode == ChainMode ? BuildChain(model, other, isLeft) : BuildDetails(model, other, isLeft));
		}

		private static string DescribePlaceholder(DiffSideContent side, byte[] bytes)
		{
			if (side == null)
			{
				return CertificateStrings.T("not present");
			}
			if (bytes == null)
			{
				return CertificateStrings.T("Certificate content unavailable") + Environment.NewLine + side.Path;
			}
			return null;
		}

		private static string DescribePasswordHint(CertificateSideModel src, CertificateSideModel dst)
		{
			CertificateError error = FirstPasswordError(src, dst);
			switch (error)
			{
				case CertificateError.PasswordRequired:
					return CertificateStrings.T("Password required");
				case CertificateError.PasswordIncorrect:
					return CertificateStrings.T("Incorrect password. Enter the password and press Apply.");
				default:
					return string.Empty;
			}
		}

		private static CertificateError FirstPasswordError(CertificateSideModel src, CertificateSideModel dst)
		{
			CertificateSideModel[] models = new CertificateSideModel[2] { src, dst };
			foreach (CertificateSideModel model in models)
			{
				if (model != null && (model.Error == CertificateError.PasswordRequired || model.Error == CertificateError.PasswordIncorrect))
				{
					return model.Error;
				}
			}
			return CertificateError.None;
		}

		private static string DescribeError(CertificateSideModel model)
		{
			switch (model.Error)
			{
				case CertificateError.Unsupported:
				case CertificateError.NoCertificate:
					return CertificateStrings.T("Unsupported certificate container");
				case CertificateError.PasswordRequired:
					return CertificateStrings.T("Password required");
				case CertificateError.PasswordIncorrect:
					return CertificateStrings.T("Incorrect password. Enter the password and press Apply.");
				default:
					return CertificateStrings.T("Failed to parse certificate")
						+ (string.IsNullOrEmpty(model.ErrorDetail) ? string.Empty : Environment.NewLine + model.ErrorDetail);
			}
		}

		// ---- 详情模式 ----

		/// <summary>详情：容器摘要 + 分组字段行（按 相同 / 变了 / 仅左 / 仅右 着色）。</summary>
		private static Control BuildDetails(CertificateSideModel model, CertificateSideModel other, bool isLeft)
		{
			CertificateInfo primary = model.Certificates[0];
			CertificateInfo otherPrimary = GetPrimary(other);
			List<FieldSpec> mine = BuildFieldSpecs(primary);
			List<FieldSpec> theirs = otherPrimary == null ? null : BuildFieldSpecs(otherPrimary);

			StackPanel panel = new StackPanel
			{
				Margin = new Thickness(0.0, 2.0, 0.0, 8.0),
			};
			panel.Children.Add(ContainerChips(model));

			string currentGroup = null;
			for (int i = 0; i < mine.Count; i++)
			{
				FieldSpec spec = mine[i];
				string ownValue = spec.Value();
				if (ownValue == null)
				{
					// 本侧缺失该字段：不渲染该行（对侧若存在会以「仅左 / 仅右」着色显示）。
					continue;
				}
				if (spec.Group != currentGroup)
				{
					currentGroup = spec.Group;
					panel.Children.Add(GroupHeader(CertificateStrings.T(currentGroup)));
				}
				string otherValue = theirs != null && i < theirs.Count ? theirs[i].Value() : null;
				RowTint tint;
				if (otherValue == null)
				{
					tint = isLeft ? RowTint.LeftOnly : RowTint.RightOnly;
				}
				else if (otherValue == ownValue)
				{
					tint = RowTint.Same;
				}
				else
				{
					tint = RowTint.Changed;
				}
				panel.Children.Add(BuildRow(spec, tint));
			}
			return panel;
		}

		private static CertificateInfo GetPrimary(CertificateSideModel model)
		{
			if (model == null || model.Error != CertificateError.None || model.Certificates.Count == 0)
			{
				return null;
			}
			return model.Certificates[0];
		}

		/// <summary>字段表：顺序两侧一致，便于逐行对齐与比较。</summary>
		private static List<FieldSpec> BuildFieldSpecs(CertificateInfo cert)
		{
			List<FieldSpec> specs = new List<FieldSpec>();
			Add(specs, "Identity", "Subject", () => cert.Subject);
			Add(specs, "Identity", "Issuer", () => cert.Issuer);
			Add(specs, "Identity", "Serial number", () => cert.SerialNumber, mono: true);
			Add(specs, "Identity", "Self-signed", () => cert.SelfSigned ? CertificateStrings.T("Yes") : CertificateStrings.T("No"));
			Add(specs, "Validity", "Not before", () => FormatDate(cert.NotBefore));
			Add(specs, "Validity", "Not after", () => FormatDate(cert.NotAfter));
			specs.Add(new FieldSpec
			{
				Group = "Validity",
				LabelKey = null,
				Value = () => FormatDate(cert.NotBefore) + " - " + FormatDate(cert.NotAfter),
				Custom = () => BuildTimelineBlock(cert, false)
			});
			Add(specs, "Key & Signature", "Public key", () => DescribeKeyText(cert));
			Add(specs, "Key & Signature", "Signature algorithm", () => DescribeSignatureText(cert));
			specs.Add(new FieldSpec
			{
				Group = "Usage",
				LabelKey = "Key usage",
				Value = () => JoinOrNull(cert.KeyUsages),
				Custom = () => cert.KeyUsages.Count > 0 ? ChipList(cert.KeyUsages, Subtle) : null
			});
			specs.Add(new FieldSpec
			{
				Group = "Usage",
				LabelKey = "Extended key usage",
				Value = () => JoinOrNull(cert.ExtendedKeyUsages),
				Custom = () => cert.ExtendedKeyUsages.Count > 0 ? ChipList(cert.ExtendedKeyUsages, Subtle) : null
			});
			Add(specs, "Usage", "Basic constraints", () => cert.IsCa.HasValue ? (cert.IsCa.Value ? CertificateStrings.T("CA") : CertificateStrings.T("End entity")) : null);
			Add(specs, "Usage", "Path length", () => cert.PathLength.HasValue ? cert.PathLength.Value.ToString(CultureInfo.InvariantCulture) : null);
			specs.Add(new FieldSpec
			{
				Group = "Extensions",
				LabelKey = "SAN",
				Value = () => DescribeSans(cert),
				Custom = () => cert.Sans.Count > 0 ? BuildSanChips(cert.Sans) : null
			});
			Add(specs, "Extensions", "CRL distribution", () => JoinOrNull(cert.CrlDistributionPoints), mono: true);
			Add(specs, "Extensions", "OCSP", () => JoinOrNull(cert.OcspUrls), mono: true);
			Add(specs, "Fingerprints", "Fingerprint SHA-1", () => cert.ThumbprintSha1, mono: true);
			Add(specs, "Fingerprints", "Fingerprint SHA-256", () => cert.ThumbprintSha256, mono: true);
			return specs;
		}

		private static void Add(List<FieldSpec> specs, string group, string labelKey, Func<string> value, bool mono = false)
		{
			specs.Add(new FieldSpec
			{
				Group = group,
				LabelKey = labelKey,
				Value = value,
				Mono = mono
			});
		}

		private static Control BuildRow(FieldSpec spec, RowTint tint)
		{
			Grid grid = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("126,*"),
			};
			if (spec.LabelKey != null)
			{
				TextBlock label = new TextBlock
				{
					Text = CertificateStrings.T(spec.LabelKey),
					FontSize = 12.0,
					Opacity = 0.7,
					TextWrapping = TextWrapping.Wrap,
					VerticalAlignment = VerticalAlignment.Top,
					Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
				};
				Grid.SetColumn(label, 0);
				grid.Children.Add(label);
			}
			Control value = spec.Custom?.Invoke();
			if (value == null)
			{
				SelectableTextBlock text = new SelectableTextBlock
				{
					Text = spec.Value() ?? string.Empty,
					FontSize = 12.0,
					TextWrapping = TextWrapping.Wrap,
				};
				if (spec.Mono)
				{
					text.FontFamily = MonoFont;
					text.FontSize = 11.0;
				}
				value = text;
			}
			if (spec.LabelKey == null)
			{
				Grid.SetColumn(value, 0);
				Grid.SetColumnSpan(value, 2);
			}
			else
			{
				Grid.SetColumn(value, 1);
			}
			grid.Children.Add(value);
			return new Border
			{
				Background = TintBrush(tint),
				CornerRadius = new CornerRadius(4.0),
				Padding = new Thickness(6.0, 3.0, 6.0, 3.0),
				Margin = new Thickness(0.0, 1.0, 0.0, 1.0),
				Child = grid,
			};
		}

		private static IBrush TintBrush(RowTint tint)
		{
			switch (tint)
			{
				case RowTint.Changed:
					return TintChanged;
				case RowTint.LeftOnly:
					return TintLeftOnly;
				case RowTint.RightOnly:
					return TintRightOnly;
				default:
					return Brushes.Transparent;
			}
		}

		// ---- 证书链模式 ----

		private static Control BuildChain(CertificateSideModel model, CertificateSideModel other, bool isLeft)
		{
			StackPanel panel = new StackPanel
			{
				Margin = new Thickness(0.0, 2.0, 0.0, 8.0),
			};
			panel.Children.Add(ContainerChips(model));
			panel.Children.Add(Note(CertificateStrings.F("{0} shared, {1} only left, {2} only right", CountShared(model, other), CountOnly(model, other), CountOnly(other, model))));

			int index = 1;
			foreach (CertificateInfo cert in model.Certificates)
			{
				bool shared = ContainsThumbprint(other, cert.ThumbprintSha1);
				RowTint tint = shared ? RowTint.Same : (isLeft ? RowTint.LeftOnly : RowTint.RightOnly);
				panel.Children.Add(BuildChainCard(index, cert, tint));
				index++;
			}
			return panel;
		}

		private static Control BuildChainCard(int index, CertificateInfo cert, RowTint tint)
		{
			StackPanel content = new StackPanel
			{
				Spacing = 2.0,
			};
			StackPanel title = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
			};
			title.Children.Add(Chip(Label("#" + index.ToString(CultureInfo.InvariantCulture), 11.0, FontWeight.SemiBold, 1.0), ChipTint));
			TextBlock subject = new TextBlock
			{
				Text = cert.Subject,
				FontSize = 12.0,
				FontWeight = FontWeight.SemiBold,
				TextWrapping = TextWrapping.Wrap,
				VerticalAlignment = VerticalAlignment.Center,
			};
			title.Children.Add(subject);
			content.Children.Add(title);
			content.Children.Add(new TextBlock
			{
				Text = CertificateStrings.T("Authority") + " : " + cert.Issuer,
				FontSize = 11.5,
				Opacity = 0.75,
				TextWrapping = TextWrapping.Wrap,
			});
			content.Children.Add(BuildTimelineBlock(cert, true));
			return new Border
			{
				Background = TintBrush(tint),
				BorderBrush = GridLine,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(6.0),
				Padding = new Thickness(8.0),
				Margin = new Thickness(0.0, 2.0, 0.0, 2.0),
				Child = content,
			};
		}

		private static int CountShared(CertificateSideModel model, CertificateSideModel other)
		{
			int count = 0;
			foreach (CertificateInfo cert in model.Certificates)
			{
				if (ContainsThumbprint(other, cert.ThumbprintSha1))
				{
					count++;
				}
			}
			return count;
		}

		private static int CountOnly(CertificateSideModel model, CertificateSideModel other)
		{
			if (model == null)
			{
				return 0;
			}
			int count = 0;
			foreach (CertificateInfo cert in model.Certificates)
			{
				if (!ContainsThumbprint(other, cert.ThumbprintSha1))
				{
					count++;
				}
			}
			return count;
		}

		private static bool ContainsThumbprint(CertificateSideModel model, string thumbprint)
		{
			if (model == null || model.Error != CertificateError.None || string.IsNullOrEmpty(thumbprint))
			{
				return false;
			}
			foreach (CertificateInfo cert in model.Certificates)
			{
				if (string.Equals(cert.ThumbprintSha1, thumbprint, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}

		// ---- CRL 降级 ----

		private static Control BuildCrl(CertificateSideModel model)
		{
			StackPanel panel = new StackPanel
			{
				Margin = new Thickness(0.0, 2.0, 0.0, 8.0),
			};
			StackPanel chips = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Margin = new Thickness(0.0, 0.0, 0.0, 6.0),
			};
			chips.Children.Add(Chip(Label(CertificateStrings.T("CRL"), 11.5, FontWeight.SemiBold, 1.0), ChipTint));
			chips.Children.Add(Chip(Label(PluginSizeFormat.ReadableFileSize(model.SizeBytes, false), 11.5, FontWeight.Normal, 0.75), Subtle));
			panel.Children.Add(chips);
			if (!string.IsNullOrEmpty(model.ContainerHeadline))
			{
				panel.Children.Add(new SelectableTextBlock
				{
					Text = model.ContainerHeadline,
					FontFamily = MonoFont,
					FontSize = 11.5,
					Opacity = 0.8,
					TextWrapping = TextWrapping.Wrap,
				});
			}
			panel.Children.Add(Note(CertificateStrings.T("CRL parsing is not supported")));
			return panel;
		}

		// ---- 通用控件 ----

		/// <summary>容器摘要徽章：容器类型 + 证书数量。</summary>
		private static Control ContainerChips(CertificateSideModel model)
		{
			StackPanel chips = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Margin = new Thickness(0.0, 0.0, 0.0, 6.0),
			};
			chips.Children.Add(Chip(Label(CertificateStrings.T(ContainerLabel(model.Container)), 11.5, FontWeight.SemiBold, 1.0), ChipTint));
			chips.Children.Add(Chip(Label(CertificateStrings.F("Certificates: {0}", model.Certificates.Count), 11.5, FontWeight.Normal, 0.75), Subtle));
			return chips;
		}

		private static string ContainerLabel(CertificateContainer container)
		{
			switch (container)
			{
				case CertificateContainer.Pkcs12:
					return "PKCS#12";
				case CertificateContainer.Pkcs7:
					return "PKCS#7";
				case CertificateContainer.Crl:
					return "CRL";
				default:
					return "DER certificate";
			}
		}

		private static Control GroupHeader(string text)
		{
			return new TextBlock
			{
				Text = text,
				FontSize = 11.5,
				FontWeight = FontWeight.SemiBold,
				Opacity = 0.6,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, 8.0, 0.0, 3.0),
			};
		}

		/// <summary>SAN 彩色徽章：DNS / IP / URI / Email 各自底色，逐条列出。</summary>
		private static Control BuildSanChips(List<SanEntry> entries)
		{
			WrapPanel panel = new WrapPanel
			{
				Orientation = Orientation.Horizontal,
			};
			foreach (SanEntry entry in entries)
			{
				IBrush background;
				string kind;
				switch (entry.Kind)
				{
					case SanKind.Dns:
						background = SanDns;
						kind = "DNS";
						break;
					case SanKind.Ip:
						background = SanIp;
						kind = "IP";
						break;
					case SanKind.Uri:
						background = SanUri;
						kind = "URI";
						break;
					case SanKind.Email:
						background = SanEmail;
						kind = "Email";
						break;
					default:
						background = Subtle;
						kind = null;
						break;
				}
				string text = string.IsNullOrEmpty(kind) ? entry.Value : CertificateStrings.T(kind) + " " + entry.Value;
				Border chip = Chip(Label(text, 11.0, FontWeight.Normal, 0.9), background);
				chip.Margin = new Thickness(0.0, 0.0, 4.0, 4.0);
				panel.Children.Add(chip);
			}
			return panel;
		}

		private static Control ChipList(List<string> texts, IBrush background)
		{
			WrapPanel panel = new WrapPanel
			{
				Orientation = Orientation.Horizontal,
			};
			foreach (string text in texts)
			{
				Border chip = Chip(Label(text, 11.0, FontWeight.Normal, 0.9), background);
				chip.Margin = new Thickness(0.0, 0.0, 4.0, 4.0);
				panel.Children.Add(chip);
			}
			return panel;
		}

		/// <summary>有效期时间轴：一条从 NotBefore 到 NotAfter 的横条 + 按当前时间比例的「现在」标记。</summary>
		private static Control BuildTimelineBlock(CertificateInfo cert, bool compact)
		{
			DateTime now = DateTime.UtcNow;
			CertificateValidity validity = cert.GetValidity(now);
			double barWidth = compact ? 150.0 : 240.0;
			double barHeight = compact ? 6.0 : 8.0;
			double canvasHeight = compact ? 12.0 : 16.0;
			double ratio = ComputeRatio(cert.NotBefore, cert.NotAfter, now);

			Canvas canvas = new Canvas
			{
				Width = barWidth,
				Height = canvasHeight,
			};
			Border bar = new Border
			{
				Width = barWidth,
				Height = barHeight,
				CornerRadius = new CornerRadius(barHeight / 2.0),
				Background = StatusBrush(validity),
			};
			Canvas.SetLeft(bar, 0.0);
			Canvas.SetTop(bar, (canvasHeight - barHeight) / 2.0);
			canvas.Children.Add(bar);
			Border marker = new Border
			{
				Width = 2.0,
				Height = canvasHeight,
				Background = NowMarker,
			};
			Canvas.SetLeft(marker, ratio * (barWidth - 2.0));
			Canvas.SetTop(marker, 0.0);
			canvas.Children.Add(marker);

			TextBlock state = new TextBlock
			{
				Text = CertificateStrings.T(ValidityKey(validity)),
				FontSize = compact ? 11.0 : 11.5,
				FontWeight = FontWeight.SemiBold,
				Foreground = StatusBrush(validity),
				VerticalAlignment = VerticalAlignment.Center,
			};
			TextBlock days = new TextBlock
			{
				Text = DaysText(cert, now, validity),
				FontSize = compact ? 11.0 : 11.5,
				Opacity = 0.7,
				VerticalAlignment = VerticalAlignment.Center,
			};
			if (compact)
			{
				StackPanel line = new StackPanel
				{
					Orientation = Orientation.Horizontal,
					Spacing = 6.0,
				};
				line.Children.Add(canvas);
				line.Children.Add(state);
				line.Children.Add(days);
				return line;
			}

			StackPanel panel = new StackPanel
			{
				Spacing = 2.0,
			};
			panel.Children.Add(canvas);
			Grid dates = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,*"),
			};
			TextBlock from = new TextBlock
			{
				Text = FormatDate(cert.NotBefore),
				FontSize = 10.5,
				Opacity = 0.6,
			};
			Grid.SetColumn(from, 0);
			dates.Children.Add(from);
			TextBlock to = new TextBlock
			{
				Text = FormatDate(cert.NotAfter),
				FontSize = 10.5,
				Opacity = 0.6,
				HorizontalAlignment = HorizontalAlignment.Right,
			};
			Grid.SetColumn(to, 1);
			dates.Children.Add(to);
			panel.Children.Add(dates);
			StackPanel statusLine = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
			};
			statusLine.Children.Add(state);
			statusLine.Children.Add(days);
			panel.Children.Add(statusLine);
			return panel;
		}

		private static double ComputeRatio(DateTime from, DateTime to, DateTime now)
		{
			double total = (to - from).TotalSeconds;
			if (total <= 0.0)
			{
				return 1.0;
			}
			double ratio = (now - from).TotalSeconds / total;
			if (ratio < 0.0)
			{
				return 0.0;
			}
			if (ratio > 1.0)
			{
				return 1.0;
			}
			return ratio;
		}

		private static string ValidityKey(CertificateValidity validity)
		{
			switch (validity)
			{
				case CertificateValidity.Expired:
					return "expired";
				case CertificateValidity.ExpiringSoon:
					return "expiring soon";
				default:
					return "valid";
			}
		}

		private static IBrush StatusBrush(CertificateValidity validity)
		{
			switch (validity)
			{
				case CertificateValidity.Expired:
					return StatusExpired;
				case CertificateValidity.ExpiringSoon:
					return StatusSoon;
				default:
					return StatusValid;
			}
		}

		private static string DaysText(CertificateInfo cert, DateTime now, CertificateValidity validity)
		{
			if (validity == CertificateValidity.Expired)
			{
				int expired = (int)Math.Max(0.0, Math.Floor((now - cert.NotAfter).TotalDays));
				return CertificateStrings.F("expired {0} days ago", expired);
			}
			int left = (int)Math.Max(0.0, Math.Floor((cert.NotAfter - now).TotalDays));
			return CertificateStrings.F("{0} days left", left);
		}

		private static string DescribeKeyText(CertificateInfo cert)
		{
			if (string.IsNullOrEmpty(cert.PublicKeyAlgorithm))
			{
				return null;
			}
			return cert.PublicKeySize.HasValue
				? cert.PublicKeyAlgorithm + " " + cert.PublicKeySize.Value.ToString(CultureInfo.InvariantCulture) + " bit"
				: cert.PublicKeyAlgorithm;
		}

		private static string DescribeSignatureText(CertificateInfo cert)
		{
			if (string.IsNullOrEmpty(cert.SignatureAlgorithm) && string.IsNullOrEmpty(cert.SignatureAlgorithmOid))
			{
				return null;
			}
			if (string.IsNullOrEmpty(cert.SignatureAlgorithmOid))
			{
				return cert.SignatureAlgorithm;
			}
			if (string.IsNullOrEmpty(cert.SignatureAlgorithm))
			{
				return cert.SignatureAlgorithmOid;
			}
			return cert.SignatureAlgorithm + " (" + cert.SignatureAlgorithmOid + ")";
		}

		private static string DescribeSans(CertificateInfo cert)
		{
			if (cert.Sans.Count == 0)
			{
				return null;
			}
			List<string> parts = new List<string>(cert.Sans.Count);
			foreach (SanEntry entry in cert.Sans)
			{
				parts.Add(entry.Kind.ToString() + ":" + entry.Value);
			}
			return string.Join(", ", parts);
		}

		private static string JoinOrNull(List<string> values)
		{
			if (values == null || values.Count == 0)
			{
				return null;
			}
			return string.Join(", ", values);
		}

		private static string FormatDate(DateTime value)
		{
			return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
		}

		private static Control Scroll(Control content)
		{
			return new ScrollViewer
			{
				Content = content,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			};
		}

		private static Control Message(string text)
		{
			return new TextBlock
			{
				Text = text,
				FontSize = 12.0,
				Opacity = 0.7,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, 4.0, 0.0, 8.0),
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

		/// <summary>小徽章：浅底 + 圆角，承载容器名 / 数量 / SAN / 用途这类短标签。</summary>
		private static Border Chip(Control child, IBrush background)
		{
			return new Border
			{
				Background = background,
				CornerRadius = new CornerRadius(4.0),
				Padding = new Thickness(7.0, 2.0, 7.0, 2.0),
				VerticalAlignment = VerticalAlignment.Center,
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
				return text + "  ·  " + CertificateStrings.T("not present");
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

		/// <summary>一条字段行：分组 + 标签 + 取值（用 <see cref="Func{TResult}"/> 延迟求值，便于两侧比较）。</summary>
		private sealed class FieldSpec
		{
			public string Group { get; set; }

			public string LabelKey { get; set; }

			public Func<string> Value { get; set; }

			public Func<Control> Custom { get; set; }

			public bool Mono { get; set; }
		}
	}
}
