using System;
using System.Collections.Generic;

namespace ForkPlus.Plugins.Certificate
{
	/// <summary>证书容器的类型（决定解析路径与界面标注）。</summary>
	internal enum CertificateContainer
	{
		/// <summary>扩展名未识别，按 DER 单证书尝试。</summary>
		Unknown,

		/// <summary>DER 编码的单张证书（.der / 二进制 .cer）。</summary>
		Der,

		/// <summary>PKCS#12 容器（.p12 / .pfx），通常为证书链 + 私钥。</summary>
		Pkcs12,

		/// <summary>PKCS#7 证书袋（.p7b / .p7c），只取证书不验签。</summary>
		Pkcs7,

		/// <summary>CRL 吊销列表（.crl），共享框架无解析 API，降级为只读头部信息。</summary>
		Crl
	}

	/// <summary>一侧解析失败时的错误分类（用于给出对应文案与密码提示）。</summary>
	internal enum CertificateError
	{
		None,

		/// <summary>容器类型无法解析 / 不含可识别的证书。</summary>
		Unsupported,

		/// <summary>PKCS#12 需要密码但未提供。</summary>
		PasswordRequired,

		/// <summary>PKCS#12 密码不正确。</summary>
		PasswordIncorrect,

		/// <summary>DER 畸形 / 结构损坏。</summary>
		Malformed,

		/// <summary>容器可解析但不含任何证书。</summary>
		NoCertificate
	}

	/// <summary>有效期相对当前时间的状态。</summary>
	internal enum CertificateValidity
	{
		/// <summary>有效。</summary>
		Valid,

		/// <summary>即将过期（距 NotAfter ≤ 30 天）。</summary>
		ExpiringSoon,

		/// <summary>已过期。</summary>
		Expired
	}

	/// <summary>SAN 条目的类型（决定徽章底色）。</summary>
	internal enum SanKind
	{
		Dns,
		Ip,
		Uri,
		Email,
		Other
	}

	/// <summary>一条 SAN 记录。</summary>
	internal sealed class SanEntry
	{
		public SanKind Kind { get; }

		public string Value { get; }

		public SanEntry(SanKind kind, string value)
		{
			Kind = kind;
			Value = value;
		}
	}

	/// <summary>一张证书的对比字段（一次解析结果里的一张证书）。</summary>
	internal sealed class CertificateInfo
	{
		public string Subject { get; set; }

		public string Issuer { get; set; }

		public string SerialNumber { get; set; }

		/// <summary>是否自签名（判定方式：Subject DN 与 Issuer DN 相同，不做信任链验签）。</summary>
		public bool SelfSigned { get; set; }

		public DateTime NotBefore { get; set; }

		public DateTime NotAfter { get; set; }

		public string PublicKeyAlgorithm { get; set; }

		public int? PublicKeySize { get; set; }

		public string SignatureAlgorithm { get; set; }

		public string SignatureAlgorithmOid { get; set; }

		public List<string> KeyUsages { get; } = new List<string>();

		public List<string> ExtendedKeyUsages { get; } = new List<string>();

		public bool? IsCa { get; set; }

		public int? PathLength { get; set; }

		public List<SanEntry> Sans { get; } = new List<SanEntry>();

		public List<string> CrlDistributionPoints { get; } = new List<string>();

		public List<string> OcspUrls { get; } = new List<string>();

		public string ThumbprintSha1 { get; set; }

		public string ThumbprintSha256 { get; set; }

		/// <summary>相对当前时间判定有效期状态。</summary>
		public CertificateValidity GetValidity(DateTime utcNow)
		{
			if (utcNow > NotAfter)
			{
				return CertificateValidity.Expired;
			}
			if ((NotAfter - utcNow).TotalDays <= 30.0)
			{
				return CertificateValidity.ExpiringSoon;
			}
			return CertificateValidity.Valid;
		}
	}

	/// <summary>一侧的解析结果：容器类型 + 证书列表 + 错误分类。</summary>
	internal sealed class CertificateSideModel
	{
		public CertificateContainer Container { get; set; }

		public CertificateError Error { get; set; }

		/// <summary>失败细节（原始异常消息，仅作附加说明）。</summary>
		public string ErrorDetail { get; set; }

		/// <summary>容器字节大小。</summary>
		public long SizeBytes { get; set; }

		/// <summary>CRL 降级展示时的可读头部信息（PEM 首行等，取不到为 null）。</summary>
		public string ContainerHeadline { get; set; }

		public List<CertificateInfo> Certificates { get; } = new List<CertificateInfo>();
	}
}
