using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Video
{
	/// <summary>
	/// 视频对比插件的自带译文（元数据 + 界面文案）。
	///
	/// 约定：字典的 key 一律用**英文原文**，value 为「语言 code → 译文」；不写 "en"——
	/// 缺省语言即英文原文本身。查不到当前语言时由 <see cref="PluginLocalization.Resolve"/>
	/// 逐级回退（当前语言 → 语言主标签 → 英文 → 英文原文），漏译不会显示成空白。
	///
	/// 宿主已提供的通用词（old / new / created / removed 等）不在此表，走
	/// <see cref="PluginEnvironment.Translate"/>。
	/// </summary>
	internal static class VideoStrings
	{
		// ---- 元数据（宿主「偏好设置 → 插件」页展示） ----

		internal static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
		{
			{ "zh-Hans", "视频对比" },
			{ "zh-Hant", "視訊對比" },
			{ "ja-JP", "ビデオ比較" },
			{ "ko-KR", "비디오 비교" },
			{ "fr-FR", "Comparaison vidéo" },
			{ "de-DE", "Videovergleich" },
			{ "es-ES", "Comparación de vídeo" }
		};

		internal static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
		{
			{ "zh-Hans", "视频对比插件：认领 .mp4 / .mkv / .mov / .webm / .avi / .m4v / .mpg / .mpeg / .wmv / .flv，用 FFmpeg 只解码不播放，并排呈现元数据、关键帧帧条与单帧像素差异（复用宿主「高亮差异像素」偏好），差异逐行标注。单侧超过 300 MB 不预览。" },
			{ "zh-Hant", "視訊對比外掛：認領 .mp4 / .mkv / .mov / .webm / .avi / .m4v / .mpg / .mpeg / .wmv / .flv，以 FFmpeg 只解碼不播放，並排呈現中繼資料、關鍵影格影格條與單幀像素差異（沿用宿主「高亮差異像素」偏好），差異逐行標註。單側超過 300 MB 不預覽。" },
			{ "ja-JP", "ビデオ比較プラグイン: .mp4 / .mkv / .mov / .webm / .avi / .m4v / .mpg / .mpeg / .wmv / .flv を対象に、FFmpeg でデコードのみ行い、メタデータ・キーフレームのフィルムストリップ・単一フレームのピクセル差分（ホストの「差分ピクセルを強調」設定に連動）を並べて表示し、差分を行単位で注記します。片面が 300 MB を超える場合はプレビューしません。" },
			{ "ko-KR", "비디오 비교 플러그인: .mp4 / .mkv / .mov / .webm / .avi / .m4v / .mpg / .mpeg / .wmv / .flv를 처리하여 FFmpeg로 디코딩만 하고 메타데이터, 키프레임 필름스트립, 단일 프레임 픽셀 차이(호스트의 '차이 픽셀 강조' 설정 연동)를 나란히 표시하며 차이를 행 단위로 표시합니다. 한쪽이 300 MB를 넘으면 미리 보지 않습니다." },
			{ "fr-FR", "Plugin de comparaison vidéo : prend en charge .mp4 / .mkv / .mov / .webm / .avi / .m4v / .mpg / .mpeg / .wmv / .flv, décode sans lire avec FFmpeg et affiche côte à côte métadonnées, bande de vignettes de keyframes et différence de pixels sur une image (lié au réglage hôte « surligner les pixels différents »), avec les différences ligne par ligne. Aucun aperçu si un côté dépasse 300 Mo." },
			{ "de-DE", "Videovergleichs-Plugin: übernimmt .mp4 / .mkv / .mov / .webm / .avi / .m4v / .mpg / .mpeg / .wmv / .flv, dekodiert mit FFmpeg (ohne Wiedergabe) und zeigt Metadaten, Keyframe-Filmstreifen und Pixelunterschiede einzelner Frames nebeneinander (mit der Host-Einstellung „Unterschiedliche Pixel hervorheben“). Keine Vorschau, wenn eine Seite 300 MB überschreitet." },
			{ "es-ES", "Plugin de comparación de vídeo: admite .mp4 / .mkv / .mov / .webm / .avi / .m4v / .mpg / .mpeg / .wmv / .flv, decodifica sin reproducir con FFmpeg y muestra en paralelo metadatos, tira de fotogramas clave y diferencia de píxeles de un fotograma (ligado a la preferencia «resaltar píxeles distintos»), con las diferencias marcadas línea a línea. Sin vista previa si un lado supera 300 MB." }
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
			{ "Filmstrip", new Dictionary<string, string>
				{
					{ "zh-Hans", "帧条" }, { "zh-Hant", "影格條" }, { "ja-JP", "フィルムストリップ" },
					{ "ko-KR", "필름스트립" }, { "fr-FR", "Bande de vignettes" }, { "de-DE", "Filmstreifen" }, { "es-ES", "Tira de fotogramas" }
				}
			},
			{ "Frame compare", new Dictionary<string, string>
				{
					{ "zh-Hans", "单帧对比" }, { "zh-Hant", "單幀對比" }, { "ja-JP", "単一フレーム比較" },
					{ "ko-KR", "단일 프레임 비교" }, { "fr-FR", "Comparer une image" }, { "de-DE", "Einzelbild-Vergleich" }, { "es-ES", "Comparar fotograma" }
				}
			},

			// ---- 元数据分组 ----
			{ "Container", new Dictionary<string, string>
				{
					{ "zh-Hans", "容器" }, { "zh-Hant", "容器" }, { "ja-JP", "コンテナ" },
					{ "ko-KR", "컨테이너" }, { "fr-FR", "Conteneur" }, { "de-DE", "Container" }, { "es-ES", "Contenedor" }
				}
			},
			{ "Video streams", new Dictionary<string, string>
				{
					{ "zh-Hans", "视频流" }, { "zh-Hant", "視訊流" }, { "ja-JP", "ビデオストリーム" },
					{ "ko-KR", "비디오 스트림" }, { "fr-FR", "Pistes vidéo" }, { "de-DE", "Videospuren" }, { "es-ES", "Pistas de vídeo" }
				}
			},
			{ "Audio streams", new Dictionary<string, string>
				{
					{ "zh-Hans", "音频流" }, { "zh-Hant", "音訊流" }, { "ja-JP", "オーディオストリーム" },
					{ "ko-KR", "오디오 스트림" }, { "fr-FR", "Pistes audio" }, { "de-DE", "Audiospuren" }, { "es-ES", "Pistas de audio" }
				}
			},
			{ "Subtitles", new Dictionary<string, string>
				{
					{ "zh-Hans", "字幕流" }, { "zh-Hant", "字幕流" }, { "ja-JP", "字幕" },
					{ "ko-KR", "자막" }, { "fr-FR", "Sous-titres" }, { "de-DE", "Untertitel" }, { "es-ES", "Subtítulos" }
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
			{ "Resolution", new Dictionary<string, string> { { "zh-Hans", "分辨率" }, { "zh-Hant", "解析度" } } },
			{ "Pixel format", new Dictionary<string, string> { { "zh-Hans", "像素格式" }, { "zh-Hant", "像素格式" } } },
			{ "Color space", new Dictionary<string, string> { { "zh-Hans", "色彩空间" }, { "zh-Hant", "色彩空間" } } },
			{ "Frame rate", new Dictionary<string, string> { { "zh-Hans", "帧率" }, { "zh-Hant", "幀率" } } },
			{ "Sample rate", new Dictionary<string, string> { { "zh-Hans", "采样率" }, { "zh-Hant", "取樣率" } } },
			{ "Channels", new Dictionary<string, string> { { "zh-Hans", "声道数" }, { "zh-Hant", "聲道數" } } },
			{ "Channel layout", new Dictionary<string, string> { { "zh-Hans", "声道布局" }, { "zh-Hant", "聲道佈局" } } },
			{ "Language", new Dictionary<string, string> { { "zh-Hans", "语言" }, { "zh-Hant", "語言" } } },
			{ "Title", new Dictionary<string, string> { { "zh-Hans", "标题" }, { "zh-Hant", "標題" } } },
			{ "Artist", new Dictionary<string, string> { { "zh-Hans", "艺术家" }, { "zh-Hant", "演出者" } } },
			{ "Album", new Dictionary<string, string> { { "zh-Hans", "专辑" }, { "zh-Hant", "專輯" } } },
			{ "Comment", new Dictionary<string, string> { { "zh-Hans", "备注" }, { "zh-Hant", "備註" } } },
			{ "Encoder", new Dictionary<string, string> { { "zh-Hans", "编码库" }, { "zh-Hant", "編碼庫" } } },
			{ "Copyright", new Dictionary<string, string> { { "zh-Hans", "版权" }, { "zh-Hant", "版權" } } },
			{ "Cover art", new Dictionary<string, string> { { "zh-Hans", "内嵌封面" }, { "zh-Hant", "內嵌封面" } } },
			{ "Embedded", new Dictionary<string, string> { { "zh-Hans", "有" }, { "zh-Hant", "有" } } },

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
			{ "No video stream", new Dictionary<string, string>
				{
					{ "zh-Hans", "没有视频流" }, { "zh-Hant", "沒有視訊流" }, { "ja-JP", "ビデオストリームがありません" },
					{ "ko-KR", "비디오 스트림 없음" }, { "fr-FR", "Aucune piste vidéo" }, { "de-DE", "Keine Videospur" }, { "es-ES", "Sin pista de vídeo" }
				}
			},
			{ "Failed to decode media", new Dictionary<string, string>
				{
					{ "zh-Hans", "媒体解码失败" }, { "zh-Hant", "媒體解碼失敗" }, { "ja-JP", "メディアのデコードに失敗しました" },
					{ "ko-KR", "미디어 디코딩 실패" }, { "fr-FR", "Échec du décodage" }, { "de-DE", "Dekodierung fehlgeschlagen" }, { "es-ES", "Error al decodificar" }
				}
			},
			{ "No frames available", new Dictionary<string, string>
				{
					{ "zh-Hans", "无可用帧" }, { "zh-Hant", "無可用影格" }, { "ja-JP", "利用できるフレームがありません" },
					{ "ko-KR", "사용 가능한 프레임 없음" }, { "fr-FR", "Aucune image disponible" }, { "de-DE", "Keine Bilder verfügbar" }, { "es-ES", "Sin fotogramas disponibles" }
				}
			},
			{ "No frame available", new Dictionary<string, string>
				{
					{ "zh-Hans", "该时间点无帧" }, { "zh-Hant", "該時間點無影格" }, { "ja-JP", "この時点のフレームがありません" },
					{ "ko-KR", "해당 시점의 프레임 없음" }, { "fr-FR", "Aucune image à cet instant" }, { "de-DE", "Kein Bild zu diesem Zeitpunkt" }, { "es-ES", "Sin fotograma en ese instante" }
				}
			},
			{ "Frame sizes differ", new Dictionary<string, string>
				{
					{ "zh-Hans", "两帧尺寸不同，跳过像素差异" }, { "zh-Hant", "兩幀尺寸不同，略過像素差異" }, { "ja-JP", "フレームサイズが異なるためピクセル差分を省略します" },
					{ "ko-KR", "프레임 크기가 달라 픽셀 차이를 건너뜁니다" }, { "fr-FR", "Tailles d'image différentes : différence de pixels ignorée" }, { "de-DE", "Bildgrößen unterscheiden sich – Pixelvergleich übersprungen" }, { "es-ES", "Los tamaños difieren: se omite la diferencia de píxeles" }
				}
			},
			{ "Difference", new Dictionary<string, string>
				{
					{ "zh-Hans", "差异" }, { "zh-Hant", "差異" }, { "ja-JP", "差分" },
					{ "ko-KR", "차이" }, { "fr-FR", "Différence" }, { "de-DE", "Unterschied" }, { "es-ES", "Diferencia" }
				}
			},
			{ "Position", new Dictionary<string, string>
				{
					{ "zh-Hans", "位置" }, { "zh-Hant", "位置" }, { "ja-JP", "位置" },
					{ "ko-KR", "위치" }, { "fr-FR", "Position" }, { "de-DE", "Position" }, { "es-ES", "Posición" }
				}
			},
			{ "Pixel difference", new Dictionary<string, string>
				{
					{ "zh-Hans", "像素差异" }, { "zh-Hant", "像素差異" }, { "ja-JP", "ピクセル差分" },
					{ "ko-KR", "픽셀 차이" }, { "fr-FR", "Différence de pixels" }, { "de-DE", "Pixelunterschied" }, { "es-ES", "Diferencia de píxeles" }
				}
			},
			{ "Filmstrip comparison", new Dictionary<string, string>
				{
					{ "zh-Hans", "帧条对比" }, { "zh-Hant", "影格條對比" }, { "ja-JP", "フィルムストリップの比較" },
					{ "ko-KR", "필름스트립 비교" }, { "fr-FR", "Comparaison des vignettes" }, { "de-DE", "Filmstreifen-Vergleich" }, { "es-ES", "Comparación de tiras" }
				}
			},
			{ "Frame comparison", new Dictionary<string, string>
				{
					{ "zh-Hans", "单帧对比" }, { "zh-Hant", "單幀對比" }, { "ja-JP", "単一フレーム比較" },
					{ "ko-KR", "단일 프레임 비교" }, { "fr-FR", "Comparaison d'image" }, { "de-DE", "Einzelbild-Vergleich" }, { "es-ES", "Comparación de fotograma" }
				}
			},
			{ "Highlighting is off", new Dictionary<string, string>
				{
					{ "zh-Hans", "「高亮差异像素」当前关闭：变了的像素未染色" }, { "zh-Hant", "「高亮差異像素」目前關閉：變了的像素未染色" }, { "ja-JP", "「差分ピクセルを強調」がオフ: 変更ピクセルは着色されません" },
					{ "ko-KR", "'차이 픽셀 강조'가 꺼져 있음: 변경된 픽셀은 색칠되지 않습니다" }, { "fr-FR", "« Surligner les pixels différents » est désactivé : les pixels modifiés ne sont pas colorés" }, { "de-DE", "„Unterschiedliche Pixel hervorheben“ ist aus: geänderte Pixel sind nicht eingefärbt" }, { "es-ES", "«Resaltar píxeles distintos» está desactivado: los píxeles modificados no se colorean" }
				}
			},
			{ "Video compare: {0}", new Dictionary<string, string>
				{
					{ "zh-Hans", "视频对比：{0}" }, { "zh-Hant", "視訊對比：{0}" }, { "ja-JP", "ビデオ比較: {0}" },
					{ "ko-KR", "비디오 비교: {0}" }, { "fr-FR", "Comparaison vidéo : {0}" }, { "de-DE", "Videovergleich: {0}" }, { "es-ES", "Comparación de vídeo: {0}" }
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
			{ "{0} fps", new Dictionary<string, string>
				{
					{ "zh-Hans", "{0} fps" }, { "zh-Hant", "{0} fps" }, { "ja-JP", "{0} fps" },
					{ "ko-KR", "{0} fps" }, { "fr-FR", "{0} i/s" }, { "de-DE", "{0} fps" }, { "es-ES", "{0} fps" }
				}
			},
			{ "{0} × {1}", new Dictionary<string, string>
				{
					{ "zh-Hans", "{0} × {1}" }, { "zh-Hant", "{0} × {1}" }, { "ja-JP", "{0} × {1}" },
					{ "ko-KR", "{0} × {1}" }, { "fr-FR", "{0} × {1}" }, { "de-DE", "{0} × {1}" }, { "es-ES", "{0} × {1}" }
				}
			},
			{ "{0} frames", new Dictionary<string, string>
				{
					{ "zh-Hans", "{0} 帧" }, { "zh-Hant", "{0} 幀" }, { "ja-JP", "{0} フレーム" },
					{ "ko-KR", "{0} 프레임" }, { "fr-FR", "{0} images" }, { "de-DE", "{0} Bilder" }, { "es-ES", "{0} fotogramas" }
				}
			},
			{ "{0}% pixels changed", new Dictionary<string, string>
				{
					{ "zh-Hans", "{0}% 像素变了" }, { "zh-Hant", "{0}% 像素變了" }, { "ja-JP", "{0}% のピクセルが変化" },
					{ "ko-KR", "{0}% 픽셀 변경" }, { "fr-FR", "{0} % de pixels modifiés" }, { "de-DE", "{0} % Pixel geändert" }, { "es-ES", "{0} % de píxeles cambiados" }
				}
			},
			{ "Frame at {0} s", new Dictionary<string, string>
				{
					{ "zh-Hans", "{0} 秒处的一帧" }, { "zh-Hant", "{0} 秒處的一幀" }, { "ja-JP", "{0} 秒時点のフレーム" },
					{ "ko-KR", "{0}초 지점의 프레임" }, { "fr-FR", "Image à {0} s" }, { "de-DE", "Bild bei {0} s" }, { "es-ES", "Fotograma en {0} s" }
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
