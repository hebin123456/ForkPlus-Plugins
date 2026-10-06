using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Executable
{
	/// <summary>
	/// 可执行文件 / 库对比视图插件：认领发版常见产物（.exe / .dll / .so / .dylib / .a / .lib / .wasm），
	/// 命中后由 <see cref="ExecutableDiffView"/> 把两侧二进制各自解析成结构模型——格式、架构、
	/// 节 / 段表、导入导出符号、依赖与体积构成——左右并排对比，逐行标注「相同 / 变了 / 仅左 / 仅右」。
	///
	/// 同一个插件承载四种格式：PE / ELF / Mach-O / WebAssembly（外加 ar 归档）。零第三方依赖：
	/// PE 走共享框架内置的 System.Reflection.Metadata / System.Reflection.PortableExecutable，
	/// 其余自解析（详见 <see cref="ExecutableParser"/>）。
	///
	/// 注意：宿主只对**二进制差异**查询插件路由；可执行文件与库一律为二进制，因此会命中本插件，
	/// 而非文本差异或 Hex 兜底。已知限制：版本化 .so（libfoo.so.1.2.3）扩展名是 .3，路由不到。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class ExecutableDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.executable";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "Executable Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "0.0.1";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "Executable Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Executable / library compare view plugin: claims .exe / .dll / .so / .dylib / .a / .lib / .wasm, parses PE / ELF / Mach-O / WebAssembly / ar itself, and compares sections, imports and exports, dependencies and size composition side by side.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, ExecutableStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, ExecutableStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与其它领域插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的发版产物扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[7]
		{
			".exe",
			".dll",
			".so",
			".dylib",
			".a",
			".lib",
			".wasm"
		};

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new ExecutableDiffView();
		}
	}
}
