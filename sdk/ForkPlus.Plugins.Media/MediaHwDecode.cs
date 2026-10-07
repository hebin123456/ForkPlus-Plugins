using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace ForkPlus.Plugins.Media
{
	/// <summary>
	/// 硬件加速解码的设备与帧搬运，供播放路径使用（见 design/audio-video-plugins.md §12）。
	///
	/// 用的是 FFmpeg 的「设了 <c>hw_device_ctx</c> 让默认 get_format 自己挑硬解像素格式」这条路：
	/// 不需要自己写 get_format 回调（那条路要处理各平台像素格式白名单，复杂且易错），
	/// 解码器拿不到可用的硬解格式时会自动退回软解，**失败不是致命错误**。
	///
	/// 设备类型按平台只试一个（与三方件仓交付件里启用的 hwaccel 对应）：
	///   Windows → d3d11va / macOS → videotoolbox / Linux → vulkan。
	/// 三方件仓故意没开 vaapi / vdpau（会给 libavutil 引入 libva / libvdpau 硬依赖），
	/// 所以这里也不去试它们——试了必然拿不到，白费一次设备创建。
	///
	/// 只在**播放**路径启用：单帧取图 / 帧条每次只解一两帧，设备创建的开销（几十到上百毫秒）
	/// 反而比软解还贵；连续播放才摊得平。
	/// </summary>
	internal static unsafe class MediaHwDecode
	{
		/// <summary>本平台要尝试的设备类型；不支持返回 <see cref="AVHWDeviceType.AV_HWDEVICE_TYPE_NONE"/>。</summary>
		internal static AVHWDeviceType PreferredDeviceType()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				return AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA;
			}
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				return AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX;
			}
			return AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN;
		}

		/// <summary>
		/// 创建硬解设备；失败返回 null（调用方照旧走软解）。
		/// </summary>
		internal static AVBufferRef* CreateDevice(AVHWDeviceType type, out string error)
		{
			error = null;
			if (type == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
			{
				error = "no hardware device type for this platform";
				return null;
			}
			try
			{
				AVBufferRef* device = null;
				// device 传 null：让 FFmpeg 自己挑默认驱动（Vulkan 的 ICD、D3D11 的默认 adapter 等）
				int result = ffmpeg.av_hwdevice_ctx_create(&device, type, null, null, 0);
				if (result < 0 || device == null)
				{
					error = "av_hwdevice_ctx_create(" + ffmpeg.av_hwdevice_get_type_name(type) + ") failed: " + FfError.Describe(result);
					return null;
				}
				return device;
			}
			catch (Exception ex)
			{
				error = ex.GetType().Name + ": " + ex.Message;
				return null;
			}
		}

		/// <summary>把设备挂到解码上下文上（open 之前调用）。</summary>
		internal static void Attach(AVCodecContext* context, AVBufferRef* device)
		{
			if (context == null || device == null)
			{
				return;
			}
			context->hw_device_ctx = ffmpeg.av_buffer_ref(device);
		}

		/// <summary>
		/// 判断一帧是不是硬件帧（硬解出来的帧带 hw_frames_ctx，像素格式是 AV_PIX_FMT_*VAAPI 之类，
		/// 不能直接喂给 sws_scale）。
		/// </summary>
		internal static bool IsHardwareFrame(AVFrame* frame)
		{
			return frame != null && frame->hw_frames_ctx != null;
		}

		/// <summary>把硬件帧拷回系统内存（<paramref name="software"/> 需预先 alloc）。成功返回 true。</summary>
		internal static bool TryTransferToSoftware(AVFrame* hardware, AVFrame* software)
		{
			if (hardware == null || software == null)
			{
				return false;
			}
			try
			{
				ffmpeg.av_frame_unref(software);
				return ffmpeg.av_hwframe_transfer_data(software, hardware, 0) >= 0;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>本进程可用的硬解设备类型清单（诊断用，不参与决策）。</summary>
		internal static IReadOnlyList<string> AvailableDeviceTypes()
		{
			List<string> types = new List<string>();
			try
			{
				AVHWDeviceType type = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
				while (true)
				{
					type = ffmpeg.av_hwdevice_iterate_types(type);
					if (type == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
					{
						break;
					}
					string name = ffmpeg.av_hwdevice_get_type_name(type);
					if (!string.IsNullOrEmpty(name))
					{
						types.Add(name);
					}
					if (types.Count > 32)
					{
						break;
					}
				}
			}
			catch
			{
				// 只有一个迭代器，异常时返回已收集到的部分
			}
			return types;
		}
	}
}
