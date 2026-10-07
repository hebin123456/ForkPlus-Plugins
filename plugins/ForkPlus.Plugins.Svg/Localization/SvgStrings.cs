using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Svg
{
	/// <summary>
	/// SVG 矢量图对比插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 组织方式与 <c>ForkPlus.Plugins.Structured/Localization/StructuredStrings.cs</c> 一致。
	/// </summary>
	internal static class SvgStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="SvgDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "SVG 矢量图对比" },
			{ "zh-Hant", "SVG 向量圖對比" },
			{ "ja-JP", "SVG ベクター比較" },
			{ "ko-KR", "SVG 벡터 비교" },
			{ "fr-FR", "Comparaison SVG" },
			{ "de-DE", "SVG-Vergleich" },
			{ "es-ES", "Comparación SVG" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="SvgDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "SVG 矢量图对比视图插件：认领 .svg，把两侧解析成元素树，既按 viewBox 等比缩放并排渲染，也按「元素路径 + 呈现属性」做结构 diff —— 新增 / 删除 / 改值一目了然。" },
			{ "zh-Hant", "SVG 向量圖對比檢視外掛：認領 .svg，將兩側解析成元素樹，既按 viewBox 等比縮放並排渲染，也按「元素路徑 + 呈現屬性」做結構 diff —— 新增 / 刪除 / 改值一目了然。" },
			{ "ja-JP", "SVG ベクター比較ビュープラグイン: .svg を対象に、両側を要素ツリーへ解析し、viewBox に従って等倍で並べて描画するとともに、要素パス + 提示属性の構造 diff（追加 / 削除 / 値変更）を表示します。" },
			{ "ko-KR", "SVG 벡터 비교 보기 플러그인: .svg를 처리하여 양쪽을 요소 트리로 분석하고, viewBox 기준으로 나란히 렌더링하며 요소 경로 + 표시 속성의 구조 diff(추가 / 삭제 / 값 변경)를 보여 줍니다." },
			{ "fr-FR", "Plugin de vue de comparaison SVG : prend en charge .svg, analyse les deux côtés en un arbre d'éléments, les rend côte à côte à l'échelle du viewBox et produit un diff structurel des éléments et attributs de présentation (ajout / suppression / modification)." },
			{ "de-DE", "Ansichts-Plugin zum SVG-Vergleich: übernimmt .svg, analysiert beide Seiten in einen Elementbaum, rendert sie maßstabsgetreu nebeneinander und zeigt einen Strukturvergleich von Elementen und Darstellungsattributen (hinzugefügt / entfernt / geändert)." },
			{ "es-ES", "Plugin de vista de comparación SVG: admite .svg, analiza ambos lados en un árbol de elementos, los representa en paralelo a escala del viewBox y muestra un diff estructural de elementos y atributos de presentación (añadido / eliminado / modificado)." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			// 模式名（同时用作工具条按钮文案）
			{ "Rendered", new Dictionary<string, string>
				{
					{ "zh-Hans", "并排渲染" },
					{ "zh-Hant", "並排渲染" },
					{ "ja-JP", "描画比較" },
					{ "ko-KR", "나란히 렌더링" },
					{ "fr-FR", "Rendu côte à côte" },
					{ "de-DE", "Rendering" },
					{ "es-ES", "Representación" }
				}
			},
			{ "Structure", new Dictionary<string, string>
				{
					{ "zh-Hans", "元素结构" },
					{ "zh-Hant", "元素結構" },
					{ "ja-JP", "要素構造" },
					{ "ko-KR", "요소 구조" },
					{ "fr-FR", "Structure" },
					{ "de-DE", "Struktur" },
					{ "es-ES", "Estructura" }
				}
			},
			// 表头
			{ "Element / attribute", new Dictionary<string, string>
				{
					{ "zh-Hans", "元素 / 属性" },
					{ "zh-Hant", "元素 / 屬性" },
					{ "ja-JP", "要素 / 属性" },
					{ "ko-KR", "요소 / 속성" },
					{ "fr-FR", "Élément / attribut" },
					{ "de-DE", "Element / Attribut" },
					{ "es-ES", "Elemento / atributo" }
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
			{ "Content unavailable", new Dictionary<string, string>
				{
					{ "zh-Hans", "内容不可用" },
					{ "zh-Hant", "內容不可用" },
					{ "ja-JP", "内容を取得できません" },
					{ "ko-KR", "내용을 사용할 수 없음" },
					{ "fr-FR", "Contenu indisponible" },
					{ "de-DE", "Inhalt nicht verfügbar" },
					{ "es-ES", "Contenido no disponible" }
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
					{ "zh-Hans", "正在分析…" },
					{ "zh-Hant", "正在分析…" },
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
			{ "SVG compare: {0} / {1} elements", new Dictionary<string, string>
				{
					{ "zh-Hans", "SVG 对比：{0} / {1} 个元素" },
					{ "zh-Hant", "SVG 對比：{0} / {1} 個元素" },
					{ "ja-JP", "SVG 比較: {0} / {1} 要素" },
					{ "ko-KR", "SVG 비교: 요소 {0} / {1}" },
					{ "fr-FR", "Comparaison SVG : {0} / {1} éléments" },
					{ "de-DE", "SVG-Vergleich: {0} / {1} Elemente" },
					{ "es-ES", "Comparación SVG: {0} / {1} elementos" }
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
			{ "Summary: {0} properties · {1} changed · {2} left only · {3} right only", new Dictionary<string, string>
				{
					{ "zh-Hans", "共 {0} 项 · {1} 已变 · {2} 仅左 · {3} 仅右" },
					{ "zh-Hant", "共 {0} 項 · {1} 已變 · {2} 僅左 · {3} 僅右" },
					{ "ja-JP", "{0} 項目 · 変更 {1} · 左のみ {2} · 右のみ {3}" },
					{ "ko-KR", "항목 {0}개 · 변경 {1} · 왼쪽만 {2} · 오른쪽만 {3}" },
					{ "fr-FR", "{0} propriétés · {1} modifiées · {2} gauche seule · {3} droite seule" },
					{ "de-DE", "{0} Eigenschaften · {1} geändert · {2} nur links · {3} nur rechts" },
					{ "es-ES", "{0} propiedades · {1} modificadas · {2} solo izquierda · {3} solo derecha" }
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
			{ "Elements: {0}  ·  viewBox {1}×{2}", new Dictionary<string, string>
				{
					{ "zh-Hans", "元素数：{0}  ·  viewBox {1}×{2}" },
					{ "zh-Hant", "元素數：{0}  ·  viewBox {1}×{2}" },
					{ "ja-JP", "要素数: {0}  ·  viewBox {1}×{2}" },
					{ "ko-KR", "요소 수: {0}  ·  viewBox {1}×{2}" },
					{ "fr-FR", "Éléments : {0}  ·  viewBox {1}×{2}" },
					{ "de-DE", "Elemente: {0}  ·  viewBox {1}×{2}" },
					{ "es-ES", "Elementos: {0}  ·  viewBox {1}×{2}" }
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
