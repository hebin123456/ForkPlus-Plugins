using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Psd
{
	/// <summary>
	/// PSD 图层对比视图插件：认领 .psd / .psb，命中后由 <see cref="PsdDiffView"/> 把两侧各自解析成
	/// 头部字段 + 图层结构模型，逐行按「相同 / 已变 / 仅左 / 仅右」四色标注，并提供内嵌缩略图并排预览。
	///
	/// 路由：宿主只对**二进制**差异查询插件路由，PSD / PSB 一律含非文本字节、git 判为二进制，
	/// 因此自动路由必命中本插件；用户绑定（偏好设置 → 扩展名绑定）可覆盖自动路由。
	/// 之所以实现，是因为 PSD 的差异往往在「某个图层挪了 / 改了混合模式 / 隐藏了 / 多了一层」，
	/// Hex 视图完全读不出来。
	///
	/// 零第三方依赖：PSD / PSB 的节段与图层记录全部自解析（见 <see cref="PsdParser"/>）；
	/// 缩略图解码用宿主共享的 SkiaSharp（不随包分发），随包进 <c>plugins/</c> 的只有插件自身主 DLL。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class PsdDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.psd";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "PSD Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "0.0.1";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "PSD Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Photoshop file compare view plugin: claims .psd / .psb, parses header fields and layer records (name, bounds, blend mode, opacity, visibility, channels) into one model, marks each entry as same / changed / left only / right only, and shows embedded thumbnails side by side.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, PsdStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, PsdStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与其它领域插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[2] { ".psd", ".psb" };

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new PsdDiffView();
		}
	}
}
