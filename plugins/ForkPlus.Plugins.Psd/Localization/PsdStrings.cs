using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Psd
{
	/// <summary>
	/// PSD 图层对比插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 组织方式与 <c>ForkPlus.Plugins.Subtitle/Localization/SubtitleStrings.cs</c> 一致。
	/// </summary>
	internal static class PsdStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="PsdDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "PSD 图层对比" },
			{ "zh-Hant", "PSD 圖層對比" },
			{ "ja-JP", "PSD レイヤー比較" },
			{ "ko-KR", "PSD 레이어 비교" },
			{ "fr-FR", "Comparaison PSD" },
			{ "de-DE", "PSD-Vergleich" },
			{ "es-ES", "Comparación PSD" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="PsdDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "PSD 图层对比视图插件：认领 .psd / .psb，把两侧解析成头部字段 + 图层结构模型（名称 / 矩形 / 混合模式 / 不透明度 / 可见性 / 通道），逐行标注「相同 / 已变 / 仅左 / 仅右」，并排显示内嵌缩略图。" },
			{ "zh-Hant", "PSD 圖層對比檢視外掛：認領 .psd / .psb，將兩側解析成檔頭欄位 + 圖層結構模型（名稱 / 矩形 / 混合模式 / 不透明度 / 可見性 / 通道），逐行標註「相同 / 已變 / 僅左 / 僅右」，並排顯示內嵌縮圖。" },
			{ "ja-JP", "PSD レイヤー比較ビュープラグイン: .psd / .psb を対象に、両側をヘッダー項目 + レイヤー構造モデル（名前 / 矩形 / ブレンドモード / 不透明度 / 可視性 / チャンネル）へ解析し、「同一 / 変更 / 左のみ / 右のみ」を逐行標記します。埋め込みサムネイルも並べて表示します。" },
			{ "ko-KR", "PSD 레이어 비교 보기 플러그인: .psd / .psb를 처리하고 양쪽을 헤더 필드 + 레이어 구조 모델(이름 / 사각형 / 블렌드 모드 / 불투명도 / 표시 여부 / 채널)로 분석하여 '동일 / 변경 / 왼쪽만 / 오른쪽만'을 표시하고 포함된 썸네일을 나란히 보여줍니다." },
			{ "fr-FR", "Plugin de vue de comparaison PSD : prend en charge .psd / .psb, analyse les deux côtés en champs d'en-tête + structure de calques (nom, rectangle, mode de fusion, opacité, visibilité, canaux), marque chaque entrée comme identique / modifiée / gauche seule / droite seule, et affiche les vignettes intégrées côte à côte." },
			{ "de-DE", "PSD-Vergleichsansichts-Plugin: übernimmt .psd / .psb, analysiert beide Seiten in Kopfzeilenfelder + Ebenenstruktur (Name, Rechteck, Füllmodus, Deckkraft, Sichtbarkeit, Kanäle), markiert jeden Eintrag als identisch / geändert / nur links / nur rechts und zeigt eingebettete Vorschaubilder nebeneinander." },
			{ "es-ES", "Plugin de vista de comparación PSD: admite .psd / .psb, analiza ambos lados en campos de cabecera + estructura de capas (nombre, rectángulo, modo de fusión, opacidad, visibilidad, canales), marca cada entrada como igual / modificada / solo izquierda / solo derecha y muestra las miniaturas incrustadas lado a lado." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			// 模式名（同时用作工具条按钮文案）
			{ "Preview", new Dictionary<string, string>
				{
					{ "zh-Hans", "缩略图" },
					{ "zh-Hant", "縮圖" },
					{ "ja-JP", "プレビュー" },
					{ "ko-KR", "미리 보기" },
					{ "fr-FR", "Aperçu" },
					{ "de-DE", "Vorschau" },
					{ "es-ES", "Vista previa" }
				}
			},
			{ "Layers", new Dictionary<string, string>
				{
					{ "zh-Hans", "图层" },
					{ "zh-Hant", "圖層" },
					{ "ja-JP", "レイヤー" },
					{ "ko-KR", "레이어" },
					{ "fr-FR", "Calques" },
					{ "de-DE", "Ebenen" },
					{ "es-ES", "Capas" }
				}
			},
			{ "Header", new Dictionary<string, string>
				{
					{ "zh-Hans", "文件头" },
					{ "zh-Hant", "檔頭" },
					{ "ja-JP", "ヘッダー" },
					{ "ko-KR", "헤더" },
					{ "fr-FR", "En-tête" },
					{ "de-DE", "Header" },
					{ "es-ES", "Cabecera" }
				}
			},
			// 表头
			{ "Layer", new Dictionary<string, string>
				{
					{ "zh-Hans", "图层" },
					{ "zh-Hant", "圖層" },
					{ "ja-JP", "レイヤー" },
					{ "ko-KR", "레이어" },
					{ "fr-FR", "Calque" },
					{ "de-DE", "Ebene" },
					{ "es-ES", "Capa" }
				}
			},
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
			// 图层属性摘要用词
			{ "visible", new Dictionary<string, string>
				{
					{ "zh-Hans", "可见" },
					{ "zh-Hant", "可見" },
					{ "ja-JP", "表示" },
					{ "ko-KR", "표시" },
					{ "fr-FR", "visible" },
					{ "de-DE", "sichtbar" },
					{ "es-ES", "visible" }
				}
			},
			{ "hidden", new Dictionary<string, string>
				{
					{ "zh-Hans", "已隐藏" },
					{ "zh-Hant", "已隱藏" },
					{ "ja-JP", "非表示" },
					{ "ko-KR", "숨김" },
					{ "fr-FR", "masqué" },
					{ "de-DE", "ausgeblendet" },
					{ "es-ES", "oculto" }
				}
			},
			// 其它文案
			{ "No embedded thumbnail", new Dictionary<string, string>
				{
					{ "zh-Hans", "没有内嵌缩略图" },
					{ "zh-Hant", "沒有內嵌縮圖" },
					{ "ja-JP", "埋め込みサムネイルがありません" },
					{ "ko-KR", "포함된 썸네일이 없습니다" },
					{ "fr-FR", "Aucune vignette intégrée" },
					{ "de-DE", "Kein eingebettetes Vorschaubild" },
					{ "es-ES", "Sin miniatura incrustada" }
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
			{ "PSD compare: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "PSD 对比：{0} / {1}" },
					{ "zh-Hant", "PSD 對比：{0} / {1}" },
					{ "ja-JP", "PSD 比較: {0} / {1}" },
					{ "ko-KR", "PSD 비교: {0} / {1}" },
					{ "fr-FR", "Comparaison PSD : {0} / {1}" },
					{ "de-DE", "PSD-Vergleich: {0} / {1}" },
					{ "es-ES", "Comparación PSD: {0} / {1}" }
				}
			},
			{ "layers: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "图层数：{0} / {1}" },
					{ "zh-Hant", "圖層數：{0} / {1}" },
					{ "ja-JP", "レイヤー数: {0} / {1}" },
					{ "ko-KR", "레이어 수: {0} / {1}" },
					{ "fr-FR", "calques : {0} / {1}" },
					{ "de-DE", "Ebenen: {0} / {1}" },
					{ "es-ES", "capas: {0} / {1}" }
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
					{ "ko-KR", "{0}개 항목 · 변경 {1} · 왼쪽만 {2} · 오른쪽만 {3}" },
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
