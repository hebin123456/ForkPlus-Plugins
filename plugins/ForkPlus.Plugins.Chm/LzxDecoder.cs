using System;

namespace ForkPlus.Plugins.Chm
{
	/// <summary>
	/// LZX 解压器（纯托管移植，源自 CHMLib 的 lzx.c；lzx.c 又源自 cabextract / unlzx 的
	/// 解压实现，含 Jed Wing / Stuart Caie / David Tritscher 的贡献与说明）。
	///
	/// 仅实现**解压**，窗口 2^15..2^21，与 CHM 的 MSCompressed 存储完全一致：
	/// 一次 <see cref="Decompress"/> 调用解**一个 LZX 块**（CHM 里通常 32768 字节），
	/// 多次调用间的窗口与重复偏移队列（R0/R1/R2）连续——因此同一「重置区间」内必须
	/// 按块顺序依次调用；到重置边界时调用方须先 <see cref="Reset"/>。
	///
	/// 返回码：0 = 成功；1 = 数据格式错误（窗口溢出）；2 = 非法数据 / 缓冲区耗尽。
	/// 移植时刻意保留 CHMLib 的位级宏语义（LSB-first 位缓冲、Huffman 快查表、
	/// aligned 块、E8 预处理），未做「优化改写」，以最大化与参考实现的一致性。
	/// </summary>
	internal sealed class LzxDecoder
	{
		private const int LzxMinMatch = 2;

		private const int LzxNumChars = 256;

		private const int LzxBlockTypeInvalid = 0;

		private const int LzxBlockTypeVerbatim = 1;

		private const int LzxBlockTypeAligned = 2;

		private const int LzxBlockTypeUncompressed = 3;

		private const int LzxPreTreeNumElements = 20;

		private const int LzxNumPrimaryLengths = 7;

		private const int LzxNumSecondaryLengths = 249;

		private const int PreTreeTableBits = 6;

		private const int MainTreeTableBits = 12;

		private const int LengthTableBits = 12;

		private const int AlignedTableBits = 7;

		private const int PreTreeMaxSymbols = LzxPreTreeNumElements;

		private const int MainTreeMaxSymbols = LzxNumChars + 50 * 8;

		private const int LengthMaxSymbols = LzxNumSecondaryLengths + 1;

		private const int AlignedMaxSymbols = 8;

		/// <summary>长度表解码允许的越界余量（与 CHMLib 的 LZX_LENTABLE_SAFETY 一致）。</summary>
		private const int LenTableSafety = 64;

		/// <summary>位置槽的额外位数表（索引 = 位置槽号）。</summary>
		private static readonly byte[] ExtraBits = new byte[51]
		{
			0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
			7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14,
			15, 15, 16, 16, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17,
			17, 17, 17
		};

		/// <summary>位置槽基址表（索引 = 位置槽号）。</summary>
		private static readonly uint[] PositionBase = new uint[51]
		{
			0, 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192,
			256, 384, 512, 768, 1024, 1536, 2048, 3072, 4096, 6144, 8192, 12288, 16384, 24576, 32768, 49152,
			65536, 98304, 131072, 196608, 262144, 393216, 524288, 655360, 786432, 917504, 1048576, 1179648, 1310720, 1441792, 1572864, 1703936,
			1835008, 1966080, 2097152
		};

		private readonly byte[] _window;

		private readonly uint _windowSize;

		private readonly ushort _mainElements;

		private uint _windowPosn;

		private uint _r0 = 1;

		private uint _r1 = 1;

		private uint _r2 = 1;

		private bool _headerRead;

		private int _blockType = LzxBlockTypeInvalid;

		private uint _blockLength;

		private uint _blockRemaining;

		private uint _framesRead;

		private int _intelFilesize;

		private int _intelCurpos;

		private bool _intelStarted;

		private readonly ushort[] _preTreeTable = new ushort[(1 << PreTreeTableBits) + (PreTreeMaxSymbols << 1)];

		private readonly byte[] _preTreeLen = new byte[PreTreeMaxSymbols + LenTableSafety];

		private readonly ushort[] _mainTreeTable = new ushort[(1 << MainTreeTableBits) + (MainTreeMaxSymbols << 1)];

		private readonly byte[] _mainTreeLen = new byte[MainTreeMaxSymbols + LenTableSafety];

		private readonly ushort[] _lengthTable = new ushort[(1 << LengthTableBits) + (LengthMaxSymbols << 1)];

		private readonly byte[] _lengthLen = new byte[LengthMaxSymbols + LenTableSafety];

