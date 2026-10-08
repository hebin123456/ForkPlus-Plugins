using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using DbcParserLib;
using DbcFile = DbcParserLib.Dbc;
using DbcParserLib.Model;
using DbcParserLib.Observers;

namespace ForkPlus.Plugins.Dbc
{
	/// <summary>
	/// 把 DBC（CAN 数据库）文本经第三方库 <c>DbcParserLib</c> 解析后，转成与结构化插件同源的
	/// 统一 <see cref="DataNode"/> 树，交由 <see cref="DataDiff"/> 按键路径做语义 diff。
	///
	/// 覆盖对象：
	/// <list type="bullet">
	/// <item>节点（BU_）：名称、注释、属性（BA_）；</item>
	/// <item>报文（BO_）：id（十进制 / 十六进制 / 扩展帧）、DLC、发送方（含附加发送方）、注释、
	/// 属性、下属信号；报文按 id 排序，保证两侧行序稳定；</item>
	/// <item>信号（SG_）：起始位、长度、字节序（大端 Motorola / 小端 Intel）、数值类型
	/// （有符号 / 无符号 / 浮点）、因子、偏移、取值范围、单位、初值、多路复用、接收方、
	/// 值表（VAL_）、属性；</item>
	/// <item>环境变量（EV_）：类型、访问权限、单位、取值范围 / 初值、注释、值表、属性；</item>
	/// <item>全局属性（BA_DEF_ / BA_）。</item>
	/// </list>
	///
	/// 解析异常不冒泡：失败返回 null 并回填 <paramref name="error"/>，视图按「无法解析」提示处理。
	/// DbcParserLib 用了 <see cref="Parser.SetParsingFailuresObserver"/> 这一静态钩子收集语法错误，
	/// 故解析过程用 <see cref="Gate"/> 串行化，避免多视图并发解析时错误列表互相污染。
	///
	/// 性能要点（v2）：DbcParserLib 对值表（VAL_/VAL_TABLE_）逐条目递归解析且随条目数超线性
	/// 增长——实测单行 65536 条目的 VAL_ 耗时 13.5 秒（详见 <see cref="ValueTableFastPathThreshold"/>），
	/// 是大文件慢的主因。因此超大值表行在进入 DbcParserLib 之前被拦截，由本类做等价的线性
	/// 解析，再在 Build 阶段注回数据树，结果与库解析一致（键重复后者覆盖前者），耗时降为毫秒级。
	/// </summary>
	internal static class DbcParser
	{
		private static readonly SimpleFailureObserver Observer = new SimpleFailureObserver();

		private static readonly object Gate = new object();

		/// <summary>单条 VAL_/VAL_TABLE_ 行允许的最大值表条目数，超出即丢弃该行并记录告警。</summary>
		private const int ValueTableEntryLimit = 250000;

		/// <summary>
		/// 值表快速通道阈值：条目数超过此值的 VAL_/VAL_TABLE_ 行不进 DbcParserLib（其逐条目
		/// 递归解析对大值表是超线性的：65536 条目单行实测 13.5s，10001 条目 0.62s），改由
		/// <see cref="ParseInterceptedEntries"/> 线性解析后注回数据树。正常 DBC 值表只有几十条，
		/// 阈值只拦病态 / 机器生成的大表。
		/// </summary>
		private const int ValueTableFastPathThreshold = 2000;

		/// <summary>解析线程的显式栈大小（字节）。</summary>
		private const int ParseStackBytes = 256 * 1024 * 1024;

