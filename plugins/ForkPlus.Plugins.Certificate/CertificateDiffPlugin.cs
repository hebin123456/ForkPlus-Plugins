using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Certificate
{
	/// <summary>
	/// 证书对比视图插件：认领证书容器（.der / 二进制 .cer / .p12 / .pfx / .p7b / .p7c / .crl），
	/// 命中后由 <see cref="CertificateDiffView"/> 把两侧证书的身份、有效期、密钥、用途、扩展、
	/// 指纹与证书链并排对比。
	///
	/// 路由限制：<c>.pem</c> / 一般 <c>.crt</c> 是 PEM 文本，而宿主只对**二进制差异**查询插件路由
	/// （文本差异由内置文本编辑器固定渲染），故扩展名里故意不列，避免「声明了但从不生效」的假象。
	///
	/// 安全边界：不导入 / 不导出私钥，不解密任何内容。加密的 .p12 / .pfx 即使输入了密码，
	/// 也只读取证书链与别名。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class CertificateDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.certificate";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "Certificate Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "0.0.1";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "Certificate Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Certificate compare view plugin: claims certificate containers (DER certificate / PKCS#12 chain / PKCS#7 bag / CRL) and lays out both sides' identity, validity, key, usage, extensions, fingerprints and chain for side-by-side comparison; the password box only unlocks encrypted PKCS#12 containers to read the certificate chain, and private keys are never imported or exported.";

		/// <summary>按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, CertificateStrings.DisplayNames, DisplayName);
		}

		/// <summary>按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, CertificateStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与示例 / PDF / Office / Archive 插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的扩展名（小写含点）。故意不含 .pem / .crt——它们是 PEM 文本，走不到二进制路由。</summary>
		public IReadOnlyList<string> FileExtensions => new string[7]
		{
			".der",
			".cer",
			".p12",
			".pfx",
			".p7b",
			".p7c",
			".crl"
		};

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new CertificateDiffView();
		}
	}
}
