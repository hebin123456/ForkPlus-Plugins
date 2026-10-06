using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Office
{
	/// <summary>
	/// Office 插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 技术词与品牌名（Word / Excel / PowerPoint / Open XML 等）不翻译、不进表。
	/// </summary>
	internal static class OfficeStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="OfficeDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "Office 对比" },
			{ "zh-Hant", "Office 對比" },
			{ "ja-JP", "Office 比較" },
			{ "ko-KR", "Office 비교" },
			{ "fr-FR", "Comparaison Office" },
			{ "de-DE", "Office-Vergleich" },
			{ "es-ES", "Comparación de Office" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="OfficeDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "Office 套件对比视图插件：认领 Word（.docx）、Excel（.xlsx）、PowerPoint（.pptx），使用 Open XML SDK 提取正文内容并左右并排对比（段落、表格、工作表网格与幻灯片文字）。" },
			{ "zh-Hant", "Office 套件對比檢視外掛：認領 Word（.docx）、Excel（.xlsx）、PowerPoint（.pptx），使用 Open XML SDK 擷取內容並左右並排對比（段落、表格、工作表網格與投影片文字）。" },
			{ "ja-JP", "Office 比較ビュープラグイン: Word（.docx）、Excel（.xlsx）、PowerPoint（.pptx）を対象に、Open XML SDK で内容を抽出し、左右に並べて比較します（段落、表、ワークシートのグリッド、スライドのテキスト）。" },
			{ "ko-KR", "Office 비교 보기 플러그인: Word(.docx), Excel(.xlsx), PowerPoint(.pptx)를 처리하고 Open XML SDK로 내용을 추출하여 나란히 비교합니다(단락, 표, 워크시트 그리드, 슬라이드 텍스트)." },
			{ "fr-FR", "Plugin de comparaison Office : prend en charge Word (.docx), Excel (.xlsx) et PowerPoint (.pptx), en extrait le contenu avec le SDK Open XML et le compare côte à côte (paragraphes, tableaux, grilles de feuilles de calcul et texte des diapositives)." },
			{ "de-DE", "Office-Vergleichs-Plugin: übernimmt Word (.docx), Excel (.xlsx) und PowerPoint (.pptx), extrahiert deren Inhalt mit dem Open XML SDK und vergleicht ihn nebeneinander (Absätze, Tabellen, Arbeitsblatt-Raster und Folientext)." },
			{ "es-ES", "Plugin de comparación de Office: admite Word (.docx), Excel (.xlsx) y PowerPoint (.pptx), extrae su contenido con el SDK de Open XML y lo compara en paralelo (párrafos, tablas, cuadrículas de hojas de cálculo y texto de diapositivas)." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			{ "Extracting Office content…", new Dictionary<string, string>
				{
					{ "zh-Hans", "正在提取 Office 内容…" },
					{ "zh-Hant", "正在擷取 Office 內容…" },
					{ "ja-JP", "Office の内容を抽出しています…" },
					{ "ko-KR", "Office 내용을 추출하는 중…" },
					{ "fr-FR", "Extraction du contenu Office…" },
					{ "de-DE", "Office-Inhalt wird extrahiert…" },
					{ "es-ES", "Extrayendo el contenido de Office…" }
				}
			},
			{ "Office compare: {0} / {1} blocks", new Dictionary<string, string>
				{
					{ "zh-Hans", "Office 对比：{0} / {1} 个块" },
					{ "zh-Hant", "Office 對比：{0} / {1} 個區塊" },
					{ "ja-JP", "Office 比較: {0} / {1} ブロック" },
					{ "ko-KR", "Office 비교: {0} / {1} 블록" },
					{ "fr-FR", "Comparaison Office : {0} / {1} blocs" },
					{ "de-DE", "Office-Vergleich: {0} / {1} Blöcke" },
					{ "es-ES", "Comparación de Office: {0} / {1} bloques" }
				}
			},
			{ "Failed to read Office document", new Dictionary<string, string>
				{
					{ "zh-Hans", "读取 Office 文档失败" },
					{ "zh-Hant", "讀取 Office 文件失敗" },
					{ "ja-JP", "Office 文書の読み込みに失敗しました" },
					{ "ko-KR", "Office 문서를 읽지 못했습니다" },
					{ "fr-FR", "Échec de la lecture du document Office" },
					{ "de-DE", "Office-Dokument konnte nicht gelesen werden" },
					{ "es-ES", "No se pudo leer el documento de Office" }
				}
			},
			{ "{0} blocks · {1} chars", new Dictionary<string, string>
				{
					{ "zh-Hans", "{0} 个块 · {1} 字符" },
					{ "zh-Hant", "{0} 個區塊 · {1} 字元" },
					{ "ja-JP", "{0} ブロック · {1} 文字" },
					{ "ko-KR", "{0} 블록 · {1} 자" },
					{ "fr-FR", "{0} blocs · {1} caractères" },
					{ "de-DE", "{0} Blöcke · {1} Zeichen" },
					{ "es-ES", "{0} bloques · {1} caracteres" }
				}
			},
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
			{ "Office content unavailable", new Dictionary<string, string>
				{
					{ "zh-Hans", "Office 内容不可用" },
					{ "zh-Hant", "Office 內容無法使用" },
					{ "ja-JP", "Office の内容を利用できません" },
					{ "ko-KR", "Office 내용을 사용할 수 없음" },
					{ "fr-FR", "Contenu Office indisponible" },
					{ "de-DE", "Office-Inhalt nicht verfügbar" },
					{ "es-ES", "Contenido de Office no disponible" }
				}
			},
			{ "Slide {0}", new Dictionary<string, string>
				{
					{ "zh-Hans", "幻灯片 {0}" },
					{ "zh-Hant", "投影片 {0}" },
					{ "ja-JP", "スライド {0}" },
					{ "ko-KR", "슬라이드 {0}" },
					{ "fr-FR", "Diapositive {0}" },
					{ "de-DE", "Folie {0}" },
					{ "es-ES", "Diapositiva {0}" }
				}
			},
			{ "({0}/{1} rows)", new Dictionary<string, string>
				{
					{ "zh-Hans", "（{0}/{1} 行）" },
					{ "zh-Hant", "（{0}/{1} 列）" },
					{ "ja-JP", "（{0}/{1} 行）" },
					{ "ko-KR", "({0}/{1}행)" },
					{ "fr-FR", "({0}/{1} lignes)" },
					{ "de-DE", "({0}/{1} Zeilen)" },
					{ "es-ES", "({0}/{1} filas)" }
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