		/// <summary>解析 DBC 文本。成功返回数据树，失败返回 null 并回填 error；token 取消时抛
		/// <see cref="OperationCanceledException"/>（由调用方按代次丢弃结果）。</summary>
		internal static DataNode Parse(string text, CancellationToken token, out string error)
		{
			error = null;
			if (string.IsNullOrWhiteSpace(text))
			{
				return DataNode.Map();
			}
			// 去 BOM："BU_" 前的 BOM 会让解析器首行无法识别。
			if (text.Length > 0 && text[0] == '\uFEFF')
			{
				text = text.Substring(1);
			}
			try
			{
				DbcFile dbc;
				List<string> failures = new List<string>();
				// 大值表走快速通道（详见类注释）；串行化 Gate 之外的部分都可被取消，
				// 已经排在 Gate 上的取消请求拿到锁后立即退出，避免快速翻版本时旧代次排队解析。
				List<InterceptedValueTable> intercepted = new List<InterceptedValueTable>();
				text = InterceptValueTables(text, failures, intercepted, token);
				token.ThrowIfCancellationRequested();
				lock (Gate)
				{
					token.ThrowIfCancellationRequested();
					Observer.Clear();
					Parser.SetParsingFailuresObserver(Observer);
					dbc = ParseOnDedicatedStack(text, token);
					IList<string> list = Observer.GetErrorList();
					if (list != null)
					{
						for (int i = 0; i < list.Count && failures.Count < 20; i++)
						{
							string message = list[i] as string;
							if (!string.IsNullOrEmpty(message))
							{
								failures.Add(message);
							}
						}
					}
				}
				token.ThrowIfCancellationRequested();
				DataNode root = Build(dbc, intercepted);
				if (failures.Count > 0)
				{
					// 有语法错误仍尽量出树（DBC 常是部分可解析），把错误作为一行提示挂在树顶。
					DataNode diagnostics = DataNode.Seq();
					foreach (string failure in failures)
					{
						diagnostics.Add(DataNode.Scalar(failure));
					}
					root.Add("Parse warnings", diagnostics);
				}
				return root;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				error = ex.GetType().Name + ": " + ex.Message;
				return null;
			}
		}

		// ---- 栈溢出防护 ----

		/// <summary>
		/// 在显式指定大栈的专用线程上执行 <see cref="Parser.Parse(string)"/> 并同步等待结果。
		/// 线程池默认 1MB 栈撑不住大值表的逐条目递归（见 <see cref="Parse"/> 内注释），
		/// <see cref="Thread(int)"/> 的 maxStackSize 只是虚拟内存预留，按需提交，代价可忽略。
		/// </summary>
		private static DbcFile ParseOnDedicatedStack(string text, CancellationToken token)
		{
			token.ThrowIfCancellationRequested();
			DbcFile dbc = null;
			Exception fault = null;
			Thread worker = new Thread((ThreadStart)delegate
			{
				try
				{
					dbc = Parser.Parse(text);
				}
				catch (Exception ex)
				{
					fault = ex;
				}
			}, ParseStackBytes);
			worker.IsBackground = true;
			worker.Name = "DbcParse";
			worker.Start();
			worker.Join();
			if (fault != null)
			{
				throw fault;
			}
			return dbc;
		}

		// ---- 大值表拦截（快速通道） ----

		/// <summary>被拦截的大值表：目标定位 + 线性解析出的条目。</summary>
		private sealed class InterceptedValueTable
		{
			/// <summary>VAL_TABLE_ 命名表（只经链接行间接生效，链接行在拦截阶段已展开）。</summary>
			internal bool IsNamedTable;

			/// <summary>VAL_ &lt;msgId&gt; &lt;sigName&gt; 形态（否则为环境变量 VAL_ &lt;envName&gt;）。</summary>
			internal bool IsSignalTable;

			internal uint MessageId;

			internal string TargetName;

			internal Dictionary<int, string> Entries;
		}

