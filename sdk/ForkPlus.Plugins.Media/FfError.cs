using System;
using System.Text;
using FFmpeg.AutoGen;

namespace ForkPlus.Plugins.Media
{
	/// <summary>FFmpeg 错误码转可读文本。</summary>
	internal static unsafe class FfError
	{
		internal static string Describe(int code)
		{
			try
			{
				byte* buffer = stackalloc byte[256];
				int result = ffmpeg.av_strerror(code, buffer, 256);
				if (result < 0)
				{
					return code.ToString();
				}
				return "[" + code + "] " + PtrToString(buffer, 256);
			}
			catch
			{
				return code.ToString();
			}
		}

		internal unsafe static string PtrToString(byte* value)
		{
			return value == null ? null : PtrToString(value, int.MaxValue);
		}

		internal unsafe static string PtrToString(byte* value, int maxLength)
		{
			if (value == null)
			{
				return null;
			}
			int length = 0;
			while (length < maxLength && value[length] != 0)
			{
				length++;
			}
			return length == 0 ? string.Empty : Encoding.UTF8.GetString(value, length);
		}
	}
}
