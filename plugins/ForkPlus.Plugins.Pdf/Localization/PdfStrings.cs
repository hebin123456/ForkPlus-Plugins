using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Pdf
{
	/// <summary>
	/// PDF 插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 技术词 / 品牌名（MD5、Word / Excel / PowerPoint、PDF 等）不翻译、不进表。
	/// </summary>
	internal static class PdfStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="PdfDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "PDF 对比" },
			{ "zh-Hant", "PDF 對比" },
			{ "ja-JP", "PDF 比較" },
			{ "ko-KR", "PDF 비교" },
			{ "fr-FR", "Comparaison PDF" },
			{ "de-DE", "PDF-Vergleich" },
			{ "es-ES", "Comparación de PDF" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="PdfDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "PDF 对比视图插件：认领 .pdf，把旧 / 新两个 PDF 逐页渲染为左右两栏并排对比（基于 Docnet.Core / PDFium）。" },
			{ "zh-Hant", "PDF 對比檢視外掛：認領 .pdf，將舊 / 新兩個 PDF 逐頁算繪為左右兩欄並排對比（採用 Docnet.Core / PDFium）。" },
			{ "ja-JP", "PDF 比較ビュープラグイン: .pdf を対象に、旧 / 新の PDF をページごとに左右に並べて表示します（Docnet.Core / PDFium を使用）。" },
			{ "ko-KR", "PDF 비교 뷰 플러그인: .pdf를 처리하여 이전 / 이후 PDF를 페이지별로 나란히 렌더링합니다(Docnet.Core / PDFium 기반)." },
			{ "fr-FR", "Plugin de vue de comparaison PDF : prend en charge .pdf et affiche les PDF ancien/nouveau côte à côte, page par page (basé sur Docnet.Core / PDFium)." },
			{ "de-DE", "PDF-Vergleichsansicht-Plugin: übernimmt .pdf und rendert die alten/neuen PDFs Seite für Seite nebeneinander (basierend auf Docnet.Core / PDFium)." },
			{ "es-ES", "Plugin de vista de comparación de PDF: admite .pdf y muestra los PDF antiguo/nuevo en paralelo, página por página (basado en Docnet.Core / PDFium)." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			{ "Rendering PDF…", new Dictionary<string, string>
				{
					{ "zh-Hans", "正在渲染 PDF…" },
					{ "zh-Hant", "正在算繪 PDF…" },
					{ "ja-JP", "PDF をレンダリング中…" },
					{ "ko-KR", "PDF 렌더링 중…" },
					{ "fr-FR", "Rendu du PDF…" },
					{ "de-DE", "PDF wird gerendert…" },
					{ "es-ES", "Procesando PDF…" }
				}
			},
			{ "No pages to display", new Dictionary<string, string>
				{
					{ "zh-Hans", "无可显示的页面" },
					{ "zh-Hant", "無可顯示的頁面" },
					{ "ja-JP", "表示できるページがありません" },
					{ "ko-KR", "표시할 페이지가 없습니다" },
					{ "fr-FR", "Aucune page à afficher" },
					{ "de-DE", "Keine Seiten zum Anzeigen" },
					{ "es-ES", "No hay páginas para mostrar" }
				}
			},
			{ "Rendered {0}/{1} pages", new Dictionary<string, string>
				{
					{ "zh-Hans", "已渲染 {0}/{1} 页" },
					{ "zh-Hant", "已算繪 {0}/{1} 頁" },
					{ "ja-JP", "{0}/{1} ページをレンダリングしました" },
					{ "ko-KR", "{0}/{1}페이지 렌더링됨" },
					{ "fr-FR", "{0}/{1} pages rendues" },
					{ "de-DE", "{0}/{1} Seiten gerendert" },
					{ "es-ES", "{0}/{1} páginas procesadas" }
				}
			},
			{ "PDF compare: {0} / {1} pages", new Dictionary<string, string>
				{
					{ "zh-Hans", "PDF 对比：{0} / {1} 页" },
					{ "zh-Hant", "PDF 對比：{0} / {1} 頁" },
					{ "ja-JP", "PDF 比較: {0} / {1} ページ" },
					{ "ko-KR", "PDF 비교: {0} / {1}페이지" },
					{ "fr-FR", "Comparaison PDF : {0} / {1} pages" },
					{ "de-DE", "PDF-Vergleich: {0} / {1} Seiten" },
					{ "es-ES", "Comparación PDF: {0} / {1} páginas" }
				}
			},
			{ "Failed to render PDF", new Dictionary<string, string>
				{
					{ "zh-Hans", "PDF 渲染失败" },
					{ "zh-Hant", "PDF 算繪失敗" },
					{ "ja-JP", "PDF のレンダリングに失敗しました" },
					{ "ko-KR", "PDF 렌더링 실패" },
					{ "fr-FR", "Échec du rendu du PDF" },
					{ "de-DE", "PDF konnte nicht gerendert werden" },
					{ "es-ES", "Error al procesar el PDF" }
				}
			},
			{ "Page {0}", new Dictionary<string, string>
				{
					{ "zh-Hans", "第 {0} 页" },
					{ "zh-Hant", "第 {0} 頁" },
					{ "ja-JP", "{0} ページ" },
					{ "ko-KR", "{0}페이지" },
					{ "fr-FR", "Page {0}" },
					{ "de-DE", "Seite {0}" },
					{ "es-ES", "Página {0}" }
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
			{ "PDF content unavailable", new Dictionary<string, string>
				{
					{ "zh-Hans", "PDF 内容不可用" },
					{ "zh-Hant", "PDF 內容無法取得" },
					{ "ja-JP", "PDF の内容を利用できません" },
					{ "ko-KR", "PDF 콘텐츠를 사용할 수 없음" },
					{ "fr-FR", "Contenu PDF indisponible" },
					{ "de-DE", "PDF-Inhalt nicht verfügbar" },
					{ "es-ES", "Contenido del PDF no disponible" }
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
