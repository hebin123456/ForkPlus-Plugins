using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ForkPlus.Plugins.Media
{
	/// <summary>
	/// 音频分析：把一侧音频解成单声道样本后汇总成波形包络 / 声谱图。
	///
	/// 两种分析都走 <see cref="AudioDecoder"/> 的流式解码，且都受 <see cref="MediaLimits"/> 的秒数上限约束
	/// （超出即截断并在结果里置 <c>Truncated</c>，由插件在界面注明）。全程在调用方线程（后台）执行，
	/// 出错一律返回 null + <see cref="MediaFailure"/>，不抛异常。
	/// </summary>
	public static class MediaAudio
	{
		/// <summary>
		/// 波形包络：按时间把单声道样本分桶，每桶记 min / max / RMS。
		/// <paramref name="buckets"/> 为期望桶数（受 <see cref="MediaLimits.MaxWaveBuckets"/> 限制）。
		/// </summary>
		public static WaveformData AnalyzeWaveform(MemoryStream stream, int buckets, double maxSeconds, CancellationToken token, out MediaFailure failure)
		{
			failure = null;
			if (stream == null)
			{
				failure = new MediaFailure(MediaErrorKind.NoData, "no data");
				return null;
			}
			if (buckets < 1)
			{
				buckets = 1;
			}
			if (buckets > MediaLimits.MaxWaveBuckets)
			{
				buckets = MediaLimits.MaxWaveBuckets;
			}
			double limit = maxSeconds > 0.0 ? maxSeconds : MediaLimits.MaxAudioSeconds;
			int rate = MediaLimits.WaveformSampleRate;

			// 先探测时长，才能把「已解出的样本序号」映射到固定的桶区间；探测很便宜。
			double duration = 0.0;
			try
			{
				MediaInfo probe = MediaProbe.Probe(stream, 0L, out MediaFailure probeFailure);
				if (probe != null)
				{
					duration = probe.DurationSeconds;
				}
				else if (probeFailure != null && probeFailure.Kind == MediaErrorKind.Unavailable)
				{
					failure = probeFailure;
					return null;
				}
			}
			catch
			{
				duration = 0.0;
			}
			double totalSeconds = duration > 0.0 ? Math.Min(duration, limit) : limit;
			long totalSamples = (long)Math.Max(1.0, totalSeconds * rate);

			float[] min = new float[buckets];
			float[] max = new float[buckets];
			double[] sumSquares = new double[buckets];
			long[] counts = new long[buckets];
			long index = 0L;
			AudioDecoder.SampleHandler handler = delegate(float[] buffer, int count)
			{
				for (int i = 0; i < count; i++)
				{
					long bucket = index * buckets / totalSamples;
					if (bucket >= buckets)
					{
						bucket = buckets - 1;
					}
					if (bucket < 0L)
					{
						bucket = 0L;
					}
					float value = buffer[i];
					int b = (int)bucket;
					if (value < min[b])
					{
						min[b] = value;
					}
					if (value > max[b])
					{
						max[b] = value;
					}
					sumSquares[b] += (double)value * value;
					counts[b]++;
					index++;
				}
			};

			if (!AudioDecoder.Run(stream, rate, limit, token, handler, out AudioDecoder.Result result, out failure))
			{
				return null;
			}
			float[] rms = new float[buckets];
			for (int b = 0; b < buckets; b++)
			{
				rms[b] = counts[b] > 0L ? (float)Math.Sqrt(sumSquares[b] / counts[b]) : 0f;
			}
			return new WaveformData
			{
				Min = min,
				Max = max,
				Rms = rms,
				AnalyzedSeconds = result.AnalyzedSeconds,
				TotalSeconds = result.DurationSeconds > 0.0 ? result.DurationSeconds : totalSeconds,
				Truncated = result.Truncated,
				SampleRate = rate,
				Channels = result.Channels
			};
		}

		/// <summary>
		/// 声谱图：STFT 后按对数频率分箱成 <see cref="MediaLimits.SpectrumRows"/> 行、最多
		/// <see cref="MediaLimits.MaxSpectrumColumns"/> 列的灰度矩阵（列优先，值域 0–255）。
		/// 分析窗口为 <see cref="MediaLimits.FftSize"/>，样本先降到 <see cref="MediaLimits.SpectrumSampleRate"/>。
		/// </summary>
		public static SpectrogramData AnalyzeSpectrum(MemoryStream stream, double maxSeconds, CancellationToken token, out MediaFailure failure)
		{
			failure = null;
			if (stream == null)
			{
				failure = new MediaFailure(MediaErrorKind.NoData, "no data");
				return null;
			}
			double limit = maxSeconds > 0.0 ? maxSeconds : MediaLimits.MaxSpectrumSeconds;
			int rate = MediaLimits.SpectrumSampleRate;
			int capacity = (int)Math.Min((long)int.MaxValue / 2L, (long)(limit * rate) + rate);
			float[] samples = new float[capacity];
			int filled = 0;
			AudioDecoder.SampleHandler handler = delegate(float[] buffer, int count)
			{
				int room = samples.Length - filled;
				if (room <= 0)
				{
					return;
				}
				int copy = Math.Min(room, count);
				Array.Copy(buffer, 0, samples, filled, copy);
				filled += copy;
			};
			if (!AudioDecoder.Run(stream, rate, limit, token, handler, out AudioDecoder.Result result, out failure))
			{
				return null;
			}
			int fftSize = MediaLimits.FftSize;
			if (filled < fftSize)
			{
				failure = new MediaFailure(MediaErrorKind.Failed, "audio too short for spectrum");
				return null;
			}
			int rows = MediaLimits.SpectrumRows;
			int hop = Math.Max(fftSize / 2, filled / MediaLimits.MaxSpectrumColumns);
			int columns = Math.Min(MediaLimits.MaxSpectrumColumns, (filled - fftSize) / hop + 1);
			if (columns < 1)
			{
				columns = 1;
			}
			double[] window = new double[fftSize];
			for (int i = 0; i < fftSize; i++)
			{
				window[i] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (fftSize - 1));
			}
			// 对数频率分箱：20 Hz – Nyquist，每行覆盖 [low, high) 的 FFT 频点并取平均能量。
			int[] binStart = new int[rows + 1];
			double minHz = 20.0;
			double maxHz = rate / 2.0;
			for (int r = 0; r <= rows; r++)
			{
				double fraction = (double)r / rows;
				double hz = minHz * Math.Pow(maxHz / minHz, fraction);
				int bin = (int)Math.Round(hz / maxHz * (fftSize / 2));
				if (bin < 0)
				{
					bin = 0;
				}
				if (bin > fftSize / 2)
				{
					bin = fftSize / 2;
				}
				if (r > 0 && bin < binStart[r - 1])
				{
					bin = binStart[r - 1];
				}
				binStart[r] = bin;
			}

			float[] re = new float[fftSize];
			float[] im = new float[fftSize];
			double[] columnDb = new double[columns * rows];
			double maxDb = double.NegativeInfinity;
			double floorDb = -96.0;
			for (int c = 0; c < columns; c++)
			{
				if (token.IsCancellationRequested)
				{
					return null;
				}
				int start = c * hop;
				int available = Math.Min(fftSize, filled - start);
				for (int i = 0; i < fftSize; i++)
				{
					re[i] = i < available ? (float)(samples[start + i] * window[i]) : 0f;
					im[i] = 0f;
				}
				Fft.Forward(re, im);
				for (int r = 0; r < rows; r++)
				{
					int from = binStart[r];
					int to = binStart[r + 1];
					if (to <= from)
					{
						to = Math.Min(from + 1, fftSize / 2);
					}
					double sum = 0.0;
					int n = 0;
					for (int k = from; k < to; k++)
					{
						double magnitude = Math.Sqrt((double)re[k] * re[k] + (double)im[k] * im[k]);
						sum += magnitude * magnitude;
						n++;
					}
					double power = n > 0 ? sum / n : 0.0;
					double db = 10.0 * Math.Log10(power / (fftSize * (double)fftSize) + 1e-12);
					columnDb[c * rows + r] = db;
					if (db > maxDb)
					{
						maxDb = db;
					}
				}
			}
			if (double.IsNegativeInfinity(maxDb))
			{
				maxDb = 0.0;
			}
			double top = maxDb;
			double bottom = maxDb + floorDb;
			double span = top - bottom;
			if (span <= 0.0)
			{
				span = 1.0;
			}
			byte[] magnitudes = new byte[columns * rows];
			for (int i = 0; i < magnitudes.Length; i++)
			{
				double normalized = (columnDb[i] - bottom) / span;
				int value = (int)Math.Round(normalized * 255.0);
				magnitudes[i] = (byte)(value < 0 ? 0 : (value > 255 ? 255 : value));
			}
			return new SpectrogramData
			{
				Columns = columns,
				Rows = rows,
				Magnitude = magnitudes,
				AnalyzedSeconds = result.AnalyzedSeconds,
				Truncated = result.Truncated,
				SampleRate = rate
			};
		}
	}
}
