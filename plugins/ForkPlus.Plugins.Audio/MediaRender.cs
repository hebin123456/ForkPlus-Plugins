using System;
using ForkPlus.Plugins.Media;

namespace ForkPlus.Plugins.Audio
{
	/// <summary>
	/// 音频可视化的裸 BGRA 渲染（后台线程执行，产出字节交 UI 线程包成 Bitmap）。
	///
	/// - 波形：按桶把 min / max 画成包络竖线，RMS 叠一层更亮的核心；左右两侧同尺寸对齐。
	/// - 差异：两侧 RMS 逐桶求差，越大的桶越暖，一眼看出「哪几秒的声音变了」。
	/// - 频谱：STFT 灰度矩阵按「低频在下」铺开，套一层冷→暖渐变。
	/// </summary>
	internal static class MediaRender
	{
		public const int WaveWidth = 1200;

		public const int WaveHeight = 168;

		public const int DiffHeight = 40;

		public const int SpectrumWidth = 960;

		public const int SpectrumHeight = 320;

		private static readonly byte[] BgColor = new byte[3] { 0xF5, 0xF5, 0xF7 };

		private static readonly byte[] GridColor = new byte[3] { 0xDD, 0xDD, 0xE2 };

		private static readonly byte[] LeftColor = new byte[3] { 0x4A, 0x90, 0xE2 };

		private static readonly byte[] LeftCore = new byte[3] { 0x1E, 0x5F, 0xB8 };

		private static readonly byte[] RightColor = new byte[3] { 0xE0, 0x5A, 0x5A };

		private static readonly byte[] RightCore = new byte[3] { 0xB0, 0x2A, 0x2A };

		// 频谱渐变色标：暗蓝 → 青蓝 → 暖黄。
		private static readonly byte[][] Heat = new byte[][]
		{
			new byte[3] { 0x0A, 0x0E, 0x28 },
			new byte[3] { 0x1E, 0x5F, 0xB8 },
			new byte[3] { 0x3C, 0xC0, 0xC0 },
			new byte[3] { 0xF2, 0xC8, 0x50 },
			new byte[3] { 0xFF, 0xFF, 0xF0 }
		};

		public static byte[] RenderWaveform(WaveformData wave, int width, int height, bool right)
		{
			byte[] canvas = NewCanvas(width, height, BgColor);
			DrawHorizontalGrid(canvas, width, height);
			if (wave == null || wave.Buckets <= 0)
			{
				return canvas;
			}
			byte[] accent = right ? RightColor : LeftColor;
			byte[] core = right ? RightCore : LeftCore;
			int buckets = wave.Buckets;
			int mid = height / 2;
			int amp = mid - 2;
			if (amp < 1)
			{
				amp = 1;
			}
			for (int x = 0; x < width; x++)
			{
				int b = (int)((long)x * buckets / width);
				if (b >= buckets)
				{
					b = buckets - 1;
				}
				if (b < 0)
				{
					b = 0;
				}
				int top = mid - (int)(Clamp01(wave.Max[b]) * amp);
				int bottom = mid - (int)(Clamp01(wave.Min[b]) * amp);
				if (bottom < top)
				{
					int swap = top;
					top = bottom;
					bottom = swap;
				}
				DrawVerticalLine(canvas, width, height, x, top, bottom, accent);
				int rmsPixels = (int)(Clamp01(wave.Rms[b]) * amp);
				DrawVerticalLine(canvas, width, height, x, mid - rmsPixels, mid + rmsPixels, core);
			}
			return canvas;
		}

		/// <summary>两侧 RMS 逐桶求差：差异越大颜色越暖，画成一条差异带。</summary>
		public static byte[] RenderDifference(WaveformData left, WaveformData right, int width, int height)
		{
			byte[] canvas = NewCanvas(width, height, new byte[3] { 0xFA, 0xFA, 0xFC });
			if (left == null || right == null)
			{
				return canvas;
			}
			int buckets = Math.Min(left.Buckets, right.Buckets);
			if (buckets <= 0)
			{
				return canvas;
			}
			for (int x = 0; x < width; x++)
			{
				int b = (int)((long)x * buckets / width);
				if (b >= buckets)
				{
					b = buckets - 1;
				}
				double delta = Math.Abs(left.Rms[b] - right.Rms[b]);
				// 0.25 以上的 RMS 差即视为显著（声音明显变了）。
				double intensity = Math.Min(1.0, delta / 0.25);
				if (intensity <= 0.02)
				{
					continue;
				}
				byte[] color = SampleHeat(intensity);
				for (int y = 0; y < height; y++)
				{
					SetPixel(canvas, width, height, x, y, color);
				}
			}
			return canvas;
		}

