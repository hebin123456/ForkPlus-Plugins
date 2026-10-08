using System.Collections.Generic;

namespace ForkPlus.Plugins.Psd
{
	/// <summary>键路径行的差异四态（与其它插件的四色口径一致）。</summary>
	public enum PsdState
	{
		Same,

		Changed,

		LeftOnly,

		RightOnly
	}

	/// <summary>图层在文件里的分组角色（来自 additional info 的 LSct 块）。</summary>
	public enum PsdSectionType
	{
		/// <summary>普通图层。</summary>
		None,

		/// <summary>展开的图层组（open folder）。</summary>
		OpenFolder,

		/// <summary>收起的图层组（closed folder）。</summary>
		ClosedFolder,

		/// <summary>分组结束的分隔线（divider）。</summary>
		Divider
	}

	/// <summary>一条图层记录的结构化投影（呈现属性已格式化成文本，便于直接 diff）。</summary>
	public sealed class PsdLayer
	{
		/// <summary>图层名（优先 Unicode 名 "luni"，缺省回退 Pascal 名）。</summary>
		public string Name;

		/// <summary>矩形摘要，形如 "512×384 @ (32,16)"。</summary>
		public string RectText;

		/// <summary>混合模式 key（"norm" / "muld" 等，Photoshop 原样四字符）。</summary>
		public string Blend;

		/// <summary>不透明度百分比（0-100）。</summary>
		public int OpacityPercent;

		/// <summary>是否可见（flags bit1 取反）。</summary>
		public bool Visible;

		/// <summary>通道构成摘要，形如 "RGB+A"。</summary>
		public string ChannelIdsText;

		/// <summary>分组角色。</summary>
		public PsdSectionType SectionType;

		/// <summary>图层属性摘要（Layers 表的 Old / New 单元格文案）。</summary>
		public string SummaryText()
		{
			return RectText + " · " + Blend + " · " + OpacityPercent.ToString() + "% · "
				+ (Visible ? PsdStrings.T("visible") : PsdStrings.T("hidden")) + " · " + ChannelIdsText;
		}
	}

	/// <summary>头部键路径行（Header 模式的行单元）。</summary>
	public sealed class PsdRow
	{
		public string Path;

		public string Value;
	}

	/// <summary>一份 PSD / PSB 的解析结果模型。</summary>
	public sealed class PsdDocument
	{
		/// <summary>容器格式："PSD" / "PSB"。</summary>
		public string Format;

		public int Width;

		public int Height;

		public int Channels;

		public int Depth;

		/// <summary>颜色模式名（RGB / CMYK / Grayscale…）。</summary>
		public string ColorModeName;

		/// <summary>图像资源块总数。</summary>
		public int ResourceCount;

		/// <summary>图层列表（文件内顺序；PSD 自顶向下记录，保持原序）。</summary>
		public List<PsdLayer> Layers = new List<PsdLayer>();

		/// <summary>头部键路径行（width / height / channels / depth / color mode / resources / layers / image compression）。</summary>
		public List<PsdRow> Rows = new List<PsdRow>();

		/// <summary>内嵌缩略图（图像资源 1036 的 JFIF 字节）；无缩略图为 null。</summary>
		public byte[] ThumbnailJfif;

		/// <summary>缩略图尺寸摘要（"256×256"）；无缩略图为 null。</summary>
		public string ThumbnailSizeText;

		/// <summary>状态行的格式徽章文案，如 "PSD 1024×768"。</summary>
		public string FormatValue()
		{
			return Format + " " + Width.ToString() + "×" + Height.ToString();
		}
	}

	/// <summary>头部键路径 diff 的一行。</summary>
	public sealed class PsdDiffRow
	{
		public string Path;

		public string LeftValue;

		public string RightValue;

		public PsdState State;
	}

	/// <summary>头部键路径 diff 结果。</summary>
	public sealed class PsdDiffResult
	{
		public List<PsdDiffRow> Rows = new List<PsdDiffRow>();

		public int Total;

		public int Changed;

		public int LeftOnly;

		public int RightOnly;

		public bool HasDifferences
		{
			get { return Changed + LeftOnly + RightOnly > 0; }
		}
	}

	/// <summary>图层 diff 的一行（按图层名配对后的左右两侧 + 四态）。</summary>
	public sealed class PsdLayerRow
	{
		public PsdLayer Left;

		public PsdLayer Right;

		public PsdState State;
	}

	/// <summary>图层 diff 结果。</summary>
	public sealed class PsdLayerDiffResult
	{
		public List<PsdLayerRow> Rows = new List<PsdLayerRow>();

		public int Total;

		public int Changed;

		public int LeftOnly;

		public int RightOnly;

		public bool HasDifferences
		{
			get { return Changed + LeftOnly + RightOnly > 0; }
		}
	}

