using System;

namespace ForkPlus.Plugins.Media
{
	/// <summary>迭代式基 2 复数 FFT（就地）。仅音频频谱分析用，规模小，不追求极致性能。</summary>
	internal static class Fft
	{
		/// <summary>对长度为 2 的幂的 <paramref name="re"/> / <paramref name="im"/> 做正向变换（就地）。</summary>
		internal static void Forward(float[] re, float[] im)
		{
			int n = re.Length;
			if (n < 2 || (n & (n - 1)) != 0 || im.Length != n)
			{
				throw new ArgumentException("fft size must be a power of two");
			}
			// 位反转置换
			for (int i = 1, j = 0; i < n; i++)
			{
				int bit = n >> 1;
				for (; (j & bit) != 0; bit >>= 1)
				{
					j ^= bit;
				}
				j ^= bit;
				if (i < j)
				{
					float tr = re[i];
					re[i] = re[j];
					re[j] = tr;
					float ti = im[i];
					im[i] = im[j];
					im[j] = ti;
				}
			}
			for (int len = 2; len <= n; len <<= 1)
			{
				double angle = -2.0 * Math.PI / len;
				float wr = (float)Math.Cos(angle);
				float wi = (float)Math.Sin(angle);
				int half = len >> 1;
				for (int i = 0; i < n; i += len)
				{
					float cwr = 1f;
					float cwi = 0f;
					for (int k = 0; k < half; k++)
					{
						int a = i + k;
						int b = a + half;
						float vr = re[b] * cwr - im[b] * cwi;
						float vi = re[b] * cwi + im[b] * cwr;
						float ur = re[a];
						float ui = im[a];
						re[a] = ur + vr;
						im[a] = ui + vi;
						re[b] = ur - vr;
						im[b] = ui - vi;
						float nextWr = cwr * wr - cwi * wi;
						cwi = cwr * wi + cwi * wr;
						cwr = nextWr;
					}
				}
			}
		}
	}
}
