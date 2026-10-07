using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ForkPlus.Plugins.Abstractions;
using ForkPlus.Plugins.Media;

namespace ForkPlus.Plugins.Video
{
	/// <summary>
	/// BGRA 字节 → Avalonia 位图。取帧与像素差异都在后台线程算成裸 BGRA 字节，
	/// 这里只在 UI 线程把它们包成可显示的 <see cref="Bitmap"/>（WriteableBitmap 锁帧直拷）。
	/// </summary>
	internal static class MediaImage
	{
		public static Bitmap FromImageData(ImageData data)
		{
			return data == null ? null : FromBgra(data.Bgra, data.Width, data.Height);
		}

		public static Bitmap FromBgra(byte[] bgra, int width, int height)
		{
			if (bgra == null || width <= 0 || height <= 0)
			{
				return null;
			}
			try
			{
				WriteableBitmap bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96.0, 96.0), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
				int rowBytes = width * 4;
				using (ILockedFramebuffer buffer = bitmap.Lock())
				{
					for (int y = 0; y < height; y++)
					{
						Marshal.Copy(bgra, y * rowBytes, IntPtr.Add(buffer.Address, y * rowBytes), rowBytes);
					}
				}
				return bitmap;
			}
			catch (System.Exception ex)
			{
				PluginLog.Warn("Video: failed to build bitmap", ex);
				return null;
			}
		}
	}
}
