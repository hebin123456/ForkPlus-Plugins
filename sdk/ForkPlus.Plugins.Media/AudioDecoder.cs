using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FFmpeg.AutoGen;

namespace ForkPlus.Plugins.Media
{
	/// <summary>
	/// 音频解码：把音频流经 swresample 统一成单声道 FLT，逐块回调给上层累加（波形 / 频谱共用）。
	/// 全流式，不缓存整段样本；由 <paramref name="maxSeconds"/> 截断。
	/// </summary>
	internal static unsafe class AudioDecoder
	{
		internal delegate void SampleHandler(float[] buffer, int count);

		internal sealed class Result
		{
			public int SampleRate;

			public int Channels;

			public double DurationSeconds;

			public bool Truncated;

			/// <summary>实际解出的秒数。</summary>
			public double AnalyzedSeconds;
		}

		/// <summary>
		/// 解码并回调。成功返回 true；失败返回 false 并给出 <paramref name="failure"/>。
		/// </summary>
		internal static bool Run(MemoryStream stream, int outputSampleRate, double maxSeconds, CancellationToken token, SampleHandler handler, out Result result, out MediaFailure failure)
		{
			result = new Result();
			failure = null;
			string error;
			if (!MediaNative.EnsureInitialized(out error))
			{
				failure = new MediaFailure(MediaErrorKind.Unavailable, error);
				return false;
			}
			AVCodecContext* codecContext = null;
			AVFrame* frame = null;
			AVPacket* packet = null;
			SwrContext* swr = null;
			MemoryAvioContext avio = null;
			float[] conversion = null;
			try
			{
				stream.Position = 0L;
				avio = new MemoryAvioContext(stream);
				AVFormatContext* format = avio.Open(out error);
				if (format == null)
				{
					failure = new MediaFailure(MediaProbe.Classify(error), error);
					return false;
				}
				int streamIndex = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
				if (streamIndex < 0)
				{
					failure = new MediaFailure(MediaErrorKind.Unsupported, "no audio stream");
					return false;
				}
				AVStream* audioStream = format->streams[streamIndex];
				AVCodecParameters* parameters = audioStream->codecpar;
				AVCodec* codec = ffmpeg.avcodec_find_decoder(parameters->codec_id);
				if (codec == null)
				{
					failure = new MediaFailure(MediaErrorKind.Unsupported, "no decoder for " + ffmpeg.avcodec_get_name(parameters->codec_id));
					return false;
				}
				codecContext = ffmpeg.avcodec_alloc_context3(codec);
				if (codecContext == null || ffmpeg.avcodec_parameters_to_context(codecContext, parameters) < 0 || ffmpeg.avcodec_open2(codecContext, codec, null) < 0)
				{
					failure = new MediaFailure(MediaErrorKind.Failed, "avcodec_open2 failed");
					return false;
				}

				int sourceRate = codecContext->sample_rate > 0 ? codecContext->sample_rate : 44100;
				int targetRate = outputSampleRate > 0 ? outputSampleRate : sourceRate;
				result.SampleRate = targetRate;
				result.Channels = parameters->ch_layout.nb_channels;
				double total = 0.0;
				if (audioStream->duration != ffmpeg.AV_NOPTS_VALUE && audioStream->duration > 0)
				{
					total = audioStream->duration * ffmpeg.av_q2d(audioStream->time_base);
				}
				else if (format->duration != ffmpeg.AV_NOPTS_VALUE && format->duration > 0)
				{
					total = format->duration / (double)ffmpeg.AV_TIME_BASE;
				}
				result.DurationSeconds = total;
				double limit = maxSeconds > 0.0 ? maxSeconds : MediaLimits.MaxAudioSeconds;
				bool hasLimit = total > 0.0;
				double targetSeconds = hasLimit ? Math.Min(total, limit) : limit;
				long targetSamples = (long)(targetSeconds * targetRate);
				result.Truncated = hasLimit && total > limit + 0.05;

				AVChannelLayout outputLayout;
				ffmpeg.av_channel_layout_default(&outputLayout, 1);
				if (ffmpeg.swr_alloc_set_opts2(&swr, &outputLayout, AVSampleFormat.AV_SAMPLE_FMT_FLT, targetRate, &codecContext->ch_layout, codecContext->sample_fmt, sourceRate, 0, null) < 0 || swr == null || ffmpeg.swr_init(swr) < 0)
				{
					failure = new MediaFailure(MediaErrorKind.Failed, "swr_init failed");
					return false;
				}

				frame = ffmpeg.av_frame_alloc();
				packet = ffmpeg.av_packet_alloc();
				if (frame == null || packet == null)
				{
					failure = new MediaFailure(MediaErrorKind.Failed, "av_frame_alloc / av_packet_alloc failed");
					return false;
				}

				long produced = 0L;
				bool cancelled = false;
				while (!token.IsCancellationRequested)
				{
					int read = ffmpeg.av_read_frame(format, packet);
					if (read < 0)
					{
						break;
					}
					if (packet->stream_index == streamIndex)
					{
						if (ffmpeg.avcodec_send_packet(codecContext, packet) >= 0)
						{
							while (ffmpeg.avcodec_receive_frame(codecContext, frame) >= 0)
							{
								produced += Drain(swr, frame, handler, ref conversion, produced, targetSamples, out bool stop);
								ffmpeg.av_frame_unref(frame);
								if (stop)
								{
									break;
								}
							}
						}
					}
					ffmpeg.av_packet_unref(packet);
					if (produced >= targetSamples)
					{
						break;
					}
				}
				// 冲掉解码器 / 重采样器里的残留
				if (produced < targetSamples && !token.IsCancellationRequested)
				{
					ffmpeg.avcodec_send_packet(codecContext, null);
					while (ffmpeg.avcodec_receive_frame(codecContext, frame) >= 0)
					{
						produced += Drain(swr, frame, handler, ref conversion, produced, targetSamples, out bool stop);
						ffmpeg.av_frame_unref(frame);
						if (stop)
						{
							break;
						}
					}
					produced += Drain(swr, null, handler, ref conversion, produced, targetSamples, out bool _);
				}
				cancelled = token.IsCancellationRequested;
				result.AnalyzedSeconds = produced / (double)targetRate;
				if (cancelled)
				{
					result.Truncated = true;
				}
				return true;
			}
			catch (Exception ex)
			{
				failure = new MediaFailure(MediaErrorKind.Failed, ex.GetType().Name + ": " + ex.Message);
				return false;
			}
			finally
			{
				if (swr != null)
				{
					SwrContext* s = swr;
					ffmpeg.swr_free(&s);
				}
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
				if (codecContext != null)
				{
					AVCodecContext* c = codecContext;
					ffmpeg.avcodec_free_context(&c);
				}
				avio?.Dispose();
			}
		}

		/// <summary>把一帧（或冲尾时的 null）经 swr 转成单声道 float 并回调，返回本次产出的样本数。</summary>
		private static long Drain(SwrContext* swr, AVFrame* frame, SampleHandler handler, ref float[] buffer, long produced, long targetSamples, out bool stop)
		{
			stop = false;
			int inputSamples = frame != null ? frame->nb_samples : 0;
			int capacity = inputSamples + 4096;
			if (capacity < 4096)
			{
				capacity = 4096;
			}
			if (buffer == null || buffer.Length < capacity)
			{
				buffer = new float[capacity];
			}
			byte*[] pointers = new byte*[1];
			int got;
			fixed (float* samples = buffer)
			fixed (byte** destination = pointers)
			{
				pointers[0] = (byte*)samples;
				got = ffmpeg.swr_convert(swr, destination, buffer.Length, frame == null ? null : frame->extended_data, inputSamples);
			}
			if (got <= 0)
			{
				return 0L;
			}
			long remaining = targetSamples - produced;
			int count = (int)Math.Min(got, Math.Max(remaining, 0L));
			if (count > 0)
			{
				handler(buffer, count);
			}
			if (produced + count >= targetSamples)
			{
				stop = true;
			}
			return count;
		}
	}
}
