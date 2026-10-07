using System;
using ForkPlus.Plugins.Media;

namespace ForkPlus.Plugins.Video
{
	/// <summary>
	/// 视频可视化的裸 BGRA 渲染（后台线程执行，产出字节交 UI 线程包成 Bitmap）。
	///
	/// 单帧对比用：先按逐通道最大差判定「变了」的像素，再据此给出变更比例，
	/// 需要高亮时把变了的像素在右侧帧上染成红色（偏好设置里的「高亮差异像素」实时生效）。
	/// </summary>
	internal static class MediaRender
	{
		/// <summary>变更像素的染色（BGRA）：偏红，叠在右侧帧上足够醒目。</summary>
		private static readonly byte[] ChangedTint = new byte[4] { 0x28, 0x38, 0xF0, 0xFF };

		/// <summary>
		/// 逐像素比较两帧，返回「变了的像素占比」（0–1）。尺寸不一致时返回 -1（调用方据此给出提示）。
		/// </summary>
		public static double ComputeDiffRatio(ImageData left, ImageData right)
		{
			if (left == null || right == null || left.Width != right.Width || left.Height != right.Height || left.Bgra == null || right.Bgra == null)
			{
				return -1.0;
			}
			int threshold = MediaLimits.PixelDiffThreshold;
			byte[] a = left.Bgra;
			byte[] b = right.Bgra;
			long changed = 0L;
			long total = (long)left.Width * left.Height;
			for (long i = 0; i < total; i++)
			{
				long o = i * 4L;
				int db = Math.Abs(a[o] - b[o]);
				int dg = Math.Abs(a[o + 1] - b[o + 1]);
				int dr = Math.Abs(a[o + 2] - b[o + 2]);
				int delta = Math.Max(db, Math.Max(dg, dr));
				if (delta > threshold)
				{
					changed++;
				}
			}
			return total > 0L ? (double)changed / total : 0.0;
		}

		/// <summary>
		/// 把「变了的像素」在右侧帧上染色，产出一张差异高亮图。尺寸不一致时返回 null。
		/// </summary>
		public static byte[] RenderHighlight(ImageData left, ImageData right)
		{
			if (left == null || right == null || left.Width != right.Width || left.Height != right.Height || left.Bgra == null || right.Bgra == null)
			{
				return null;
			}
			int threshold = MediaLimits.PixelDiffThreshold;
			byte[] a = left.Bgra;
			byte[] canvas = (byte[])right.Bgra.Clone();
			long total = (long)left.Width * right.Height;
			for (long i = 0; i < total; i++)
			{
				long o = i * 4L;
				int db = Math.Abs(a[o] - canvas[o]);
				int dg = Math.Abs(a[o + 1] - canvas[o + 1]);
				int dr = Math.Abs(a[o + 2] - canvas[o + 2]);
				int delta = Math.Max(db, Math.Max(dg, dr));
				if (delta > threshold)
				{
					canvas[o] = ChangedTint[0];
					canvas[o + 1] = ChangedTint[1];
					canvas[o + 2] = ChangedTint[2];
					canvas[o + 3] = ChangedTint[3];
				}
			}
			return canvas;
		}
	}
}