	/// <summary>
	/// PSD 语义 diff：头部行按键路径取并集、图层按「名称首个未配对者配对」对齐，各得四态结果。
	/// </summary>
	internal static class PsdDiff
	{
		/// <summary>头部键路径 diff：并集逐行比较（同路径同值 = 相同；都存在但不同 = 已变）。</summary>
		internal static PsdDiffResult ComputeHeader(List<PsdRow> left, List<PsdRow> right)
		{
			PsdDiffResult result = new PsdDiffResult();
			Dictionary<string, string> rightMap = new Dictionary<string, string>(System.StringComparer.Ordinal);
			if (right != null)
			{
				foreach (PsdRow row in right)
				{
					rightMap[row.Path] = row.Value;
				}
			}
			HashSet<string> seen = new HashSet<string>(System.StringComparer.Ordinal);
			if (left != null)
			{
				foreach (PsdRow row in left)
				{
					string rightValue;
					bool hasRight = rightMap.TryGetValue(row.Path, out rightValue);
					PsdDiffRow diffRow = new PsdDiffRow
					{
						Path = row.Path,
						LeftValue = row.Value,
						RightValue = hasRight ? rightValue : null,
						State = !hasRight ? PsdState.LeftOnly : (StringEquals(row.Value, rightValue) ? PsdState.Same : PsdState.Changed)
					};
					result.Rows.Add(diffRow);
					seen.Add(row.Path);
				}
			}
			if (right != null)
			{
				foreach (PsdRow row in right)
				{
					if (seen.Contains(row.Path))
					{
						continue;
					}
					result.Rows.Add(new PsdDiffRow
					{
						Path = row.Path,
						LeftValue = null,
						RightValue = row.Value,
						State = PsdState.RightOnly
					});
				}
			}
			Summarize(result);
			return result;
		}

		/// <summary>图层 diff：按名称配对（两侧各自按文件内顺序，同名首个未配对者配对）。</summary>
		internal static PsdLayerDiffResult ComputeLayers(List<PsdLayer> left, List<PsdLayer> right)
		{
			PsdLayerDiffResult result = new PsdLayerDiffResult();
			Dictionary<string, Queue<PsdLayer>> rightMap = new Dictionary<string, Queue<PsdLayer>>(System.StringComparer.Ordinal);
			if (right != null)
			{
				foreach (PsdLayer layer in right)
				{
					Queue<PsdLayer> queue;
					if (!rightMap.TryGetValue(layer.Name, out queue))
					{
						queue = new Queue<PsdLayer>();
						rightMap[layer.Name] = queue;
					}
					queue.Enqueue(layer);
				}
			}
			HashSet<PsdLayer> matched = new HashSet<PsdLayer>();
			if (left != null)
			{
				foreach (PsdLayer layer in left)
				{
					Queue<PsdLayer> queue;
					PsdLayer partner = null;
					if (rightMap.TryGetValue(layer.Name, out queue) && queue.Count > 0)
					{
						partner = queue.Dequeue();
						matched.Add(partner);
					}
					result.Rows.Add(new PsdLayerRow
					{
						Left = layer,
						Right = partner,
						State = partner == null ? PsdState.LeftOnly : (LayerEquals(layer, partner) ? PsdState.Same : PsdState.Changed)
					});
				}
			}
			if (right != null)
			{
				foreach (PsdLayer layer in right)
				{
					if (matched.Contains(layer))
					{
						continue;
					}
					result.Rows.Add(new PsdLayerRow
					{
						Left = null,
						Right = layer,
						State = PsdState.RightOnly
					});
				}
			}
			Summarize(result);
			return result;
		}

		/// <summary>图层属性等价：呈现属性全等（名称已由配对保证相同）。</summary>
		private static bool LayerEquals(PsdLayer left, PsdLayer right)
		{
			return left.RectText == right.RectText
				&& left.Blend == right.Blend
				&& left.OpacityPercent == right.OpacityPercent
				&& left.Visible == right.Visible
				&& left.ChannelIdsText == right.ChannelIdsText
				&& left.SectionType == right.SectionType;
		}

		private static bool StringEquals(string left, string right)
		{
			return string.Equals(left ?? string.Empty, right ?? string.Empty, System.StringComparison.Ordinal);
		}

		private static void Summarize(PsdDiffResult result)
		{
			result.Total = result.Rows.Count;
			foreach (PsdDiffRow row in result.Rows)
			{
				Count(result, row.State);
			}
		}

		private static void Summarize(PsdLayerDiffResult result)
		{
			result.Total = result.Rows.Count;
			foreach (PsdLayerRow row in result.Rows)
			{
				Count(result, row.State);
			}
		}

		private static void Count(PsdDiffResult result, PsdState state)
		{
			switch (state)
			{
			case PsdState.Changed:
				result.Changed++;
				break;
			case PsdState.LeftOnly:
				result.LeftOnly++;
				break;
			case PsdState.RightOnly:
				result.RightOnly++;
				break;
			}
		}

		private static void Count(PsdLayerDiffResult result, PsdState state)
		{
			switch (state)
			{
			case PsdState.Changed:
				result.Changed++;
				break;
			case PsdState.LeftOnly:
				result.LeftOnly++;
				break;
			case PsdState.RightOnly:
				result.RightOnly++;
				break;
			}
		}
	}
}
