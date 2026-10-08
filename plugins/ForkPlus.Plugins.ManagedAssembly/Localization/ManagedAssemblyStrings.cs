using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.ManagedAssembly
{
	/// <summary>
	/// 托管程序集对比插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 组织方式与 <c>ForkPlus.Plugins.Sqlite/Localization/SqliteStrings.cs</c> 一致。
	/// </summary>
	internal static class ManagedAssemblyStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="ManagedAssemblyDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "托管程序集对比" },
			{ "zh-Hant", "受控組件對比" },
			{ "ja-JP", "マネージド アセンブリ比較" },
			{ "ko-KR", "관리형 어셈블리 비교" },
			{ "fr-FR", "Comparaison d'assembly managé" },
			{ "de-DE", "Managed-Assembly-Vergleich" },
			{ "es-ES", "Comparación de ensamblado administrado" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="ManagedAssemblyDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "托管程序集（.NET）对比视图插件：认领 .dll / .exe，用共享框架内置的 System.Reflection.Metadata 读取 ECMA-335 元数据，提供「程序集标识」「类型 → 方法 / 字段」「AssemblyRef 引用」三种 diff 视图。" },
			{ "zh-Hant", "受控組件（.NET）對比檢視外掛：認領 .dll / .exe，用共享框架內建的 System.Reflection.Metadata 讀取 ECMA-335 中繼資料，提供「組件識別」「型別 → 方法 / 欄位」「AssemblyRef 參考」三種 diff 檢視。" },
			{ "ja-JP", "マネージド アセンブリ (.NET) 比較ビュープラグイン: .dll / .exe を対象に、共有フレームワーク内蔵の System.Reflection.Metadata で ECMA-335 メタデータを読み取り、「アセンブリ ID」「型 → メソッド / フィールド」「AssemblyRef 参照」の 3 つの diff ビューを提供します。" },
			{ "ko-KR", "관리형 어셈블리(.NET) 비교 보기 플러그인: .dll / .exe를 처리하고 공유 프레임워크 내장 System.Reflection.Metadata로 ECMA-335 메타데이터를 읽어 '어셈블리 ID', '형식 → 메서드 / 필드', 'AssemblyRef 참조' 세 가지 diff 보기를 제공합니다." },
			{ "fr-FR", "Plugin de vue de comparaison d'assembly managé : prend en charge .dll / .exe, lit les métadonnées ECMA-335 avec System.Reflection.Metadata intégré au framework partagé, et fournit trois vues de comparaison « identité », « types → méthodes / champs » et « références AssemblyRef »." },
			{ "de-DE", "Managed-Assembly-Vergleichs-Plugin: übernimmt .dll / .exe, liest ECMA-335-Metadaten mit dem im Shared Framework enthaltenen System.Reflection.Metadata und bietet drei Diff-Ansichten „Identität“, „Typen → Methoden / Felder“ und „AssemblyRef-Verweise“." },
			{ "es-ES", "Plugin de vista de comparación de ensamblado administrado: admite .dll / .exe, lee metadatos ECMA-335 con System.Reflection.Metadata incluido en el framework compartido y ofrece tres vistas de comparación «identidad», «tipos → métodos / campos» y «referencias AssemblyRef»." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			// 模式名（同时用作工具条按钮文案）
			{ "Identity", new Dictionary<string, string>
				{
					{ "zh-Hans", "程序集标识" },
					{ "zh-Hant", "組件識別" },
					{ "ja-JP", "アセンブリ ID" },
					{ "ko-KR", "어셈블리 ID" },
					{ "fr-FR", "Identité" },
					{ "de-DE", "Identität" },
					{ "es-ES", "Identidad" }
				}
			},
			{ "Types", new Dictionary<string, string>
				{
					{ "zh-Hans", "类型" },
					{ "zh-Hant", "型別" },
					{ "ja-JP", "型" },
					{ "ko-KR", "형식" },
					{ "fr-FR", "Types" },
					{ "de-DE", "Typen" },
					{ "es-ES", "Tipos" }
				}
			},
			{ "References", new Dictionary<string, string>
				{
					{ "zh-Hans", "引用" },
					{ "zh-Hant", "參考" },
					{ "ja-JP", "参照" },
					{ "ko-KR", "참조" },
					{ "fr-FR", "Références" },
					{ "de-DE", "Verweise" },
					{ "es-ES", "Referencias" }
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
			{ "Managed assembly compare: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "托管程序集对比：{0} / {1}" },
					{ "zh-Hant", "受控組件對比：{0} / {1}" },
					{ "ja-JP", "マネージド アセンブリ比較: {0} / {1}" },
					{ "ko-KR", "관리형 어셈블리 비교: {0} / {1}" },
					{ "fr-FR", "Comparaison d'assembly managé : {0} / {1}" },
					{ "de-DE", "Managed-Assembly-Vergleich: {0} / {1}" },
					{ "es-ES", "Comparación de ensamblado administrado: {0} / {1}" }
				}
			},
			{ "types: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "类型数：{0} / {1}" },
					{ "zh-Hant", "型別數：{0} / {1}" },
					{ "ja-JP", "型数: {0} / {1}" },
					{ "ko-KR", "형식 수: {0} / {1}" },
					{ "fr-FR", "types : {0} / {1}" },
					{ "de-DE", "Typen: {0} / {1}" },
					{ "es-ES", "tipos: {0} / {1}" }
				}
			},
			{ "members: {0} / {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "成员数：{0} / {1}" },
					{ "zh-Hant", "成員數：{0} / {1}" },
					{ "ja-JP", "メンバー数: {0} / {1}" },
					{ "ko-KR", "멤버 수: {0} / {1}" },
					{ "fr-FR", "membres : {0} / {1}" },
					{ "de-DE", "Member: {0} / {1}" },
					{ "es-ES", "miembros: {0} / {1}" }
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
			{ "Metadata truncated: only the first types / members / references are shown.", new Dictionary<string, string>
				{
					{ "zh-Hans", "元数据已截断：仅显示前若干类型 / 成员 / 引用。" },
					{ "zh-Hant", "中繼資料已截斷：僅顯示前若干型別 / 成員 / 參考。" },
					{ "ja-JP", "メタデータを切り詰めました。先頭の型 / メンバー / 参照のみ表示しています。" },
					{ "ko-KR", "메타데이터가 잘렸습니다. 앞부분의 형식 / 멤버 / 참조만 표시합니다." },
					{ "fr-FR", "Métadonnées tronquées : seuls les premiers types / membres / références sont affichés." },
					{ "de-DE", "Metadaten abgeschnitten: Es werden nur die ersten Typen / Member / Verweise angezeigt." },
					{ "es-ES", "Metadatos truncados: solo se muestran los primeros tipos / miembros / referencias." }
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