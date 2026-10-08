using System;
using System.Collections.Generic;
using System.Globalization;
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
	/// </summary>
	internal static class DbcParser
	{
		private static readonly SimpleFailureObserver Observer = new SimpleFailureObserver();

		private static readonly object Gate = new object();

		/// <summary>解析 DBC 文本。成功返回数据树，失败返回 null 并回填 error。</summary>
		internal static DataNode Parse(string text, out string error)
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
				lock (Gate)
				{
					Observer.Clear();
					Parser.SetParsingFailuresObserver(Observer);
					dbc = Parser.Parse(text);
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
				DataNode root = Build(dbc);
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
			catch (Exception ex)
			{
				error = ex.GetType().Name + ": " + ex.Message;
				return null;
			}
		}

		// ---- Dbc → DataNode ----

		private static DataNode Build(DbcFile dbc)
		{
			DataNode root = DataNode.Map();
			if (dbc == null)
			{
				return root;
			}
			AddNodes(root, dbc);
			AddMessages(root, dbc);
			AddEnvironmentVariables(root, dbc);
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

		private static void AddMessages(DataNode root, DbcFile dbc)
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
				map.Add(Unique(used, MessageKey(message), "message"), BuildMessage(message));
			}
			root.Add("Messages", map);
		}

		private static DataNode BuildMessage(Message message)
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
			AddSignals(node, message);
			return node;
		}

		private static void AddSignals(DataNode messageNode, Message message)
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
				map.Add(Unique(used, signal.Name, "signal"), BuildSignal(signal));
			}
			messageNode.Add("Signals", map);
		}

		private static DataNode BuildSignal(Signal signal)
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
			AddValueTable(node, signal.ValueTableMap);
			AddComment(node, signal.Comment);
			AddAttributes(node, signal.CustomProperties);
			return node;
		}

		private static void AddEnvironmentVariables(DataNode root, DbcFile dbc)
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
				map.Add(Unique(used, variable.Name, "envvar"), BuildEnvironmentVariable(variable));
			}
			root.Add("Environment variables", map);
		}

		private static DataNode BuildEnvironmentVariable(EnvironmentVariable variable)
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
			AddValueTable(node, variable.ValueTableMap);
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
