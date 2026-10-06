using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Example
{
	/// <summary>
	/// 示例插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 新插件可直接复制本文件的组织方式。
	/// </summary>
	internal static class ExampleStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="ExampleDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "示例对比" },
			{ "zh-Hant", "範例對比" },
			{ "ja-JP", "サンプル比較" },
			{ "ko-KR", "예제 비교" },
			{ "fr-FR", "Comparaison d'exemple" },
			{ "de-DE", "Beispielvergleich" },
			{ "es-ES", "Comparación de ejemplo" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="ExampleDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "示例插件：认领 .example / .exampletxt，命中后渲染只读的「左旧 / 右新」信息面板，演示 IDiffViewPlugin / IDiffView 的最小实现。" },
			{ "zh-Hant", "範例外掛：認領 .example / .exampletxt，命中後顯示唯讀的「左舊 / 右新」資訊面板，示範 IDiffViewPlugin / IDiffView 的最小實作。" },
			{ "ja-JP", "サンプルプラグイン: .example / .exampletxt を対象に、読み取り専用の「左=旧 / 右=新」情報パネルを表示します。IDiffViewPlugin / IDiffView の最小実装例です。" },
			{ "ko-KR", "예제 플러그인: .example / .exampletxt를 처리하여 읽기 전용 '왼쪽=이전 / 오른쪽=이후' 정보 패널을 표시합니다. IDiffViewPlugin / IDiffView의 최소 구현 예제입니다." },
			{ "fr-FR", "Plugin d'exemple : prend en charge .example / .exampletxt et affiche un panneau d'informations en lecture seule « ancien à gauche / nouveau à droite » ; implémentation minimale de référence de IDiffViewPlugin / IDiffView." },
			{ "de-DE", "Beispiel-Plugin: übernimmt .example / .exampletxt und zeigt ein schreibgeschütztes Informationsfeld „alt links / neu rechts“; minimale Referenzimplementierung von IDiffViewPlugin / IDiffView." },
			{ "es-ES", "Plugin de ejemplo: admite .example / .exampletxt y muestra un panel informativo de solo lectura «antiguo a la izquierda / nuevo a la derecha»; implementación mínima de referencia de IDiffViewPlugin / IDiffView." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			{ "Example Plugin", new Dictionary<string, string>
				{
					{ "zh-Hans", "示例插件" },
					{ "zh-Hant", "範例外掛" },
					{ "ja-JP", "サンプルプラグイン" },
					{ "ko-KR", "예제 플러그인" },
					{ "fr-FR", "Plugin d'exemple" },
					{ "de-DE", "Beispiel-Plugin" },
					{ "es-ES", "Plugin de ejemplo" }
				}
			},
			{ "(not present)", new Dictionary<string, string>
				{
					{ "zh-Hans", "（不存在）" },
					{ "zh-Hant", "（不存在）" },
					{ "ja-JP", "（存在しません）" },
					{ "ko-KR", "(없음)" },
					{ "fr-FR", "(absent)" },
					{ "de-DE", "(nicht vorhanden)" },
					{ "es-ES", "(no presente)" }
				}
			},
			{ "Loaded bytes: {0}", new Dictionary<string, string>
				{
					{ "zh-Hans", "已加载字节：{0}" },
					{ "zh-Hant", "已載入位元組：{0}" },
					{ "ja-JP", "読み込み済みバイト数: {0}" },
					{ "ko-KR", "로드된 바이트: {0}" },
					{ "fr-FR", "Octets chargés : {0}" },
					{ "de-DE", "Geladene Bytes: {0}" },
					{ "es-ES", "Bytes cargados: {0}" }
				}
			},
			{ "LFS: {0}", new Dictionary<string, string>
				{
					{ "zh-Hans", "LFS：{0}" },
					{ "zh-Hant", "LFS：{0}" },
					{ "ja-JP", "LFS: {0}" },
					{ "ko-KR", "LFS: {0}" },
					{ "fr-FR", "LFS : {0}" },
					{ "de-DE", "LFS: {0}" },
					{ "es-ES", "LFS: {0}" }
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
