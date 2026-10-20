using System;
using System.Collections.Generic;

namespace ForkPlus.Plugins.Patch
{
	/// <summary>补丁内一行的类型：上下文 / 新增 / 删除 / 「文件末尾无换行」标记。</summary>
	internal enum PatchLineKind
	{
		Context,
		Add,
		Delete,
		NoNewline
	}

	/// <summary>补丁里的一行（不含前缀字符的正文）。</summary>
	internal sealed class PatchLine
	{
		public PatchLineKind Kind { get; }

		public string Text { get; }

		public PatchLine(PatchLineKind kind, string text)
		{
			Kind = kind;
			Text = text ?? string.Empty;
		}
	}

	/// <summary>一个 hunk：<c>@@ -oldStart,oldCount +newStart,newCount @@ section</c> 及其行。</summary>
	internal sealed class PatchHunk
	{
		public int OldStart { get; }

		public int OldCount { get; }

		public int NewStart { get; }

		public int NewCount { get; }

		/// <summary>hunk 尾部的函数 / 区段名（git diff 附加，可能为空）。</summary>
		public string Section { get; }

		public List<PatchLine> Lines { get; }

		public PatchHunk(int oldStart, int oldCount, int newStart, int newCount, string section)
		{
			OldStart = oldStart;
			OldCount = oldCount;
			NewStart = newStart;
			NewCount = newCount;
			Section = section ?? string.Empty;
			Lines = new List<PatchLine>();
		}
	}

	/// <summary>补丁内单个文件的改动类型。</summary>
	internal enum PatchFileChange
	{
		Modified,
		Added,
		Deleted,
		Renamed,
		Binary
	}

	/// <summary>补丁针对的一个文件：路径、改动类型与全部 hunk。</summary>
	internal sealed class PatchFile
	{
		/// <summary>旧路径（新增文件 / 纯新增侧为 null）。</summary>
		public string OldPath { get; }

		/// <summary>新路径（删除文件 / 纯删除侧为 null）。</summary>
		public string NewPath { get; }

		public PatchFileChange Change { get; }

		public bool IsBinary { get; }

		public List<PatchHunk> Hunks { get; }

		public PatchFile(string oldPath, string newPath, PatchFileChange change, bool isBinary)
		{
			OldPath = oldPath;
			NewPath = newPath;
			Change = change;
			IsBinary = isBinary;
			Hunks = new List<PatchHunk>();
		}

		/// <summary>对齐用键：新路径优先（补丁作用于「新状态」），缺失回退旧路径。</summary>
		public string Key
		{
			get
			{
				return !string.IsNullOrEmpty(NewPath) ? NewPath : (OldPath ?? string.Empty);
			}
		}

		public int Additions
		{
			get
			{
				return CountLines(PatchLineKind.Add);
			}
		}

		public int Deletions
		{
			get
			{
				return CountLines(PatchLineKind.Delete);
			}
		}

		private int CountLines(PatchLineKind kind)
		{
			int count = 0;
			foreach (PatchHunk hunk in Hunks)
			{
				foreach (PatchLine line in hunk.Lines)
				{
					if (line.Kind == kind)
					{
						count++;
					}
				}
			}
			return count;
		}

		/// <summary>两个版本的补丁针对同一文件时，判定内容是否完全一致（头部 + 逐行）。</summary>
		public bool SameAs(PatchFile other)
		{
			if (other == null || Change != other.Change || IsBinary != other.IsBinary || Hunks.Count != other.Hunks.Count)
			{
				return false;
			}
			for (int i = 0; i < Hunks.Count; i++)
			{
				PatchHunk a = Hunks[i];
				PatchHunk b = other.Hunks[i];
				if (a.OldStart != b.OldStart || a.OldCount != b.OldCount
					|| a.NewStart != b.NewStart || a.NewCount != b.NewCount
					|| !string.Equals(a.Section, b.Section, StringComparison.Ordinal)
					|| a.Lines.Count != b.Lines.Count)
				{
					return false;
				}
				for (int j = 0; j < a.Lines.Count; j++)
				{
					if (a.Lines[j].Kind != b.Lines[j].Kind
						|| !string.Equals(a.Lines[j].Text, b.Lines[j].Text, StringComparison.Ordinal))
					{
						return false;
					}
				}
			}
			return true;
		}
	}

