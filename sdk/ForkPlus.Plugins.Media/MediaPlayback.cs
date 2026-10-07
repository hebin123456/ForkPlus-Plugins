using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FFmpeg.AutoGen;

namespace ForkPlus.Plugins.Media
{
	/// <summary>
	/// 共享媒体播放引擎：一路字节 → 最多两路独立解码（音频 / 视频）+ 音频输出 + 时钟同步。
	///
	/// 设计要点（见 design/audio-video-plugins.md §5 / §8 / §12）：
	/// - 音频、视频各开**独立的** avio / AVFormatContext（同一份 byte[] 上两个 MemoryStream，不复制字节），
	///   哪路打不开只把对应 Has* 置 false，不整体失败；两路都打不开才置 <see cref="Error"/>。
	/// - 三条后台线程：音频生产者（<c>fpp_audio_free_frames</c> 背压）、视频生产者（等帧该出现的时间再投递）、
	///   10Hz 节拍线程。全部靠 <c>volatile</c> + <see cref="Monitor"/> 协作，<see cref="Dispose"/> 时干净退出。
	/// - 时钟：有音频且输出可用时以「已播帧数 / 采样率」为主时钟（欠载补静音时停住，视频跟着等，不漂）；
	///   否则走墙钟（暂停冻结）。<see cref="SetSyncSource"/> 后以 master 的时钟为准，供左右两路同步播放。
	/// </summary>
	public sealed class MediaPlayback : IDisposable
	{
		private readonly object _sync = new object();

		private readonly byte[] _bytes;

		private readonly bool _enableVideo;

		private readonly bool _enableAudio;

		private readonly int _maxVideoWidth;

		private readonly int _maxVideoHeight;

		// 音频 / 视频各一路，解码器状态由各自的生产者线程独占（跨线程只通过 _sync 下的标志交互）。
		private AudioPlaybackSession _audio;

		private VideoPlaybackSession _video;

		private Thread _audioThread;

		private Thread _videoThread;

		private Thread _tickThread;

		private IntPtr _audioDevice;

		private bool _audioDeviceOpened;

		private volatile bool _audioOutputAvailable;

		private volatile string _audioOutputError;

		private bool _hasVideo;

		private bool _hasAudio;

		private double _durationSeconds;

		private volatile string _error;

		private volatile bool _disposed;

		private volatile bool _playing;

		private volatile bool _endReached;

		private float _volume = 1.0f;

		// 同步源：非 null 时本播放器的时钟与视频出帧节奏都跟它走。
		private volatile MediaPlayback _syncSource;

		// 时钟基准。音频主时钟 = _audioClockBase + 已播帧数 / 输出采样率；墙钟 = _wallBase + 秒表。
		private bool _audioClockActive;

		private int _audioOutRate;

		private int _audioOutChannels;

		private double _audioClockBase;

		private double _wallBase;

		private readonly Stopwatch _wallWatch = new Stopwatch();

		// 已解出的最大时间点，时长未知时用它作为收尾判据。
		private double _maxPtsObserved;

		// seek 请求：由 Seek 置位，生产者线程落实后清除；版本号防止「落实中被再次 seek」丢请求。
		private bool _audioSeekPending;

		private double _audioSeekTarget;

		private bool _audioSeekInProgress;

		private long _audioSeekVersion;

		private long _audioSeekInProgressVersion;

		private volatile bool _audioEof;

		private bool _videoSeekPending;

		private double _videoSeekTarget;

		private long _videoSeekVersion;

		private long _videoSeekInProgressVersion;

		private volatile bool _videoEof;

		// 生产者线程私有的「seek 后丢弃目标之前残留」的水位线。
		private double _audioDropBefore = double.NegativeInfinity;

		private double _videoDropBefore = double.NegativeInfinity;

		// 写设备前的暂存：会话缓冲会复用，写的时候还可能因背压分几次，故先拷出来。
		private float[] _audioStaging;

		private float[] _audioSlice;

		/// <summary>
		/// 打开媒体。<paramref name="bytes"/> 为整个媒体文件字节，两路共用、不复制；
		/// <paramref name="hardwareDecode"/> 为 true 时视频尝试硬解（失败静默回落软解）；
		/// <paramref name="maxVideoWidth"/> / <paramref name="maxVideoHeight"/> 为投递帧的最大尺寸（沿用
		/// <see cref="MediaConvert"/> 的「不放大」语义，传 0 表示该方向不限）。
		/// </summary>
		public MediaPlayback(byte[] bytes, bool enableVideo, bool enableAudio, bool hardwareDecode, int maxVideoWidth, int maxVideoHeight)
		{
			_bytes = bytes;
			_enableVideo = enableVideo;
			_enableAudio = enableAudio;
			_maxVideoWidth = maxVideoWidth;
			_maxVideoHeight = maxVideoHeight;

			if (bytes == null || bytes.Length == 0)
			{
				_error = "无媒体数据";
				return;
			}
			if (!enableVideo && !enableAudio)
			{
				_error = "未启用任何媒体流";
				return;
			}
			string bindingError;
			if (!MediaNative.EnsureInitialized(out bindingError))
			{
				_error = "FFmpeg 解码不可用：" + bindingError;
				return;
			}

			List<string> failures = new List<string>();
			if (enableAudio)
			{
				_audio = AudioPlaybackSession.Open(NewStream(), out MediaFailure audioFailure);
				if (_audio != null)
				{
					_hasAudio = true;
					_audioOutRate = _audio.OutputSampleRate;
					_audioOutChannels = _audio.OutputChannels;
				}
				else
				{
					failures.Add("音频：" + Detail(audioFailure));
				}
			}
			if (enableVideo)
			{
				_video = VideoPlaybackSession.Open(NewStream(), hardwareDecode, maxVideoWidth, maxVideoHeight, out MediaFailure videoFailure);
				if (_video != null)
				{
					_hasVideo = true;
				}
				else
				{
					failures.Add("视频：" + Detail(videoFailure));
				}
			}

			if (_audio == null && _video == null)
			{
				// 请求的每一路都打不开：这才是致命错误（设计 §11：出错降级为提示，绝不抛给宿主）。
				_error = "媒体无法打开（" + string.Join("；", failures) + "）";
				return;
			}

			double audioDuration = _audio != null ? _audio.DurationSeconds : 0.0;
			double videoDuration = _video != null ? _video.DurationSeconds : 0.0;
			_durationSeconds = Math.Max(audioDuration, videoDuration);

			_tickThread = StartThread("ForkPlus.Media.Tick", TickLoop);
			if (_audio != null)
			{
				_audioThread = StartThread("ForkPlus.Media.Audio", AudioLoop);
			}
			if (_video != null)
			{
				_videoThread = StartThread("ForkPlus.Media.Video", VideoLoop);
			}
		}

