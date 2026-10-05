using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Docnet.Core;

namespace ForkPlus.Plugins.Pdf
{
	/// <summary>
	/// PDFium 原生库解析器。
	///
	/// 宿主的程序集解析钩子只覆盖 plugins/ 目录下的**托管** DLL；Docnet.Core 经
	/// <c>[DllImport("pdfium")]</c> 触发的原生库查找默认不会探测插件目录。这里给
	/// Docnet.Core 注册一个 DllImportResolver，从插件自身目录（含
	/// <c>runtimes/&lt;rid&gt;/native/</c>）递归查找 <c>pdfium.*</c> / <c>libpdfium.*</c> 并加载。
	/// </summary>
	internal static class PdfNativeLibrary
	{
		private static readonly object SyncRoot = new object();

		private static bool _registered;

		/// <summary>注册解析器（幂等）。失败只记日志，交由默认探测兜底。</summary>
		public static void EnsureRegistered()
		{
			lock (SyncRoot)
			{
				if (_registered)
				{
					return;
				}
				_registered = true;
				try
				{
					Assembly docnet = typeof(DocLib).Assembly;
					NativeLibrary.SetDllImportResolver(docnet, Resolve);
				}
				catch (Exception ex)
				{
					PluginLog.Warn("Pdf: failed to register PDFium DllImportResolver", ex);
				}
			}
		}

		private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPaths)
		{
			try
			{
				string root = Path.GetDirectoryName(typeof(PdfDiffPlugin).Assembly.Location);
				if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
				{
					return IntPtr.Zero;
				}
				foreach (string pattern in new string[2] { "pdfium.*", "libpdfium.*" })
				{
					foreach (string file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
					{
						if (NativeLibrary.TryLoad(file, out IntPtr handle))
						{
							return handle;
						}
					}
				}
			}
			catch (Exception ex)
			{
				PluginLog.Warn("Pdf: failed to resolve native library '" + libraryName + "'", ex);
			}
			return IntPtr.Zero;
		}
	}
}