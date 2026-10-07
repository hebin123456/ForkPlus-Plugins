using System;
using FFmpeg.AutoGen;

namespace ForkPlus.Plugins.Media
{
	/// <summary>AVFrame → BGRA <see cref="ImageData"/>（sws_scale 缩放到目标尺寸，保持比例）。</summary>
	internal static unsafe class MediaConvert
	{
		/// <summary>
		/// 把一帧缩放到不超过 <paramref name="maxWidth"/> × <paramref name="maxHeight"/> 的 BGRA 位图。
		/// 源比目标小时不放大。失败返回 null。
		/// </summary>
		internal static ImageData FrameToImage(AVFrame* frame, int maxWidth, int maxHeight)
		{
			if (frame == null || frame->width <= 0 || frame->height <= 0)
			{
				return null;
			}
			int srcWidth = frame->width;
			int srcHeight = frame->height;
			double scale = 1.0;
			if (maxWidth > 0 && srcWidth > maxWidth)
			{
				scale = Math.Min(scale, (double)maxWidth / srcWidth);
			}
			if (maxHeight > 0 && srcHeight > maxHeight)
			{
				scale = Math.Min(scale, (double)maxHeight / srcHeight);
			}
			int dstWidth = Math.Max(1, (int)Math.Round(srcWidth * scale));
			int dstHeight = Math.Max(1, (int)Math.Round(srcHeight * scale));

			SwsContext* sws = null;
			byte[] buffer = null;
			try
			{
				sws = ffmpeg.sws_getContext(
					srcWidth, srcHeight, (AVPixelFormat)frame->format,
					dstWidth, dstHeight, AVPixelFormat.AV_PIX_FMT_BGRA,
					(int)SwsFlags.SWS_BILINEAR, null, null, null);
				if (sws == null)
				{
					return null;
				}
				buffer = new byte[dstWidth * dstHeight * 4];
				fixed (byte* destination = buffer)
				{
					byte*[] dstData = new byte*[4];
					int[] dstLinesize = new int[4];
					dstData[0] = destination;
					dstLinesize[0] = dstWidth * 4;
					ffmpeg.sws_scale(sws, frame->data, frame->linesize, 0, srcHeight, dstData, dstLinesize);
				}
				return new ImageData
				{
					Bgra = buffer,
					Width = dstWidth,
					Height = dstHeight
				};
			}
			catch
			{
				return null;
			}
			finally
			{
				if (sws != null)
				{
					ffmpeg.sws_freeContext(sws);
				}
			}
		}
	}
}