		/// <summary>是否存在可解码的视频流。</summary>
		public bool HasVideo => _hasVideo;

		/// <summary>是否存在可解码的音频流。</summary>
		public bool HasAudio => _hasAudio;

		/// <summary>媒体时长（秒）；拿不到为 0。</summary>
		public double DurationSeconds => _durationSeconds;

		/// <summary>是否正在播放。</summary>
		public bool IsPlaying => _playing;

		/// <summary>视频是否确实在用硬解（设备建不出或搬帧失败时回落软解，保持 false）。</summary>
		public bool IsHardwareActive => _video != null && _video.HardwareActive;

		/// <summary>音频输出设备是否可用（首次 <see cref="Play"/> 时打开设备，之前为 false）。</summary>
		public bool AudioOutputAvailable => _audioOutputAvailable;

		/// <summary>音频输出不可用的原因；可用为 null。此时静音播放、时钟走墙钟。</summary>
		public string AudioOutputError => _audioOutputError;

		/// <summary>
		/// 打不开媒体等致命错误（此时各事件不触发）。解码线程意外抛出也会在此留下原因。
		/// </summary>
		public string Error => _error;

		/// <summary>
		/// 播放头（秒）。有音频且输出可用时取音频主时钟（已播帧数 / 采样率 + 基准偏移），
		/// 否则取墙钟（暂停时冻结）。设了 <see cref="SetSyncSource"/> 时取 master 的时钟。
		/// </summary>
		public double ClockSeconds
		{
			get
			{
				MediaPlayback master = _syncSource;
				if (master != null)
				{
					return master.ClockSeconds;
				}
				lock (_sync)
				{
					if (_audioClockActive)
					{
						if (_audioSeekPending || _audioSeekInProgress)
						{
							// seek 尚未被生产者落实：先按目标时间汇报，避免时钟回跳一帧。
							return _audioSeekTarget;
						}
						ulong played = MiniAudioNative.PlayedFrames(_audioDevice);
						return _audioClockBase + played / (double)(_audioOutRate > 0 ? _audioOutRate : 48000);
					}
				}
				return _wallBase + _wallWatch.Elapsed.TotalSeconds;
			}
		}

		/// <summary>后台线程回调：一帧 BGRA + 该帧 pts（秒）。</summary>
		public event Action<ImageData, double> FrameReady;

		/// <summary>后台线程回调，约 10Hz。</summary>
		public event Action<double> PositionChanged;

		/// <summary>播到结尾（后台线程回调）。</summary>
		public event Action Ended;

		/// <summary>本进程可用的硬解设备类型清单（诊断用，不参与决策）。</summary>
		public static IReadOnlyList<string> AvailableHardwareDevices
		{
			get
			{
				string error;
				if (!MediaNative.EnsureInitialized(out error))
				{
					return Array.Empty<string>();
				}
				return MediaHwDecode.AvailableDeviceTypes();
			}
		}

		/// <summary>
		/// 让本播放器按 <paramref name="master"/> 的 <see cref="ClockSeconds"/> 出帧（双路同步）。
		/// 传 null 回到自己的时钟。
		/// </summary>
		public void SetSyncSource(MediaPlayback master)
		{
			lock (_sync)
			{
				_syncSource = master;
				Monitor.PulseAll(_sync);
			}
		}

		/// <summary>设置音量（0.0–1.0，越界截断）。</summary>
		public void SetVolume(float volume)
		{
			if (volume < 0.0f)
			{
				volume = 0.0f;
			}
			if (volume > 1.0f)
			{
				volume = 1.0f;
			}
			IntPtr device;
			lock (_sync)
			{
				_volume = volume;
				device = _audioDevice;
			}
			if (device != IntPtr.Zero)
			{
				MiniAudioNative.SetVolume(device, volume);
			}
		}

		/// <summary>开始 / 续播。已在结尾则先回到 0；首次调用时打开音频输出设备。</summary>
		public void Play()
		{
			if (_disposed || (_audio == null && _video == null))
			{
				return;
			}
			if (_endReached)
			{
				Seek(0.0);
			}
			EnsureAudioDevice();
			lock (_sync)
			{
				if (_disposed)
				{
					return;
				}
				if (!_audioClockActive)
				{
					// 墙钟：从当前位置起算（暂停时已把已走过的时间累进 _wallBase）。
					_wallWatch.Reset();
					_wallWatch.Start();
				}
				_playing = true;
				Monitor.PulseAll(_sync);
			}
			if (_audioDevice != IntPtr.Zero)
			{
				MiniAudioNative.Start(_audioDevice);
			}
		}