		private readonly ushort[] _alignedTable = new ushort[(1 << AlignedTableBits) + (AlignedMaxSymbols << 1)];

		private readonly byte[] _alignedLen = new byte[AlignedMaxSymbols + LenTableSafety];

		/// <summary>按窗口位数（15..21）建解压器；<paramref name="windowBits"/> 由 CHM 控制块的 windowSize 推出。</summary>
		internal LzxDecoder(int windowBits)
		{
			if (windowBits < 15 || windowBits > 21)
			{
				throw new ArgumentOutOfRangeException(nameof(windowBits));
			}
			_windowSize = 1u << windowBits;
			_window = new byte[_windowSize];

			int posnSlots;
			if (windowBits == 20)
			{
				posnSlots = 42;
			}
			else if (windowBits == 21)
			{
				posnSlots = 50;
			}
			else
			{
				posnSlots = windowBits << 1;
			}
			_mainElements = (ushort)(LzxNumChars + (posnSlots << 3));

			Reset();
		}

		/// <summary>重置流状态（CHM 每个重置区间开始时调用）。</summary>
		internal void Reset()
		{
			_r0 = 1;
			_r1 = 1;
			_r2 = 1;
			_headerRead = false;
			_blockRemaining = 0;
			_blockType = LzxBlockTypeInvalid;
			_framesRead = 0;
			_intelCurpos = 0;
			_intelStarted = false;
			_windowPosn = 0;
			Array.Clear(_mainTreeLen, 0, _mainTreeLen.Length);
			Array.Clear(_lengthLen, 0, _lengthLen.Length);
		}

