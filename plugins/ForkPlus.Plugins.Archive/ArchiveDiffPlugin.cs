using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Archive
{
	/// <summary>
	/// 压缩包对比视图插件：认领主流压缩包（zip / 7z / rar / tar，以及 gz / bz2 / xz / zst 等
	/// 流式压缩，含包在其中的 tar），命中后由 <see cref="ArchiveDiffView"/> 把两侧压缩包各自
	/// 展开成「条目树」左右并排对比——只看压缩包里有什么（目录 / 文件 / 大小 / 是否加密），
	/// 不展开解压后的文件内容。带密码的压缩包（如加密头部的 7z / rar）可在视图里输入密码。
	///
	/// 注意：宿主只对**二进制差异**查询插件路由。压缩包一律为二进制，因此会命中本插件，
	/// 而非文本差异或 Hex 兜底。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class ArchiveDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.archive";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "Archive";

		/// <summary>插件自身的版本号（与宿主版本解耦；0.1.0 起支持多语言）。</summary>
		public string Version => "0.1.0";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "Archive Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Archive compare view plugin: claims mainstream archives (zip / 7z / rar / tar plus gz / bz2 / xz / zst streams) and expands both sides into an entry tree for side-by-side comparison of folders, files, sizes and encryption, without comparing extracted contents; password-protected archives can be unlocked in the view.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, ArchiveStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, ArchiveStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与示例 / PDF / Office 插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的扩展名（小写含点）。gzip / bzip2 / xz / zstd 这类流式压缩可与 tar
		/// 组合（.tar.gz / .tgz 等），统一按扩展名认领，视图内部再判定是否 tar。</summary>
		public IReadOnlyList<string> FileExtensions => new string[19]
		{
			".zip",
			".7z",
			".rar",
			".tar",
			".gz",
			".tgz",
			".taz",
			".bz2",
			".tbz",
			".tbz2",
			".xz",
			".txz",
			".zst",
			".tzst",
			".jar",
			".war",
			".apk",
			".nupkg",
			".whl"
		};

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new ArchiveDiffView();
		}
	}
}