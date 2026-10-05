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
		private readonly TextBlock _srcText;
		private readonly TextBlock _dstText;

		public ExampleDiffView()
		{
			_srcText = NewSideText();
			_dstText = NewSideText();
			_root = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("*,*"),
				RowDefinitions = new RowDefinitions("Auto,*"),
			};

			// 顶部提示行（经宿主能力桥取当前语言译文；宿主未接线时原样返回 key）
			TextBlock header = new TextBlock
			{
				Margin = new Thickness(12.0, 8.0),
				FontWeight = FontWeight.SemiBold,
				Text = PluginEnvironment.Translate("Example Plugin"),
			};
			Grid.SetRow(header, 0);
			Grid.SetColumnSpan(header, 2);
			_root.Children.Add(header);

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
			_srcText.Text = Describe(context?.Src, "old");
			_dstText.Text = Describe(context?.Dst, "new");
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

		/// <summary>应用当前语言（宿主在语言切换时广播）。</summary>
		public void ApplyLocalization()
		{
			PluginEnvironment.ApplyLocalization(_root);
		}

		/// <summary>释放：停后台任务、退订事件、释放位图；之后实例不再复用。</summary>
		public void Release()
		{
			_srcText.Text = string.Empty;
			_dstText.Text = string.Empty;
		}

		// ---- 内容装配 ----

		private static string Describe(DiffSideContent side, string role)
		{
			if (side == null)
			{
				// 一侧不存在：新增文件（只有右侧）/ 删除文件（只有左侧）
				return role + Environment.NewLine + Environment.NewLine + PluginEnvironment.Translate("(not present)");
			}
			List<string> lines = new List<string>
			{
				role,
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
				lines.Add("Loaded bytes: " + data.Length);
			}
			if (side.Lfs != null)
			{
				lines.Add("LFS: " + side.Lfs.Sha256);
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