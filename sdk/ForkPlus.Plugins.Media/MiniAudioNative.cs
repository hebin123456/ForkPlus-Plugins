using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ForkPlus.Plugins.Media
{
	/// <summary>
	/// 音频输出后端的 .NET 绑定：P/Invoke 到三方件仓自建的 <c>fpp_audio</c> 共享库
	/// （miniaudio + 一层不透明句柄的 C ABI 垫片，见三方件仓 miniaudio/manifest.json）。
	///
	/// 与 <see cref="MediaNative"/> 同样按「本程序集所在目录」找原生件：音视频插件的原生库都随包
	/// 平铺在宿主的 plugins/ 目录，与本程序集同目录。
	///
	/// 用 <see cref="NativeLibrary"/> 显式加载而非 DllImport：库名四平台不同
	/// （Linux <c>libfpp_audio.so.0</c> / Windows <c>fpp_audio.dll</c> / macOS <c>libfpp_audio.0.dylib</c>），
	/// 且要落在插件自己的目录里解析，DllImport 的探测顺序不可控。
	///
	/// 只做一次初始化；失败后记住原因不再重试。**音频输出不可用不是致命错误**：调用方据此
	/// 降级为「静音播放 + 墙钟」，而不是整段音频无法播放。
	/// </summary>
	internal static class MiniAudioNative
	{
		private const string ComponentDirectory = "miniaudio";

		private static readonly object SyncRoot = new object();

		private static bool _initialized;

		private static string _error;

		private static IntPtr _library;

		private static OpenDelegate _open;

		private static CloseDelegate _close;

		private static StartDelegate _start;

		private static StopDelegate _stop;

		private static PlayingDelegate _playing;

		private static WriteDelegate _write;

		private static FreeFramesDelegate _freeFrames;

		private static PlayedFramesDelegate _playedFrames;

		private static ResetDelegate _reset;

		private static SetVolumeDelegate _setVolume;

		private static LastErrorDelegate _lastError;

		private delegate IntPtr OpenDelegate(uint sampleRate, uint channels);

		private delegate void CloseDelegate(IntPtr handle);

		private delegate int StartDelegate(IntPtr handle);

		private delegate int StopDelegate(IntPtr handle);

		private delegate int PlayingDelegate(IntPtr handle);

		private delegate uint WriteDelegate(IntPtr handle, float[] frames, uint frameCount);

		private delegate uint FreeFramesDelegate(IntPtr handle);

		private delegate ulong PlayedFramesDelegate(IntPtr handle);

		private delegate void ResetDelegate(IntPtr handle);

		private delegate void SetVolumeDelegate(IntPtr handle, float volume);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate IntPtr LastErrorDelegate();

		/// <summary>音频输出后端是否可用（库加载成功且符号齐全）。</summary>
		public static bool IsAvailable
		{
			get
			{
				lock (SyncRoot)
				{
					return _initialized;
				}
			}
		}

		/// <summary>不可用时的原因（可用为 null）。</summary>
		public static string Error
		{
			get
			{
				lock (SyncRoot)
				{
					return _error;
				}
			}
		}

		/// <summary>确保绑定已初始化。多次调用只初始化一次；失败返回 false 并给出可读原因。</summary>
		public static bool EnsureInitialized(out string error)
		{
			lock (SyncRoot)
			{
				if (_initialized)
				{
					error = null;
					return true;
				}
				if (_error != null)
				{
					error = _error;
					return false;
				}
				try
				{
					string directory = MediaNative.ResolveDirectory();
					if (directory == null)
					{
						throw new DirectoryNotFoundException("native directory not found");
					}
					string path = Path.Combine(directory, LibraryFileName());
					if (!File.Exists(path))
					{
						throw new FileNotFoundException("音频输出后端缺失：" + path);
					}
					_library = NativeLibrary.Load(path);
					_open = Load<OpenDelegate>("fpp_audio_open");
					_close = Load<CloseDelegate>("fpp_audio_close");
					_start = Load<StartDelegate>("fpp_audio_start");
					_stop = Load<StopDelegate>("fpp_audio_stop");
					_playing = Load<PlayingDelegate>("fpp_audio_playing");
					_write = Load<WriteDelegate>("fpp_audio_write");
					_freeFrames = Load<FreeFramesDelegate>("fpp_audio_free_frames");
					_playedFrames = Load<PlayedFramesDelegate>("fpp_audio_played_frames");
					_reset = Load<ResetDelegate>("fpp_audio_reset");
					_setVolume = Load<SetVolumeDelegate>("fpp_audio_set_volume");
					_lastError = Load<LastErrorDelegate>("fpp_audio_last_error");
					_initialized = true;
					error = null;
					return true;
				}
				catch (Exception ex)
				{
					_error = ex is DllNotFoundException || ex is EntryPointNotFoundException
						? ex.Message
						: ex.GetType().Name + ": " + ex.Message;
					error = _error;
					return false;
				}
			}
		}

		/// <summary>RID 对应的库文件名，与三方件仓 miniaudio/manifest.json 的 assets.layout 逐字对应。</summary>
		private static string LibraryFileName()
		{
			if (OperatingSystem.IsWindows())
			{
				return "fpp_audio.dll";
			}
			if (OperatingSystem.IsMacOS())
			{
				return "libfpp_audio.0.dylib";
			}
			return "libfpp_audio.so.0";
		}

		private static T Load<T>(string symbol) where T : Delegate
		{
			IntPtr address = NativeLibrary.GetExport(_library, symbol);
			return (T)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
		}

		/// <summary>打开输出设备；失败返回 <see cref="IntPtr.Zero"/>，原因见 <see cref="LastError"/>。</summary>
		public static IntPtr Open(uint sampleRate, uint channels)
		{
			EnsureInitialized(out _);
			return _initialized ? _open(sampleRate, channels) : IntPtr.Zero;
		}

		public static void Close(IntPtr handle)
		{
			if (_initialized && handle != IntPtr.Zero)
			{
				_close(handle);
			}
		}

		public static int Start(IntPtr handle)
		{
			return _initialized && handle != IntPtr.Zero ? _start(handle) : -1;
		}

		public static int Stop(IntPtr handle)
		{
			return _initialized && handle != IntPtr.Zero ? _stop(handle) : -1;
		}

		public static bool IsPlaying(IntPtr handle)
		{
			return _initialized && handle != IntPtr.Zero && _playing(handle) == 1;
		}

		/// <summary>写入交错 float32（<paramref name="frameCount"/> 为每声道帧数），返回实际写入帧数。</summary>
		public static uint Write(IntPtr handle, float[] frames, uint frameCount)
		{
			return _initialized && handle != IntPtr.Zero ? _write(handle, frames, frameCount) : 0u;
		}

		/// <summary>环形缓冲剩余可写帧数（生产者背压）。</summary>
		public static uint FreeFrames(IntPtr handle)
		{
			return _initialized && handle != IntPtr.Zero ? _freeFrames(handle) : 0u;
		}

		/// <summary>已播出的真实音频帧数（音频主时钟）。</summary>
		public static ulong PlayedFrames(IntPtr handle)
		{
			return _initialized && handle != IntPtr.Zero ? _playedFrames(handle) : 0ul;
		}

		public static void Reset(IntPtr handle)
		{
			if (_initialized && handle != IntPtr.Zero)
			{
				_reset(handle);
			}
		}

		public static void SetVolume(IntPtr handle, float volume)
		{
			if (_initialized && handle != IntPtr.Zero)
			{
				_setVolume(handle, volume);
			}
		}

		/// <summary>最近一次失败的静态字符串（不可用时为绑定自身的错误）。</summary>
		public static string LastError()
		{
			if (!_initialized)
			{
				return _error;
			}
			IntPtr pointer = _lastError();
			return pointer == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(pointer);
		}
	}
}
