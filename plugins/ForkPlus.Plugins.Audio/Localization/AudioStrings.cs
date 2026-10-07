using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Audio
{
	/// <summary>
	/// 音频对比插件的自带译文（元数据 + 界面文案）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），漏译不会显示成空白。
	///
	/// 语言 code 与宿主界面语言一致：en / zh-Hans / zh-Hant / ja-JP / ko-KR / fr-FR / de-DE / es-ES。
	/// 宿主已提供的通用词（old / new / created / removed 等）不在此表，走
	/// <see cref="PluginEnvironment.Translate"/>。
	/// </summary>
	internal static class AudioStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "音频对比" },
			{ "zh-Hant", "音訊對比" },
			{ "ja-JP", "オーディオ比較" },
			{ "ko-KR", "오디오 비교" },
			{ "fr-FR", "Comparaison audio" },
			{ "de-DE", "Audiovergleich" },
			{ "es-ES", "Comparación de audio" }
		};

		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "音频对比插件：认领 .mp3 / .wav / .flac / .ogg / .oga / .opus / .m4a / .aac / .wma，用 FFmpeg 解码后并排呈现元数据、波形包络、声谱图与内嵌封面，差异逐行标注；波形 / 频谱两模式下可就地试听旧 / 新两侧（播放 / 暂停 + 进度定位），音频输出走 miniaudio。单侧超过 300 MB 不预览。" },
			{ "zh-Hant", "音訊對比外掛：認領 .mp3 / .wav / .flac / .ogg / .oga / .opus / .m4a / .aac / .wma，以 FFmpeg 解碼後並排呈現中繼資料、波形包絡、聲譜圖與內嵌封面，差異逐行標註；波形 / 頻譜兩模式下可就地試聽舊 / 新兩側（播放 / 暫停 + 進度定位），音訊輸出走 miniaudio。單側超過 300 MB 不預覽。" },
			{ "ja-JP", "オーディオ比較プラグイン: .mp3 / .wav / .flac / .ogg / .oga / .opus / .m4a / .aac / .wma を対象に、FFmpeg でデコードし、メタデータ・波形エンベロープ・スペクトログラム・埋め込みカバーを並べて表示し、差分を行単位で注記します。波形 / スペクトルモードでは新旧どちらもその場で試聴でき（再生 / 一時停止 + シーク）、音声出力は miniaudio を使用します。片面が 300 MB を超える場合はプレビューしません。" },
			{ "ko-KR", "오디오 비교 플러그인: .mp3 / .wav / .flac / .ogg / .oga / .opus / .m4a / .aac / .wma를 처리하여 FFmpeg로 디코딩하고 메타데이터, 파형 포락선, 스펙트로그램, 내장 커버를 나란히 표시하며 차이를 행 단위로 표시합니다. 파형 / 스펙트럼 모드에서는 이전 / 이후 어느 쪽이든 바로 들어볼 수 있으며(재생 / 일시정지 + 탐색) 오디오 출력은 miniaudio를 사용합니다. 한쪽이 300 MB를 넘으면 미리 보지 않습니다." },
			{ "fr-FR", "Plugin de comparaison audio : prend en charge .mp3 / .wav / .flac / .ogg / .oga / .opus / .m4a / .aac / .wma, décode avec FFmpeg et affiche côte à côte métadonnées, enveloppe d'onde, spectrogramme et pochette intégrée, avec les différences ligne par ligne ; dans les modes forme d'onde / spectre, écoute de l'ancien ou du nouveau côté (lecture / pause + position), sortie audio via miniaudio. Aucun aperçu si un côté dépasse 300 Mo." },
			{ "de-DE", "Audiovergleichs-Plugin: übernimmt .mp3 / .wav / .flac / .ogg / .oga / .opus / .m4a / .aac / .wma, dekodiert mit FFmpeg und zeigt Metadaten, Wellenform-Hüllkurve, Spektrogramm und eingebettetes Cover nebeneinander, Unterschiede zeilenweise markiert; in den Modi Wellenform / Spektrum lässt sich die alte oder neue Seite direkt anhören (Wiedergabe / Pause + Position), Audioausgabe über miniaudio. Keine Vorschau, wenn eine Seite 300 MB überschreitet." },
			{ "es-ES", "Plugin de comparación de audio: admite .mp3 / .wav / .flac / .ogg / .oga / .opus / .m4a / .aac / .wma, decodifica con FFmpeg y muestra en paralelo metadatos, envolvente de forma de onda, espectrograma y carátula incrustada, con las diferencias marcadas línea a línea; en los modos de forma de onda / espectro permite escuchar el lado antiguo o el nuevo (reproducir / pausar + posición), con salida de audio vía miniaudio. Sin vista previa si un lado supera 300 MB." }
		};

		// ---- 界面文案（key = 英文原文；含 {0} 的走 F(...) 格式化） ----

		private static readonly Dictionary<string, Dictionary<string, string>> Table = new Dictionary<string, Dictionary<string, string>>
		{
			// ---- 视图模式 ----
			{ "Metadata", new Dictionary<string, string>
				{
					{ "zh-Hans", "元数据" }, { "zh-Hant", "中繼資料" }, { "ja-JP", "メタデータ" },
					{ "ko-KR", "메타데이터" }, { "fr-FR", "Métadonnées" }, { "de-DE", "Metadaten" }, { "es-ES", "Metadatos" }
				}
			},
			{ "Waveform", new Dictionary<string, string>
				{
					{ "zh-Hans", "波形" }, { "zh-Hant", "波形" }, { "ja-JP", "波形" },
					{ "ko-KR", "파형" }, { "fr-FR", "Forme d'onde" }, { "de-DE", "Wellenform" }, { "es-ES", "Forma de onda" }
				}
			},
			{ "Spectrum", new Dictionary<string, string>
				{
					{ "zh-Hans", "频谱" }, { "zh-Hant", "頻譜" }, { "ja-JP", "スペクトル" },
					{ "ko-KR", "스펙트럼" }, { "fr-FR", "Spectre" }, { "de-DE", "Spektrum" }, { "es-ES", "Espectro" }
				}
			},
			{ "Cover", new Dictionary<string, string>
				{
					{ "zh-Hans", "封面" }, { "zh-Hant", "封面" }, { "ja-JP", "カバー" },
					{ "ko-KR", "커버" }, { "fr-FR", "Pochette" }, { "de-DE", "Cover" }, { "es-ES", "Carátula" }
				}
			},

			// ---- 试听 / 播放（传输条） ----
			{ "Play", new Dictionary<string, string>
				{
					{ "zh-Hans", "播放" }, { "zh-Hant", "播放" }, { "ja-JP", "再生" },
					{ "ko-KR", "재생" }, { "fr-FR", "Lecture" }, { "de-DE", "Wiedergabe" }, { "es-ES", "Reproducir" }
				}
			},
			{ "Pause", new Dictionary<string, string>
				{
					{ "zh-Hans", "暂停" }, { "zh-Hant", "暫停" }, { "ja-JP", "一時停止" },
					{ "ko-KR", "일시정지" }, { "fr-FR", "Pause" }, { "de-DE", "Pause" }, { "es-ES", "Pausar" }
				}
			},
			{ "Audition", new Dictionary<string, string>
				{
					{ "zh-Hans", "试听" }, { "zh-Hant", "試聽" }, { "ja-JP", "試聴" },
					{ "ko-KR", "미리 듣기" }, { "fr-FR", "Écoute" }, { "de-DE", "Anhören" }, { "es-ES", "Escuchar" }
				}
			},
			{ "Audio output unavailable", new Dictionary<string, string>
				{
					{ "zh-Hans", "音频输出不可用" }, { "zh-Hant", "音訊輸出不可用" }, { "ja-JP", "オーディオ出力を利用できません" },
					{ "ko-KR", "오디오 출력을 사용할 수 없음" }, { "fr-FR", "Sortie audio indisponible" }, { "de-DE", "Audioausgabe nicht verfügbar" }, { "es-ES", "Salida de audio no disponible" }
				}
			},

			// ---- 元数据分组 ----
			{ "Container", new Dictionary<string, string>
				{
					{ "zh-Hans", "容器" }, { "zh-Hant", "容器" }, { "ja-JP", "コンテナ" },
					{ "ko-KR", "컨테이너" }, { "fr-FR", "Conteneur" }, { "de-DE", "Container" }, { "es-ES", "Contenedor" }
				}
			},
			{ "Audio streams", new Dictionary<string, string>
				{
					{ "zh-Hans", "音频流" }, { "zh-Hant", "音訊流" }, { "ja-JP", "オーディオストリーム" },
					{ "ko-KR", "오디오 스트림" }, { "fr-FR", "Pistes audio" }, { "de-DE", "Audiospuren" }, { "es-ES", "Pistas de audio" }
				}
			},
			{ "Tags", new Dictionary<string, string>
				{
					{ "zh-Hans", "标签" }, { "zh-Hant", "標籤" }, { "ja-JP", "タグ" },
					{ "ko-KR", "태그" }, { "fr-FR", "Étiquettes" }, { "de-DE", "Tags" }, { "es-ES", "Etiquetas" }
				}
			},

			// ---- 元数据行（仅中英；其余语言回退英文原文） ----
			{ "Format", new Dictionary<string, string> { { "zh-Hans", "格式" }, { "zh-Hant", "格式" } } },
			{ "Long name", new Dictionary<string, string> { { "zh-Hans", "格式全名" }, { "zh-Hant", "格式全名" } } },
			{ "Duration", new Dictionary<string, string> { { "zh-Hans", "时长" }, { "zh-Hant", "時長" } } },
			{ "Bit rate", new Dictionary<string, string> { { "zh-Hans", "码率" }, { "zh-Hant", "位元率" } } },
			{ "Size", new Dictionary<string, string> { { "zh-Hans", "大小" }, { "zh-Hant", "大小" } } },
			{ "Streams", new Dictionary<string, string> { { "zh-Hans", "流数量" }, { "zh-Hant", "串流數" } } },
			{ "Stream", new Dictionary<string, string> { { "zh-Hans", "流" }, { "zh-Hant", "串流" } } },
			{ "Codec", new Dictionary<string, string> { { "zh-Hans", "编码器" }, { "zh-Hant", "編碼器" } } },
			{ "Sample rate", new Dictionary<string, string> { { "zh-Hans", "采样率" }, { "zh-Hant", "取樣率" } } },
			{ "Channels", new Dictionary<string, string> { { "zh-Hans", "声道数" }, { "zh-Hant", "聲道數" } } },
			{ "Channel layout", new Dictionary<string, string> { { "zh-Hans", "声道布局" }, { "zh-Hant", "聲道佈局" } } },
			{ "Language", new Dictionary<string, string> { { "zh-Hans", "语言" }, { "zh-Hant", "語言" } } },
			{ "Title", new Dictionary<string, string> { { "zh-Hans", "标题" }, { "zh-Hant", "標題" } } },
			{ "Artist", new Dictionary<string, string> { { "zh-Hans", "艺术家" }, { "zh-Hant", "演出者" } } },
			{ "Album", new Dictionary<string, string> { { "zh-Hans", "专辑" }, { "zh-Hant", "專輯" } } },
			{ "Album artist", new Dictionary<string, string> { { "zh-Hans", "专辑艺术家" }, { "zh-Hant", "專輯演出者" } } },
			{ "Track", new Dictionary<string, string> { { "zh-Hans", "音轨" }, { "zh-Hant", "音軌" } } },
			{ "Genre", new Dictionary<string, string> { { "zh-Hans", "流派" }, { "zh-Hant", "曲風" } } },
			{ "Year", new Dictionary<string, string> { { "zh-Hans", "年份" }, { "zh-Hant", "年份" } } },
			{ "Comment", new Dictionary<string, string> { { "zh-Hans", "备注" }, { "zh-Hant", "備註" } } },
			{ "Composer", new Dictionary<string, string> { { "zh-Hans", "作曲" }, { "zh-Hant", "作曲" } } },
			{ "Encoder", new Dictionary<string, string> { { "zh-Hans", "编码库" }, { "zh-Hant", "編碼庫" } } },
			{ "Copyright", new Dictionary<string, string> { { "zh-Hans", "版权" }, { "zh-Hant", "版權" } } },
			{ "Cover art", new Dictionary<string, string> { { "zh-Hans", "内嵌封面" }, { "zh-Hant", "內嵌封面" } } },
			{ "Embedded", new Dictionary<string, string> { { "zh-Hans", "有" }, { "zh-Hant", "有" } } },
			{ "Pictures", new Dictionary<string, string> { { "zh-Hans", "图片数" }, { "zh-Hant", "圖片數" } } },

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

			// ---- 消息 / 标签 ----
			{ "Analyzing…", new Dictionary<string, string>
				{
					{ "zh-Hans", "正在分析…" }, { "zh-Hant", "正在分析…" }, { "ja-JP", "解析中…" },
					{ "ko-KR", "분석 중…" }, { "fr-FR", "Analyse…" }, { "de-DE", "Analyse…" }, { "es-ES", "Analizando…" }
				}
			},
			{ "Media content unavailable", new Dictionary<string, string>
				{
					{ "zh-Hans", "媒体内容不可用" }, { "zh-Hant", "媒體內容不可用" }, { "ja-JP", "メディア内容を取得できません" },
					{ "ko-KR", "미디어 내용을 사용할 수 없음" }, { "fr-FR", "Contenu multimédia indisponible" }, { "de-DE", "Medieninhalt nicht verfügbar" }, { "es-ES", "Contenido multimedia no disponible" }
				}
			},
			{ "FFmpeg decoding unavailable", new Dictionary<string, string>
				{
					{ "zh-Hans", "FFmpeg 解码不可用" }, { "zh-Hant", "FFmpeg 解碼不可用" }, { "ja-JP", "FFmpeg デコードを利用できません" },
					{ "ko-KR", "FFmpeg 디코딩을 사용할 수 없음" }, { "fr-FR", "Décodage FFmpeg indisponible" }, { "de-DE", "FFmpeg-Dekodierung nicht verfügbar" }, { "es-ES", "Decodificación FFmpeg no disponible" }
				}
			},
			{ "File too large to preview", new Dictionary<string, string>
				{
					{ "zh-Hans", "文件过大，不预览" }, { "zh-Hant", "檔案過大，不預覽" }, { "ja-JP", "ファイルが大きすぎるためプレビューしません" },
					{ "ko-KR", "파일이 너무 커서 미리 보지 않음" }, { "fr-FR", "Fichier trop volumineux pour l'aperçu" }, { "de-DE", "Datei zu groß für die Vorschau" }, { "es-ES", "Archivo demasiado grande para la vista previa" }
				}
			},
			{ "Unsupported media format", new Dictionary<string, string>
				{
					{ "zh-Hans", "不支持的媒体格式" }, { "zh-Hant", "不支援的媒體格式" }, { "ja-JP", "未対応のメディア形式" },
					{ "ko-KR", "지원되지 않는 미디어 형식" }, { "fr-FR", "Format multimédia non pris en charge" }, { "de-DE", "Nicht unterstütztes Medienformat" }, { "es-ES", "Formato multimedia no compatible" }
				}
			},
			{ "No audio stream", new Dictionary<string, string>
				{
					{ "zh-Hans", "没有音频流" }, { "zh-Hant", "沒有音訊流" }, { "ja-JP", "オーディオストリームがありません" },
					{ "ko-KR", "오디오 스트림 없음" }, { "fr-FR", "Aucune piste audio" }, { "de-DE", "Keine Audiospur" }, { "es-ES", "Sin pista de audio" }
				}
			},
			{ "Failed to decode media", new Dictionary<string, string>
				{
					{ "zh-Hans", "媒体解码失败" }, { "zh-Hant", "媒體解碼失敗" }, { "ja-JP", "メディアのデコードに失敗しました" },
					{ "ko-KR", "미디어 디코딩 실패" }, { "fr-FR", "Échec du décodage" }, { "de-DE", "Dekodierung fehlgeschlagen" }, { "es-ES", "Error al decodificar" }
				}
			},
			{ "No waveform data", new Dictionary<string, string>
				{
					{ "zh-Hans", "无波形数据" }, { "zh-Hant", "無波形資料" }, { "ja-JP", "波形データがありません" },
					{ "ko-KR", "파형 데이터 없음" }, { "fr-FR", "Aucune donnée de forme d'onde" }, { "de-DE", "Keine Wellenformdaten" }, { "es-ES", "Sin datos de forma de onda" }
				}
			},
			{ "No spectrum data", new Dictionary<string, string>
				{
					{ "zh-Hans", "无频谱数据" }, { "zh-Hant", "無頻譜資料" }, { "ja-JP", "スペクトルデータがありません" },
					{ "ko-KR", "스펙트럼 데이터 없음" }, { "fr-FR", "Aucune donnée de spectre" }, { "de-DE", "Keine Spektrumdaten" }, { "es-ES", "Sin datos de espectro" }
				}
			},
			{ "No embedded cover", new Dictionary<string, string>
				{
					{ "zh-Hans", "无内嵌封面" }, { "zh-Hant", "無內嵌封面" }, { "ja-JP", "埋め込みカバーはありません" },
					{ "ko-KR", "내장 커버 없음" }, { "fr-FR", "Aucune pochette intégrée" }, { "de-DE", "Kein eingebettetes Cover" }, { "es-ES", "Sin carátula incrustada" }
				}
			},
			{ "Difference", new Dictionary<string, string>
				{
					{ "zh-Hans", "差异" }, { "zh-Hant", "差異" }, { "ja-JP", "差分" },
					{ "ko-KR", "차이" }, { "fr-FR", "Différence" }, { "de-DE", "Unterschied" }, { "es-ES", "Diferencia" }
				}
			},
			{ "Waveform comparison", new Dictionary<string, string>
				{
					{ "zh-Hans", "波形对比" }, { "zh-Hant", "波形對比" }, { "ja-JP", "波形の比較" },
					{ "ko-KR", "파형 비교" }, { "fr-FR", "Comparaison des formes d'onde" }, { "de-DE", "Wellenform-Vergleich" }, { "es-ES", "Comparación de formas de onda" }
				}
			},
			{ "Spectrum comparison", new Dictionary<string, string>
				{
					{ "zh-Hans", "频谱对比" }, { "zh-Hant", "頻譜對比" }, { "ja-JP", "スペクトルの比較" },
					{ "ko-KR", "스펙트럼 비교" }, { "fr-FR", "Comparaison des spectres" }, { "de-DE", "Spektrum-Vergleich" }, { "es-ES", "Comparación de espectros" }
				}
			},
			{ "Cover comparison", new Dictionary<string, string>
				{
					{ "zh-Hans", "封面对比" }, { "zh-Hant", "封面對比" }, { "ja-JP", "カバーの比較" },
					{ "ko-KR", "커버 비교" }, { "fr-FR", "Comparaison des pochettes" }, { "de-DE", "Cover-Vergleich" }, { "es-ES", "Comparación de carátulas" }
				}
			},
			{ "Audio compare: {0}", new Dictionary<string, string>
				{
					{ "zh-Hans", "音频对比：{0}" }, { "zh-Hant", "音訊對比：{0}" }, { "ja-JP", "オーディオ比較: {0}" },
					{ "ko-KR", "오디오 비교: {0}" }, { "fr-FR", "Comparaison audio : {0}" }, { "de-DE", "Audiovergleich: {0}" }, { "es-ES", "Comparación de audio: {0}" }
				}
			},
			{ "Analyzed first {0} of {1} s", new Dictionary<string, string>
				{
					{ "zh-Hans", "仅分析前 {0} / {1} 秒" }, { "zh-Hant", "僅分析前 {0} / {1} 秒" }, { "ja-JP", "先頭 {0} / {1} 秒のみ解析" },
					{ "ko-KR", "처음 {0} / {1}초만 분석" }, { "fr-FR", "Analyse des {0} premières secondes sur {1}" }, { "de-DE", "Nur die ersten {0} von {1} s analysiert" }, { "es-ES", "Solo se analizaron los primeros {0} de {1} s" }
				}
			},
			{ "{0} Hz", new Dictionary<string, string>
				{
					{ "zh-Hans", "{0} Hz" }, { "zh-Hant", "{0} Hz" }, { "ja-JP", "{0} Hz" },
					{ "ko-KR", "{0} Hz" }, { "fr-FR", "{0} Hz" }, { "de-DE", "{0} Hz" }, { "es-ES", "{0} Hz" }
				}
			},
			{ "{0} kbps", new Dictionary<string, string>
				{
					{ "zh-Hans", "{0} kbps" }, { "zh-Hant", "{0} kbps" }, { "ja-JP", "{0} kbps" },
					{ "ko-KR", "{0} kbps" }, { "fr-FR", "{0} kbps" }, { "de-DE", "{0} kbps" }, { "es-ES", "{0} kbps" }
				}
			},
			{ "{0} ch", new Dictionary<string, string>
				{
					{ "zh-Hans", "{0} 声道" }, { "zh-Hant", "{0} 聲道" }, { "ja-JP", "{0} ch" },
					{ "ko-KR", "{0} ch" }, { "fr-FR", "{0} canaux" }, { "de-DE", "{0} Kanäle" }, { "es-ES", "{0} canales" }
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
