using System;
using System.Collections.Generic;

namespace ForkPlus.Plugins.Sqlite
{
	/// <summary>键路径对比表的一行：稳定键路径 + 展示值（表结构 / 数据共用同一行模型）。</summary>
	internal sealed class SqliteRow
	{
		internal string Path;

		internal string Value;
	}

	/// <summary>CREATE TABLE 解析出的一列：名字 + 规范化后的类型 / 约束描述。</summary>
	internal sealed class SqliteColumn
	{
		internal string Name;

		/// <summary>规范化描述，如 "INTEGER PRIMARY KEY" / "TEXT NOT NULL"。</summary>
		internal string Type;

		/// <summary>是否 INTEGER PRIMARY KEY（rowid 别名，记录里该列存 NULL，展示时用 rowid 回填）。</summary>
		internal bool IntegerPrimaryKey;
	}

	/// <summary>sqlite_master 里的一条记录（表 / 索引 / 视图 / 触发器）。</summary>
	internal sealed class SqliteObject
	{
		internal string Type;

		internal string Name;

		internal string TableName;

		internal long RootPage;

		internal string Sql;

		/// <summary>表：解析出的列（非表为空）。</summary>
		internal List<SqliteColumn> Columns = new List<SqliteColumn>();

		/// <summary>表：按 B 树读出的数据行键路径（Path / Value，Path 形如 "data.users[1]"）。</summary>
		internal List<SqliteRow> DataRows = new List<SqliteRow>();

		/// <summary>表：数据行是否被截断（超过单表上限）。</summary>
		internal bool DataTruncated;

		internal int RowCount;
	}

	/// <summary>一次 SQLite 解析的结果：头部统计 + 对象清单 + 两种口径的键路径行。</summary>
	internal sealed class SqliteDocument
	{
		/// <summary>表结构口径的键路径行（头部统计 + 每个对象的列 / 索引 / 视图 / 触发器）。</summary>
		internal List<SqliteRow> SchemaRows = new List<SqliteRow>();

		/// <summary>数据口径的键路径行（每张表一张行的逐行键路径）。</summary>
		internal List<SqliteRow> DataRows = new List<SqliteRow>();

		internal int PageSize;

		internal int PageCount;

		internal string Encoding;

		internal int SchemaFormat;

		internal int TableCount;

		internal int IndexCount;

		internal int ViewCount;

		internal int TriggerCount;

		/// <summary>数据行的总条数（两侧统计展示用）。</summary>
		internal long TotalDataRows;

		/// <summary>数据读取是否被截断（超出表数 / 单表行数上限）。</summary>
		internal bool DataTruncated;
	}

	internal enum SqliteState
	{
		Same,
		Changed,
		LeftOnly,
		RightOnly
	}

	/// <summary>键路径对齐后的一行：键路径 + 旧 / 新两侧的值 + 变更状态。</summary>
	internal sealed class SqliteRowDiff
	{
		internal string Path;

		internal string Left;

		internal string Right;

		internal SqliteState State;
	}

	/// <summary>键路径 diff 结果：Rows 为键路径并集的四态行，计数用于状态行汇总。</summary>
	internal sealed class SqliteDiffResult
	{
		internal List<SqliteRowDiff> Rows = new List<SqliteRowDiff>();

		internal int Changed;

		internal int LeftOnly;

		internal int RightOnly;

		internal int Total => Rows.Count;

		internal bool HasDifferences => Changed > 0 || LeftOnly > 0 || RightOnly > 0;
	}

	/// <summary>
	/// SQLite 对齐：两侧键路径行取并集——两侧都有且值相等 = same、都有但不等 = changed、
	/// 仅左侧有 = left only、仅右侧有 = right only；行序保持左侧解析顺序，再追加仅右侧的键。
	/// </summary>
	internal static class SqliteDiff
	{
		internal static SqliteDiffResult ComputeRows(List<SqliteRow> left, List<SqliteRow> right)
		{
			SqliteDiffResult result = new SqliteDiffResult();
			Dictionary<string, SqliteRow> rightMap = new Dictionary<string, SqliteRow>(StringComparer.Ordinal);
			if (right != null)
			{
				foreach (SqliteRow row in right)
				{
					if (!string.IsNullOrEmpty(row.Path) && !rightMap.ContainsKey(row.Path))
					{
						rightMap.Add(row.Path, row);
					}
				}
			}
			HashSet<string> consumed = new HashSet<string>(StringComparer.Ordinal);
			if (left != null)
			{
				foreach (SqliteRow row in left)
				{
					if (string.IsNullOrEmpty(row.Path) || consumed.Contains(row.Path))
					{
						continue;
					}
					consumed.Add(row.Path);
					if (rightMap.TryGetValue(row.Path, out SqliteRow match))
					{
						bool same = string.Equals(row.Value ?? string.Empty, match.Value ?? string.Empty, StringComparison.Ordinal);
						Add(result, row.Path, row.Value, match.Value, same ? SqliteState.Same : SqliteState.Changed);
					}
					else
					{
						Add(result, row.Path, row.Value, null, SqliteState.LeftOnly);
					}
				}
			}
			if (right != null)
			{
				foreach (SqliteRow row in right)
				{
					if (string.IsNullOrEmpty(row.Path) || consumed.Contains(row.Path))
					{
						continue;
					}
					consumed.Add(row.Path);
					Add(result, row.Path, null, row.Value, SqliteState.RightOnly);
				}
			}
			return result;
		}

		private static void Add(SqliteDiffResult result, string path, string left, string right, SqliteState state)
		{
			result.Rows.Add(new SqliteRowDiff
			{
				Path = path,
				Left = left,
				Right = right,
				State = state
			});
			switch (state)
			{
			case SqliteState.Changed:
				result.Changed++;
				break;
			case SqliteState.LeftOnly:
				result.LeftOnly++;
				break;
			case SqliteState.RightOnly:
				result.RightOnly++;
				break;
			}
		}
	}
}