		/// <summary>暂停：冻结时钟但不清音频缓冲，再次 <see cref="Play"/> 从当前位置续播。</summary>
		public void Pause()
		{
			lock (_sync)
			{
				if (_disposed || !_playing)
				{
					return;
				}
				_playing = false;
				if (!_audioClockActive)
				{
					_wallBase += _wallWatch.Elapsed.TotalSeconds;
					_wallWatch.Reset();
				}
				Monitor.PulseAll(_sync);
			}
			if (_audioDevice != IntPtr.Zero)
			{
				MiniAudioNative.Stop(_audioDevice);
			}
		}

		/// <summary>
		/// 定位到 <paramref name="seconds"/>：两路都 seek（BACKWARD + flush），音频侧请生产者
		/// <c>fpp_audio_reset</c> 把已播帧数归零并丢掉目标时间之前的残留样本，时钟基准重置到目标秒。
		/// </summary>
		public void Seek(double seconds)
		{
			if (_disposed || (_audio == null && _video == null))
			{
				return;
			}
			double target = seconds;
			if (target < 0.0)
			{
				target = 0.0;
			}
			if (_durationSeconds > 0.0 && target > _durationSeconds)
			{
				target = _durationSeconds;
			}
			lock (_sync)
			{
				if (_disposed)
				{
					return;
				}
				_endReached = false;
				_audioEof = false;
				_videoEof = false;
				_maxPtsObserved = target;
				if (_audio != null)
				{
					_audioSeekTarget = target;
					_audioSeekVersion++;
					_audioSeekPending = true;
				}
				if (_video != null)
				{
					_videoSeekTarget = target;
					_videoSeekVersion++;
					_videoSeekPending = true;
				}
				_wallBase = target;
				_wallWatch.Reset();
				if (_playing)
				{
					_wallWatch.Start();
				}
				Monitor.PulseAll(_sync);
			}
		}

		/// <summary>停线程、关设备、释放两路所有 FFmpeg 资源。可重入且幂等。</summary>
		public void Dispose()
		{
			lock (_sync)
			{
				if (_disposed)
				{
					return;
				}
				_disposed = true;
				_playing = false;
				Monitor.PulseAll(_sync);
			}
			JoinThread(_audioThread);
			JoinThread(_videoThread);
			JoinThread(_tickThread);
			IntPtr device = _audioDevice;
			_audioDevice = IntPtr.Zero;
			if (device != IntPtr.Zero)
			{
				MiniAudioNative.Stop(device);
				MiniAudioNative.Close(device);
			}
			_audio?.Dispose();
			_video?.Dispose();
			_audio = null;
			_video = null;
		}

		private MemoryStream NewStream()
		{
			// 两路各自持有一个 MemoryStream（各自独立的位置），共用底层字节数组，不复制。
			return new MemoryStream(_bytes, 0, _bytes.Length, false);
		}

		private static string Detail(MediaFailure failure)
		{
			return failure != null ? failure.Detail : "未知原因";
		}

		private static Thread StartThread(string name, ThreadStart body)
		{
			Thread thread = new Thread(body)
			{
				Name = name,
				IsBackground = true
			};
			thread.Start();
			return thread;
		}

		private static void JoinThread(Thread thread)
		{
			if (thread == null)
			{
				return;
			}
			try
			{
				// 生产者每轮都查 _disposed 且最长只睡 100ms，1s 足够退出；超时也不阻塞宿主收尾。
				thread.Join(1000);
			}
			catch
			{
				// Join 失败（例如自线程调用）不致命，交给 GC。
			}
		}

		/// <summary>首次 Play 时打开音频输出设备；失败只记 <see cref="AudioOutputError"/>，不影响视频与时钟。</summary>
		private void EnsureAudioDevice()
		{
			if (_audio == null)
			{
				return;
			}
			lock (_sync)
			{
				if (_audioDeviceOpened)
				{
					return;
				}
				_audioDeviceOpened = true;
			}
			string bindingError;
			if (!MiniAudioNative.EnsureInitialized(out bindingError))
			{
				_audioOutputError = bindingError;
				return;
			}
			IntPtr handle = MiniAudioNative.Open((uint)_audio.OutputSampleRate, (uint)_audio.OutputChannels);
			if (handle == IntPtr.Zero)
			{
				_audioOutputError = MiniAudioNative.LastError() ?? "音频输出设备打开失败";
				return;
			}
			float volume;
			lock (_sync)
			{
				_audioDevice = handle;
				_audioOutputAvailable = true;
				_audioClockActive = true;
				volume = _volume;
			}
			MiniAudioNative.SetVolume(handle, volume);
		}

		private bool WaitUntilPlaying()
		{
			lock (_sync)
			{
				while (!_disposed && !_playing)
				{
					Monitor.Wait(_sync);
				}
				return !_disposed;
			}
		}

		/// <summary>可被 Pulse 唤醒的小睡（Dispose / Play / Pause / Seek 都会唤醒）。</summary>
		private void Sleep(int milliseconds)
		{
			lock (_sync)
			{
				if (!_disposed)
				{
					Monitor.Wait(_sync, milliseconds);
				}
			}
		}

