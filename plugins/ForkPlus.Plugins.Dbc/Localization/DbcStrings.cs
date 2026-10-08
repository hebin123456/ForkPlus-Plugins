using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Dbc
{
	/// <summary>
	/// DBC 对比插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 组织方式与 <c>ForkPlus.Plugins.Structured/Localization/StructuredStrings.cs</c> 一致。
	/// </summary>
	internal static class DbcStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="DbcDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "DBC 对比" },
			{ "zh-Hant", "DBC 對比" },
			{ "ja-JP", "DBC 比較" },
			{ "ko-KR", "DBC 비교" },
			{ "fr-FR", "Comparaison DBC" },
			{ "de-DE", "DBC-Vergleich" },
			{ "es-ES", "Comparación DBC" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="DbcDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "DBC（CAN 数据库）对比视图插件：认领 .dbc，用 DbcParserLib 把两侧解析成同一套数据模型，对节点 / 报文 / 信号 / 环境变量做语义 diff —— 新增 / 删除 / 改值一目了然，提供键路径表与原文对照两种视图。" },
			{ "zh-Hant", "DBC（CAN 資料庫）對比檢視外掛：認領 .dbc，用 DbcParserLib 將兩側解析成同一套資料模型，對節點 / 報文 / 訊號 / 環境變數做語意 diff —— 新增 / 刪除 / 改值一目了然，提供鍵路徑表與原文對照兩種檢視。" },
			{ "ja-JP", "DBC（CAN データベース）比較ビュープラグイン: .dbc を対象に、DbcParserLib で両側を単一のデータモデルへ解析し、ノード / メッセージ / シグナル / 環境変数を意味的に diff（追加 / 削除 / 値変更）します。キーパス表と原文比較の 2 つのビューを提供します。" },
			{ "ko-KR", "DBC(CAN 데이터베이스) 비교 보기 플러그인: .dbc를 처리하고 DbcParserLib로 양쪽을 하나의 데이터 모델로 분석하여 노드 / 메시지 / 신호 / 환경 변수를 의미적으로 diff(추가 / 삭제 / 값 변경)합니다. 키 경로 표와 원문 비교 두 가지 보기를 제공합니다." },
			{ "fr-FR", "Plugin de vue de comparaison DBC (base de données CAN) : prend en charge .dbc, analyse les deux côtés avec DbcParserLib dans un même modèle de données et compare nœuds / messages / signaux / variables d'environnement de façon sémantique (ajout / suppression / modification) via un tableau de chemins de clés ou une comparaison du texte brut." },
			{ "de-DE", "Ansichts-Plugin zum Vergleich von DBC-Dateien (CAN-Datenbank): übernimmt .dbc, analysiert beide Seiten mit DbcParserLib in ein gemeinsames Datenmodell und vergleicht Knoten / Nachrichten / Signale / Umgebungsvariablen semantisch (hinzugefügt / entfernt / geändert) — als Schlüsselpfad-Tabelle oder Klartext-Gegenüberstellung." },
			{ "es-ES", "Plugin de vista de comparación de DBC (base de datos CAN): admite .dbc, analiza ambos lados con DbcParserLib en un mismo modelo de datos y compara nodos / mensajes / señales / variables de entorno de forma semántica (añadido / eliminado / modificado) mediante una tabla de rutas de clave o una comparación de texto sin formato." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			// 模式名（同时用作工具条按钮文案）
			{ "Structured", new Dictionary<string, string>
				{
					{ "zh-Hans", "结构化" },
					{ "zh-Hant", "結構化" },
					{ "ja-JP", "構造化" },
					{ "ko-KR", "구조화" },
					{ "fr-FR", "Structuré" },
					{ "de-DE", "Strukturiert" },
					{ "es-ES", "Estructurado" }
				}
			},
		{ "Raw text", new Dictionary<string, string>
			{
				{ "zh-Hans", "原文" },
				{ "zh-Hant", "原文" },
				{ "ja-JP", "原文" },
				{ "ko-KR", "원문" },
				{ "fr-FR", "Texte brut" },
				{ "de-DE", "Klartext" },
				{ "es-ES", "Texto sin formato" }
			}
		},
		{ "Only differences", new Dictionary<string, string>
			{
				{ "zh-Hans", "仅差异" },
				{ "zh-Hant", "僅差異" },
				{ "ja-JP", "差分のみ" },
				{ "ko-KR", "차이만" },
				{ "fr-FR", "Différences uniquement" },
				{ "de-DE", "Nur Unterschiede" },
				{ "es-ES", "Solo diferencias" }
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
			{ "empty", new Dictionary<string, string>
				{
					{ "zh-Hans", "空" },
					{ "zh-Hant", "空" },
					{ "ja-JP", "空" },
					{ "ko-KR", "비어 있음" },
					{ "fr-FR", "vide" },
					{ "de-DE", "leer" },
					{ "es-ES", "vacío" }
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
			{ "DBC compare: {0} messages · {1} signals", new Dictionary<string, string>
				{
					{ "zh-Hans", "DBC 对比：{0} 个报文 · {1} 个信号" },
					{ "zh-Hant", "DBC 對比：{0} 個報文 · {1} 個訊號" },
					{ "ja-JP", "DBC 比較: メッセージ {0} · シグナル {1}" },
					{ "ko-KR", "DBC 비교: 메시지 {0} · 신호 {1}" },
					{ "fr-FR", "Comparaison DBC : {0} messages · {1} signaux" },
					{ "de-DE", "DBC-Vergleich: {0} Nachrichten · {1} Signale" },
					{ "es-ES", "Comparación DBC: {0} mensajes · {1} señales" }
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
			{ "Summary: {0} keys · {1} changed · {2} left only · {3} right only", new Dictionary<string, string>
				{
					{ "zh-Hans", "共 {0} 项 · {1} 已变 · {2} 仅左 · {3} 仅右" },
					{ "zh-Hant", "共 {0} 項 · {1} 已變 · {2} 僅左 · {3} 僅右" },
					{ "ja-JP", "{0} 項目 · 変更 {1} · 左のみ {2} · 右のみ {3}" },
					{ "ko-KR", "항목 {0}개 · 변경 {1} · 왼쪽만 {2} · 오른쪽만 {3}" },
					{ "fr-FR", "{0} éléments · {1} modifiés · {2} gauche seule · {3} droite seule" },
					{ "de-DE", "{0} Einträge · {1} geändert · {2} nur links · {3} nur rechts" },
					{ "es-ES", "{0} elementos · {1} modificados · {2} solo izquierda · {3} solo derecha" }
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
