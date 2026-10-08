using System.Collections.Generic;
using ForkPlus.Plugins.Abstractions;

namespace ForkPlus.Plugins.Sqlite
{
	/// <summary>
	/// SQLite 数据库对比视图插件：认领 .db / .sqlite / .sqlite3，命中后由 <see cref="SqliteDiffView"/>
	/// 纯托管解析 SQLite 数据库文件（100 字节文件头 + 各页 B 树 + 记录格式），把两侧库的
	/// 「表结构」与「数据」分别对比。
	///
	/// 路由：SQLite 数据库是二进制（页头含 NUL），git 判为二进制，宿主只对**二进制差异**查询插件路由
	/// —— .db 必命中本视图；用户绑定（偏好设置 → 扩展名绑定）可覆盖自动路由。
	/// 之所以实现，是因为改表结构 / 改配置数据之后，整库二进制 diff 什么都看不出，解开页结构对比才有意义。
	///
	/// 零第三方依赖：不引入任何 SQLite 原生库 / 托管驱动，SQLite 文件格式（文件头 / B 树页 / 变长整数 /
	/// 记录序列类型）全部自解析；随包进 <c>plugins/</c> 的只有插件自身主 DLL（不需要 third-party.json）。
	///
	/// 另实现 <see cref="IPluginMetadata"/>（可选）向宿主「偏好设置 → 插件」页提供名称/版本/描述。
	/// </summary>
	public sealed class SqliteDiffPlugin : IDiffViewPlugin, IPluginMetadata
	{
		/// <summary>插件唯一 Id。</summary>
		public string Id => "forkplus.sqlite";

		/// <summary>显示名的翻译 key（宿主「扩展名绑定」列表用；未接线时按原文显示）。</summary>
		public string DisplayNameKey => "SQLite Compare";

		/// <summary>插件自身的版本号（与宿主版本解耦）。</summary>
		public string Version => "0.0.1";

		/// <summary>插件显示名（英文原文，同时作为多语言缺省值）。</summary>
		public string DisplayName => "SQLite Compare";

		/// <summary>一句话描述插件能力（英文原文，同时作为多语言缺省值）。</summary>
		public string Description => "SQLite database compare view plugin: claims .db / .sqlite / .sqlite3, parses the SQLite file format in pure managed code (file header, B-tree pages, record serial types) with no native driver, and compares table schemas and row data side by side.";

		/// <summary>v5.0.3：按界面语言取显示名（未覆盖的语言回退英文原文）。</summary>
		public string GetDisplayName(string language)
		{
			return PluginLocalization.Resolve(language, SqliteStrings.DisplayNames, DisplayName);
		}

		/// <summary>v5.0.3：按界面语言取描述（未覆盖的语言回退英文原文）。</summary>
		public string GetDescription(string language)
		{
			return PluginLocalization.Resolve(language, SqliteStrings.Descriptions, Description);
		}

		/// <summary>高于内置通配兜底（Hex，0）；与其它领域插件同档。</summary>
		public int Priority => 100;

		/// <summary>认领的扩展名（小写含点）。</summary>
		public IReadOnlyList<string> FileExtensions => new string[3] { ".db", ".sqlite", ".sqlite3" };

		/// <summary>宿主在此阶段尚未加载字节，扩展名已足够判定，一律接受。</summary>
		public bool CanHandle(DiffViewRequest request)
		{
			return true;
		}

		/// <summary>每次对比创建独立视图实例。</summary>
		public IDiffView CreateView()
		{
			return new SqliteDiffView();
		}
	}
}