using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Midi
{
	/// <summary>
	/// MIDI 对比插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 组织方式与 <c>ForkPlus.Plugins.Subtitle/Localization/SubtitleStrings.cs</c> 一致。
	/// </summary>
	internal static class MidiStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="MidiDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "MIDI 对比" },
			{ "zh-Hant", "MIDI 對比" },
			{ "ja-JP", "MIDI 比較" },
			{ "ko-KR", "MIDI 비교" },
			{ "fr-FR", "Comparaison MIDI" },
			{ "de-DE", "MIDI-Vergleich" },
			{ "es-ES", "Comparación MIDI" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="MidiDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "MIDI 对比视图插件：认领 .mid / .midi，把两侧解析成同一套音符模型，先按（通道, 音高, 起始毫秒）配对再判差异，逐个标注「相同 / 已变 / 仅左 / 仅右」，并提供音符表与钢琴卷帘时间轴两种视图。" },
			{ "zh-Hant", "MIDI 對比檢視外掛：認領 .mid / .midi，將兩側解析成同一套音符模型，先按（通道, 音高, 起始毫秒）配對再判差異，逐個標註「相同 / 已變 / 僅左 / 僅右」，並提供音符表與鋼琴捲簾時間軸兩種檢視。" },
			{ "ja-JP", "MIDI 比較ビュープラグイン: .mid / .midi を対象に、両側を単一のノートモデルへ解析し、（チャンネル, 音高, 開始ミリ秒）で対応づけてから差異を判定し、「同一 / 変更 / 左のみ / 右のみ」をマークします。ノート一覧とピアノロール・タイムラインの 2 つのビューを提供します。" },
			{ "ko-KR", "MIDI 비교 보기 플러그인: .mid / .midi를 처리하고 양쪽을 하나의 노트 모델로 분석하여 (채널, 음높이, 시작 밀리초)로 짝을 맺은 뒤 차이를 판정해 '동일 / 변경 / 왼쪽만 / 오른쪽만'으로 표시합니다. 노트 표와 피아노 롤 타임라인 두 가지 보기를 제공합니다." },
			{ "fr-FR", "Plugin de vue de comparaison MIDI : prend en charge .mid / .midi, analyse les deux côtés dans un même modèle de notes, apparie les notes par canal, hauteur et temps de début, puis marque chacune comme identique / modifiée / gauche uniquement / droite uniquement — tableau de notes ou chronologie en rouleau de piano." },
			{ "de-DE", "Ansichts-Plugin zum Vergleich von MIDI-Dateien: übernimmt .mid / .midi, analysiert beide Seiten in ein gemeinsames Notenmodell, paart Noten nach Kanal, Tonhöhe und Startzeit und markiert jede Note als identisch / geändert / nur links / nur rechts — als Notentabelle oder Piano-Roll-Zeitachse." },
			{ "es-ES", "Plugin de vista de comparación MIDI: admite .mid / .midi, analiza ambos lados en un mismo modelo de notas, empareja las notas por canal, altura y tiempo de inicio, y marca cada nota como igual / modificada / solo izquierda / solo derecha, en tabla de notas o línea de tiempo de rodillo de piano." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			// 模式名（同时用作工具条按钮文案）
			{ "Notes", new Dictionary<string, string>
				{
					{ "zh-Hans", "音符表" },
					{ "zh-Hant", "音符表" },
					{ "ja-JP", "ノート一覧" },
					{ "ko-KR", "노트 표" },
					{ "fr-FR", "Tableau des notes" },
					{ "de-DE", "Notentabelle" },
					{ "es-ES", "Tabla de notas" }
				}
			},
			{ "Piano roll", new Dictionary<string, string>
				{
					{ "zh-Hans", "钢琴卷帘" },
					{ "zh-Hant", "鋼琴捲簾" },
					{ "ja-JP", "ピアノロール" },
					{ "ko-KR", "피아노 롤" },
					{ "fr-FR", "Rouleau de piano" },
					{ "de-DE", "Piano-Roll" },
					{ "es-ES", "Rodillo de piano" }
				}
			},
			// 表头
			{ "Old time", new Dictionary<string, string>
				{
					{ "zh-Hans", "旧时间" },
					{ "zh-Hant", "舊時間" },
					{ "ja-JP", "旧の時刻" },
					{ "ko-KR", "이전 시간" },
					{ "fr-FR", "Temps ancien" },
					{ "de-DE", "Alte Zeit" },
					{ "es-ES", "Tiempo antiguo" }
				}
			},
			{ "New time", new Dictionary<string, string>
				{
					{ "zh-Hans", "新时间" },
					{ "zh-Hant", "新時間" },
					{ "ja-JP", "新の時刻" },
					{ "ko-KR", "이후 시간" },
					{ "fr-FR", "Nouveau temps" },
					{ "de-DE", "Neue Zeit" },
					{ "es-ES", "Tiempo nuevo" }
				}
			},
			{ "Old", new Dictionary<string, string>
				{
					{ "zh-Hans", "旧音符" },
					{ "zh-Hant", "舊音符" },
					{ "ja-JP", "旧" },
					{ "ko-KR", "이전" },
					{ "fr-FR", "Ancien" },
					{ "de-DE", "Alt" },
					{ "es-ES", "Antiguo" }
				}
			},
			{ "New", new Dictionary<string, string>
				{
					{ "zh-Hans", "新音符" },
					{ "zh-Hant", "新音符" },
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
			{ "MIDI compare: format {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "MIDI 对比：格式 {0} / {1}" },
					{ "zh-Hant", "MIDI 對比：格式 {0} / {1}" },
					{ "ja-JP", "MIDI 比較: フォーマット {0} / {1}" },
					{ "ko-KR", "MIDI 비교: 형식 {0} / {1}" },
					{ "fr-FR", "Comparaison MIDI : format {0} / {1}" },
					{ "de-DE", "MIDI-Vergleich: Format {0} / {1}" },
					{ "es-ES", "Comparación MIDI: formato {0} / {1}" }
				}
			},
			{ "tracks: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "音轨数：{0} / {1}" },
					{ "zh-Hant", "音軌數：{0} / {1}" },
					{ "ja-JP", "トラック数: {0} / {1}" },
					{ "ko-KR", "트랙 수: {0} / {1}" },
					{ "fr-FR", "Pistes : {0} / {1}" },
					{ "de-DE", "Spuren: {0} / {1}" },
					{ "es-ES", "Pistas: {0} / {1}" }
				}
			},
			{ "notes: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "音符数：{0} / {1}" },
					{ "zh-Hant", "音符數：{0} / {1}" },
					{ "ja-JP", "ノート数: {0} / {1}" },
					{ "ko-KR", "노트 수: {0} / {1}" },
					{ "fr-FR", "Notes : {0} / {1}" },
					{ "de-DE", "Noten: {0} / {1}" },
					{ "es-ES", "Notas: {0} / {1}" }
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
			{ "Summary: {0} notes · {1} changed · {2} left only · {3} right only", new Dictionary<string, string>
				{
					{ "zh-Hans", "共 {0} 个音符 · {1} 已变 · {2} 仅左 · {3} 仅右" },
					{ "zh-Hant", "共 {0} 個音符 · {1} 已變 · {2} 僅左 · {3} 僅右" },
					{ "ja-JP", "ノート {0} 個 · 変更 {1} · 左のみ {2} · 右のみ {3}" },
					{ "ko-KR", "노트 {0}개 · 변경 {1} · 왼쪽만 {2} · 오른쪽만 {3}" },
					{ "fr-FR", "{0} notes · {1} modifiées · {2} gauche uniquement · {3} droite uniquement" },
					{ "de-DE", "{0} Noten · {1} geändert · {2} nur links · {3} nur rechts" },
					{ "es-ES", "{0} notas · {1} modificadas · {2} solo izquierda · {3} solo derecha" }
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
			{ "No notes", new Dictionary<string, string>
				{
					{ "zh-Hans", "没有音符" },
					{ "zh-Hant", "沒有音符" },
					{ "ja-JP", "ノートがありません" },
					{ "ko-KR", "노트가 없습니다" },
					{ "fr-FR", "Aucune note" },
					{ "de-DE", "Keine Noten" },
					{ "es-ES", "Sin notas" }
				}
			},
			{ "Notes truncated to {0} per side", new Dictionary<string, string>
				{
					{ "zh-Hans", "每侧音符已截断为 {0} 个" },
					{ "zh-Hant", "每側音符已截斷為 {0} 個" },
					{ "ja-JP", "片側あたり {0} 個でノートを打ち切りました" },
					{ "ko-KR", "측면당 노트를 {0}개로 잘랐습니다" },
					{ "fr-FR", "Notes tronquées à {0} par côté" },
					{ "de-DE", "Noten auf {0} pro Seite gekürzt" },
					{ "es-ES", "Notas truncadas a {0} por lado" }
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
