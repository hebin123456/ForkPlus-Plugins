using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Pcap
{
	/// <summary>
	/// 网络抓包对比插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 组织方式与 <c>ForkPlus.Plugins.Subtitle/Localization/SubtitleStrings.cs</c> 一致。
	/// </summary>
	internal static class PcapStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="PcapDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "网络抓包对比" },
			{ "zh-Hant", "網路抓包對比" },
			{ "ja-JP", "パケットキャプチャ比較" },
			{ "ko-KR", "패킷 캡처 비교" },
			{ "fr-FR", "Comparaison de captures réseau" },
			{ "de-DE", "Netzwerk-Mitschnitt-Vergleich" },
			{ "es-ES", "Comparación de capturas de red" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="PcapDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "网络抓包对比视图插件：认领 .pcap / .pcapng，把两侧抓包各自聚合出统计（包数 / 字节数 / 时长 / 协议分布 / 会话 Top）与包列表，再做语义 diff，并提供统计表与包列表两种视图。" },
			{ "zh-Hant", "網路抓包對比檢視外掛：認領 .pcap / .pcapng，將兩側抓包各自聚合出統計（包數 / 位元組數 / 時長 / 協定分布 / 會談 Top）與包列表，再做語意 diff，並提供統計表與包列表兩種檢視。" },
			{ "ja-JP", "パケットキャプチャ比較ビュープラグイン: .pcap / .pcapng を対象に、両側のキャプチャから統計（パケット数 / バイト数 / 継続時間 / プロトコル分布 / トップ会話）とパケット一覧を集計し、意味的な差分を取ります。統計テーブルとパケット一覧の 2 つのビューを提供します。" },
			{ "ko-KR", "패킷 캡처 비교 보기 플러그인: .pcap / .pcapng를 처리하고 양쪽 캡처에서 통계(패킷 수 / 바이트 수 / 길이 / 프로토콜 분포 / 상위 대화)와 패킷 목록을 집계한 뒤 의미 기반 diff를 수행합니다. 통계 표와 패킷 목록 두 가지 보기를 제공합니다." },
			{ "fr-FR", "Plugin de vue de comparaison de captures réseau : prend en charge .pcap / .pcapng, agrège chaque côté en statistiques de capture (paquets / octets / durée / distribution des protocoles / conversations principales) et en liste de paquets, puis les compare sémantiquement." },
			{ "de-DE", "Ansichts-Plugin zum Vergleich von Netzwerk-Mitschnitten: übernimmt .pcap / .pcapng, aggregiert beide Seiten zu Mitschnitt-Statistiken (Pakete / Bytes / Dauer / Protokollverteilung / Top-Konversationen) und einer Paketliste und gleicht sie semantisch ab." },
			{ "es-ES", "Plugin de vista de comparación de capturas de red: admite .pcap / .pcapng, agrega cada lado en estadísticas de captura (paquetes / bytes / duración / distribución de protocolos / conversaciones principales) y una lista de paquetes, y luego las compara semánticamente." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			// 模式名（同时用作工具条按钮文案）
			{ "Statistics", new Dictionary<string, string>
				{
					{ "zh-Hans", "统计" },
					{ "zh-Hant", "統計" },
					{ "ja-JP", "統計" },
					{ "ko-KR", "통계" },
					{ "fr-FR", "Statistiques" },
					{ "de-DE", "Statistik" },
					{ "es-ES", "Estadísticas" }
				}
			},
			{ "Packets", new Dictionary<string, string>
				{
					{ "zh-Hans", "包列表" },
					{ "zh-Hant", "包列表" },
					{ "ja-JP", "パケット一覧" },
					{ "ko-KR", "패킷 목록" },
					{ "fr-FR", "Paquets" },
					{ "de-DE", "Pakete" },
					{ "es-ES", "Paquetes" }
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
			{ "Capture compare: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "抓包对比：{0} / {1}" },
					{ "zh-Hant", "抓包對比：{0} / {1}" },
					{ "ja-JP", "キャプチャ比較: {0} / {1}" },
					{ "ko-KR", "캡처 비교: {0} / {1}" },
					{ "fr-FR", "Comparaison de captures : {0} / {1}" },
					{ "de-DE", "Mitschnitt-Vergleich: {0} / {1}" },
					{ "es-ES", "Comparación de capturas: {0} / {1}" }
				}
			},
			{ "packets: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "包数：{0} / {1}" },
					{ "zh-Hant", "包數：{0} / {1}" },
					{ "ja-JP", "パケット数: {0} / {1}" },
					{ "ko-KR", "패킷 수: {0} / {1}" },
					{ "fr-FR", "paquets : {0} / {1}" },
					{ "de-DE", "Pakete: {0} / {1}" },
					{ "es-ES", "paquetes: {0} / {1}" }
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
					{ "ko-KR", "항목 {0}개 · 변경 {1} · 왼쪽만 {2} · 오른쪽만 {3}" },
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
			{ "Showing first {0} of {1} packets", new Dictionary<string, string>
				{
					{ "zh-Hans", "仅显示前 {0} / {1} 个包" },
					{ "zh-Hant", "僅顯示前 {0} / {1} 個包" },
					{ "ja-JP", "先頭 {0} / {1} パケットのみ表示" },
					{ "ko-KR", "처음 {0} / {1}개 패킷만 표시" },
					{ "fr-FR", "Affichage des {0} premiers paquets sur {1}" },
					{ "de-DE", "Es werden die ersten {0} von {1} Paketen angezeigt" },
					{ "es-ES", "Mostrando los primeros {0} de {1} paquetes" }
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
