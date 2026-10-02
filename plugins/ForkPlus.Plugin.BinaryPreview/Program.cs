using System;
using System.Collections.Generic;
using ForkPlus.Plugins;
using Newtonsoft.Json.Linq;

namespace ForkPlus.Plugin.BinaryPreview
{
	/// <summary>
	/// 二进制结构预览插件：把任意二进制文件的字节画成一张"字节热力图"（每行 16 字节，
	/// 每个字节按取值上色）交回宿主显示。
	///
	/// 用途：.bin/.so/.class/.wasm 这类文件在对比时原本只能看到"文件卡片"，
	/// 借此可以并排/滑动地看到两侧字节分布的差异（结构不同 → 热力图不同）。
	/// 渲染完全在本进程内、无第三方依赖；真实插件可以在这里接 GPL 的格式解析库。
	/// </summary>
	internal static class Program
	{
		private const string PluginId = "com.forkplus.plugin.binary-preview";

		private const string PluginName = "Binary Structure Preview";

		private const string PluginVersion = "1.0.0";

		/// <summary>每行 16 字节（经典 hex dump 布局）。</summary>
		private const int BytesPerRow = 16;

		/// <summary>单元格尺寸与间隙（像素）。</summary>
		private const int CellWidth = 12;

		private const int CellHeight = 16;

		private const int Gap = 2;

		private const int Padding = 8;

		/// <summary>最多画多少行——防止一个几百 MB 的文件画出一张天文数字的图。</summary>
		private const int MaxRows = 64;

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
				throw new InvalidOperationException("No bytes were provided for this side of the diff.");
			}

			int totalRows = (data.Length + BytesPerRow - 1) / BytesPerRow;
			int rows = Math.Min(totalRows, MaxRows);
			int width = Padding * 2 + BytesPerRow * CellWidth + (BytesPerRow - 1) * Gap;
			int height = Padding * 2 + rows * CellHeight + (rows - 1) * Gap;

			byte[] rgba = new byte[width * height * 4];
			Fill(rgba, width, height, 30, 30, 36); // 背景

			for (int row = 0; row < rows; row++)
			{
				int y0 = Padding + row * (CellHeight + Gap);
				for (int col = 0; col < BytesPerRow; col++)
				{
					int index = row * BytesPerRow + col;
					if (index >= data.Length)
					{
						break;
					}
					int x0 = Padding + col * (CellWidth + Gap);
					Heat(data[index], out byte r, out byte g, out byte b);
					FillRect(rgba, width, height, x0, y0, CellWidth, CellHeight, r, g, b);
				}
			}

			string suffix = (totalRows > rows) ? " (showing first " + rows + " rows)" : string.Empty;
			return new RenderResult
			{
				Frames = new List<byte[]> { PngWriter.Encode(width, height, rgba) },
				FrameDelayMs = 0,
				StatusLabel = data.Length + " bytes · " + BytesPerRow + "/row" + suffix
			};
		}

		/// <summary>字节取值 → 颜色：低值偏蓝、中值偏绿、高值偏红（0 值压暗以示区分）。</summary>
		private static void Heat(byte value, out byte r, out byte g, out byte b)
		{
			if (value == 0)
			{
				// 全零字节（二进制文件里最常见）压暗，避免整屏蓝。
				r = 45;
				g = 45;
				b = 52;
				return;
			}
			if (value < 128)
			{
				byte t = (byte)(value * 2);
				r = 0;
				g = t;
				b = (byte)(255 - t);
				return;
			}
			byte u = (byte)((value - 128) * 2);
			r = u;
			g = (byte)(255 - u);
			b = 0;
		}

		private static void Fill(byte[] rgba, int width, int height, byte r, byte g, byte b)
		{
			FillRect(rgba, width, height, 0, 0, width, height, r, g, b);
		}

		private static void FillRect(byte[] rgba, int width, int height, int x0, int y0, int w, int h, byte r, byte g, byte b)
		{
			for (int y = y0; y < y0 + h; y++)
			{
				if (y < 0 || y >= height)
				{
					continue;
				}
				int rowStart = y * width * 4;
				for (int x = x0; x < x0 + w; x++)
				{
					if (x < 0 || x >= width)
					{
						continue;
					}
					int pos = rowStart + x * 4;
					rgba[pos] = r;
					rgba[pos + 1] = g;
					rgba[pos + 2] = b;
					rgba[pos + 3] = 255;
				}
			}
		}
	}
}