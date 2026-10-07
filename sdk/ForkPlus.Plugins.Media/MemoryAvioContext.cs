using System;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace ForkPlus.Plugins.Media
{
	/// <summary>
	/// 把 git blob 字节（<see cref="MemoryStream"/>）包成 FFmpeg 的 AVIOContext。
	///
	/// 两侧内容都是仓库里的旧 / 新版本字节，不是磁盘文件，所以**不能** avformat_open_input(路径)，
	/// 必须 avio_alloc_context + read / seek 回调（见 design/audio-video-plugins.md §6）。
	///
	/// 要点：
	/// - 回调委托挂在本对象的实例字段上，本对象由调用方持有直到收尾 → 不会被 GC 回收踩空指针。
	/// - 读缓冲用 av_malloc（FFmpeg 约定；收尾用 av_freep(&amp;avio->buffer) + avio_context_free）。
	/// - seek 支持 SET / CUR / END 与 AVSEEK_SIZE；MemoryStream 可 seek，因此拖进度条是真 seek。
	///
	/// 用法：构造 → <see cref="Open"/> 拿到 AVFormatContext* → 用完 <see cref="Dispose"/>。
	/// </summary>
	internal sealed unsafe class MemoryAvioContext : IDisposable
	{
		private const int ReadChunkSize = 32768;

		private readonly MemoryStream _stream;

		// 必须作为字段持有：这两个委托由原生层调用，若只作为局部变量会被 GC 回收。
		private readonly avio_alloc_context_read_packet _readPacket;

		private readonly avio_alloc_context_seek _seek;
		private byte[] _scratch;

		private AVIOContext* _avio;

		private AVFormatContext* _format;

		private bool _disposed;

		internal MemoryAvioContext(MemoryStream stream)
		{
			_stream = stream ?? throw new ArgumentNullException(nameof(stream));
			_readPacket = ReadPacket;
			_seek = Seek;
		}

		/// <summary>分配 AVIOContext 并打开输入；失败返回 null 并给出原因。</summary>
		internal AVFormatContext* Open(out string error)
		{
			error = null;
			byte* buffer = null;
			try
			{
				buffer = (byte*)ffmpeg.av_malloc(ReadChunkSize);
				if (buffer == null)
				{
					error = "av_malloc failed";
					return null;
				}
				_avio = ffmpeg.avio_alloc_context(buffer, ReadChunkSize, 0, null, _readPacket, null, _seek);
				if (_avio == null)
				{
					ffmpeg.av_free(buffer);
					error = "avio_alloc_context failed";
					return null;
				}
				_avio->seekable = ffmpeg.AVIO_SEEKABLE_NORMAL;

				_format = ffmpeg.avformat_alloc_context();
				if (_format == null)
				{
					error = "avformat_alloc_context failed";
					Dispose();
					return null;
				}
				_format->pb = _avio;
				_format->flags |= ffmpeg.AVFMT_FLAG_CUSTOM_IO;

				AVFormatContext* format = _format;
				int result = ffmpeg.avformat_open_input(&format, null, null, null);
				_format = format;
				if (result < 0)
				{
					error = "avformat_open_input: " + FfError.Describe(result);
					Dispose();
					return null;
				}
				result = ffmpeg.avformat_find_stream_info(_format, null);
				if (result < 0)
				{
					error = "avformat_find_stream_info: " + FfError.Describe(result);
					Dispose();
					return null;
				}
				return _format;
			}
			catch (Exception ex)
			{
				error = ex.GetType().Name + ": " + ex.Message;
				Dispose();
				return null;
			}
		}

		private int ReadPacket(void* opaque, byte* buf, int bufSize)
		{
			if (bufSize <= 0 || _stream == null)
			{
				return ffmpeg.AVERROR_EOF;
			}
			try
			{
				if (_scratch == null || _scratch.Length < bufSize)
				{
					_scratch = new byte[bufSize];
				}
				int read = _stream.Read(_scratch, 0, bufSize);
				if (read <= 0)
				{
					return ffmpeg.AVERROR_EOF;
				}
				Marshal.Copy(_scratch, 0, (IntPtr)buf, read);
				return read;
			}
			catch
			{
				return ffmpeg.AVERROR_EOF;
			}
		}

		private long Seek(void* opaque, long offset, int whence)
		{
			if (_stream == null)
			{
				return -1L;
			}
			try
			{
				if ((whence & ffmpeg.AVSEEK_SIZE) != 0)
				{
					return _stream.Length;
				}
				switch (whence)
				{
					case 0:
						return _stream.Seek(offset, SeekOrigin.Begin);
					case 1:
						return _stream.Seek(offset, SeekOrigin.Current);
					case 2:
						return _stream.Seek(offset, SeekOrigin.End);
					default:
						return -1L;
				}
			}
			catch
			{
				return -1L;
			}
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}
			_disposed = true;
			if (_format != null)
			{
				AVFormatContext* format = _format;
				ffmpeg.avformat_close_input(&format);
				_format = null;
			}
			if (_avio != null)
			{
				// 缓冲由我们 av_malloc 分配，avio_context_free 不负责释放（FFmpeg 官方示例的收尾顺序）。
				ffmpeg.av_freep((void**)(&_avio->buffer));
				AVIOContext* avio = _avio;
				ffmpeg.avio_context_free(&avio);
				_avio = null;
			}
			_scratch = null;
		}
	}
}
