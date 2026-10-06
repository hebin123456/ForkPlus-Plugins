using System;
using System.Collections.Generic;

namespace ForkPlus.Plugins.Font
{
	/// <summary>
	/// 一套字体的解析结果（.ttc / .otc 集合里的一「套」）。字段全部来自纯自研 sfnt 表解析；
	/// 任一表损坏时该字段保持 null，其余可读部分照常填充（降级而非整体失败）。
	/// </summary>
	internal sealed class FontFace
	{
		/// <summary>在集合中的序号（0 起；单套字体恒为 0）。</summary>
		public int Index { get; set; }

		/// <summary>可选行的展示名（family + subfamily，缺省回退 full name / PostScript name）。</summary>
		public string DisplayName { get; set; }

		// ---- name 表 ----

		public string Family { get; set; }

		public string Subfamily { get; set; }

		public string UniqueId { get; set; }

		public string FullName { get; set; }

		public string Version { get; set; }

		public string Copyright { get; set; }

		public string Trademark { get; set; }

		public string PostScriptName { get; set; }

		// ---- head 表 ----

		public int? UnitsPerEm { get; set; }

		public DateTime? Created { get; set; }

		public DateTime? Modified { get; set; }

		/// <summary>macStyle 原始位（bold=0x01 / italic=0x02 / underline=0x04 …）。</summary>
		public int? MacStyle { get; set; }

		// ---- OS/2 表 ----

		public int? WeightClass { get; set; }

		public int? WidthClass { get; set; }

		/// <summary>fsSelection 原始位（italic=0x01 / bold=0x20 / regular=0x40 / oblique=0x200 …）。</summary>
		public int? Selection { get; set; }

		public int? TypoAscender { get; set; }

		public int? TypoDescender { get; set; }

		public int? TypoLineGap { get; set; }

		public int? WinAscent { get; set; }

		public int? WinDescent { get; set; }

		// ---- maxp 表 ----

		public int? NumGlyphs { get; set; }

		// ---- hhea 表 ----

		public int? HheaAscender { get; set; }

		public int? HheaDescender { get; set; }

		public int? HheaLineGap { get; set; }

		// ---- 表目录 / 布局表 ----

		/// <summary>表目录：tag → 声明长度（字节）。表存在性由 ContainsKey 判定。</summary>
		public SortedDictionary<string, int> TableSizes { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

		/// <summary>GSUB 的 feature tag 列表（已排序）。</summary>
		public List<string> GsubFeatures { get; } = new List<string>();

		// ---- cmap 码位覆盖 ----

		public HashSet<int> Codepoints { get; } = new HashSet<int>();

		/// <summary>码位集合因超过上限被截断。</summary>
		public bool CodepointsTruncated { get; set; }

		/// <summary>是否至少成功读到一个 cmap 子表（区分「无码位」与「表不可读」）。</summary>
		public bool CmapParsed { get; set; }

		/// <summary>逐表解析过程中的降级说明（表损坏但未导致整体失败）。</summary>
		public List<string> Warnings { get; } = new List<string>();

		public bool HasTable(string tag)
		{
			return TableSizes.ContainsKey(tag);
		}
	}
}
