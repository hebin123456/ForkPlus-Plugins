using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Font
{
	/// <summary>
	/// 字体对比视图插件：认领主流字体容器（.ttf / .otf / .ttc / .otc / .woff），命中后由
	/// <see cref="FontDiffView"/> 用 SkiaSharp 并排渲染固定样张（多字号），并对 sfnt 表做结构化
	/// diff——元数据（head / name / OS/2 / maxp / hhea）与 cmap 码位覆盖。.woff2 一期不支持
	/// （Brotli 解压后还有 glyf / loca 变换需重建）。
	///
	/// 注意：宿主对二进制与文本差异均查询插件路由（v5.0.4 起含文本）。字体一律为二进制，
	/// 因此会命中本插件，而非 Hex 兜底。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class FontDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.font";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "Font Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "0.0.1";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "Font Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Font compare view plugin: claims .ttf / .otf / .ttc / .otc / .woff, renders specimen samples side by side with SkiaSharp, and structurally diffs sfnt tables (head / name / OS/2 / maxp / hhea) plus cmap codepoint coverage. WOFF2 is not supported.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, FontStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, FontStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与 PDF / Office / Archive 插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的扩展名（小写含点）。.woff2 一期不做，故不认领。</summary>
		public IReadOnlyList<string> FileExtensions => new string[5]
		{
			".ttf",
			".otf",
			".ttc",
			".otc",
			".woff"
		};

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new FontDiffView();
		}
	}
}
