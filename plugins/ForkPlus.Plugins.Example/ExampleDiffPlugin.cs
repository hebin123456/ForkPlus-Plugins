using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Example
{
	/// <summary>
	/// 示例对比视图插件（最小可用形态）：声明认领 <c>.example</c> / <c>.exampletxt</c> 两个
	/// 扩展名，命中后由 <see cref="ExampleDiffView"/> 渲染一个只读的「左旧 / 右新」信息面板。
	///
	/// 写一个新插件 = 复制本目录，改掉 AssemblyName / 命名空间 / <see cref="Id"/> / 扩展名即可。
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class ExampleDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id，建议用 <c>forkplus.</c> 前缀（内置为 forkplus.image / forkplus.hex）。</summary>
		public string Id => "forkplus.example";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；本示例直接用英文原文）。</summary>
		public string DisplayNameKey => "Example";

		/// <summary>插件自身的版本号（与宿主版本解耦；0.1.0 起支持多语言）。</summary>
		public string Version => "0.1.0";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "Example Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Example plugin: claims .example / .exampletxt and renders a read-only old/new information panel — the minimal reference implementation of IDiffViewPlugin / IDiffView.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, ExampleStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, ExampleStrings.Descriptions, Description);
		}

		/// <summary>同扩展名竞争时的优先级，大者优先；通配兜底插件取最低（内置 Hex 为 0）。</summary>
		public int Priority => 100;

		/// <summary>认领的扩展名（小写含点）；<c>"*"</c> 表示通配兜底。</summary>
		public IReadOnlyList<string> FileExtensions => new string[2] { ".example", ".exampletxt" };

		/// <summary>扩展名命中后的二次判定（如校验文件头魔数）。本示例一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例（宿主负责生命周期调度与复用）。</summary>
		public IDiffView CreateView()
		{
			return new ExampleDiffView();
		}
	}
}