using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using FFmpeg.AutoGen.Native;
using DynBindings = FFmpeg.AutoGen.DynamicallyLoadedBindings;

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
	/// 绑定只走自包含的 <c>FFmpeg.AutoGen</c>：它的 <c>ffmpeg</c> 类自带
	/// <see cref="DynBindings"/> 与各平台解析器，并以 <c>ffmpeg.RootPath</c> 定位原生件。
	/// **不要再引 <c>FFmpeg.AutoGen.Bindings.DynamicallyLoaded</c>**——那是另一套并行、互不相通的绑定栈
	/// （命名空间 <c>FFmpeg.AutoGen.Abstractions</c> / <c>FFmpeg.AutoGen.Bindings.DynamicallyLoaded</c>），
	/// 配置它不会影响本程序实际调用的 <c>FFmpeg.AutoGen.ffmpeg</c>，Windows 上会因解析器永不被读取而失败。
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
					// 先装解析器，再设根路径：设置 ffmpeg.RootPath 会触发 ffmpeg 的静态构造，
					// 其中 DynamicallyLoadedBindings.Initialize() 只在 FunctionResolver 为 null 时才建
					// 平台默认解析器。自定义解析器先就位，Windows 的 altered-search-path 才会真正生效。
					DynBindings.FunctionResolver = CreateResolver();
					ffmpeg.RootPath = directory;
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

		/// <summary>
		/// 按「显式指定 → 本程序集目录 → 宿主根目录」取第一个存在的目录；都不存在返回 null。
		/// 供同程序集的其它原生绑定（如 <c>MiniAudioNative</c>）复用，保证两者从同一处解析。
		/// </summary>
		public static string ResolveDirectory()
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

		private static IFunctionResolver CreateResolver()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return new WindowsAlteredSearchFunctionResolver();
			}
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				return new MacFunctionResolver();
			}
			return new LinuxFunctionResolver();
		}

		/// <summary>
		/// Windows 专用解析器：FFmpeg 的 DLL 是按完整路径加载的，但它们的**非 FFmpeg 运行期依赖**
		/// （随交付件一起平铺在插件目录的 <c>zlib1.dll</c> / <c>libwinpthread-1.dll</c> /
		/// <c>libgcc_s_seh-1.dll</c>）不在 FFmpeg.AutoGen 的依赖预加载表里，默认的
		/// <c>LoadLibrary</c> 只按「宿主可执行文件目录 + 系统目录 + PATH」找依赖，**不会看被加载
		/// DLL 自己所在的目录**，于是 avformat 等一律加载失败——表现为「FFmpeg 解码不可用」，
		/// 音频和视频都放不出来（Windows 尤其明显）。
		///
		/// 改用 <c>LOAD_WITH_ALTERED_SEARCH_PATH</c>：依赖优先在被加载 DLL 自己的目录里找，
		/// 插件把原生件平铺在同一目录即可自洽。Linux / macOS 无此问题——前者靠基类的依赖预加载
		/// 先命中已加载的 SONAME，后者的 install_name 是 <c>@loader_path</c>。
		/// </summary>
		private sealed class WindowsAlteredSearchFunctionResolver : WindowsFunctionResolver
		{
			private const uint LoadWithAlteredSearchPath = 0x00000008;

			protected override IntPtr LoadNativeLibrary(string libraryName)
			{
				return LoadLibraryEx(libraryName, IntPtr.Zero, LoadWithAlteredSearchPath);
			}

			[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
			private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);
		}

		private static string Describe(Exception ex)
		{
			return ex.GetType().Name + ": " + ex.Message;
		}
	}
}
