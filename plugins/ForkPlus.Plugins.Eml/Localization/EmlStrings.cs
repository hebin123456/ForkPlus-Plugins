using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Eml
{
	/// <summary>
	/// 邮件对比插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 组织方式与 <c>ForkPlus.Plugins.Sqlite/Localization/SqliteStrings.cs</c> 一致。
	/// </summary>
	internal static class EmlStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="EmlDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "邮件对比" },
			{ "zh-Hant", "郵件對比" },
			{ "ja-JP", "メール比較" },
			{ "ko-KR", "이메일 비교" },
			{ "fr-FR", "Comparaison d'e-mail" },
			{ "de-DE", "E-Mail-Vergleich" },
			{ "es-ES", "Comparación de correo" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="EmlDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "邮件（.eml）对比视图插件：认领 .eml，纯托管解析 RFC 5322 邮件头与 MIME 多部件树（boundary 切分、base64 / quoted-printable 解码、RFC 2047 编码字解码），提供「邮件头」「部件」「正文」三种 diff 视图。" },
			{ "zh-Hant", "郵件（.eml）對比檢視外掛：認領 .eml，純受控解析 RFC 5322 郵件標頭與 MIME 多部件樹（boundary 切分、base64 / quoted-printable 解碼、RFC 2047 編碼字解碼），提供「郵件標頭」「部件」「內文」三種 diff 檢視。" },
			{ "ja-JP", "メール (.eml) 比較ビュープラグイン: .eml を対象に、RFC 5322 ヘッダーと MIME マルチパート ツリー (boundary 分割、base64 / quoted-printable デコード、RFC 2047 エンコード語デコード) をマネージドで解析し、「ヘッダー」「パート」「本文」の 3 つの diff ビューを提供します。" },
			{ "ko-KR", "이메일(.eml) 비교 보기 플러그인: .eml을 처리하고 RFC 5322 헤더와 MIME 멀티파트 트리(경계 분할, base64 / quoted-printable 디코딩, RFC 2047 인코딩 단어 디코딩)를 관리 코드로 분석하여 '헤더', '파트', '본문' 세 가지 diff 보기를 제공합니다." },
			{ "fr-FR", "Plugin de vue de comparaison d'e-mail (.eml) : prend en charge .eml, analyse les en-têtes RFC 5322 et l'arbre MIME multipart en code managé (découpage par boundary, décodage base64 / quoted-printable, décodage des mots encodés RFC 2047) et fournit trois vues de comparaison « en-têtes », « parties » et « corps »." },
			{ "de-DE", "E-Mail-Vergleichs-Plugin (.eml): übernimmt .eml, parst RFC 5322-Header und den MIME-Multipart-Baum in verwaltetem Code (Boundary-Aufteilung, base64-/quoted-printable-Dekodierung, RFC 2047-Encoded-Word-Dekodierung) und bietet drei Diff-Ansichten „Header“, „Teile“ und „Textkörper“." },
			{ "es-ES", "Plugin de vista de comparación de correo (.eml): admite .eml, analiza las cabeceras RFC 5322 y el árbol MIME multipart en código administrado (división por boundary, decodificación base64 / quoted-printable, decodificación de palabras codificadas RFC 2047) y ofrece tres vistas de comparación «cabeceras», «partes» y «cuerpo»." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			// 模式名（同时用作工具条按钮文案）
			{ "Headers", new Dictionary<string, string>
				{
					{ "zh-Hans", "邮件头" },
					{ "zh-Hant", "郵件標頭" },
					{ "ja-JP", "ヘッダー" },
					{ "ko-KR", "헤더" },
					{ "fr-FR", "En-têtes" },
					{ "de-DE", "Header" },
					{ "es-ES", "Cabeceras" }
				}
			},
			{ "Parts", new Dictionary<string, string>
				{
					{ "zh-Hans", "部件" },
					{ "zh-Hant", "部件" },
					{ "ja-JP", "パート" },
					{ "ko-KR", "파트" },
					{ "fr-FR", "Parties" },
					{ "de-DE", "Teile" },
					{ "es-ES", "Partes" }
				}
			},
			{ "Body", new Dictionary<string, string>
				{
					{ "zh-Hans", "正文" },
					{ "zh-Hant", "內文" },
					{ "ja-JP", "本文" },
					{ "ko-KR", "본문" },
					{ "fr-FR", "Corps" },
					{ "de-DE", "Textkörper" },
					{ "es-ES", "Cuerpo" }
				}
			},
			// 表头
			{ "Key path", new Dictionary<string, string>
				{
					{ "zh-Hans", "键路径" },
					{ "zh-Hant", "鍵路徑" },
					{ "ja-JP", "キーパス" },
					{ "ko-KR", "키 경로" },
					{ "fr-FR", "Chemin de clé" },
					{ "de-DE", "Schlüsselpfad" },
					{ "es-ES", "Ruta de clave" }
				}
			},
			{ "Old", new Dictionary<string, string>
				{
					{ "zh-Hans", "旧值" },
					{ "zh-Hant", "舊值" },
					{ "ja-JP", "旧" },
					{ "ko-KR", "이전" },
					{ "fr-FR", "Ancien" },
					{ "de-DE", "Alt" },
					{ "es-ES", "Antiguo" }
				}
			},
			{ "New", new Dictionary<string, string>
				{
					{ "zh-Hans", "新值" },
					{ "zh-Hant", "新值" },
					{ "ja-JP", "新" },
					{ "ko-KR", "이후" },
					{ "fr-FR", "Nouveau" },
					{ "de-DE", "Neu" },
					{ "es-ES", "Nuevo" }
				}
			},
			{ "State", new Dictionary<string, string>
				{
					{ "zh-Hans", "状态" },
					{ "zh-Hant", "狀態" },
					{ "ja-JP", "状態" },
					{ "ko-KR", "상태" },
					{ "fr-FR", "État" },
					{ "de-DE", "Status" },
					{ "es-ES", "Estado" }
				}
			},
			// 变更标注词
			{ "same", new Dictionary<string, string>
				{
					{ "zh-Hans", "相同" },
					{ "zh-Hant", "相同" },
					{ "ja-JP", "同一" },
					{ "ko-KR", "동일" },
					{ "fr-FR", "identique" },
					{ "de-DE", "identisch" },
					{ "es-ES", "igual" }
				}
			},
			{ "changed", new Dictionary<string, string>
				{
					{ "zh-Hans", "已变" },
					{ "zh-Hant", "已變" },
					{ "ja-JP", "変更" },
					{ "ko-KR", "변경됨" },
					{ "fr-FR", "modifié" },
					{ "de-DE", "geändert" },
					{ "es-ES", "cambiado" }
				}
			},
			{ "left only", new Dictionary<string, string>
				{
					{ "zh-Hans", "仅左" },
					{ "zh-Hant", "僅左" },
					{ "ja-JP", "左のみ" },
					{ "ko-KR", "왼쪽만" },
					{ "fr-FR", "gauche uniquement" },
					{ "de-DE", "nur links" },
					{ "es-ES", "solo izquierda" }
				}
			},
			{ "right only", new Dictionary<string, string>
				{
					{ "zh-Hans", "仅右" },
					{ "zh-Hant", "僅右" },
					{ "ja-JP", "右のみ" },
					{ "ko-KR", "오른쪽만" },
					{ "fr-FR", "droite uniquement" },
					{ "de-DE", "nur rechts" },
					{ "es-ES", "solo derecha" }
				}
			},
			// 其它文案
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
			{ "File too large to preview", new Dictionary<string, string>
				{
					{ "zh-Hans", "文件过大，不预览" },
					{ "zh-Hant", "檔案過大，不預覽" },
					{ "ja-JP", "ファイルが大きすぎるためプレビューしません" },
					{ "ko-KR", "파일이 너무 커서 미리 보지 않습니다" },
					{ "fr-FR", "Fichier trop volumineux pour un aperçu" },
					{ "de-DE", "Datei zu groß für die Vorschau" },
					{ "es-ES", "Archivo demasiado grande para previsualizar" }
				}
			},
			{ "Analyzing…", new Dictionary<string, string>
				{
					{ "zh-Hans", "正在解析…" },
					{ "zh-Hant", "正在解析…" },
					{ "ja-JP", "解析中…" },
					{ "ko-KR", "분석하는 중…" },
					{ "fr-FR", "Analyse…" },
					{ "de-DE", "Analyse läuft…" },
					{ "es-ES", "Analizando…" }
				}
			},
			{ "Failed to parse: {0}", new Dictionary<string, string>
				{
					{ "zh-Hans", "解析失败：{0}" },
					{ "zh-Hant", "解析失敗：{0}" },
					{ "ja-JP", "解析に失敗しました: {0}" },
					{ "ko-KR", "분석 실패: {0}" },
					{ "fr-FR", "Échec de l'analyse : {0}" },
					{ "de-DE", "Analyse fehlgeschlagen: {0}" },
					{ "es-ES", "No se pudo analizar: {0}" }
				}
			},
			{ "Email compare: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "邮件对比：{0} / {1}" },
					{ "zh-Hant", "郵件對比：{0} / {1}" },
					{ "ja-JP", "メール比較: {0} / {1}" },
					{ "ko-KR", "이메일 비교: {0} / {1}" },
					{ "fr-FR", "Comparaison d'e-mail : {0} / {1}" },
					{ "de-DE", "E-Mail-Vergleich: {0} / {1}" },
					{ "es-ES", "Comparación de correo: {0} / {1}" }
				}
			},
			{ "headers: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "邮件头数：{0} / {1}" },
					{ "zh-Hant", "郵件標頭數：{0} / {1}" },
					{ "ja-JP", "ヘッダー数: {0} / {1}" },
					{ "ko-KR", "헤더 수: {0} / {1}" },
					{ "fr-FR", "en-têtes : {0} / {1}" },
					{ "de-DE", "Header: {0} / {1}" },
					{ "es-ES", "cabeceras: {0} / {1}" }
				}
			},
			{ "parts: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "部件数：{0} / {1}" },
					{ "zh-Hant", "部件數：{0} / {1}" },
					{ "ja-JP", "パート数: {0} / {1}" },
					{ "ko-KR", "파트 수: {0} / {1}" },
					{ "fr-FR", "parties : {0} / {1}" },
					{ "de-DE", "Teile: {0} / {1}" },
					{ "es-ES", "partes: {0} / {1}" }
				}
			},
			{ "No differences", new Dictionary<string, string>
				{
					{ "zh-Hans", "无差异" },
					{ "zh-Hant", "無差異" },
					{ "ja-JP", "差分なし" },
					{ "ko-KR", "차이 없음" },
					{ "fr-FR", "Aucune différence" },
					{ "de-DE", "Keine Unterschiede" },
					{ "es-ES", "Sin diferencias" }
				}
			},
			{ "Summary: {0} entries · {1} changed · {2} left only · {3} right only", new Dictionary<string, string>
				{
					{ "zh-Hans", "共 {0} 项 · {1} 已变 · {2} 仅左 · {3} 仅右" },
					{ "zh-Hant", "共 {0} 項 · {1} 已變 · {2} 僅左 · {3} 僅右" },
					{ "ja-JP", "{0} 項目 · 変更 {1} · 左のみ {2} · 右のみ {3}" },
					{ "ko-KR", "총 {0}개 · 변경 {1} · 왼쪽만 {2} · 오른쪽만 {3}" },
					{ "fr-FR", "{0} entrées · {1} modifiées · {2} gauche uniquement · {3} droite uniquement" },
					{ "de-DE", "{0} Einträge · {1} geändert · {2} nur links · {3} nur rechts" },
					{ "es-ES", "{0} entradas · {1} modificadas · {2} solo izquierda · {3} solo derecha" }
				}
			},
			{ "Showing first {0} of {1} rows", new Dictionary<string, string>
				{
					{ "zh-Hans", "仅显示前 {0} / {1} 行" },
					{ "zh-Hant", "僅顯示前 {0} / {1} 行" },
					{ "ja-JP", "先頭 {0} / {1} 行のみ表示" },
					{ "ko-KR", "처음 {0} / {1}개 행만 표시" },
					{ "fr-FR", "Affichage des {0} premières lignes sur {1}" },
					{ "de-DE", "Es werden die ersten {0} von {1} Zeilen angezeigt" },
					{ "es-ES", "Mostrando las primeras {0} de {1} filas" }
				}
			},
			{ "One side is not present; values shown for the other side only.", new Dictionary<string, string>
				{
					{ "zh-Hans", "一侧不存在，仅显示另一侧的值。" },
					{ "zh-Hant", "一側不存在，僅顯示另一側的值。" },
					{ "ja-JP", "片側が存在しないため、もう一方の値のみ表示しています。" },
					{ "ko-KR", "한쪽이 존재하지 않아 다른 쪽 값만 표시합니다." },
					{ "fr-FR", "Un côté est absent ; seules les valeurs de l'autre côté sont affichées." },
					{ "de-DE", "Eine Seite ist nicht vorhanden; es werden nur die Werte der anderen Seite angezeigt." },
					{ "es-ES", "Un lado no está presente; solo se muestran los valores del otro lado." }
				}
			},
			{ "Content truncated: only the first headers / parts / body lines are shown.", new Dictionary<string, string>
				{
					{ "zh-Hans", "内容已截断：仅显示前若干邮件头 / 部件 / 正文行。" },
					{ "zh-Hant", "內容已截斷：僅顯示前若干郵件標頭 / 部件 / 內文行。" },
					{ "ja-JP", "内容を切り詰めました。先頭のヘッダー / パート / 本文行のみ表示しています。" },
					{ "ko-KR", "내용이 잘렸습니다. 앞부분의 헤더 / 파트 / 본문 줄만 표시합니다." },
					{ "fr-FR", "Contenu tronqué : seuls les premiers en-têtes / parties / lignes de corps sont affichés." },
					{ "de-DE", "Inhalt abgeschnitten: Es werden nur die ersten Header / Teile / Textzeilen angezeigt." },
					{ "es-ES", "Contenido truncado: solo se muestran las primeras cabeceras / partes / líneas de cuerpo." }
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