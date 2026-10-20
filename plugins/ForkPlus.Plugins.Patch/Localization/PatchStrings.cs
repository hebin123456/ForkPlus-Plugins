using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Patch
{
	/// <summary>
	/// 补丁对比插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 组织方式与 <c>ForkPlus.Plugins.Epub/Localization/EpubStrings.cs</c> 一致。
	/// </summary>
	internal static class PatchStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="PatchDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "补丁对比" },
			{ "zh-Hant", "補丁對比" },
			{ "ja-JP", "パッチ比較" },
			{ "ko-KR", "패치 비교" },
			{ "fr-FR", "Comparaison de patchs" },
			{ "de-DE", "Patch-Vergleich" },
			{ "es-ES", "Comparación de parches" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="PatchDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "补丁对比视图插件：认领 .patch / .diff，把两侧补丁按 unified / git diff 容错解析（文件头、hunk、二进制标记），按路径把文件对齐成「相同 / 已变 / 仅左 / 仅右」，提供文件级 diff 表格；另提供「预览」，并排双 WebView 真渲染两侧补丁内容（自包含 HTML，受 NavigateToString 2MB 预算约束）。" },
			{ "zh-Hant", "補丁對比檢視外掛：認領 .patch / .diff，把兩側補丁按 unified / git diff 容錯解析（檔案頭、hunk、二進位標記），按路徑把檔案對齊成「相同 / 已變 / 僅左 / 僅右」，提供檔案級 diff 表格；另提供「預覽」，並排雙 WebView 真渲染兩側補丁內容（自包含 HTML，受 NavigateToString 2MB 預算約束）。" },
			{ "ja-JP", "パッチ比較ビュープラグイン: .patch / .diff を対象に、両側のパッチを unified / git diff として寛容に解析（ファイルヘッダー、hunk、バイナリマーカー）し、パスでファイルを整列して「同一 / 変更 / 左のみ / 右のみ」に分類するファイル単位の diff テーブルを提供します。さらに「プレビュー」では、左右のパッチ内容を並排デュアル WebView で実レンダリングします（自己完結 HTML、NavigateToString 2MB 制限内）。" },
			{ "ko-KR", "패치 비교 보기 플러그인: .patch / .diff를 대상으로 양쪽 패치를 unified / git diff로 관대하게 구문 분석(파일 헤더, hunk, 바이너리 마커)하고, 경로별로 파일을 정렬해 '동일 / 변경 / 왼쪽만 / 오른쪽만'으로 분류하는 파일 단위 diff 표를 제공합니다. 또한 '미리 보기'에서 양쪽 패치 내용을 나란히 놓인 듀얼 WebView로 실제 렌더링합니다(자체 완결 HTML, NavigateToString 2MB 제한 준수)." },
			{ "fr-FR", "Plugin de vue de comparaison de patchs : prend en charge .patch / .diff, analyse avec tolérance les deux patchs comme unified / git diff (en-têtes de fichiers, hunks, marqueurs binaires), aligne les fichiers par chemin en identique / modifié / gauche seule / droite seule et fournit un tableau de diff au niveau des fichiers ; une vue « Aperçu » restitue en outre le contenu des deux patchs dans deux WebView côte à côte (HTML autonome, dans la limite NavigateToString de 2 Mo)." },
			{ "de-DE", "Patch-Vergleichsansichts-Plugin: übernimmt .patch / .diff, analysiert beide Patchs toleranterweise als unified / git diff (Dateiköpfe, Hunks, Binärmarkierungen), ordnet Dateien pfadweise zu identisch / geändert / nur links / nur rechts und bietet eine Datei-Diff-Tabelle; eine Ansicht „Vorschau“ rendert zudem den Inhalt beider Patchs in zwei nebeneinanderliegenden WebViews (eigenständiges HTML, innerhalb des NavigateToString-Limits von 2 MB)." },
			{ "es-ES", "Plugin de vista de comparación de parches: admite .patch / .diff, analiza con tolerancia ambos parches como unified / git diff (cabeceras de archivo, hunks, marcadores binarios), alinea los archivos por ruta en igual / modificado / solo izquierda / solo derecha y ofrece una tabla de diff por archivo; una vista «Vista previa» además representa el contenido de ambos parches en dos WebView en paralelo (HTML autónomo, dentro del límite de NavigateToString de 2 MB)." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			// 模式名（同时用作工具条按钮文案）
			{ "Files", new Dictionary<string, string>
				{
					{ "zh-Hans", "文件" },
					{ "zh-Hant", "檔案" },
					{ "ja-JP", "ファイル" },
					{ "ko-KR", "파일" },
					{ "fr-FR", "Fichiers" },
					{ "de-DE", "Dateien" },
					{ "es-ES", "Archivos" }
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
			{ "File", new Dictionary<string, string>
				{
					{ "zh-Hans", "文件" },
					{ "zh-Hant", "檔案" },
					{ "ja-JP", "ファイル" },
					{ "ko-KR", "파일" },
					{ "fr-FR", "Fichier" },
					{ "de-DE", "Datei" },
					{ "es-ES", "Archivo" }
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
			// 改动类型（表格单元格与预览文件头共用）
			{ "added", new Dictionary<string, string>
				{
					{ "zh-Hans", "新增" },
					{ "zh-Hant", "新增" },
					{ "ja-JP", "追加" },
					{ "ko-KR", "추가됨" },
					{ "fr-FR", "ajouté" },
					{ "de-DE", "hinzugefügt" },
					{ "es-ES", "añadido" }
				}
			},
			{ "deleted", new Dictionary<string, string>
				{
					{ "zh-Hans", "删除" },
					{ "zh-Hant", "刪除" },
					{ "ja-JP", "削除" },
					{ "ko-KR", "삭제됨" },
					{ "fr-FR", "supprimé" },
					{ "de-DE", "gelöscht" },
					{ "es-ES", "eliminado" }
				}
			},
			{ "renamed", new Dictionary<string, string>
				{
					{ "zh-Hans", "重命名" },
					{ "zh-Hant", "重新命名" },
					{ "ja-JP", "改名" },
					{ "ko-KR", "이름 변경됨" },
					{ "fr-FR", "renommé" },
					{ "de-DE", "umbenannt" },
					{ "es-ES", "renombrado" }
				}
			},
			{ "modified", new Dictionary<string, string>
				{
					{ "zh-Hans", "修改" },
					{ "zh-Hant", "修改" },
					{ "ja-JP", "変更" },
					{ "ko-KR", "수정됨" },
					{ "fr-FR", "modifié" },
					{ "de-DE", "geändert" },
					{ "es-ES", "modificado" }
				}
			},
			{ "binary file", new Dictionary<string, string>
				{
					{ "zh-Hans", "二进制文件" },
					{ "zh-Hant", "二進位檔案" },
					{ "ja-JP", "バイナリファイル" },
					{ "ko-KR", "바이너리 파일" },
					{ "fr-FR", "fichier binaire" },
					{ "de-DE", "Binärdatei" },
					{ "es-ES", "archivo binario" }
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
			{ "Patch compare: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "补丁对比：{0} / {1}" },
					{ "zh-Hant", "補丁對比：{0} / {1}" },
					{ "ja-JP", "パッチ比較: {0} / {1}" },
					{ "ko-KR", "패치 비교: {0} / {1}" },
					{ "fr-FR", "Comparaison de patchs : {0} / {1}" },
					{ "de-DE", "Patch-Vergleich: {0} / {1}" },
					{ "es-ES", "Comparación de parches: {0} / {1}" }
				}
			},
			{ "files: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "文件数：{0} / {1}" },
					{ "zh-Hant", "檔案數：{0} / {1}" },
					{ "ja-JP", "ファイル数: {0} / {1}" },
					{ "ko-KR", "파일 수: {0} / {1}" },
					{ "fr-FR", "fichiers : {0} / {1}" },
					{ "de-DE", "Dateien: {0} / {1}" },
					{ "es-ES", "archivos: {0} / {1}" }
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
			{ "Summary: {0} files · {1} changed · {2} left only · {3} right only", new Dictionary<string, string>
				{
					{ "zh-Hans", "共 {0} 个文件 · {1} 已变 · {2} 仅左 · {3} 仅右" },
					{ "zh-Hant", "共 {0} 個檔案 · {1} 已變 · {2} 僅左 · {3} 僅右" },
					{ "ja-JP", "{0} ファイル · 変更 {1} · 左のみ {2} · 右のみ {3}" },
					{ "ko-KR", "총 {0}개 파일 · 변경 {1} · 왼쪽만 {2} · 오른쪽만 {3}" },
					{ "fr-FR", "{0} fichiers · {1} modifiés · {2} gauche uniquement · {3} droite uniquement" },
					{ "de-DE", "{0} Dateien · {1} geändert · {2} nur links · {3} nur rechts" },
					{ "es-ES", "{0} archivos · {1} modificados · {2} solo izquierda · {3} solo derecha" }
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
			// 预览组装器
			{ "{0} hunks", new Dictionary<string, string>
				{
					{ "zh-Hans", "{0} 个 hunk" },
					{ "zh-Hant", "{0} 個 hunk" },
					{ "ja-JP", "hunk {0} 件" },
					{ "ko-KR", "hunk {0}개" },
					{ "fr-FR", "{0} hunks" },
					{ "de-DE", "{0} Hunks" },
					{ "es-ES", "{0} hunks" }
				}
			},
			{ "Truncated: showing first {0} files", new Dictionary<string, string>
				{
					{ "zh-Hans", "内容过长，仅显示前 {0} 个文件" },
					{ "zh-Hant", "內容過長，僅顯示前 {0} 個檔案" },
					{ "ja-JP", "内容が長すぎるため先頭 {0} ファイルのみ表示" },
					{ "ko-KR", "내용이 길어 처음 {0}개 파일만 표시" },
					{ "fr-FR", "Contenu trop long ; affichage des {0} premiers fichiers" },
					{ "de-DE", "Inhalt zu lang; es werden die ersten {0} Dateien angezeigt" },
					{ "es-ES", "Contenido demasiado largo; mostrando los primeros {0} archivos" }
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
