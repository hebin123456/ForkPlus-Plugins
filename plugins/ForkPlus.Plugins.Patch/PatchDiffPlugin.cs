using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Patch
{
	/// <summary>
	/// 补丁对比视图插件：认领 .patch / .diff，命中后由 <see cref="PatchDiffView"/> 把两侧
	/// 补丁文本按 unified / git diff 容错解析（git 扩展头 / <c>@@</c> hunk / 二进制标记，
	/// <see cref="PatchParser"/>），按「新路径优先，缺失回退旧路径」把文件对齐成
	/// 「相同 / 已变 / 仅左 / 仅右」（<see cref="PatchFileDiffResult"/>），
	/// 提供「文件」表格 diff 视图，以及「预览」——并排双 WebView 真渲染两侧补丁内容
	/// （<see cref="PatchHtmlComposer"/> 组装的自包含 HTML，依赖宿主共享组件
	/// <c>ForkPlus.Plugins.Ui</c> 的 HtmlPreview）。
	///
	/// 路由：补丁是文本文件，宿主 5.0.4 起对**文本差异**同样查询插件路由
	/// （用户绑定 &gt; 精确扩展名认领，不走通配兜底）——.patch / .diff 默认即自动接管本视图；
	/// 用户绑定（偏好设置 → 扩展名绑定）可覆盖自动路由。
	///
	/// 零第三方依赖：补丁解析 / 文件级对齐 / 自包含 HTML 组装均为纯托管实现
	/// （System.* + Avalonia），随包进 <c>plugins/</c> 的只有插件自身主 DLL。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class PatchDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.patch";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "Patch Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "1.0.0";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "Patch Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Patch compare view plugin: claims .patch / .diff, parses both sides as unified / git diffs with a tolerant parser (headers, hunks, binary markers), aligns files by path into same / changed / left only / right only in a file-level diff table, and shows a side-by-side WebView preview rendering each side's patch content as self-contained HTML.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, PatchStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, PatchStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与其它领域插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[2] { ".patch", ".diff" };

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new PatchDiffView();
		}
	}
}