		/// <summary>
		/// 解压一个 LZX 块。<paramref name="input"/> 需保证从 <paramref name="inStart"/> 起
		/// 到 <c>inStart+inLen</c> 之外仍有至少 16 字节可读（位缓冲会前视读取）。
		/// 成功返回 0。
		/// </summary>
		internal int Decompress(byte[] input, int inStart, int inLen, byte[] output, int outStart, int outLen)
		{
			int inpos = inStart;
			int endinp = inStart + inLen;
			uint bitbuf = 0;
			int bitsleft = 0;

			byte[] window = _window;
			uint windowSize = _windowSize;
			uint windowPosn = _windowPosn;
			uint r0 = _r0;
			uint r1 = _r1;
			uint r2 = _r2;

			int togo = outLen;

			if (!_headerRead)
			{
				uint k;
				uint i = 0;
				uint j = 0;
				ReadBits(input, ref inpos, ref bitbuf, ref bitsleft, 1, out k);
				if (k != 0)
				{
					ReadBits(input, ref inpos, ref bitbuf, ref bitsleft, 16, out i);
					ReadBits(input, ref inpos, ref bitbuf, ref bitsleft, 16, out j);
				}
				_intelFilesize = (int)((i << 16) | j);
				_headerRead = true;
			}

			while (togo > 0)
			{
				if (_blockRemaining == 0)
				{
					if (_blockType == LzxBlockTypeUncompressed)
					{
						if ((_blockLength & 1) != 0)
						{
							inpos++;
						}
						bitbuf = 0;
						bitsleft = 0;
					}

					uint blockType;
					uint hi;
					uint lo;
					ReadBits(input, ref inpos, ref bitbuf, ref bitsleft, 3, out blockType);
					ReadBits(input, ref inpos, ref bitbuf, ref bitsleft, 16, out hi);
					ReadBits(input, ref inpos, ref bitbuf, ref bitsleft, 8, out lo);
					_blockType = (int)blockType;
					_blockRemaining = _blockLength = (hi << 8) | lo;

					switch (_blockType)
					{
					case LzxBlockTypeAligned:
						for (int idx = 0; idx < 8; idx++)
						{
							uint v;
							ReadBits(input, ref inpos, ref bitbuf, ref bitsleft, 3, out v);
							_alignedLen[idx] = (byte)v;
						}
						if (!MakeDecodeTable(AlignedMaxSymbols, AlignedTableBits, _alignedLen, _alignedTable))
						{
							return 2;
						}
						goto case LzxBlockTypeVerbatim;
					case LzxBlockTypeVerbatim:
						if (!ReadLengths(_mainTreeLen, 0, LzxNumChars, input, ref inpos, ref bitbuf, ref bitsleft)
							|| !ReadLengths(_mainTreeLen, LzxNumChars, _mainElements, input, ref inpos, ref bitbuf, ref bitsleft))
						{
							return 2;
						}
						if (!MakeDecodeTable(MainTreeMaxSymbols, MainTreeTableBits, _mainTreeLen, _mainTreeTable))
						{
							return 2;
						}
						if (_mainTreeLen[0xE8] != 0)
						{
							_intelStarted = true;
						}
						if (!ReadLengths(_lengthLen, 0, LzxNumSecondaryLengths, input, ref inpos, ref bitbuf, ref bitsleft)
							|| !MakeDecodeTable(LengthMaxSymbols, LengthTableBits, _lengthLen, _lengthTable))
						{
							return 2;
						}
						break;
					case LzxBlockTypeUncompressed:
						_intelStarted = true;
						EnsureBits(input, ref inpos, ref bitbuf, ref bitsleft, 16);
						if (bitsleft > 16)
						{
							inpos -= 2;
						}
						r0 = ReadLe32(input, ref inpos);
						r1 = ReadLe32(input, ref inpos);
						r2 = ReadLe32(input, ref inpos);
						break;
					default:
						return 2;
					}
				}

				if (inpos > endinp)
				{
					if (inpos > endinp + 2 || bitsleft < 16)
					{
						return 2;
					}
				}

				int thisRun;
				while ((thisRun = (int)_blockRemaining) > 0 && togo > 0)
				{
					if (thisRun > togo)
					{
						thisRun = togo;
					}
					togo -= thisRun;
					_blockRemaining -= (uint)thisRun;

					windowPosn &= windowSize - 1;
					if (windowPosn + thisRun > windowSize)
					{
						return 1;
					}

					if (_blockType == LzxBlockTypeVerbatim || _blockType == LzxBlockTypeAligned)
					{
						bool aligned = _blockType == LzxBlockTypeAligned;
						while (thisRun > 0)
						{
							int mainElement;
							if (!ReadHuffSym(_mainTreeTable, _mainTreeLen, MainTreeMaxSymbols, MainTreeTableBits, input, ref inpos, ref bitbuf, ref bitsleft, out mainElement))
							{
								return 2;
							}
							if (mainElement < LzxNumChars)
							{
								window[windowPosn++] = (byte)mainElement;
								thisRun--;
								continue;
							}

							mainElement -= LzxNumChars;
							int matchLength = mainElement & LzxNumPrimaryLengths;
							if (matchLength == LzxNumPrimaryLengths)
							{
								int footer;
								if (!ReadHuffSym(_lengthTable, _lengthLen, LengthMaxSymbols, LengthTableBits, input, ref inpos, ref bitbuf, ref bitsleft, out footer))
								{
									return 2;
								}
								matchLength += footer;
							}
							matchLength += LzxMinMatch;

							int matchOffset = mainElement >> 3;
							if (matchOffset > 2)
							{
								uint extra = ExtraBits[matchOffset];
								if (aligned)
								{
									matchOffset = (int)PositionBase[matchOffset] - 2;
									if (extra > 3)
									{
										uint verbatimBits;
										ReadBits(input, ref inpos, ref bitbuf, ref bitsleft, (int)extra - 3, out verbatimBits);
										matchOffset += (int)(verbatimBits << 3);
										int alignedBits;
										if (!ReadHuffSym(_alignedTable, _alignedLen, AlignedMaxSymbols, AlignedTableBits, input, ref inpos, ref bitbuf, ref bitsleft, out alignedBits))
										{
											return 2;
										}
										matchOffset += alignedBits;
									}
									else if (extra == 3)
									{
										int alignedBits;
										if (!ReadHuffSym(_alignedTable, _alignedLen, AlignedMaxSymbols, AlignedTableBits, input, ref inpos, ref bitbuf, ref bitsleft, out alignedBits))
										{
											return 2;
										}
										matchOffset += alignedBits;
									}
									else if (extra > 0)
									{
										uint verbatimBits;
										ReadBits(input, ref inpos, ref bitbuf, ref bitsleft, (int)extra, out verbatimBits);
										matchOffset += (int)verbatimBits;
									}
									else
									{
										matchOffset = 1;
									}
								}
								else
								{
									if (matchOffset != 3)
									{
										uint verbatimBits;
										ReadBits(input, ref inpos, ref bitbuf, ref bitsleft, (int)extra, out verbatimBits);
										matchOffset = (int)PositionBase[matchOffset] - 2 + (int)verbatimBits;
									}
									else
									{
										matchOffset = 1;
									}
								}
								r2 = r1;
								r1 = r0;
								r0 = (uint)matchOffset;
							}
							else if (matchOffset == 0)
							{
								matchOffset = (int)r0;
							}
							else if (matchOffset == 1)
							{
								matchOffset = (int)r1;
								r1 = r0;
								r0 = (uint)matchOffset;
							}
							else
							{
								matchOffset = (int)r2;
								r2 = r0;
								r0 = (uint)matchOffset;
							}

							int rundest = (int)windowPosn;
							int runsrc = rundest - matchOffset;
							windowPosn += (uint)matchLength;
							if (windowPosn > windowSize)
							{
								return 2;
							}
							thisRun -= matchLength;

							while (runsrc < 0 && matchLength-- > 0)
							{
								window[rundest++] = window[runsrc + (int)windowSize];
								runsrc++;
							}
							while (matchLength-- > 0)
							{
								window[rundest++] = window[runsrc++];
							}
						}
					}
					else
					{
						if (inpos + thisRun > endinp)
						{
							return 2;
						}
						Array.Copy(input, inpos, window, (int)windowPosn, thisRun);
						inpos += thisRun;
						windowPosn += (uint)thisRun;
					}
				}
			}

			if (togo != 0)
			{
				return 2;
			}

			int copyStart = (int)((windowPosn == 0 ? windowSize : windowPosn) - (uint)outLen);
			Array.Copy(window, copyStart, output, outStart, outLen);

			_windowPosn = windowPosn;
			_r0 = r0;
			_r1 = r1;
			_r2 = r2;

			ApplyIntelE8(output, outStart, outLen);
			return 0;
		}

