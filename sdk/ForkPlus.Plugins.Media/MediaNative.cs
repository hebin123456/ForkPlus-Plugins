using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded.Native;
using DynBindings = FFmpeg.AutoGen.Bindings.DynamicallyLoaded.DynamicallyLoadedBindings;

namespace ForkPlus.Plugins.Media
{
	/// <summary>
	/// 原生件（libavformat / libavcodec / libavutil / libswscale / libswresample）随插件包平铺
	/// 分发在宿主的 plugins/ 目录，与本程序集同目录，因此解析路径取**本程序集所在目录**。
	///
	/// 不能用 <see cref="AppContext.BaseDirectory"/>：那是宿主可执行文件所在目录，并不含
	/// plugins/ 子目录，会解析不到原生件而整体报「FFmpeg 解码不可用」。
	/// 只做一次初始化；失败后记住原因，不再重试（避免每次对比都抛一遍）。
	///
	/// 设计约束（见 design/audio-video-plugins.md §12）：只解码不编码；绑定大版本必须与原生库一致。
	/// </summary>
	public static class MediaNative
	{
		private static readonly object SyncRoot = new object();

		private static bool _initialized;

		private static string _error;

		/// <summary>原生件所在的目录（默认与本程序集同目录）。</summary>
		public static string NativeDirectory { get; set; }

		/// <summary>是否已成功完成绑定初始化。</summary>
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

		/// <summary>初始化失败时的原因（成功为 null）。</summary>
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

		/// <summary>
		/// 确保绑定已初始化。多次调用只初始化一次；失败返回 false 并给出可读原因。
		/// </summary>
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
					string directory = ResolveDirectory();
					if (directory == null)
					{
						throw new DirectoryNotFoundException("native directory not found (tried: " + string.Join(", ", Candidates()) + ")");
					}
					ffmpeg.RootPath = directory;
					DynBindings.LibrariesPath = directory;
					DynBindings.FunctionResolver = CreateResolver();
					DynBindings.Initialize();
					// FFmpeg 默认把 warning 级日志打到 stderr；宿主日志里会出现 mp3 时间戳之类的
					// 解析噪音。只保留 error，真正的问题仍可从解码返回码看到。
					ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
					// 触发一次真实调用，确认符号解析与 ABI 都可用。
					uint version = ffmpeg.avformat_version();
					if (version == 0)
					{
						throw new InvalidOperationException("avformat_version() returned 0");
					}
					_initialized = true;
					error = null;
					return true;
				}
				catch (Exception ex)
				{
					_error = Describe(ex);
					error = _error;
					return false;
				}
			}
		}

		/// <summary>按「显式指定 → 本程序集目录 → 宿主根目录」取第一个存在的目录；都不存在返回 null。</summary>
		private static string ResolveDirectory()
		{
			foreach (string candidate in Candidates())
			{
				if (!string.IsNullOrEmpty(candidate) && Directory.Exists(candidate))
				{
					return candidate;
				}
			}
			return null;
		}

		private static IEnumerable<string> Candidates()
		{
			yield return NativeDirectory;
			string location = typeof(MediaNative).Assembly.Location;
			yield return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
			yield return AppContext.BaseDirectory;
		}

		private static FFmpeg.AutoGen.Bindings.DynamicallyLoaded.IFunctionResolver CreateResolver()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return new WindowsFunctionResolver();
			}
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				return new MacFunctionResolver();
			}
			return new LinuxFunctionResolver();
		}

		private static string Describe(Exception ex)
		{
			return ex.GetType().Name + ": " + ex.Message;
		}
	}
}
