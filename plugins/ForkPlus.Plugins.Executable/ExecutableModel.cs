using System.Collections.Generic;

namespace ForkPlus.Plugins.Executable
{
	/// <summary>识别出的可执行 / 库格式。用于视图顶部格式徽章与对比分支。</summary>
	internal enum ExecutableFormat
	{
		/// <summary>无法识别的格式（魔数未知）。</summary>
		Unknown,

		/// <summary>Windows PE / COFF（.exe / .dll / .lib 内的 COFF 目标）。</summary>
		PE,

		/// <summary>ELF（.so / Linux 可执行）。</summary>
		ELF,

		/// <summary>Mach-O（.dylib / macOS 可执行，含 fat / universal 多架构）。</summary>
		MachO,

		/// <summary>WebAssembly（.wasm）。</summary>
		WebAssembly,

		/// <summary>Unix ar 归档（.a / .lib 静态库）。</summary>
		Ar
	}

	/// <summary>解析结果的错误分类，决定视图给出的提示文案。</summary>
	internal enum ExecutableError
	{
		/// <summary>成功（可能带 <see cref="ExecutableModel.Warning"/> 降级说明）。</summary>
		None,

		/// <summary>魔数未知，不是本插件支持的二进制格式。</summary>
		Unsupported,

		/// <summary>识别为支持的格式，但结构损坏 / 解析失败。</summary>
		Corrupt
	}

	/// <summary>
	/// 结构摘要里的一项关键字段（键 / 值 + 可选安全位徽章）。键为英文原文，渲染时经
	/// <see cref="ExecutableStrings"/> 取当前语言译文；值为已格式化好的可读文本。
	/// </summary>
	internal sealed class ExecField
	{
		private static readonly IReadOnlyList<string> NoBadges = new List<string>();

		public ExecField(string key, string value, IReadOnlyList<string> badges = null)
		{
			Key = key ?? string.Empty;
			Value = value ?? string.Empty;
			Badges = badges ?? NoBadges;
		}

		/// <summary>字段名（英文原文，如 "Machine" / "Subsystem"）。</summary>
		public string Key { get; }

		/// <summary>字段值（已格式化，如 "AMD64 (0x8664)" / "0x8160"）。</summary>
		public string Value { get; }

		/// <summary>安全位等短徽章（如 ASLR / DEP / CFG / No SEH）；无则空列表。</summary>
		public IReadOnlyList<string> Badges { get; }
	}

	/// <summary>
	/// 节 / 段 / 成员的一行：名、文件中字节数、权限位。PE 节表、ELF 节表、Mach-O 段内节、
	/// ar 成员共用此模型（ar 成员无权限，用 <see cref="Label"/> 标注特殊成员，并带偏移 / 时间）。
	/// </summary>
	internal sealed class ExecSection
	{
		public ExecSection(string name, long size, bool read, bool write, bool execute, string label = null, long offset = -1L, long timestamp = -1L)
		{
			Name = name ?? string.Empty;
			Size = size < 0L ? 0L : size;
			Read = read;
			Write = write;
			Execute = execute;
			Label = label;
			Offset = offset;
			Timestamp = timestamp;
		}

		/// <summary>节 / 段 / 成员名（ELF 无名节回退为 "[N]"）。</summary>
		public string Name { get; }

		/// <summary>该节在文件中的字节数（体积构成占比按此计算）。</summary>
		public long Size { get; }

		public bool Read { get; }

		public bool Write { get; }

		public bool Execute { get; }

		/// <summary>可选徽章（如 ar 特殊成员的 "symbol table" / "long names"）；普通节为 null。</summary>
		public string Label { get; }

		/// <summary>ar 成员在文件中的偏移；其它格式为 -1。</summary>
		public long Offset { get; }

		/// <summary>ar 成员的修改时间（Unix 秒）；其它格式为 -1。</summary>
		public long Timestamp { get; }
	}

	/// <summary>一条导入依赖：库 / 模块名 + 由它导入的符号（PE 为函数名或 "#序号"）。</summary>
	internal sealed class ExecDependency
	{
		private static readonly IReadOnlyList<string> NoSymbols = new List<string>();

		public ExecDependency(string name, IReadOnlyList<string> symbols = null)
		{
			Name = name ?? string.Empty;
			Symbols = symbols ?? NoSymbols;
		}

		/// <summary>依赖库名（如 "KERNEL32.dll" / "libc.so.6" / wasm 导入模块名）。</summary>
		public string Name { get; }

		/// <summary>由该库导入的符号；未知 / 不分组时为空列表。</summary>
		public IReadOnlyList<string> Symbols { get; }
	}

	/// <summary>一条程序集引用（仅 .NET PE）：被引用程序集名 + 版本。</summary>
	internal sealed class ExecAssemblyRef
	{
		public ExecAssemblyRef(string name, string version)
		{
			Name = name ?? string.Empty;
			Version = version ?? string.Empty;
		}

