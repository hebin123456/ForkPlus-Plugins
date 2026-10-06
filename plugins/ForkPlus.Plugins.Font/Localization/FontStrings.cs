using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Font
{
	/// <summary>
	/// 字体对比插件的自带译文（元数据 + 界面文案）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 宿主已提供的通用词（old / new / created / removed / Apply / Password 等）不在此表，走
	/// <see cref="PluginEnvironment.Translate"/>。
	/// </summary>
	internal static class FontStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="FontDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "字体对比" },
			{ "zh-Hant", "字型對比" },
			{ "ja-JP", "フォント比較" },
			{ "ko-KR", "글꼴 비교" },
			{ "fr-FR", "Comparaison de polices" },
			{ "de-DE", "Schriftvergleich" },
			{ "es-ES", "Comparación de fuentes" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="FontDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "字体对比插件：认领 .ttf / .otf / .ttc / .otc / .woff，用 SkiaSharp 并排渲染固定样张，并对 sfnt 表（head / name / OS/2 / maxp / hhea）与 cmap 码位覆盖做结构化 diff。不支持 WOFF2。" },
			{ "zh-Hant", "字型對比外掛：認領 .ttf / .otf / .ttc / .otc / .woff，以 SkiaSharp 並排渲染固定樣張，並對 sfnt 表（head / name / OS/2 / maxp / hhea）與 cmap 碼位覆蓋做結構化 diff。不支援 WOFF2。" },
			{ "ja-JP", "フォント比較プラグイン: .ttf / .otf / .ttc / .otc / .woff を対象に、SkiaSharp で固定サンプルを並べて描画し、sfnt テーブル（head / name / OS/2 / maxp / hhea）と cmap のコードポイント網羅を構造的に差分表示します。WOFF2 は未対応です。" },
			{ "ko-KR", "글꼴 비교 플러그인: .ttf / .otf / .ttc / .otc / .woff를 처리하여 SkiaSharp로 고정 견본을 나란히 렌더링하고, sfnt 테이블(head / name / OS/2 / maxp / hhea)과 cmap 코드포인트 범위를 구조적으로 비교합니다. WOFF2는 지원하지 않습니다." },
			{ "fr-FR", "Plugin de comparaison de polices : prend en charge .ttf / .otf / .ttc / .otc / .woff, rend un échantillon fixe côte à côte avec SkiaSharp et compare structurellement les tables sfnt (head / name / OS/2 / maxp / hhea) ainsi que la couverture des points de code cmap. WOFF2 n'est pas pris en charge." },
			{ "de-DE", "Schriftvergleichs-Plugin: übernimmt .ttf / .otf / .ttc / .otc / .woff, rendert ein festes Muster mit SkiaSharp nebeneinander und vergleicht sfnt-Tabellen (head / name / OS/2 / maxp / hhea) sowie die cmap-Codepunkt-Abdeckung strukturell. WOFF2 wird nicht unterstützt." },
			{ "es-ES", "Plugin de comparación de fuentes: admite .ttf / .otf / .ttc / .otc / .woff, representa una muestra fija en paralelo con SkiaSharp y compara estructuralmente las tablas sfnt (head / name / OS/2 / maxp / hhea) y la cobertura de puntos de código cmap. WOFF2 no es compatible." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			// ---- 视图模式 ----
			{ "Sample", new Dictionary<string, string>
				{
					{ "zh-Hans", "样张" }, { "zh-Hant", "樣張" }, { "ja-JP", "見本" },
					{ "ko-KR", "견본" }, { "fr-FR", "Échantillon" }, { "de-DE", "Muster" }, { "es-ES", "Muestra" }
				}
			},
			{ "Metadata", new Dictionary<string, string>
				{
					{ "zh-Hans", "元数据" }, { "zh-Hant", "中繼資料" }, { "ja-JP", "メタデータ" },
					{ "ko-KR", "메타데이터" }, { "fr-FR", "Métadonnées" }, { "de-DE", "Metadaten" }, { "es-ES", "Metadatos" }
				}
			},
			{ "Codepoints", new Dictionary<string, string>
				{
					{ "zh-Hans", "码位" }, { "zh-Hant", "碼位" }, { "ja-JP", "コードポイント" },
					{ "ko-KR", "코드포인트" }, { "fr-FR", "Points de code" }, { "de-DE", "Codepunkte" }, { "es-ES", "Puntos de código" }
				}
			},

			// ---- 元数据分组 ----
			{ "Identity", new Dictionary<string, string>
				{
					{ "zh-Hans", "标识" }, { "zh-Hant", "識別" }, { "ja-JP", "識別情報" },
					{ "ko-KR", "식별 정보" }, { "fr-FR", "Identité" }, { "de-DE", "Identität" }, { "es-ES", "Identidad" }
				}
			},
			{ "Vertical metrics", new Dictionary<string, string>
				{
					{ "zh-Hans", "垂直度量" }, { "zh-Hant", "垂直度量" }, { "ja-JP", "垂直メトリクス" },
					{ "ko-KR", "수직 메트릭" }, { "fr-FR", "Métriques verticales" }, { "de-DE", "Vertikale Metriken" }, { "es-ES", "Métricas verticales" }
				}
			},
			{ "Weight & width", new Dictionary<string, string>
				{
					{ "zh-Hans", "字重与字宽" }, { "zh-Hant", "字重與字寬" }, { "ja-JP", "ウェイトと幅" },
					{ "ko-KR", "굵기와 폭" }, { "fr-FR", "Graisse et largeur" }, { "de-DE", "Gewicht und Breite" }, { "es-ES", "Grosor y ancho" }
				}
			},
			{ "Tables", new Dictionary<string, string>
				{
					{ "zh-Hans", "表" }, { "zh-Hant", "表" }, { "ja-JP", "テーブル" },
					{ "ko-KR", "테이블" }, { "fr-FR", "Tables" }, { "de-DE", "Tabellen" }, { "es-ES", "Tablas" }
				}
			},

			// ---- Identity 行 ----
			{ "Family", new Dictionary<string, string>
				{
					{ "zh-Hans", "字体族" }, { "zh-Hant", "字型家族" }, { "ja-JP", "ファミリー" },
					{ "ko-KR", "패밀리" }, { "fr-FR", "Famille" }, { "de-DE", "Familie" }, { "es-ES", "Familia" }
				}
			},
			{ "Subfamily", new Dictionary<string, string>
				{
					{ "zh-Hans", "子族" }, { "zh-Hant", "子家族" }, { "ja-JP", "サブファミリー" },
					{ "ko-KR", "서브패밀리" }, { "fr-FR", "Sous-famille" }, { "de-DE", "Unterfamilie" }, { "es-ES", "Subfamilia" }
				}
			},
			{ "Full name", new Dictionary<string, string>
				{
					{ "zh-Hans", "全名" }, { "zh-Hant", "全名" }, { "ja-JP", "フルネーム" },
					{ "ko-KR", "전체 이름" }, { "fr-FR", "Nom complet" }, { "de-DE", "Vollständiger Name" }, { "es-ES", "Nombre completo" }
				}
			},
			{ "PostScript name", new Dictionary<string, string>
				{
					{ "zh-Hans", "PostScript 名称" }, { "zh-Hant", "PostScript 名稱" }, { "ja-JP", "PostScript 名" },
					{ "ko-KR", "PostScript 이름" }, { "fr-FR", "Nom PostScript" }, { "de-DE", "PostScript-Name" }, { "es-ES", "Nombre PostScript" }
				}
			},
			{ "Version", new Dictionary<string, string>
				{
					{ "zh-Hans", "版本" }, { "zh-Hant", "版本" }, { "ja-JP", "バージョン" },
					{ "ko-KR", "버전" }, { "fr-FR", "Version" }, { "de-DE", "Version" }, { "es-ES", "Versión" }
				}
			},
			{ "Unique ID", new Dictionary<string, string>
				{
					{ "zh-Hans", "唯一标识" }, { "zh-Hant", "唯一識別碼" }, { "ja-JP", "一意 ID" },
					{ "ko-KR", "고유 ID" }, { "fr-FR", "Identifiant unique" }, { "de-DE", "Eindeutige ID" }, { "es-ES", "ID único" }
				}
			},
			{ "Copyright", new Dictionary<string, string>
				{
					{ "zh-Hans", "版权" }, { "zh-Hant", "版權" }, { "ja-JP", "著作権" },
					{ "ko-KR", "저작권" }, { "fr-FR", "Copyright" }, { "de-DE", "Copyright" }, { "es-ES", "Copyright" }
				}
			},
			{ "Trademark", new Dictionary<string, string>
				{
					{ "zh-Hans", "商标" }, { "zh-Hant", "商標" }, { "ja-JP", "商標" },
					{ "ko-KR", "상표" }, { "fr-FR", "Marque déposée" }, { "de-DE", "Marke" }, { "es-ES", "Marca registrada" }
				}
			},
			{ "Format", new Dictionary<string, string>
				{
					{ "zh-Hans", "格式" }, { "zh-Hant", "格式" }, { "ja-JP", "フォーマット" },
					{ "ko-KR", "형식" }, { "fr-FR", "Format" }, { "de-DE", "Format" }, { "es-ES", "Formato" }
				}
			},

			// ---- Vertical metrics 行 ----
			{ "Units per em", new Dictionary<string, string>
				{
					{ "zh-Hans", "每 em 单位" }, { "zh-Hant", "每 em 單位" }, { "ja-JP", "em あたりのユニット" },
					{ "ko-KR", "em당 단위" }, { "fr-FR", "Unités par em" }, { "de-DE", "Einheiten pro em" }, { "es-ES", "Unidades por em" }
				}
			},
			{ "Typo ascender", new Dictionary<string, string>
				{
					{ "zh-Hans", "排版上伸部" }, { "zh-Hant", "排版上伸部" }, { "ja-JP", "タイポ上昇部" },
					{ "ko-KR", "타이포 어센더" }, { "fr-FR", "Ascendante typo" }, { "de-DE", "Typo-Aufstrich" }, { "es-ES", "Ascendente tipográfico" }
				}
			},
			{ "Typo descender", new Dictionary<string, string>
				{
					{ "zh-Hans", "排版下伸部" }, { "zh-Hant", "排版下伸部" }, { "ja-JP", "タイポ下降部" },
					{ "ko-KR", "타이포 디센더" }, { "fr-FR", "Descendante typo" }, { "de-DE", "Typo-Abstrich" }, { "es-ES", "Descendente tipográfico" }
				}
			},
			{ "Typo line gap", new Dictionary<string, string>
				{
					{ "zh-Hans", "排版行距" }, { "zh-Hant", "排版行距" }, { "ja-JP", "タイポ行間" },
					{ "ko-KR", "타이포 행간" }, { "fr-FR", "Interligne typo" }, { "de-DE", "Typo-Zeilenabstand" }, { "es-ES", "Interlineado tipográfico" }
				}
			},
			{ "Win ascent", new Dictionary<string, string>
				{
					{ "zh-Hans", "Windows 上伸部" }, { "zh-Hant", "Windows 上伸部" }, { "ja-JP", "Windows 上昇部" },
					{ "ko-KR", "Windows 어센더" }, { "fr-FR", "Ascendante Windows" }, { "de-DE", "Windows-Aufstrich" }, { "es-ES", "Ascendente Windows" }
				}
			},
			{ "Win descent", new Dictionary<string, string>
				{
					{ "zh-Hans", "Windows 下伸部" }, { "zh-Hant", "Windows 下伸部" }, { "ja-JP", "Windows 下降部" },
					{ "ko-KR", "Windows 디센더" }, { "fr-FR", "Descendante Windows" }, { "de-DE", "Windows-Abstrich" }, { "es-ES", "Descendente Windows" }
				}
			},
			{ "hhea ascender", new Dictionary<string, string>
				{
					{ "zh-Hans", "hhea 上伸部" }, { "zh-Hant", "hhea 上伸部" }, { "ja-JP", "hhea 上昇部" },
					{ "ko-KR", "hhea 어센더" }, { "fr-FR", "Ascendante hhea" }, { "de-DE", "hhea-Aufstrich" }, { "es-ES", "Ascendente hhea" }
				}
			},
			{ "hhea descender", new Dictionary<string, string>
				{
					{ "zh-Hans", "hhea 下伸部" }, { "zh-Hant", "hhea 下伸部" }, { "ja-JP", "hhea 下降部" },
					{ "ko-KR", "hhea 디센더" }, { "fr-FR", "Descendante hhea" }, { "de-DE", "hhea-Abstrich" }, { "es-ES", "Descendente hhea" }
				}
			},
			{ "hhea line gap", new Dictionary<string, string>
				{
					{ "zh-Hans", "hhea 行距" }, { "zh-Hant", "hhea 行距" }, { "ja-JP", "hhea 行間" },
					{ "ko-KR", "hhea 행간" }, { "fr-FR", "Interligne hhea" }, { "de-DE", "hhea-Zeilenabstand" }, { "es-ES", "Interlineado hhea" }
				}
			},

			// ---- Weight & width 行 ----
			{ "Weight class", new Dictionary<string, string>
				{
					{ "zh-Hans", "字重等级" }, { "zh-Hant", "字重等級" }, { "ja-JP", "ウェイト値" },
					{ "ko-KR", "굵기 등급" }, { "fr-FR", "Classe de graisse" }, { "de-DE", "Gewichtsklasse" }, { "es-ES", "Clase de grosor" }
				}
			},
			{ "Width class", new Dictionary<string, string>
				{
					{ "zh-Hans", "字宽等级" }, { "zh-Hant", "字寬等級" }, { "ja-JP", "幅値" },
					{ "ko-KR", "폭 등급" }, { "fr-FR", "Classe de largeur" }, { "de-DE", "Breitenklasse" }, { "es-ES", "Clase de ancho" }
				}
			},
			{ "Selection", new Dictionary<string, string>
				{
					{ "zh-Hans", "选择标志" }, { "zh-Hant", "選擇旗標" }, { "ja-JP", "選択フラグ" },
					{ "ko-KR", "선택 플래그" }, { "fr-FR", "Indicateurs" }, { "de-DE", "Auswahl-Flags" }, { "es-ES", "Indicadores" }
				}
			},
			{ "Mac style", new Dictionary<string, string>
				{
					{ "zh-Hans", "Mac 样式" }, { "zh-Hant", "Mac 樣式" }, { "ja-JP", "Mac スタイル" },
					{ "ko-KR", "Mac 스타일" }, { "fr-FR", "Style Mac" }, { "de-DE", "Mac-Stil" }, { "es-ES", "Estilo Mac" }
				}
			},

			// ---- Tables 行 ----
			{ "Glyphs", new Dictionary<string, string>
				{
					{ "zh-Hans", "字形数" }, { "zh-Hant", "字形數" }, { "ja-JP", "グリフ数" },
					{ "ko-KR", "글리프 수" }, { "fr-FR", "Glyphes" }, { "de-DE", "Glyphen" }, { "es-ES", "Glifos" }
				}
			},
			{ "Codepoints count", new Dictionary<string, string>
				{
					{ "zh-Hans", "码位数" }, { "zh-Hant", "碼位數" }, { "ja-JP", "コードポイント数" },
					{ "ko-KR", "코드포인트 수" }, { "fr-FR", "Points de code" }, { "de-DE", "Codepunkte" }, { "es-ES", "Puntos de código" }
				}
			},
			{ "GSUB features", new Dictionary<string, string>
				{
					{ "zh-Hans", "GSUB 特性" }, { "zh-Hant", "GSUB 特性" }, { "ja-JP", "GSUB 機能" },
					{ "ko-KR", "GSUB 기능" }, { "fr-FR", "Fonctionnalités GSUB" }, { "de-DE", "GSUB-Features" }, { "es-ES", "Funciones GSUB" }
				}
			},
			{ "Created", new Dictionary<string, string>
				{
					{ "zh-Hans", "创建时间" }, { "zh-Hant", "建立時間" }, { "ja-JP", "作成日時" },
					{ "ko-KR", "생성 시각" }, { "fr-FR", "Créé le" }, { "de-DE", "Erstellt" }, { "es-ES", "Creado" }
				}
			},
			{ "Modified", new Dictionary<string, string>
				{
					{ "zh-Hans", "修改时间" }, { "zh-Hant", "修改時間" }, { "ja-JP", "更新日時" },
					{ "ko-KR", "수정 시각" }, { "fr-FR", "Modifié le" }, { "de-DE", "Geändert" }, { "es-ES", "Modificado" }
				}
			},

			// ---- 表名（Tables 分组行标签） ----
			{ "head", new Dictionary<string, string>
				{
					{ "zh-Hans", "head" }, { "zh-Hant", "head" }, { "ja-JP", "head" },
					{ "ko-KR", "head" }, { "fr-FR", "head" }, { "de-DE", "head" }, { "es-ES", "head" }
				}
			},
			{ "name", new Dictionary<string, string>
				{
					{ "zh-Hans", "name" }, { "zh-Hant", "name" }, { "ja-JP", "name" },
					{ "ko-KR", "name" }, { "fr-FR", "name" }, { "de-DE", "name" }, { "es-ES", "name" }
				}
			},
			{ "OS/2", new Dictionary<string, string>
				{
					{ "zh-Hans", "OS/2" }, { "zh-Hant", "OS/2" }, { "ja-JP", "OS/2" },
					{ "ko-KR", "OS/2" }, { "fr-FR", "OS/2" }, { "de-DE", "OS/2" }, { "es-ES", "OS/2" }
				}
			},
			{ "maxp", new Dictionary<string, string>
				{
					{ "zh-Hans", "maxp" }, { "zh-Hant", "maxp" }, { "ja-JP", "maxp" },
					{ "ko-KR", "maxp" }, { "fr-FR", "maxp" }, { "de-DE", "maxp" }, { "es-ES", "maxp" }
				}
			},
			{ "hhea", new Dictionary<string, string>
				{
					{ "zh-Hans", "hhea" }, { "zh-Hant", "hhea" }, { "ja-JP", "hhea" },
					{ "ko-KR", "hhea" }, { "fr-FR", "hhea" }, { "de-DE", "hhea" }, { "es-ES", "hhea" }
				}
			},
			{ "cmap", new Dictionary<string, string>
				{
					{ "zh-Hans", "cmap" }, { "zh-Hant", "cmap" }, { "ja-JP", "cmap" },
					{ "ko-KR", "cmap" }, { "fr-FR", "cmap" }, { "de-DE", "cmap" }, { "es-ES", "cmap" }
				}
			},
			{ "kern", new Dictionary<string, string>
				{
					{ "zh-Hans", "kern" }, { "zh-Hant", "kern" }, { "ja-JP", "kern" },
					{ "ko-KR", "kern" }, { "fr-FR", "kern" }, { "de-DE", "kern" }, { "es-ES", "kern" }
				}
			},
			{ "GPOS", new Dictionary<string, string>
				{
					{ "zh-Hans", "GPOS" }, { "zh-Hant", "GPOS" }, { "ja-JP", "GPOS" },
					{ "ko-KR", "GPOS" }, { "fr-FR", "GPOS" }, { "de-DE", "GPOS" }, { "es-ES", "GPOS" }
				}
			},
			{ "GSUB", new Dictionary<string, string>
				{
					{ "zh-Hans", "GSUB" }, { "zh-Hant", "GSUB" }, { "ja-JP", "GSUB" },
					{ "ko-KR", "GSUB" }, { "fr-FR", "GSUB" }, { "de-DE", "GSUB" }, { "es-ES", "GSUB" }
				}
			},

			// ---- 变更标注 ----
			{ "same", new Dictionary<string, string>
				{
					{ "zh-Hans", "相同" }, { "zh-Hant", "相同" }, { "ja-JP", "同一" },
					{ "ko-KR", "동일" }, { "fr-FR", "Identique" }, { "de-DE", "Gleich" }, { "es-ES", "Igual" }
				}
			},
			{ "changed", new Dictionary<string, string>
				{
					{ "zh-Hans", "已变更" }, { "zh-Hant", "已變更" }, { "ja-JP", "変更" },
					{ "ko-KR", "변경됨" }, { "fr-FR", "Modifié" }, { "de-DE", "Geändert" }, { "es-ES", "Modificado" }
				}
			},
			{ "left only", new Dictionary<string, string>
				{
					{ "zh-Hans", "仅左" }, { "zh-Hant", "僅左" }, { "ja-JP", "左のみ" },
					{ "ko-KR", "왼쪽만" }, { "fr-FR", "Gauche seule" }, { "de-DE", "Nur links" }, { "es-ES", "Solo izquierda" }
				}
			},
			{ "right only", new Dictionary<string, string>
				{
					{ "zh-Hans", "仅右" }, { "zh-Hant", "僅右" }, { "ja-JP", "右のみ" },
					{ "ko-KR", "오른쪽만" }, { "fr-FR", "Droite seule" }, { "de-DE", "Nur rechts" }, { "es-ES", "Solo derecha" }
				}
			},
			{ "not present", new Dictionary<string, string>
				{
					{ "zh-Hans", "不存在" }, { "zh-Hant", "不存在" }, { "ja-JP", "存在しません" },
					{ "ko-KR", "없음" }, { "fr-FR", "Absent" }, { "de-DE", "Nicht vorhanden" }, { "es-ES", "No presente" }
				}
			},

			// ---- 格式名 ----
			{ "TrueType", new Dictionary<string, string>
				{
					{ "zh-Hans", "TrueType" }, { "zh-Hant", "TrueType" }, { "ja-JP", "TrueType" },
					{ "ko-KR", "TrueType" }, { "fr-FR", "TrueType" }, { "de-DE", "TrueType" }, { "es-ES", "TrueType" }
				}
			},
			{ "OpenType (CFF)", new Dictionary<string, string>
				{
					{ "zh-Hans", "OpenType (CFF)" }, { "zh-Hant", "OpenType (CFF)" }, { "ja-JP", "OpenType (CFF)" },
					{ "ko-KR", "OpenType (CFF)" }, { "fr-FR", "OpenType (CFF)" }, { "de-DE", "OpenType (CFF)" }, { "es-ES", "OpenType (CFF)" }
				}
			},
			{ "TrueType Collection", new Dictionary<string, string>
				{
					{ "zh-Hans", "TrueType 集合" }, { "zh-Hant", "TrueType 集合" }, { "ja-JP", "TrueType コレクション" },
					{ "ko-KR", "TrueType 컬렉션" }, { "fr-FR", "Collection TrueType" }, { "de-DE", "TrueType-Sammlung" }, { "es-ES", "Colección TrueType" }
				}
			},
			{ "WOFF", new Dictionary<string, string>
				{
					{ "zh-Hans", "WOFF" }, { "zh-Hant", "WOFF" }, { "ja-JP", "WOFF" },
					{ "ko-KR", "WOFF" }, { "fr-FR", "WOFF" }, { "de-DE", "WOFF" }, { "es-ES", "WOFF" }
				}
			},
			{ "Unknown", new Dictionary<string, string>
				{
					{ "zh-Hans", "未知" }, { "zh-Hant", "未知" }, { "ja-JP", "不明" },
					{ "ko-KR", "알 수 없음" }, { "fr-FR", "Inconnu" }, { "de-DE", "Unbekannt" }, { "es-ES", "Desconocido" }
				}
			},

			// ---- Unicode 区块 ----
			{ "Latin", new Dictionary<string, string>
				{
					{ "zh-Hans", "拉丁" }, { "zh-Hant", "拉丁" }, { "ja-JP", "ラテン" },
					{ "ko-KR", "라틴" }, { "fr-FR", "Latin" }, { "de-DE", "Latein" }, { "es-ES", "Latino" }
				}
			},
			{ "Greek", new Dictionary<string, string>
				{
					{ "zh-Hans", "希腊" }, { "zh-Hant", "希臘" }, { "ja-JP", "ギリシャ" },
					{ "ko-KR", "그리스" }, { "fr-FR", "Grec" }, { "de-DE", "Griechisch" }, { "es-ES", "Griego" }
				}
			},
			{ "Cyrillic", new Dictionary<string, string>
				{
					{ "zh-Hans", "西里尔" }, { "zh-Hant", "西里爾" }, { "ja-JP", "キリル" },
					{ "ko-KR", "키릴" }, { "fr-FR", "Cyrillique" }, { "de-DE", "Kyrillisch" }, { "es-ES", "Cirílico" }
				}
			},
			{ "CJK Unified", new Dictionary<string, string>
				{
					{ "zh-Hans", "CJK 统一表意文字" }, { "zh-Hant", "CJK 統一表意文字" }, { "ja-JP", "CJK 統合漢字" },
					{ "ko-KR", "CJK 통합 한자" }, { "fr-FR", "CJK unifié" }, { "de-DE", "CJK Unified" }, { "es-ES", "CJK unificado" }
				}
			},
			{ "Punctuation", new Dictionary<string, string>
				{
					{ "zh-Hans", "标点" }, { "zh-Hant", "標點" }, { "ja-JP", "約物" },
					{ "ko-KR", "문장 부호" }, { "fr-FR", "Ponctuation" }, { "de-DE", "Interpunktion" }, { "es-ES", "Puntuación" }
				}
			},
			{ "Digits", new Dictionary<string, string>
				{
					{ "zh-Hans", "数字" }, { "zh-Hant", "數字" }, { "ja-JP", "数字" },
					{ "ko-KR", "숫자" }, { "fr-FR", "Chiffres" }, { "de-DE", "Ziffern" }, { "es-ES", "Dígitos" }
				}
			},
			{ "Other", new Dictionary<string, string>
				{
					{ "zh-Hans", "其它" }, { "zh-Hant", "其他" }, { "ja-JP", "その他" },
					{ "ko-KR", "기타" }, { "fr-FR", "Autres" }, { "de-DE", "Sonstige" }, { "es-ES", "Otros" }
				}
			},

			// ---- 消息 / 标签 ----
			{ "Rendering samples…", new Dictionary<string, string>
				{
					{ "zh-Hans", "正在渲染样张…" }, { "zh-Hant", "正在渲染樣張…" }, { "ja-JP", "見本を描画中…" },
					{ "ko-KR", "견본 렌더링 중…" }, { "fr-FR", "Rendu des échantillons…" }, { "de-DE", "Muster werden gerendert…" }, { "es-ES", "Representando muestras…" }
				}
			},
			{ "Font compare: {0} / {1} faces", new Dictionary<string, string>
				{
					{ "zh-Hans", "字体对比：{0} / {1} 套" }, { "zh-Hant", "字型對比：{0} / {1} 套" }, { "ja-JP", "フォント比較: {0} / {1} 書体" },
					{ "ko-KR", "글꼴 비교: {0} / {1} 서체" }, { "fr-FR", "Comparaison de polices : {0} / {1} fontes" }, { "de-DE", "Schriftvergleich: {0} / {1} Schnitte" }, { "es-ES", "Comparación de fuentes: {0} / {1} caras" }
				}
			},
			{ "Coverage: {0} codepoints", new Dictionary<string, string>
				{
					{ "zh-Hans", "覆盖：{0} 个码位" }, { "zh-Hant", "覆蓋：{0} 個碼位" }, { "ja-JP", "カバレッジ: {0} コードポイント" },
					{ "ko-KR", "적용 범위: {0} 코드포인트" }, { "fr-FR", "Couverture : {0} points de code" }, { "de-DE", "Abdeckung: {0} Codepunkte" }, { "es-ES", "Cobertura: {0} puntos de código" }
				}
			},
			{ "Only in left: {0}", new Dictionary<string, string>
				{
					{ "zh-Hans", "仅左：{0}" }, { "zh-Hant", "僅左：{0}" }, { "ja-JP", "左のみ: {0}" },
					{ "ko-KR", "왼쪽만: {0}" }, { "fr-FR", "Gauche seule : {0}" }, { "de-DE", "Nur links: {0}" }, { "es-ES", "Solo izquierda: {0}" }
				}
			},
			{ "Only in right: {0}", new Dictionary<string, string>
				{
					{ "zh-Hans", "仅右：{0}" }, { "zh-Hant", "僅右：{0}" }, { "ja-JP", "右のみ: {0}" },
					{ "ko-KR", "오른쪽만: {0}" }, { "fr-FR", "Droite seule : {0}" }, { "de-DE", "Nur rechts: {0}" }, { "es-ES", "Solo derecha: {0}" }
				}
			},
			{ "Common: {0}", new Dictionary<string, string>
				{
					{ "zh-Hans", "共有：{0}" }, { "zh-Hant", "共有：{0}" }, { "ja-JP", "共通: {0}" },
					{ "ko-KR", "공통: {0}" }, { "fr-FR", "Communs : {0}" }, { "de-DE", "Gemeinsam: {0}" }, { "es-ES", "Comunes: {0}" }
				}
			},
			{ "Only in left/right: {0}", new Dictionary<string, string>
				{
					{ "zh-Hans", "仅左/右：{0}" }, { "zh-Hant", "僅左/右：{0}" }, { "ja-JP", "左/右のみ: {0}" },
					{ "ko-KR", "왼쪽/오른쪽만: {0}" }, { "fr-FR", "Gauche/droite : {0}" }, { "de-DE", "Nur links/rechts: {0}" }, { "es-ES", "Solo izquierda/derecha: {0}" }
				}
			},
			{ "Font content unavailable", new Dictionary<string, string>
				{
					{ "zh-Hans", "字体内容不可用" }, { "zh-Hant", "字型內容不可用" }, { "ja-JP", "フォント内容を取得できません" },
					{ "ko-KR", "글꼴 내용을 사용할 수 없음" }, { "fr-FR", "Contenu de police indisponible" }, { "de-DE", "Schriftinhalt nicht verfügbar" }, { "es-ES", "Contenido de fuente no disponible" }
				}
			},
			{ "Failed to parse font", new Dictionary<string, string>
				{
					{ "zh-Hans", "字体解析失败" }, { "zh-Hant", "字型解析失敗" }, { "ja-JP", "フォントの解析に失敗しました" },
					{ "ko-KR", "글꼴 구문 분석 실패" }, { "fr-FR", "Échec de l'analyse de la police" }, { "de-DE", "Schrift konnte nicht analysiert werden" }, { "es-ES", "No se pudo analizar la fuente" }
				}
			},
			{ "Face {0}", new Dictionary<string, string>
				{
					{ "zh-Hans", "第 {0} 套" }, { "zh-Hant", "第 {0} 套" }, { "ja-JP", "書体 {0}" },
					{ "ko-KR", "서체 {0}" }, { "fr-FR", "Fonte {0}" }, { "de-DE", "Schnitt {0}" }, { "es-ES", "Cara {0}" }
				}
			},
			{ "WOFF2 not supported", new Dictionary<string, string>
				{
					{ "zh-Hans", "不支持 WOFF2" }, { "zh-Hant", "不支援 WOFF2" }, { "ja-JP", "WOFF2 は未対応" },
					{ "ko-KR", "WOFF2 지원 안 함" }, { "fr-FR", "WOFF2 non pris en charge" }, { "de-DE", "WOFF2 wird nicht unterstützt" }, { "es-ES", "WOFF2 no compatible" }
				}
			},
			{ "Unsupported font format", new Dictionary<string, string>
				{
					{ "zh-Hans", "不支持的字体格式" }, { "zh-Hant", "不支援的字型格式" }, { "ja-JP", "未対応のフォント形式" },
					{ "ko-KR", "지원되지 않는 글꼴 형식" }, { "fr-FR", "Format de police non pris en charge" }, { "de-DE", "Nicht unterstütztes Schriftformat" }, { "es-ES", "Formato de fuente no compatible" }
				}
			},
			{ "Empty font file", new Dictionary<string, string>
				{
					{ "zh-Hans", "字体文件为空" }, { "zh-Hant", "字型檔案為空" }, { "ja-JP", "フォントファイルが空です" },
					{ "ko-KR", "글꼴 파일이 비어 있음" }, { "fr-FR", "Fichier de police vide" }, { "de-DE", "Leere Schriftdatei" }, { "es-ES", "Archivo de fuente vacío" }
				}
			},
			{ "No data", new Dictionary<string, string>
				{
					{ "zh-Hans", "无数据" }, { "zh-Hant", "無資料" }, { "ja-JP", "データなし" },
					{ "ko-KR", "데이터 없음" }, { "fr-FR", "Aucune donnée" }, { "de-DE", "Keine Daten" }, { "es-ES", "Sin datos" }
				}
			},
			{ "Failed to render samples", new Dictionary<string, string>
				{
					{ "zh-Hans", "样张渲染失败" }, { "zh-Hant", "樣張渲染失敗" }, { "ja-JP", "見本の描画に失敗しました" },
					{ "ko-KR", "견본 렌더링 실패" }, { "fr-FR", "Échec du rendu des échantillons" }, { "de-DE", "Muster konnten nicht gerendert werden" }, { "es-ES", "No se pudieron representar las muestras" }
				}
			},
			{ "{0} px", new Dictionary<string, string>
				{
					{ "zh-Hans", "{0} 像素" }, { "zh-Hant", "{0} 像素" }, { "ja-JP", "{0} px" },
					{ "ko-KR", "{0} px" }, { "fr-FR", "{0} px" }, { "de-DE", "{0} px" }, { "es-ES", "{0} px" }
				}
			},
			{ "yes", new Dictionary<string, string>
				{
					{ "zh-Hans", "是" }, { "zh-Hant", "是" }, { "ja-JP", "はい" },
					{ "ko-KR", "예" }, { "fr-FR", "Oui" }, { "de-DE", "Ja" }, { "es-ES", "Sí" }
				}
			},
			{ "no", new Dictionary<string, string>
				{
					{ "zh-Hans", "否" }, { "zh-Hant", "否" }, { "ja-JP", "いいえ" },
					{ "ko-KR", "아니요" }, { "fr-FR", "Non" }, { "de-DE", "Nein" }, { "es-ES", "No" }
				}
			},
			{ "Unicode blocks", new Dictionary<string, string>
				{
					{ "zh-Hans", "Unicode 区块" }, { "zh-Hant", "Unicode 區塊" }, { "ja-JP", "Unicode ブロック" },
					{ "ko-KR", "Unicode 블록" }, { "fr-FR", "Blocs Unicode" }, { "de-DE", "Unicode-Blöcke" }, { "es-ES", "Bloques Unicode" }
				}
			},
			{ "Block", new Dictionary<string, string>
				{
					{ "zh-Hans", "区块" }, { "zh-Hant", "區塊" }, { "ja-JP", "ブロック" },
					{ "ko-KR", "블록" }, { "fr-FR", "Bloc" }, { "de-DE", "Block" }, { "es-ES", "Bloque" }
				}
			},
			{ "Showing first {0} codepoints.", new Dictionary<string, string>
				{
					{ "zh-Hans", "仅显示前 {0} 个码位。" }, { "zh-Hant", "僅顯示前 {0} 個碼位。" }, { "ja-JP", "先頭 {0} コードポイントのみ表示。" },
					{ "ko-KR", "처음 {0}개 코드포인트만 표시합니다." }, { "fr-FR", "Affichage des {0} premiers points de code." }, { "de-DE", "Nur die ersten {0} Codepunkte werden angezeigt." }, { "es-ES", "Mostrando solo los primeros {0} puntos de código." }
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
