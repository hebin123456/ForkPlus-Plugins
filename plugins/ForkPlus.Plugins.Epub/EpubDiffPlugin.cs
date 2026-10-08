using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Epub
{
	/// <summary>
	/// EPUB 电子书对比视图插件：认领 .epub，命中后由 <see cref="EpubDiffView"/> 把两侧
	/// ZIP 容器解开（共享框架内置的 System.IO.Compression.ZipArchive），解析
	/// container.xml → OPF（元数据 / manifest / spine），提供「元数据」与「章节」两种 diff 视图。
	///
	/// 路由：EPUB 是 ZIP 二进制，git 判为二进制，宿主只对**二进制差异**查询插件路由
	/// （用户绑定 &gt; 精确扩展名认领，不走通配兜底）——.epub 必命中本视图；
	/// 用户绑定（偏好设置 → 扩展名绑定）可覆盖自动路由。
	/// 之所以实现，是因为 EPUB 改的常是「OPF 里几行元数据」或「spine 里挪了 / 换了某章」——
	/// 整包二进制 diff 什么都看不出，解开容器对比结构才有意义。
	///
	/// 零第三方依赖：ZIP 走 System.IO.Compression（共享框架内置），XML 走 System.Xml.Linq
	/// （同内置），随包进 <c>plugins/</c> 的只有插件自身主 DLL（不需要 third-party.json）。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class EpubDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.epub";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "EPUB Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "0.0.1";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "EPUB Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "EPUB e-book compare view plugin: claims .epub, unpacks the ZIP container (System.IO.Compression), parses container.xml and the OPF (metadata / manifest / spine), and shows a metadata table and a chapter-by-chapter diff.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, EpubStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, EpubStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与其它领域插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[1] { ".epub" };

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new EpubDiffView();
		}
	}
}
