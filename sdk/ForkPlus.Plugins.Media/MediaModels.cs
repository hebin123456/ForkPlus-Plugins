using System.Collections.Generic;

namespace ForkPlus.Plugins.Media
{
	/// <summary>分析额度上限（沿用仓库既有的「双上限」思路，见 Archive 的 HashBudget / Office 的 MaxSheetRows）。</summary>
	public static class MediaLimits
	{
		/// <summary>单侧声明大小超过该值不渲染媒体内容，只给提示（design §5）。</summary>
		public const long MaxSideBytes = 300L * 1024L * 1024L;

		/// <summary>波形最多分桶数。</summary>
		public const int MaxWaveBuckets = 2048;

		/// <summary>波形分析时的单声道重采样率（够画包络，省内存）。</summary>
		public const int WaveformSampleRate = 8000;

		/// <summary>音频最多分析秒数（超出截断并在界面注明）。</summary>
		public const double MaxAudioSeconds = 600.0;

		/// <summary>频谱最多分析秒数（STFT 需缓存降采样后的样本，故比波形更保守）。</summary>
		public const double MaxSpectrumSeconds = 120.0;

		/// <summary>频谱时间列数。</summary>
		public const int MaxSpectrumColumns = 720;

		/// <summary>频谱频率行数（对数分箱后）。</summary>
		public const int SpectrumRows = 128;

		/// <summary>STFT 窗长。</summary>
		public const int FftSize = 1024;

		/// <summary>频谱分析时的单声道重采样率。</summary>
		public const int SpectrumSampleRate = 16000;

		/// <summary>帧条最多取帧数。</summary>
		public const int MaxFilmstripFrames = 8;

		/// <summary>帧条缩略图目标宽度。</summary>
		public const int FilmstripWidth = 240;

		/// <summary>单帧对比目标宽度上限。</summary>
		public const int FrameCompareWidth = 720;

		/// <summary>单帧对比目标高度上限。</summary>
		public const int FrameCompareHeight = 480;

		/// <summary>像素差异判定阈值（0–255，逐通道最大差）。</summary>
		public const int PixelDiffThreshold = 24;
	}

	/// <summary>一条流（音频 / 视频 / 字幕 / 封面）。</summary>
	public sealed class MediaStreamInfo
	{
		public int Index { get; set; }

		/// <summary>"audio" / "video" / "subtitle" / "data" / "attachment"。</summary>
		public string Kind { get; set; }

		public string CodecName { get; set; }

		public string CodecLongName { get; set; }

		public long BitRate { get; set; }

		public double DurationSeconds { get; set; }

		public int SampleRate { get; set; }

		public int Channels { get; set; }

		public string ChannelLayout { get; set; }

		public int Width { get; set; }

		public int Height { get; set; }

		public string PixelFormat { get; set; }

		public string ColorSpace { get; set; }

		public double FrameRate { get; set; }

		/// <summary>是否内嵌封面（ID3 APIC / mp4 covr）。</summary>
		public bool IsAttachedPicture { get; set; }

		public Dictionary<string, string> Tags { get; } = new Dictionary<string, string>();
	}

	/// <summary>解码出来的 BGRA 位图（顶层向下，stride 已紧凑）。</summary>
	public sealed class ImageData
	{
		public byte[] Bgra { get; set; }

		public int Width { get; set; }

		public int Height { get; set; }

		public int Stride => Width * 4;
	}

	/// <summary>一个媒体文件的探测结果（不解全量）。</summary>
	public sealed class MediaInfo
	{
		/// <summary>容器短名（如 "mp3" / "mov,mp4,m4a,3gp,3g2,mj2"）。</summary>
		public string FormatName { get; set; }

		public string FormatLongName { get; set; }

		public double DurationSeconds { get; set; }

		public long BitRate { get; set; }

		public long SizeBytes { get; set; }

		public List<MediaStreamInfo> Streams { get; } = new List<MediaStreamInfo>();

		public Dictionary<string, string> Tags { get; } = new Dictionary<string, string>();

		/// <summary>内嵌封面（没有为 null）。</summary>
		public ImageData Cover { get; set; }

		/// <summary>是否存在可解码的视频流。</summary>
		public bool HasVideo { get; set; }

		public bool HasAudio { get; set; }

		public MediaStreamInfo BestVideo { get; set; }

		public MediaStreamInfo BestAudio { get; set; }
	}

	/// <summary>波形包络：每个桶的 min / max / RMS（单声道下混）。</summary>
	public sealed class WaveformData
	{
		public float[] Min { get; set; }

		public float[] Max { get; set; }

		public float[] Rms { get; set; }

		public int Buckets => Min?.Length ?? 0;

		public double AnalyzedSeconds { get; set; }

		public double TotalSeconds { get; set; }

		public bool Truncated { get; set; }

		public int SampleRate { get; set; }

		public int Channels { get; set; }
	}

	/// <summary>声谱图：<see cref="Magnitude"/> 按列优先（column * Rows + row），值域 0–255。</summary>
	public sealed class SpectrogramData
	{
		public int Columns { get; set; }

		public int Rows { get; set; }

		public byte[] Magnitude { get; set; }

		public double AnalyzedSeconds { get; set; }

		public bool Truncated { get; set; }

		public int SampleRate { get; set; }
	}

	/// <summary>视频取帧结果。</summary>
	public sealed class VideoFrame
	{
		public ImageData Image { get; set; }

		/// <summary>该帧在时间轴上的秒数。</summary>
		public double Timestamp { get; set; }
	}

	/// <summary>探测失败时的错误分类（供插件侧给出本地化提示）。</summary>
	public enum MediaErrorKind
	{
		None,
		Unavailable,
		TooLarge,
		NoData,
		Unsupported,
		Failed
	}

	/// <summary>统一的失败结果。</summary>
	public sealed class MediaFailure
	{
		public MediaFailure(MediaErrorKind kind, string detail)
		{
			Kind = kind;
			Detail = detail;
		}

		public MediaErrorKind Kind { get; }

		public string Detail { get; }
	}
}