		/// <summary>Intel E8 调用转换（LZX 的预处理逆变换）。</summary>
		private void ApplyIntelE8(byte[] output, int outStart, int outLen)
		{
			if (_framesRead++ >= 32768 || _intelFilesize == 0)
			{
				return;
			}
			if (outLen <= 6 || !_intelStarted)
			{
				_intelCurpos += outLen;
				return;
			}
			int data = outStart;
			int dataend = outStart + outLen - 10;
			int curpos = _intelCurpos;
			int filesize = _intelFilesize;
			_intelCurpos = curpos + outLen;

			while (data < dataend)
			{
				if (output[data++] != 0xE8)
				{
					curpos++;
					continue;
				}
				int absOff = output[data] | (output[data + 1] << 8) | (output[data + 2] << 16) | (output[data + 3] << 24);
				if (absOff >= -curpos && absOff < filesize)
				{
					int relOff = absOff >= 0 ? absOff - curpos : absOff + filesize;
					output[data] = (byte)relOff;
					output[data + 1] = (byte)(relOff >> 8);
					output[data + 2] = (byte)(relOff >> 16);
					output[data + 3] = (byte)(relOff >> 24);
				}
				data += 4;
				curpos += 5;
			}
		}

		// ---- 位流 ----

		private static void EnsureBits(byte[] input, ref int inpos, ref uint bitbuf, ref int bitsleft, int n)
		{
			while (bitsleft < n)
			{
				uint pair = (uint)((input[inpos + 1] << 8) | input[inpos]);
				bitbuf |= pair << (32 - 16 - bitsleft);
				bitsleft += 16;
				inpos += 2;
			}
		}

		private static void ReadBits(byte[] input, ref int inpos, ref uint bitbuf, ref int bitsleft, int n, out uint value)
		{
			EnsureBits(input, ref inpos, ref bitbuf, ref bitsleft, n);
			value = n == 0 ? 0u : bitbuf >> (32 - n);
			bitbuf <<= n;
			bitsleft -= n;
		}

		private static uint ReadLe32(byte[] input, ref int inpos)
		{
			uint value = (uint)(input[inpos] | (input[inpos + 1] << 8) | (input[inpos + 2] << 16) | (input[inpos + 3] << 24));
			inpos += 4;
			return value;
		}

		private static bool ReadHuffSym(ushort[] table, byte[] len, int maxSymbols, int tableBits, byte[] input, ref int inpos, ref uint bitbuf, ref int bitsleft, out int symbol)
		{
			EnsureBits(input, ref inpos, ref bitbuf, ref bitsleft, 16);
			int i = table[bitbuf >> (32 - tableBits)];
			if (i >= maxSymbols)
			{
				uint j = 1u << (32 - tableBits);
				while (true)
				{
					j >>= 1;
					i <<= 1;
					i |= (bitbuf & j) != 0 ? 1 : 0;
					if (j == 0)
					{
						symbol = 0;
						return false;
					}
					i = table[i];
					if (i < maxSymbols)
					{
						break;
					}
				}
			}
			symbol = i;
			int codeLen = len[i];
			bitbuf <<= codeLen;
			bitsleft -= codeLen;
			return true;
		}

