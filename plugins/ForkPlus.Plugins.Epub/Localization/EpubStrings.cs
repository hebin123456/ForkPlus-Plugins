using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Epub
{
	/// <summary>
	/// EPUB 电子书对比插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 组织方式与 <c>ForkPlus.Plugins.Subtitle/Localization/SubtitleStrings.cs</c> 一致。
	/// </summary>
	internal static class EpubStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="EpubDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "EPUB 电子书对比" },
			{ "zh-Hant", "EPUB 電子書對比" },
			{ "ja-JP", "EPUB 電子書籍比較" },
			{ "ko-KR", "EPUB 전자책 비교" },
			{ "fr-FR", "Comparaison EPUB" },
			{ "de-DE", "EPUB-Vergleich" },
			{ "es-ES", "Comparación EPUB" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="EpubDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "EPUB 电子书对比视图插件：认领 .epub，用共享框架内置的 ZipArchive 解开 ZIP 容器，解析 container.xml → OPF（元数据 / manifest / spine），提供「元数据」与「章节」两种 diff 视图，逐行标注「相同 / 已变 / 仅左 / 仅右」。" },
			{ "zh-Hant", "EPUB 電子書對比檢視外掛：認領 .epub，以共享框架內建的 ZipArchive 解開 ZIP 容器，解析 container.xml → OPF（元資料 / manifest / spine），提供「元資料」與「章節」兩種 diff 檢視，逐行標註「相同 / 已變 / 僅左 / 僅右」。" },
			{ "ja-JP", "EPUB 電子書籍比較ビュープラグイン: .epub を対象に、共有フレームワーク内蔵の ZipArchive で ZIP コンテナを展開し、container.xml → OPF（メタデータ / manifest / spine）を解析して「メタデータ」と「章」の 2 つの diff ビューを提供します。各行を「同一 / 変更 / 左のみ / 右のみ」で判定します。" },
			{ "ko-KR", "EPUB 전자책 비교 보기 플러그인: .epub를 처리하고 공유 프레임워크 내장 ZipArchive로 ZIP 컨테이너를 풀어 container.xml → OPF(메타데이터 / manifest / spine)를 분석하며 '메타데이터'와 '챕터' 두 가지 diff 보기를 제공합니다. 각 행을 '동일 / 변경 / 왼쪽만 / 오른쪽만'으로 표시합니다." },
			{ "fr-FR", "Plugin de vue de comparaison EPUB : prend en charge .epub, ouvre le conteneur ZIP via le ZipArchive intégré du framework partagé, analyse container.xml → OPF (métadonnées / manifest / spine) et fournit deux vues de comparaison « métadonnées » et « chapitres », en marquant chaque ligne identique / modifiée / gauche seule / droite seule." },
			{ "de-DE", "EPUB-E-Book-Vergleichsansichts-Plugin: übernimmt .epub, öffnet den ZIP-Container mit der im gemeinsam genutzten Framework eingebauten ZipArchive-Klasse, analysiert container.xml → OPF (Metadaten / Manifest / Spine) und bietet zwei Diff-Ansichten „Metadaten“ und „Kapitel“, jede Zeile markiert als identisch / geändert / nur links / nur rechts." },
			{ "es-ES", "Plugin de vista de comparación de EPUB: admite .epub, abre el contenedor ZIP con el ZipArchive integrado del marco compartido, analiza container.xml → OPF (metadatos / manifest / spine) y ofrece dos vistas de comparación «metadatos» y «capítulos», marcando cada fila como igual / modificada / solo izquierda / solo derecha." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			// 模式名（同时用作工具条按钮文案）
			{ "Metadata", new Dictionary<string, string>
				{
					{ "zh-Hans", "元数据" },
					{ "zh-Hant", "元資料" },
					{ "ja-JP", "メタデータ" },
					{ "ko-KR", "메타데이터" },
					{ "fr-FR", "Métadonnées" },
					{ "de-DE", "Metadaten" },
					{ "es-ES", "Metadatos" }
				}
			},
			{ "Chapters", new Dictionary<string, string>
				{
					{ "zh-Hans", "章节" },
					{ "zh-Hant", "章節" },
					{ "ja-JP", "章" },
					{ "ko-KR", "챕터" },
					{ "fr-FR", "Chapitres" },
					{ "de-DE", "Kapitel" },
					{ "es-ES", "Capítulos" }
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
			{ "#", new Dictionary<string, string>
				{
					{ "zh-Hans", "序号" },
					{ "zh-Hant", "序號" },
					{ "ja-JP", "番号" },
					{ "ko-KR", "번호" },
					{ "fr-FR", "N°" },
					{ "de-DE", "Nr." },
					{ "es-ES", "N.º" }
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
			{ "EPUB compare: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "EPUB 对比：{0} / {1}" },
					{ "zh-Hant", "EPUB 對比：{0} / {1}" },
					{ "ja-JP", "EPUB 比較: {0} / {1}" },
					{ "ko-KR", "EPUB 비교: {0} / {1}" },
					{ "fr-FR", "Comparaison EPUB : {0} / {1}" },
					{ "de-DE", "EPUB-Vergleich: {0} / {1}" },
					{ "es-ES", "Comparación EPUB: {0} / {1}" }
				}
			},
			{ "chapters: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "章节数：{0} / {1}" },
					{ "zh-Hant", "章節數：{0} / {1}" },
					{ "ja-JP", "章数: {0} / {1}" },
					{ "ko-KR", "장 수: {0} / {1}" },
					{ "fr-FR", "chapitres : {0} / {1}" },
					{ "de-DE", "Kapitel: {0} / {1}" },
					{ "es-ES", "capítulos: {0} / {1}" }
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
			// 章节单元格：「第 {0} 章 · 标题（href 文件名 · 体积）」；无标题时标题回退 href 文件名。
			{ "Chapter {0} · {1} ({2} · {3})", new Dictionary<string, string>
				{
					{ "zh-Hans", "第 {0} 章 · {1}（{2} · {3}）" },
					{ "zh-Hant", "第 {0} 章 · {1}（{2} · {3}）" },
					{ "ja-JP", "第 {0} 章 · {1}（{2} · {3}）" },
					{ "ko-KR", "{0}장 · {1} ({2} · {3})" },
					{ "fr-FR", "Chapitre {0} · {1} ({2} · {3})" },
					{ "de-DE", "Kapitel {0} · {1} ({2} · {3})" },
					{ "es-ES", "Capítulo {0} · {1} ({2} · {3})" }
				}
			},
			// spine itemref linear="no" 的章节标注
			{ "non-linear", new Dictionary<string, string>
				{
					{ "zh-Hans", "非阅读顺序" },
					{ "zh-Hant", "非閱讀順序" },
					{ "ja-JP", "非線形" },
					{ "ko-KR", "비선형" },
					{ "fr-FR", "non linéaire" },
					{ "de-DE", "nicht-linear" },
					{ "es-ES", "no lineal" }
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
