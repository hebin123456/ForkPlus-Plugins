using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FFmpeg.AutoGen;

namespace ForkPlus.Plugins.Media
{
	/// <summary>
	/// 视频取帧：按时间轴定位并解出关键帧附近的一帧，转成 BGRA <see cref="ImageData"/>。
	///
	/// 定位策略：<c>av_seek_frame</c>（BACKWARD）跳到目标时间之前最近的关键帧，<c>avcodec_flush_buffers</c>
	/// 清解码器后从该关键帧顺序解到 pts ≥ 目标 —— 每帧一次 seek，长视频也不必从头解到尾。
	/// 帧条与单帧对比共用这一条路径。
	///
	/// 与音频分析同样：后台线程执行、受 <see cref="MediaLimits"/> 的帧数上限约束、出错返回 null + 失败原因。
	/// </summary>
	public static unsafe class MediaVideo
	{
		/// <summary>取目标时间点最近的一帧（缩放到不超过给定尺寸）。</summary>
		public static VideoFrame ExtractFrame(MemoryStream stream, double seconds, int maxWidth, int maxHeight, CancellationToken token, out MediaFailure failure)
		{
			failure = null;
			if (stream == null)
			{
				failure = new MediaFailure(MediaErrorKind.NoData, "no data");
				return null;
			}
			VideoSession session = null;
			try
			{
				session = VideoSession.Open(stream, out failure);
				if (session == null)
				{
					return null;
				}
				VideoFrame frame = session.DecodeNear(seconds, maxWidth, maxHeight, token);
				if (frame == null)
				{
					failure = new MediaFailure(MediaErrorKind.Failed, "no frame decoded");
				}
				return frame;
			}
			catch (Exception ex)
			{
				failure = new MediaFailure(MediaErrorKind.Failed, ex.GetType().Name + ": " + ex.Message);
				return null;
			}
			finally
			{
				session?.Dispose();
			}
		}

		/// <summary>
		/// 帧条：在时间轴上均匀取 <paramref name="count"/> 帧（每段中点），最多
		/// <see cref="MediaLimits.MaxFilmstripFrames"/> 帧。
		/// </summary>
		public static List<VideoFrame> ExtractFilmstrip(MemoryStream stream, int count, CancellationToken token, out MediaFailure failure)
		{
			failure = null;
			if (stream == null)
			{
				failure = new MediaFailure(MediaErrorKind.NoData, "no data");
				return null;
			}
			if (count < 1)
			{
				count = 1;
			}
			if (count > MediaLimits.MaxFilmstripFrames)
			{
				count = MediaLimits.MaxFilmstripFrames;
			}
			VideoSession session = null;
			try
			{
				session = VideoSession.Open(stream, out failure);
				if (session == null)
				{
					return null;
				}
				double duration = session.DurationSeconds > 0.0 ? session.DurationSeconds : 1.0;
				List<VideoFrame> frames = new List<VideoFrame>(count);
				for (int i = 0; i < count; i++)
				{
					if (token.IsCancellationRequested)
					{
						break;
					}
					double seconds = duration * (i + 0.5) / count;
					VideoFrame frame = session.DecodeNear(seconds, MediaLimits.FilmstripWidth, 0, token);
					if (frame != null)
					{
						frames.Add(frame);
					}
				}
				if (frames.Count == 0)
				{
					failure = new MediaFailure(MediaErrorKind.Failed, "no frames decoded");
					return null;
				}
				return frames;
			}
			catch (Exception ex)
			{
				failure = new MediaFailure(MediaErrorKind.Failed, ex.GetType().Name + ": " + ex.Message);
				return null;
			}
			finally
			{
				session?.Dispose();
			}
		}

		/// <summary>一次打开会话：格式上下文 + 视频流 + 解码器，复用给多个时间点取帧。</summary>
		private sealed unsafe class VideoSession : IDisposable
		{
			private const int MaxPacketsPerSeek = 900;

			private MemoryAvioContext _avio;

			private AVFormatContext* _format;

			private AVStream* _stream;

			private AVCodecContext* _codec;

			private AVPacket* _packet;

			private AVFrame* _frame;

			private AVFrame* _hold;

			private bool _disposed;

			public double DurationSeconds;

