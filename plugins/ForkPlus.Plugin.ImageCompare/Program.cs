using System;
using System.Collections.Generic;
using System.IO;
using ForkPlus.Plugins;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Webp;

namespace ForkPlus.Plugin.ImageCompare
{
	/// <summary>
	/// 图片对比插件：把"某一侧的图片字节"解码成 PNG 帧交回宿主。
	///
	/// 与宿主的分工：
	///   插件只回答"这一侧长什么样"（静态图回 1 帧，GIF/动态 WebP 回 N 帧 + 帧间隔）；
	///   并排 / 滑动 / 洋葱皮 / 像素高亮 / 缩放 / 平移等对比交互仍由宿主统一提供。
	///
	/// 为什么 priority 取 250：内置动图查看器是 200，插件若 ≤200 就永远抢不到 GIF 这类
	/// 被内置高优先级挡住的格式。取 250 表示"清单里声明的后缀由本插件负责"，从而真正接管。
	///
	/// 解码用 ImageSharp（Six Labors Split License，非纯 OSI 许可）。它只活在本进程内，
	/// 与主程序（MIT）以进程边界隔离——插件内换任何许可证的编解码库都不影响主程序。
	/// </summary>
	internal static class Program
	{
		private const string PluginId = "com.forkplus.plugin.image-compare";

		private const string PluginName = "Image Compare Viewer";

		private const string PluginVersion = "1.0.0";

		/// <summary>多帧但读不到帧延时的兜底（毫秒）。</summary>
		private const int DefaultFrameDelayMs = 100;

		private static int Main(string[] args)
		{
			return PluginHost.Run(Handle);
		}

		private static object Handle(PluginRequest request)
		{
			switch (request.Method)
			{
			case PluginProtocol.MethodHello:
				return new HelloResult
				{
					ProtocolVersion = PluginProtocol.Version,
					PluginId = PluginId,
					Name = PluginName,
					Version = PluginVersion
				};
			case PluginProtocol.MethodRender:
				return Render(request.Params?.ToObject<RenderParams>());
			default:
				throw new InvalidOperationException("Unsupported method '" + request.Method + "'.");
			}
		}

		private static RenderResult Render(RenderParams parameters)
		{
			byte[] data = parameters?.Data;
			if (data == null || data.Length == 0)
			{
				// 新增/删除文件的一侧没有字节；插件无法凭空渲染，交由宿主按降级处理。
				throw new InvalidOperationException("No image bytes were provided for this side of the diff.");
			}

			using Image image = Image.Load(data);
			List<byte[]> frames;
			int delayMs;
			if (image.Frames.Count > 1)
			{
				// 动图：逐帧转 PNG，帧间隔交给宿主重建 AnimatedImage 并显示播放条。
				frames = new List<byte[]>(image.Frames.Count);
				for (int i = 0; i < image.Frames.Count; i++)
				{
					using Image frame = image.Frames.CloneFrame(i);
					using MemoryStream frameStream = new MemoryStream();
					frame.SaveAsPng(frameStream);
					frames.Add(frameStream.ToArray());
				}
				delayMs = ResolveFrameDelayMs(image);
			}
			else
			{
				frames = new List<byte[]> { EncodePng(image) };
				delayMs = 0;
			}

			return new RenderResult
			{
				Frames = frames,
				FrameDelayMs = delayMs,
				StatusLabel = "W: " + image.Width + "px | H: " + image.Height + "px"
			};
		}

		private static byte[] EncodePng(Image image)
		{
			using MemoryStream stream = new MemoryStream();
			image.SaveAsPng(stream);
			return stream.ToArray();
		}

		/// <summary>读首选帧的帧间隔；GIF 单位是 1/100 秒，WebP 是毫秒。</summary>
		private static int ResolveFrameDelayMs(Image image)
		{
			try
			{
				GifFrameMetadata gif = image.Frames.RootFrame.Metadata.GetGifMetadata();
				if (gif != null && gif.FrameDelay > 0)
				{
					return gif.FrameDelay * 10;
				}
			}
			catch (Exception)
			{
				// 非 GIF：忽略。
			}
			try
			{
				WebpFrameMetadata webp = image.Frames.RootFrame.Metadata.GetWebpMetadata();
				if (webp != null && webp.FrameDelay > 0)
				{
					return (int)webp.FrameDelay;
				}
			}
			catch (Exception)
			{
				// 非 WebP：忽略。
			}
			return DefaultFrameDelayMs;
		}
	}
}