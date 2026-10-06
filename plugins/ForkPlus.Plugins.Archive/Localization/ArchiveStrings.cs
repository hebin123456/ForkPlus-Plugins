using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Archive
{
	/// <summary>
	/// 压缩包插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 组织方式与 <c>ForkPlus.Plugins.Example/Localization/ExampleStrings.cs</c> 一致。
	/// </summary>
	internal static class ArchiveStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="ArchiveDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "压缩包对比" },
			{ "zh-Hant", "壓縮檔對比" },
			{ "ja-JP", "アーカイブ比較" },
			{ "ko-KR", "압축 파일 비교" },
			{ "fr-FR", "Comparaison d'archives" },
			{ "de-DE", "Archivvergleich" },
			{ "es-ES", "Comparación de archivos" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="ArchiveDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "压缩包对比视图插件：认领主流压缩包（zip / 7z / rar / tar，以及 gz / bz2 / xz / zst 流式压缩），把两侧压缩包各自展开成条目树，左右并排对比目录、文件、大小与加密状态，不对比解压后的内容；带密码的压缩包可在视图中解锁。" },
			{ "zh-Hant", "壓縮檔對比檢視外掛：認領主流壓縮檔（zip / 7z / rar / tar，以及 gz / bz2 / xz / zst 串流壓縮），將兩側壓縮檔各自展開成項目樹，左右並排對比資料夾、檔案、大小與加密狀態，不比較解壓後的內容；受密碼保護的壓縮檔可在檢視中解鎖。" },
			{ "ja-JP", "アーカイブ比較ビュープラグイン: 主要なアーカイブ（zip / 7z / rar / tar および gz / bz2 / xz / zst ストリーム）を対象に、両側をエントリツリーへ展開し、フォルダー・ファイル・サイズ・暗号化状態を左右に並べて比較します（解凍後の内容は比較しません）。パスワード保護されたアーカイブはビュー内で解除できます。" },
			{ "ko-KR", "압축 파일 비교 보기 플러그인: 주요 압축 파일(zip / 7z / rar / tar 및 gz / bz2 / xz / zst 스트림)을 처리하여 양쪽을 항목 트리로 확장하고 폴더, 파일, 크기, 암호화 여부를 나란히 비교합니다. 압축 해제된 내용은 비교하지 않으며, 암호로 보호된 압축 파일은 보기에서 잠금을 해제할 수 있습니다." },
			{ "fr-FR", "Plugin de vue de comparaison d'archives : prend en charge les archives courantes (zip / 7z / rar / tar ainsi que les flux gz / bz2 / xz / zst) et développe les deux côtés en une arborescence d'entrées pour comparer côte à côte dossiers, fichiers, tailles et chiffrement, sans comparer le contenu extrait ; les archives protégées par mot de passe peuvent être déverrouillées dans la vue." },
			{ "de-DE", "Archivvergleichs-Ansichts-Plugin: übernimmt gängige Archive (zip / 7z / rar / tar sowie gz / bz2 / xz / zst-Streams) und entpackt beide Seiten in einen Eintragsbaum, um Ordner, Dateien, Größen und Verschlüsselung nebeneinander zu vergleichen, ohne die extrahierten Inhalte zu vergleichen; passwortgeschützte Archive können in der Ansicht entsperrt werden." },
			{ "es-ES", "Plugin de vista de comparación de archivos: admite los archivos más comunes (zip / 7z / rar / tar y flujos gz / bz2 / xz / zst) y expande ambos lados en un árbol de entradas para comparar en paralelo carpetas, archivos, tamaños y cifrado, sin comparar el contenido extraído; los archivos protegidos con contraseña se pueden desbloquear en la vista." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			{ "archive password (optional)", new Dictionary<string, string>
				{
					{ "zh-Hans", "压缩包密码（可选）" },
					{ "zh-Hant", "壓縮檔密碼（選填）" },
					{ "ja-JP", "アーカイブのパスワード（任意）" },
					{ "ko-KR", "압축 파일 비밀번호(선택 사항)" },
					{ "fr-FR", "mot de passe de l'archive (facultatif)" },
					{ "de-DE", "Archivpasswort (optional)" },
					{ "es-ES", "contraseña del archivo (opcional)" }
				}
			},
			{ "Reading archive…", new Dictionary<string, string>
				{
					{ "zh-Hans", "正在读取压缩包…" },
					{ "zh-Hant", "正在讀取壓縮檔…" },
					{ "ja-JP", "アーカイブを読み込み中…" },
					{ "ko-KR", "압축 파일을 읽는 중…" },
					{ "fr-FR", "Lecture de l'archive…" },
					{ "de-DE", "Archiv wird gelesen…" },
					{ "es-ES", "Leyendo el archivo…" }
				}
			},
			{ "Archive compare: {0} / {1} entries", new Dictionary<string, string>
				{
					{ "zh-Hans", "压缩包对比：{0} / {1} 个条目" },
					{ "zh-Hant", "壓縮檔對比：{0} / {1} 個項目" },
					{ "ja-JP", "アーカイブ比較: {0} / {1} エントリ" },
					{ "ko-KR", "압축 파일 비교: {0} / {1}개 항목" },
					{ "fr-FR", "Comparaison d'archives : {0} / {1} entrées" },
					{ "de-DE", "Archivvergleich: {0} / {1} Einträge" },
					{ "es-ES", "Comparación de archivos: {0} / {1} entradas" }
				}
			},
			{ "Failed to read archive", new Dictionary<string, string>
				{
					{ "zh-Hans", "读取压缩包失败" },
					{ "zh-Hant", "讀取壓縮檔失敗" },
					{ "ja-JP", "アーカイブの読み込みに失敗しました" },
					{ "ko-KR", "압축 파일을 읽지 못했습니다" },
					{ "fr-FR", "Échec de la lecture de l'archive" },
					{ "de-DE", "Archiv konnte nicht gelesen werden" },
					{ "es-ES", "No se pudo leer el archivo" }
				}
			},
			{ "{0} files, {1} folders", new Dictionary<string, string>
				{
					{ "zh-Hans", "{0} 个文件，{1} 个文件夹" },
					{ "zh-Hant", "{0} 個檔案，{1} 個資料夾" },
					{ "ja-JP", "{0} ファイル、{1} フォルダー" },
					{ "ko-KR", "파일 {0}개, 폴더 {1}개" },
					{ "fr-FR", "{0} fichiers, {1} dossiers" },
					{ "de-DE", "{0} Dateien, {1} Ordner" },
					{ "es-ES", "{0} archivos, {1} carpetas" }
				}
			},
			{ "encrypted", new Dictionary<string, string>
				{
					{ "zh-Hans", "已加密" },
					{ "zh-Hant", "已加密" },
					{ "ja-JP", "暗号化" },
					{ "ko-KR", "암호화됨" },
					{ "fr-FR", "chiffré" },
					{ "de-DE", "verschlüsselt" },
					{ "es-ES", "cifrado" }
				}
			},
			{ "Showing first {0} entries only.", new Dictionary<string, string>
				{
					{ "zh-Hans", "仅显示前 {0} 个条目。" },
					{ "zh-Hant", "僅顯示前 {0} 個項目。" },
					{ "ja-JP", "先頭 {0} 件のエントリのみ表示しています。" },
					{ "ko-KR", "처음 {0}개 항목만 표시합니다." },
					{ "fr-FR", "Affichage des {0} premières entrées uniquement." },
					{ "de-DE", "Es werden nur die ersten {0} Einträge angezeigt." },
					{ "es-ES", "Solo se muestran las primeras {0} entradas." }
				}
			},
			{ "Entry MD5 computed for {0} / {1} files.", new Dictionary<string, string>
				{
					{ "zh-Hans", "已为 {0} / {1} 个文件计算条目 MD5。" },
					{ "zh-Hant", "已為 {0} / {1} 個檔案計算項目 MD5。" },
					{ "ja-JP", "{0} / {1} ファイルのエントリ MD5 を計算しました。" },
					{ "ko-KR", "{0} / {1}개 파일의 항목 MD5를 계산했습니다." },
					{ "fr-FR", "MD5 d'entrée calculé pour {0} / {1} fichiers." },
					{ "de-DE", "Eintrags-MD5 für {0} / {1} Dateien berechnet." },
					{ "es-ES", "MD5 de entrada calculado para {0} / {1} archivos." }
				}
			},
			{ "empty archive", new Dictionary<string, string>
				{
					{ "zh-Hans", "空压缩包" },
					{ "zh-Hant", "空壓縮檔" },
					{ "ja-JP", "空のアーカイブ" },
					{ "ko-KR", "빈 압축 파일" },
					{ "fr-FR", "archive vide" },
					{ "de-DE", "leeres Archiv" },
					{ "es-ES", "archivo vacío" }
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
			{ "Enter the archive password and press Apply.", new Dictionary<string, string>
				{
					{ "zh-Hans", "请输入压缩包密码并点击「应用」。" },
					{ "zh-Hant", "請輸入壓縮檔密碼並按下「套用」。" },
					{ "ja-JP", "アーカイブのパスワードを入力し、「適用」を押してください。" },
					{ "ko-KR", "압축 파일 비밀번호를 입력하고 '적용'을 누르세요." },
					{ "fr-FR", "Saisissez le mot de passe de l'archive et cliquez sur « Appliquer »." },
					{ "de-DE", "Geben Sie das Archivpasswort ein und klicken Sie auf „Anwenden“." },
					{ "es-ES", "Introduzca la contraseña del archivo y pulse «Aplicar»." }
				}
			},
			{ "Password incorrect", new Dictionary<string, string>
				{
					{ "zh-Hans", "密码不正确" },
					{ "zh-Hant", "密碼不正確" },
					{ "ja-JP", "パスワードが正しくありません" },
					{ "ko-KR", "비밀번호가 올바르지 않습니다" },
					{ "fr-FR", "Mot de passe incorrect" },
					{ "de-DE", "Falsches Passwort" },
					{ "es-ES", "Contraseña incorrecta" }
				}
			},
			{ "Unsupported archive format", new Dictionary<string, string>
				{
					{ "zh-Hans", "不支持的压缩包格式" },
					{ "zh-Hant", "不支援的壓縮檔格式" },
					{ "ja-JP", "未対応のアーカイブ形式" },
					{ "ko-KR", "지원되지 않는 압축 파일 형식" },
					{ "fr-FR", "Format d'archive non pris en charge" },
					{ "de-DE", "Nicht unterstütztes Archivformat" },
					{ "es-ES", "Formato de archivo no compatible" }
				}
			},
			{ "This archive is encrypted. Enter the password and press Apply.", new Dictionary<string, string>
				{
					{ "zh-Hans", "该压缩包已加密。请输入密码并点击「应用」。" },
					{ "zh-Hant", "此壓縮檔已加密。請輸入密碼並按下「套用」。" },
					{ "ja-JP", "このアーカイブは暗号化されています。パスワードを入力して「適用」を押してください。" },
					{ "ko-KR", "이 압축 파일은 암호화되어 있습니다. 비밀번호를 입력하고 '적용'을 누르세요." },
					{ "fr-FR", "Cette archive est chiffrée. Saisissez le mot de passe et cliquez sur « Appliquer »." },
					{ "de-DE", "Dieses Archiv ist verschlüsselt. Geben Sie das Passwort ein und klicken Sie auf „Anwenden“." },
					{ "es-ES", "Este archivo está cifrado. Introduzca la contraseña y pulse «Aplicar»." }
				}
			},
			{ "Incorrect password. Try again.", new Dictionary<string, string>
				{
					{ "zh-Hans", "密码错误，请重试。" },
					{ "zh-Hant", "密碼錯誤，請重試。" },
					{ "ja-JP", "パスワードが正しくありません。もう一度お試しください。" },
					{ "ko-KR", "비밀번호가 올바르지 않습니다. 다시 시도하세요." },
					{ "fr-FR", "Mot de passe incorrect. Réessayez." },
					{ "de-DE", "Falsches Passwort. Bitte erneut versuchen." },
					{ "es-ES", "Contraseña incorrecta. Inténtelo de nuevo." }
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
			{ "Archive content unavailable", new Dictionary<string, string>
				{
					{ "zh-Hans", "压缩包内容不可用" },
					{ "zh-Hant", "壓縮檔內容無法使用" },
					{ "ja-JP", "アーカイブの内容を利用できません" },
					{ "ko-KR", "압축 파일 내용을 사용할 수 없습니다" },
					{ "fr-FR", "Contenu de l'archive indisponible" },
					{ "de-DE", "Archivinhalt nicht verfügbar" },
					{ "es-ES", "Contenido del archivo no disponible" }
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
