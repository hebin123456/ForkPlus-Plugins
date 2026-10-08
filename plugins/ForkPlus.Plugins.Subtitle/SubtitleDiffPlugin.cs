using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Subtitle
{
	/// <summary>
	/// 字幕 / 时间轴对比视图插件：认领 .srt / .vtt / .ass / .ssa / .sub，命中后由
	/// <see cref="SubtitleDiffView"/> 把两侧解析成同一套 cue 列表，先按文本对齐、再按时间配对，
	/// 得到「相同 / 已变 / 仅左 / 仅右」四类，并有字幕行表与时间轴两种视图。
	///
	/// 路由：宿主 v5.0.4 起对**文本差异**同样查询插件路由（用户绑定 &gt; 精确扩展名认领，
	/// 不走通配兜底），字幕文件都是文本，选中文本 diff 即命中本视图；
	/// 用户绑定（偏好设置 → 扩展名绑定）可覆盖自动路由。
	/// 之所以实现，是因为字幕改的是「某句话在某几秒」——逐行文本 diff 很难看出时间轴的挪动。
	///
	/// 零第三方依赖：五种格式的解析与对齐全在插件内自实现（详见 <see cref="SubtitleParser"/>、
	/// <see cref="SubtitleDiff"/>），随包进 <c>plugins/</c> 的只有插件自身主 DLL。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class SubtitleDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.subtitle";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "Subtitle Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "0.0.1";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "Subtitle Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Subtitle / timeline compare view plugin: claims .srt / .vtt / .ass / .ssa / .sub, parses both sides into one cue model, aligns by text then by time, and marks each cue as same / changed / left only / right only — as a cue table or a proportional timeline.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, SubtitleStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, SubtitleStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与其它领域插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的字幕扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[5]
		{
			".srt",
			".vtt",
			".ass",
			".ssa",
			".sub"
		};

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new SubtitleDiffView();
		}
	}
}