	/// <summary>一份补丁文件（一个 .patch / .diff）解析出的全部文件条目与统计。</summary>
	internal sealed class PatchDocument
	{
		public List<PatchFile> Files { get; }

		public int TotalAdditions { get; private set; }

		public int TotalDeletions { get; private set; }

		public PatchDocument(List<PatchFile> files)
		{
			Files = files ?? new List<PatchFile>();
			foreach (PatchFile file in Files)
			{
				TotalAdditions += file.Additions;
				TotalDeletions += file.Deletions;
			}
		}
	}

	/// <summary>两侧补丁按文件对齐后的状态（沿用 CHM / 结构化插件的四态口径）。</summary>
	internal enum PatchFileState
	{
		Same,
		Changed,
		LeftOnly,
		RightOnly
	}

	/// <summary>对齐结果的一行：同一文件在两个补丁版本里的形态。</summary>
	internal sealed class PatchFileDiffRow
	{
		public string Key { get; }

		public PatchFile Left { get; }

		public PatchFile Right { get; }

		public PatchFileState State { get; }

		public PatchFileDiffRow(string key, PatchFile left, PatchFile right, PatchFileState state)
		{
			Key = key;
			Left = left;
			Right = right;
			State = state;
		}
	}

	/// <summary>文件级对齐结果（含四态汇总计数）。</summary>
	internal sealed class PatchFileDiffResult
	{
		public List<PatchFileDiffRow> Rows { get; } = new List<PatchFileDiffRow>();

		public int Total
		{
			get { return Rows.Count; }
		}

		public int Changed { get; private set; }

		public int LeftOnly { get; private set; }

		public int RightOnly { get; private set; }

		public bool HasDifferences
		{
			get { return Changed > 0 || LeftOnly > 0 || RightOnly > 0; }
		}

		private void Add(PatchFile left, PatchFile right, PatchFileState state)
		{
			string key = left?.Key ?? right?.Key ?? string.Empty;
			Rows.Add(new PatchFileDiffRow(key, left, right, state));
			if (state == PatchFileState.Changed)
			{
				Changed++;
			}
			else if (state == PatchFileState.LeftOnly)
			{
				LeftOnly++;
			}
			else if (state == PatchFileState.RightOnly)
			{
				RightOnly++;
			}
		}

		/// <summary>把两侧补丁按文件键对齐（新路径优先；先到先得，键为空跳过）。
		/// 与 CHM / 目录条目对齐同口径：左遍历配对，右剩余记 RightOnly。</summary>
		public static PatchFileDiffResult Compute(List<PatchFile> left, List<PatchFile> right)
		{
			PatchFileDiffResult result = new PatchFileDiffResult();
			Dictionary<string, PatchFile> rightMap = new Dictionary<string, PatchFile>(StringComparer.Ordinal);
			if (right != null)
			{
				foreach (PatchFile file in right)
				{
					if (!string.IsNullOrEmpty(file.Key) && !rightMap.ContainsKey(file.Key))
					{
						rightMap.Add(file.Key, file);
					}
				}
			}
			HashSet<string> consumed = new HashSet<string>(StringComparer.Ordinal);
			if (left != null)
			{
				foreach (PatchFile file in left)
				{
					if (string.IsNullOrEmpty(file.Key) || consumed.Contains(file.Key))
					{
						continue;
					}
					consumed.Add(file.Key);
					if (rightMap.TryGetValue(file.Key, out PatchFile match))
					{
						result.Add(file, match, file.SameAs(match) ? PatchFileState.Same : PatchFileState.Changed);
					}
					else
					{
						result.Add(file, null, PatchFileState.LeftOnly);
					}
				}
			}
			if (right != null)
			{
				foreach (PatchFile file in right)
				{
					if (string.IsNullOrEmpty(file.Key) || consumed.Contains(file.Key))
					{
						continue;
					}
					consumed.Add(file.Key);
					result.Add(null, file, PatchFileState.RightOnly);
				}
			}
			return result;
		}
	}
}
