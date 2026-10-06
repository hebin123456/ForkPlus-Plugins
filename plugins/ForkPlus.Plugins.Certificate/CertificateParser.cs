using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ForkPlus.Plugins.Certificate
{
	/// <summary>
	/// 证书容器解析器：按扩展名选择解析路径，产出一侧的数据模型。
	///
	/// 支持范围：
	/// <list type="bullet">
	/// <item>.der / 二进制 .cer：<see cref="X509CertificateLoader.LoadCertificate(byte[])"/> 单证书，
	/// 失败再按证书集合兜底（多证书串联 / PKCS#7）。</item>
	/// <item>.p12 / .pfx：PKCS#12 证书链。为守住安全边界，加载时设置
	/// <see cref="Pkcs12LoaderLimits.IgnorePrivateKeys"/> = true 且用
	/// <see cref="X509KeyStorageFlags.EphemeralKeySet"/>——只读证书链与别名，绝不导入私钥、
	/// 不解密任何内容。</item>
	/// <item>.p7b / .p7c：<see cref="SignedCms"/> 证书袋（detached: true），只取证书、不验签。</item>
	/// <item>.crl：共享框架无内置 CRL 解析 API，降级为只读识别（大小 + 可读头部信息），不引第三方。</item>
	/// </list>
	///
	/// 解析全程 try/catch，畸形 DER 一律降级为「无法解析」，绝不把异常抛给宿主。
	/// </summary>
	internal static class CertificateParser
	{
		private const string SanOid = "2.5.29.17";

		private const string AiaOid = "1.3.6.1.5.5.7.1.1";

		private const string CrlDistributionOid = "2.5.29.31";

		/// <summary>在 DER 原始字节里识别 URI 的 scheme（CRL 分发点无内置解析 API 时使用）。</summary>
		private static readonly string[] UriSchemes = new string[6] { "https://", "http://", "ldaps://", "ldap://", "file://", "ftp://" };

		/// <summary>把一侧的原始字节解析成数据模型；任何失败都降级，不抛异常。</summary>
		internal static CertificateSideModel Parse(string path, byte[] bytes, string password)
		{
			CertificateSideModel model = new CertificateSideModel
			{
				SizeBytes = bytes?.LongLength ?? 0L
			};
			model.Container = DetectContainer(path);
			if (bytes == null || bytes.Length == 0)
			{
				model.Error = CertificateError.Malformed;
				return model;
			}
			try
			{
				CertificateError error = CertificateError.None;
				string detail = null;
				List<X509Certificate2> certificates = LoadCertificates(model.Container, bytes, password, ref error, ref detail);
				if (error != CertificateError.None)
				{
					model.Error = error;
					model.ErrorDetail = detail;
					return model;
				}
				if (model.Container == CertificateContainer.Crl)
				{
					model.ContainerHeadline = DescribeCrlHeadline(bytes);
					return model;
				}
				foreach (X509Certificate2 certificate in certificates)
				{
					try
					{
						model.Certificates.Add(Extract(certificate));
					}
					finally
					{
						certificate.Dispose();
					}
				}
				if (model.Certificates.Count == 0)
				{
					model.Error = CertificateError.NoCertificate;
				}
				return model;
			}
			catch (Exception ex)
			{
				PluginLog.Warn("Certificate: failed to parse '" + (path ?? "<none>") + "'", ex);
				model.Error = CertificateError.Malformed;
				model.ErrorDetail = ex.Message;
				return model;
			}
		}

		/// <summary>按扩展名判定容器类型（不在声明清单内的按 DER 尝试）。</summary>
		private static CertificateContainer DetectContainer(string path)
		{
			string extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
			switch (extension)
			{
				case ".p12":
				case ".pfx":
					return CertificateContainer.Pkcs12;
				case ".p7b":
				case ".p7c":
					return CertificateContainer.Pkcs7;
				case ".crl":
					return CertificateContainer.Crl;
				case ".der":
				case ".cer":
					return CertificateContainer.Der;
				default:
					return CertificateContainer.Unknown;
			}
		}

		private static List<X509Certificate2> LoadCertificates(CertificateContainer container, byte[] bytes, string password, ref CertificateError error, ref string detail)
		{
			List<X509Certificate2> certificates = new List<X509Certificate2>();
			switch (container)
			{
				case CertificateContainer.Pkcs12:
					if (bytes[0] != 0x30)
					{
						// PKCS#12 是 DER，首字节必为 SEQUENCE 标记；不是则判定为畸形而非密码问题。
						error = CertificateError.Malformed;
						break;
					}
					try
					{
						// 安全边界：IgnorePrivateKeys 让加载器跳过私钥材料；EphemeralKeySet 保证不落盘。
						Pkcs12LoaderLimits limits = new Pkcs12LoaderLimits
						{
							IgnorePrivateKeys = true
						};
						X509Certificate2Collection collection = X509CertificateLoader.LoadPkcs12Collection(bytes, password ?? string.Empty, X509KeyStorageFlags.EphemeralKeySet, limits);
						foreach (X509Certificate2 certificate in collection)
						{
							certificates.Add(certificate);
						}
					}
					catch (CryptographicException ex)
					{
						error = string.IsNullOrEmpty(password) ? CertificateError.PasswordRequired : CertificateError.PasswordIncorrect;
						detail = ex.Message;
					}
					break;
				case CertificateContainer.Pkcs7:
					certificates.AddRange(LoadPkcs7(bytes, ref error, ref detail));
					break;
				case CertificateContainer.Crl:
					// 无内置解析 API：不加载证书，交给视图降级展示。
					break;
				default:
					certificates.AddRange(LoadDer(bytes, ref error, ref detail));
					break;
			}
			return certificates;
		}

		/// <summary>DER 单证书（失败再按证书集合兜底）。</summary>
		private static List<X509Certificate2> LoadDer(byte[] bytes, ref CertificateError error, ref string detail)
		{
			List<X509Certificate2> certificates = new List<X509Certificate2>();
			try
			{
				certificates.Add(X509CertificateLoader.LoadCertificate(bytes));
				return certificates;
			}
			catch (CryptographicException)
			{
			}
			// 单个证书解析失败：按证书集合兜底（多证书串联 / PKCS#7 证书袋）。
			try
			{
#pragma warning disable SYSLIB0057 // 契约要求以证书集合导入方式兜底；此处为唯一用法。
				X509Certificate2Collection collection = new X509Certificate2Collection();
				collection.Import(bytes);
#pragma warning restore SYSLIB0057
				if (collection.Count > 0)
				{
					foreach (X509Certificate2 certificate in collection)
					{
						certificates.Add(certificate);
					}
					return certificates;
				}
			}
			catch (CryptographicException ex)
			{
				detail = ex.Message;
			}
			error = CertificateError.Malformed;
			return certificates;
		}

		/// <summary>
		/// PKCS#7 证书袋：只解析结构取证书，不调 CheckSignature、不验签、不解密内容。
		/// 注意不能用 <c>new SignedCms(new ContentInfo(bytes), detached)</c>——那个重载把入参当作
		/// 「内容值」而非完整 PKCS#7 结构，证书会取成 0 张；必须走 <see cref="SignedCms.Decode(byte[])"/>。
		/// </summary>
		private static List<X509Certificate2> LoadPkcs7(byte[] bytes, ref CertificateError error, ref string detail)
		{
			List<X509Certificate2> certificates = new List<X509Certificate2>();
			try
			{
				SignedCms cms = new SignedCms();
				cms.Decode(bytes);
				foreach (X509Certificate2 certificate in cms.Certificates)
				{
					certificates.Add(certificate);
				}
				if (certificates.Count == 0)
				{
					error = CertificateError.NoCertificate;
				}
			}
			catch (CryptographicException ex)
			{
				error = CertificateError.Malformed;
				detail = ex.Message;
			}
			return certificates;
		}

		/// <summary>CRL 降级：只做轻量识别，不猜结构（PEM 文本取首行）。</summary>
		private static string DescribeCrlHeadline(byte[] bytes)
		{
			try
			{
				int probe = Math.Min(bytes.Length, 128);
				string head = Encoding.ASCII.GetString(bytes, 0, probe);
				int index = head.IndexOf("-----BEGIN", StringComparison.Ordinal);
				if (index >= 0)
				{
					string line = head.Substring(index);
					int newline = line.IndexOfAny(new char[2] { '\r', '\n' });
					return newline > 0 ? line.Substring(0, newline).Trim() : line.Trim();
				}
			}
			catch (ArgumentException)
			{
			}
			return null;
		}

		/// <summary>把一张证书的字段抽取成对比模型；单个字段失败不影响其余字段。</summary>
		private static CertificateInfo Extract(X509Certificate2 certificate)
		{
			CertificateInfo info = new CertificateInfo
			{
				Subject = DescribeName(certificate.SubjectName, certificate.Subject),
				Issuer = DescribeName(certificate.IssuerName, certificate.Issuer),
				SerialNumber = certificate.SerialNumber,
				NotBefore = certificate.NotBefore.ToUniversalTime(),
				NotAfter = certificate.NotAfter.ToUniversalTime(),
				ThumbprintSha1 = certificate.Thumbprint,
				SignatureAlgorithm = certificate.SignatureAlgorithm?.FriendlyName,
				SignatureAlgorithmOid = certificate.SignatureAlgorithm?.Value
			};
			// 自签名判定：仅按 DN 比较（不引入信任链验签，避免副作用）。
			info.SelfSigned = string.Equals(certificate.Subject, certificate.Issuer, StringComparison.OrdinalIgnoreCase);
			DescribePublicKey(certificate, out string algorithm, out int? keySize);
			info.PublicKeyAlgorithm = algorithm;
			info.PublicKeySize = keySize;
			try
			{
				info.ThumbprintSha256 = Convert.ToHexString(SHA256.HashData(certificate.RawData));
			}
			catch (CryptographicException)
			{
				info.ThumbprintSha256 = null;
			}
			ReadExtensions(certificate, info);
			return info;
		}

		private static void ReadExtensions(X509Certificate2 certificate, CertificateInfo info)
		{
			X509ExtensionCollection extensions;
			try
			{
				extensions = certificate.Extensions;
			}
			catch (CryptographicException)
			{
				return;
			}
			foreach (X509Extension extension in extensions)
			{
				try
				{
					if (extension is X509KeyUsageExtension keyUsage)
					{
						foreach (X509KeyUsageFlags flag in Enum.GetValues<X509KeyUsageFlags>())
						{
							if (flag != X509KeyUsageFlags.None && keyUsage.KeyUsages.HasFlag(flag))
							{
								info.KeyUsages.Add(SplitPascalCase(flag.ToString()));
							}
						}
					}
					else if (extension is X509EnhancedKeyUsageExtension enhanced)
					{
						foreach (Oid oid in enhanced.EnhancedKeyUsages)
						{
							info.ExtendedKeyUsages.Add(DescribeOid(oid));
						}
					}
					else if (extension is X509BasicConstraintsExtension basic)
					{
						info.IsCa = basic.CertificateAuthority;
						if (basic.HasPathLengthConstraint)
						{
							info.PathLength = basic.PathLengthConstraint;
						}
					}
					else if (extension.Oid?.Value == SanOid)
					{
						info.Sans.AddRange(ParseSubjectAlternativeNames(extension.RawData));
					}
					else if (extension.Oid?.Value == AiaOid)
					{
						info.OcspUrls.AddRange(ParseOcspUrls(extension));
					}
					else if (extension.Oid?.Value == CrlDistributionOid)
					{
						info.CrlDistributionPoints.AddRange(ScanUris(extension.RawData));
					}
				}
				catch (CryptographicException)
				{
				}
				catch (AsnContentException)
				{
				}
			}
		}

		/// <summary>公钥算法与长度：优先用强类型取 KeySize，拿不到退回 OID 名。</summary>
		private static void DescribePublicKey(X509Certificate2 certificate, out string algorithm, out int? keySize)
		{
			keySize = null;
			try
			{
				using RSA rsa = certificate.GetRSAPublicKey();
				if (rsa != null)
				{
					algorithm = "RSA";
					keySize = rsa.KeySize;
					return;
				}
			}
			catch (CryptographicException)
			{
			}
			try
			{
				using ECDsa ecdsa = certificate.GetECDsaPublicKey();
				if (ecdsa != null)
				{
					algorithm = "ECDSA";
					keySize = ecdsa.KeySize;
					return;
				}
			}
			catch (CryptographicException)
			{
			}
			try
			{
				using ECDiffieHellman ecdh = certificate.GetECDiffieHellmanPublicKey();
				if (ecdh != null)
				{
					algorithm = "ECDH";
					keySize = ecdh.KeySize;
					return;
				}
			}
			catch (CryptographicException)
			{
			}
			try
			{
				using DSA dsa = certificate.GetDSAPublicKey();
				if (dsa != null)
				{
					algorithm = "DSA";
					keySize = dsa.KeySize;
					return;
				}
			}
			catch (CryptographicException)
			{
			}
			Oid oid = certificate.PublicKey?.Oid;
			algorithm = oid?.FriendlyName ?? oid?.Value;
		}

		/// <summary>手动解析 SAN 的通用名（DNS / IP / URI / Email / 其它）——共享框架只提供 DNS 与 IP 的枚举器。</summary>
		private static List<SanEntry> ParseSubjectAlternativeNames(byte[] rawData)
		{
			List<SanEntry> entries = new List<SanEntry>();
			try
			{
				AsnReader sequence = new AsnReader(rawData, AsnEncodingRules.DER).ReadSequence();
				while (sequence.HasData)
				{
					Asn1Tag tag = sequence.PeekTag();
					if (tag.TagClass != TagClass.ContextSpecific)
					{
						sequence.ReadEncodedValue();
						continue;
					}
					if (tag.IsConstructed)
					{
						ReadOnlyMemory<byte> encoded = sequence.ReadEncodedValue();
						entries.Add(new SanEntry(SanKind.Other, "#" + Convert.ToHexString(encoded.Span)));
						continue;
					}
					switch (tag.TagValue)
					{
						case 1:
							entries.Add(new SanEntry(SanKind.Email, sequence.ReadCharacterString(UniversalTagNumber.IA5String, tag)));
							break;
						case 2:
							entries.Add(new SanEntry(SanKind.Dns, sequence.ReadCharacterString(UniversalTagNumber.IA5String, tag)));
							break;
						case 6:
							entries.Add(new SanEntry(SanKind.Uri, sequence.ReadCharacterString(UniversalTagNumber.IA5String, tag)));
							break;
						case 7:
							entries.Add(new SanEntry(SanKind.Ip, FormatIpAddress(sequence.ReadOctetString(tag))));
							break;
						default:
							ReadOnlyMemory<byte> other = sequence.ReadEncodedValue();
							entries.Add(new SanEntry(SanKind.Other, "#" + Convert.ToHexString(other.Span)));
							break;
					}
				}
			}
			catch (AsnContentException)
			{
			}
			return entries;
		}

		/// <summary>OCSP 分发点：优先用内置的 AIA 扩展枚举器。</summary>
		private static List<string> ParseOcspUrls(X509Extension extension)
		{
			List<string> urls = new List<string>();
			try
			{
				X509AuthorityInformationAccessExtension aia = extension as X509AuthorityInformationAccessExtension
					?? new X509AuthorityInformationAccessExtension(extension.RawData, extension.Critical);
				foreach (object uri in aia.EnumerateOcspUris())
				{
					string text = Convert.ToString(uri, CultureInfo.InvariantCulture);
					if (!string.IsNullOrEmpty(text))
					{
						urls.Add(text);
					}
				}
			}
			catch (CryptographicException)
			{
			}
			return urls;
		}

		/// <summary>CRLDistributionPoints 无内置解析 API：在 DER 原始字节里扫描 URI，取不到即留空。</summary>
		private static List<string> ScanUris(byte[] rawData)
		{
			List<string> urls = new List<string>();
			StringBuilder current = new StringBuilder();
			foreach (byte value in rawData)
			{
				if (value >= 0x21 && value <= 0x7E)
				{
					current.Append((char)value);
				}
				else
				{
					AppendUri(urls, current);
					current.Clear();
				}
			}
			AppendUri(urls, current);
			return urls;
		}

		private static void AppendUri(List<string> urls, StringBuilder builder)
		{
			if (builder.Length == 0)
			{
				return;
			}
			string text = builder.ToString();
			foreach (string scheme in UriSchemes)
			{
				int index = text.IndexOf(scheme, StringComparison.OrdinalIgnoreCase);
				if (index >= 0)
				{
					string uri = text.Substring(index);
					if (!urls.Contains(uri))
					{
						urls.Add(uri);
					}
					return;
				}
			}
		}

		private static string FormatIpAddress(byte[] bytes)
		{
			if (bytes.Length == 4 || bytes.Length == 16)
			{
				try
				{
					return new IPAddress(bytes).ToString();
				}
				catch (ArgumentException)
				{
				}
			}
			return Convert.ToHexString(bytes);
		}

		private static string DescribeName(X500DistinguishedName name, string fallback)
		{
			if (name != null)
			{
				try
				{
					return name.Format(false);
				}
				catch (CryptographicException)
				{
				}
			}
			return fallback;
		}

		private static string DescribeOid(Oid oid)
		{
			if (oid == null)
			{
				return null;
			}
			return string.IsNullOrEmpty(oid.FriendlyName) ? oid.Value : oid.FriendlyName + " (" + oid.Value + ")";
		}

		/// <summary>把枚举名拆成可读词（DigitalSignature → Digital Signature）。</summary>
		private static string SplitPascalCase(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return text;
			}
			StringBuilder builder = new StringBuilder(text.Length + 8);
			for (int i = 0; i < text.Length; i++)
			{
				char c = text[i];
				if (i > 0 && char.IsUpper(c) && !char.IsUpper(text[i - 1]))
				{
					builder.Append(' ');
				}
				builder.Append(c);
			}
			return builder.ToString();
		}
	}
}