		public static byte[] RenderSpectrum(SpectrogramData spectrum, int width, int height)
		{
			byte[] canvas = NewCanvas(width, height, BgColor);
			if (spectrum == null || spectrum.Magnitude == null || spectrum.Columns <= 0 || spectrum.Rows <= 0)
			{
				return canvas;
			}
			int columns = spectrum.Columns;
			int rows = spectrum.Rows;
			for (int x = 0; x < width; x++)
			{
				int c = (int)((long)x * columns / width);
				if (c >= columns)
				{
					c = columns - 1;
				}
				for (int y = 0; y < height; y++)
				{
					// 低频在下：屏幕 y 增大对应频率降低。
					int r = rows - 1 - (int)((long)y * rows / height);
					if (r < 0)
					{
						r = 0;
					}
					if (r >= rows)
					{
						r = rows - 1;
					}
					byte value = spectrum.Magnitude[c * rows + r];
					SetPixel(canvas, width, height, x, y, SampleHeat(value / 255.0));
				}
			}
			return canvas;
		}

		private static float Clamp01(float value)
		{
			if (value < -1f)
			{
				return -1f;
			}
			if (value > 1f)
			{
				return 1f;
			}
			return value;
		}

		private static byte[] SampleHeat(double t)
		{
			if (t < 0.0)
			{
				t = 0.0;
			}
			if (t > 1.0)
			{
				t = 1.0;
			}
			double scaled = t * (Heat.Length - 1);
			int index = (int)scaled;
			if (index >= Heat.Length - 1)
			{
				return Heat[Heat.Length - 1];
			}
			double f = scaled - index;
			byte[] a = Heat[index];
			byte[] b = Heat[index + 1];
			return new byte[3]
			{
				(byte)(a[0] + (b[0] - a[0]) * f),
				(byte)(a[1] + (b[1] - a[1]) * f),
				(byte)(a[2] + (b[2] - a[2]) * f)
			};
		}

		private static byte[] NewCanvas(int width, int height, byte[] background)
		{
			byte[] canvas = new byte[width * height * 4];
			for (int i = 0; i < width * height; i++)
			{
				int o = i * 4;
				canvas[o] = background[2];
				canvas[o + 1] = background[1];
				canvas[o + 2] = background[0];
				canvas[o + 3] = 0xFF;
			}
			return canvas;
		}

		private static void DrawHorizontalGrid(byte[] canvas, int width, int height)
		{
			int mid = height / 2;
			DrawHorizontalLine(canvas, width, height, mid, GridColor);
			DrawHorizontalLine(canvas, width, height, mid / 2, GridColor);
			DrawHorizontalLine(canvas, width, height, mid + mid / 2, GridColor);
		}

		private static void DrawHorizontalLine(byte[] canvas, int width, int height, int y, byte[] color)
		{
			if (y < 0 || y >= height)
			{
				return;
			}
			for (int x = 0; x < width; x++)
			{
				SetPixel(canvas, width, height, x, y, color);
			}
		}

		private static void DrawVerticalLine(byte[] canvas, int width, int height, int x, int from, int to, byte[] color)
		{
			if (from < 0)
			{
				from = 0;
			}
			if (to >= height)
			{
				to = height - 1;
			}
			for (int y = from; y <= to; y++)
			{
				SetPixel(canvas, width, height, x, y, color);
			}
		}

		private static void SetPixel(byte[] canvas, int width, int height, int x, int y, byte[] rgb)
		{
			if (x < 0 || x >= width || y < 0 || y >= height)
			{
				return;
			}
			int o = (y * width + x) * 4;
			canvas[o] = rgb[2];
			canvas[o + 1] = rgb[1];
			canvas[o + 2] = rgb[0];
			canvas[o + 3] = 0xFF;
		}
	}
}
