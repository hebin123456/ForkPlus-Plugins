using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.Xz;
using SharpCompress.Compressors.ZStandard;
using SharpCompress.Readers;
using SC = SharpCompress.Compressors;

namespace ForkPlus.Plugins.Archive
{
	/// <summary>
	/// 用 SharpCompress（MIT）把压缩包「列表化」成条目树——只读条目标（路径 / 目录 / 大小 /
	/// 是否加密），不读取解压后的文件内容，因此对比视图只看压缩包里有什么。
	///
	/// 处理分两路：
	/// <list type="bullet">
	/// <item>压缩包本体（zip / 7z / rar / tar，及 jar / war / apk / nupkg / whl）：直接交给
	/// <see cref="ArchiveFactory"/> 识别并按条目建树。</item>
	/// <item>流式压缩（gz / bz2 / xz / zst，含 gzip 等）先解压：若内容为 tar（或文件名形如
	/// .tar.gz / .tgz）则按 tar 建树，否则视为「单文件压缩流」给出唯一一条条目。</item>
	/// </list>
	/// 带密码的压缩包（如做了头部加密的 7z / rar）解不动时归类为需要 / 密码错误，交由视图提示并输入。
	/// </summary>
	internal static class ArchiveContentExtractor
	{
		/// <summary>渲染 / 建树的条目上限，超出仅保留前 N 条并标记截断（防止超大包卡死 UI）。</summary>
		private const int MaxEntries = 20000;

		/// <summary>解压 sniff / 建树时允许的最大解压体积（防止压缩炸弹撑爆内存）。</summary>
		private const long MaxDecompressedBytes = 512L * 1024 * 1024;

		/// <summary>sniff 解压流的字节数（够判定 tar 头即可）。</summary>
		private const int SniffBytes = 512;

		/// <summary>按扩展名 + 内容把一侧压缩包展开成条目树。永不抛异常，失败归类到 <see cref="ArchiveModel.Error"/>。</summary>
		public static ArchiveModel Extract(string path, byte[] bytes, string password)
		{
			if (bytes == null || bytes.Length == 0)
			{
				return ArchiveModel.Failed(ArchiveError.Corrupt, "no bytes");
			}
			string pw = string.IsNullOrEmpty(password) ? null : password;
			string fileName = Path.GetFileName(path) ?? string.Empty;
			string ext = Extension(path);

			if (IsWrapper(ext))
			{
				byte[] head;
				try
				{
					head = Sniff(ext, bytes);
				}
				catch (Exception ex)
				{
					return ArchiveModel.Failed(ArchiveError.Corrupt, ex.Message);
				}
				bool isTar = LooksLikeTar(head) || IsTarName(fileName);
				if (isTar)
				{
					byte[] inner;
					try
					{
						inner = DecompressAll(ext, bytes);
					}
					catch (Exception ex)
					{
						return ArchiveModel.Failed(ArchiveError.Corrupt, ex.Message);
					}
					return FromArchive(inner, pw, "TAR · " + CompressionName(ext));
				}
				return SingleStream(fileName, ext, bytes);
			}

			return FromArchive(bytes, pw, FormatForExtension(ext));
		}

		// ---- 压缩包本体 ----

		private static ArchiveModel FromArchive(byte[] bytes, string password, string formatOverride)
		{
			try
			{
				using (MemoryStream stream = new MemoryStream(bytes))
				using (IArchive archive = ArchiveFactory.Open(stream, new ReaderOptions { Password = password, LeaveStreamOpen = true }))
				{
					string format = formatOverride ?? FormatForArchiveType(archive.Type);
					List<RawEntry> raw = new List<RawEntry>();
					foreach (IArchiveEntry entry in archive.Entries)
					{
						raw.Add(RawEntry.From(entry));
					}
					return Build(format, raw);
				}
			}
			catch (Exception ex)
			{
				return ArchiveModel.Failed(ClassifyFailure(ex, password), ex.Message);
			}
		}

