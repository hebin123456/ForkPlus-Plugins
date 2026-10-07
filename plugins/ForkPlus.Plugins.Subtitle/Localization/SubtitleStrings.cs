using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Subtitle
{
	/// <summary>
	/// 字幕 / 时间轴对比插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 组织方式与 <c>ForkPlus.Plugins.Executable/Localization/ExecutableStrings.cs</c> 一致。
	/// </summary>
	internal static class SubtitleStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="SubtitleDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "字幕 / 时间轴对比" },
			{ "zh-Hant", "字幕 / 時間軸對比" },
			{ "ja-JP", "字幕 / タイムライン比較" },
			{ "ko-KR", "자막 / 타임라인 비교" },
			{ "fr-FR", "Comparaison de sous-titres" },
			{ "de-DE", "Untertitel-Vergleich" },
			{ "es-ES", "Comparación de subtítulos" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="SubtitleDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "字幕 / 时间轴对比视图插件：认领 .srt / .vtt / .ass / .ssa / .sub，把两侧解析成同一套 cue 模型，先按文本对齐再按时间配对，逐条标注「相同 / 已变 / 仅左 / 仅右」，并提供字幕行表与等比时间轴两种视图。" },
			{ "zh-Hant", "字幕 / 時間軸對比檢視外掛：認領 .srt / .vtt / .ass / .ssa / .sub，將兩側解析成同一套 cue 模型，先按文字對齊再按時間配對，逐條標註「相同 / 已變 / 僅左 / 僅右」，並提供字幕行表與等比時間軸兩種檢視。" },
			{ "ja-JP", "字幕 / タイムライン比較ビュープラグイン: .srt / .vtt / .ass / .ssa / .sub を対象に、両側を単一の cue モデルへ解析し、まずテキストで、次に時間で対応づけて「同一 / 変更 / 左のみ / 右のみ」を判定します。字幕行テーブルと比例タイムラインの 2 つのビューを提供します。" },
			{ "ko-KR", "자막 / 타임라인 비교 보기 플러그인: .srt / .vtt / .ass / .ssa / .sub를 처리하고 양쪽을 하나의 cue 모델로 분석하여 텍스트로 먼저, 시간으로 다음으로 정렬해 '동일 / 변경 / 왼쪽만 / 오른쪽만'을 표시합니다. cue 표와 비례 타임라인 두 가지 보기를 제공합니다." },
			{ "fr-FR", "Plugin de vue de comparaison de sous-titres : prend en charge .srt / .vtt / .ass / .ssa / .sub, analyse les deux côtés dans un même modèle de cues, aligne d'abord par texte puis par temps, et marque chaque cue comme identique / modifié / gauche seule / droite seule — tableau de cues ou chronologie proportionnelle." },
			{ "de-DE", "Ansichts-Plugin zum Vergleich von Untertiteln: übernimmt .srt / .vtt / .ass / .ssa / .sub, analysiert beide Seiten in ein gemeinsames Cue-Modell und gleicht erst nach Text, dann nach Zeit ab — identisch / geändert / nur links / nur rechts — als Cue-Tabelle oder proportionale Zeitachse." },
			{ "es-ES", "Plugin de vista de comparación de subtítulos: admite .srt / .vtt / .ass / .ssa / .sub, analiza ambos lados en un mismo modelo de cues, alinea primero por texto y luego por tiempo, y marca cada cue como igual / modificado / solo izquierda / solo derecha, en tabla de cues o línea de tiempo proporcional." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			// 模式名（同时用作工具条按钮文案）
			{ "Cue list", new Dictionary<string, string>
				{
					{ "zh-Hans", "字幕行" },
					{ "zh-Hant", "字幕行" },
					{ "ja-JP", "字幕一覧" },
					{ "ko-KR", "자막 목록" },
					{ "fr-FR", "Liste des cues" },
					{ "de-DE", "Cue-Liste" },
					{ "es-ES", "Lista de cues" }
				}
			},
			{ "Timeline", new Dictionary<string, string>
				{
					{ "zh-Hans", "时间轴" },
					{ "zh-Hant", "時間軸" },
					{ "ja-JP", "タイムライン" },
					{ "ko-KR", "타임라인" },
					{ "fr-FR", "Chronologie" },
					{ "de-DE", "Zeitachse" },
					{ "es-ES", "Línea de tiempo" }
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
					{ "zh-Hans", "旧文本" },
					{ "zh-Hant", "舊文字" },
					{ "ja-JP", "旧" },
					{ "ko-KR", "이전" },
					{ "fr-FR", "Ancien" },
					{ "de-DE", "Alt" },
					{ "es-ES", "Antiguo" }
				}
			},
			{ "New", new Dictionary<string, string>
				{
					{ "zh-Hans", "新文本" },
					{ "zh-Hant", "新文字" },
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
			{ "Subtitle compare: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "字幕对比：{0} / {1}" },
					{ "zh-Hant", "字幕對比：{0} / {1}" },
					{ "ja-JP", "字幕比較: {0} / {1}" },
					{ "ko-KR", "자막 비교: {0} / {1}" },
					{ "fr-FR", "Comparaison de sous-titres : {0} / {1}" },
					{ "de-DE", "Untertitelvergleich: {0} / {1}" },
					{ "es-ES", "Comparación de subtítulos: {0} / {1}" }
				}
			},
			{ "cues: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "字幕条数：{0} / {1}" },
					{ "zh-Hant", "字幕條數：{0} / {1}" },
					{ "ja-JP", "cue 数: {0} / {1}" },
					{ "ko-KR", "cue 수: {0} / {1}" },
					{ "fr-FR", "cues : {0} / {1}" },
					{ "de-DE", "Cues: {0} / {1}" },
					{ "es-ES", "cues: {0} / {1}" }
				}
			},
			{ "Summary: {0} cues · {1} changed · {2} left only · {3} right only", new Dictionary<string, string>
				{
					{ "zh-Hans", "共 {0} 条 · {1} 已变 · {2} 仅左 · {3} 仅右" },
					{ "zh-Hant", "共 {0} 條 · {1} 已變 · {2} 僅左 · {3} 僅右" },
					{ "ja-JP", "{0} cue · 変更 {1} · 左のみ {2} · 右のみ {3}" },
					{ "ko-KR", "cue {0}개 · 변경 {1} · 왼쪽만 {2} · 오른쪽만 {3}" },
					{ "fr-FR", "{0} cues · {1} modifiés · {2} gauche seule · {3} droite seule" },
					{ "de-DE", "{0} Cues · {1} geändert · {2} nur links · {3} nur rechts" },
					{ "es-ES", "{0} cues · {1} modificados · {2} solo izquierda · {3} solo derecha" }
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
			{ "No cues", new Dictionary<string, string>
				{
					{ "zh-Hans", "没有字幕" },
					{ "zh-Hant", "沒有字幕" },
					{ "ja-JP", "字幕がありません" },
					{ "ko-KR", "자막이 없습니다" },
					{ "fr-FR", "Aucun cue" },
					{ "de-DE", "Keine Cues" },
					{ "es-ES", "Sin cues" }
				}
			},
			{ "MicroDVD frames converted at {0} fps", new Dictionary<string, string>
				{
					{ "zh-Hans", "MicroDVD 帧号按 {0} fps 换算" },
					{ "zh-Hant", "MicroDVD 影格號以 {0} fps 換算" },
					{ "ja-JP", "MicroDVD のフレーム番号は {0} fps で換算" },
					{ "ko-KR", "MicroDVD 프레임 번호는 {0} fps로 환산" },
					{ "fr-FR", "Images MicroDVD converties à {0} i/s" },
					{ "de-DE", "MicroDVD-Frames mit {0} fps umgerechnet" },
					{ "es-ES", "Fotogramas MicroDVD convertidos a {0} fps" }
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
			{ "Timeline spans {0}", new Dictionary<string, string>
				{
					{ "zh-Hans", "时间轴总长 {0}" },
					{ "zh-Hant", "時間軸總長 {0}" },
					{ "ja-JP", "タイムライン全長 {0}" },
					{ "ko-KR", "타임라인 전체 길이 {0}" },
					{ "fr-FR", "Chronologie sur {0}" },
					{ "de-DE", "Zeitachse über {0}" },
					{ "es-ES", "Línea de tiempo de {0}" }
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