		private void RecordStreamError(Exception ex, string what)
		{
			string message = what + "失败：" + ex.GetType().Name + ": " + ex.Message;
			lock (_sync)
			{
				if (_error == null)
				{
					_error = message;
				}
			}
		}

		/// <summary>音频生产者：背压写入设备；seek 时先清缓冲归零再丢目标前的残留样本。</summary>
		private void AudioLoop()
		{
			while (!_disposed)
			{
				if (!WaitUntilPlaying())
				{
					return;
				}

				if (TryBeginAudioSeek(out double seekTarget))
				{
					try
					{
						if (_audioDevice != IntPtr.Zero)
						{
							MiniAudioNative.Reset(_audioDevice);
						}
						_audio.Seek(seekTarget);
						_audioDropBefore = seekTarget;
					}
					catch (Exception ex)
					{
						RecordStreamError(ex, "音频 seek");
					}
					lock (_sync)
					{
						_audioClockBase = seekTarget;
						_audioEof = false;
						_audioSeekInProgress = false;
						// 落实期间若又被 seek，则保留新的 pending，下一轮继续处理。
						if (_audioSeekVersion == _audioSeekInProgressVersion)
						{
							_audioSeekPending = false;
						}
					}
					continue;
				}

				if (_audioDevice == IntPtr.Zero || _audioEof)
				{
					// 设备不可用（静音播放）或已到流尾：不做解码，靠墙钟推进；睡一下让出 CPU。
					Sleep(_audioDevice == IntPtr.Zero ? 50 : 20);
					continue;
				}

				float[] buffer;
				int frames;
				double seconds;
				double duration;
				try
				{
					if (!_audio.TryDecodeNext(out buffer, out frames, out seconds, out duration))
					{
						_audioEof = true;
						continue;
					}
				}
				catch (Exception ex)
				{
					_audioEof = true;
					RecordStreamError(ex, "音频解码");
					continue;
				}

				if (seconds + duration <= _audioDropBefore + 1e-6)
				{
					// 目标时间之前的样本：丢弃，避免内容与时间轴对不上。
					continue;
				}
				lock (_sync)
				{
					double end = seconds + duration;
					if (end > _maxPtsObserved)
					{
						_maxPtsObserved = end;
					}
				}
				WriteToDevice(buffer, frames);
			}
		}

		/// <summary>按剩余可写帧数背压写入；缓冲满时小睡让出 CPU；暂停 / seek / 释放时尽快退出。</summary>
		private void WriteToDevice(float[] buffer, int frameCount)
		{
			int channels = _audioOutChannels;
			int total = frameCount * channels;
			if (_audioStaging == null || _audioStaging.Length < total)
			{
				_audioStaging = new float[total];
			}
			Array.Copy(buffer, 0, _audioStaging, 0, total);

			int doneFrames = 0;
			while (doneFrames < frameCount)
			{
				lock (_sync)
				{
					if (_disposed || !_playing)
					{
						return;
					}
				}
				if (IsAudioSeekPending())
				{
					// 让生产者先处理 seek，本次剩余样本丢弃（重置后本来就要丢）。
					return;
				}
				uint free = MiniAudioNative.FreeFrames(_audioDevice);
				if (free == 0u)
				{
					Sleep(2);
					continue;
				}
				int chunk = (int)Math.Min((long)free, (long)(frameCount - doneFrames));
				int offsetSamples = doneFrames * channels;
				float[] slice;
				if (offsetSamples == 0 && chunk == frameCount)
				{
					slice = _audioStaging;
				}
				else
				{
					int needed = chunk * channels;
					if (_audioSlice == null || _audioSlice.Length < needed)
					{
						_audioSlice = new float[needed];
					}
					Array.Copy(_audioStaging, offsetSamples, _audioSlice, 0, needed);
					slice = _audioSlice;
				}
				uint written = MiniAudioNative.Write(_audioDevice, slice, (uint)chunk);
				if (written == 0u)
				{
					Sleep(1);
					continue;
				}
				doneFrames += (int)written;
			}
		}

		/// <summary>视频生产者：顺序解码 → 等帧该出现的时间 → 投递；seek 时丢掉目标前的残留帧。</summary>
		private void VideoLoop()
		{
			while (!_disposed)
			{
				if (!WaitUntilPlaying())
				{
					return;
				}

				if (TryBeginVideoSeek(out double seekTarget))
				{
					try
					{
						_video.Seek(seekTarget);
						_videoDropBefore = seekTarget;
					}
					catch (Exception ex)
					{
						RecordStreamError(ex, "视频 seek");
					}
					lock (_sync)
					{
						_videoEof = false;
						if (_videoSeekVersion == _videoSeekInProgressVersion)
						{
							_videoSeekPending = false;
						}
					}
					continue;
				}

				if (_videoEof)
				{
					Sleep(20);
					continue;
				}

				ImageData image;
				double seconds;
				double duration;
				try
				{
					if (!_video.TryDecodeNext(out image, out seconds, out duration))
					{
						_videoEof = true;
						continue;
					}
				}
				catch (Exception ex)
				{
					_videoEof = true;
					RecordStreamError(ex, "视频解码");
					continue;
				}

				if (seconds + duration <= _videoDropBefore + 1e-6)
				{
					continue;
				}
				lock (_sync)
				{
					double end = seconds + duration;
					if (end > _maxPtsObserved)
					{
						_maxPtsObserved = end;
					}
				}

				if (!WaitForFrameTime(seconds))
				{
					continue;
				}

				Action<ImageData, double> handler = FrameReady;
				if (handler != null)
				{
					try
					{
						handler(image, seconds);
					}
					catch
					{
						// 订阅方异常不得拖垮解码线程。
					}
				}
			}
		}

