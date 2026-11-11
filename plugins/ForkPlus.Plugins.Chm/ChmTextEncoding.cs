using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ForkPlus.Plugins.Chm
{
	/// <summary>
	/// CHM 文本解码的编码工具：注册 CodePages 提供程序 + 提供「严格 UTF-8 → ANSI 代码页」回退解码。
	///
	/// CHM 的目录条目名（PMGL）与 <c>#SYSTEM</c> 文本按**编译机 ANSI 代码页**存储（中文 CHM 即
	/// GBK/CP936，规范未提供编码声明），HTML 页面也常声明 <c>charset=gb2312/gbk</c>。而 .NET
	/// 默认编码集不含这些代码页——此前一律按 UTF-8 解码，中文字符全部变成替换符（乱码）。
	///
	/// 策略与 7-Zip / chmlib 系工具一致：
	/// <list type="bullet">
	/// <item>先注册 <c>CodePagesEncodingProvider</c>，让 <c>Encoding.GetEncoding("gb2312")</c>
	/// 等按名解析得以成功（<see cref="TryGetEncoding"/>）；</item>
	/// <item>无声明文本按「严格 UTF-8 失败 → 系统 ANSI 代码页」解码（<see cref="Decode"/>）——
	/// ASCII 两种口径结果一致，纯 ASCII CHM 行为不变；Windows 取 <c>GetACP()</c>（编译机
	/// 代码页的常见近似：用户系统区域设置），非 Windows 回退当前文化的 ANSICodePage。</item>
	/// </list>
	/// </summary>
	internal static class ChmTextEncoding
	{
		/// <summary>严格 UTF-8：非法字节抛 <see cref="DecoderFallbackException"/>，用于「先按 UTF-8 试、失败回退 ANSI」。</summary>
		internal static readonly Encoding Utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

		/// <summary>系统 ANSI 代码页（注册提供程序后解析；解析失败回退 UTF-8，行为与修复前一致）。</summary>
		private static readonly Encoding Ansi = ResolveAnsi();

		[DllImport("kernel32", SetLastError = false)]
		private static extern int GetACP();

		private static Encoding ResolveAnsi()
		{
			// 必须先注册 CodePages 提供程序再 GetEncoding：GBK/Big5/Shift-JIS 等都不在默认编码集里。
			// 放在 ResolveAnsi 里（而不是单独的静态构造器）——静态字段初始化先于静态构造器执行，
			// 注册动作必须赶在第一次 GetEncoding 之前。
			try
			{
				Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			}
			catch (Exception)
			{
			}
			try
			{
				int codePage = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
					? GetACP()
					: CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
				if (codePage > 0 && codePage != 65001)
				{
					return Encoding.GetEncoding(codePage);
				}
			}
			catch (Exception)
			{
			}
			return Encoding.UTF8;
		}

		/// <summary>按「严格 UTF-8 → 系统 ANSI 代码页」解码字节。
		/// 纯 ASCII 两种口径结果一致；合法 UTF-8（含中文）优先按 UTF-8；GBK 等非 UTF-8
		/// 字节序列严格解码失败后按 ANSI 落地，中文 CHM 的路径 / 标题 / 正文由此恢复。</summary>
		internal static string Decode(byte[] bytes, int index, int count)
		{
			try
			{
				return Utf8Strict.GetString(bytes, index, count);
			}
			catch (DecoderFallbackException)
			{
				return Ansi.GetString(bytes, index, count);
			}
		}

		/// <summary>按名解析编码（如 HTML 声明的 gb2312 / gbk / big5 / shift_jis / windows-1252）。
		/// CodePages 提供程序已注册；不认识的编码名返回 null（调用方回退探测逻辑）。</summary>
		internal static Encoding TryGetEncoding(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}
			try
			{
				return Encoding.GetEncoding(name);
			}
			catch (Exception)
			{
				return null;
			}
		}
	}
}