		/// <summary>
		/// 单遍扫描（免 Split/免按行分配）：
		/// <list type="bullet">
		/// <item>条目数 &gt; <see cref="ValueTableEntryLimit"/>：整行丢弃（原有行为）并记录告警；</item>
		/// <item><see cref="ValueTableFastPathThreshold"/> &lt; 条目数 ≤ 上限：拦截该行（从文本抹掉），
		/// 线性解析后 Build 阶段注回树；</item>
		/// <item>引用了被拦截命名表的链接行（VAL_ id sig 表名;）一并拦截，避免库报「表不存在」且丢失关联；</item>
		/// <item>本类解析失败的行保留在文本里交给库（宁可慢也不能静默丢数据），并记录告警。</item>
		/// </list>
		/// 只有确实发生拦截 / 丢弃时才新建结果文本，否则原样返回。
		/// </summary>
		private static string InterceptValueTables(string text, List<string> notes, List<InterceptedValueTable> intercepted, CancellationToken token)
		{
			if (text.IndexOf("VAL_", StringComparison.Ordinal) < 0)
			{
				return text;
			}
			List<int> replaceFrom = null;
			List<int> replaceTo = null;
			int textLength = text.Length;
			int lineStart = 0;
			while (lineStart < textLength)
			{
				token.ThrowIfCancellationRequested();
				int lineEnd = text.IndexOf('\n', lineStart);
				if (lineEnd < 0)
				{
					lineEnd = textLength;
				}
				int entries = ValueTableEntryCount(text, lineStart, lineEnd);
				if (entries > ValueTableFastPathThreshold)
				{
					if (entries > ValueTableEntryLimit)
					{
						MarkLine(ref replaceFrom, ref replaceTo, lineStart, lineEnd);
						AddNote(notes, "Value table with " + entries.ToString(CultureInfo.InvariantCulture)
							+ " entries skipped (exceeds " + ValueTableEntryLimit.ToString(CultureInfo.InvariantCulture) + "-entry limit)");
					}
					else
					{
						InterceptedValueTable table = ParseInterceptedLine(text, lineStart, lineEnd, out int entryStart);
						if (table != null)
						{
							table.Entries = ParseInterceptedEntries(text, entryStart, lineEnd);
							if (table.Entries.Count > 0)
							{
								MarkLine(ref replaceFrom, ref replaceTo, lineStart, lineEnd);
								intercepted.Add(table);
								if (notes != null && intercepted.Count == 1)
								{
									AddNote(notes, "Large value tables parsed via fast path");
								}
							}
							else
							{
								AddNote(notes, "Large value table left to standard parser");
							}
						}
						else
						{
							AddNote(notes, "Large value table left to standard parser");
						}
					}
				}
				if (lineEnd >= textLength)
				{
					break;
				}
				lineStart = lineEnd + 1;
			}
			if (replaceFrom == null)
			{
				return text;
			}
			// 命名表引用链接：VAL_ <msgId> <sigName> <tableName> ;（无引号）。引用被拦截命名表的
			// 链接行也拦截为信号值表，条目直接共享命名表的解析结果。
			Dictionary<string, InterceptedValueTable> namedTables = null;
			for (int i = 0; i < intercepted.Count; i++)
			{
				if (intercepted[i].IsNamedTable)
				{
					namedTables = namedTables ?? new Dictionary<string, InterceptedValueTable>(StringComparer.Ordinal);
					namedTables[intercepted[i].TargetName] = intercepted[i];
				}
			}
			if (namedTables != null && namedTables.Count > 0)
			{
				lineStart = 0;
				while (lineStart < textLength)
				{
					token.ThrowIfCancellationRequested();
					int lineEnd = text.IndexOf('\n', lineStart);
					if (lineEnd < 0)
					{
						lineEnd = textLength;
					}
					if (!IsReplaced(replaceFrom, lineStart)
						&& ValueTableEntryCount(text, lineStart, lineEnd) == 0
						&& TryParseTableLink(text, lineStart, lineEnd, out uint messageId, out string signalName, out string tableName)
						&& namedTables.TryGetValue(tableName, out InterceptedValueTable named))
					{
						MarkLine(ref replaceFrom, ref replaceTo, lineStart, lineEnd);
						intercepted.Add(new InterceptedValueTable
						{
							IsSignalTable = true,
							MessageId = messageId,
							TargetName = signalName,
							Entries = named.Entries
						});
					}
					if (lineEnd >= textLength)
					{
						break;
					}
					lineStart = lineEnd + 1;
				}
			}
			// 重组文本：保留未命中区间（行内容抹掉、换行保留，与旧 Split 实现语义一致）。
			StringBuilder builder = new StringBuilder(textLength);
			int copyFrom = 0;
			for (int i = 0; i < replaceFrom.Count; i++)
			{
				builder.Append(text, copyFrom, replaceFrom[i] - copyFrom);
				copyFrom = replaceTo[i];
			}
			builder.Append(text, copyFrom, textLength - copyFrom);
			return builder.ToString();
		}

		/// <summary>把 [from, to) 标记为待抹除行（按出现顺序追加）。</summary>
		private static void MarkLine(ref List<int> replaceFrom, ref List<int> replaceTo, int from, int to)
		{
			replaceFrom = replaceFrom ?? new List<int>();
			replaceTo = replaceTo ?? new List<int>();
			replaceFrom.Add(from);
			replaceTo.Add(to);
		}

