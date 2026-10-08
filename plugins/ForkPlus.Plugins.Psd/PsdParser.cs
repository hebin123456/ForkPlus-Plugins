using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ForkPlus.Plugins.Psd
{
	/// <summary>
	/// PSD / PSB 结构解析器：按 Adobe Photoshop File Formats Specification 自解析文件头、
	/// 图像资源段（捕获缩略图资源 1036 的 JFIF 数据）与图层记录（矩形 / 通道构成 / 混合模式 /
	/// 不透明度 / 可见性 / Unicode 名 "luni" / 分组标记 "LSct"），全部字段按呈现需求格式化成文本。
	///
	/// 只解析「对比需要的骨架」，不解码像素：图像数据段只读出压缩标记，通道数据一律跳过。
	/// PSB（Large Document Format）与 PSD 的差异只在若干长度字段（u32 → u64）与 Pascal 名对齐
	/// （偶数 → 4 字节倍），由 <c>psb</c> 标志统一分派。
	/// </summary>
	internal static class PsdParser
	{
		/// <summary>解析一份 PSD / PSB 字节；失败返回 null 并给出面向用户的错误说明。</summary>
		public static PsdDocument Parse(byte[] data, out string error)
		{
			error = null;
			try
			{
				return ParseCore(data);
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return null;
			}
		}

		private static PsdDocument ParseCore(byte[] data)
		{
			if (data == null || data.Length < 26)
			{
				throw new InvalidDataException("not a PSD/PSB file (too small)");
			}
			PsdReader reader = new PsdReader(data);
			if (reader.Ascii(4) != "8BPS")
			{
				throw new InvalidDataException("not a PSD/PSB file (missing 8BPS signature)");
			}
			int version = reader.U16();
			if (version != 1 && version != 2)
			{
				throw new InvalidDataException("unsupported PSD version " + version.ToString());
			}
			bool psb = version == 2;
			reader.Skip(6);
			int channels = reader.U16();
			int height = unchecked((int)reader.U32());
			int width = unchecked((int)reader.U32());
			int depth = reader.U16();
			int colorMode = reader.U16();

			PsdDocument document = new PsdDocument
			{
				Format = psb ? "PSB" : "PSD",
				Width = width,
				Height = height,
				Channels = channels,
				Depth = depth,
				ColorModeName = ColorModeName(colorMode)
			};

			// 颜色模式数据段：只有长度需要关心。
			reader.Skip(reader.SectionLength(psb));

			// 图像资源段：逐块走，捕获缩略图（resource 1036，kJpegRGB）。
			ParseResources(reader, psb, document);

			// 图层与蒙版段：段首是 layer info（图层记录），其后是全局蒙版等，整体跳过。
			ParseLayerSection(reader, psb, document);

			// 图像数据段：无长度前缀，段首 2 字节是压缩标记；像素不解码。
			string compression = "unknown";
			if (reader.Remaining >= 2)
			{
				ushort value = reader.U16();
				compression = value == 0 ? "none" : (value == 1 ? "RLE" : "unknown");
			}

			document.Rows.Add(new PsdRow { Path = "width", Value = width.ToString() });
			document.Rows.Add(new PsdRow { Path = "height", Value = height.ToString() });
			document.Rows.Add(new PsdRow { Path = "channels", Value = channels.ToString() });
			document.Rows.Add(new PsdRow { Path = "depth", Value = depth.ToString() });
			document.Rows.Add(new PsdRow { Path = "color mode", Value = document.ColorModeName });
			document.Rows.Add(new PsdRow { Path = "resources", Value = document.ResourceCount.ToString() });
			document.Rows.Add(new PsdRow { Path = "layers", Value = document.Layers.Count.ToString() });
			document.Rows.Add(new PsdRow { Path = "image compression", Value = compression });
			return document;
		}

		// ---- 图像资源段 ----

		private static void ParseResources(PsdReader reader, bool psb, PsdDocument document)
		{
			long sectionLength = reader.SectionLength(psb);
			int sectionEnd = reader.ClampEnd(sectionLength);
			int count = 0;
			while (reader.Position < sectionEnd && reader.Remaining >= 8)
			{
				string signature = reader.Ascii(4);
				if (signature != "8BIM" && signature != "8B64")
				{
					throw new InvalidDataException("corrupt image resource block at offset " + reader.Position.ToString());
				}
				int resourceId = reader.U16();
				int nameLength = reader.U8();
				int nameTotal = PadTo(1 + nameLength, psb ? 4 : 2);
				reader.Skip(nameTotal - 1);
				long dataLength = reader.SectionLength(psb);
				if (dataLength < 0 || reader.Position + dataLength > sectionEnd)
				{
					throw new InvalidDataException("corrupt image resource length for resource " + resourceId.ToString());
				}

				// 缩略图资源 1036：28 字节头（format/尺寸/保留/bits/planes）后是 JFIF 字节流。
				if (resourceId == 1036 && dataLength >= 28)
				{
					int dataStart = reader.Position;
					uint format = reader.U32();
					uint thumbWidth = reader.U32();
					uint thumbHeight = reader.U32();
					reader.Skip(16);
					if (format == 1)
					{
						document.ThumbnailJfif = reader.Bytes((int)(dataLength - 28));
						document.ThumbnailSizeText = thumbWidth.ToString() + "×" + thumbHeight.ToString();
					}
					reader.Seek(dataStart + (int)dataLength);
				}
				else
				{
					reader.Skip(dataLength);
				}
				if ((dataLength & 1L) == 1L)
				{
					reader.Skip(1L);
				}
				count++;
			}
			reader.Seek(sectionEnd);
			document.ResourceCount = count;
		}

		// ---- 图层与蒙版段 ----

		private static void ParseLayerSection(PsdReader reader, bool psb, PsdDocument document)
		{
			long sectionLength = reader.SectionLength(psb);
			int sectionEnd = reader.ClampEnd(sectionLength);
			if (sectionLength > 0 && reader.Position < sectionEnd)
			{
				long layerInfoLength = reader.SectionLength(psb);
				int layerInfoEnd = reader.ClampEnd(layerInfoLength);
				if (layerInfoLength > 0 && reader.Position + 2 <= layerInfoEnd)
				{
					short countRaw = reader.I16();
					int count = Math.Abs(countRaw);
					for (int i = 0; i < count && reader.Position < layerInfoEnd; i++)
					{
						ParseLayer(reader, psb, layerInfoEnd, document);
					}
				}
				reader.Seek(layerInfoEnd);
			}
			reader.Seek(sectionEnd);
		}

		private static void ParseLayer(PsdReader reader, bool psb, int layerInfoEnd, PsdDocument document)
		{
			int top = reader.I32();
			int left = reader.I32();
			int bottom = reader.I32();
			int right = reader.I32();
			int width = right - left;
			int height = bottom - top;
			int channelCount = reader.U16();
			List<int> channelIds = new List<int>(channelCount);
			for (int i = 0; i < channelCount; i++)
			{
				channelIds.Add(reader.I16());
				reader.Skip(psb ? 8L : 4L);
			}
			if (reader.Ascii(4) != "8BIM")
			{
				throw new InvalidDataException("corrupt layer record (blend mode signature)");
			}
			string blendKey = reader.Ascii(4);
			byte opacity = reader.U8();
			reader.U8();
			byte flags = reader.U8();
			reader.U8();
			long extraLength = reader.SectionLength(psb);
			if (extraLength < 0 || reader.Position + extraLength > layerInfoEnd)
			{
				throw new InvalidDataException("corrupt layer extra data length");
			}
			int extraEnd = reader.Position + (int)extraLength;

			PsdLayer layer = new PsdLayer
			{
				RectText = width.ToString() + "×" + height.ToString() + " @ (" + left.ToString() + "," + top.ToString() + ")",
				Blend = blendKey,
				OpacityPercent = (int)Math.Round(opacity / 2.55),
				Visible = (flags & 0x02) == 0,
				ChannelIdsText = ChannelIdsText(channelIds),
				SectionType = PsdSectionType.None
			};

			string pascalName = null;
			if (reader.Position + 8 <= extraEnd)
			{
				// 图层蒙版数据与混合范围：只有长度需要关心。
				reader.Skip(reader.SectionLength(psb));
				if (reader.Position + 8 <= extraEnd)
				{
					reader.Skip(reader.SectionLength(psb));
				}
				// Pascal 图层名（pad 到 4 字节倍）。
				if (reader.Position < extraEnd)
				{
					int nameLength = reader.U8();
					int nameTotal = PadTo(1 + nameLength, 4);
					if (nameLength > 0 && reader.Position + nameLength <= extraEnd)
					{
						pascalName = Encoding.GetEncoding(28591).GetString(reader.Bytes(nameLength));
					}
					reader.Skip(Math.Max(0, nameTotal - 1 - nameLength));
				}
				// additional layer info 块：捕获 "luni"（Unicode 名）与 "LSct"（分组标记）。
				while (reader.Position + 8 <= extraEnd)
				{
					string peek = reader.PeekAscii(4);
					if (peek != "8BIM" && peek != "8B64")
					{
						break;
					}
					reader.Ascii(4);
					string key = reader.Ascii(4);
					long length = reader.SectionLength(psb);
					if (length < 0 || reader.Position + length > extraEnd)
					{
						break;
					}
					int dataStart = reader.Position;
					if (key == "luni" && length >= 4)
					{
						int charCount = reader.I32();
						if (charCount > 0 && reader.Position + (long)charCount * 2L <= dataStart + length)
						{
							layer.Name = ReadUtf16Be(reader.Bytes(charCount * 2));
						}
					}
					else if (key == "LSct" && length >= 4)
					{
						int type = reader.I32();
						layer.SectionType = type == 1 ? PsdSectionType.OpenFolder
							: (type == 2 ? PsdSectionType.ClosedFolder : PsdSectionType.Divider);
					}
					reader.Seek(dataStart + (int)length);
				}
			}
			reader.Seek(extraEnd);
			if (string.IsNullOrEmpty(layer.Name))
			{
				layer.Name = !string.IsNullOrEmpty(pascalName) ? pascalName : ("Layer " + (document.Layers.Count + 1).ToString());
			}
			document.Layers.Add(layer);
		}

		// ---- 小工具 ----

		/// <summary>通道构成摘要：-2 → M（蒙版）、-1 → A（透明度）、0/1/2 → R/G/B，其余用原始 id。</summary>
		private static string ChannelIdsText(List<int> channelIds)
		{
			List<string> tokens = new List<string>(channelIds.Count);
			foreach (int id in channelIds)
			{
				switch (id)
				{
				case -2:
					tokens.Add("M");
					break;
				case -1:
					tokens.Add("A");
					break;
				case 0:
					tokens.Add("R");
					break;
				case 1:
					tokens.Add("G");
					break;
				case 2:
					tokens.Add("B");
					break;
				default:
					tokens.Add(id.ToString());
					break;
				}
			}
			return string.Join("+", tokens);
		}

		private static string ColorModeName(int colorMode)
		{
			switch (colorMode)
			{
			case 0:
				return "Bitmap";
			case 1:
				return "Grayscale";
			case 2:
				return "Indexed";
			case 3:
				return "RGB";
			case 4:
				return "CMYK";
			case 7:
				return "Multichannel";
			case 8:
				return "Duotone";
			case 9:
				return "Lab";
			default:
				return "mode " + colorMode.ToString();
			}
		}

		/// <summary>UTF-16BE 字节序列解成字符串（"luni" 图层名用）。</summary>
		private static string ReadUtf16Be(byte[] bytes)
		{
			if (bytes == null || bytes.Length < 2)
			{
				return null;
			}
			char[] chars = new char[bytes.Length / 2];
			for (int i = 0; i < chars.Length; i++)
			{
				chars[i] = (char)((bytes[i * 2] << 8) | bytes[i * 2 + 1]);
			}
			return new string(chars);
		}

		private static int PadTo(int value, int align)
		{
			return (value + align - 1) / align * align;
		}

		/// <summary>
		/// 大端字节读取器：PSD / PSB 全字段大端；长度越界一律抛 <see cref="InvalidDataException"/>，
		/// 由 <see cref="Parse"/> 统一转成解析失败。
		/// </summary>
		private sealed class PsdReader
		{
			private readonly byte[] _data;

			private int _position;

			public int Position
			{
				get { return _position; }
			}

			public int Remaining
			{
				get { return _data.Length - _position; }
			}

			public PsdReader(byte[] data)
			{
				_data = data;
			}

			private void Ensure(long count)
			{
				if (_position + count > _data.Length)
				{
					throw new InvalidDataException("truncated PSD data at offset " + _position.ToString());
				}
			}

			public byte U8()
			{
				Ensure(1);
				return _data[_position++];
			}

			public ushort U16()
			{
				Ensure(2);
				int value = (_data[_position] << 8) | _data[_position + 1];
				_position += 2;
				return (ushort)value;
			}

			public short I16()
			{
				return unchecked((short)U16());
			}

			public uint U32()
			{
				Ensure(4);
				uint value = ((uint)_data[_position] << 24) | ((uint)_data[_position + 1] << 16)
					| ((uint)_data[_position + 2] << 8) | _data[_position + 3];
				_position += 4;
				return value;
			}

			public int I32()
			{
				return unchecked((int)U32());
			}

			public ulong U64()
			{
				Ensure(8);
				ulong value = 0UL;
				for (int i = 0; i < 8; i++)
				{
					value = (value << 8) | _data[_position + i];
				}
				_position += 8;
				return value;
			}

			/// <summary>PSD 用 u32 / PSB 用 u64 的「节段长度」字段。</summary>
			public long SectionLength(bool psb)
			{
				return psb ? unchecked((long)U64()) : unchecked((long)U32());
			}

			public byte[] Bytes(int count)
			{
				if (count < 0)
				{
					throw new InvalidDataException("negative block size");
				}
				Ensure(count);
				byte[] buffer = new byte[count];
				Buffer.BlockCopy(_data, _position, buffer, 0, count);
				_position += count;
				return buffer;
			}

			public string Ascii(int count)
			{
				return Encoding.ASCII.GetString(Bytes(count));
			}

			/// <summary>窥视 4 字节 ASCII（不移动读指针），用于 additional info 块的签名判定。</summary>
			public string PeekAscii(int count)
			{
				if (_position + count > _data.Length)
				{
					return null;
				}
				return Encoding.ASCII.GetString(_data, _position, count);
			}

			public void Skip(long count)
			{
				if (count < 0)
				{
					throw new InvalidDataException("negative skip");
				}
				Ensure(count);
				_position += (int)count;
			}

			public void Seek(int position)
			{
				if (position < 0 || position > _data.Length)
				{
					throw new InvalidDataException("seek out of range");
				}
				_position = position;
			}

			/// <summary>把「当前位置 + 段长」钳制到文件末尾并校验非负，返回段结束偏移。</summary>
			public int ClampEnd(long sectionLength)
			{
				if (sectionLength < 0)
				{
					throw new InvalidDataException("corrupt section length");
				}
				long end = _position + sectionLength;
				if (end > _data.Length)
				{
					throw new InvalidDataException("section length exceeds file size");
				}
				return (int)end;
			}
		}
	}
}
