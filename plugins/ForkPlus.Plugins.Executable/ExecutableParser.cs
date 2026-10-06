using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace ForkPlus.Plugins.Executable
{
	/// <summary>
	/// 二进制结构解析器：按魔数判定格式并抽取「结构摘要 / 节段表 / 导入导出 / 体积构成」所需数据。
	///
	/// PE 优先用共享框架内置的 <see cref="PEReader"/> 读 COFF / 可选头 / 节表 / .NET 元数据，
	/// 导入表与导出表（<see cref="PEReader"/> 未暴露）手动解析；PEReader 抛错时退化为手动读 COFF
	/// 头与节表。ELF / Mach-O / WebAssembly / ar 全部自解析。所有偏移与长度都做边界校验，
	/// 任何异常都在本层吞掉并降级为可读结果（<see cref="ExecutableModel.Error"/> /
	/// <see cref="ExecutableModel.Warning"/>），绝不冒泡到宿主。
	///
	/// 已知限制：版本化 .so（如 libfoo.so.1.2.3）扩展名是 .3，宿主按扩展名路由不到本插件，
	/// 插件侧无法解决。
	/// </summary>
	internal static class ExecutableParser
	{
		/// <summary>各类表格 / 符号列表的条目上限，防止畸形输入造成超长循环或超大列表。</summary>
		private const int MaxListEntries = 20000;

		/// <summary>节段表条目上限。</summary>
		private const int MaxSections = 4096;

		/// <summary>字符串长度上限（导入 / 导出名、段名等）。</summary>
		private const int MaxStringLength = 512;

		// ---- 入口 ----

		/// <summary>解析一侧二进制；永不抛异常，失败降级为带错误分类的模型。</summary>
		public static ExecutableModel Parse(string path, byte[] bytes)
		{
			if (bytes == null || bytes.Length == 0)
			{
				return ExecutableModel.Failed(ExecutableFormat.Unknown, ExecutableError.Unsupported, null, 0L);
			}
			try
			{
				Buf buf = new Buf(bytes);
				if (IsElf(buf))
				{
					return ParseElf(buf);
				}
				if (IsPe(buf))
				{
					return ParsePe(buf);
				}
				if (IsMachO(buf))
				{
					return ParseMachO(buf);
				}
				if (IsWasm(buf))
				{
					return ParseWasm(buf);
				}
				if (IsAr(buf))
				{
					return ParseAr(buf);
				}
				return ExecutableModel.Failed(ExecutableFormat.Unknown, ExecutableError.Unsupported, null, bytes.Length);
			}
			catch (Exception ex)
			{
				// 兜底：任何意外都降级为「格式未知 / 损坏」，绝不让异常冒泡。
				return ExecutableModel.Failed(ExecutableFormat.Unknown, ExecutableError.Corrupt, ex.Message, bytes.Length);
			}
		}

		/// <summary>格式名的英文原文 key（视图徽章经 ExecutableStrings.T 取译文）。</summary>
		public static string FormatName(ExecutableFormat format)
		{
			switch (format)
			{
				case ExecutableFormat.PE:
					return "PE";
				case ExecutableFormat.ELF:
					return "ELF";
				case ExecutableFormat.MachO:
					return "Mach-O";
				case ExecutableFormat.WebAssembly:
					return "WebAssembly";
				case ExecutableFormat.Ar:
					return "ar";
				default:
					return "Unknown format";
			}
		}

		// ---- 魔数判定 ----

		private static bool IsElf(Buf b)
		{
			return b.Length >= 16 && b.U8(0) == 0x7F && b.U8(1) == (byte)'E' && b.U8(2) == (byte)'L' && b.U8(3) == (byte)'F';
		}

		private static bool IsPe(Buf b)
		{
			if (b.Length < 64 || b.U8(0) != (byte)'M' || b.U8(1) != (byte)'Z')
			{
				return false;
			}
			try
			{
				long peOffset = b.U32(0x3C);
				return b.Has(peOffset, 4) && b.U32(peOffset) == 0x00004550u;
			}
			catch (InvalidDataException)
			{
				return false;
			}
		}

		private static bool IsMachO(Buf b)
		{
			if (b.Length < 4)
			{
				return false;
			}
			byte m0 = b.U8(0);
			byte m1 = b.U8(1);
			byte m2 = b.U8(2);
			byte m3 = b.U8(3);
			return (m0 == 0xFE && m1 == 0xED && m2 == 0xFA && m3 == 0xCE)
				|| (m0 == 0xCE && m1 == 0xFA && m2 == 0xED && m3 == 0xFE)
				|| (m0 == 0xFE && m1 == 0xED && m2 == 0xFA && m3 == 0xCF)
				|| (m0 == 0xCF && m1 == 0xFA && m2 == 0xED && m3 == 0xFE)
				|| (m0 == 0xCA && m1 == 0xFE && m2 == 0xBA && m3 == 0xBE)
				|| (m0 == 0xBE && m1 == 0xBA && m2 == 0xFE && m3 == 0xCA)
				|| (m0 == 0xCA && m1 == 0xFE && m2 == 0xBA && m3 == 0xBF)
				|| (m0 == 0xBF && m1 == 0xBA && m2 == 0xFE && m3 == 0xCA);
		}

		private static bool IsWasm(Buf b)
		{
			return b.Length >= 8 && b.U8(0) == 0x00 && b.U8(1) == 0x61 && b.U8(2) == 0x73 && b.U8(3) == 0x6D;
		}

		private static bool IsAr(Buf b)
		{
			return b.Length >= 8 && b.U8(0) == (byte)'!' && b.U8(1) == (byte)'<' && b.U8(2) == (byte)'a'
				&& b.U8(3) == (byte)'r' && b.U8(4) == (byte)'c' && b.U8(5) == (byte)'h' && b.U8(6) == (byte)'>' && b.U8(7) == 0x0A;
		}

		// ====================================================================
		// PE / COFF
		// ====================================================================

		private readonly struct PeSectionInfo
		{
			public PeSectionInfo(long virtualAddress, long virtualSize, long rawPointer, long rawSize)
			{
				VirtualAddress = virtualAddress;
				VirtualSize = virtualSize;
				RawPointer = rawPointer;
				RawSize = rawSize;
			}

			public readonly long VirtualAddress;

			public readonly long VirtualSize;

			public readonly long RawPointer;

			public readonly long RawSize;
		}

		private static ExecutableModel ParsePe(Buf buf)
		{
			Builder builder = new Builder(ExecutableFormat.PE, buf.Length);
			List<PeSectionInfo> sections = new List<PeSectionInfo>();
			bool is64 = false;

			// 第一路：PEReader（受信任输入的官方解析；对不可信输入不安全，故整体 try/catch）。
			try
			{
				using (MemoryStream stream = new MemoryStream(buf.Raw, false))
				using (PEReader pe = new PEReader(stream, PEStreamOptions.PrefetchEntireImage))
				{
					PEHeaders headers = pe.PEHeaders;
					CoffHeader coff = headers.CoffHeader;
					PEHeader optional = headers.PEHeader;
					is64 = optional != null && optional.Magic == PEMagic.PE32Plus;

					builder.AddField("Machine", MachineName(coff.Machine) + " (0x" + ((int)coff.Machine).ToString("X4") + ")");
					builder.AddField("Bits", is64 ? "64-bit" : "32-bit");
					builder.AddField("Flags", "0x" + ((int)coff.Characteristics).ToString("X4"));
					if (optional != null)
					{
						builder.AddField("Subsystem", SubsystemName(optional.Subsystem));
						builder.AddField("DllCharacteristics", "0x" + ((int)optional.DllCharacteristics).ToString("X4"), SecurityBadges(optional.DllCharacteristics));
					}
					foreach (SectionHeader section in headers.SectionHeaders)
					{
						if (builder.SectionCount >= MaxSections)
						{
							break;
						}
						string name = TrimName(section.Name, 8);
						Perms(section.SectionCharacteristics, out bool read, out bool write, out bool execute);
						builder.AddSection(new ExecSection(name, section.SizeOfRawData, read, write, execute));
						sections.Add(new PeSectionInfo(section.VirtualAddress, section.VirtualSize, section.PointerToRawData, section.SizeOfRawData));
					}
					if (pe.HasMetadata && headers.CorHeader != null)
					{
						try
						{
							ReadDotNetMetadata(pe, builder);
						}
						catch (Exception ex)
						{
							builder.NoteError(ex.Message);
						}
					}
				}
			}
			catch (Exception ex)
			{
				builder.NoteError(ex.Message);
			}

			// 第二路：PEReader 失败或无节表时，手动读 COFF 头 / 节表兜底。
			if (builder.SectionCount == 0)
			{
				try
				{
					ManualPeSections(buf, builder, sections, ref is64);
				}
				catch (Exception ex)
				{
					builder.NoteError(ex.Message);
				}
			}

			// 导入 / 导出表：PEReader 未暴露，手动解析（独立 try/catch，失败不影响其余）。
			try
			{
				ReadPeImports(buf, builder, sections, is64);
			}
			catch (Exception ex)
			{
				builder.NoteError(ex.Message);
			}
			try
			{
				ReadPeExports(buf, builder, sections);
			}
			catch (Exception ex)
			{
				builder.NoteError(ex.Message);
			}
			return builder.Build();
		}

		private static void ManualPeSections(Buf buf, Builder builder, List<PeSectionInfo> sections, ref bool is64)
		{
			long peOffset = buf.U32(0x3C);
			if (!buf.Has(peOffset, 24) || buf.U32(peOffset) != 0x00004550u)
			{
				throw new InvalidDataException("invalid PE signature");
			}
			ushort machine = buf.U16(peOffset + 4);
			ushort sectionCount = buf.U16(peOffset + 6);
			ushort optionalSize = buf.U16(peOffset + 20);
			long optionalOffset = peOffset + 24;
			if (optionalSize >= 2)
			{
				ushort magic = buf.U16(optionalOffset);
				is64 = magic == 0x020B;
			}
			builder.AddField("Machine", MachineName((Machine)machine) + " (0x" + machine.ToString("X4") + ")");
			builder.AddField("Bits", is64 ? "64-bit" : "32-bit");
			long sectionOffset = optionalOffset + optionalSize;
			int count = sectionCount;
			if (count > MaxSections)
			{
				count = MaxSections;
			}
			for (int i = 0; i < count; i++)
			{
				long entry = sectionOffset + i * 40L;
				string name = TrimName(buf.Ascii(entry, 8), 8);
				int virtualSize = (int)buf.U32(entry + 8);
				int virtualAddress = (int)buf.U32(entry + 12);
				int rawSize = (int)buf.U32(entry + 16);
				int rawPointer = (int)buf.U32(entry + 20);
				uint characteristics = buf.U32(entry + 36);
				Perms((SectionCharacteristics)characteristics, out bool read, out bool write, out bool execute);
				builder.AddSection(new ExecSection(name, rawSize, read, write, execute));
				sections.Add(new PeSectionInfo(virtualAddress, virtualSize, rawPointer, rawSize));
			}
		}

		private static void ReadDotNetMetadata(PEReader pe, Builder builder)
		{
			MetadataReader md = pe.GetMetadataReader();
			if (md.IsAssembly)
			{
				AssemblyDefinition assembly = md.GetAssemblyDefinition();
				string assemblyName = md.GetString(assembly.Name);
				builder.AddField("Assembly", assemblyName + " " + (assembly.Version?.ToString() ?? string.Empty));
				// 目标框架经自定义特性读取不可靠，按设计文档退化为运行时（元数据）版本。
				builder.AddField("Runtime", md.MetadataVersion ?? string.Empty);
				builder.AddField("Types", md.TypeDefinitions.Count.ToString());
				builder.AddField("Methods", md.MethodDefinitions.Count.ToString());
			}
			foreach (AssemblyReferenceHandle handle in md.AssemblyReferences)
			{
				if (builder.AssemblyRefCount >= MaxListEntries)
				{
					break;
				}
				AssemblyReference reference = md.GetAssemblyReference(handle);
				builder.AddAssemblyRef(new ExecAssemblyRef(md.GetString(reference.Name), reference.Version?.ToString() ?? string.Empty));
			}
		}

		/// <summary>PE 导入表：IMAGE_IMPORT_DESCRIPTOR 数组 → 每个依赖 DLL 及其函数名 / 序号。</summary>
		private static void ReadPeImports(Buf buf, Builder builder, List<PeSectionInfo> sections, bool is64)
		{
			long importRva = ReadDataDirectoryRva(buf, is64, 1);
			if (importRva == 0L)
			{
				return;
			}
			long descriptor = RvaToOffset(sections, importRva);
			for (int i = 0; i < 1024; i++)
			{
				long entry = descriptor + i * 20L;
				uint originalFirstThunk = buf.U32(entry);
				uint nameRva = buf.U32(entry + 12);
				uint firstThunk = buf.U32(entry + 16);
				if (originalFirstThunk == 0u && nameRva == 0u && firstThunk == 0u)
				{
					break;
				}
				string dll = ReadAsciiZ(buf, RvaToOffset(sections, nameRva));
				List<string> symbols = new List<string>();
				long thunkRva = originalFirstThunk != 0u ? originalFirstThunk : firstThunk;
				long thunk = RvaToOffset(sections, thunkRva);
				int step = is64 ? 8 : 4;
				for (int j = 0; j < 8192; j++)
				{
					ulong value = is64 ? buf.U64(thunk + j * (long)step) : buf.U32(thunk + j * (long)step);
					if (value == 0uL)
					{
						break;
					}
					ulong highBit = is64 ? 0x8000000000000000uL : 0x80000000uL;
					if ((value & highBit) != 0uL)
					{
						symbols.Add("#" + (value & 0xFFFFuL).ToString());
					}
					else
					{
						long nameOffset = RvaToOffset(sections, (uint)value);
						symbols.Add(ReadAsciiZ(buf, nameOffset + 2));
					}
				}
				builder.AddDependency(new ExecDependency(dll, symbols));
				if (builder.DependencyCount >= MaxListEntries)
				{
					break;
				}
			}
		}

		/// <summary>PE 导出表：IMAGE_EXPORT_DIRECTORY → 名称数组（无名导出以 #序号 呈现）。</summary>
		private static void ReadPeExports(Buf buf, Builder builder, List<PeSectionInfo> sections)
		{
			long exportRva = ReadDataDirectoryRva(buf, false, 0);
			if (exportRva == 0L)
			{
				return;
			}
			long directory = RvaToOffset(sections, exportRva);
			uint ordinalBase = buf.U32(directory + 16);
			uint numberOfNames = buf.U32(directory + 24);
			uint addressOfNames = buf.U32(directory + 32);
			uint addressOfOrdinals = buf.U32(directory + 36);
			long names = RvaToOffset(sections, addressOfNames);
			long ordinals = RvaToOffset(sections, addressOfOrdinals);
			int count = (int)Math.Min((long)numberOfNames, MaxListEntries);
			for (int i = 0; i < count; i++)
			{
				uint nameRva = buf.U32(names + i * 4L);
				string name = ReadAsciiZ(buf, RvaToOffset(sections, nameRva));
				ushort ordinal = buf.U16(ordinals + i * 2L);
				string display = string.IsNullOrEmpty(name) ? "#" + (ordinalBase + ordinal).ToString() : name;
				builder.AddExport(display);
			}
		}

		/// <summary>读可选头里的数据目录 RVA（PE32 目录起点 96，PE32+ 112）。</summary>
		private static long ReadDataDirectoryRva(Buf buf, bool is64, int index)
		{
			long peOffset = buf.U32(0x3C);
			long directoryOffset = peOffset + 24 + (is64 ? 112 : 96) + index * 8L;
			return buf.U32(directoryOffset);
		}

		private static long RvaToOffset(List<PeSectionInfo> sections, long rva)
		{
			foreach (PeSectionInfo section in sections)
			{
				long span = Math.Max(section.VirtualSize, section.RawSize);
				if (rva >= section.VirtualAddress && rva < section.VirtualAddress + span)
				{
					return section.RawPointer + (rva - section.VirtualAddress);
				}
			}
			// 落在头部区域时 RVA 与文件偏移一致。
			return rva;
		}

		private static void Perms(SectionCharacteristics characteristics, out bool read, out bool write, out bool execute)
		{
			write = (characteristics & SectionCharacteristics.MemWrite) != 0;
			execute = (characteristics & SectionCharacteristics.MemExecute) != 0
				|| (characteristics & SectionCharacteristics.ContainsCode) != 0;
			read = (characteristics & SectionCharacteristics.MemRead) != 0;
			if (!read && !write && !execute)
			{
				read = true;
			}
		}

		private static IReadOnlyList<string> SecurityBadges(DllCharacteristics value)
		{
			List<string> badges = new List<string>();
			if ((value & DllCharacteristics.DynamicBase) != 0 || (value & DllCharacteristics.HighEntropyVirtualAddressSpace) != 0)
			{
				badges.Add("ASLR");
			}
			if ((value & DllCharacteristics.HighEntropyVirtualAddressSpace) != 0)
			{
				badges.Add("HIGH-ENTROPY");
			}
			if ((value & DllCharacteristics.NxCompatible) != 0)
			{
				badges.Add("DEP");
			}
			if (((int)value & 0x4000) != 0)
			{
				badges.Add("CFG");
			}
			if ((value & DllCharacteristics.NoSeh) != 0)
			{
				badges.Add("NO-SEH");
			}
			return badges;
		}

		private static string MachineName(Machine machine)
		{
			return machine.ToString();
		}

		private static string SubsystemName(Subsystem subsystem)
		{
			switch (subsystem)
			{
				case Subsystem.Native:
					return "Native";
				case Subsystem.WindowsGui:
					return "Windows GUI";
				case Subsystem.WindowsCui:
					return "Windows Console";
				case Subsystem.WindowsCEGui:
					return "Windows CE GUI";
				case Subsystem.EfiApplication:
					return "EFI Application";
				case Subsystem.EfiBootServiceDriver:
					return "EFI Boot Service Driver";
				case Subsystem.EfiRuntimeDriver:
					return "EFI Runtime Driver";
				case Subsystem.EfiRom:
					return "EFI ROM";
				case Subsystem.Xbox:
					return "Xbox";
				case Subsystem.WindowsBootApplication:
					return "Windows Boot Application";
				default:
					return subsystem.ToString();
			}
		}

		// ====================================================================
		// ELF
		// ====================================================================

		private static ExecutableModel ParseElf(Buf buf)
		{
			Builder builder = new Builder(ExecutableFormat.ELF, buf.Length);
			byte elfClass = buf.U8(4);
			byte data = buf.U8(5);
			bool is64 = elfClass == 2;
			bool little = data != 2;
			builder.AddField("Class", is64 ? "64-bit" : "32-bit");
			builder.AddField("Endianness", little ? "little-endian" : "big-endian");

			ushort type = ReadU16(buf, 16, little);
			ushort machine = ReadU16(buf, 18, little);
			builder.AddField("Type", ElfTypeName(type));
			builder.AddField("Machine", ElfMachineName(machine) + " (0x" + machine.ToString("X") + ")");

			ulong entry;
			ulong programOffset;
			ulong sectionOffset;
			ushort programEntrySize;
			ushort programCount;
			ushort sectionEntrySize;
			ushort sectionCount;
			ushort stringTableIndex;
			if (is64)
			{
				entry = ReadU64(buf, 24, little);
				programOffset = ReadU64(buf, 32, little);
				sectionOffset = ReadU64(buf, 40, little);
				programEntrySize = ReadU16(buf, 54, little);
				programCount = ReadU16(buf, 56, little);
				sectionEntrySize = ReadU16(buf, 58, little);
				sectionCount = ReadU16(buf, 60, little);
				stringTableIndex = ReadU16(buf, 62, little);
			}
			else
			{
				entry = ReadU32(buf, 24, little);
				programOffset = ReadU32(buf, 28, little);
				sectionOffset = ReadU32(buf, 32, little);
				programEntrySize = ReadU16(buf, 42, little);
				programCount = ReadU16(buf, 44, little);
				sectionEntrySize = ReadU16(buf, 46, little);
				sectionCount = ReadU16(buf, 48, little);
				stringTableIndex = ReadU16(buf, 50, little);
			}
			builder.AddField("Entry point", "0x" + entry.ToString("X"));
			builder.AddField("Program headers", programCount.ToString());

			// 节表：先读原始结构，再用节头字符串表解名。
			int count = Math.Min((int)sectionCount, MaxSections);
			long[] names = new long[count];
			long[] offsets = new long[count];
			long[] sizes = new long[count];
			bool[] alloc = new bool[count];
			bool[] write = new bool[count];
			bool[] execute = new bool[count];
			for (int i = 0; i < count; i++)
			{
				long entryOffset = (long)sectionOffset + i * (long)sectionEntrySize;
				names[i] = buf.U32(entryOffset);
				ulong flags;
				if (is64)
				{
					flags = ReadU64(buf, entryOffset + 8, little);
					offsets[i] = (long)ReadU64(buf, entryOffset + 24, little);
					sizes[i] = (long)ReadU64(buf, entryOffset + 32, little);
				}
				else
				{
					flags = ReadU32(buf, entryOffset + 8, little);
					offsets[i] = (long)ReadU32(buf, entryOffset + 16, little);
					sizes[i] = (long)ReadU32(buf, entryOffset + 20, little);
				}
				alloc[i] = (flags & 0x2uL) != 0uL;
				write[i] = (flags & 0x1uL) != 0uL;
				execute[i] = (flags & 0x4uL) != 0uL;
			}
			long stringTableOffset = 0L;
			long stringTableSize = 0L;
			if (stringTableIndex < count)
			{
				stringTableOffset = offsets[stringTableIndex];
				stringTableSize = sizes[stringTableIndex];
			}
			int dynStrIndex = -1;
			int dynSymIndex = -1;
			int dynamicIndex = -1;
			for (int i = 0; i < count; i++)
			{
				string name = ReadStringAt(buf, stringTableOffset, stringTableSize, names[i]);
				if (builder.SectionCount < MaxSections)
				{
					string display = string.IsNullOrEmpty(name) ? "[" + i + "]" : name;
					builder.AddSection(new ExecSection(display, sizes[i], alloc[i], write[i], execute[i]));
				}
				if (name == ".dynstr")
				{
					dynStrIndex = i;
				}
				else if (name == ".dynsym")
				{
					dynSymIndex = i;
				}
				else if (name == ".dynamic")
				{
					dynamicIndex = i;
				}
				else if (name == ".note.gnu.build-id")
				{
					string buildId = ReadBuildId(buf, offsets[i], sizes[i]);
					if (!string.IsNullOrEmpty(buildId))
					{
						builder.AddField("Build ID", buildId);
					}
				}
			}

			long dynStrOffset = dynStrIndex >= 0 ? offsets[dynStrIndex] : 0L;
			long dynStrSize = dynStrIndex >= 0 ? sizes[dynStrIndex] : 0L;
			if (dynamicIndex >= 0)
			{
				ReadElfDynamic(buf, builder, offsets[dynamicIndex], sizes[dynamicIndex], dynStrOffset, dynStrSize, is64, little);
			}
			if (dynSymIndex >= 0)
			{
				ReadElfSymbols(buf, builder, offsets[dynSymIndex], sizes[dynSymIndex], dynStrOffset, dynStrSize, is64, little);
			}
			return builder.Build();
		}

		private static void ReadElfDynamic(Buf buf, Builder builder, long offset, long size, long strOffset, long strSize, bool is64, bool little)
		{
			int entrySize = is64 ? 16 : 8;
			long count = entrySize > 0 ? size / entrySize : 0L;
			if (count > MaxListEntries)
			{
				count = MaxListEntries;
			}
			for (long i = 0; i < count; i++)
			{
				long entry = offset + i * entrySize;
				ulong tag = is64 ? ReadU64(buf, entry, little) : ReadU32(buf, entry, little);
				ulong value = is64 ? ReadU64(buf, entry + 8, little) : ReadU32(buf, entry + 4, little);
				if (tag == 1uL)
				{
					string needed = ReadStringAt(buf, strOffset, strSize, (long)value);
					if (!string.IsNullOrEmpty(needed))
					{
						builder.AddDependency(new ExecDependency(needed));
					}
				}
				else if (tag == 14uL)
				{
					string soname = ReadStringAt(buf, strOffset, strSize, (long)value);
					if (!string.IsNullOrEmpty(soname))
					{
						builder.AddField("SONAME", soname);
					}
				}
				else if (tag == 0uL)
				{
					break;
				}
			}
		}

		private static void ReadElfSymbols(Buf buf, Builder builder, long offset, long size, long strOffset, long strSize, bool is64, bool little)
		{
			int entrySize = is64 ? 24 : 16;
			long count = entrySize > 0 ? size / entrySize : 0L;
			if (count > MaxListEntries)
			{
				count = MaxListEntries;
			}
			for (long i = 0; i < count; i++)
			{
				long entry = offset + i * entrySize;
				long nameIndex = buf.U32(entry);
				byte info = buf.U8(entry + (is64 ? 4 : 12));
				ushort sectionIndex = ReadU16(buf, entry + (is64 ? 6 : 14), little);
				string name = ReadStringAt(buf, strOffset, strSize, nameIndex);
				if (string.IsNullOrEmpty(name))
				{
					continue;
				}
				int bind = info >> 4;
				if (sectionIndex == 0)
				{
					builder.AddImport(name);
				}
				else if (bind == 1 || bind == 2)
				{
					builder.AddExport(name);
				}
			}
		}

		private static string ReadBuildId(Buf buf, long offset, long size)
		{
			if (size < 12L)
			{
				return null;
			}
			uint nameSize = buf.U32(offset);
			uint descSize = buf.U32(offset + 4);
			long descOffset = offset + 12L + Align4(nameSize);
			if (descSize == 0u || descSize > 64u || !buf.Has(descOffset, descSize))
			{
				return null;
			}
			return ToHex(buf, descOffset, (int)descSize);
		}

		private static long Align4(long value)
		{
			return (value + 3L) & ~3L;
		}

		private static string ElfTypeName(ushort type)
		{
			switch (type)
			{
				case 1:
					return "Relocatable";
				case 2:
					return "Executable";
				case 3:
					return "Shared object";
				case 4:
					return "Core";
				default:
					return "0x" + type.ToString("X");
			}
		}

		private static string ElfMachineName(ushort machine)
		{
			switch (machine)
			{
				case 0x03:
					return "x86";
				case 0x3E:
					return "x86-64";
				case 0x08:
					return "MIPS";
				case 0x14:
					return "PowerPC";
				case 0x15:
					return "PowerPC64";
				case 0x16:
					return "s390";
				case 0x28:
					return "ARM";
				case 0x2A:
					return "SuperH";
				case 0x32:
					return "IA-64";
				case 0xB7:
					return "AArch64";
				case 0xF3:
					return "RISC-V";
				default:
					return "machine";
			}
		}

		// ====================================================================
		// Mach-O
		// ====================================================================

		private static ExecutableModel ParseMachO(Buf buf)
		{
			Builder builder = new Builder(ExecutableFormat.MachO, buf.Length);
			byte m0 = buf.U8(0);
			byte m1 = buf.U8(1);
			byte m2 = buf.U8(2);
			byte m3 = buf.U8(3);
			bool fat = (m0 == 0xCA && m1 == 0xFE && m2 == 0xBA && m3 == 0xBE)
				|| (m0 == 0xBE && m1 == 0xBA && m2 == 0xFE && m3 == 0xCA)
				|| (m0 == 0xCA && m1 == 0xFE && m2 == 0xBA && m3 == 0xBF)
				|| (m0 == 0xBF && m1 == 0xBA && m2 == 0xFE && m3 == 0xCA);
			if (fat)
			{
				ReadMachOFat(buf, builder);
				return builder.Build();
			}

			// 原生 Mach-O：magic 决定位数；CIGAM 系列为大端。
			bool is64 = (m0 == 0xFE && m1 == 0xED && m2 == 0xFA && m3 == 0xCF) || (m0 == 0xCF && m1 == 0xFA && m2 == 0xED && m3 == 0xFE);
			bool little = m0 == 0xFE || m0 == 0xCF;
			builder.AddField("Bits", is64 ? "64-bit" : "32-bit");
			builder.AddField("Endianness", little ? "little-endian" : "big-endian");

			uint cpuType = ReadU32(buf, 4, little);
			uint fileType = ReadU32(buf, 12, little);
			uint commandCount = ReadU32(buf, 16, little);
			builder.AddField("Architecture", MachoCpuName(cpuType));
			builder.AddField("Type", MachOFileTypeName(fileType));

			long headerSize = is64 ? 32L : 28L;
			long commandOffset = headerSize;
			int count = (int)Math.Min((long)commandCount, MaxSections);
			for (int i = 0; i < count; i++)
			{
				if (!buf.Has(commandOffset, 8L))
				{
					break;
				}
				uint command = buf.U32(commandOffset);
				uint commandSize = buf.U32(commandOffset + 4);
				if (commandSize < 8u)
				{
					break;
				}
				switch (command)
				{
					case 0x1u: // LC_SEGMENT
					case 0x19u: // LC_SEGMENT_64
						ReadMachOSegment(buf, builder, commandOffset, command == 0x19u, little);
						break;
					case 0xCu: // LC_LOAD_DYLIB
					case 0x80000018u: // LC_LOAD_WEAK_DYLIB
					case 0x8000001Fu: // LC_REEXPORT_DYLIB
					case 0x80000023u: // LC_LOAD_UPWARD_DYLIB
					case 0xDu: // LC_ID_DYLIB
						{
							uint nameOffset = buf.U32(commandOffset + 8);
							string dylib = ReadAsciiZ(buf, commandOffset + nameOffset);
							if (command == 0xDu)
							{
								builder.AddField("SONAME", dylib);
							}
							else if (!string.IsNullOrEmpty(dylib))
							{
								builder.AddDependency(new ExecDependency(dylib));
							}
							break;
						}
					case 0x1Bu: // LC_UUID
						builder.AddField("UUID", FormatUuid(buf, commandOffset + 8));
						break;
					case 0x32u: // LC_BUILD_VERSION
						{
							uint platform = buf.U32(commandOffset + 8);
							builder.AddField("Platform", MachOPlatformName(platform));
							break;
						}
					case 0x24u: // LC_VERSION_MIN_MACOSX
						builder.AddField("Platform", "macOS");
						break;
					case 0x25u: // LC_VERSION_MIN_IPHONEOS
						builder.AddField("Platform", "iOS");
						break;
				}
				commandOffset += commandSize;
			}
			return builder.Build();
		}

		private static void ReadMachOSegment(Buf buf, Builder builder, long commandOffset, bool is64, bool little)
		{
			int prototype;
			long sectionOffset;
			int sectionEntrySize;
			int sectionCount;
			if (is64)
			{
				prototype = (int)ReadU32(buf, commandOffset + 60, little); // initprot
				sectionCount = (int)ReadU32(buf, commandOffset + 64, little);
				sectionOffset = commandOffset + 72L;
				sectionEntrySize = 80;
			}
			else
			{
				prototype = (int)ReadU32(buf, commandOffset + 44, little); // initprot
				sectionCount = (int)ReadU32(buf, commandOffset + 48, little);
				sectionOffset = commandOffset + 56L;
				sectionEntrySize = 68;
			}
			bool read = (prototype & 0x1) != 0;
			bool write = (prototype & 0x2) != 0;
			bool execute = (prototype & 0x4) != 0;
			int count = Math.Min((int)sectionCount, MaxSections);
			for (int i = 0; i < count; i++)
			{
				if (builder.SectionCount >= MaxSections)
				{
					break;
				}
				long entry = sectionOffset + i * (long)sectionEntrySize;
				string name = TrimName(buf.Ascii(entry, 16), 16);
				long size = is64 ? (long)ReadU64(buf, entry + 40, little) : (long)ReadU32(buf, entry + 36, little);
				builder.AddSection(new ExecSection(name, size, read, write, execute));
			}
		}

		private static void ReadMachOFat(Buf buf, Builder builder)
		{
			// fat_header 字段固定大端；0xCAFEBABF / 0xBFBAFECA 为 64 位 fat_arch_64。
			uint magic = buf.U32Be(0);
			bool arch64 = magic == 0xCAFEBABFu || magic == 0xBFBAFECAu;
			uint architectureCount = buf.U32Be(4);
			builder.AddField("Endianness", "big-endian");
			int count = (int)Math.Min((long)architectureCount, 32L);
			List<string> architectures = new List<string>();
			long entryOffset = 8L;
			int entrySize = arch64 ? 32 : 20;
			for (int i = 0; i < count; i++)
			{
				long offset = entryOffset + i * (long)entrySize;
				if (!buf.Has(offset, 8L))
				{
					break;
				}
				uint cpuType = buf.U32Be(offset);
				architectures.Add(MachoCpuName(cpuType));
			}
			builder.MarkFat();
			builder.AddField("Architecture", "fat (" + string.Join(", ", architectures) + ")");
		}

		private static string FormatUuid(Buf buf, long offset)
		{
			if (!buf.Has(offset, 16L))
			{
				return string.Empty;
			}
			string hex = ToHex(buf, offset, 16);
			if (hex.Length != 32)
			{
				return hex;
			}
			return hex.Substring(0, 8) + "-" + hex.Substring(8, 4) + "-" + hex.Substring(12, 4) + "-"
				+ hex.Substring(16, 4) + "-" + hex.Substring(20, 12);
		}

		private static string MachoCpuName(uint cpuType)
		{
			switch (cpuType)
			{
				case 0x00000007u:
					return "x86";
				case 0x01000007u:
					return "x86_64";
				case 0x0000000Cu:
					return "arm";
				case 0x0100000Cu:
					return "arm64";
				case 0x00000012u:
					return "ppc";
				case 0x01000012u:
					return "ppc64";
				default:
					return "0x" + cpuType.ToString("X8");
			}
		}

		private static string MachOFileTypeName(uint fileType)
		{
			switch (fileType)
			{
				case 1u:
					return "Object";
				case 2u:
					return "Executable";
				case 4u:
					return "Core";
				case 6u:
					return "Dynamic library";
				case 8u:
					return "Bundle";
				case 9u:
					return "Dynamic linker";
				case 11u:
					return "Kernel extension";
				default:
					return "0x" + fileType.ToString("X");
			}
		}

		private static string MachOPlatformName(uint platform)
		{
			switch (platform)
			{
				case 1u:
					return "macOS";
				case 2u:
					return "iOS";
				case 3u:
					return "tvOS";
				case 4u:
					return "watchOS";
				case 5u:
					return "bridgeOS";
				case 6u:
					return "Mac Catalyst";
				case 7u:
					return "iOS Simulator";
				case 8u:
					return "tvOS Simulator";
				case 9u:
					return "watchOS Simulator";
				case 11u:
					return "visionOS";
				default:
					return "0x" + platform.ToString("X");
			}
		}

		// ====================================================================
		// WebAssembly
		// ====================================================================

		private static ExecutableModel ParseWasm(Buf buf)
		{
			Builder builder = new Builder(ExecutableFormat.WebAssembly, buf.Length);
			uint version = buf.U32(4);
			builder.AddField("Type", "WebAssembly module (v" + version + ")");

			long position = 8L;
			while (position < buf.Length)
			{
				byte id = buf.U8(position);
				position++;
				ulong sectionSize = ReadLeb(buf, ref position, out int sizeLength);
				long payloadOffset = position;
				long totalSize = 1L + sizeLength + (long)sectionSize;
				string name = WasmSectionName(id);
				long entryCount = -1L;
				if (id != 0 && sectionSize > 0uL)
				{
					try
					{
						long probe = payloadOffset;
						entryCount = (long)ReadLeb(buf, ref probe, out _);
					}
					catch (InvalidDataException)
					{
						entryCount = -1L;
					}
				}
				if (builder.SectionCount < MaxSections)
				{
					string label = entryCount >= 0L ? entryCount.ToString() : null;
					builder.AddSection(new ExecSection(name, totalSize, true, false, false, label));
				}
				switch (id)
				{
					case 1:
						builder.AddField("Type entries", entryCount >= 0L ? entryCount.ToString() : "-");
						break;
					case 2:
						builder.AddField("Import entries", entryCount >= 0L ? entryCount.ToString() : "-");
						ReadWasmImports(buf, builder, payloadOffset, payloadOffset + (long)sectionSize);
						break;
					case 7:
						builder.AddField("Export entries", entryCount >= 0L ? entryCount.ToString() : "-");
						ReadWasmExports(buf, builder, payloadOffset, payloadOffset + (long)sectionSize);
						break;
					case 10:
						builder.AddField("Code entries", entryCount >= 0L ? entryCount.ToString() : "-");
						break;
					case 11:
						builder.AddField("Data entries", entryCount >= 0L ? entryCount.ToString() : "-");
						break;
				}
				long next = payloadOffset + (long)sectionSize;
				if (next <= position)
				{
					break;
				}
				position = next;
			}
			return builder.Build();
		}

		private static void ReadWasmImports(Buf buf, Builder builder, long start, long end)
		{
			long position = start;
			ulong count = ReadLeb(buf, ref position, out _);
			Dictionary<string, List<string>> modules = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			List<string> order = new List<string>();
			for (ulong i = 0uL; i < count && position < end; i++)
			{
				string module = ReadWasmName(buf, ref position);
				string field = ReadWasmName(buf, ref position);
				if (position >= end)
				{
					break;
				}
				byte kind = buf.U8(position);
				position++;
				SkipWasmImportDescriptor(buf, ref position, kind);
				if (string.IsNullOrEmpty(module))
				{
					module = "-";
				}
				if (!modules.TryGetValue(module, out List<string> symbols))
				{
					symbols = new List<string>();
					modules[module] = symbols;
					order.Add(module);
				}
				symbols.Add(field);
				if (builder.DependencyCount >= MaxListEntries)
				{
					break;
				}
			}
			foreach (string module in order)
			{
				builder.AddDependency(new ExecDependency(module, modules[module]));
			}
		}

		private static void SkipWasmImportDescriptor(Buf buf, ref long position, byte kind)
		{
			switch (kind)
			{
				case 0x00: // func: typeidx
					ReadLeb(buf, ref position, out _);
					break;
				case 0x01: // table: reftype + limits
					position++;
					SkipWasmLimits(buf, ref position);
					break;
				case 0x02: // mem: limits
					SkipWasmLimits(buf, ref position);
					break;
				case 0x03: // global: valtype + mut
					position += 2;
					break;
			}
		}

		private static void SkipWasmLimits(Buf buf, ref long position)
		{
			byte flags = buf.U8(position);
			position++;
			ReadLeb(buf, ref position, out _);
			if ((flags & 0x1) != 0)
			{
				ReadLeb(buf, ref position, out _);
			}
		}

		private static void ReadWasmExports(Buf buf, Builder builder, long start, long end)
		{
			long position = start;
			ulong count = ReadLeb(buf, ref position, out _);
			for (ulong i = 0uL; i < count && position < end; i++)
			{
				string name = ReadWasmName(buf, ref position);
				if (position >= end)
				{
					break;
				}
				position++; // export kind
				ReadLeb(buf, ref position, out _); // index
				if (!string.IsNullOrEmpty(name))
				{
					builder.AddExport(name);
				}
				if (builder.ExportCount >= MaxListEntries)
				{
					break;
				}
			}
		}

		private static string ReadWasmName(Buf buf, ref long position)
		{
			ulong size = ReadLeb(buf, ref position, out _);
			int take = (int)Math.Min((long)size, MaxStringLength);
			string result = buf.Ascii(position, take);
			position += (long)size;
			return result;
		}

		private static string WasmSectionName(byte id)
		{
			switch (id)
			{
				case 0:
					return "custom";
				case 1:
					return "type";
				case 2:
					return "import";
				case 3:
					return "function";
				case 4:
					return "table";
				case 5:
					return "memory";
				case 6:
					return "global";
				case 7:
					return "export";
				case 8:
					return "start";
				case 9:
					return "element";
				case 10:
					return "code";
				case 11:
					return "data";
				case 12:
					return "data count";
				default:
					return "section " + id;
			}
		}

		// ====================================================================
		// ar 归档（.a / .lib 静态库）
		// ====================================================================

		private static ExecutableModel ParseAr(Buf buf)
		{
			Builder builder = new Builder(ExecutableFormat.Ar, buf.Length);

			// 预扫长名表（成员名 "//"）的偏移与长度，供后续成员解长名。
			long longNamesOffset = -1L;
			long longNamesSize = 0L;
			long scan = 8L;
			for (int i = 0; i < MaxListEntries && buf.Has(scan, 60L); i++)
			{
				string rawName = TrimSpaces(buf.Ascii(scan, 16));
				long memberSize = ReadArSize(buf, scan + 48);
				long dataOffset = scan + 60L;
				if (rawName == "//")
				{
					longNamesOffset = dataOffset;
					longNamesSize = memberSize;
					break;
				}
				scan = dataOffset + memberSize + (memberSize & 1L);
			}

			long position = 8L;
			for (int i = 0; i < MaxListEntries && buf.Has(position, 60L); i++)
			{
				string rawName = TrimSpaces(buf.Ascii(position, 16));
				long timestamp = ParseArNumber(buf.Ascii(position + 16, 12));
				long memberSize = ReadArSize(buf, position + 48);
				long dataOffset = position + 60L;
				string name = rawName;
				string label = null;
				if (rawName == "/")
				{
					label = "symbol table";
				}
				else if (rawName == "//")
				{
					label = "long names";
				}
				else if (rawName.StartsWith("__.SYMDEF", StringComparison.Ordinal) || rawName.StartsWith("__.GOSYMDEF", StringComparison.Ordinal))
				{
					label = "symbol table";
				}
				else if (rawName.Length > 1 && rawName[0] == '/' && longNamesOffset >= 0L && long.TryParse(rawName.Substring(1), out long nameOffset))
				{
					name = ReadArLongName(buf, longNamesOffset, longNamesSize, nameOffset);
				}
				else if (rawName.EndsWith("/", StringComparison.Ordinal))
				{
					name = rawName.Substring(0, rawName.Length - 1);
				}
				if (builder.SectionCount < MaxSections)
				{
					builder.AddSection(new ExecSection(name, memberSize, false, false, false, label, dataOffset, timestamp));
				}
				long next = dataOffset + memberSize + (memberSize & 1L);
				if (next <= position)
				{
					break;
				}
				position = next;
			}
			return builder.Build();
		}

		private static string ReadArLongName(Buf buf, long tableOffset, long tableSize, long index)
		{
			if (index < 0L || index >= tableSize)
			{
				return string.Empty;
			}
			StringBuilder builder = new StringBuilder();
			long position = tableOffset + index;
			for (int i = 0; i < MaxStringLength; i++)
			{
				if (position >= tableOffset + tableSize || position >= buf.Length)
				{
					break;
				}
				byte value = buf.U8(position);
				position++;
				if (value == '\n' || value == '/')
				{
					break;
				}
				builder.Append((char)value);
			}
			return builder.ToString();
		}

		private static long ReadArSize(Buf buf, long offset)
		{
			return ParseArNumber(buf.Ascii(offset, 10));
		}

		private static long ParseArNumber(string value)
		{
			string text = TrimSpaces(value);
			return long.TryParse(text, out long result) ? result : -1L;
		}

		// ====================================================================
		// 通用读取辅助
		// ====================================================================

		private static ushort ReadU16(Buf buf, long offset, bool little)
		{
			return little ? buf.U16(offset) : buf.U16Be(offset);
		}

		private static uint ReadU32(Buf buf, long offset, bool little)
		{
			return little ? buf.U32(offset) : buf.U32Be(offset);
		}

		private static ulong ReadU64(Buf buf, long offset, bool little)
		{
			return little ? buf.U64(offset) : buf.U64Be(offset);
		}

		/// <summary>读 LEB128 无符号整数；同时给出编码字节数。</summary>
		private static ulong ReadLeb(Buf buf, ref long position, out int length)
		{
			ulong result = 0uL;
			int shift = 0;
			length = 0;
			while (true)
			{
				byte value = buf.U8(position);
				position++;
				length++;
				result |= (ulong)(value & 0x7F) << shift;
				if ((value & 0x80) == 0)
				{
					break;
				}
				shift += 7;
				if (length > 10)
				{
					throw new InvalidDataException("LEB128 too long");
				}
			}
			return result;
		}

		/// <summary>从字符串表（偏移 + 大小）取以 NUL 结尾的串；越界 / 无终止返回空串。</summary>
		private static string ReadStringAt(Buf buf, long tableOffset, long tableSize, long index)
		{
			if (index < 0L || index >= tableSize)
			{
				return string.Empty;
			}
			long limit = tableOffset + tableSize;
			long position = tableOffset + index;
			StringBuilder builder = new StringBuilder();
			for (int i = 0; i < MaxStringLength; i++)
			{
				if (position >= limit || position >= buf.Length)
				{
					break;
				}
				byte value = buf.U8(position);
				position++;
				if (value == 0)
				{
					break;
				}
				builder.Append((char)value);
			}
			return builder.ToString();
		}

		private static string ReadAsciiZ(Buf buf, long offset)
		{
			return buf.AsciiZ(offset, MaxStringLength);
		}

		private static string ToHex(Buf buf, long offset, int count)
		{
			StringBuilder builder = new StringBuilder(count * 2);
			for (int i = 0; i < count; i++)
			{
				builder.Append(buf.U8(offset + i).ToString("x2"));
			}
			return builder.ToString();
		}

		private static string TrimName(string value, int max)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}
			int end = value.Length < max ? value.Length : max;
			int length = 0;
			while (length < end && value[length] != '\0')
			{
				length++;
			}
			return value.Substring(0, length);
		}

		private static string TrimSpaces(string value)
		{
			return value == null ? string.Empty : value.TrimEnd(' ', '\0');
		}

		// ====================================================================
		// 模型构建
		// ====================================================================

		/// <summary>解析过程中的累积器：收集字段 / 节段 / 依赖 / 符号，最后统一产出模型。</summary>
		private sealed class Builder
		{
			private readonly List<ExecField> _fields = new List<ExecField>();
			private readonly List<ExecSection> _sections = new List<ExecSection>();
			private readonly List<ExecDependency> _dependencies = new List<ExecDependency>();
			private readonly List<string> _imports = new List<string>();
			private readonly List<string> _exports = new List<string>();
			private readonly List<ExecAssemblyRef> _assemblyRefs = new List<ExecAssemblyRef>();

			public Builder(ExecutableFormat format, int fileSize)
			{
				Format = format;
				FileSize = fileSize;
			}

			public ExecutableFormat Format { get; }

			public long FileSize { get; }

			public int SectionCount => _sections.Count;

			public int DependencyCount => _dependencies.Count;

			public int AssemblyRefCount => _assemblyRefs.Count;

			public int ExportCount => _exports.Count;

			private string Warning { get; set; }

			public void AddField(string key, string value)
			{
				AddField(key, value, null);
			}

			public void AddField(string key, string value, IReadOnlyList<string> badges)
			{
				if (string.IsNullOrEmpty(key) || FindField(key) != null)
				{
					return;
				}
				_fields.Add(new ExecField(key, value, badges));
			}

			/// <summary>按字段名查已收集的摘要字段；不存在返回 null（同名只保留首次）。</summary>
			private ExecField FindField(string key)
			{
				for (int i = 0; i < _fields.Count; i++)
				{
					if (string.Equals(_fields[i].Key, key, StringComparison.Ordinal))
					{
						return _fields[i];
					}
				}
				return null;
			}

			public void AddSection(ExecSection section)
			{
				if (section != null)
				{
					_sections.Add(section);
				}
			}

			public void AddDependency(ExecDependency dependency)
			{
				if (dependency != null && _dependencies.Count < MaxListEntries)
				{
					_dependencies.Add(dependency);
				}
			}

			public void AddImport(string symbol)
			{
				if (!string.IsNullOrEmpty(symbol) && _imports.Count < MaxListEntries)
				{
					_imports.Add(symbol);
				}
			}

			public void AddExport(string symbol)
			{
				if (!string.IsNullOrEmpty(symbol) && _exports.Count < MaxListEntries)
				{
					_exports.Add(symbol);
				}
			}

			public void AddAssemblyRef(ExecAssemblyRef reference)
			{
				if (reference != null && _assemblyRefs.Count < MaxListEntries)
				{
					_assemblyRefs.Add(reference);
				}
			}

			public void MarkFat()
			{
				IsFat = true;
			}

			public bool IsFat { get; private set; }

			/// <summary>记录一处子表解析失败：整体仍可呈现时作为降级说明；完全无数据时作为错误详情。</summary>
			public void NoteError(string message)
			{
				if (string.IsNullOrEmpty(message))
				{
					return;
				}
				Warning = Warning == null ? message : Warning + "; " + message;
			}

			public ExecutableModel Build()
			{
				ExecutableError error = ExecutableError.None;
				string detail = null;
				if (_fields.Count == 0 && _sections.Count == 0)
				{
					error = ExecutableError.Corrupt;
					detail = Warning ?? "no parseable structure";
				}
				return new ExecutableModel(Format, error, detail, error == ExecutableError.None ? Warning : null, FileSize, _fields, _sections, _dependencies, _imports, _exports, _assemblyRefs, IsFat);
			}
		}

		/// <summary>
		/// 带边界校验的字节缓冲读取器：所有方法在越界时抛 <see cref="InvalidDataException"/>，
		/// 由上层 try/catch 统一降级；偏移一律用 long，避免无符号回绕。
		/// </summary>
		private readonly struct Buf
		{
			private readonly byte[] _data;

			public Buf(byte[] data)
			{
				_data = data;
			}

			public byte[] Raw => _data;

			public int Length => _data.Length;

			public bool Has(long offset, long count)
			{
				return offset >= 0L && count >= 0L && offset + count <= _data.Length;
			}

			public void Check(long offset, long count)
			{
				if (!Has(offset, count))
				{
					throw new InvalidDataException("offset out of range");
				}
			}

			public byte U8(long offset)
			{
				Check(offset, 1L);
				return _data[offset];
			}

			public ushort U16(long offset)
			{
				Check(offset, 2L);
				return (ushort)(_data[offset] | (_data[offset + 1] << 8));
			}

			public ushort U16Be(long offset)
			{
				Check(offset, 2L);
				return (ushort)((_data[offset] << 8) | _data[offset + 1]);
			}

			public uint U32(long offset)
			{
				Check(offset, 4L);
				return (uint)(_data[offset] | (_data[offset + 1] << 8) | (_data[offset + 2] << 16) | (_data[offset + 3] << 24));
			}

			public uint U32Be(long offset)
			{
				Check(offset, 4L);
				return (uint)((_data[offset] << 24) | (_data[offset + 1] << 16) | (_data[offset + 2] << 8) | _data[offset + 3]);
			}

			public ulong U64(long offset)
			{
				Check(offset, 8L);
				return (ulong)U32(offset) | ((ulong)U32(offset + 4) << 32);
			}

			public ulong U64Be(long offset)
			{
				Check(offset, 8L);
				return ((ulong)U32Be(offset) << 32) | U32Be(offset + 4);
			}

			/// <summary>定长 ASCII 字段（不足补空，遇 NUL 截断）。</summary>
			public string Ascii(long offset, int count)
			{
				if (count <= 0)
				{
					return string.Empty;
				}
				Check(offset, count);
				StringBuilder builder = new StringBuilder(count);
				for (int i = 0; i < count; i++)
				{
					byte value = _data[offset + i];
					if (value == 0)
					{
						break;
					}
					builder.Append((char)value);
				}
				return builder.ToString();
			}

			/// <summary>以 NUL 结尾的 ASCII 串；越界即停，最多 max 字节。</summary>
			public string AsciiZ(long offset, int max)
			{
				if (max <= 0 || offset < 0L || offset >= _data.Length)
				{
					return string.Empty;
				}
				StringBuilder builder = new StringBuilder();
				for (int i = 0; i < max; i++)
				{
					long position = offset + i;
					if (position >= _data.Length)
					{
						break;
					}
					byte value = _data[position];
					if (value == 0)
					{
						break;
					}
					builder.Append((char)value);
				}
				return builder.ToString();
			}
		}
	}
}
