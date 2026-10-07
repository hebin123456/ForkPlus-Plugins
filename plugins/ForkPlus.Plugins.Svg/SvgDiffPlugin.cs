using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Svg
{
	/// <summary>
	/// SVG 矢量图对比视图插件：认领 .svg，命中后由 <see cref="SvgDiffView"/> 把两侧各自解析成
	/// 元素树，提供「并排渲染」（按 viewBox 等比缩放直接比图形）与「元素结构 diff」
	/// （元素路径 + 属性逐项标出新增 / 删除 / 改值）两种视图。
	///
	/// 路由限制：宿主只对**二进制差异**查询插件路由，纯文本差异固定由内置文本编辑器渲染。
	/// SVG 是 XML 文本，因此**自动路由通常不会命中**；要使用本视图，需在宿主
	/// 「偏好设置 → 扩展名绑定」里把 .svg 绑定到本插件（用户绑定优先级最高）。
	/// 之所以仍然实现，是因为 SVG 的差异往往在「某个图形挪了 / 改了颜色 / 多了一笔」，
	/// 逐字符的文本 diff 几乎读不出来，而并排渲染 + 结构 diff 一眼可见。
	///
	/// 零第三方依赖：解析走 BCL 的 System.Xml.Linq，渲染走 iOS / 桌面共用的 Avalonia 图形原语
	/// （Rectangle / Ellipse / Line / Polyline / Polygon / Path），随包进 <c>plugins/</c> 的只有插件自身主 DLL。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class SvgDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.svg";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "SVG Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "0.0.1";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "SVG Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "SVG compare view plugin: claims .svg, parses both sides into an element tree, and shows them side by side rendered to scale plus a structural diff of every element and presentation attribute — added / removed / changed.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, SvgStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, SvgStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与其它领域插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的矢量图扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[1]
		{
			".svg"
		};

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new SvgDiffView();
		}
	}
}
