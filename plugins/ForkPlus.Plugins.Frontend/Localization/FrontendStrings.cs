using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Frontend
{
	/// <summary>
	/// 前端代码对比插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 组织方式与 <c>ForkPlus.Plugins.Epub/Localization/EpubStrings.cs</c> 一致。
	/// </summary>
	internal static class FrontendStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="FrontendDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "前端代码预览对比" },
			{ "zh-Hant", "前端程式碼預覽對比" },
			{ "ja-JP", "フロントエンドコード比較" },
			{ "ko-KR", "프런트엔드 코드 비교" },
			{ "fr-FR", "Comparaison de code front-end" },
			{ "de-DE", "Frontend-Code-Vergleich" },
			{ "es-ES", "Comparación de código front-end" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="FrontendDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "前端代码对比视图插件：认领 .html / .htm / .vue，把两侧各自组装成自包含 HTML（.vue 由随包内嵌的 Vue 3 运行时做运行期模板编译），交给并排双 WebView 真渲染；另提供「源码」模式并排查看原始代码。" },
			{ "zh-Hant", "前端程式碼對比檢視外掛：認領 .html / .htm / .vue，把兩側各自組裝成自包含 HTML（.vue 由隨包內嵌的 Vue 3 執行期做執行期範本編譯），交給並排雙 WebView 真渲染；另提供「原始碼」模式並排檢視原始程式碼。" },
			{ "ja-JP", "フロントエンドコード比較ビュープラグイン: .html / .htm / .vue を対象に、各側を自己完結型 HTML に組み立て（.vue は同梱の Vue 3 ランタイムで実行時テンプレートコンパイル）、並排デュアル WebView で実レンダリングします。さらに「ソース」モードで生のコードを並べて表示します。" },
			{ "ko-KR", "프런트엔드 코드 비교 보기 플러그인: .html / .htm / .vue를 처리하고 각 측을 자체 포함 HTML로 조립하며(.vue는 동봉된 Vue 3 런타임으로 실행 시 템플릿 컴파일), 나란히 놓인 듀얼 WebView로 실제 렌더링합니다. 또한 '소스' 모드에서 원본 코드를 나란히 표시합니다." },
			{ "fr-FR", "Plugin de vue de comparaison front-end : prend en charge .html / .htm / .vue, assemble chaque côté en un document HTML autonome (les composants monofichiers .vue sont compilés à l'exécution par le runtime Vue 3 embarqué) et les rend dans deux WebView côte à côte ; un mode « source » affiche le code brut côte à côte." },
			{ "de-DE", "Frontend-Code-Vergleichsansichts-Plugin: übernimmt .html / .htm / .vue, setzt jede Seite zu einem eigenständigen HTML-Dokument zusammen (.vue-Einzeldateikomponenten werden zur Laufzeit vom mitgelieferten Vue-3-Runtime kompiliert) und rendert es in zwei nebeneinanderliegenden WebViews; ein Modus „Quelltext“ zeigt den Rohcode nebeneinander." },
			{ "es-ES", "Plugin de vista de comparación de código front-end: admite .html / .htm / .vue, ensambla cada lado en un documento HTML autocontenido (los componentes de archivo único .vue se compilan en tiempo de ejecución con el runtime de Vue 3 incluido) y lo representa en dos WebView en paralelo; un modo «fuente» muestra el código sin procesar en paralelo." }
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
			{ "Frontend compare: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "前端对比：{0} / {1}" },
					{ "zh-Hant", "前端對比：{0} / {1}" },
					{ "ja-JP", "フロントエンド比較: {0} / {1}" },
					{ "ko-KR", "프런트엔드 비교: {0} / {1}" },
					{ "fr-FR", "Comparaison front-end : {0} / {1}" },
					{ "de-DE", "Frontend-Vergleich: {0} / {1}" },
					{ "es-ES", "Comparación front-end: {0} / {1}" }
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
			{ "Identical", new Dictionary<string, string>
				{
					{ "zh-Hans", "两侧内容相同" },
					{ "zh-Hant", "兩側內容相同" },
					{ "ja-JP", "両側の内容は同一" },
					{ "ko-KR", "양쪽 내용 동일" },
					{ "fr-FR", "Contenu identique" },
					{ "de-DE", "Inhalt identisch" },
					{ "es-ES", "Contenido idéntico" }
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
			// 「渲染」模式下对已知限制的说明（外部相对资源无法解析）
			{ "External relative assets (css / js / images) cannot be resolved and may be missing.", new Dictionary<string, string>
				{
					{ "zh-Hans", "页面里以相对路径引用的外部 css / js / 图片无法解析，可能缺失。" },
					{ "zh-Hant", "頁面以相對路徑引用的外部 css / js / 圖片無法解析，可能缺失。" },
					{ "ja-JP", "相対パスで参照された外部 css / js / 画像は解決できず、欠落する場合があります。" },
					{ "ko-KR", "상대 경로로 참조된 외부 css / js / 이미지는 확인할 수 없어 누락될 수 있습니다." },
					{ "fr-FR", "Les ressources externes référencées par chemin relatif (css / js / images) ne peuvent pas être résolues et peuvent manquer." },
					{ "de-DE", "Über relative Pfade referenzierte externe Ressourcen (css / js / Bilder) können nicht aufgelöst werden und fehlen möglicherweise." },
					{ "es-ES", "Los recursos externos referenciados por rutas relativas (css / js / imágenes) no se pueden resolver y pueden faltar." }
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