		/// <summary>等到该帧应当出现的时间再返回 true；暂停则阻塞、释放返回 false。</summary>
		private bool WaitForFrameTime(double seconds)
		{
			while (!_disposed)
			{
				if (!_playing && !WaitUntilPlaying())
				{
					return false;
				}
				if (IsVideoSeekPending())
				{
					return false;
				}
				double clock = ClockSeconds;
				if (clock >= seconds - 1e-6)
				{
					return true;
				}
				double remaining = seconds - clock;
				int milliseconds = (int)(remaining * 1000.0);
				if (milliseconds < 1)
				{
					milliseconds = 1;
				}
				if (milliseconds > 10)
				{
					milliseconds = 10;
				}
				Sleep(milliseconds);
			}
			return false;
		}

		/// <summary>10Hz 节拍：投递位置，并做收尾判定。</summary>
		private void TickLoop()
		{
			while (true)
			{
				lock (_sync)
				{
					if (_disposed)
					{
						return;
					}
				}
				if (_playing)
				{
					double position = ClockSeconds;
					Action<double> handler = PositionChanged;
					if (handler != null)
					{
						try
						{
							handler(position);
						}
						catch
						{
							// 订阅方异常不得拖垮节拍线程。
						}
					}
					CheckEnd(position);
				}
				Sleep(100);
			}
		}

		private void CheckEnd(double position)
		{
			if (_endReached)
			{
				return;
			}
			bool reached;
			if (_durationSeconds > 0.0)
			{
				reached = position >= _durationSeconds - 1e-3;
			}
			else
			{
				reached = ProducersFinished() && position >= _maxPtsObserved - 1e-3;
			}
			if (!reached)
			{
				return;
			}
			_endReached = true;
			StopAxisPlayback();
			Action handler = Ended;
			if (handler != null)
			{
				try
				{
					handler();
				}
				catch
				{
					// 订阅方异常不得拖垮节拍线程。
				}
			}
		}

		private bool ProducersFinished()
		{
			bool audioDone = !_hasAudio || _audioEof || _audioDevice == IntPtr.Zero;
			bool videoDone = !_hasVideo || _videoEof;
			return audioDone && videoDone;
		}

		/// <summary>到达结尾：冻结状态并停音频设备（不清缓冲，便于再次 Play 时先 Seek(0)）。</summary>
		private void StopAxisPlayback()
		{
			lock (_sync)
			{
				_playing = false;
				if (!_audioClockActive)
				{
					_wallBase += _wallWatch.Elapsed.TotalSeconds;
					_wallWatch.Reset();
				}
				Monitor.PulseAll(_sync);
			}
			if (_audioDevice != IntPtr.Zero)
			{
				MiniAudioNative.Stop(_audioDevice);
			}
		}

		private bool TryBeginAudioSeek(out double target)
		{
			lock (_sync)
			{
				if (!_audioSeekPending)
				{
					target = 0.0;
					return false;
				}
				target = _audioSeekTarget;
				_audioSeekInProgress = true;
				_audioSeekInProgressVersion = _audioSeekVersion;
				return true;
			}
		}

		private bool TryBeginVideoSeek(out double target)
		{
			lock (_sync)
			{
				if (!_videoSeekPending)
				{
					target = 0.0;
					return false;
				}
				target = _videoSeekTarget;
				_videoSeekInProgressVersion = _videoSeekVersion;
				return true;
			}
		}

		private bool IsAudioSeekPending()
		{
			lock (_sync)
			{
				return _audioSeekPending;
			}
		}

		private bool IsVideoSeekPending()
		{
			lock (_sync)
			{
				return _videoSeekPending;
			}
		}

		/// <summary>音频一路的流式解码：独立 avio / AVFormatContext + swr 转交错 f32。</summary>
		private sealed unsafe class AudioPlaybackSession : IDisposable
		{
			private MemoryAvioContext _avio;

			private AVFormatContext* _format;

			private AVStream* _stream;

			private AVCodecContext* _codec;

			private AVPacket* _packet;

			private AVFrame* _frame;

			private SwrContext* _swr;

			private float[] _converted;

			private bool _draining;

			private bool _disposed;

			// 帧 pts 缺失时用的兜底时间轴。
			private double _fallbackSeconds;

			/// <summary>输出采样率（源采样率；拿不到时 48000）。</summary>
			public int OutputSampleRate;

			/// <summary>输出声道数（源声道数；&gt;2 降混到 2，&lt;1 按 1）。</summary>
			public int OutputChannels;

			public double DurationSeconds;

