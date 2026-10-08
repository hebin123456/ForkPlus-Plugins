using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Office
{
	/// <summary>
	/// Office 套件对比视图插件：认领 Word / Excel / PowerPoint 的现代 OOXML 格式
	/// （.docx/.xlsx/.pptx），命中后由 <see cref="OfficeDiffView"/> 用 Open XML SDK 提取正文内容，
	/// 左右两栏并排对比。
	///
	/// 不认领老格式 .doc/.ppt/.xls：宽松许可下没有能解析它们的 .NET 库（NPOI 仅覆盖 xls/xlsx/docx，
	/// 其余可选项均为商业库），故本插件只做现代格式。
	///
	/// 注意：宿主对二进制与文本差异均查询插件路由（v5.0.4 起含文本）。Office 文档是
	/// OOXML 包，git 一律判为二进制，因此会命中本插件而非 Hex 兜底。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class OfficeDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.office";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "Office";

		/// <summary>插件自身的版本号（与宿主版本解耦；0.1.0 起支持多语言）。</summary>
		public string Version => "0.1.0";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "Office Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "Office compare view plugin: claims Word (.docx), Excel (.xlsx) and PowerPoint (.pptx), extracts their content with the Open XML SDK and compares it side by side (paragraphs, tables, worksheet grids and slide text).";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, OfficeStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, OfficeStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与示例 / PDF 插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[3]
		{
			".docx",
			".xlsx",
			".pptx"
		};

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new OfficeDiffView();
		}
	}
}