		/// <summary>
		/// 把 SharpCompress 抛出的异常归类成视图可用的错误类别：
		/// <list type="bullet">
		/// <item><see cref="CryptographicException"/>：头部 / 内容加密，未给密码或密码错；</item>
		/// <item><c>DataErrorException</c>（LZMA 解密失败，SharpCompress 中为 internal，无法按类型捕获）：
		/// 7z 密码错误——按异常类型名识别，未给密码时归为「需要密码」；</item>
		/// <item><see cref="InvalidOperationException"/>：宿主尚未支持该格式；</item>
		/// <item>其余（含 <c>ArchiveException</c> 系列）：视为压缩包损坏。</item>
		/// </list>
		/// </summary>
		private static ArchiveError ClassifyFailure(Exception ex, string password)
		{
			if (ex is CryptographicException)
			{
				return password == null ? ArchiveError.PasswordRequired : ArchiveError.PasswordIncorrect;
			}
			if (string.Equals(ex.GetType().Name, "DataErrorException", StringComparison.Ordinal))
			{
				return password == null ? ArchiveError.PasswordRequired : ArchiveError.PasswordIncorrect;
			}
			if (ex is InvalidOperationException)
			{
				return ArchiveError.Unsupported;
			}
			return ArchiveError.Corrupt;
		}

		/// <summary>一条原始条目（建树前的扁平投影）。大小字段读取出错时降级为 null，不让整包失败。</summary>
		private sealed class RawEntry
		{
			public string Key;
			public bool IsDirectory;
			public long? Size;
			public long? CompressedSize;
			public bool IsEncrypted;

			public static RawEntry From(IArchiveEntry entry)
			{
				RawEntry result = new RawEntry
				{
					Key = entry.Key ?? string.Empty,
					IsDirectory = entry.IsDirectory,
					IsEncrypted = SafeEncrypted(entry),
				};
				if (!entry.IsDirectory)
				{
					result.Size = SafeLong(delegate { return entry.Size; });
					result.CompressedSize = SafeLong(delegate { return entry.CompressedSize; });
				}
				return result;
			}

			private static bool SafeEncrypted(IArchiveEntry entry)
			{
				try
				{
					return entry.IsEncrypted;
				}
				catch (Exception)
				{
					return false;
				}
			}

			private static long? SafeLong(Func<long> read)
			{
				try
				{
					return read();
				}
				catch (Exception)
				{
					return null;
				}
			}
		}

		/// <summary>把扁平条目列表还原成目录树，再按「目录在前、同名按名排序」的树序遍历展平。</summary>
		private static ArchiveModel Build(string format, List<RawEntry> raw)
		{
			TreeNode root = new TreeNode(string.Empty, string.Empty, true);
			foreach (RawEntry entry in raw)
			{
				string key = Normalize(entry.Key);
				if (key.Length == 0)
				{
					continue;
				}
				string[] parts = key.Split('/');
				TreeNode current = root;
				StringBuilder accumulated = new StringBuilder();
				for (int i = 0; i < parts.Length; i++)
				{
					if (parts[i].Length == 0)
					{
						continue;
					}
					if (accumulated.Length > 0)
					{
						accumulated.Append('/');
					}
					accumulated.Append(parts[i]);
					bool last = i == parts.Length - 1;
					TreeNode child = current.Find(parts[i]);
					if (child == null)
					{
						child = new TreeNode(parts[i], accumulated.ToString(), !last || entry.IsDirectory);
						current.Children.Add(child);
					}
					if (last)
					{
						child.IsDirectory = entry.IsDirectory;
						child.Size = entry.IsDirectory ? (long?)null : entry.Size;
						child.CompressedSize = entry.IsDirectory ? (long?)null : entry.CompressedSize;
						child.IsEncrypted = entry.IsEncrypted;
					}
					current = child;
				}
			}

			List<ArchiveEntryNode> flat = new List<ArchiveEntryNode>();
			int[] counters = new int[2];
			long[] total = new long[1];
			bool[] encrypted = new bool[1];
			bool[] truncated = new bool[1];
			Walk(root, 0, flat, counters, total, encrypted, truncated);
			return new ArchiveModel(format, flat, counters[1], counters[0], total[0], encrypted[0], truncated[0], ArchiveError.None, null);
		}

