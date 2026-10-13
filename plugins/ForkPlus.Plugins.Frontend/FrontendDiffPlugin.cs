using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Frontend
{
	/// <summary>
	/// 前端代码对比视图插件：认领 .html / .htm / .vue，命中后由 <see cref="FrontendDiffView"/>
	/// 把两侧源码组装成**自包含 HTML**——.html/.htm 原样（仅归一 charset），.vue 用随包内嵌的
	/// Vue 3 全量运行时做运行期模板编译——交给宿主共享组件
	/// <c>ForkPlus.Plugins.Ui.HtmlPreview.SideBySideHtmlPreview</c> 并排双 WebView 真渲染；
	/// 另提供「源码」模式并排看原始代码。
	///
	/// 路由：.html/.vue 是**文本**文件，git 默认判为文本、走内置文本编辑器；宿主 5.0.5 起
	/// 文本差异也会查询插件路由（<c>DiffViewPluginRegistry.ResolveClaimed</c>：用户绑定 &gt;
	/// 精确扩展名认领，不含 "*" 通配兜底），本插件以精确扩展名显式认领，因此**默认即自动接管**；
	/// 用户在「偏好设置 → 扩展名绑定」里的绑定优先于自动路由。
	///
	/// 已知限制（如实标注）：插件只拿到仓库相对路径，拿不到工作区绝对路径，因此页面里
	/// 以相对路径引用的**外部** css / js / 图片无法解析——只有自包含（样式 / 脚本内联）的页面
	/// 能完整渲染。WebView 不可用（headless、缺 WebView2 运行时 / WPE WebKit / WebKitGTK）时
	/// 「渲染」模式仅给提示，可切「源码」模式查看代码。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class FrontendDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.frontend";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "Frontend Preview";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "1.0.0";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "Frontend Preview";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Frontend compare view plugin: claims .html / .htm / .vue, assembles each side into a self-contained HTML document (Vue single-file components are compiled at runtime by the bundled Vue 3 build) and renders it in a side-by-side WebView preview; a source mode shows the raw code side by side.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, FrontendStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, FrontendStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与其它领域插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[3] { ".html", ".htm", ".vue" };

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new FrontendDiffView();
		}
	}
}