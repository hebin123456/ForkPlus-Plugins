using System;
using System.Collections.Generic;
using ForkPlus.Plugins;
using Newtonsoft.Json.Linq;

namespace ForkPlus.Plugin.ExampleSwatch
{
	/// <summary>
	/// 最小示例插件：声明 .swatch / .swx 两个后缀，把"这一侧的字节"画成一张色块图交回宿主。
	/// 重点不在画图，而在演示插件仓里一个插件的最小完整形态：
	///   plugin.json（清单）+ 一个可执行宿主进程（本文件）+ 引用 SDK。
	/// 协议主循环（hello / render / shutdown、分帧、异常转错误）已收敛到 SDK 的 PluginHost.Run。
	/// </summary>
	internal static class Program
	{
		private const string PluginId = "com.forkplus.plugin.example-swatch";

		private const string PluginName = "Example Swatch Viewer";

		private const string PluginVersion = "1.0.0";

		private const int SwatchSize = 128;

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
			byte[] data = parameters?.Data ?? new byte[0];
			byte[] rgba = BuildSwatch(SwatchSize, Fnv1a(data), data.Length);
			return new RenderResult
			{
				Frames = new List<byte[]> { PngWriter.Encode(SwatchSize, SwatchSize, rgba) },
				FrameDelayMs = 0,
				StatusLabel = "example swatch · " + data.Length + " bytes"
			};
		}

		/// <summary>按输入字节画一张"色块 + 边框 + 斜纹"示意图，同样的内容出同样的图。</summary>
		private static byte[] BuildSwatch(int size, uint seed, int dataLength)
		{
			byte[] rgba = new byte[size * size * 4];
			byte baseR = (byte)((seed >> 16) & 0xFF);
			byte baseG = (byte)((seed >> 8) & 0xFF);
			byte baseB = (byte)(seed & 0xFF);
			// 字节数也参与配色，方便肉眼确认"渲染确实读了输入内容"。
			baseR = (byte)(baseR ^ (dataLength & 0xFF));
			baseG = (byte)(baseG ^ ((dataLength >> 8) & 0xFF));
			int pos = 0;
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					byte r = (byte)((baseR + x) & 0xFF);
					byte g = (byte)((baseG + y) & 0xFF);
					byte b = baseB;
					if (x < 8 || y < 8 || x >= size - 8 || y >= size - 8)
					{
						// 边框：反相，边界一眼可辨。
						r = (byte)(255 - r);
						g = (byte)(255 - g);
						b = (byte)(255 - b);
					}
					else if (((x + y) & 15) < 4)
					{
						// 斜纹：证明不是纯色常量图。
						r = (byte)(r >> 1);
						g = (byte)(g >> 1);
						b = (byte)(b >> 1);
					}
					rgba[pos++] = r;
					rgba[pos++] = g;
					rgba[pos++] = b;
					rgba[pos++] = 255;
				}
			}
			return rgba;
		}

		/// <summary>FNV-1a：把输入字节折成一个稳定的配色种子。</summary>
		private static uint Fnv1a(byte[] data)
		{
			uint hash = 2166136261u;
			for (int i = 0; i < data.Length; i++)
			{
				hash ^= data[i];
				hash *= 16777619u;
			}
			return hash;
		}
	}
}