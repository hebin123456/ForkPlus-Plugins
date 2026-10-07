using System;
using System.Collections.Generic;
using System.IO;
using FFmpeg.AutoGen;

namespace ForkPlus.Plugins.Media
{
	/// <summary>
	/// 媒体探测：打开输入 → 读流信息 → 汇总容器 / 各流 / 标签，并解出内嵌封面。
	/// 不做全量解码，因此很快，元数据模式只依赖它。
	/// </summary>
	public static unsafe class MediaProbe
	{
		/// <summary>
		/// 探测一侧内容。失败返回 null 并给出 <see cref="MediaFailure"/>。
		/// </summary>
		public static MediaInfo Probe(MemoryStream stream, long declaredSize, out MediaFailure failure)
		{
			failure = null;
			if (stream == null)
			{
				failure = new MediaFailure(MediaErrorKind.NoData, "no data");
				return null;
			}
			string error;
			if (!MediaNative.EnsureInitialized(out error))
			{
				failure = new MediaFailure(MediaErrorKind.Unavailable, error);
				return null;
			}
			MediaInfo info = new MediaInfo();
			MemoryAvioContext avio = null;
			try
			{
				stream.Position = 0L;
				avio = new MemoryAvioContext(stream);
				AVFormatContext* format = avio.Open(out error);
				if (format == null)
				{
					failure = new MediaFailure(Classify(error), error);
					return null;
				}

				info.FormatName = FfError.PtrToString(format->iformat != null ? format->iformat->name : null);
				info.FormatLongName = FfError.PtrToString(format->iformat != null ? format->iformat->long_name : null);
				info.BitRate = format->bit_rate;
				info.SizeBytes = stream.Length > 0 ? stream.Length : declaredSize;
				if (format->duration != ffmpeg.AV_NOPTS_VALUE && format->duration > 0)
				{
					info.DurationSeconds = format->duration / (double)ffmpeg.AV_TIME_BASE;
				}
				ReadTags(format->metadata, info.Tags);

				uint count = format->nb_streams;
				for (uint i = 0; i < count; i++)
				{
					AVStream* streamPtr = format->streams[i];
					if (streamPtr == null)
					{
						continue;
					}
					MediaStreamInfo streamInfo = ReadStream(streamPtr);
					if (streamInfo == null)
					{
						continue;
					}
					info.Streams.Add(streamInfo);
					if (streamInfo.IsAttachedPicture)
					{
						if (info.Cover == null)
						{
							info.Cover = DecodeAttachedPicture(streamPtr);
						}
						continue;
					}
					if (string.Equals(streamInfo.Kind, "video", StringComparison.Ordinal))
					{
						info.HasVideo = true;
						if (info.BestVideo == null)
						{
							info.BestVideo = streamInfo;
						}
					}
					else if (string.Equals(streamInfo.Kind, "audio", StringComparison.Ordinal))
					{
						info.HasAudio = true;
						if (info.BestAudio == null)
						{
							info.BestAudio = streamInfo;
						}
					}
				}
				if (info.BestVideo == null)
				{
					int index = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
					if (index >= 0)
					{
						foreach (MediaStreamInfo s in info.Streams)
						{
							if (s.Index == index)
							{
								info.BestVideo = s;
								info.HasVideo = true;
								break;
							}
						}
					}
				}
				if (info.BestAudio == null)
				{
					int index = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
					if (index >= 0)
					{
						foreach (MediaStreamInfo s in info.Streams)
						{
							if (s.Index == index)
							{
								info.BestAudio = s;
								info.HasAudio = true;
								break;
							}
						}
					}
				}
				return info;
			}
			catch (Exception ex)
			{
				failure = new MediaFailure(MediaErrorKind.Failed, ex.GetType().Name + ": " + ex.Message);
				return null;
			}
			finally
			{
				avio?.Dispose();
			}
		}

		private static MediaStreamInfo ReadStream(AVStream* stream)
		{
			AVCodecParameters* parameters = stream->codecpar;
			if (parameters == null)
			{
				return null;
			}
			MediaStreamInfo info = new MediaStreamInfo
			{
				Index = stream->index,
				Kind = KindName(parameters->codec_type),
				CodecName = ffmpeg.avcodec_get_name(parameters->codec_id),
				CodecLongName = DescribeCodec(parameters->codec_id),
				BitRate = parameters->bit_rate,
				IsAttachedPicture = (stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) != 0
			};
			if (stream->duration != ffmpeg.AV_NOPTS_VALUE && stream->duration > 0)
			{
				info.DurationSeconds = stream->duration * ffmpeg.av_q2d(stream->time_base);
			}
			if (stream->avg_frame_rate.den > 0 && stream->avg_frame_rate.num > 0)
			{
				info.FrameRate = ffmpeg.av_q2d(stream->avg_frame_rate);
			}
			if (string.Equals(info.Kind, "audio", StringComparison.Ordinal))
			{
				info.SampleRate = parameters->sample_rate;
				info.Channels = parameters->ch_layout.nb_channels;
				info.ChannelLayout = DescribeChannelLayout(&parameters->ch_layout);
			}
			else if (string.Equals(info.Kind, "video", StringComparison.Ordinal))
			{
				info.Width = parameters->width;
				info.Height = parameters->height;
				info.PixelFormat = ffmpeg.av_get_pix_fmt_name((AVPixelFormat)parameters->format);
				info.ColorSpace = DescribeColorSpace(parameters->color_space);
			}
			ReadTags(stream->metadata, info.Tags);
			return info;
		}

