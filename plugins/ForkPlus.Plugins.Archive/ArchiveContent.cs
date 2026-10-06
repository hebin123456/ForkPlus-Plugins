using System.Collections.Generic;

namespace ForkPlus.Plugins.Archive
{
	/// <summary>解包 / 列表过程中的错误分类，决定视图给出的提示文案。</summary>
	internal enum ArchiveError
	{
		/// <summary>成功。</summary>
		None,

		/// <summary>压缩包已加密，但未提供密码（如做了头部加密的 7z / rar）。</summary>
		PasswordRequired,

		/// <summary>提供了密码但不正确。</summary>
		PasswordIncorrect,

		/// <summary>无法识别的压缩格式。</summary>
		Unsupported,

		/// <summary>压缩包损坏或读取失败。</summary>
		Corrupt
	}

	/// <summary>
	/// 压缩包里的一条条目（展开成树后的一行）：相对路径、目录标记、未压缩 / 压缩后大小、
	/// 是否加密，以及缩进深度（渲染时用）。
	/// </summary>
	internal sealed class ArchiveEntryNode
	{
		public ArchiveEntryNode(string path, string name, bool isDirectory, long? size, long? compressedSize, bool isEncrypted, int depth)
		{
			Path = path ?? string.Empty;
			Name = name ?? string.Empty;
			IsDirectory = isDirectory;
			Size = size;
			CompressedSize = compressedSize;
			IsEncrypted = isEncrypted;
			Depth = depth;
		}

		/// <summary>压缩包内的完整相对路径（/ 分隔）。</summary>
		public string Path { get; }

		/// <summary>叶子名（最后一段）。</summary>
		public string Name { get; }

		public bool IsDirectory { get; }

		/// <summary>未压缩大小；未知为 null。</summary>
		public long? Size { get; }

		/// <summary>压缩后大小；未知为 null（目录为 null）。</summary>
		public long? CompressedSize { get; }

		public bool IsEncrypted { get; }

		/// <summary>树深度，根下第一层为 0（渲染缩进用）。</summary>
		public int Depth { get; }
	}

	/// <summary>
	/// 一侧压缩包展开后的结果：格式名、按树序遍历的条目序列（已展平）、各类计数，
	/// 以及错误分类（成功为 <see cref="ArchiveError.None"/>）。
	/// </summary>
	internal sealed class ArchiveModel
	{
		private static readonly IReadOnlyList<ArchiveEntryNode> Empty = new List<ArchiveEntryNode>();

		public ArchiveModel(
			string format,
			IReadOnlyList<ArchiveEntryNode> entries,
			int fileCount,
			int directoryCount,
			long totalFileSize,
			bool hasEncrypted,
			bool truncated,
			ArchiveError error,
			string errorDetail)
		{
			Format = format ?? string.Empty;
			Entries = entries ?? Empty;
			FileCount = fileCount;
			DirectoryCount = directoryCount;
			TotalFileSize = totalFileSize;
			HasEncrypted = hasEncrypted;
			Truncated = truncated;
			Error = error;
			ErrorDetail = errorDetail;
		}

		/// <summary>格式名（如 "ZIP" / "7-Zip" / "TAR · GZIP"）。</summary>
		public string Format { get; }

		/// <summary>按树序遍历展平后的条目（目录在前、文件在后，各自按名排序）。</summary>
		public IReadOnlyList<ArchiveEntryNode> Entries { get; }

		public int FileCount { get; }

		public int DirectoryCount { get; }

		public long TotalFileSize { get; }

		/// <summary>是否存在加密条目（zip 的条目名可见但内容加密时同样为 true）。</summary>
		public bool HasEncrypted { get; }

		/// <summary>条目过多被截断时为 true（视图会给出提示）。</summary>
		public bool Truncated { get; }

		public ArchiveError Error { get; }

		/// <summary>错误详情（原始异常消息等；成功为 null）。</summary>
		public string ErrorDetail { get; }

		/// <summary>条目总数（目录 + 文件）。</summary>
		public int EntryCount => DirectoryCount + FileCount;

		public static ArchiveModel Failed(ArchiveError error, string detail)
		{
			return new ArchiveModel(string.Empty, Empty, 0, 0, 0L, false, false, error, detail);
		}
	}
}