using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Eml
{
	/// <summary>
	/// 邮件（.eml）对比视图插件：认领 .eml，命中后由 <see cref="EmlDiffView"/> 纯托管自解析
	/// RFC 5322 邮件头与 MIME 多部件树，把两侧邮件的「邮件头」「MIME 部件」「正文」分别对比。
	///
	/// 路由：EML 通常是纯文本（除非带附件 / 二进制编码），git 可能判为文本差异。为避免被内置文本
	/// 编辑器接管、看成一堆 base64，demo 里的 .eml 带附件与 base64 编码正文，确保 git 判为二进制；
	/// 用户绑定（偏好设置 → 扩展名绑定）可覆盖自动路由。
	/// 之所以实现，是因为邮件改的常是「Subject 变了」「多 / 少一个附件」「正文改了几句」——
	/// 整封二进制 / 文本 diff 都读不出结构，解析成头 / 部件 / 正文才有意义。
	///
	/// 零第三方依赖：全用共享框架内置类型，随包进 <c>plugins/</c> 的只有插件自身主 DLL。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class EmlDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.eml";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "EML Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "0.0.1";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "EML Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Email (.eml) compare view plugin: claims .eml, parses RFC 5322 headers and the MIME multipart tree in pure managed code (boundary split, base64 / quoted-printable decoding, RFC 2047 encoded-word decoding), and compares mail headers, MIME parts and the text body side by side.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, EmlStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, EmlStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与其它领域插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[1] { ".eml" };

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new EmlDiffView();
		}
	}
}