		private static bool IsReplaced(List<int> replaceFrom, int lineStart)
		{
			for (int i = 0; i < replaceFrom.Count; i++)
			{
				if (replaceFrom[i] == lineStart)
				{
					return true;
				}
			}
			return false;
		}

		private static void AddNote(List<string> notes, string message)
		{
			if (notes != null && notes.Count < 20)
			{
				notes.Add(message);
			}
		}

		/// <summary>条目数按行内引号对数估算——每个条目恰好一对引号；非 VAL_/VAL_TABLE_ 行返回 -1，
		/// 引号数为 0 的行（如命名表链接行）返回 0。</summary>
		private static int ValueTableEntryCount(string text, int start, int end)
		{
			int i = start;
			while (i < end && (text[i] == ' ' || text[i] == '\t' || text[i] == '\r'))
			{
				i++;
			}
			if (i + 4 > end || text[i] != 'V' || text[i + 1] != 'A' || text[i + 2] != 'L' || text[i + 3] != '_')
			{
				return -1;
			}
			int quotes = 0;
			for (int j = i; j < end; j++)
			{
				if (text[j] == '"')
				{
					quotes++;
				}
			}
			return quotes / 2;
		}

		/// <summary>
		/// 线性解析被拦截的 VAL_/VAL_TABLE_ 行头部，定位目标（信号 / 环境变量 / 命名表）并给出
		/// 条目段的起始位置。行内含语法问题时返回 null（该行保留给库处理）。
		/// </summary>
		private static InterceptedValueTable ParseInterceptedLine(string text, int start, int end, out int entryStart)
		{
			entryStart = start;
			int i = start;
			SkipWhitespace(text, ref i, end);
			if (!Consume(text, ref i, end, "VAL_"))
			{
				return null;
			}
			InterceptedValueTable table = new InterceptedValueTable();
			if (Consume(text, ref i, end, "TABLE_"))
			{
				table.IsNamedTable = true;
			}
			SkipWhitespace(text, ref i, end);
			if (i >= end)
			{
				return null;
			}
			if (!table.IsNamedTable && char.IsDigit(text[i]))
			{
				if (!TryParseUInt(text, ref i, end, out uint messageId))
				{
					return null;
				}
				table.MessageId = messageId;
				table.IsSignalTable = true;
				SkipWhitespace(text, ref i, end);
			}
			string name = ReadIdentifier(text, ref i, end);
			if (name == null)
			{
				return null;
			}
			table.TargetName = name;
			entryStart = i;
			return table;
		}

		/// <summary>线性解析条目段：&lt;int&gt; "label" 重复到 ';'，键重复后者覆盖前者（与库的
		/// <c>StringToDictionaryParser</c> 一致）。尽力解析：中途遇到语法问题即停在已解析部分。</summary>
		private static Dictionary<int, string> ParseInterceptedEntries(string text, int entryStart, int end)
		{
			Dictionary<int, string> entries = new Dictionary<int, string>();
			int i = entryStart;
			while (i < end)
			{
				SkipWhitespace(text, ref i, end);
				if (i >= end)
				{
					break;
				}
				if (text[i] == ';')
				{
					break;
				}
				if (!TryParseInt(text, ref i, end, out int key))
				{
					break;
				}
				SkipWhitespace(text, ref i, end);
				if (i >= end || text[i] != '"')
				{
					break;
				}
				i++;
				int labelStart = i;
				while (i < end && text[i] != '"')
				{
					i++;
				}
				if (i >= end)
				{
					break;
				}
				entries[key] = text.Substring(labelStart, i - labelStart);
				i++;
			}
			return entries;
		}

		/// <summary>解析命名表链接行：VAL_ &lt;msgId&gt; &lt;sigName&gt; &lt;tableName&gt; ;。</summary>
		private static bool TryParseTableLink(string text, int start, int end, out uint messageId, out string signalName, out string tableName)
		{
			messageId = 0;
			signalName = null;
			tableName = null;
			int i = start;
			SkipWhitespace(text, ref i, end);
			if (!Consume(text, ref i, end, "VAL_") || Consume(text, ref i, end, "TABLE_"))
			{
				return false;
			}
			SkipWhitespace(text, ref i, end);
			if (!TryParseUInt(text, ref i, end, out messageId))
			{
				return false;
			}
			SkipWhitespace(text, ref i, end);
			signalName = ReadIdentifier(text, ref i, end);
			if (signalName == null)
			{
				return false;
			}
			SkipWhitespace(text, ref i, end);
			tableName = ReadIdentifier(text, ref i, end);
			if (tableName == null)
			{
				return false;
			}
			SkipWhitespace(text, ref i, end);
			return i < end && text[i] == ';';
		}

