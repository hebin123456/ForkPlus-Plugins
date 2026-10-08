using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ForkPlus.Plugins.Sqlite
{
	/// <summary>
	/// SQLite 数据库文件解析（纯托管，不依赖任何 SQLite 原生库 / 驱动）。
	///
	/// 只做「对比」需要的最小解析：
	/// <list type="bullet">
	/// <item>100 字节文件头——magic（"SQLite format 3\0"）、页大小、保留区、schema 格式号、
	/// 页数、文本编码。</item>
	/// <item>sqlite_master（根页 1 的 B 树）——逐条取 type / name / tbl_name / rootpage / sql，
	/// 即库里的表 / 索引 / 视图 / 触发器清单。</item>
	/// <item>CREATE TABLE 语句——抽出列名 / 类型 / 约束（INTEGER PRIMARY KEY 标注 rowid 别名）。</item>
	/// <item>各表根页的 B 树——按（变长整数 / 记录序列类型）解码数据行，支撑「数据」口径对比。</item>
	/// </list>
	///
	/// 出于对比展示的安全阈值：对象（表）最多 200 张、单表数据最多读 200 行，超出置
	/// <see cref="SqliteDocument.DataTruncated"/>；B 树遍历用「已访问页集合」防止损坏文件造成环。
	///
	/// 对外入口 <see cref="Parse"/> 不抛异常：失败返回 null 并回填 error，视图按「无法解析」提示处理。
	/// </summary>
	internal static class SqliteParser
	{
		/// <summary>库内对象（表）读取上限。</summary>
		private const int MaxTables = 200;

		/// <summary>单表数据行读取上限。</summary>
		private const int MaxDataRowsPerTable = 200;

		/// <summary>单个展示值的最大字符数（长 TEXT / BLOB 截断）。</summary>
		private const int MaxValueChars = 120;

		/// <summary>成功返回文档，失败返回 null 并回填 error（error = 异常类型名 + 消息）。</summary>
		internal static SqliteDocument Parse(byte[] data, bool readData, out string error)
		{
			error = null;
			try
			{
				return ParseCore(data, readData);
			}
			catch (Exception ex)
			{
				error = ex.GetType().Name + ": " + ex.Message;
				return null;
			}
		}

		private static SqliteDocument ParseCore(byte[] data, bool readData)
		{
			if (data == null || data.Length < 100)
			{
				throw new InvalidDataException("not a valid SQLite database: file too small");
			}
			string magic = Encoding.ASCII.GetString(data, 0, 16);
			if (!string.Equals(magic, "SQLite format 3\0", StringComparison.Ordinal))
			{
				throw new InvalidDataException("not a valid SQLite database: bad magic header");
			}

			int pageSize = ReadUInt16BE(data, 16);
			if (pageSize == 1)
			{
				pageSize = 65536;
			}
			if (pageSize < 512 || pageSize > 65536 || (pageSize & (pageSize - 1)) != 0)
			{
				throw new InvalidDataException("not a valid SQLite database: illegal page size " + pageSize);
			}
			int reserved = data[20];
			int usable = pageSize - reserved;
			if (usable < 480)
			{
				throw new InvalidDataException("not a valid SQLite database: illegal usable page size");
			}
			int schemaFormat = (int)ReadUInt32BE(data, 44);
			int textEncoding = (int)ReadUInt32BE(data, 56);
			Encoding encoding = ResolveEncoding(textEncoding);

			int pageCount = (int)ReadUInt32BE(data, 28);
			int availablePages = data.Length / pageSize;
			if (pageCount <= 0 || pageCount > availablePages)
			{
				pageCount = availablePages;
			}

			Ctx ctx = new Ctx(data, pageSize, usable, encoding);

			SqliteDocument document = new SqliteDocument
			{
				PageSize = pageSize,
				PageCount = pageCount,
				Encoding = EncodingName(textEncoding),
				SchemaFormat = schemaFormat
			};

			// sqlite_master 在根页 1：逐条读出库对象。
			List<SqliteObject> objects = new List<SqliteObject>();
			WalkTableTree(ctx, 1L, delegate (long rowid, SqliteRecord record)
			{
				if (objects.Count >= MaxTables)
				{
					return;
				}
				SqliteObject item = new SqliteObject
				{
					Type = RecordText(record, 0),
					Name = RecordText(record, 1),
					TableName = RecordText(record, 2),
					RootPage = RecordInt(record, 3),
					Sql = RecordText(record, 4)
				};
				if (string.IsNullOrEmpty(item.Type) || string.IsNullOrEmpty(item.Name))
				{
					return;
				}
				if (string.Equals(item.Type, "table", StringComparison.Ordinal) && !string.IsNullOrEmpty(item.Sql))
				{
					item.Columns = ParseColumns(item.Sql);
				}
				objects.Add(item);
			});

			IndexObjects(objects, document);

			// 表结构口径：头部统计 + 每对象一行（表另逐列一行）。
			BuildSchemaRows(objects, document);

			// 数据口径：逐表读 B 树（仅在需要时读，避免表结构模式白跑）。
			if (readData)
			{
				BuildDataRows(ctx, objects, document);
			}

			return document;
		}

		// ---- sqlite_master 归类统计 ----

		private static void IndexObjects(List<SqliteObject> objects, SqliteDocument document)
		{
			foreach (SqliteObject item in objects)
			{
				switch (item.Type)
				{
				case "table":
					document.TableCount++;
					break;
				case "index":
					document.IndexCount++;
					break;
				case "view":
					document.ViewCount++;
					break;
				case "trigger":
					document.TriggerCount++;
					break;
				}
			}
		}

		// ---- 表结构口径 ----

		private static void BuildSchemaRows(List<SqliteObject> objects, SqliteDocument document)
		{
			List<SqliteRow> rows = document.SchemaRows;
			rows.Add(new SqliteRow { Path = "page size", Value = document.PageSize.ToString(CultureInfo.InvariantCulture) });
			rows.Add(new SqliteRow { Path = "text encoding", Value = document.Encoding });
			rows.Add(new SqliteRow { Path = "schema format", Value = document.SchemaFormat.ToString(CultureInfo.InvariantCulture) });
			rows.Add(new SqliteRow { Path = "page count", Value = document.PageCount.ToString(CultureInfo.InvariantCulture) });
			rows.Add(new SqliteRow { Path = "tables", Value = document.TableCount.ToString(CultureInfo.InvariantCulture) });
			rows.Add(new SqliteRow { Path = "indexes", Value = document.IndexCount.ToString(CultureInfo.InvariantCulture) });
			rows.Add(new SqliteRow { Path = "views", Value = document.ViewCount.ToString(CultureInfo.InvariantCulture) });
			rows.Add(new SqliteRow { Path = "triggers", Value = document.TriggerCount.ToString(CultureInfo.InvariantCulture) });

			foreach (SqliteObject item in objects)
			{
				switch (item.Type)
				{
				case "table":
				{
					int indexCount = CountIndexesOn(objects, item.Name);
					rows.Add(new SqliteRow
					{
						Path = "table." + item.Name,
						Value = Plural(item.Columns.Count, "column") + " · " + Plural(indexCount, "index")
					});
					foreach (SqliteColumn column in item.Columns)
					{
						rows.Add(new SqliteRow
						{
							Path = "table." + item.Name + ".column." + column.Name,
							Value = string.IsNullOrEmpty(column.Type) ? "(no type)" : column.Type
						});
					}
					break;
				}
				case "index":
					rows.Add(new SqliteRow { Path = "index." + item.Name, Value = DescribeIndex(item) });
					break;
				case "view":
					rows.Add(new SqliteRow { Path = "view." + item.Name, Value = NormalizeSql(item.Sql) });
					break;
				case "trigger":
					rows.Add(new SqliteRow { Path = "trigger." + item.Name, Value = NormalizeSql(item.Sql) });
					break;
				}
			}
		}

		private static int CountIndexesOn(List<SqliteObject> objects, string tableName)
		{
			int count = 0;
			foreach (SqliteObject item in objects)
			{
				if (item.Type == "index" && string.Equals(item.TableName, tableName, StringComparison.Ordinal))
				{
					count++;
				}
			}
			return count;
		}

		/// <summary>索引描述：UNIQUE 前缀 + "on <table>(<cols>)"（从 CREATE INDEX 语句截取 ON 之后）。</summary>
		private static string DescribeIndex(SqliteObject item)
		{
			string unique = (item.Sql != null && item.Sql.IndexOf("UNIQUE", StringComparison.OrdinalIgnoreCase) >= 0) ? "UNIQUE " : string.Empty;
			string target = ExtractAfterOn(item.Sql);
			if (string.IsNullOrEmpty(target))
			{
				target = item.TableName ?? string.Empty;
			}
			return unique + "on " + target;
		}

		/// <summary>取 CREATE INDEX 语句里 "ON" 之后的片段（含表名与列括号），并折叠空白。</summary>
		private static string ExtractAfterOn(string sql)
		{
			if (string.IsNullOrEmpty(sql))
			{
				return null;
			}
			int on = sql.IndexOf(" ON ", StringComparison.OrdinalIgnoreCase);
			if (on < 0)
			{
				return null;
			}
			return CollapseWhitespace(sql.Substring(on + 4));
		}

		// ---- 数据口径 ----

		private static void BuildDataRows(Ctx ctx, List<SqliteObject> objects, SqliteDocument document)
		{
			int tablesRead = 0;
			foreach (SqliteObject item in objects)
			{
				if (item.Type != "table" || item.RootPage <= 0)
				{
					continue;
				}
				if (tablesRead >= MaxTables)
				{
					document.DataTruncated = true;
					break;
				}
				tablesRead++;

				SqliteObject table = item;
				int read = 0;
				ctx.ResetVisited();
				WalkTableTree(ctx, item.RootPage, delegate (long rowid, SqliteRecord record)
				{
					table.RowCount++;
					if (read < MaxDataRowsPerTable)
					{
						read++;
						document.DataRows.Add(new SqliteRow
						{
							Path = "data." + table.Name + "[" + rowid.ToString(CultureInfo.InvariantCulture) + "]",
							Value = FormatRow(table, rowid, record)
						});
					}
					else if (!table.DataTruncated)
					{
						table.DataTruncated = true;
						document.DataTruncated = true;
					}
				});
				document.TotalDataRows += table.RowCount;
				document.DataRows.Add(new SqliteRow
				{
					Path = "rows." + table.Name,
					Value = table.RowCount.ToString(CultureInfo.InvariantCulture) + (table.DataTruncated ? " (truncated)" : string.Empty)
				});
			}
		}

		/// <summary>一行数据的展示值："col=val, col=val…"；INTEGER PRIMARY KEY 列为 NULL 时用 rowid 回填。</summary>
		private static string FormatRow(SqliteObject table, long rowid, SqliteRecord record)
		{
			StringBuilder builder = new StringBuilder();
			for (int i = 0; i < table.Columns.Count; i++)
			{
				SqliteColumn column = table.Columns[i];
				object value = (record != null && i < record.Values.Length) ? record.Values[i] : null;
				if (value == null && column.IntegerPrimaryKey)
				{
					value = rowid;
				}
				if (builder.Length > 0)
				{
					builder.Append(", ");
				}
				builder.Append(column.Name).Append('=').Append(FormatValue(value));
			}
			// 记录比列多（异常 / 生成列）时把余下值一并挂上，避免信息丢失。
			if (record != null && record.Values.Length > table.Columns.Count)
			{
				for (int i = table.Columns.Count; i < record.Values.Length; i++)
				{
					if (builder.Length > 0)
					{
						builder.Append(", ");
					}
					builder.Append('?').Append(i + 1).Append('=').Append(FormatValue(record.Values[i]));
				}
			}
			return builder.ToString();
		}

		/// <summary>记录值格式化（NULL / 整数 / 浮点 / 文本 / BLOB）。</summary>
		private static string FormatValue(object value)
		{
			if (value == null)
			{
				return "NULL";
			}
			if (value is long number)
			{
				return number.ToString(CultureInfo.InvariantCulture);
			}
			if (value is double real)
			{
				return real.ToString("R", CultureInfo.InvariantCulture);
			}
			if (value is byte[] blob)
			{
				return "blob(" + blob.Length.ToString(CultureInfo.InvariantCulture) + " bytes) " + HexPrefix(blob);
			}
			return Truncate(CollapseWhitespace(Convert.ToString(value, CultureInfo.InvariantCulture)), MaxValueChars);
		}

		/// <summary>BLOB 前缀十六进制（最多 8 字节），如 x'0a0b0c0d'。</summary>
		private static string HexPrefix(byte[] blob)
		{
			if (blob.Length == 0)
			{
				return "x''";
			}
			int count = Math.Min(blob.Length, 8);
			StringBuilder builder = new StringBuilder(count * 2 + 3);
			builder.Append("x'");
			for (int i = 0; i < count; i++)
			{
				builder.Append(blob[i].ToString("x2", CultureInfo.InvariantCulture));
			}
			if (blob.Length > count)
			{
				builder.Append("…");
			}
			builder.Append('\'');
			return builder.ToString();
		}

		// ---- B 树遍历 ----

		/// <summary>解析上下文：页大小 / 可用大小 / 文本编码 / 已访问页集合。</summary>
		private sealed class Ctx
		{
			private readonly HashSet<long> _visited = new HashSet<long>();

			internal readonly byte[] Data;

			internal readonly int PageSize;

			internal readonly int Usable;

			internal readonly Encoding Encoding;

			internal Ctx(byte[] data, int pageSize, int usable, Encoding encoding)
			{
				Data = data;
				PageSize = pageSize;
				Usable = usable;
				Encoding = encoding;
			}

			internal long PageBase(long pageNo)
			{
				return (pageNo - 1) * (long)PageSize;
			}

			internal bool MarkVisited(long pageNo)
			{
				return _visited.Add(pageNo);
			}

			internal void ResetVisited()
			{
				_visited.Clear();
			}

			internal int PageCount => (int)(Data.Length / PageSize);
		}

		/// <summary>遍历一棵「表」B 树（页类型 5 内部 / 13 叶子），按 rowid 升序回调每行记录。</summary>
		private static void WalkTableTree(Ctx ctx, long pageNo, Action<long, SqliteRecord> visit)
		{
			if (pageNo <= 0 || pageNo > ctx.PageCount || !ctx.MarkVisited(pageNo))
			{
				return;
			}
			long pageBase = ctx.PageBase(pageNo);
			int headerOffset = (pageNo == 1) ? 100 : 0;
			if (pageBase + headerOffset + 8 > ctx.Data.Length)
			{
				return;
			}
			byte pageType = ctx.Data[pageBase + headerOffset];
			int cellCount = ReadUInt16BE(ctx.Data, (int)(pageBase + headerOffset + 3));

			if (pageType == 13)
			{
				// 表叶子：cell = varint payload 长度 + varint rowid + payload（可能溢出到后续页）。
				int pointerArray = (int)(pageBase + headerOffset + 8);
				for (int i = 0; i < cellCount; i++)
				{
					int cellOffset = ReadUInt16BE(ctx.Data, pointerArray + i * 2);
					long cellBase = pageBase + cellOffset;
					int cursor = (int)cellBase;
					long payloadLength = ReadVarint(ctx.Data, ref cursor);
					long rowid = ReadVarint(ctx.Data, ref cursor);
					byte[] payload = ReadPayload(ctx, pageNo, cursor - (int)pageBase, payloadLength);
					visit(rowid, DecodeRecord(payload, ctx.Encoding));
				}
				return;
			}
			if (pageType == 5)
			{
				// 表内部：cell = 4 字节左子页 + varint rowid；先递归左子，再走最右指针。
				int pointerArray = (int)(pageBase + headerOffset + 12);
				for (int i = 0; i < cellCount; i++)
				{
					int cellOffset = ReadUInt16BE(ctx.Data, pointerArray + i * 2);
					long cellBase = pageBase + cellOffset;
					long child = ReadUInt32BE(ctx.Data, (int)cellBase);
					WalkTableTree(ctx, child, visit);
				}
				long rightmost = ReadUInt32BE(ctx.Data, (int)(pageBase + headerOffset + 8));
				WalkTableTree(ctx, rightmost, visit);
			}
			// 索引页（2 / 10）不参与数据口径读取。
		}

		/// <summary>读取带溢出的 payload：先取页内本地部分，再沿溢出页链补齐。</summary>
		private static byte[] ReadPayload(Ctx ctx, long pageNo, int payloadStartInPage, long payloadLength)
		{
			if (payloadLength <= 0)
			{
				return Array.Empty<byte>();
			}
			int usable = ctx.Usable;
			long maxLocal = usable - 35;
			long minLocal = ((usable - 12) * 32L / 255L) - 23L;
			long local;
			if (payloadLength <= maxLocal)
			{
				local = payloadLength;
			}
			else
			{
				long k = minLocal + (payloadLength - minLocal) % (usable - 4);
				local = (k <= maxLocal) ? k : minLocal;
			}

			byte[] buffer = new byte[payloadLength];
			long baseOffset = ctx.PageBase(pageNo);
			int copied = (int)Math.Min(local, payloadLength);
			Array.Copy(ctx.Data, baseOffset + payloadStartInPage, buffer, 0, copied);

			if (copied >= payloadLength)
			{
				return buffer;
			}
			if (baseOffset + payloadStartInPage + local + 4 > ctx.Data.Length)
			{
				return buffer;
			}
			long overflow = ReadUInt32BE(ctx.Data, (int)(baseOffset + payloadStartInPage + local));
			int guard = 0;
			while (overflow > 0 && overflow <= ctx.PageCount && copied < payloadLength && guard++ < 100000)
			{
				long overflowBase = ctx.PageBase(overflow);
				if (overflowBase + 4 > ctx.Data.Length)
				{
					break;
				}
				int chunk = (int)Math.Min(usable - 4, payloadLength - copied);
				if (overflowBase + 4 + chunk > ctx.Data.Length)
				{
					chunk = (int)Math.Max(0, ctx.Data.Length - overflowBase - 4);
				}
				Array.Copy(ctx.Data, overflowBase + 4, buffer, copied, chunk);
				copied += chunk;
				overflow = ReadUInt32BE(ctx.Data, (int)overflowBase);
			}
			return buffer;
		}

		// ---- 记录（record）解码 ----

		private static SqliteRecord DecodeRecord(byte[] payload, Encoding encoding)
		{
			SqliteRecord record = new SqliteRecord();
			if (payload == null || payload.Length == 0)
			{
				record.Values = Array.Empty<object>();
				return record;
			}
			int offset = 0;
			long headerSize = ReadVarint(payload, ref offset);
			if (headerSize < 1 || headerSize > payload.Length)
			{
				headerSize = payload.Length;
			}
			int headerEnd = (int)headerSize;
			List<long> serialTypes = new List<long>();
			while (offset < headerEnd)
			{
				serialTypes.Add(ReadVarint(payload, ref offset));
			}
			object[] values = new object[serialTypes.Count];
			int body = headerEnd;
			for (int i = 0; i < serialTypes.Count; i++)
			{
				values[i] = ReadValue(payload, ref body, serialTypes[i], encoding);
			}
			record.Values = values;
			return record;
		}

		private static object ReadValue(byte[] data, ref int offset, long serialType, Encoding encoding)
		{
			if (serialType == 0)
			{
				return null;
			}
			if (serialType >= 1 && serialType <= 6)
			{
				int size = (int)serialType;
				long value = 0;
				for (int i = 0; i < size; i++)
				{
					value = (value << 8) | (long)(data[offset + i] & 0xFF);
				}
				// 符号扩展。
				int shift = (8 - size) * 8;
				value = (value << shift) >> shift;
				offset += size;
				return value;
			}
			if (serialType == 7)
			{
				long bits = 0;
				for (int i = 0; i < 8; i++)
				{
					bits = (bits << 8) | (long)(data[offset + i] & 0xFF);
				}
				offset += 8;
				return BitConverter.Int64BitsToDouble(bits);
			}
			if (serialType == 8)
			{
				return 0L;
			}
			if (serialType == 9)
			{
				return 1L;
			}
			if (serialType >= 12)
			{
				long length = (serialType - 12) / 2;
				int count = (int)Math.Min(length, Math.Max(0, data.Length - offset));
				byte[] bytes = new byte[count];
				Array.Copy(data, offset, bytes, 0, count);
				offset += count;
				if ((serialType & 1) == 1)
				{
					return encoding.GetString(bytes);
				}
				return bytes;
			}
			return null;
		}

		// ---- CREATE TABLE 列解析 ----

		/// <summary>抽 CREATE TABLE 的列定义：列名 + 规范化类型 / 约束；表级约束（PRIMARY KEY(...) 等）跳过。</summary>
		internal static List<SqliteColumn> ParseColumns(string createTableSql)
		{
			List<SqliteColumn> columns = new List<SqliteColumn>();
			string body = ExtractParenBody(createTableSql);
			if (string.IsNullOrEmpty(body))
			{
				return columns;
			}
			foreach (string part in SplitTopLevel(body))
			{
				string definition = part.Trim();
				if (definition.Length == 0)
				{
					continue;
				}
				if (IsTableConstraint(definition))
				{
					continue;
				}
				SqliteColumn column = ParseColumn(definition);
				if (column != null)
				{
					columns.Add(column);
				}
			}
			return columns;
		}

		private static bool IsTableConstraint(string definition)
		{
			string upper = definition.TrimStart().ToUpperInvariant();
			return upper.StartsWith("PRIMARY ", StringComparison.Ordinal)
				|| upper.StartsWith("PRIMARY(", StringComparison.Ordinal)
				|| upper.StartsWith("UNIQUE", StringComparison.Ordinal)
				|| upper.StartsWith("CHECK", StringComparison.Ordinal)
				|| upper.StartsWith("FOREIGN", StringComparison.Ordinal)
				|| upper.StartsWith("CONSTRAINT", StringComparison.Ordinal);
		}

		private static SqliteColumn ParseColumn(string definition)
		{
			int cursor = 0;
			string name = ReadIdentifier(definition, ref cursor);
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}
			string rest = definition.Substring(cursor).Trim();
			string upper = " " + rest.ToUpperInvariant() + " ";

			SqliteColumn column = new SqliteColumn { Name = name };

			// 类型：取到第一个约束关键字之前。
			List<string> typeTokens = new List<string>();
			foreach (string token in rest.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (IsConstraintKeyword(token))
				{
					break;
				}
				typeTokens.Add(token.ToUpperInvariant());
			}
			List<string> constraints = new List<string>();
			if (upper.Contains(" PRIMARY KEY ") || upper.EndsWith(" PRIMARY KEY "))
			{
				constraints.Add("PRIMARY KEY");
				column.IntegerPrimaryKey = string.Join(" ", typeTokens).Equals("INTEGER", StringComparison.OrdinalIgnoreCase);
			}
			if (upper.Contains(" NOT NULL "))
			{
				constraints.Add("NOT NULL");
			}
			if (upper.Contains(" UNIQUE "))
			{
				constraints.Add("UNIQUE");
			}
			if (upper.Contains(" AUTOINCREMENT "))
			{
				constraints.Add("AUTOINCREMENT");
			}

			StringBuilder builder = new StringBuilder();
			builder.Append(string.Join(" ", typeTokens));
			foreach (string constraint in constraints)
			{
				if (builder.Length > 0)
				{
					builder.Append(' ');
				}
				builder.Append(constraint);
			}
			column.Type = builder.ToString().Trim();
			return column;
		}

		private static bool IsConstraintKeyword(string token)
		{
			switch (token.ToUpperInvariant())
			{
			case "PRIMARY":
			case "NOT":
			case "NULL":
			case "UNIQUE":
			case "CHECK":
			case "DEFAULT":
			case "COLLATE":
			case "REFERENCES":
			case "GENERATED":
			case "AS":
			case "AUTOINCREMENT":
			case "CONSTRAINT":
				return true;
			default:
				return false;
			}
		}

		/// <summary>读列名 / 表名：支持裸露、双引号、方括号、反引号、单引号四种写法。</summary>
		private static string ReadIdentifier(string text, ref int cursor)
		{
			while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
			{
				cursor++;
			}
			if (cursor >= text.Length)
			{
				return null;
			}
			char first = text[cursor];
			char closing = '\0';
			switch (first)
			{
			case '"':
				closing = '"';
				break;
			case '\'':
				closing = '\'';
				break;
			case '`':
				closing = '`';
				break;
			case '[':
				closing = ']';
				break;
			}
			if (closing != '\0')
			{
				int start = ++cursor;
				while (cursor < text.Length && text[cursor] != closing)
				{
					cursor++;
				}
				string quoted = text.Substring(start, cursor - start);
				if (cursor < text.Length)
				{
					cursor++;
				}
				return quoted;
			}
			int plainStart = cursor;
			while (cursor < text.Length && !char.IsWhiteSpace(text[cursor]) && text[cursor] != '(' && text[cursor] != ',')
			{
				cursor++;
			}
			return text.Substring(plainStart, cursor - plainStart);
		}

		/// <summary>取最外层括号内的内容（CREATE TABLE 的列定义体）。</summary>
		private static string ExtractParenBody(string sql)
		{
			if (string.IsNullOrEmpty(sql))
			{
				return null;
			}
			int open = sql.IndexOf('(');
			if (open < 0)
			{
				return null;
			}
			int depth = 0;
			char quote = '\0';
			for (int i = open; i < sql.Length; i++)
			{
				char c = sql[i];
				if (quote != '\0')
				{
					if (c == quote)
					{
						quote = '\0';
					}
					continue;
				}
				switch (c)
				{
				case '\'':
				case '"':
				case '`':
					quote = c;
					continue;
				case '(':
					depth++;
					continue;
				case ')':
					depth--;
					if (depth == 0)
					{
						return sql.Substring(open + 1, i - open - 1);
					}
					continue;
				}
			}
			return sql.Substring(open + 1);
		}

		/// <summary>按顶层逗号切分（忽略括号内 / 引号内的逗号）。</summary>
		private static List<string> SplitTopLevel(string body)
		{
			List<string> parts = new List<string>();
			int depth = 0;
			char quote = '\0';
			int start = 0;
			for (int i = 0; i < body.Length; i++)
			{
				char c = body[i];
				if (quote != '\0')
				{
					if (c == quote)
					{
						quote = '\0';
					}
					continue;
				}
				switch (c)
				{
				case '\'':
				case '"':
				case '`':
					quote = c;
					continue;
				case '(':
					depth++;
					continue;
				case ')':
					depth--;
					continue;
				case ',':
					if (depth == 0)
					{
						parts.Add(body.Substring(start, i - start));
						start = i + 1;
					}
					continue;
				}
			}
			parts.Add(body.Substring(start));
			return parts;
		}

		// ---- 编码 / 文本小工具 ----

		private static Encoding ResolveEncoding(int textEncoding)
		{
			switch (textEncoding)
			{
			case 2:
				return new UnicodeEncoding(false, true, true);
			case 3:
				return new UnicodeEncoding(true, true, true);
			default:
				return new UTF8Encoding(false, false);
			}
		}

		private static string EncodingName(int textEncoding)
		{
			switch (textEncoding)
			{
			case 2:
				return "UTF-16le";
			case 3:
				return "UTF-16be";
			default:
				return "UTF-8";
			}
		}

		private static string Plural(int count, string noun)
		{
			return count.ToString(CultureInfo.InvariantCulture) + " " + noun + (count == 1 ? string.Empty : "s");
		}

		private static string NormalizeSql(string sql)
		{
			return Truncate(CollapseWhitespace(sql), MaxValueChars);
		}

		private static string CollapseWhitespace(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}
			StringBuilder builder = new StringBuilder(value.Length);
			bool previousSpace = false;
			foreach (char c in value)
			{
				if (char.IsWhiteSpace(c))
				{
					if (!previousSpace && builder.Length > 0)
					{
						builder.Append(' ');
					}
					previousSpace = true;
					continue;
				}
				previousSpace = false;
				builder.Append(c);
			}
			return builder.ToString().Trim();
		}

		private static string Truncate(string value, int maxChars)
		{
			if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
			{
				return value;
			}
			return value.Substring(0, maxChars) + "…";
		}

		// ---- 记录字段读取 ----

		private static string RecordText(SqliteRecord record, int index)
		{
			if (record == null || index < 0 || index >= record.Values.Length)
			{
				return null;
			}
			object value = record.Values[index];
			if (value is string text)
			{
				return text;
			}
			return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
		}

		private static long RecordInt(SqliteRecord record, int index)
		{
			if (record == null || index < 0 || index >= record.Values.Length)
			{
				return 0L;
			}
			object value = record.Values[index];
			if (value is long number)
			{
				return number;
			}
			if (value is double real)
			{
				return (long)real;
			}
			if (value is string text && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
			{
				return parsed;
			}
			return 0L;
		}

		// ---- 定长整数 / 变长整数 ----

		private static int ReadUInt16BE(byte[] data, int offset)
		{
			return (data[offset] << 8) | data[offset + 1];
		}

		private static long ReadUInt32BE(byte[] data, int offset)
		{
			return ((long)data[offset] << 24) | ((long)data[offset + 1] << 16) | ((long)data[offset + 2] << 8) | (long)data[offset + 3];
		}

		/// <summary>读变长整数（1–9 字节，大端 base-128；第 9 字节用满 8 位）。</summary>
		private static long ReadVarint(byte[] data, ref int offset)
		{
			long result = 0;
			for (int i = 0; i < 8; i++)
			{
				byte b = data[offset++];
				result = (result << 7) | (long)(b & 0x7F);
				if ((b & 0x80) == 0)
				{
					return result;
				}
			}
			byte last = data[offset++];
			return (result << 8) | (long)(last & 0xFF);
		}
	}

	/// <summary>解码后的一条记录：按列顺序排列的值（null / long / double / string / byte[]）。</summary>
	internal sealed class SqliteRecord
	{
		internal object[] Values = Array.Empty<object>();
	}
}