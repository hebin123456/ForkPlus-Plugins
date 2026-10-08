using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Dbc
{
	/// <summary>
	/// DBC（CAN 数据库）对比视图插件：认领 <c>.dbc</c>，命中后由 <see cref="DbcDiffView"/>
	/// 用第三方库 <c>DbcParserLib</c> 把两侧解析成同一套数据模型，按键路径做语义 diff——
	/// 报文 / 信号 / 环境变量的新增、删除、改值逐行标注，并附带一份逐字符的原文对照。
	///
	/// 路由限制：宿主只对**二进制差异**查询插件路由，纯文本差异固定由内置文本编辑器渲染。
	/// <c>.dbc</c> 是文本格式，因此**自动路由通常不会命中**；要使用本视图，需在宿主
	/// 「偏好设置 → 扩展名绑定」里把 <c>.dbc</c> 绑定到本插件（用户绑定优先级最高）。
	/// 之所以仍然实现，是因为 DBC 的「报文 / 信号级差异」（哪个信号改了起始位、因子、取值范围）
	/// 用文本 diff 很难读；绑定后能直接看到结构化变更。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class DbcDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.dbc";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "DBC Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "0.0.1";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "DBC Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "DBC (CAN database) compare view plugin: claims .dbc, parses both sides with DbcParserLib into one data model and compares nodes / messages / signals / environment variables semantically — added / removed / changed — as a key-path table, plus a raw text side-by-side view.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, DbcStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, DbcStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与其它领域插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的 CAN 数据库扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[1] { ".dbc" };

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new DbcDiffView();
		}
	}
}