		private static void SkipWhitespace(string text, ref int i, int end)
		{
			while (i < end && (text[i] == ' ' || text[i] == '\t' || text[i] == '\r'))
			{
				i++;
			}
		}

		private static bool Consume(string text, ref int i, int end, string word)
		{
			if (i + word.Length > end)
			{
				return false;
			}
			for (int j = 0; j < word.Length; j++)
			{
				if (text[i + j] != word[j])
				{
					return false;
				}
			}
			i += word.Length;
			return true;
		}

		private static string ReadIdentifier(string text, ref int i, int end)
		{
			int start = i;
			while (i < end && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
			{
				i++;
			}
			if (i == start)
			{
				return null;
			}
			return text.Substring(start, i - start);
		}

		private static bool TryParseUInt(string text, ref int i, int end, out uint value)
		{
			value = 0;
			int start = i;
			while (i < end && char.IsDigit(text[i]))
			{
				value = value * 10u + (uint)(text[i] - '0');
				i++;
			}
			return i > start;
		}

		private static bool TryParseInt(string text, ref int i, int end, out int value)
		{
			value = 0;
			bool negative = i < end && text[i] == '-';
			if (negative)
			{
				i++;
			}
			if (!TryParseUInt(text, ref i, end, out uint magnitude))
			{
				return false;
			}
			if (negative)
			{
				if (magnitude > 2147483648u)
				{
					return false;
				}
				value = -(int)magnitude;
			}
			else
			{
				if (magnitude > 2147483647u)
				{
					return false;
				}
				value = (int)magnitude;
			}
			return true;
		}

		// ---- Dbc → DataNode ----

		private static DataNode Build(DbcFile dbc, List<InterceptedValueTable> intercepted)
		{
			DataNode root = DataNode.Map();
			if (dbc == null)
			{
				return root;
			}
			Dictionary<(uint, string), Dictionary<int, string>> signalTables = null;
			Dictionary<string, Dictionary<int, string>> envTables = null;
			for (int i = 0; i < intercepted.Count; i++)
			{
				InterceptedValueTable table = intercepted[i];
				if (table.IsNamedTable || table.Entries == null || string.IsNullOrEmpty(table.TargetName))
				{
					// 命名表只经链接行生效，链接行在拦截阶段已展开为信号值表，无需注回。
					continue;
				}
				if (table.IsSignalTable)
				{
					signalTables = signalTables ?? new Dictionary<(uint, string), Dictionary<int, string>>();
					signalTables[(table.MessageId, table.TargetName)] = table.Entries;
				}
				else
				{
					envTables = envTables ?? new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);
					envTables[table.TargetName] = table.Entries;
				}
			}
			AddNodes(root, dbc);
			AddMessages(root, dbc, signalTables);
			AddEnvironmentVariables(root, dbc, envTables);
			AddGlobalProperties(root, dbc);
			return root;
		}

		private static void AddNodes(DataNode root, DbcFile dbc)
		{
			List<Node> nodes = new List<Node>();
			foreach (Node node in dbc.Nodes)
			{
				if (node != null)
				{
					nodes.Add(node);
				}
			}
			if (nodes.Count == 0)
			{
				return;
			}
			nodes.Sort(delegate (Node a, Node b)
			{
				return string.CompareOrdinal(a.Name, b.Name);
			});
			DataNode map = DataNode.Map();
			HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
			foreach (Node node in nodes)
			{
				DataNode entry = DataNode.Map();
				AddComment(entry, node.Comment);
				AddAttributes(entry, node.CustomProperties);
				map.Add(Unique(used, node.Name, "node"), entry);
			}
			root.Add("Nodes", map);
		}