			public static AudioPlaybackSession Open(MemoryStream stream, out MediaFailure failure)
			{
				failure = null;
				AudioPlaybackSession session = new AudioPlaybackSession();
				try
				{
					stream.Position = 0L;
					session._avio = new MemoryAvioContext(stream);
					string error;
					session._format = session._avio.Open(out error);
					if (session._format == null)
					{
						failure = new MediaFailure(MediaProbe.Classify(error), error);
						session.Dispose();
						return null;
					}
					int index = ffmpeg.av_find_best_stream(session._format, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
					if (index < 0)
					{
						failure = new MediaFailure(MediaErrorKind.Unsupported, "no audio stream");
						session.Dispose();
						return null;
					}
					session._stream = session._format->streams[index];
					AVCodecParameters* parameters = session._stream->codecpar;
					AVCodec* codec = ffmpeg.avcodec_find_decoder(parameters->codec_id);
					if (codec == null)
					{
						failure = new MediaFailure(MediaErrorKind.Unsupported, "no decoder for " + ffmpeg.avcodec_get_name(parameters->codec_id));
						session.Dispose();
						return null;
					}
					session._codec = ffmpeg.avcodec_alloc_context3(codec);
					if (session._codec == null
						|| ffmpeg.avcodec_parameters_to_context(session._codec, parameters) < 0
						|| ffmpeg.avcodec_open2(session._codec, codec, null) < 0)
					{
						failure = new MediaFailure(MediaErrorKind.Failed, "avcodec_open2 failed");
						session.Dispose();
						return null;
					}

					int sourceRate = session._codec->sample_rate > 0 ? session._codec->sample_rate : 0;
					int outputRate = sourceRate > 0 ? sourceRate : 48000;
					session.OutputSampleRate = outputRate;

					int sourceChannels = session._codec->ch_layout.nb_channels;
					if (sourceChannels <= 0)
					{
						sourceChannels = parameters->ch_layout.nb_channels;
					}
					int outputChannels = sourceChannels < 1 ? 1 : (sourceChannels > 2 ? 2 : sourceChannels);
					session.OutputChannels = outputChannels;

					AVChannelLayout inputLayout = session._codec->ch_layout;
					if (inputLayout.nb_channels <= 0)
					{
						ffmpeg.av_channel_layout_default(&inputLayout, sourceChannels < 1 ? outputChannels : sourceChannels);
					}
					AVChannelLayout outputLayout;
					ffmpeg.av_channel_layout_default(&outputLayout, outputChannels);
					int inputRate = sourceRate > 0 ? sourceRate : outputRate;
					// swr 指针先落在局部变量上：类字段取地址需 fixed，局部则不必。
					SwrContext* swr = null;
					if (ffmpeg.swr_alloc_set_opts2(&swr, &outputLayout, AVSampleFormat.AV_SAMPLE_FMT_FLT, outputRate, &inputLayout, session._codec->sample_fmt, inputRate, 0, null) < 0 || swr == null || ffmpeg.swr_init(swr) < 0)
					{
						if (swr != null)
						{
							SwrContext* failed = swr;
							ffmpeg.swr_free(&failed);
						}
						failure = new MediaFailure(MediaErrorKind.Failed, "swr_init failed");
						session.Dispose();
						return null;
					}
					session._swr = swr;

					session._packet = ffmpeg.av_packet_alloc();
					session._frame = ffmpeg.av_frame_alloc();
					if (session._packet == null || session._frame == null)
					{
						failure = new MediaFailure(MediaErrorKind.Failed, "av_packet_alloc / av_frame_alloc failed");
						session.Dispose();
						return null;
					}
					if (session._stream->duration != ffmpeg.AV_NOPTS_VALUE && session._stream->duration > 0)
					{
						session.DurationSeconds = session._stream->duration * ffmpeg.av_q2d(session._stream->time_base);
					}
					else if (session._format->duration != ffmpeg.AV_NOPTS_VALUE && session._format->duration > 0)
					{
						session.DurationSeconds = session._format->duration / (double)ffmpeg.AV_TIME_BASE;
					}
					return session;
				}
				catch (Exception ex)
				{
					failure = new MediaFailure(MediaErrorKind.Failed, ex.GetType().Name + ": " + ex.Message);
					session.Dispose();
					return null;
				}
			}

			/// <summary>
			/// 解出下一块交错 f32（<paramref name="frameCount"/> 为每声道帧数）。到流尾或出错返回 false。
			/// 返回的缓冲由本会话复用，调用方须在下次调用前消费完。
			/// </summary>
			public bool TryDecodeNext(out float[] samples, out int frameCount, out double seconds, out double duration)
			{
				samples = null;
				frameCount = 0;
				seconds = 0.0;
				duration = 0.0;
				double timeBase = ffmpeg.av_q2d(_stream->time_base);
				if (timeBase <= 0.0)
				{
					timeBase = 1.0 / (OutputSampleRate > 0 ? OutputSampleRate : 48000);
				}
				while (true)
				{
					int receive = ffmpeg.avcodec_receive_frame(_codec, _frame);
					if (receive >= 0)
					{
						long pts = _frame->pts;
						if (pts == ffmpeg.AV_NOPTS_VALUE)
						{
							pts = _frame->pkt_dts;
						}
						double start = pts == ffmpeg.AV_NOPTS_VALUE ? _fallbackSeconds : pts * timeBase;
						if (start < 0.0)
						{
							start = 0.0;
						}
						int inputSamples = _frame->nb_samples;
						int capacity = ffmpeg.swr_get_out_samples(_swr, inputSamples);
						if (capacity <= 0)
						{
							capacity = inputSamples + 1024;
						}
						int total = capacity * OutputChannels;
						if (_converted == null || _converted.Length < total)
						{
							_converted = new float[total];
						}
						int got;
						byte*[] destination = new byte*[1];
						fixed (float* converted = _converted)
						fixed (byte** output = destination)
						{
							destination[0] = (byte*)converted;
							got = ffmpeg.swr_convert(_swr, output, capacity, _frame->extended_data, inputSamples);
						}
						ffmpeg.av_frame_unref(_frame);
						if (got <= 0)
						{
							_fallbackSeconds = start;
							continue;
						}
						samples = _converted;
						frameCount = got;
						seconds = start;
						duration = got / (double)OutputSampleRate;
						_fallbackSeconds = seconds + duration;
						return true;
					}
					if (receive == ffmpeg.AVERROR_EOF)
					{
						return false;
					}
					if (_draining)
					{
						return false;
					}
					// 其余负值都当作「还需要更多输入」处理：读包 → 送解码器；读到 EOF 就送 null 冲尾。
					int read = ffmpeg.av_read_frame(_format, _packet);
					if (read < 0)
					{
						_draining = true;
						ffmpeg.avcodec_send_packet(_codec, null);
						continue;
					}
					if (_packet->stream_index == _stream->index)
					{
						ffmpeg.avcodec_send_packet(_codec, _packet);
					}
					ffmpeg.av_packet_unref(_packet);
				}
			}

			/// <summary>定位（BACKWARD + flush），并清掉重采样器里跨 seek 的残留样本。</summary>
			public void Seek(double seconds)
			{
				if (seconds < 0.0)
				{
					seconds = 0.0;
				}
				double timeBase = ffmpeg.av_q2d(_stream->time_base);
				if (timeBase <= 0.0)
				{
					timeBase = 1.0 / (OutputSampleRate > 0 ? OutputSampleRate : 48000);
				}
				long timestamp = (long)Math.Round(seconds / timeBase);
				ffmpeg.av_seek_frame(_format, _stream->index, timestamp, ffmpeg.AVSEEK_FLAG_BACKWARD);
				ffmpeg.avcodec_flush_buffers(_codec);
				if (_swr != null)
				{
					// 输出传 null：把内部缓冲里的样本直接丢弃，避免 seek 后混入旧样本。
					ffmpeg.swr_convert(_swr, null, 0, null, 0);
				}
				_draining = false;
				_fallbackSeconds = seconds;
			}

			public void Dispose()
			{
				if (_disposed)
				{
					return;
				}
				_disposed = true;
				if (_swr != null)
				{
					SwrContext* s = _swr;
					ffmpeg.swr_free(&s);
					_swr = null;
				}
				if (_packet != null)
				{
					AVPacket* p = _packet;
					ffmpeg.av_packet_free(&p);
					_packet = null;
				}
				if (_frame != null)
				{
					AVFrame* f = _frame;
					ffmpeg.av_frame_free(&f);
					_frame = null;
				}
				if (_codec != null)
				{
					AVCodecContext* c = _codec;
					ffmpeg.avcodec_free_context(&c);
					_codec = null;
				}
				_stream = null;
				_avio?.Dispose();
				_avio = null;
				_format = null;
				_converted = null;
			}
		}

