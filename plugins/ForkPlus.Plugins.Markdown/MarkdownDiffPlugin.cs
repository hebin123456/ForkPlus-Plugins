using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Markdown
{
	/// <summary>
	/// Markdown 对比视图插件：认领 .md / .markdown，命中后由 <see cref="MarkdownDiffView"/>
	/// 提供三模式对比——「渲染」（Markdig 把两侧 Markdown 渲成样式化 HTML，交给宿主共享组件
	/// <c>ForkPlus.Plugins.Ui.HtmlPreview.SideBySideHtmlPreview</c> 并排双 WebView 预览）、
	/// 「大纲」（标题层级四色语义 diff，一眼看出章节增删与升降级）、「源码」（并排原始文本）。
	///
	/// 路由：.md / .markdown 是**文本**文件，git 默认判为文本、走内置文本编辑器；宿主 5.0.5 起
	/// 文本差异也会查询插件路由（<c>DiffViewPluginRegistry.ResolveClaimed</c>：用户绑定 &gt;
	/// 精确扩展名认领，不含 "*" 通配兜底），本插件以精确扩展名显式认领，因此**默认即自动接管**；
	/// 用户在「偏好设置 → 扩展名绑定」里的绑定优先于自动路由。
	///
	/// 已知限制（如实标注）：插件只拿到仓库相对路径，拿不到工作区绝对路径，因此文档里以相对路径
	/// 引用的**外部**图片无法解析；「渲染」模式的并排预览无跨侧滚动同步（共享组件未提供脚本桥）。
	/// WebView 不可用（headless、缺 WebView2 运行时 / WPE WebKit / WebKitGTK）时「渲染」模式
	/// 仅给提示，可切「大纲」/「源码」模式查看。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class MarkdownDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.markdown";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "Markdown Preview";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "1.0.0";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "Markdown Preview";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Markdown compare view plugin: claims .md / .markdown, renders each side to styled HTML (Markdig) in a side-by-side WebView preview, compares the heading outline with four-state coloring, and shows the raw source side by side.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, MarkdownStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, MarkdownStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与其它领域插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[2] { ".md", ".markdown" };

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new MarkdownDiffView();
		}
	}
}
