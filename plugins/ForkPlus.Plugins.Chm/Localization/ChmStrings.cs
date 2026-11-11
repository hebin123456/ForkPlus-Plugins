using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Chm
{
	/// <summary>
	/// CHM 帮助文档对比插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 组织方式与 <c>ForkPlus.Plugins.Epub/Localization/EpubStrings.cs</c> 一致。
	/// </summary>
	internal static class ChmStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="ChmDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "CHM 帮助文档对比" },
			{ "zh-Hant", "CHM 說明檔對比" },
			{ "ja-JP", "CHM ヘルプ比較" },
			{ "ko-KR", "CHM 도움말 비교" },
			{ "fr-FR", "Comparaison CHM" },
			{ "de-DE", "CHM-Vergleich" },
			{ "es-ES", "Comparación CHM" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="ChmDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "CHM 帮助文档对比视图插件：认领 .chm，自行解析已编译 HTML 帮助的 ITSF / ITSP 头部与 PMGL 目录块，提供「元数据」与「目录条目」两种 diff 视图；MSCompressed 内容流用内置的纯托管 LZX 解压器解开后为条目算内容指纹，逐行标注「相同 / 已变 / 仅左 / 仅右」；另提供「预览」，并排双 WebView 真渲染两侧默认主题页。" },
			{ "zh-Hant", "CHM 說明檔對比檢視外掛：認領 .chm，自行解析已編譯 HTML 說明的 ITSF / ITSP 標頭與 PMGL 目錄區塊，提供「中繼資料」與「目錄項目」兩種 diff 檢視；MSCompressed 內容串流以內建的純受控 LZX 解壓器解開後為項目計算內容指紋，逐列標註「相同 / 已變 / 僅左 / 僅右」；另提供「預覽」，並排雙 WebView 真渲染兩側預設主題頁。" },
			{ "ja-JP", "CHM ヘルプ比較ビュープラグイン: .chm を対象に、コンパイル済み HTML ヘルプの ITSF / ITSP ヘッダーと PMGL ディレクトリブロックを独自に解析し、「メタデータ」と「ディレクトリ項目」の 2 つの diff ビューを提供します。MSCompressed の内容ストリームは内蔵のマネージド LZX デコーダーで展開し、各項目の内容フィンガープリントを算出して「同一 / 変更 / 左のみ / 右のみ」で判定します。さらに「プレビュー」では、左右の既定トピックページを並排デュアル WebView で実レンダリングします。" },
			{ "ko-KR", "CHM 도움말 비교 보기 플러그인: .chm를 대상으로 컴파일된 HTML 도움말의 ITSF / ITSP 헤더와 PMGL 디렉터리 블록을 직접 분석하여 '메타데이터'와 '디렉터리 항목' 두 가지 diff 보기를 제공합니다. MSCompressed 콘텐츠 스트림은 내장 관리형 LZX 디코더로 풀어 각 항목의 콘텐츠 지문을 계산하고 '동일 / 변경 / 왼쪽만 / 오른쪽만'으로 표시합니다. 또한 '미리 보기'에서 양쪽 기본 항목 페이지를 나란히 놓인 듀얼 WebView로 실제 렌더링합니다." },
			{ "fr-FR", "Plugin de vue de comparaison CHM : prend en charge .chm, analyse lui-même les en-têtes ITSF / ITSP et les blocs d'annuaire PMGL des fichiers d'aide HTML compilés, et fournit deux vues de comparaison « métadonnées » et « entrées d'annuaire » ; le flux de contenu MSCompressed est décompressé par un décodeur LZX managé intégré afin de calculer une empreinte du contenu de chaque entrée, avec le marquage identique / modifié / gauche seule / droite seule. Une vue « Aperçu » restitue en outre les pages de rubrique par défaut des deux côtés dans deux WebView côte à côte." },
			{ "de-DE", "CHM-Hilfedokument-Vergleichsansichts-Plugin: übernimmt .chm, analysiert selbst die ITSF-/ITSP-Kopfbereiche und PMGL-Verzeichnisblöcke kompilierter HTML-Hilfen und bietet zwei Diff-Ansichten „Metadaten“ und „Verzeichniseinträge“; der MSCompressed-Inhaltsstrom wird mit einem integrierten verwalteten LZX-Dekoder entpackt, um für jeden Eintrag einen Inhalts-Fingerprint zu berechnen – jede Zeile markiert als identisch / geändert / nur links / nur rechts. Eine Ansicht „Vorschau“ rendert zudem die Standardthemen-Seiten beider Seiten in zwei nebeneinanderliegenden WebViews." },
			{ "es-ES", "Plugin de vista de comparación de CHM: admite .chm, analiza por sí mismo las cabeceras ITSF / ITSP y los bloques de directorio PMGL de las ayudas HTML compiladas, y ofrece dos vistas de comparación «metadatos» y «entradas de directorio»; el flujo de contenido MSCompressed se descomprime con un decodificador LZX administrado integrado para calcular la huella de contenido de cada entrada, marcando cada fila como igual / modificada / solo izquierda / solo derecha. Una vista «Vista previa» además representa las páginas de tema predeterminadas de ambos lados en dos WebView en paralelo." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			// 模式名（同时用作工具条按钮文案）
			{ "Metadata", new Dictionary<string, string>
				{
					{ "zh-Hans", "元数据" },
					{ "zh-Hant", "中繼資料" },
					{ "ja-JP", "メタデータ" },
					{ "ko-KR", "메타데이터" },
					{ "fr-FR", "Métadonnées" },
					{ "de-DE", "Metadaten" },
					{ "es-ES", "Metadatos" }
				}
			},
			{ "Entries", new Dictionary<string, string>
				{
					{ "zh-Hans", "目录条目" },
					{ "zh-Hant", "目錄項目" },
					{ "ja-JP", "ディレクトリ項目" },
					{ "ko-KR", "디렉터리 항목" },
					{ "fr-FR", "Entrées" },
					{ "de-DE", "Einträge" },
					{ "es-ES", "Entradas" }
				}
			},
			{ "Preview", new Dictionary<string, string>
				{
					{ "zh-Hans", "预览" },
					{ "zh-Hant", "預覽" },
					{ "ja-JP", "プレビュー" },
					{ "ko-KR", "미리 보기" },
					{ "fr-FR", "Aperçu" },
					{ "de-DE", "Vorschau" },
					{ "es-ES", "Vista previa" }
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
			{ "Path", new Dictionary<string, string>
				{
					{ "zh-Hans", "路径" },
					{ "zh-Hant", "路徑" },
					{ "ja-JP", "パス" },
					{ "ko-KR", "경로" },
					{ "fr-FR", "Chemin" },
					{ "de-DE", "Pfad" },
					{ "es-ES", "Ruta" }
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
			// 侧存在但字节没拿到（宿主 Hex 预载失败等）：与「不存在」区分的占位。
			{ "content unavailable", new Dictionary<string, string>
				{
					{ "zh-Hans", "内容不可用" },
					{ "zh-Hant", "內容無法使用" },
					{ "ja-JP", "内容を利用できません" },
					{ "ko-KR", "콘텐츠를 사용할 수 없음" },
					{ "fr-FR", "contenu indisponible" },
					{ "de-DE", "Inhalt nicht verfügbar" },
					{ "es-ES", "contenido no disponible" }
				}
			},
			{ "One side's content is unavailable; values shown for the other side only.", new Dictionary<string, string>
				{
					{ "zh-Hans", "一侧内容不可用；仅显示另一侧的值。" },
					{ "zh-Hant", "一側內容無法使用；僅顯示另一側的值。" },
					{ "ja-JP", "一方の内容を利用できないため、他方の値のみを表示します。" },
					{ "ko-KR", "한쪽 콘텐츠를 사용할 수 없어 다른 쪽 값만 표시합니다." },
					{ "fr-FR", "Le contenu d'un côté est indisponible ; seules les valeurs de l'autre côté sont affichées." },
					{ "de-DE", "Der Inhalt einer Seite ist nicht verfügbar; es werden nur die Werte der anderen Seite angezeigt." },
					{ "es-ES", "El contenido de un lado no está disponible; solo se muestran los valores del otro lado." }
				}
			},
			{ "directory", new Dictionary<string, string>
				{
					{ "zh-Hans", "目录" },
					{ "zh-Hant", "目錄" },
					{ "ja-JP", "フォルダー" },
					{ "ko-KR", "디렉터리" },
					{ "fr-FR", "répertoire" },
					{ "de-DE", "Verzeichnis" },
					{ "es-ES", "directorio" }
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
			{ "CHM compare: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "CHM 对比：{0} / {1}" },
					{ "zh-Hant", "CHM 對比：{0} / {1}" },
					{ "ja-JP", "CHM 比較: {0} / {1}" },
					{ "ko-KR", "CHM 비교: {0} / {1}" },
					{ "fr-FR", "Comparaison CHM : {0} / {1}" },
					{ "de-DE", "CHM-Vergleich: {0} / {1}" },
					{ "es-ES", "Comparación CHM: {0} / {1}" }
				}
			},
			{ "entries: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "条目数：{0} / {1}" },
					{ "zh-Hant", "項目數：{0} / {1}" },
					{ "ja-JP", "項目数: {0} / {1}" },
					{ "ko-KR", "항목 수: {0} / {1}" },
					{ "fr-FR", "entrées : {0} / {1}" },
					{ "de-DE", "Einträge: {0} / {1}" },
					{ "es-ES", "entradas: {0} / {1}" }
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
			{ "Failed to load LFS content: {0}", new Dictionary<string, string>
				{
					{ "zh-Hans", "LFS 内容加载失败：{0}" },
					{ "zh-Hant", "LFS 內容載入失敗：{0}" },
					{ "ja-JP", "LFS コンテンツの読み込みに失敗しました：{0}" },
					{ "ko-KR", "LFS 콘텐츠를 로드하지 못했습니다: {0}" },
					{ "fr-FR", "Échec du chargement du contenu LFS : {0}" },
					{ "de-DE", "LFS-Inhalt konnte nicht geladen werden: {0}" },
					{ "es-ES", "No se pudo cargar el contenido LFS: {0}" }
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