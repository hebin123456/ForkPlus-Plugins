using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Certificate
{
	/// <summary>
	/// 证书插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 组织方式与 <c>ForkPlus.Plugins.Example/Localization/ExampleStrings.cs</c> 一致。
	/// </summary>
	internal static class CertificateStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="CertificateDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "证书对比" },
			{ "zh-Hant", "憑證對比" },
			{ "ja-JP", "証明書の比較" },
			{ "ko-KR", "인증서 비교" },
			{ "fr-FR", "Comparaison de certificats" },
			{ "de-DE", "Zertifikatvergleich" },
			{ "es-ES", "Comparación de certificados" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="CertificateDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "证书对比视图插件：认领证书容器（DER 单证书 / PKCS#12 链 / PKCS#7 证书袋 / CRL），把两侧证书的身份、有效期、密钥、用途、扩展、指纹与证书链并排对比；密码框仅用于读取加密 PKCS#12 的证书链，绝不导入或导出私钥。" },
			{ "zh-Hant", "憑證對比檢視外掛：認領憑證容器（DER 單憑證 / PKCS#12 鏈 / PKCS#7 憑證袋 / CRL），將兩側憑證的身分、有效期、金鑰、用途、擴充功能、指紋與憑證鏈並排對比；密碼欄僅用於讀取加密 PKCS#12 的憑證鏈，絕不匯入或匯出私密金鑰。" },
			{ "ja-JP", "証明書比較ビュープラグイン: 証明書コンテナ（DER 単一証明書 / PKCS#12 チェーン / PKCS#7 証明書バッグ / CRL）を対象に、両側の証明書の識別情報・有効期間・鍵・用途・拡張機能・フィンガープリント・チェーンを並べて比較します。パスワード欄は暗号化された PKCS#12 の証明書チェーンを読むためだけに使い、秘密鍵のインポートやエクスポートは行いません。" },
			{ "ko-KR", "인증서 비교 보기 플러그인: 인증서 컨테이너(DER 단일 인증서 / PKCS#12 체인 / PKCS#7 인증서 모음 / CRL)를 처리하여 양쪽 인증서의 주체, 유효 기간, 키, 용도, 확장, 지문, 체인을 나란히 비교합니다. 비밀번호 필드는 암호화된 PKCS#12의 인증서 체인을 읽기 위한 용도로만 사용되며, 개인 키를 가져오거나 내보내지 않습니다." },
			{ "fr-FR", "Plugin de vue de comparaison de certificats : prend en charge les conteneurs de certificats (certificat DER unique / chaîne PKCS#12 / lot PKCS#7 / CRL) et compare côte à côte l'identité, la validité, la clé, l'usage, les extensions, les empreintes et la chaîne des deux côtés ; le champ mot de passe sert uniquement à lire la chaîne d'un PKCS#12 chiffré et n'importe ni n'exporte jamais de clé privée." },
			{ "de-DE", "Zertifikatvergleichs-Ansichts-Plugin: übernimmt Zertifikatcontainer (einzelnes DER-Zertifikat / PKCS#12-Kette / PKCS#7-Zertifikatsammlung / CRL) und stellt Identität, Gültigkeit, Schlüssel, Verwendung, Erweiterungen, Fingerabdrücke und Kette beider Seiten nebeneinander; das Passwortfeld dient nur dem Lesen der Zertifikatskette eines verschlüsselten PKCS#12 und importiert oder exportiert niemals private Schlüssel." },
			{ "es-ES", "Plugin de vista de comparación de certificados: admite contenedores de certificados (certificado DER único / cadena PKCS#12 / lote PKCS#7 / CRL) y compara en paralelo la identidad, la validez, la clave, el uso, las extensiones, las huellas y la cadena de ambos lados; el campo de contraseña solo sirve para leer la cadena de un PKCS#12 cifrado y nunca importa ni exporta claves privadas." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			{ "Details", new Dictionary<string, string>
				{
					{ "zh-Hans", "证书详情" },
					{ "zh-Hant", "憑證詳情" },
					{ "ja-JP", "詳細" },
					{ "ko-KR", "세부 정보" },
					{ "fr-FR", "Détails" },
					{ "de-DE", "Details" },
					{ "es-ES", "Detalles" }
				}
			},
			{ "Chain", new Dictionary<string, string>
				{
					{ "zh-Hans", "证书链" },
					{ "zh-Hant", "憑證鏈" },
					{ "ja-JP", "証明書チェーン" },
					{ "ko-KR", "인증서 체인" },
					{ "fr-FR", "Chaîne" },
					{ "de-DE", "Kette" },
					{ "es-ES", "Cadena" }
				}
			},
			{ "Identity", new Dictionary<string, string>
				{
					{ "zh-Hans", "身份" },
					{ "zh-Hant", "身分" },
					{ "ja-JP", "識別情報" },
					{ "ko-KR", "ID 정보" },
					{ "fr-FR", "Identité" },
					{ "de-DE", "Identität" },
					{ "es-ES", "Identidad" }
				}
			},
			{ "Validity", new Dictionary<string, string>
				{
					{ "zh-Hans", "有效期" },
					{ "zh-Hant", "有效期" },
					{ "ja-JP", "有効期間" },
					{ "ko-KR", "유효 기간" },
					{ "fr-FR", "Validité" },
					{ "de-DE", "Gültigkeit" },
					{ "es-ES", "Validez" }
				}
			},
			{ "Key & Signature", new Dictionary<string, string>
				{
					{ "zh-Hans", "密钥与签名" },
					{ "zh-Hant", "金鑰與簽章" },
					{ "ja-JP", "鍵と署名" },
					{ "ko-KR", "키 및 서명" },
					{ "fr-FR", "Clé et signature" },
					{ "de-DE", "Schlüssel und Signatur" },
					{ "es-ES", "Clave y firma" }
				}
			},
			{ "Usage", new Dictionary<string, string>
				{
					{ "zh-Hans", "用途" },
					{ "zh-Hant", "用途" },
					{ "ja-JP", "用途" },
					{ "ko-KR", "용도" },
					{ "fr-FR", "Usage" },
					{ "de-DE", "Verwendung" },
					{ "es-ES", "Uso" }
				}
			},
			{ "Extensions", new Dictionary<string, string>
				{
					{ "zh-Hans", "扩展" },
					{ "zh-Hant", "擴充功能" },
					{ "ja-JP", "拡張機能" },
					{ "ko-KR", "확장" },
					{ "fr-FR", "Extensions" },
					{ "de-DE", "Erweiterungen" },
					{ "es-ES", "Extensiones" }
				}
			},
			{ "Fingerprints", new Dictionary<string, string>
				{
					{ "zh-Hans", "指纹" },
					{ "zh-Hant", "指紋" },
					{ "ja-JP", "フィンガープリント" },
					{ "ko-KR", "지문" },
					{ "fr-FR", "Empreintes" },
					{ "de-DE", "Fingerabdrücke" },
					{ "es-ES", "Huellas" }
				}
			},
			{ "Subject", new Dictionary<string, string>
				{
					{ "zh-Hans", "主体" },
					{ "zh-Hant", "主體" },
					{ "ja-JP", "サブジェクト" },
					{ "ko-KR", "주체" },
					{ "fr-FR", "Sujet" },
					{ "de-DE", "Subjekt" },
					{ "es-ES", "Sujeto" }
				}
			},
			{ "Issuer", new Dictionary<string, string>
				{
					{ "zh-Hans", "签发者" },
					{ "zh-Hant", "簽發者" },
					{ "ja-JP", "発行者" },
					{ "ko-KR", "발급자" },
					{ "fr-FR", "Émetteur" },
					{ "de-DE", "Aussteller" },
					{ "es-ES", "Emisor" }
				}
			},
			{ "Serial number", new Dictionary<string, string>
				{
					{ "zh-Hans", "序列号" },
					{ "zh-Hant", "序號" },
					{ "ja-JP", "シリアル番号" },
					{ "ko-KR", "일련 번호" },
					{ "fr-FR", "Numéro de série" },
					{ "de-DE", "Seriennummer" },
					{ "es-ES", "Número de serie" }
				}
			},
			{ "Self-signed", new Dictionary<string, string>
				{
					{ "zh-Hans", "自签名" },
					{ "zh-Hant", "自簽章" },
					{ "ja-JP", "自己署名" },
					{ "ko-KR", "자체 서명" },
					{ "fr-FR", "Auto-signé" },
					{ "de-DE", "Selbstsigniert" },
					{ "es-ES", "Autofirmado" }
				}
			},
			{ "Not before", new Dictionary<string, string>
				{
					{ "zh-Hans", "生效时间" },
					{ "zh-Hant", "生效時間" },
					{ "ja-JP", "有効開始" },
					{ "ko-KR", "유효 시작" },
					{ "fr-FR", "Valide à partir du" },
					{ "de-DE", "Gültig ab" },
					{ "es-ES", "Válido desde" }
				}
			},
			{ "Not after", new Dictionary<string, string>
				{
					{ "zh-Hans", "失效时间" },
					{ "zh-Hant", "失效時間" },
					{ "ja-JP", "有効終了" },
					{ "ko-KR", "유효 종료" },
					{ "fr-FR", "Valide jusqu'au" },
					{ "de-DE", "Gültig bis" },
					{ "es-ES", "Válido hasta" }
				}
			},
			{ "Public key", new Dictionary<string, string>
				{
					{ "zh-Hans", "公钥" },
					{ "zh-Hant", "公開金鑰" },
					{ "ja-JP", "公開鍵" },
					{ "ko-KR", "공개 키" },
					{ "fr-FR", "Clé publique" },
					{ "de-DE", "Öffentlicher Schlüssel" },
					{ "es-ES", "Clave pública" }
				}
			},
			{ "Signature algorithm", new Dictionary<string, string>
				{
					{ "zh-Hans", "签名算法" },
					{ "zh-Hant", "簽章演算法" },
					{ "ja-JP", "署名アルゴリズム" },
					{ "ko-KR", "서명 알고리즘" },
					{ "fr-FR", "Algorithme de signature" },
					{ "de-DE", "Signaturalgorithmus" },
					{ "es-ES", "Algoritmo de firma" }
				}
			},
			{ "Key usage", new Dictionary<string, string>
				{
					{ "zh-Hans", "密钥用途" },
					{ "zh-Hant", "金鑰用途" },
					{ "ja-JP", "鍵用途" },
					{ "ko-KR", "키 사용" },
					{ "fr-FR", "Usage de la clé" },
					{ "de-DE", "Schlüsselverwendung" },
					{ "es-ES", "Uso de la clave" }
				}
			},
			{ "Extended key usage", new Dictionary<string, string>
				{
					{ "zh-Hans", "扩展密钥用途" },
					{ "zh-Hant", "擴充金鑰用途" },
					{ "ja-JP", "拡張鍵用途" },
					{ "ko-KR", "확장 키 사용" },
					{ "fr-FR", "Usage étendu de la clé" },
					{ "de-DE", "Erweiterte Schlüsselverwendung" },
					{ "es-ES", "Uso extendido de la clave" }
				}
			},
			{ "Basic constraints", new Dictionary<string, string>
				{
					{ "zh-Hans", "基本约束" },
					{ "zh-Hant", "基本限制" },
					{ "ja-JP", "基本制約" },
					{ "ko-KR", "기본 제약" },
					{ "fr-FR", "Contraintes de base" },
					{ "de-DE", "Basiseinschränkungen" },
					{ "es-ES", "Restricciones básicas" }
				}
			},
			{ "CA", new Dictionary<string, string>
				{
					{ "zh-Hans", "CA" },
					{ "zh-Hant", "CA" },
					{ "ja-JP", "CA" },
					{ "ko-KR", "CA" },
					{ "fr-FR", "CA" },
					{ "de-DE", "CA" },
					{ "es-ES", "CA" }
				}
			},
			{ "End entity", new Dictionary<string, string>
				{
					{ "zh-Hans", "终端实体" },
					{ "zh-Hant", "終端實體" },
					{ "ja-JP", "エンドエンティティ" },
					{ "ko-KR", "최종 엔터티" },
					{ "fr-FR", "Entité finale" },
					{ "de-DE", "Endentität" },
					{ "es-ES", "Entidad final" }
				}
			},
			{ "Path length", new Dictionary<string, string>
				{
					{ "zh-Hans", "路径长度" },
					{ "zh-Hant", "路徑長度" },
					{ "ja-JP", "パス長" },
					{ "ko-KR", "경로 길이" },
					{ "fr-FR", "Longueur du chemin" },
					{ "de-DE", "Pfadlänge" },
					{ "es-ES", "Longitud de ruta" }
				}
			},
			{ "SAN", new Dictionary<string, string>
				{
					{ "zh-Hans", "SAN" },
					{ "zh-Hant", "SAN" },
					{ "ja-JP", "SAN" },
					{ "ko-KR", "SAN" },
					{ "fr-FR", "SAN" },
					{ "de-DE", "SAN" },
					{ "es-ES", "SAN" }
				}
			},
			{ "DNS", new Dictionary<string, string>
				{
					{ "zh-Hans", "DNS" },
					{ "zh-Hant", "DNS" },
					{ "ja-JP", "DNS" },
					{ "ko-KR", "DNS" },
					{ "fr-FR", "DNS" },
					{ "de-DE", "DNS" },
					{ "es-ES", "DNS" }
				}
			},
			{ "IP", new Dictionary<string, string>
				{
					{ "zh-Hans", "IP" },
					{ "zh-Hant", "IP" },
					{ "ja-JP", "IP" },
					{ "ko-KR", "IP" },
					{ "fr-FR", "IP" },
					{ "de-DE", "IP" },
					{ "es-ES", "IP" }
				}
			},
			{ "URI", new Dictionary<string, string>
				{
					{ "zh-Hans", "URI" },
					{ "zh-Hant", "URI" },
					{ "ja-JP", "URI" },
					{ "ko-KR", "URI" },
					{ "fr-FR", "URI" },
					{ "de-DE", "URI" },
					{ "es-ES", "URI" }
				}
			},
			{ "Email", new Dictionary<string, string>
				{
					{ "zh-Hans", "邮箱" },
					{ "zh-Hant", "電子郵件" },
					{ "ja-JP", "メール" },
					{ "ko-KR", "이메일" },
					{ "fr-FR", "E-mail" },
					{ "de-DE", "E-Mail" },
					{ "es-ES", "Correo" }
				}
			},
			{ "CRL distribution", new Dictionary<string, string>
				{
					{ "zh-Hans", "CRL 分发点" },
					{ "zh-Hant", "CRL 分發點" },
					{ "ja-JP", "CRL 配布点" },
					{ "ko-KR", "CRL 배포 지점" },
					{ "fr-FR", "Point de distribution CRL" },
					{ "de-DE", "CRL-Verteilung" },
					{ "es-ES", "Distribución CRL" }
				}
			},
			{ "OCSP", new Dictionary<string, string>
				{
					{ "zh-Hans", "OCSP" },
					{ "zh-Hant", "OCSP" },
					{ "ja-JP", "OCSP" },
					{ "ko-KR", "OCSP" },
					{ "fr-FR", "OCSP" },
					{ "de-DE", "OCSP" },
					{ "es-ES", "OCSP" }
				}
			},
			{ "Fingerprint SHA-1", new Dictionary<string, string>
				{
					{ "zh-Hans", "指纹 SHA-1" },
					{ "zh-Hant", "指紋 SHA-1" },
					{ "ja-JP", "フィンガープリント SHA-1" },
					{ "ko-KR", "지문 SHA-1" },
					{ "fr-FR", "Empreinte SHA-1" },
					{ "de-DE", "Fingerabdruck SHA-1" },
					{ "es-ES", "Huella SHA-1" }
				}
			},
			{ "Fingerprint SHA-256", new Dictionary<string, string>
				{
					{ "zh-Hans", "指纹 SHA-256" },
					{ "zh-Hant", "指紋 SHA-256" },
					{ "ja-JP", "フィンガープリント SHA-256" },
					{ "ko-KR", "지문 SHA-256" },
					{ "fr-FR", "Empreinte SHA-256" },
					{ "de-DE", "Fingerabdruck SHA-256" },
					{ "es-ES", "Huella SHA-256" }
				}
			},
			{ "Authority", new Dictionary<string, string>
				{
					{ "zh-Hans", "颁发机构" },
					{ "zh-Hant", "頒發機構" },
					{ "ja-JP", "認証局" },
					{ "ko-KR", "인증 기관" },
					{ "fr-FR", "Autorité" },
					{ "de-DE", "Zertifizierungsstelle" },
					{ "es-ES", "Autoridad" }
				}
			},
			{ "valid", new Dictionary<string, string>
				{
					{ "zh-Hans", "有效" },
					{ "zh-Hant", "有效" },
					{ "ja-JP", "有効" },
					{ "ko-KR", "유효" },
					{ "fr-FR", "valide" },
					{ "de-DE", "gültig" },
					{ "es-ES", "válido" }
				}
			},
			{ "expiring soon", new Dictionary<string, string>
				{
					{ "zh-Hans", "即将过期" },
					{ "zh-Hant", "即將過期" },
					{ "ja-JP", "まもなく失効" },
					{ "ko-KR", "곧 만료" },
					{ "fr-FR", "expire bientôt" },
					{ "de-DE", "läuft bald ab" },
					{ "es-ES", "expira pronto" }
				}
			},
			{ "expired", new Dictionary<string, string>
				{
					{ "zh-Hans", "已过期" },
					{ "zh-Hant", "已過期" },
					{ "ja-JP", "失効済み" },
					{ "ko-KR", "만료됨" },
					{ "fr-FR", "expiré" },
					{ "de-DE", "abgelaufen" },
					{ "es-ES", "caducado" }
				}
			},
			{ "not present", new Dictionary<string, string>
				{
					{ "zh-Hans", "不存在" },
					{ "zh-Hant", "不存在" },
					{ "ja-JP", "存在しません" },
					{ "ko-KR", "없음" },
					{ "fr-FR", "absent" },
					{ "de-DE", "nicht vorhanden" },
					{ "es-ES", "no presente" }
				}
			},
			{ "Parsing certificate…", new Dictionary<string, string>
				{
					{ "zh-Hans", "正在解析证书…" },
					{ "zh-Hant", "正在解析憑證…" },
					{ "ja-JP", "証明書を解析中…" },
					{ "ko-KR", "인증서 구문 분석 중…" },
					{ "fr-FR", "Analyse du certificat…" },
					{ "de-DE", "Zertifikat wird analysiert…" },
					{ "es-ES", "Analizando el certificado…" }
				}
			},
			{ "Certificate compare: {0} / {1} certificates", new Dictionary<string, string>
				{
					{ "zh-Hans", "证书对比：{0} / {1} 张证书" },
					{ "zh-Hant", "憑證對比：{0} / {1} 張憑證" },
					{ "ja-JP", "証明書の比較: {0} / {1} 件" },
					{ "ko-KR", "인증서 비교: {0} / {1}개" },
					{ "fr-FR", "Comparaison de certificats : {0} / {1} certificats" },
					{ "de-DE", "Zertifikatvergleich: {0} / {1} Zertifikate" },
					{ "es-ES", "Comparación de certificados: {0} / {1} certificados" }
				}
			},
			{ "Certificate content unavailable", new Dictionary<string, string>
				{
					{ "zh-Hans", "证书内容不可用" },
					{ "zh-Hant", "憑證內容無法取得" },
					{ "ja-JP", "証明書の内容を取得できません" },
					{ "ko-KR", "인증서 내용을 사용할 수 없음" },
					{ "fr-FR", "Contenu du certificat indisponible" },
					{ "de-DE", "Zertifikatsinhalt nicht verfügbar" },
					{ "es-ES", "Contenido del certificado no disponible" }
				}
			},
			{ "Failed to parse certificate", new Dictionary<string, string>
				{
					{ "zh-Hans", "证书解析失败" },
					{ "zh-Hant", "憑證解析失敗" },
					{ "ja-JP", "証明書の解析に失敗しました" },
					{ "ko-KR", "인증서 구문 분석 실패" },
					{ "fr-FR", "Échec de l'analyse du certificat" },
					{ "de-DE", "Zertifikat konnte nicht analysiert werden" },
					{ "es-ES", "No se pudo analizar el certificado" }
				}
			},
			{ "Unsupported certificate container", new Dictionary<string, string>
				{
					{ "zh-Hans", "不支持的证书容器" },
					{ "zh-Hant", "不支援的憑證容器" },
					{ "ja-JP", "未対応の証明書コンテナ" },
					{ "ko-KR", "지원되지 않는 인증서 컨테이너" },
					{ "fr-FR", "Conteneur de certificat non pris en charge" },
					{ "de-DE", "Nicht unterstützter Zertifikatcontainer" },
					{ "es-ES", "Contenedor de certificado no compatible" }
				}
			},
			{ "Password required", new Dictionary<string, string>
				{
					{ "zh-Hans", "需要密码" },
					{ "zh-Hant", "需要密碼" },
					{ "ja-JP", "パスワードが必要です" },
					{ "ko-KR", "비밀번호 필요" },
					{ "fr-FR", "Mot de passe requis" },
					{ "de-DE", "Passwort erforderlich" },
					{ "es-ES", "Se requiere contraseña" }
				}
			},
			{ "Incorrect password. Enter the password and press Apply.", new Dictionary<string, string>
				{
					{ "zh-Hans", "密码不正确。请输入密码并点击「应用」。" },
					{ "zh-Hant", "密碼不正確。請輸入密碼並點選「套用」。" },
					{ "ja-JP", "パスワードが正しくありません。パスワードを入力して「適用」を押してください。" },
					{ "ko-KR", "비밀번호가 올바르지 않습니다. 비밀번호를 입력하고 적용을 누르세요." },
					{ "fr-FR", "Mot de passe incorrect. Saisissez le mot de passe et cliquez sur Appliquer." },
					{ "de-DE", "Falsches Passwort. Geben Sie das Passwort ein und klicken Sie auf Übernehmen." },
					{ "es-ES", "Contraseña incorrecta. Introduzca la contraseña y pulse Aplicar." }
				}
			},
			{ "CRL parsing is not supported", new Dictionary<string, string>
				{
					{ "zh-Hans", "不支持解析 CRL" },
					{ "zh-Hant", "不支援解析 CRL" },
					{ "ja-JP", "CRL の解析はサポートされていません" },
					{ "ko-KR", "CRL 구문 분석은 지원되지 않습니다" },
					{ "fr-FR", "L'analyse des CRL n'est pas prise en charge" },
					{ "de-DE", "CRL-Analyse wird nicht unterstützt" },
					{ "es-ES", "El análisis de CRL no es compatible" }
				}
			},
			{ "Certificates: {0}", new Dictionary<string, string>
				{
					{ "zh-Hans", "证书：{0}" },
					{ "zh-Hant", "憑證：{0}" },
					{ "ja-JP", "証明書: {0}" },
					{ "ko-KR", "인증서: {0}" },
					{ "fr-FR", "Certificats : {0}" },
					{ "de-DE", "Zertifikate: {0}" },
					{ "es-ES", "Certificados: {0}" }
				}
			},
			{ "certificate password (optional)", new Dictionary<string, string>
				{
					{ "zh-Hans", "证书密码（可选）" },
					{ "zh-Hant", "憑證密碼（選填）" },
					{ "ja-JP", "証明書のパスワード（任意）" },
					{ "ko-KR", "인증서 비밀번호(선택 사항)" },
					{ "fr-FR", "mot de passe du certificat (facultatif)" },
					{ "de-DE", "Zertifikatspasswort (optional)" },
					{ "es-ES", "contraseña del certificado (opcional)" }
				}
			},
			{ "{0} days left", new Dictionary<string, string>
				{
					{ "zh-Hans", "剩余 {0} 天" },
					{ "zh-Hant", "剩餘 {0} 天" },
					{ "ja-JP", "残り {0} 日" },
					{ "ko-KR", "{0}일 남음" },
					{ "fr-FR", "{0} jours restants" },
					{ "de-DE", "Noch {0} Tage" },
					{ "es-ES", "Quedan {0} días" }
				}
			},
			{ "expired {0} days ago", new Dictionary<string, string>
				{
					{ "zh-Hans", "已于 {0} 天前过期" },
					{ "zh-Hant", "已於 {0} 天前過期" },
					{ "ja-JP", "{0} 日前に失効" },
					{ "ko-KR", "{0}일 전 만료" },
					{ "fr-FR", "expiré depuis {0} jours" },
					{ "de-DE", "Vor {0} Tagen abgelaufen" },
					{ "es-ES", "Caducó hace {0} días" }
				}
			},
			{ "{0} shared, {1} only left, {2} only right", new Dictionary<string, string>
				{
					{ "zh-Hans", "共有 {0}，仅左 {1}，仅右 {2}" },
					{ "zh-Hant", "共有 {0}，僅左 {1}，僅右 {2}" },
					{ "ja-JP", "共通 {0}、左のみ {1}、右のみ {2}" },
					{ "ko-KR", "공통 {0}, 왼쪽만 {1}, 오른쪽만 {2}" },
					{ "fr-FR", "{0} communs, {1} à gauche uniquement, {2} à droite uniquement" },
					{ "de-DE", "{0} gemeinsam, {1} nur links, {2} nur rechts" },
					{ "es-ES", "{0} compartidos, {1} solo izquierda, {2} solo derecha" }
				}
			},
			{ "Yes", new Dictionary<string, string>
				{
					{ "zh-Hans", "是" },
					{ "zh-Hant", "是" },
					{ "ja-JP", "はい" },
					{ "ko-KR", "예" },
					{ "fr-FR", "Oui" },
					{ "de-DE", "Ja" },
					{ "es-ES", "Sí" }
				}
			},
			{ "No", new Dictionary<string, string>
				{
					{ "zh-Hans", "否" },
					{ "zh-Hant", "否" },
					{ "ja-JP", "いいえ" },
					{ "ko-KR", "아니요" },
					{ "fr-FR", "Non" },
					{ "de-DE", "Nein" },
					{ "es-ES", "No" }
				}
			},
			{ "DER certificate", new Dictionary<string, string>
				{
					{ "zh-Hans", "DER 证书" },
					{ "zh-Hant", "DER 憑證" },
					{ "ja-JP", "DER 証明書" },
					{ "ko-KR", "DER 인증서" },
					{ "fr-FR", "Certificat DER" },
					{ "de-DE", "DER-Zertifikat" },
					{ "es-ES", "Certificado DER" }
				}
			},
			{ "PKCS#12", new Dictionary<string, string>
				{
					{ "zh-Hans", "PKCS#12" },
					{ "zh-Hant", "PKCS#12" },
					{ "ja-JP", "PKCS#12" },
					{ "ko-KR", "PKCS#12" },
					{ "fr-FR", "PKCS#12" },
					{ "de-DE", "PKCS#12" },
					{ "es-ES", "PKCS#12" }
				}
			},
			{ "PKCS#7", new Dictionary<string, string>
				{
					{ "zh-Hans", "PKCS#7" },
					{ "zh-Hant", "PKCS#7" },
					{ "ja-JP", "PKCS#7" },
					{ "ko-KR", "PKCS#7" },
					{ "fr-FR", "PKCS#7" },
					{ "de-DE", "PKCS#7" },
					{ "es-ES", "PKCS#7" }
				}
			},
			{ "CRL", new Dictionary<string, string>
				{
					{ "zh-Hans", "CRL" },
					{ "zh-Hant", "CRL" },
					{ "ja-JP", "CRL" },
					{ "ko-KR", "CRL" },
					{ "fr-FR", "CRL" },
					{ "de-DE", "CRL" },
					{ "es-ES", "CRL" }
				}
			}
		};

		/// <summary>取当前语言译文（查不到回退英文原文）。</summary>
		internal static string T(string english)
		{
			return Table.TryGetValue(english, out Dictionary<string, string> map)
				? PluginLocalization.Resolve(PluginEnvironment.CurrentLanguage, map, english)
				: english;
		}

		/// <summary>取当前语言译文并套用 {0} 占位符。</summary>
		internal static string F(string english, params object[] args)
		{
			return string.Format(T(english), args);
		}
	}
}
