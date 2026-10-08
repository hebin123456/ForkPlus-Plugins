using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.ManagedAssembly
{
	/// <summary>
	/// 托管程序集（.NET assembly）对比视图插件：认领 .dll / .exe，命中后由
	/// <see cref="ManagedAssemblyDiffView"/> 纯托管读取 ECMA-335 元数据（共享框架内置的
	/// System.Reflection.Metadata / System.Reflection.PortableExecutable），把两侧程序集的
	/// 「程序集标识」「类型 → 方法 / 字段」「AssemblyRef 引用」分别对比。
	///
	/// 路由：托管程序集是二进制（PE 头含 NUL），git 判为二进制，宿主只对**二进制差异**查询插件路由。
	/// .dll / .exe 同时也被「可执行文件 / 库对比」插件认领，本插件以更高优先级（120 &gt; 100）
	/// 接管 .dll / .exe——因为在这个生态里，仓库中的 .dll / .exe 绝大多数是**托管程序集**，
	/// 元数据对比远比 PE 节段对比有用。若要看原生 PE 结构，可在「偏好设置 → 扩展名绑定」里把
	/// .dll / .exe 绑回可执行文件插件（用户绑定优先级最高）。
	///
	/// 零第三方依赖：PE 读取走共享框架内置类型，随包进 <c>plugins/</c> 的只有插件自身主 DLL
	/// （不需要 third-party.json）。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class ManagedAssemblyDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.managedassembly";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "Managed Assembly Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "0.0.1";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "Managed Assembly Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Managed assembly (.NET) compare view plugin: claims .dll / .exe, reads ECMA-335 metadata with the shared framework (System.Reflection.Metadata), and compares assembly identity, namespaces → types → methods / fields, and AssemblyRef references side by side.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, ManagedAssemblyStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, ManagedAssemblyStrings.Descriptions, Description);
		}

		/// <summary>
		/// 高于可执行文件插件（100）：.dll / .exe 默认走本插件的托管元数据对比；
		/// 要看原生 PE 结构时由用户在扩展名绑定里改派回可执行文件插件。
		/// </summary>
		public int Priority => 120;

		/// <summary>认领的扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[2] { ".dll", ".exe" };

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new ManagedAssemblyDiffView();
		}
	}
}