			public static VideoSession Open(MemoryStream stream, out MediaFailure failure)
			{
				failure = null;
				string error;
				if (!MediaNative.EnsureInitialized(out error))
				{
					failure = new MediaFailure(MediaErrorKind.Unavailable, error);
					return null;
				}
				VideoSession session = new VideoSession();
				try
				{
					stream.Position = 0L;
					session._avio = new MemoryAvioContext(stream);
					session._format = session._avio.Open(out error);
					if (session._format == null)
					{
						failure = new MediaFailure(MediaProbe.Classify(error), error);
						session.Dispose();
						return null;
					}
					int index = ffmpeg.av_find_best_stream(session._format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
					if (index < 0)
					{
						failure = new MediaFailure(MediaErrorKind.Unsupported, "no video stream");
						session.Dispose();
						return null;
					}
					session._stream = session._format->streams[index];
					AVCodecParameters* parameters = session._stream->codecpar;
					AVCodec* codec = ffmpeg.avcodec_find_decoder(parameters->codec_id);
					if (codec == null)
					{
						failure = new MediaFailure(MediaErrorKind.Unsupported, "no decoder for " + ffmpeg.avcodec_get_name(parameters->codec_id));
						session.Dispose();
						return null;
					}
					session._codec = ffmpeg.avcodec_alloc_context3(codec);
					if (session._codec == null
						|| ffmpeg.avcodec_parameters_to_context(session._codec, parameters) < 0
						|| ffmpeg.avcodec_open2(session._codec, codec, null) < 0)
					{
						failure = new MediaFailure(MediaErrorKind.Failed, "avcodec_open2 failed");
						session.Dispose();
						return null;
					}
					session._packet = ffmpeg.av_packet_alloc();
					session._frame = ffmpeg.av_frame_alloc();
					session._hold = ffmpeg.av_frame_alloc();
					if (session._packet == null || session._frame == null || session._hold == null)
					{
						failure = new MediaFailure(MediaErrorKind.Failed, "av_packet_alloc / av_frame_alloc failed");
						session.Dispose();
						return null;
					}
					if (session._stream->duration != ffmpeg.AV_NOPTS_VALUE && session._stream->duration > 0)
					{
						session.DurationSeconds = session._stream->duration * ffmpeg.av_q2d(session._stream->time_base);
					}
					else if (session._format->duration != ffmpeg.AV_NOPTS_VALUE && session._format->duration > 0)
					{
						session.DurationSeconds = session._format->duration / (double)ffmpeg.AV_TIME_BASE;
					}
					return session;
				}
				catch (Exception ex)
				{
					failure = new MediaFailure(MediaErrorKind.Failed, ex.GetType().Name + ": " + ex.Message);
					session.Dispose();
					return null;
				}
			}

			/// <summary>定位到 <paramref name="seconds"/> 并解出最近的一帧（缩放后转 BGRA）。</summary>
			public VideoFrame DecodeNear(double seconds, int maxWidth, int maxHeight, CancellationToken token)
			{
				if (seconds < 0.0)
				{
					seconds = 0.0;
				}
				double timeBase = ffmpeg.av_q2d(_stream->time_base);
				if (timeBase <= 0.0)
				{
					timeBase = 1.0 / 1000.0;
				}
				long timestamp = (long)Math.Round(seconds / timeBase);
				// BACKWARD：跳到目标之前最近的关键帧。失败不致命，从当前位置继续解。
				ffmpeg.av_seek_frame(_format, _stream->index, timestamp, ffmpeg.AVSEEK_FLAG_BACKWARD);
				ffmpeg.avcodec_flush_buffers(_codec);
				ffmpeg.av_frame_unref(_hold);
				bool haveHold = false;
				VideoFrame result = null;
				int guard = 0;
				while (guard++ < MaxPacketsPerSeek && !token.IsCancellationRequested)
				{
					int read = ffmpeg.av_read_frame(_format, _packet);
					if (read < 0)
					{
						break;
					}
					if (_packet->stream_index == _stream->index)
					{
						if (ffmpeg.avcodec_send_packet(_codec, _packet) >= 0)
						{
							while (ffmpeg.avcodec_receive_frame(_codec, _frame) >= 0)
							{
								long pts = _frame->pts;
								if (pts == ffmpeg.AV_NOPTS_VALUE)
								{
									pts = _frame->pkt_dts;
								}
								double frameSeconds = pts == ffmpeg.AV_NOPTS_VALUE ? seconds : pts * timeBase;
								if (frameSeconds + 1e-6 >= seconds)
								{
									result = ConvertFrame(_frame, maxWidth, maxHeight, frameSeconds < 0.0 ? 0.0 : frameSeconds);
								}
								// 保留当前帧作为「目标时间之后没有更多帧」时的兜底。
								ffmpeg.av_frame_unref(_hold);
								ffmpeg.av_frame_ref(_hold, _frame);
								haveHold = true;
								ffmpeg.av_frame_unref(_frame);
								if (result != null)
								{
									ffmpeg.av_packet_unref(_packet);
									return result;
								}
							}
						}
					}
					ffmpeg.av_packet_unref(_packet);
				}
				if (result != null)
				{
					return result;
				}
				if (haveHold)
				{
					return ConvertFrame(_hold, maxWidth, maxHeight, seconds);
				}
				return null;
			}

			private static VideoFrame ConvertFrame(AVFrame* frame, int maxWidth, int maxHeight, double seconds)
			{
				ImageData image = MediaConvert.FrameToImage(frame, maxWidth, maxHeight);
				if (image == null)
				{
					return null;
				}
				return new VideoFrame
				{
					Image = image,
					Timestamp = seconds
				};
			}

			public void Dispose()
			{
				if (_disposed)
				{
					return;
				}
				_disposed = true;
				if (_hold != null)
				{
					AVFrame* f = _hold;
					ffmpeg.av_frame_free(&f);
					_hold = null;
				}
				if (_frame != null)
				{
					AVFrame* f = _frame;
					ffmpeg.av_frame_free(&f);
					_frame = null;
				}
				if (_packet != null)
				{
					AVPacket* p = _packet;
					ffmpeg.av_packet_free(&p);
					_packet = null;
				}
				if (_codec != null)
				{
					AVCodecContext* c = _codec;
					ffmpeg.avcodec_free_context(&c);
					_codec = null;
				}
				_stream = null;
				_avio?.Dispose();
				_avio = null;
				_format = null;
			}
		}
	}
}
