using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Midi
{
	/// <summary>
	/// MIDI 对比视图插件：认领 .mid / .midi，命中后由 <see cref="MidiDiffView"/> 把两侧 SMF
	/// 解析成同一套音符模型，先按（通道, 音高, 起始毫秒）配对、再比较力度与时长，
	/// 得到「相同 / 已变 / 仅左 / 仅右」四类，并有音符表与钢琴卷帘时间轴两种视图。
	///
	/// 路由：.mid / .midi 是二进制，宿主只对**二进制差异**查询插件路由（用户绑定 &gt; 精确扩展名
	/// 认领，不走通配兜底），MIDI 文件 git 一律判为二进制，选中的即必命中本视图；
	/// 用户绑定（偏好设置 → 扩展名绑定）可覆盖自动路由。
	/// 之所以实现，是因为 MIDI 改的是「哪个音在哪个时刻」——字节级 diff 看不出音符的挪动与增删。
	///
	/// 零第三方依赖：SMF 解析与配对全部在插件内自实现（详见 <see cref="MidiParser"/>、
	/// <see cref="MidiDiff"/>），随包进 <c>plugins/</c> 的只有插件自身主 DLL。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class MidiDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.midi";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "MIDI Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "0.0.1";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "MIDI Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "MIDI compare view plugin: claims .mid / .midi, parses both sides into one note model, pairs notes by channel, pitch and start time, and marks each note as same / changed / left only / right only — as a note table or a piano-roll timeline.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, MidiStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, MidiStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与其它领域插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的 MIDI 扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[2]
		{
			".mid",
			".midi"
		};

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new MidiDiffView();
		}
	}
}
