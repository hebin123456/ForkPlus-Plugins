using System;
using System.Collections.Generic;

namespace ForkPlus.Plugins.Font
{
	/// <summary>字体容器格式（由 sfnt / WOFF 魔数判定）。</summary>
	internal enum FontFormat
	{
		/// <summary>未识别 / 解析失败。</summary>
		Unknown,

		/// <summary>TrueType 轮廓（魔数 0x00010000 / "true" / "typ1"）。</summary>
		TrueType,

		/// <summary>OpenType（CFF 轮廓，魔数 "OTTO"）。</summary>
		OpenTypeCff,

		/// <summary>TrueType / OpenType 集合（魔数 "ttcf"，一个文件多套字体）。</summary>
		TrueTypeCollection,

		/// <summary>WOFF（sfnt 的 zlib 封装，魔数 "wOFF"）。</summary>
		Woff
	}

	/// <summary>解析失败 / 降级的错误分类。</summary>
	internal enum FontError
	{
		/// <summary>无错误。</summary>
		None,

		/// <summary>字节为空。</summary>
		Empty,

		/// <summary>魔数不是已知字体格式。</summary>
		BadMagic,

		/// <summary>已知但不支持（如 WOFF2）。</summary>
		Unsupported,

		/// <summary>解析过程中出错（结构性损坏）。</summary>
		Failed
	}

	/// <summary>
	/// 一侧字体的解析结果：容器格式 + 各字体套（TTC 多套）的元数据 / 码位覆盖 / 表存在性。
	/// 解析失败时 <see cref="Error"/> 给出可读分类，<see cref="Faces"/> 仍可能含能读出来的部分。
	/// </summary>
	internal sealed class FontModel
	{
		public string Path { get; set; }

		public FontFormat Format { get; set; }

		public FontError Error { get; set; }

		/// <summary>错误细节（面向用户的补充说明，可为空）。</summary>
		public string ErrorDetail { get; set; }

		/// <summary>是否为字体集合（.ttc / .otc，多套字体）。</summary>
		public bool IsCollection { get; set; }

		public List<FontFace> Faces { get; } = new List<FontFace>();

		/// <summary>格式名的译文 key（走 <see cref="FontStrings"/>）。</summary>
		public string FormatKey
		{
			get
			{
				switch (Format)
				{
					case FontFormat.TrueType:
						return "TrueType";
					case FontFormat.OpenTypeCff:
						return "OpenType (CFF)";
					case FontFormat.TrueTypeCollection:
						return "TrueType Collection";
					case FontFormat.Woff:
						return "WOFF";
					default:
						return "Unknown";
				}
			}
		}

		/// <summary>构造一个只有错误分类、没有可用字体套的模型。</summary>
		public static FontModel Failure(string path, FontError error, string detail = null)
		{
			return new FontModel
			{
				Path = path,
				Format = FontFormat.Unknown,
				Error = error,
				ErrorDetail = detail
			};
		}
	}
}
