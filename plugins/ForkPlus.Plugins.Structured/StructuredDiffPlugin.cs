using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Structured
{
	/// <summary>
	/// 结构化数据对比视图插件：认领常见配置 / 数据格式（.json / .jsonc / .yaml / .yml / .toml /
	/// .xml / .ini / .cfg / .properties），命中后由 <see cref="StructuredDiffView"/> 把两侧解析成
	/// 同一套数据模型，按键路径做语义 diff——新增 / 删除 / 改值逐行标注，而不是逐字符比文本。
	///
	/// 路由：宿主 v5.0.4 起对**文本差异**同样查询插件路由（用户绑定 &gt; 精确扩展名认领，
	/// 不走通配兜底），本插件认领的九种扩展名都是文本格式，选中文本 diff 即命中本视图；
	/// 用户绑定（偏好设置 → 扩展名绑定）可覆盖自动路由。
	/// 之所以实现键级对比，是因为 JSON / YAML / TOML 这类配置的「键级差异」用文本 diff 很难读，
	/// 本视图能直接看到「哪个键加/删/改了」。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class StructuredDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.structured";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "Structured Data Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "0.0.1";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "Structured Data Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Structured data compare view plugin: claims .json / .jsonc / .yaml / .yml / .toml / .xml / .ini / .cfg / .properties, parses both sides into one data model and compares keys semantically — added / removed / changed — as a key-path table or a structure tree.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, StructuredStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, StructuredStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与其它领域插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的配置 / 数据格式扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[9]
		{
			".json",
			".jsonc",
			".yaml",
			".yml",
			".toml",
			".xml",
			".ini",
			".cfg",
			".properties"
		};

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new StructuredDiffView();
		}
	}
}
