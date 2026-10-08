using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Torrent
{
	/// <summary>
	/// Torrent 种子文件对比视图插件：认领 .torrent，命中后由 <see cref="TorrentDiffView"/> 把两侧
	/// bencode 各自解码拍平成「键路径 → 值」列表，取并集逐行标注「相同 / 已变 / 仅左 / 仅右」，
	/// 并附 info 摘要（名称 / 分片数）。
	///
	/// 路由：宿主只对**二进制**差异查询插件路由，.torrent 是 bencode 二进制、git 一律判为二进制，
	/// 因此必然命中本插件而非 Hex 兜底；用户绑定（偏好设置 → 扩展名绑定）可覆盖自动路由。
	///
	/// 零第三方依赖：bencode 解码、拍平与 diff 全在插件内自实现（详见 <see cref="TorrentParser"/>、
	/// <see cref="TorrentDiff"/>），随包进 <c>plugins/</c> 的只有插件自身主 DLL。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class TorrentDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.torrent";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "Torrent Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "0.0.1";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "Torrent Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Torrent file compare view plugin: claims .torrent, decodes both sides from bencode into a key-path table of tracker / info / piece metadata, and marks each entry as same / changed / left only / right only.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, TorrentStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, TorrentStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与其它领域插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的种子文件扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[1] { ".torrent" };

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new TorrentDiffView();
		}
	}
}
