using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Example
{
	/// <summary>
	/// 示例对比视图：把左右两侧的路径 / 声明大小 / 可读字节数与 LFS 引用渲染成两列只读信息面板。
	///
	/// 纯代码构建 UI（未使用 .axaml），足以演示 <see cref="IDiffView"/> 的完整生命周期：
	/// CreateView → SetContent →（SetMode / Activate / Deactivate / ApplyLocalization 循环）→ Release。
	/// </summary>
	public sealed class ExampleDiffView : IDiffView
	{
		private readonly Grid _root;
		private readonly TextBlock _header;
		private readonly TextBlock _srcText;
		private readonly TextBlock _dstText;

		/// <summary>最近一次下发的内容（语言热切换时按它重算文案）。</summary>
		private DiffViewContext _context;

		public ExampleDiffView()
		{
			_srcText = NewSideText();
			_dstText = NewSideText();
			_root = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,*"),
				RowDefinitions = new RowDefinitions("Auto,*"),
			};

			// 顶部提示行（文案走插件自带译文表，随宿主界面语言切换）
			_header = new TextBlock
			{
				Margin = new Thickness(12.0, 8.0),
				FontWeight = FontWeight.SemiBold,
				Text = ExampleStrings.T("Example Plugin"),
			};
			Grid.SetRow(_header, 0);
			Grid.SetColumnSpan(_header, 2);
			_root.Children.Add(_header);

			Border srcPanel = NewSidePanel(_srcText);
			Grid.SetRow(srcPanel, 1);
			Grid.SetColumn(srcPanel, 0);
			_root.Children.Add(srcPanel);

			Border dstPanel = NewSidePanel(_dstText);
			Grid.SetRow(dstPanel, 1);
			Grid.SetColumn(dstPanel, 1);
			_root.Children.Add(dstPanel);

			// 构造期先应用一次语言，之后宿主切语言会再调 ApplyLocalization。
			PluginEnvironment.ApplyLocalization(_root);
		}

		// ---- IDiffView ----

		/// <summary>视图根控件（宿主直接挂进自己的容器）。</summary>
		Control IDiffView.View => _root;

		/// <summary>本示例自带单一视图，不需要宿主渲染模式切换工具条。</summary>
		IReadOnlyList<DiffViewMode> IDiffView.Modes => Array.Empty<DiffViewMode>();

		/// <summary>无像素级差异高亮能力，不触发（显式空实现，避免 CS0067）。</summary>
		event EventHandler<bool> IDiffView.HighlightPixelsAvailableChanged
		{
			add { }
			remove { }
		}

		/// <summary>下发内容：两侧数据 + 宿主能力桥。每次刷新对比都会调用（实例会被复用）。</summary>
		public void SetContent(DiffViewContext context, IDiffViewHost host)
		{
			_context = context;
			UpdateText();
			// 日志汇入宿主 NLog 输出（契约不引用宿主类型，只用 PluginLog 门面）。
			PluginLog.Info($"ExampleDiffView.SetContent src='{context?.Src?.Path ?? "<none>"}' dst='{context?.Dst?.Path ?? "<none>"}'");
		}

		/// <summary>切换视图模式：本示例只有一个视图，无模式可切。</summary>
		public void SetMode(string modeId)
		{
		}

		/// <summary>切入显示（如恢复动图播放）；本示例无后台任务。</summary>
		public void Activate()
		{
		}

		/// <summary>切走 / 失活（如暂停动图播放）；本示例无后台任务。</summary>
		public void Deactivate()
		{
		}

		/// <summary>应用当前语言（宿主在语言切换时广播）：宿主 key 重刷 + 插件自带文案重算。</summary>
		public void ApplyLocalization()
		{
			PluginEnvironment.ApplyLocalization(_root);
			UpdateText();
		}

		/// <summary>释放：停后台任务、退订事件、释放位图；之后实例不再复用。</summary>
		public void Release()
		{
			_context = null;
			_header.Text = string.Empty;
			_srcText.Text = string.Empty;
			_dstText.Text = string.Empty;
		}

		// ---- 内容装配 ----

		/// <summary>按当前语言重算全部插件自带文案（构造后、SetContent、语言切换都会走）。</summary>
		private void UpdateText()
		{
			_header.Text = ExampleStrings.T("Example Plugin");
			_srcText.Text = Describe(_context?.Src, "old");
			_dstText.Text = Describe(_context?.Dst, "new");
		}

		private static string Describe(DiffSideContent side, string role)
		{
			// 角色名（old / new / created / removed）是宿主已有 8 语言译文的通用词，继续走宿主翻译。
			string roleText = PluginEnvironment.Translate(role);
			if (side == null)
			{
				// 一侧不存在：新增文件（只有右侧）/ 删除文件（只有左侧）
				return roleText + Environment.NewLine + Environment.NewLine + ExampleStrings.T("(not present)");
			}
			List<string> lines = new List<string>
			{
				roleText,
				string.Empty,
				side.Path ?? string.Empty,
			};
			if (side.Size.HasValue)
			{
				lines.Add(PluginSizeFormat.ReadableFileSize(side.Size.Value));
			}
			// DiffSideContent.Data 为懒加载 + 缓存；不可用（未加载 / 超阈值）时为 null。
			MemoryStream data = side.Data;
			if (data != null)
			{
				lines.Add(ExampleStrings.F("Loaded bytes: {0}", data.Length));
			}
			if (side.Lfs != null)
			{
				lines.Add(ExampleStrings.F("LFS: {0}", side.Lfs.Sha256));
			}
			return string.Join(Environment.NewLine, lines);
		}

		private static TextBlock NewSideText()
		{
			return new TextBlock
			{
				Margin = new Thickness(12.0),
				TextWrapping = TextWrapping.Wrap,
				VerticalAlignment = VerticalAlignment.Top,
			};
		}

		private static Border NewSidePanel(TextBlock text)
		{
			return new Border
			{
				Margin = new Thickness(8.0),
				Padding = new Thickness(4.0),
				BorderThickness = new Thickness(1.0),
				BorderBrush = Brushes.Gainsboro,
				CornerRadius = new CornerRadius(6.0),
				Child = new ScrollViewer { Content = text },
			};
		}
	}
}