		private static void Walk(TreeNode node, int depth, List<ArchiveEntryNode> flat, int[] counters, long[] total, bool[] encrypted, bool[] truncated)
		{
			foreach (TreeNode child in TreeNode.Sorted(node.Children))
			{
				if (flat.Count >= MaxEntries)
				{
					truncated[0] = true;
					return;
				}
				flat.Add(new ArchiveEntryNode(child.Path, child.Name, child.IsDirectory, child.Size, child.CompressedSize, child.IsEncrypted, depth));
				if (child.IsDirectory)
				{
					counters[0]++;
				}
				else
				{
					counters[1]++;
					total[0] += child.Size ?? 0L;
				}
				if (child.IsEncrypted)
				{
					encrypted[0] = true;
				}
				if (child.IsDirectory)
				{
					Walk(child, depth + 1, flat, counters, total, encrypted, truncated);
				}
			}
		}

		private sealed class TreeNode
		{
			private static readonly IReadOnlyList<TreeNode> EmptyChildren = new List<TreeNode>();

			public TreeNode(string name, string path, bool isDirectory)
			{
				Name = name;
				Path = path;
				IsDirectory = isDirectory;
			}

			public string Name { get; }

			public string Path { get; }

			public bool IsDirectory { get; set; }

			public long? Size { get; set; }

			public long? CompressedSize { get; set; }

			public bool IsEncrypted { get; set; }

			public List<TreeNode> Children { get; } = new List<TreeNode>();

			public TreeNode Find(string name)
			{
				for (int i = 0; i < Children.Count; i++)
				{
					if (string.Equals(Children[i].Name, name, StringComparison.Ordinal))
					{
						return Children[i];
					}
				}
				return null;
			}

			/// <summary>目录在前，其后按名排序（忽略大小写优先，再按序数保证稳定）。</summary>
			public static IReadOnlyList<TreeNode> Sorted(List<TreeNode> children)
			{
				if (children == null || children.Count == 0)
				{
					return EmptyChildren;
				}
				return children
					.OrderByDescending(delegate (TreeNode c) { return c.IsDirectory; })
					.ThenBy(delegate (TreeNode c) { return c.Name; }, StringComparer.OrdinalIgnoreCase)
					.ThenBy(delegate (TreeNode c) { return c.Name; }, StringComparer.Ordinal)
					.ToList();
			}
		}

		// ---- 单文件压缩流 ----

		private static ArchiveModel SingleStream(string fileName, string ext, byte[] bytes)
		{
			string name = StripCompressionExt(fileName, ext);
			long? size = TryUncompressedSize(ext, bytes);
			List<ArchiveEntryNode> flat = new List<ArchiveEntryNode>
			{
				new ArchiveEntryNode(name, name, false, size, bytes.Length, false, 0)
			};
			return new ArchiveModel(CompressionName(ext), flat, 1, 0, size ?? 0L, false, false, ArchiveError.None, null);
		}

		/// <summary>gzip 的未压缩大小可由末尾 4 字节 ISIZE 直接读出，避免为列表而整包解压。</summary>
		private static long? TryUncompressedSize(string ext, byte[] bytes)
		{
			if ((ext == "gz" || ext == "gzip") && bytes.Length >= 4)
			{
				int i = bytes.Length - 4;
				uint isize = (uint)(bytes[i] | (bytes[i + 1] << 8) | (bytes[i + 2] << 16) | (bytes[i + 3] << 24));
				return isize;
			}
			return null;
		}

		// ---- 压缩流解包 ----

		private static bool IsWrapper(string ext)
		{
			switch (ext)
			{
				case "gz":
				case "gzip":
				case "tgz":
				case "taz":
				case "bz2":
				case "bzip2":
				case "tbz":
				case "tbz2":
				case "xz":
				case "txz":
				case "zst":
				case "zstd":
				case "tzst":
					return true;
				default:
					return false;
			}
		}

		private static Stream Wrap(string ext, Stream source)
		{
			switch (ext)
			{
				case "gz":
				case "gzip":
				case "tgz":
				case "taz":
					return new GZipStream(source, CompressionMode.Decompress);
				case "bz2":
				case "bzip2":
				case "tbz":
				case "tbz2":
					return new BZip2Stream(source, SC.CompressionMode.Decompress, false);
				case "xz":
				case "txz":
					return new XZStream(source);
				case "zst":
				case "zstd":
				case "tzst":
					return new DecompressionStream(source);
				default:
					throw new InvalidDataException("not a compression wrapper: " + ext);
			}
		}

