using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Pcap
{
	/// <summary>
	/// 网络抓包对比视图插件：认领 .pcap / .pcapng，命中后由 <see cref="PcapDiffView"/> 把两侧
	/// 抓包各自聚合出统计（包数 / 字节数 / 时长 / 协议分布 / 会话 Top）与包列表，再做语义 diff，
	/// 得到「相同 / 已变 / 仅左 / 仅右」四类，并有统计表与包列表两种视图。
	///
	/// 路由：宿主只对**二进制差异**查询插件路由，抓包文件是二进制、git 一律判为二进制，
	/// 因此自动路由必命中本插件而非 Hex 兜底；用户绑定（偏好设置 → 扩展名绑定）可覆盖自动路由。
	/// 之所以实现，是因为两次抓包的差异在「统计口径与包序列」上——逐字节 Hex diff 读不出来。
	///
	/// 零第三方依赖：pcap / pcapng 的解析与聚合全在插件内自实现（详见 <see cref="PcapParser"/>、
	/// <see cref="PcapDiff"/>），随包进 <c>plugins/</c> 的只有插件自身主 DLL。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class PcapDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.pcap";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "Packet Capture Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "0.0.1";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "Packet Capture Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Packet capture compare view plugin: claims .pcap / .pcapng, aggregates each side into capture statistics (packets / bytes / duration / protocol distribution / top conversations) and a packet list, then diffs them semantically.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, PcapStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, PcapStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与其它领域插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的抓包扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[2]
		{
			".pcap",
			".pcapng"
		};

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new PcapDiffView();
		}
	}
}
