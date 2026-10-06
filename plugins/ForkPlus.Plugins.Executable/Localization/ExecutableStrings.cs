using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Executable
{
	/// <summary>
	/// 可执行文件 / 库对比插件的自带译文（v5.0.3 多语言）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），因此漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 组织方式与 <c>ForkPlus.Plugins.Archive/Localization/ArchiveStrings.cs</c> 一致。
	/// </summary>
	internal static class ExecutableStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		/// <summary>插件显示名译文（英文原文见 <see cref="ExecutableDiffPlugin.DisplayName"/>）。</summary>
		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "可执行文件对比" },
			{ "zh-Hant", "可執行檔對比" },
			{ "ja-JP", "実行ファイル比較" },
			{ "ko-KR", "실행 파일 비교" },
			{ "fr-FR", "Comparaison d'exécutables" },
			{ "de-DE", "Vergleich ausführbarer Dateien" },
			{ "es-ES", "Comparación de ejecutables" }
		};

		/// <summary>插件描述译文（英文原文见 <see cref="ExecutableDiffPlugin.Description"/>）。</summary>
		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "可执行文件 / 库对比视图插件：认领 .exe / .dll / .so / .dylib / .a / .lib / .wasm，自行解析 PE / ELF / Mach-O / WebAssembly / ar，并就段与节、导入导出符号、依赖与体积构成做左右并排对比。" },
			{ "zh-Hant", "可執行檔 / 函式庫對比檢視外掛：認領 .exe / .dll / .so / .dylib / .a / .lib / .wasm，自行解析 PE / ELF / Mach-O / WebAssembly / ar，並就區段與節、匯入匯出符號、相依與體積組成做左右並排對比。" },
			{ "ja-JP", "実行ファイル / ライブラリ比較ビュープラグイン: .exe / .dll / .so / .dylib / .a / .lib / .wasm を対象に、PE / ELF / Mach-O / WebAssembly / ar を自前で解析し、セクション、インポート / エクスポート、依存関係、サイズ構成を左右に並べて比較します。" },
			{ "ko-KR", "실행 파일 / 라이브러리 비교 보기 플러그인: .exe / .dll / .so / .dylib / .a / .lib / .wasm을 처리하고 PE / ELF / Mach-O / WebAssembly / ar을 직접 분석하여 섹션, 가져오기 / 내보내기, 종속성, 크기 구성을 나란히 비교합니다." },
			{ "fr-FR", "Plugin de vue de comparaison d'exécutables / bibliothèques : prend en charge .exe / .dll / .so / .dylib / .a / .lib / .wasm, analyse lui-même PE / ELF / Mach-O / WebAssembly / ar et compare côte à côte sections, imports / exports, dépendances et composition de la taille." },
			{ "de-DE", "Ansichts-Plugin zum Vergleich ausführbarer Dateien / Bibliotheken: übernimmt .exe / .dll / .so / .dylib / .a / .lib / .wasm, analysiert PE / ELF / Mach-O / WebAssembly / ar selbst und vergleicht Abschnitte, Importe / Exporte, Abhängigkeiten und Größenverteilung nebeneinander." },
			{ "es-ES", "Plugin de vista de comparación de ejecutables / bibliotecas: admite .exe / .dll / .so / .dylib / .a / .lib / .wasm, analiza por sí mismo PE / ELF / Mach-O / WebAssembly / ar y compara en paralelo secciones, importaciones / exportaciones, dependencias y composición del tamaño." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			// 模式名
			{ "Summary", new Dictionary<string, string>
				{
					{ "zh-Hans", "结构摘要" },
					{ "zh-Hant", "結構摘要" },
					{ "ja-JP", "サマリー" },
					{ "ko-KR", "요약" },
					{ "fr-FR", "Résumé" },
					{ "de-DE", "Übersicht" },
					{ "es-ES", "Resumen" }
				}
			},
			{ "Sections", new Dictionary<string, string>
				{
					{ "zh-Hans", "段 · 节表" },
					{ "zh-Hant", "區段 · 節表" },
					{ "ja-JP", "セクション" },
					{ "ko-KR", "섹션" },
					{ "fr-FR", "Sections" },
					{ "de-DE", "Abschnitte" },
					{ "es-ES", "Secciones" }
				}
			},
			{ "Symbols", new Dictionary<string, string>
				{
					{ "zh-Hans", "导入导出" },
					{ "zh-Hant", "匯入匯出" },
					{ "ja-JP", "シンボル" },
					{ "ko-KR", "심볼" },
					{ "fr-FR", "Symboles" },
					{ "de-DE", "Symbole" },
					{ "es-ES", "Símbolos" }
				}
			},
			{ "Size", new Dictionary<string, string>
				{
					{ "zh-Hans", "体积构成" },
					{ "zh-Hant", "體積組成" },
					{ "ja-JP", "サイズ構成" },
					{ "ko-KR", "크기 구성" },
					{ "fr-FR", "Taille" },
					{ "de-DE", "Größe" },
					{ "es-ES", "Tamaño" }
				}
			},
			// 格式名
			{ "PE", new Dictionary<string, string>
				{
					{ "zh-Hans", "PE" },
					{ "zh-Hant", "PE" },
					{ "ja-JP", "PE" },
					{ "ko-KR", "PE" },
					{ "fr-FR", "PE" },
					{ "de-DE", "PE" },
					{ "es-ES", "PE" }
				}
			},
			{ "ELF", new Dictionary<string, string>
				{
					{ "zh-Hans", "ELF" },
					{ "zh-Hant", "ELF" },
					{ "ja-JP", "ELF" },
					{ "ko-KR", "ELF" },
					{ "fr-FR", "ELF" },
					{ "de-DE", "ELF" },
					{ "es-ES", "ELF" }
				}
			},
			{ "Mach-O", new Dictionary<string, string>
				{
					{ "zh-Hans", "Mach-O" },
					{ "zh-Hant", "Mach-O" },
					{ "ja-JP", "Mach-O" },
					{ "ko-KR", "Mach-O" },
					{ "fr-FR", "Mach-O" },
					{ "de-DE", "Mach-O" },
					{ "es-ES", "Mach-O" }
				}
			},
			{ "WebAssembly", new Dictionary<string, string>
				{
					{ "zh-Hans", "WebAssembly" },
					{ "zh-Hant", "WebAssembly" },
					{ "ja-JP", "WebAssembly" },
					{ "ko-KR", "WebAssembly" },
					{ "fr-FR", "WebAssembly" },
					{ "de-DE", "WebAssembly" },
					{ "es-ES", "WebAssembly" }
				}
			},
			{ "ar", new Dictionary<string, string>
				{
					{ "zh-Hans", "ar" },
					{ "zh-Hant", "ar" },
					{ "ja-JP", "ar" },
					{ "ko-KR", "ar" },
					{ "fr-FR", "ar" },
					{ "de-DE", "ar" },
					{ "es-ES", "ar" }
				}
			},
			{ "Unknown format", new Dictionary<string, string>
				{
					{ "zh-Hans", "未知格式" },
					{ "zh-Hant", "未知格式" },
					{ "ja-JP", "不明な形式" },
					{ "ko-KR", "알 수 없는 형식" },
					{ "fr-FR", "Format inconnu" },
					{ "de-DE", "Unbekanntes Format" },
					{ "es-ES", "Formato desconocido" }
				}
			},
			// 分组名
			{ "Header", new Dictionary<string, string>
				{
					{ "zh-Hans", "头部" },
					{ "zh-Hant", "標頭" },
					{ "ja-JP", "ヘッダー" },
					{ "ko-KR", "헤더" },
					{ "fr-FR", "En-tête" },
					{ "de-DE", "Kopf" },
					{ "es-ES", "Encabezado" }
				}
			},
			{ "Imports", new Dictionary<string, string>
				{
					{ "zh-Hans", "导入" },
					{ "zh-Hant", "匯入" },
					{ "ja-JP", "インポート" },
					{ "ko-KR", "가져오기" },
					{ "fr-FR", "Importations" },
					{ "de-DE", "Importe" },
					{ "es-ES", "Importaciones" }
				}
			},
			{ "Exports", new Dictionary<string, string>
				{
					{ "zh-Hans", "导出" },
					{ "zh-Hant", "匯出" },
					{ "ja-JP", "エクスポート" },
					{ "ko-KR", "내보내기" },
					{ "fr-FR", "Exportations" },
					{ "de-DE", "Exporte" },
					{ "es-ES", "Exportaciones" }
				}
			},
			{ "Dependencies", new Dictionary<string, string>
				{
					{ "zh-Hans", "依赖" },
					{ "zh-Hant", "相依" },
					{ "ja-JP", "依存関係" },
					{ "ko-KR", "종속성" },
					{ "fr-FR", "Dépendances" },
					{ "de-DE", "Abhängigkeiten" },
					{ "es-ES", "Dependencias" }
				}
			},
			{ "Metadata", new Dictionary<string, string>
				{
					{ "zh-Hans", "元数据" },
					{ "zh-Hant", "中繼資料" },
					{ "ja-JP", "メタデータ" },
					{ "ko-KR", "메타데이터" },
					{ "fr-FR", "Métadonnées" },
					{ "de-DE", "Metadaten" },
					{ "es-ES", "Metadatos" }
				}
			},
			{ "Assembly references", new Dictionary<string, string>
				{
					{ "zh-Hans", "程序集引用" },
					{ "zh-Hant", "組件參考" },
					{ "ja-JP", "アセンブリ参照" },
					{ "ko-KR", "어셈블리 참조" },
					{ "fr-FR", "Références d'assembly" },
					{ "de-DE", "Assemblyverweise" },
					{ "es-ES", "Referencias de ensamblado" }
				}
			},
			{ "Size composition", new Dictionary<string, string>
				{
					{ "zh-Hans", "体积构成" },
					{ "zh-Hant", "體積組成" },
					{ "ja-JP", "サイズ構成" },
					{ "ko-KR", "크기 구성" },
					{ "fr-FR", "Composition de la taille" },
					{ "de-DE", "Größenverteilung" },
					{ "es-ES", "Composición del tamaño" }
				}
			},
			// 字段名
			{ "Machine", new Dictionary<string, string>
				{
					{ "zh-Hans", "机器" },
					{ "zh-Hant", "機器" },
					{ "ja-JP", "マシン" },
					{ "ko-KR", "머신" },
					{ "fr-FR", "Machine" },
					{ "de-DE", "Maschine" },
					{ "es-ES", "Máquina" }
				}
			},
			{ "Bits", new Dictionary<string, string>
				{
					{ "zh-Hans", "位数" },
					{ "zh-Hant", "位元數" },
					{ "ja-JP", "ビット数" },
					{ "ko-KR", "비트 수" },
					{ "fr-FR", "Bits" },
					{ "de-DE", "Bits" },
					{ "es-ES", "Bits" }
				}
			},
			{ "Endianness", new Dictionary<string, string>
				{
					{ "zh-Hans", "字节序" },
					{ "zh-Hant", "位元組序" },
					{ "ja-JP", "エンディアン" },
					{ "ko-KR", "엔디언" },
					{ "fr-FR", "Boutisme" },
					{ "de-DE", "Byte-Reihenfolge" },
					{ "es-ES", "Endianness" }
				}
			},
			{ "Type", new Dictionary<string, string>
				{
					{ "zh-Hans", "类型" },
					{ "zh-Hant", "類型" },
					{ "ja-JP", "タイプ" },
					{ "ko-KR", "형식" },
					{ "fr-FR", "Type" },
					{ "de-DE", "Typ" },
					{ "es-ES", "Tipo" }
				}
			},
			{ "Subsystem", new Dictionary<string, string>
				{
					{ "zh-Hans", "子系统" },
					{ "zh-Hant", "子系統" },
					{ "ja-JP", "サブシステム" },
					{ "ko-KR", "하위 시스템" },
					{ "fr-FR", "Sous-système" },
					{ "de-DE", "Subsystem" },
					{ "es-ES", "Subsistema" }
				}
			},
			{ "DllCharacteristics", new Dictionary<string, string>
				{
					{ "zh-Hans", "DllCharacteristics" },
					{ "zh-Hant", "DllCharacteristics" },
					{ "ja-JP", "DllCharacteristics" },
					{ "ko-KR", "DllCharacteristics" },
					{ "fr-FR", "DllCharacteristics" },
					{ "de-DE", "DllCharacteristics" },
					{ "es-ES", "DllCharacteristics" }
				}
			},
			{ "Flags", new Dictionary<string, string>
				{
					{ "zh-Hans", "标志" },
					{ "zh-Hant", "旗標" },
					{ "ja-JP", "フラグ" },
					{ "ko-KR", "플래그" },
					{ "fr-FR", "Indicateurs" },
					{ "de-DE", "Flags" },
					{ "es-ES", "Indicadores" }
				}
			},
			{ "Entry point", new Dictionary<string, string>
				{
					{ "zh-Hans", "入口点" },
					{ "zh-Hant", "進入點" },
					{ "ja-JP", "エントリポイント" },
					{ "ko-KR", "진입점" },
					{ "fr-FR", "Point d'entrée" },
					{ "de-DE", "Einstiegspunkt" },
					{ "es-ES", "Punto de entrada" }
				}
			},
			{ "Program headers", new Dictionary<string, string>
				{
					{ "zh-Hans", "程序头" },
					{ "zh-Hant", "程式標頭" },
					{ "ja-JP", "プログラムヘッダー" },
					{ "ko-KR", "프로그램 헤더" },
					{ "fr-FR", "En-têtes de programme" },
					{ "de-DE", "Programmheader" },
					{ "es-ES", "Encabezados de programa" }
				}
			},
			{ "Class", new Dictionary<string, string>
				{
					{ "zh-Hans", "类别" },
					{ "zh-Hant", "類別" },
					{ "ja-JP", "クラス" },
					{ "ko-KR", "클래스" },
					{ "fr-FR", "Classe" },
					{ "de-DE", "Klasse" },
					{ "es-ES", "Clase" }
				}
			},
			{ "SONAME", new Dictionary<string, string>
				{
					{ "zh-Hans", "SONAME" },
					{ "zh-Hant", "SONAME" },
					{ "ja-JP", "SONAME" },
					{ "ko-KR", "SONAME" },
					{ "fr-FR", "SONAME" },
					{ "de-DE", "SONAME" },
					{ "es-ES", "SONAME" }
				}
			},
			{ "Platform", new Dictionary<string, string>
				{
					{ "zh-Hans", "平台" },
					{ "zh-Hant", "平台" },
					{ "ja-JP", "プラットフォーム" },
					{ "ko-KR", "플랫폼" },
					{ "fr-FR", "Plateforme" },
					{ "de-DE", "Plattform" },
					{ "es-ES", "Plataforma" }
				}
			},
			{ "UUID", new Dictionary<string, string>
				{
					{ "zh-Hans", "UUID" },
					{ "zh-Hant", "UUID" },
					{ "ja-JP", "UUID" },
					{ "ko-KR", "UUID" },
					{ "fr-FR", "UUID" },
					{ "de-DE", "UUID" },
					{ "es-ES", "UUID" }
				}
			},
			{ "Build ID", new Dictionary<string, string>
				{
					{ "zh-Hans", "构建 ID" },
					{ "zh-Hant", "建置 ID" },
					{ "ja-JP", "ビルド ID" },
					{ "ko-KR", "빌드 ID" },
					{ "fr-FR", "ID de build" },
					{ "de-DE", "Build-ID" },
					{ "es-ES", "ID de compilación" }
				}
			},
			{ "Architecture", new Dictionary<string, string>
				{
					{ "zh-Hans", "架构" },
					{ "zh-Hant", "架構" },
					{ "ja-JP", "アーキテクチャ" },
					{ "ko-KR", "아키텍처" },
					{ "fr-FR", "Architecture" },
					{ "de-DE", "Architektur" },
					{ "es-ES", "Arquitectura" }
				}
			},
			{ "Runtime", new Dictionary<string, string>
				{
					{ "zh-Hans", "运行时" },
					{ "zh-Hant", "執行階段" },
					{ "ja-JP", "ランタイム" },
					{ "ko-KR", "런타임" },
					{ "fr-FR", "Runtime" },
					{ "de-DE", "Laufzeit" },
					{ "es-ES", "Runtime" }
				}
			},
			{ "Assembly", new Dictionary<string, string>
				{
					{ "zh-Hans", "程序集" },
					{ "zh-Hant", "組件" },
					{ "ja-JP", "アセンブリ" },
					{ "ko-KR", "어셈블리" },
					{ "fr-FR", "Assembly" },
					{ "de-DE", "Assembly" },
					{ "es-ES", "Ensamblado" }
				}
			},
			{ "Types", new Dictionary<string, string>
				{
					{ "zh-Hans", "类型数" },
					{ "zh-Hant", "類型數" },
					{ "ja-JP", "型の数" },
					{ "ko-KR", "형식 수" },
					{ "fr-FR", "Types" },
					{ "de-DE", "Typen" },
					{ "es-ES", "Tipos" }
				}
			},
			{ "Methods", new Dictionary<string, string>
				{
					{ "zh-Hans", "方法数" },
					{ "zh-Hant", "方法數" },
					{ "ja-JP", "メソッド数" },
					{ "ko-KR", "메서드 수" },
					{ "fr-FR", "Méthodes" },
					{ "de-DE", "Methoden" },
					{ "es-ES", "Métodos" }
				}
			},
			{ "Type entries", new Dictionary<string, string>
				{
					{ "zh-Hans", "类型段条目" },
					{ "zh-Hant", "型別區段項目" },
					{ "ja-JP", "type セクション項目" },
					{ "ko-KR", "type 섹션 항목" },
					{ "fr-FR", "Entrées de type" },
					{ "de-DE", "Typ-Einträge" },
					{ "es-ES", "Entradas de tipo" }
				}
			},
			{ "Import entries", new Dictionary<string, string>
				{
					{ "zh-Hans", "导入段条目" },
					{ "zh-Hant", "匯入區段項目" },
					{ "ja-JP", "import セクション項目" },
					{ "ko-KR", "import 섹션 항목" },
					{ "fr-FR", "Entrées d'import" },
					{ "de-DE", "Import-Einträge" },
					{ "es-ES", "Entradas de importación" }
				}
			},
			{ "Export entries", new Dictionary<string, string>
				{
					{ "zh-Hans", "导出段条目" },
					{ "zh-Hant", "匯出區段項目" },
					{ "ja-JP", "export セクション項目" },
					{ "ko-KR", "export 섹션 항목" },
					{ "fr-FR", "Entrées d'export" },
					{ "de-DE", "Export-Einträge" },
					{ "es-ES", "Entradas de exportación" }
				}
			},
			{ "Code entries", new Dictionary<string, string>
				{
					{ "zh-Hans", "代码段条目" },
					{ "zh-Hant", "程式碼區段項目" },
					{ "ja-JP", "code セクション項目" },
					{ "ko-KR", "code 섹션 항목" },
					{ "fr-FR", "Entrées de code" },
					{ "de-DE", "Code-Einträge" },
					{ "es-ES", "Entradas de código" }
				}
			},
			{ "Data entries", new Dictionary<string, string>
				{
					{ "zh-Hans", "数据段条目" },
					{ "zh-Hant", "資料區段項目" },
					{ "ja-JP", "data セクション項目" },
					{ "ko-KR", "data 섹션 항목" },
					{ "fr-FR", "Entrées de données" },
					{ "de-DE", "Daten-Einträge" },
					{ "es-ES", "Entradas de datos" }
				}
			},
			{ "File size", new Dictionary<string, string>
				{
					{ "zh-Hans", "文件大小" },
					{ "zh-Hant", "檔案大小" },
					{ "ja-JP", "ファイルサイズ" },
					{ "ko-KR", "파일 크기" },
					{ "fr-FR", "Taille du fichier" },
					{ "de-DE", "Dateigröße" },
					{ "es-ES", "Tamaño del archivo" }
				}
			},
			{ "Offset", new Dictionary<string, string>
				{
					{ "zh-Hans", "偏移" },
					{ "zh-Hant", "偏移" },
					{ "ja-JP", "オフセット" },
					{ "ko-KR", "오프셋" },
					{ "fr-FR", "Décalage" },
					{ "de-DE", "Offset" },
					{ "es-ES", "Desplazamiento" }
				}
			},
			{ "Modified", new Dictionary<string, string>
				{
					{ "zh-Hans", "修改时间" },
					{ "zh-Hant", "修改時間" },
					{ "ja-JP", "更新日時" },
					{ "ko-KR", "수정 시각" },
					{ "fr-FR", "Modifié" },
					{ "de-DE", "Geändert" },
					{ "es-ES", "Modificado" }
				}
			},
			{ "Other", new Dictionary<string, string>
				{
					{ "zh-Hans", "其它" },
					{ "zh-Hant", "其它" },
					{ "ja-JP", "その他" },
					{ "ko-KR", "기타" },
					{ "fr-FR", "Autre" },
					{ "de-DE", "Sonstiges" },
					{ "es-ES", "Otros" }
				}
			},
			// 权限词
			{ "read", new Dictionary<string, string>
				{
					{ "zh-Hans", "读" },
					{ "zh-Hant", "讀" },
					{ "ja-JP", "読み取り" },
					{ "ko-KR", "읽기" },
					{ "fr-FR", "lecture" },
					{ "de-DE", "Lesen" },
					{ "es-ES", "lectura" }
				}
			},
			{ "write", new Dictionary<string, string>
				{
					{ "zh-Hans", "写" },
					{ "zh-Hant", "寫" },
					{ "ja-JP", "書き込み" },
					{ "ko-KR", "쓰기" },
					{ "fr-FR", "écriture" },
					{ "de-DE", "Schreiben" },
					{ "es-ES", "escritura" }
				}
			},
			{ "execute", new Dictionary<string, string>
				{
					{ "zh-Hans", "执行" },
					{ "zh-Hant", "執行" },
					{ "ja-JP", "実行" },
					{ "ko-KR", "실행" },
					{ "fr-FR", "exécution" },
					{ "de-DE", "Ausführen" },
					{ "es-ES", "ejecución" }
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
			{ "shared", new Dictionary<string, string>
				{
					{ "zh-Hans", "共有" },
					{ "zh-Hant", "共有" },
					{ "ja-JP", "共通" },
					{ "ko-KR", "공통" },
					{ "fr-FR", "commun" },
					{ "de-DE", "gemeinsam" },
					{ "es-ES", "común" }
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
			{ "Parsing binary…", new Dictionary<string, string>
				{
					{ "zh-Hans", "正在解析二进制…" },
					{ "zh-Hant", "正在解析二進位…" },
					{ "ja-JP", "バイナリを解析中…" },
					{ "ko-KR", "바이너리를 분석하는 중…" },
					{ "fr-FR", "Analyse du binaire…" },
					{ "de-DE", "Binärdatei wird analysiert…" },
					{ "es-ES", "Analizando el binario…" }
				}
			},
			{ "Binary compare: {0} / {1} sections", new Dictionary<string, string>
				{
					{ "zh-Hans", "二进制对比：{0} / {1} 个段" },
					{ "zh-Hant", "二進位對比：{0} / {1} 個區段" },
					{ "ja-JP", "バイナリ比較: {0} / {1} セクション" },
					{ "ko-KR", "바이너리 비교: {0} / {1}개 섹션" },
					{ "fr-FR", "Comparaison binaire : {0} / {1} sections" },
					{ "de-DE", "Binärvergleich: {0} / {1} Abschnitte" },
					{ "es-ES", "Comparación binaria: {0} / {1} secciones" }
				}
			},
			{ "Binary content unavailable", new Dictionary<string, string>
				{
					{ "zh-Hans", "二进制内容不可用" },
					{ "zh-Hant", "二進位內容無法使用" },
					{ "ja-JP", "バイナリの内容を利用できません" },
					{ "ko-KR", "바이너리 내용을 사용할 수 없습니다" },
					{ "fr-FR", "Contenu binaire indisponible" },
					{ "de-DE", "Binärinhalt nicht verfügbar" },
					{ "es-ES", "Contenido binario no disponible" }
				}
			},
			{ "Failed to parse binary", new Dictionary<string, string>
				{
					{ "zh-Hans", "解析二进制失败" },
					{ "zh-Hant", "解析二進位失敗" },
					{ "ja-JP", "バイナリの解析に失敗しました" },
					{ "ko-KR", "바이너리 분석 실패" },
					{ "fr-FR", "Échec de l'analyse du binaire" },
					{ "de-DE", "Binärdatei konnte nicht analysiert werden" },
					{ "es-ES", "No se pudo analizar el binario" }
				}
			},
			{ "Unsupported binary format", new Dictionary<string, string>
				{
					{ "zh-Hans", "不支持的二进制格式" },
					{ "zh-Hant", "不支援的二進位格式" },
					{ "ja-JP", "未対応のバイナリ形式" },
					{ "ko-KR", "지원되지 않는 바이너리 형식" },
					{ "fr-FR", "Format binaire non pris en charge" },
					{ "de-DE", "Nicht unterstütztes Binärformat" },
					{ "es-ES", "Formato binario no compatible" }
				}
			},
			{ "fat binary ({0} architectures)", new Dictionary<string, string>
				{
					{ "zh-Hans", "胖二进制（{0} 个架构）" },
					{ "zh-Hant", "胖二進位（{0} 個架構）" },
					{ "ja-JP", "ファットバイナリ（{0} アーキテクチャ）" },
					{ "ko-KR", "팻 바이너리({0}개 아키텍처)" },
					{ "fr-FR", "Binaire fat ({0} architectures)" },
					{ "de-DE", "Fat-Binärdatei ({0} Architekturen)" },
					{ "es-ES", "Binario fat ({0} arquitecturas)" }
				}
			},
			{ "Section composition", new Dictionary<string, string>
				{
					{ "zh-Hans", "段构成" },
					{ "zh-Hant", "區段組成" },
					{ "ja-JP", "セクション構成" },
					{ "ko-KR", "섹션 구성" },
					{ "fr-FR", "Composition des sections" },
					{ "de-DE", "Abschnittszusammensetzung" },
					{ "es-ES", "Composición de secciones" }
				}
			},
			{ "No entries", new Dictionary<string, string>
				{
					{ "zh-Hans", "无条目" },
					{ "zh-Hant", "無項目" },
					{ "ja-JP", "エントリなし" },
					{ "ko-KR", "항목 없음" },
					{ "fr-FR", "Aucune entrée" },
					{ "de-DE", "Keine Einträge" },
					{ "es-ES", "Sin entradas" }
				}
			},
			{ "No assembly references", new Dictionary<string, string>
				{
					{ "zh-Hans", "无程序集引用" },
					{ "zh-Hant", "無組件參考" },
					{ "ja-JP", "アセンブリ参照なし" },
					{ "ko-KR", "어셈블리 참조 없음" },
					{ "fr-FR", "Aucune référence d'assembly" },
					{ "de-DE", "Keine Assemblyverweise" },
					{ "es-ES", "Sin referencias de ensamblado" }
				}
			},
			{ "symbol table", new Dictionary<string, string>
				{
					{ "zh-Hans", "符号表" },
					{ "zh-Hant", "符號表" },
					{ "ja-JP", "シンボルテーブル" },
					{ "ko-KR", "심볼 테이블" },
					{ "fr-FR", "table des symboles" },
					{ "de-DE", "Symboltabelle" },
					{ "es-ES", "tabla de símbolos" }
				}
			},
			{ "long names", new Dictionary<string, string>
				{
					{ "zh-Hans", "长名表" },
					{ "zh-Hant", "長名表" },
					{ "ja-JP", "ロングネームテーブル" },
					{ "ko-KR", "긴 이름 테이블" },
					{ "fr-FR", "noms longs" },
					{ "de-DE", "lange Namen" },
					{ "es-ES", "nombres largos" }
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