		/// <summary>视频一路的流式解码：独立 avio / AVFormatContext，可选硬解 + 搬帧回系统内存。</summary>
		private sealed unsafe class VideoPlaybackSession : IDisposable
		{
			private readonly int _maxWidth;

			private readonly int _maxHeight;

			private MemoryAvioContext _avio;

			private AVFormatContext* _format;

			private AVStream* _stream;

			private AVCodecContext* _codec;

			private AVPacket* _packet;

			private AVFrame* _frame;

			private AVFrame* _software;

			private AVBufferRef* _device;

			private bool _draining;

			private bool _disposed;

			private double _fallbackSeconds;

			private double _frameDuration = 0.04;

			public double DurationSeconds;

			/// <summary>是否真的用上了硬解（搬帧成功才置 true）。</summary>
			public bool HardwareActive;

			private VideoPlaybackSession(int maxWidth, int maxHeight)
			{
				_maxWidth = maxWidth;
				_maxHeight = maxHeight;
			}

			public static VideoPlaybackSession Open(MemoryStream stream, bool hardwareDecode, int maxWidth, int maxHeight, out MediaFailure failure)
			{
				failure = null;
				VideoPlaybackSession session = new VideoPlaybackSession(maxWidth, maxHeight);
				try
				{
					stream.Position = 0L;
					session._avio = new MemoryAvioContext(stream);
					string error;
					session._format = session._avio.Open(out error);
					if (session._format == null)
					{
						failure = new MediaFailure(MediaProbe.Classify(error), error);
						session.Dispose();
						return null;
					}
					int index = ffmpeg.av_find_best_stream(session._format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
					if (index < 0)
					{
						failure = new MediaFailure(MediaErrorKind.Unsupported, "no video stream");
						session.Dispose();
						return null;
					}
					session._stream = session._format->streams[index];
					AVCodecParameters* parameters = session._stream->codecpar;
					AVCodec* codec = ffmpeg.avcodec_find_decoder(parameters->codec_id);
					if (codec == null)
					{
						failure = new MediaFailure(MediaErrorKind.Unsupported, "no decoder for " + ffmpeg.avcodec_get_name(parameters->codec_id));
						session.Dispose();
						return null;
					}
					session._codec = ffmpeg.avcodec_alloc_context3(codec);
					if (session._codec == null || ffmpeg.avcodec_parameters_to_context(session._codec, parameters) < 0)
					{
						failure = new MediaFailure(MediaErrorKind.Failed, "avcodec_parameters_to_context failed");
						session.Dispose();
						return null;
					}

					if (hardwareDecode)
					{
						// 设备必须在 open 之前挂上；建不出设备就静默回落软解（设计 §12）。
						string deviceError;
						AVBufferRef* device = MediaHwDecode.CreateDevice(MediaHwDecode.PreferredDeviceType(), out deviceError);
						if (device != null)
						{
							session._software = ffmpeg.av_frame_alloc();
							if (session._software != null)
							{
								MediaHwDecode.Attach(session._codec, device);
								session._device = device;
							}
							else
							{
								AVBufferRef* released = device;
								ffmpeg.av_buffer_unref(&released);
							}
						}
					}

					if (ffmpeg.avcodec_open2(session._codec, codec, null) < 0)
					{
						failure = new MediaFailure(MediaErrorKind.Failed, "avcodec_open2 failed");
						session.Dispose();
						return null;
					}
					session._packet = ffmpeg.av_packet_alloc();
					session._frame = ffmpeg.av_frame_alloc();
					if (session._packet == null || session._frame == null)
					{
						failure = new MediaFailure(MediaErrorKind.Failed, "av_packet_alloc / av_frame_alloc failed");
						session.Dispose();
						return null;
					}

					AVRational rate = session._stream->avg_frame_rate;
					double fps = rate.num > 0 && rate.den > 0 ? (double)rate.num / rate.den : 0.0;
					if (fps <= 0.0)
					{
						AVRational fallbackRate = session._stream->r_frame_rate;
						fps = fallbackRate.num > 0 && fallbackRate.den > 0 ? (double)fallbackRate.num / fallbackRate.den : 0.0;
					}
					session._frameDuration = fps > 0.0 ? 1.0 / fps : 0.04;

					if (session._stream->duration != ffmpeg.AV_NOPTS_VALUE && session._stream->duration > 0)
					{
						session.DurationSeconds = session._stream->duration * ffmpeg.av_q2d(session._stream->time_base);
					}
					else if (session._format->duration != ffmpeg.AV_NOPTS_VALUE && session._format->duration > 0)
					{
						session.DurationSeconds = session._format->duration / (double)ffmpeg.AV_TIME_BASE;
					}
					return session;
				}
				catch (Exception ex)
				{
					failure = new MediaFailure(MediaErrorKind.Failed, ex.GetType().Name + ": " + ex.Message);
					session.Dispose();
					return null;
				}
			}

			/// <summary>
			/// 解出下一帧 BGRA 及 pts（秒）；硬解帧先搬到预分配的软件帧再交给
			/// <see cref="MediaConvert.FrameToImage"/>。到流尾或出错返回 false。
			/// </summary>
			public bool TryDecodeNext(out ImageData image, out double seconds, out double duration)
			{
				image = null;
				seconds = 0.0;
				duration = 0.0;
				double timeBase = ffmpeg.av_q2d(_stream->time_base);
				if (timeBase <= 0.0)
				{
					timeBase = 1.0 / 1000.0;
				}
				while (true)
				{
					int receive = ffmpeg.avcodec_receive_frame(_codec, _frame);
					if (receive >= 0)
					{
						long pts = _frame->pts;
						if (pts == ffmpeg.AV_NOPTS_VALUE)
						{
							pts = _frame->pkt_dts;
						}
						double start = pts == ffmpeg.AV_NOPTS_VALUE ? _fallbackSeconds : pts * timeBase;
						if (start < 0.0)
						{
							start = 0.0;
						}

						AVFrame* source = _frame;
						if (_device != null && MediaHwDecode.IsHardwareFrame(_frame))
						{
							if (MediaHwDecode.TryTransferToSoftware(_frame, _software))
							{
								source = _software;
								HardwareActive = true;
							}
						}
						ImageData converted = MediaConvert.FrameToImage(source, _maxWidth, _maxHeight);
						ffmpeg.av_frame_unref(_frame);
						_fallbackSeconds = start + _frameDuration;
						if (converted == null)
						{
							continue;
						}
						image = converted;
						seconds = start;
						duration = _frameDuration;
						return true;
					}
					if (receive == ffmpeg.AVERROR_EOF)
					{
						return false;
					}
					if (_draining)
					{
						return false;
					}
					int read = ffmpeg.av_read_frame(_format, _packet);
					if (read < 0)
					{
						_draining = true;
						ffmpeg.avcodec_send_packet(_codec, null);
						continue;
					}
					if (_packet->stream_index == _stream->index)
					{
						ffmpeg.avcodec_send_packet(_codec, _packet);
					}
					ffmpeg.av_packet_unref(_packet);
				}
			}

			/// <summary>定位（BACKWARD + flush），与单帧取帧同一策略。</summary>
			public void Seek(double seconds)
			{
				if (seconds < 0.0)
				{
					seconds = 0.0;
				}
				double timeBase = ffmpeg.av_q2d(_stream->time_base);
				if (timeBase <= 0.0)
				{
					timeBase = 1.0 / 1000.0;
				}
				long timestamp = (long)Math.Round(seconds / timeBase);
				ffmpeg.av_seek_frame(_format, _stream->index, timestamp, ffmpeg.AVSEEK_FLAG_BACKWARD);
				ffmpeg.avcodec_flush_buffers(_codec);
				_draining = false;
				_fallbackSeconds = seconds;
			}

			public void Dispose()
			{
				if (_disposed)
				{
					return;
				}
				_disposed = true;
				if (_software != null)
				{
					AVFrame* f = _software;
					ffmpeg.av_frame_free(&f);
					_software = null;
				}
				if (_frame != null)
				{
					AVFrame* f = _frame;
					ffmpeg.av_frame_free(&f);
					_frame = null;
				}
				if (_packet != null)
				{
					AVPacket* p = _packet;
					ffmpeg.av_packet_free(&p);
					_packet = null;
				}
				if (_codec != null)
				{
					AVCodecContext* c = _codec;
					ffmpeg.avcodec_free_context(&c);
					_codec = null;
				}
				if (_device != null)
				{
					AVBufferRef* d = _device;
					ffmpeg.av_buffer_unref(&d);
					_device = null;
				}
				_stream = null;
				_avio?.Dispose();
				_avio = null;
				_format = null;
			}
		}
	}
}