		private static byte[] Sniff(string ext, byte[] bytes)
		{
			using (MemoryStream source = new MemoryStream(bytes))
			using (Stream inner = Wrap(ext, source))
			{
				return ReadUpTo(inner, SniffBytes);
			}
		}

		private static byte[] DecompressAll(string ext, byte[] bytes)
		{
			using (MemoryStream source = new MemoryStream(bytes))
			using (Stream inner = Wrap(ext, source))
			using (MemoryStream output = new MemoryStream())
			{
				byte[] buffer = new byte[81920];
				long total = 0L;
				int read;
				while ((read = inner.Read(buffer, 0, buffer.Length)) > 0)
				{
					total += read;
					if (total > MaxDecompressedBytes)
					{
						throw new InvalidDataException("decompressed stream too large");
					}
					output.Write(buffer, 0, read);
				}
				return output.ToArray();
			}
		}

		private static byte[] ReadUpTo(Stream stream, int count)
		{
			byte[] buffer = new byte[count];
			int total = 0;
			while (total < count)
			{
				int read = stream.Read(buffer, total, count - total);
				if (read <= 0)
				{
					break;
				}
				total += read;
			}
			if (total == count)
			{
				return buffer;
			}
			byte[] smaller = new byte[total];
			Array.Copy(buffer, smaller, total);
			return smaller;
		}

		/// <summary>tar 判定：POSIX ustar 魔数位于第 257 字节起。</summary>
		private static bool LooksLikeTar(byte[] head)
		{
			if (head == null || head.Length < 263)
			{
				return false;
			}
			return head[257] == (byte)'u' && head[258] == (byte)'s' && head[259] == (byte)'t'
				&& head[260] == (byte)'a' && head[261] == (byte)'r';
		}

		private static bool IsTarName(string fileName)
		{
			string lower = (fileName ?? string.Empty).ToLowerInvariant();
			string[] suffixes = new string[]
			{
				".tar.gz", ".tar.gzip", ".tar.bz2", ".tar.bzip2", ".tar.xz", ".tar.zst", ".tar.zstd",
				".tgz", ".taz", ".tbz", ".tbz2", ".txz", ".tzst"
			};
			foreach (string suffix in suffixes)
			{
				if (lower.EndsWith(suffix, StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		private static string StripCompressionExt(string fileName, string ext)
		{
			string suffix = "." + ext;
			if (fileName.Length > suffix.Length && fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
			{
				return fileName.Substring(0, fileName.Length - suffix.Length);
			}
			return fileName;
		}

		private static string CompressionName(string ext)
		{
			switch (ext)
			{
				case "gz":
				case "gzip":
				case "tgz":
				case "taz":
					return "GZIP";
				case "bz2":
				case "bzip2":
				case "tbz":
				case "tbz2":
					return "BZIP2";
				case "xz":
				case "txz":
					return "XZ";
				case "zst":
				case "zstd":
				case "tzst":
					return "ZSTD";
				default:
					return "COMPRESSED";
			}
		}

		private static string Extension(string path)
		{
			return (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant().TrimStart('.');
		}

		/// <summary>包容器格式名（zip 系容器按扩展名细分展示）。</summary>
		private static string FormatForExtension(string ext)
		{
			switch (ext)
			{
				case "jar":
					return "JAR";
				case "war":
					return "WAR";
				case "apk":
					return "APK";
				case "nupkg":
					return "NUPKG";
				case "whl":
					return "WHEEL";
				default:
					return null;
			}
		}

		private static string FormatForArchiveType(ArchiveType type)
		{
			switch (type)
			{
				case ArchiveType.Zip:
					return "ZIP";
				case ArchiveType.SevenZip:
					return "7-Zip";
				case ArchiveType.Rar:
					return "RAR";
				case ArchiveType.Tar:
					return "TAR";
				case ArchiveType.GZip:
					return "GZIP";
				default:
					return type.ToString();
			}
		}

		private static string Normalize(string key)
		{
			if (string.IsNullOrEmpty(key))
			{
				return string.Empty;
			}
			string normalized = key.Replace('\\', '/').Trim();
			while (normalized.StartsWith("./", StringComparison.Ordinal))
			{
				normalized = normalized.Substring(2);
			}
			normalized = normalized.TrimStart('/');
			return normalized.TrimEnd('/');
		}
	}
}