		private static unsafe ImageData DecodeAttachedPicture(AVStream* stream)
		{
			AVCodec* codec = null;
			AVCodecContext* context = null;
			AVFrame* frame = null;
			AVPacket* packet = null;
			try
			{
				codec = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id);
				if (codec == null)
				{
					return null;
				}
				context = ffmpeg.avcodec_alloc_context3(codec);
				if (context == null)
				{
					return null;
				}
				if (ffmpeg.avcodec_parameters_to_context(context, stream->codecpar) < 0)
				{
					return null;
				}
				if (ffmpeg.avcodec_open2(context, codec, null) < 0)
				{
					return null;
				}
				packet = ffmpeg.av_packet_alloc();
				frame = ffmpeg.av_frame_alloc();
				if (packet == null || frame == null)
				{
					return null;
				}
				if (ffmpeg.av_packet_ref(packet, &stream->attached_pic) < 0)
				{
					return null;
				}
				if (ffmpeg.avcodec_send_packet(context, packet) < 0)
				{
					return null;
				}
				if (ffmpeg.avcodec_receive_frame(context, frame) < 0)
				{
					return null;
				}
				return MediaConvert.FrameToImage(frame, 512, 512);
			}
			catch
			{
				return null;
			}
			finally
			{
				if (packet != null)
				{
					AVPacket* p = packet;
					ffmpeg.av_packet_free(&p);
				}
				if (frame != null)
				{
					AVFrame* f = frame;
					ffmpeg.av_frame_free(&f);
				}
				if (context != null)
				{
					AVCodecContext* c = context;
					ffmpeg.avcodec_free_context(&c);
				}
			}
		}

		internal static void ReadTags(AVDictionary* metadata, Dictionary<string, string> target)
		{
			if (metadata == null)
			{
				return;
			}
			AVDictionaryEntry* entry = null;
			int guard = 0;
			while (guard++ < 512)
			{
				entry = ffmpeg.av_dict_get(metadata, "", entry, ffmpeg.AV_DICT_IGNORE_SUFFIX);
				if (entry == null)
				{
					break;
				}
				string key = FfError.PtrToString(entry->key);
				string value = FfError.PtrToString(entry->value);
				if (!string.IsNullOrEmpty(key) && !target.ContainsKey(key))
				{
					target[key] = value ?? string.Empty;
				}
			}
		}

		internal static string KindName(AVMediaType type)
		{
			switch (type)
			{
				case AVMediaType.AVMEDIA_TYPE_VIDEO:
					return "video";
				case AVMediaType.AVMEDIA_TYPE_AUDIO:
					return "audio";
				case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
					return "subtitle";
				case AVMediaType.AVMEDIA_TYPE_DATA:
					return "data";
				case AVMediaType.AVMEDIA_TYPE_ATTACHMENT:
					return "attachment";
				default:
					return "unknown";
			}
		}

		private static string DescribeCodec(AVCodecID id)
		{
			AVCodecDescriptor* descriptor = ffmpeg.avcodec_descriptor_get(id);
			return descriptor == null ? null : FfError.PtrToString(descriptor->long_name);
		}

		internal static unsafe string DescribeChannelLayout(AVChannelLayout* layout)
		{
			try
			{
				byte* buffer = stackalloc byte[128];
				if (ffmpeg.av_channel_layout_describe(layout, buffer, 128) < 0)
				{
					return null;
				}
				return FfError.PtrToString(buffer, 128);
			}
			catch
			{
				return null;
			}
		}

		internal static string DescribeColorSpace(AVColorSpace space)
		{
			try
			{
				return ffmpeg.av_color_space_name(space);
			}
			catch
			{
				return null;
			}
		}

		internal static MediaErrorKind Classify(string message)
		{
			if (string.IsNullOrEmpty(message))
			{
				return MediaErrorKind.Failed;
			}
			if (message.IndexOf("Invalid data", StringComparison.OrdinalIgnoreCase) >= 0
				|| message.IndexOf("moov atom not found", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return MediaErrorKind.Unsupported;
			}
			return MediaErrorKind.Failed;
		}
	}
}