		private static void AddMessages(DataNode root, DbcFile dbc, Dictionary<(uint, string), Dictionary<int, string>> signalTables)
		{
			List<Message> messages = new List<Message>();
			foreach (Message message in dbc.Messages)
			{
				if (message != null)
				{
					messages.Add(message);
				}
			}
			if (messages.Count == 0)
			{
				return;
			}
			messages.Sort(delegate (Message a, Message b)
			{
				int byId = a.ID.CompareTo(b.ID);
				return byId != 0 ? byId : string.CompareOrdinal(a.Name, b.Name);
			});
			DataNode map = DataNode.Map();
			HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
			foreach (Message message in messages)
			{
				map.Add(Unique(used, MessageKey(message), "message"), BuildMessage(message, signalTables));
			}
			root.Add("Messages", map);
		}

		private static DataNode BuildMessage(Message message, Dictionary<(uint, string), Dictionary<int, string>> signalTables)
		{
			DataNode node = DataNode.Map();
			node.Add("ID", DataNode.Scalar(message.ID.ToString(CultureInfo.InvariantCulture)));
			node.Add("ID (hex)", DataNode.Scalar("0x" + message.ID.ToString("X", CultureInfo.InvariantCulture)));
			node.Add("Frame", DataNode.Scalar(message.IsExtID ? "Extended" : "Standard"));
			node.Add("DLC", DataNode.Scalar(message.DLC.ToString(CultureInfo.InvariantCulture)));
			node.Add("Transmitter", DataNode.Scalar(Value(message.Transmitter)));
			if (message.AdditionalTransmitters != null && message.AdditionalTransmitters.Length > 0)
			{
				node.Add("Additional transmitters", DataNode.Scalar(string.Join(", ", message.AdditionalTransmitters)));
			}
			AddComment(node, message.Comment);
			AddAttributes(node, message.CustomProperties);
			AddSignals(node, message, signalTables);
			return node;
		}

		private static void AddSignals(DataNode messageNode, Message message, Dictionary<(uint, string), Dictionary<int, string>> signalTables)
		{
			if (message.Signals == null || message.Signals.Count == 0)
			{
				return;
			}
			List<Signal> signals = new List<Signal>(message.Signals);
			signals.Sort(delegate (Signal a, Signal b)
			{
				int byStart = a.StartBit.CompareTo(b.StartBit);
				return byStart != 0 ? byStart : string.CompareOrdinal(a.Name, b.Name);
			});
			DataNode map = DataNode.Map();
			HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
			foreach (Signal signal in signals)
			{
				map.Add(Unique(used, signal.Name, "signal"), BuildSignal(signal, message, signalTables));
			}
			messageNode.Add("Signals", map);
		}

		private static DataNode BuildSignal(Signal signal, Message message, Dictionary<(uint, string), Dictionary<int, string>> signalTables)
		{
			DataNode node = DataNode.Map();
			node.Add("Start bit", DataNode.Scalar(signal.StartBit.ToString(CultureInfo.InvariantCulture)));
			node.Add("Length", DataNode.Scalar(signal.Length.ToString(CultureInfo.InvariantCulture)));
			node.Add("Byte order", DataNode.Scalar(signal.ByteOrder == 0 ? "Big Endian (Motorola)" : "Little Endian (Intel)"));
			node.Add("Value type", DataNode.Scalar(ValueTypeLabel(signal.ValueType)));
			node.Add("Factor", DataNode.Scalar(Number(signal.Factor)));
			node.Add("Offset", DataNode.Scalar(Number(signal.Offset)));
			node.Add("Minimum", DataNode.Scalar(Number(signal.Minimum)));
			node.Add("Maximum", DataNode.Scalar(Number(signal.Maximum)));
			node.Add("Unit", DataNode.Scalar(Value(signal.Unit)));
			node.Add("Initial value", DataNode.Scalar(Number(signal.InitialValue)));
			if (!string.IsNullOrEmpty(signal.Multiplexing))
			{
				node.Add("Multiplexing", DataNode.Scalar(signal.Multiplexing));
			}
			if (signal.Receiver != null && signal.Receiver.Length > 0)
			{
				node.Add("Receivers", DataNode.Scalar(string.Join(", ", signal.Receiver)));
			}
			AddValueTable(node, LookupSignalTable(signalTables, message, signal) ?? signal.ValueTableMap);
			AddComment(node, signal.Comment);
			AddAttributes(node, signal.CustomProperties);
			return node;
		}