		// ---- Huffman ----

		private static bool MakeDecodeTable(uint nsyms, int nbits, byte[] length, ushort[] table)
		{
			uint pos = 0;
			uint tableMask = 1u << nbits;
			uint bitMask = tableMask >> 1;
			uint nextSymbol = bitMask;
			int bitNum = 1;

			while (bitNum <= nbits)
			{
				for (uint sym = 0; sym < nsyms; sym++)
				{
					if (length[sym] == bitNum)
					{
						uint leaf = pos;
						pos += bitMask;
						if (pos > tableMask)
						{
							return false;
						}
						uint fill = bitMask;
						while (fill-- > 0)
						{
							table[leaf++] = (ushort)sym;
						}
					}
				}
				bitMask >>= 1;
				bitNum++;
			}

			if (pos != tableMask)
			{
				for (uint sym = pos; sym < tableMask; sym++)
				{
					table[sym] = 0;
				}

				pos <<= 16;
				tableMask <<= 16;
				bitMask = 1u << 15;

				while (bitNum <= 16)
				{
					for (uint sym = 0; sym < nsyms; sym++)
					{
						if (length[sym] == bitNum)
						{
							uint leaf = pos >> 16;
							for (uint fill = 0; fill < (uint)(bitNum - nbits); fill++)
							{
								if (table[leaf] == 0)
								{
									table[nextSymbol << 1] = 0;
									table[(nextSymbol << 1) + 1] = 0;
									table[leaf] = (ushort)nextSymbol;
									nextSymbol++;
								}
								leaf = (uint)(table[leaf] << 1);
								if (((pos >> (15 - (int)fill)) & 1) != 0)
								{
									leaf++;
								}
							}
							table[leaf] = (ushort)sym;
							pos += bitMask;
							if (pos > tableMask)
							{
								return false;
							}
						}
					}
					bitMask >>= 1;
					bitNum++;
				}
			}

			if (pos == tableMask)
			{
				return true;
			}
			for (uint sym = 0; sym < nsyms; sym++)
			{
				if (length[sym] != 0)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>读取 [first, last) 段的码长（LZX 特有的差分 + 游程编码）。</summary>
		private bool ReadLengths(byte[] lens, int first, int last, byte[] input, ref int inpos, ref uint bitbuf, ref int bitsleft)
		{
			for (int x = 0; x < LzxPreTreeNumElements; x++)
			{
				uint y;
				ReadBits(input, ref inpos, ref bitbuf, ref bitsleft, 4, out y);
				_preTreeLen[x] = (byte)y;
			}
			if (!MakeDecodeTable(PreTreeMaxSymbols, PreTreeTableBits, _preTreeLen, _preTreeTable))
			{
				return false;
			}

			for (int x = first; x < last;)
			{
				int z;
				if (!ReadHuffSym(_preTreeTable, _preTreeLen, PreTreeMaxSymbols, PreTreeTableBits, input, ref inpos, ref bitbuf, ref bitsleft, out z))
				{
					return false;
				}
				if (z == 17)
				{
					uint y;
					ReadBits(input, ref inpos, ref bitbuf, ref bitsleft, 4, out y);
					y += 4;
					while (y-- > 0)
					{
						lens[x++] = 0;
					}
				}
				else if (z == 18)
				{
					uint y;
					ReadBits(input, ref inpos, ref bitbuf, ref bitsleft, 5, out y);
					y += 20;
					while (y-- > 0)
					{
						lens[x++] = 0;
					}
				}
				else if (z == 19)
				{
					uint y;
					ReadBits(input, ref inpos, ref bitbuf, ref bitsleft, 1, out y);
					y += 4;
					int z2;
					if (!ReadHuffSym(_preTreeTable, _preTreeLen, PreTreeMaxSymbols, PreTreeTableBits, input, ref inpos, ref bitbuf, ref bitsleft, out z2))
					{
						return false;
					}
					z = lens[x] - z2;
					if (z < 0)
					{
						z += 17;
					}
					while (y-- > 0)
					{
						lens[x++] = (byte)z;
					}
				}
				else
				{
					z = lens[x] - z;
					if (z < 0)
					{
						z += 17;
					}
					lens[x++] = (byte)z;
				}
			}
			return true;
		}
	}
}