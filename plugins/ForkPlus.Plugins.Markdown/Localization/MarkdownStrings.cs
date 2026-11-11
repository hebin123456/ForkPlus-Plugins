using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Markdown
{
	/// <summary>
	/// Markdown 对比插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 组织方式与 <c>ForkPlus.Plugins.Frontend/Localization/FrontendStrings.cs</c> 一致。
	/// </summary>
	internal static class MarkdownStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="MarkdownDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "Markdown 预览对比" },
			{ "zh-Hant", "Markdown 預覽對比" },
			{ "ja-JP", "Markdown 比較" },
			{ "ko-KR", "Markdown 비교" },
			{ "fr-FR", "Comparaison Markdown" },
			{ "de-DE", "Markdown-Vergleich" },
			{ "es-ES", "Comparación de Markdown" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="MarkdownDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "Markdown 对比视图插件：认领 .md / .markdown，三模式对比——「渲染」把两侧 Markdown 用 Markdig 渲成样式化 HTML 并排预览；「大纲」按标题文本对齐做层级语义 diff（四色标注）；「源码」并排查看原始文本。" },
			{ "zh-Hant", "Markdown 對比檢視外掛：認領 .md / .markdown，三模式對比——「渲染」把兩側 Markdown 用 Markdig 渲成樣式化 HTML 並排預覽；「大綱」按標題文字對齊做層級語義 diff（四色標註）；「原始碼」並排檢視原始文字。" },
			{ "ja-JP", "Markdown 比較ビュープラグイン: .md / .markdown を対象に三つのモードで比較します。「レンダリング」は両側の Markdown を Markdig でスタイル付き HTML にして並べてプレビュー、「アウトライン」は見出しテキストで位置合わせしたレベルの意味 diff（四色标注）、「ソース」は生のテキストを並べて表示します。" },
			{ "ko-KR", "Markdown 비교 보기 플러그인: .md / .markdown을 처리하며 세 가지 모드로 비교합니다. '렌더링'은 양쪽 Markdown을 Markdig로 스타일이 적용된 HTML로 변환해 나란히 미리 보기, '개요'는 제목 텍스트 기준으로 정렬한 수준 의미 diff(4색 표시), '소스'는 원본 텍스트를 나란히 표시합니다." },
			{ "fr-FR", "Plugin de vue de comparaison Markdown : prend en charge .md / .markdown et compare selon trois modes — « rendu » assemble chaque côté en HTML stylisé (Markdig) affiché côte à côte ; « plan » effectue un diff sémantique des niveaux de titres alignés par texte (quatre couleurs) ; « source » affiche le texte brut côte à côte." },
			{ "de-DE", "Markdown-Vergleichsansichts-Plugin: übernimmt .md / .markdown und vergleicht in drei Modi — „Wiedergabe“ rendert jede Seite mit Markdig zu gestaltetem HTML nebeneinander; „Gliederung“ führt ein semantisches Diff der Überschriftenebenen durch, ausgerichtet am Text (Vierfarbenmarkierung); „Quelltext“ zeigt den Rohtext nebeneinander." },
			{ "es-ES", "Plugin de vista de comparación de Markdown: admite .md / .markdown y compara en tres modos: «representación» convierte cada lado a HTML con estilo (Markdig) en paralelo; «esquema» hace un diff semántico de los niveles de títulos alineados por texto (marcado de cuatro colores); «fuente» muestra el texto sin procesar en paralelo." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			// 模式名（同时用作工具条按钮文案）
			{ "Render", new Dictionary<string, string>
				{
					{ "zh-Hans", "渲染" },
					{ "zh-Hant", "渲染" },
					{ "ja-JP", "レンダリング" },
					{ "ko-KR", "렌더링" },
					{ "fr-FR", "Rendu" },
					{ "de-DE", "Wiedergabe" },
					{ "es-ES", "Representación" }
				}
			},
			{ "Outline", new Dictionary<string, string>
				{
					{ "zh-Hans", "大纲" },
					{ "zh-Hant", "大綱" },
					{ "ja-JP", "アウトライン" },
					{ "ko-KR", "개요" },
					{ "fr-FR", "Plan" },
					{ "de-DE", "Gliederung" },
					{ "es-ES", "Esquema" }
				}
			},
			{ "Source", new Dictionary<string, string>
				{
					{ "zh-Hans", "源码" },
					{ "zh-Hant", "原始碼" },
					{ "ja-JP", "ソース" },
					{ "ko-KR", "소스" },
					{ "fr-FR", "Source" },
					{ "de-DE", "Quelltext" },
					{ "es-ES", "Fuente" }
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
			{ "File too large to render", new Dictionary<string, string>
				{
					{ "zh-Hans", "文件过大，不渲染" },
					{ "zh-Hant", "檔案過大，不渲染" },
					{ "ja-JP", "ファイルが大きすぎるためレンダリングしません" },
					{ "ko-KR", "파일이 너무 커서 렌더링하지 않습니다" },
					{ "fr-FR", "Fichier trop volumineux pour le rendu" },
					{ "de-DE", "Datei zu groß für die Wiedergabe" },
					{ "es-ES", "Archivo demasiado grande para representar" }
				}
			},
			{ "Preview not available in this environment", new Dictionary<string, string>
				{
					{ "zh-Hans", "当前环境不支持渲染预览" },
					{ "zh-Hant", "目前環境不支援渲染預覽" },
					{ "ja-JP", "この環境ではプレビューをレンダリングできません" },
					{ "ko-KR", "이 환경에서는 미리 보기를 렌더링할 수 없습니다" },
					{ "fr-FR", "Aperçu non disponible dans cet environnement" },
					{ "de-DE", "Vorschau in dieser Umgebung nicht verfügbar" },
					{ "es-ES", "Vista previa no disponible en este entorno" }
				}
			},
			{ "Analyzing…", new Dictionary<string, string>
				{
					{ "zh-Hans", "正在分析…" },
					{ "zh-Hant", "正在分析…" },
					{ "ja-JP", "解析中…" },
					{ "ko-KR", "분석하는 중…" },
					{ "fr-FR", "Analyse…" },
					{ "de-DE", "Analyse läuft…" },
					{ "es-ES", "Analizando…" }
				}
			},
			{ "Failed to read: {0}", new Dictionary<string, string>
				{
					{ "zh-Hans", "读取失败：{0}" },
					{ "zh-Hant", "讀取失敗：{0}" },
					{ "ja-JP", "読み込みに失敗しました: {0}" },
					{ "ko-KR", "읽기 실패: {0}" },
					{ "fr-FR", "Échec de la lecture : {0}" },
					{ "de-DE", "Lesen fehlgeschlagen: {0}" },
					{ "es-ES", "No se pudo leer: {0}" }
				}
			},
			{ "Markdown compare: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "Markdown 对比：{0} / {1}" },
					{ "zh-Hant", "Markdown 對比：{0} / {1}" },
					{ "ja-JP", "Markdown 比較: {0} / {1}" },
					{ "ko-KR", "Markdown 비교: {0} / {1}" },
					{ "fr-FR", "Comparaison Markdown : {0} / {1}" },
					{ "de-DE", "Markdown-Vergleich: {0} / {1}" },
					{ "es-ES", "Comparación de Markdown: {0} / {1}" }
				}
			},
			{ "headings: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "标题 {0} / {1}" },
					{ "zh-Hant", "標題 {0} / {1}" },
					{ "ja-JP", "見出し {0} / {1}" },
					{ "ko-KR", "제목 {0} / {1}" },
					{ "fr-FR", "titres : {0} / {1}" },
					{ "de-DE", "Überschriften: {0} / {1}" },
					{ "es-ES", "títulos: {0} / {1}" }
				}
			},
			{ "Outline: {0} changed · {1} left only · {2} right only", new Dictionary<string, string>
				{
					{ "zh-Hans", "大纲：{0} 层级变化 · {1} 仅左 · {2} 仅右" },
					{ "zh-Hant", "大綱：{0} 層級變化 · {1} 僅左 · {2} 僅右" },
					{ "ja-JP", "アウトライン: {0} 件のレベル変更 · {1} 左のみ · {2} 右のみ" },
					{ "ko-KR", "개요: {0}개 수준 변경 · {1} 왼쪽만 · {2} 오른쪽만" },
					{ "fr-FR", "Plan : {0} niveaux modifiés · {1} à gauche uniquement · {2} à droite uniquement" },
					{ "de-DE", "Gliederung: {0} Ebenen geändert · {1} nur links · {2} nur rechts" },
					{ "es-ES", "Esquema: {0} niveles modificados · {1} solo izquierda · {2} solo derecha" }
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
			{ "Changed", new Dictionary<string, string>
				{
					{ "zh-Hans", "内容有差异" },
					{ "zh-Hant", "內容有差異" },
					{ "ja-JP", "内容に差分あり" },
					{ "ko-KR", "내용에 차이 있음" },
					{ "fr-FR", "Contenu différent" },
					{ "de-DE", "Inhalt abweichend" },
					{ "es-ES", "Contenido diferente" }
				}
			},
			{ "One side is not present; only the other side is shown.", new Dictionary<string, string>
				{
					{ "zh-Hans", "一侧不存在，仅显示另一侧。" },
					{ "zh-Hant", "一側不存在，僅顯示另一側。" },
					{ "ja-JP", "片側が存在しないため、もう一方のみ表示しています。" },
					{ "ko-KR", "한쪽이 존재하지 않아 다른 쪽만 표시합니다." },
					{ "fr-FR", "Un côté est absent ; seul l'autre côté est affiché." },
					{ "de-DE", "Eine Seite ist nicht vorhanden; es wird nur die andere Seite angezeigt." },
					{ "es-ES", "Un lado no está presente; solo se muestra el otro lado." }
				}
			},
			{ "One side is not present; values shown for the other side only.", new Dictionary<string, string>
				{
					{ "zh-Hans", "一侧不存在，仅显示另一侧的值。" },
					{ "zh-Hant", "一側不存在，僅顯示另一側的值。" },
					{ "ja-JP", "片側が存在しないため、もう一方の値のみ表示しています。" },
					{ "ko-KR", "한쪽이 존재하지 않아 다른 쪽의 값만 표시합니다." },
					{ "fr-FR", "Un côté est absent ; seules les valeurs de l'autre côté sont affichées." },
					{ "de-DE", "Eine Seite ist nicht vorhanden; es werden nur die Werte der anderen Seite angezeigt." },
					{ "es-ES", "Un lado no está presente; solo se muestran los valores del otro lado." }
				}
			},
			{ "Showing first {0} of {1} characters", new Dictionary<string, string>
				{
					{ "zh-Hans", "仅显示前 {0} 个字符（共 {1} 个）" },
					{ "zh-Hant", "僅顯示前 {0} 個字元（共 {1} 個）" },
					{ "ja-JP", "先頭 {0} 文字のみ表示（全 {1} 文字）" },
					{ "ko-KR", "앞 {0}자만 표시(전체 {1}자)" },
					{ "fr-FR", "Affichage des {0} premiers caractères sur {1}" },
					{ "de-DE", "Es werden nur die ersten {0} von {1} Zeichen angezeigt" },
					{ "es-ES", "Se muestran solo los primeros {0} de {1} caracteres" }
				}
			},
			{ "Showing first {0} of {1} rows", new Dictionary<string, string>
				{
					{ "zh-Hans", "仅显示前 {0} 行（共 {1} 行）" },
					{ "zh-Hant", "僅顯示前 {0} 行（共 {1} 行）" },
					{ "ja-JP", "先頭 {0} 行のみ表示（全 {1} 行）" },
					{ "ko-KR", "앞 {0}행만 표시(전체 {1}행)" },
					{ "fr-FR", "Affichage des {0} premières lignes sur {1}" },
					{ "de-DE", "Es werden nur die ersten {0} von {1} Zeilen angezeigt" },
					{ "es-ES", "Se muestran solo las primeras {0} de {1} filas" }
				}
			},
			// 大纲表表头与状态列
			{ "Heading", new Dictionary<string, string>
				{
					{ "zh-Hans", "标题" },
					{ "zh-Hant", "標題" },
					{ "ja-JP", "見出し" },
					{ "ko-KR", "제목" },
					{ "fr-FR", "Titre" },
					{ "de-DE", "Überschrift" },
					{ "es-ES", "Título" }
				}
			},
			{ "Old", new Dictionary<string, string>
				{
					{ "zh-Hans", "旧" },
					{ "zh-Hant", "舊" },
					{ "ja-JP", "旧" },
					{ "ko-KR", "이전" },
					{ "fr-FR", "Ancien" },
					{ "de-DE", "Alt" },
					{ "es-ES", "Antiguo" }
				}
			},
			{ "New", new Dictionary<string, string>
				{
					{ "zh-Hans", "新" },
					{ "zh-Hant", "新" },
					{ "ja-JP", "新" },
					{ "ko-KR", "새" },
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
					{ "zh-Hans", "层级已变" },
					{ "zh-Hant", "層級已變" },
					{ "ja-JP", "レベル変更" },
					{ "ko-KR", "수준 변경" },
					{ "fr-FR", "niveau modifié" },
					{ "de-DE", "Ebene geändert" },
					{ "es-ES", "nivel modificado" }
				}
			},
			{ "left only", new Dictionary<string, string>
				{
					{ "zh-Hans", "仅左" },
					{ "zh-Hant", "僅左" },
					{ "ja-JP", "左のみ" },
					{ "ko-KR", "왼쪽만" },
					{ "fr-FR", "à gauche uniquement" },
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
					{ "fr-FR", "à droite uniquement" },
					{ "de-DE", "nur rechts" },
					{ "es-ES", "solo derecha" }
				}
			},
			// 「渲染」模式下对已知限制的说明（外部相对图片无法解析）
			{ "Relative image references cannot be resolved and may be missing.", new Dictionary<string, string>
				{
					{ "zh-Hans", "文档里以相对路径引用的图片无法解析，可能缺失。" },
					{ "zh-Hant", "文件以相對路徑引用的圖片無法解析，可能缺失。" },
					{ "ja-JP", "相対パスで参照された画像は解決できず、欠落する場合があります。" },
					{ "ko-KR", "상대 경로로 참조된 이미지는 확인할 수 없어 누락될 수 있습니다." },
					{ "fr-FR", "Les images référencées par chemin relatif ne peuvent pas être résolues et peuvent manquer." },
					{ "de-DE", "Über relative Pfade referenzierte Bilder können nicht aufgelöst werden und fehlen möglicherweise." },
					{ "es-ES", "Las imágenes referenciadas por rutas relativas no se pueden resolver y pueden faltar." }
				}
			},
			// 组装器截断说明
			{ "Truncated: document exceeds the preview budget.", new Dictionary<string, string>
				{
					{ "zh-Hans", "已截断：文档超出预览预算。" },
					{ "zh-Hant", "已截斷：文件超出預覽預算。" },
					{ "ja-JP", "切り詰めました: ドキュメントがプレビュー予算を超えています。" },
					{ "ko-KR", "잘렸습니다: 문서가 미리 보기 예산을 초과합니다." },
					{ "fr-FR", "Tronqué : le document dépasse le budget d'aperçu." },
					{ "de-DE", "Gekürzt: das Dokument überschreitet das Vorschau-Budget." },
					{ "es-ES", "Truncado: el documento supera el presupuesto de vista previa." }
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