		/// <summary>查被拦截值表：库的 Build 会把 ≥ 2^31 的报文 id 剥掉扩展位（AdjustExtendedId），
		/// 而 VAL_ 行里的是原始 id，两种形态都查。</summary>
		private static Dictionary<int, string> LookupSignalTable(Dictionary<(uint, string), Dictionary<int, string>> signalTables, Message message, Signal signal)
		{
			if (signalTables == null || signalTables.Count == 0)
			{
				return null;
			}
			if (signalTables.TryGetValue((message.ID, signal.Name), out Dictionary<int, string> table))
			{
				return table;
			}
			if (signalTables.TryGetValue((message.ID + 0x80000000u, signal.Name), out table))
			{
				return table;
			}
			return null;
		}

		private static void AddEnvironmentVariables(DataNode root, DbcFile dbc, Dictionary<string, Dictionary<int, string>> envTables)
		{
			List<EnvironmentVariable> variables = new List<EnvironmentVariable>();
			foreach (EnvironmentVariable variable in dbc.EnvironmentVariables)
			{
				if (variable != null)
				{
					variables.Add(variable);
				}
			}
			if (variables.Count == 0)
			{
				return;
			}
			variables.Sort(delegate (EnvironmentVariable a, EnvironmentVariable b)
			{
				return string.CompareOrdinal(a.Name, b.Name);
			});
			DataNode map = DataNode.Map();
			HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
			foreach (EnvironmentVariable variable in variables)
			{
				map.Add(Unique(used, variable.Name, "envvar"), BuildEnvironmentVariable(variable, envTables));
			}
			root.Add("Environment variables", map);
		}

		private static DataNode BuildEnvironmentVariable(EnvironmentVariable variable, Dictionary<string, Dictionary<int, string>> envTables)
		{
			DataNode node = DataNode.Map();
			node.Add("Type", DataNode.Scalar(variable.Type.ToString()));
			node.Add("Access", DataNode.Scalar(variable.Access.ToString()));
			node.Add("Unit", DataNode.Scalar(Value(variable.Unit)));
			if (variable.IntegerEnvironmentVariable != null)
			{
				node.Add("Minimum", DataNode.Scalar(variable.IntegerEnvironmentVariable.Minimum.ToString(CultureInfo.InvariantCulture)));
				node.Add("Maximum", DataNode.Scalar(variable.IntegerEnvironmentVariable.Maximum.ToString(CultureInfo.InvariantCulture)));
				node.Add("Default", DataNode.Scalar(variable.IntegerEnvironmentVariable.Default.ToString(CultureInfo.InvariantCulture)));
			}
			else if (variable.FloatEnvironmentVariable != null)
			{
				node.Add("Minimum", DataNode.Scalar(Number(variable.FloatEnvironmentVariable.Minimum)));
				node.Add("Maximum", DataNode.Scalar(Number(variable.FloatEnvironmentVariable.Maximum)));
				node.Add("Default", DataNode.Scalar(Number(variable.FloatEnvironmentVariable.Default)));
			}
			else if (variable.DataEnvironmentVariable != null)
			{
				node.Add("Length", DataNode.Scalar(variable.DataEnvironmentVariable.Length.ToString(CultureInfo.InvariantCulture)));
			}
			Dictionary<int, string> injected = envTables != null && envTables.TryGetValue(variable.Name, out Dictionary<int, string> table) ? table : null;
			AddValueTable(node, injected ?? variable.ValueTableMap);
			AddComment(node, variable.Comment);
			AddAttributes(node, variable.CustomProperties);
			return node;
		}

		private static void AddGlobalProperties(DataNode root, DbcFile dbc)
		{
			if (dbc.GlobalProperties == null)
			{
				return;
			}
			DataNode map = DataNode.Map();
			List<KeyValuePair<string, string>> pairs = new List<KeyValuePair<string, string>>();
			foreach (CustomProperty property in dbc.GlobalProperties)
			{
				if (property == null)
				{
					continue;
				}
				string name = property.CustomPropertyDefinition?.Name;
				if (string.IsNullOrEmpty(name))
				{
					continue;
				}
				pairs.Add(new KeyValuePair<string, string>(name, CustomValue(property)));
			}
			if (pairs.Count == 0)
			{
				return;
			}
			pairs.Sort(delegate (KeyValuePair<string, string> a, KeyValuePair<string, string> b)
			{
				return string.CompareOrdinal(a.Key, b.Key);
			});
			foreach (KeyValuePair<string, string> pair in pairs)
			{
				map.Add(pair.Key, DataNode.Scalar(pair.Value));
			}
			root.Add("Global attributes", map);
		}

