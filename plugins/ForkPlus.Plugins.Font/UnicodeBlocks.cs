namespace ForkPlus.Plugins.Font
{
	/// <summary>码位 → Unicode 区块归类（用于码位覆盖统计，按设计文档要求的粒度分组）。</summary>
	internal static class UnicodeBlocks
	{
		/// <summary>统计表的固定行序（含兜底 "Other"）。</summary>
		public static readonly string[] Order = new string[7]
		{
			"Latin",
			"Greek",
			"Cyrillic",
			"CJK Unified",
			"Digits",
			"Punctuation",
			"Other"
		};

		// 顺序即归类优先级：先匹配在前。区间为闭区间。
		private static readonly (string Name, int Start, int End)[] Ranges = new (string, int, int)[]
		{
			("Digits", 0x0030, 0x0039),
			("Digits", 0x0660, 0x0669),
			("Digits", 0xFF10, 0xFF19),
			("Latin", 0x0041, 0x005A),
			("Latin", 0x0061, 0x007A),
			("Latin", 0x00C0, 0x024F),
			("Latin", 0x1E00, 0x1EFF),
			("Latin", 0x2C60, 0x2C7F),
			("Latin", 0xA720, 0xA7FF),
			("Greek", 0x0370, 0x03FF),
			("Greek", 0x1F00, 0x1FFF),
			("Cyrillic", 0x0400, 0x052F),
			("Cyrillic", 0x2DE0, 0x2DFF),
			("Cyrillic", 0xA640, 0xA69F),
			("CJK Unified", 0x3400, 0x4DBF),
			("CJK Unified", 0x4E00, 0x9FFF),
			("CJK Unified", 0xF900, 0xFAFF),
			("CJK Unified", 0x20000, 0x2FA1F),
			("Punctuation", 0x0021, 0x002F),
			("Punctuation", 0x003A, 0x0040),
			("Punctuation", 0x005B, 0x0060),
			("Punctuation", 0x007B, 0x007E),
			("Punctuation", 0x00A1, 0x00BF),
			("Punctuation", 0x2000, 0x206F),
			("Punctuation", 0x3000, 0x303F),
			("Punctuation", 0xFF01, 0xFF0F),
			("Punctuation", 0xFF1A, 0xFF20),
			("Punctuation", 0xFF3B, 0xFF40),
			("Punctuation", 0xFF5B, 0xFF65)
		};

		/// <summary>把码位归入区块名（查不到返回 "Other"）。</summary>
		public static string Classify(int codepoint)
		{
			foreach ((string name, int start, int end) in Ranges)
			{
				if (codepoint >= start && codepoint <= end)
				{
					return name;
				}
			}
			return "Other";
		}
	}
}