		public string Name { get; }

		public string Version { get; }
	}

	/// <summary>
	/// 一侧二进制解析后的结构模型：格式、错误分类、架构信息、节 / 段列表、导入依赖、
	/// 导入 / 导出符号、体积构成（由 <see cref="Sections"/> 推导）、程序集引用等。
	/// 视图按该模型对左右两侧逐项做「相同 / 变了 / 仅左 / 仅右」四色标注。
	/// </summary>
	internal sealed class ExecutableModel
	{
		private static readonly IReadOnlyList<ExecField> NoFields = new List<ExecField>();
		private static readonly IReadOnlyList<ExecSection> NoSections = new List<ExecSection>();
		private static readonly IReadOnlyList<ExecDependency> NoDependencies = new List<ExecDependency>();
		private static readonly IReadOnlyList<string> NoStrings = new List<string>();
		private static readonly IReadOnlyList<ExecAssemblyRef> NoAssemblyRefs = new List<ExecAssemblyRef>();

		public ExecutableModel(
			ExecutableFormat format,
			ExecutableError error,
			string errorDetail,
			string warning,
			long fileSize,
			IReadOnlyList<ExecField> fields,
			IReadOnlyList<ExecSection> sections,
			IReadOnlyList<ExecDependency> dependencies,
			IReadOnlyList<string> imports,
			IReadOnlyList<string> exports,
			IReadOnlyList<ExecAssemblyRef> assemblyRefs,
			bool isFat = false)
		{
			Format = format;
			Error = error;
			ErrorDetail = errorDetail;
			Warning = warning;
			FileSize = fileSize < 0L ? 0L : fileSize;
			Fields = fields ?? NoFields;
			Sections = sections ?? NoSections;
			Dependencies = dependencies ?? NoDependencies;
			Imports = imports ?? NoStrings;
			Exports = exports ?? NoStrings;
			AssemblyRefs = assemblyRefs ?? NoAssemblyRefs;
			IsFat = isFat;
		}

		public ExecutableFormat Format { get; }

		public ExecutableError Error { get; }

		/// <summary>错误详情（原始异常消息等；无错误为 null）。</summary>
		public string ErrorDetail { get; }

		/// <summary>降级说明（部分子表解析失败但主体可用时给出；无则 null）。</summary>
		public string Warning { get; }

		/// <summary>文件字节数（体积构成里「其它」= 文件大小 - 各节之和）。</summary>
		public long FileSize { get; }

		/// <summary>结构摘要字段（机器 / 位数 / 字节序 / 类型 / 子系统 / DllCharacteristics 等）。</summary>
		public IReadOnlyList<ExecField> Fields { get; }

		/// <summary>节 / 段 / ar 成员列表（体积构成占比按此计算）。</summary>
		public IReadOnlyList<ExecSection> Sections { get; }

		/// <summary>导入依赖库列表（PE 含符号；ELF / Mach-O 仅库名）。</summary>
		public IReadOnlyList<ExecDependency> Dependencies { get; }

		/// <summary>无库归属的导入符号（ELF .dynsym 未定义符号等）。</summary>
		public IReadOnlyList<string> Imports { get; }

		/// <summary>导出符号。</summary>
		public IReadOnlyList<string> Exports { get; }

		/// <summary>程序集引用（仅 .NET PE）。</summary>
		public IReadOnlyList<ExecAssemblyRef> AssemblyRefs { get; }

		/// <summary>是否 Mach-O fat / universal 二进制（单文件多架构）。</summary>
		public bool IsFat { get; }

		/// <summary>节 / 段数量（状态行展示）。</summary>
		public int SectionCount => Sections.Count;

		/// <summary>按字段名查一项摘要字段；不存在返回 null。</summary>
		public ExecField FindField(string key)
		{
			for (int i = 0; i < Fields.Count; i++)
			{
				if (string.Equals(Fields[i].Key, key, System.StringComparison.Ordinal))
				{
					return Fields[i];
				}
			}
			return null;
		}

		/// <summary>按名查一节 / 段；不存在返回 null。</summary>
		public ExecSection FindSection(string name)
		{
			for (int i = 0; i < Sections.Count; i++)
			{
				if (string.Equals(Sections[i].Name, name, System.StringComparison.Ordinal))
				{
					return Sections[i];
				}
			}
			return null;
		}

		/// <summary>未知格式 / 无法解析时的降级模型（保留文件大小）。</summary>
		public static ExecutableModel Failed(ExecutableFormat format, ExecutableError error, string detail, long fileSize)
		{
			return new ExecutableModel(format, error, detail, null, fileSize, NoFields, NoSections, NoDependencies, NoStrings, NoStrings, NoAssemblyRefs);
		}
	}
}
