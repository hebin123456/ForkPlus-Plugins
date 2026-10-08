using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Sqlite
{
	/// <summary>
	/// SQLite 数据库对比插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 组织方式与 <c>ForkPlus.Plugins.Epub/Localization/EpubStrings.cs</c> 一致。
	/// </summary>
	internal static class SqliteStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="SqliteDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "SQLite 数据库对比" },
			{ "zh-Hant", "SQLite 資料庫對比" },
			{ "ja-JP", "SQLite データベース比較" },
			{ "ko-KR", "SQLite 데이터베이스 비교" },
			{ "fr-FR", "Comparaison SQLite" },
			{ "de-DE", "SQLite-Vergleich" },
			{ "es-ES", "Comparación SQLite" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="SqliteDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "SQLite 数据库对比视图插件：认领 .db / .sqlite / .sqlite3，纯托管解析 SQLite 文件格式（100 字节文件头 + 各页 B 树 + 记录序列类型），不依赖任何原生驱动，提供「表结构」与「数据」两种 diff 视图，逐行标注「相同 / 已变 / 仅左 / 仅右」。" },
			{ "zh-Hant", "SQLite 資料庫對比檢視外掛：認領 .db / .sqlite / .sqlite3，純託管解析 SQLite 檔案格式（100 位元組檔頭 + 各頁 B 樹 + 記錄序列型別），不依賴任何原生驅動，提供「表結構」與「資料」兩種 diff 檢視，逐行標註「相同 / 已變 / 僅左 / 僅右」。" },
			{ "ja-JP", "SQLite データベース比較ビュープラグイン: .db / .sqlite / .sqlite3 を対象に、SQLite ファイル形式（100 バイトのヘッダー + B ツリーページ + レコードのシリアル型）を純マネージドで解析し、ネイティブドライバーに依存せず「スキーマ」と「データ」の 2 つの diff ビューを提供します。" },
			{ "ko-KR", "SQLite 데이터베이스 비교 보기 플러그인: .db / .sqlite / .sqlite3를 처리하고 SQLite 파일 형식(100바이트 헤더 + B-트리 페이지 + 레코드 직렬 형식)을 순수 관리 코드로 분석하며 네이티브 드라이버 없이 '스키마'와 '데이터' 두 가지 diff 보기를 제공합니다." },
			{ "fr-FR", "Plugin de vue de comparaison SQLite : prend en charge .db / .sqlite / .sqlite3, analyse le format de fichier SQLite (en-tête de 100 octets + pages B-tree + types de sérialisation des enregistrements) en pur managé, sans pilote natif, et fournit deux vues de comparaison « schéma » et « données »." },
			{ "de-DE", "SQLite-Datenbankvergleichs-Plugin: übernimmt .db / .sqlite / .sqlite3, analysiert das SQLite-Dateiformat (100-Byte-Header + B-Tree-Seiten + Datensatz-Serialtypen) vollständig in verwaltetem Code ohne nativen Treiber und bietet zwei Diff-Ansichten „Schema“ und „Daten“." },
			{ "es-ES", "Plugin de vista de comparación de SQLite: admite .db / .sqlite / .sqlite3, analiza el formato de archivo SQLite (cabecera de 100 bytes + páginas B-tree + tipos de serialización de registros) en código totalmente administrado, sin controlador nativo, y ofrece dos vistas de comparación «esquema» y «datos»." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			// 模式名（同时用作工具条按钮文案）
			{ "Schema", new Dictionary<string, string>
				{
					{ "zh-Hans", "表结构" },
					{ "zh-Hant", "表結構" },
					{ "ja-JP", "スキーマ" },
					{ "ko-KR", "스키마" },
					{ "fr-FR", "Schéma" },
					{ "de-DE", "Schema" },
					{ "es-ES", "Esquema" }
				}
			},
			{ "Data", new Dictionary<string, string>
				{
					{ "zh-Hans", "数据" },
					{ "zh-Hant", "資料" },
					{ "ja-JP", "データ" },
					{ "ko-KR", "데이터" },
					{ "fr-FR", "Données" },
					{ "de-DE", "Daten" },
					{ "es-ES", "Datos" }
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
			{ "SQLite compare: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "SQLite 对比：{0} / {1}" },
					{ "zh-Hant", "SQLite 對比：{0} / {1}" },
					{ "ja-JP", "SQLite 比較: {0} / {1}" },
					{ "ko-KR", "SQLite 비교: {0} / {1}" },
					{ "fr-FR", "Comparaison SQLite : {0} / {1}" },
					{ "de-DE", "SQLite-Vergleich: {0} / {1}" },
					{ "es-ES", "Comparación SQLite: {0} / {1}" }
				}
			},
			{ "tables: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "表数：{0} / {1}" },
					{ "zh-Hant", "表數：{0} / {1}" },
					{ "ja-JP", "テーブル数: {0} / {1}" },
					{ "ko-KR", "테이블 수: {0} / {1}" },
					{ "fr-FR", "tables : {0} / {1}" },
					{ "de-DE", "Tabellen: {0} / {1}" },
					{ "es-ES", "tablas: {0} / {1}" }
				}
			},
			{ "rows: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "行数：{0} / {1}" },
					{ "zh-Hant", "列數：{0} / {1}" },
					{ "ja-JP", "行数: {0} / {1}" },
					{ "ko-KR", "행 수: {0} / {1}" },
					{ "fr-FR", "lignes : {0} / {1}" },
					{ "de-DE", "Zeilen: {0} / {1}" },
					{ "es-ES", "filas: {0} / {1}" }
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
			// 数据读取被截断的提示（超过单表行数 / 表数阈值）。
			{ "Data truncated: only the first tables and rows are shown.", new Dictionary<string, string>
				{
					{ "zh-Hans", "数据已截断：仅显示前若干张表与行。" },
					{ "zh-Hant", "資料已截斷：僅顯示前若干張表與列。" },
					{ "ja-JP", "データを切り詰めました。先頭のテーブルと行のみ表示しています。" },
					{ "ko-KR", "데이터가 잘렸습니다. 앞부분의 테이블과 행만 표시합니다." },
					{ "fr-FR", "Données tronquées : seules les premières tables et lignes sont affichées." },
					{ "de-DE", "Daten abgeschnitten: Es werden nur die ersten Tabellen und Zeilen angezeigt." },
					{ "es-ES", "Datos truncados: solo se muestran las primeras tablas y filas." }
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