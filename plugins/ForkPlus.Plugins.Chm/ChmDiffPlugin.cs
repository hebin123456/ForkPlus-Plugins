using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Chm
{
	/// <summary>
	/// CHM（已编译 HTML 帮助）对比视图插件：认领 .chm，命中后由 <see cref="ChmDiffView"/>
	/// 解析 ITSF / ITSP 头部与 PMGL 目录块，提取元数据（标题 / 默认主题 / 语言 / 格式版本等）
	/// 与全部目录条目（<c>::DataSpace/*</c> 元数据对象 + 文档 / 图片等），
	/// 提供「元数据」与「目录条目」两种 diff 视图；内容流若为 MSCompressed 存储，
	/// 用内置的纯托管 LZX 解压器（<see cref="LzxDecoder"/>）解开后为条目算内容指纹，供内容级比对。
	///
	/// 路由：CHM 头部含 NUL，git 判为二进制，宿主只对**二进制差异**查询插件路由
	/// （用户绑定 &gt; 精确扩展名认领，不走通配兜底）——.chm 必命中本视图；
	/// 用户绑定（偏好设置 → 扩展名绑定）可覆盖自动路由。
	///
	/// 零第三方依赖：头部解析 / 目录遍历 / LZX 解压 / 内容指纹均为纯托管实现
	/// （System.* + Avalonia），随包进 <c>plugins/</c> 的只有插件自身主 DLL。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class ChmDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.chm";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "CHM Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "1.0.0";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "CHM Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "CHM (compiled HTML Help) compare view plugin: claims .chm, parses the ITSF/ITSP headers and PMGL directory blocks, and shows a metadata table (title / default topic / language / format version / counts) plus an entry-by-entry diff; MSCompressed content is unpacked with a built-in managed LZX decoder so entries can be compared by content fingerprint.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, ChmStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, ChmStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与其它领域插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[1] { ".chm" };

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new ChmDiffView();
		}
	}
}