		// ---- 公共片段 ----

		private static void AddComment(DataNode node, string comment)
		{
			if (!string.IsNullOrEmpty(comment))
			{
				node.Add("Comment", DataNode.Scalar(comment));
			}
		}

		private static void AddValueTable(DataNode node, IReadOnlyDictionary<int, string> valueTable)
		{
			if (valueTable == null || valueTable.Count == 0)
			{
				return;
			}
			List<int> keys = new List<int>(valueTable.Keys);
			keys.Sort();
			DataNode map = DataNode.Map();
			foreach (int key in keys)
			{
				map.Add(key.ToString(CultureInfo.InvariantCulture), DataNode.Scalar(Value(valueTable[key])));
			}
			node.Add("Values", map);
		}

		private static void AddAttributes(DataNode node, IDictionary<string, CustomProperty> properties)
		{
			if (properties == null || properties.Count == 0)
			{
				return;
			}
			DataNode map = DataNode.Map();
			List<string> names = new List<string>(properties.Keys);
			names.Sort(StringComparer.Ordinal);
			foreach (string name in names)
			{
				if (string.IsNullOrEmpty(name))
				{
					continue;
				}
				map.Add(name, DataNode.Scalar(CustomValue(properties[name])));
			}
			if (map.Entries.Count > 0)
			{
				node.Add("Attributes", map);
			}
		}

		/// <summary>按属性定义的数据类型取展示值（整型 / 十六进制 / 浮点 / 字符串 / 枚举）。</summary>
		private static string CustomValue(CustomProperty property)
		{
			if (property == null)
			{
				return string.Empty;
			}
			CustomPropertyDataType dataType = property.CustomPropertyDefinition?.DataType ?? CustomPropertyDataType.String;
			switch (dataType)
			{
			case CustomPropertyDataType.Integer:
				return property.IntegerCustomProperty != null
					? property.IntegerCustomProperty.Value.ToString(CultureInfo.InvariantCulture)
					: string.Empty;
			case CustomPropertyDataType.Hex:
				return property.HexCustomProperty != null
					? "0x" + property.HexCustomProperty.Value.ToString("X", CultureInfo.InvariantCulture)
					: string.Empty;
			case CustomPropertyDataType.Float:
				return property.FloatCustomProperty != null ? Number(property.FloatCustomProperty.Value) : string.Empty;
			case CustomPropertyDataType.Enum:
				return property.EnumCustomProperty?.Value ?? string.Empty;
			default:
				return property.StringCustomProperty?.Value ?? string.Empty;
			}
		}

		private static string MessageKey(Message message)
		{
			string name = message.Name;
			if (!string.IsNullOrEmpty(name))
			{
				return name;
			}
			return "0x" + message.ID.ToString("X", CultureInfo.InvariantCulture);
		}

		/// <summary>同名对象（DBC 允许重名场景）加后缀区分，避免键路径碰撞。</summary>
		private static string Unique(HashSet<string> used, string name, string fallback)
		{
			string key = string.IsNullOrEmpty(name) ? fallback : name;
			if (used.Add(key))
			{
				return key;
			}
			int index = 2;
			string candidate;
			do
			{
				candidate = key + " #" + index.ToString(CultureInfo.InvariantCulture);
				index++;
			}
			while (!used.Add(candidate));
			return candidate;
		}

		private static string ValueTypeLabel(DbcValueType valueType)
		{
			switch (valueType)
			{
			case DbcValueType.Signed:
				return "Signed";
			case DbcValueType.Unsigned:
				return "Unsigned";
			case DbcValueType.IEEEFloat:
				return "IEEE Float";
			case DbcValueType.IEEEDouble:
				return "IEEE Double";
			default:
				return valueType.ToString();
			}
		}

		private static string Value(string text)
		{
			return text ?? string.Empty;
		}

		private static string Number(double value)
		{
			return value.ToString("R", CultureInfo.InvariantCulture);
		}
	}
}
