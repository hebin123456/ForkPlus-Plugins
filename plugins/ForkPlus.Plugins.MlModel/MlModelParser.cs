using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ForkPlus.Plugins.MlModel
{
	/// <summary>
	/// 机器学习模型解析：把 ONNX / SafeTensors / GGUF 三种二进制格式各解析成同一套
	/// <see cref="MlRow"/>（键路径 + 值）行列表。
	///
	/// 分工：
	/// <list type="bullet">
	/// <item>ONNX —— protobuf 线格式自解析（不引第三方 protobuf 库）：只按已知字段号走
	/// （ModelProto → GraphProto → NodeProto / ValueInfoProto / TensorProto），
	/// 未知字段一律按 wireType 跳过，解析容错。</item>
	/// <item>SafeTensors —— 前 8 字节小端 u64 是 JSON 头长度，其后是 UTF-8 JSON
	/// （框架内置 System.Text.Json 解析），剩余为张量数据区。</item>
	/// <item>GGUF —— 魔数 "GGUF" + 版本 u32（支持 2 / 3）+ 张量数 / KV 数 u64 +
	/// KV 对 + 张量信息，整数读取一律小端。</item>
	/// </list>
	///
	/// 解析失败不抛异常：返回 <c>null</c> 并回填 <paramref name="error"/>，视图按「无法解析」提示处理。
	/// </summary>
	internal static class MlModelParser
	{
		/// <summary>ONNX 的 node / input / output / initializer 各自的行数上限，超出截断。</summary>
		private const int OnnxListLimit = 1000;

		/// <summary>GGUF 数组值渲染时最多逐项写出的元素个数，超出折叠成省略号 + 总数。</summary>
		private const int GgufArrayPreview = 8;

		/// <summary>
		/// 按扩展名分派解析器。成功返回模型，失败返回 null 并回填 error。
		/// 扩展名带不带前导点都接受（视图侧 FormatOf 产出无点形式，此处归一成小写含点）。
		/// </summary>
		internal static MlModel Parse(byte[] data, string fileExtension, out string error)
		{
			error = null;
			if (data == null)
			{
				error = "no data available";
				return null;
			}
			string ext = (fileExtension ?? string.Empty).Trim().ToLowerInvariant();
			if (ext.Length > 0 && ext[0] != '.')
			{
				ext = "." + ext;
			}
			try
			{
				switch (ext)
				{
				case ".onnx":
					return ParseOnnx(data);
				case ".safetensors":
					return ParseSafeTensors(data);
				case ".gguf":
					return ParseGguf(data);
				default:
					throw new InvalidDataException("unsupported model extension: " + ext);
				}
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return null;
			}
		}

		// ---- ONNX（protobuf 线格式） ----

		/// <summary>ONNX 图解析的累计统计（node / input / output / initializer 计数与算子直方图）。</summary>
		private sealed class OnnxGraphStats
		{
			internal int NodeCount;

			internal int InputCount;

			internal int OutputCount;

			internal int InitializerCount;

			/// <summary>算子直方图：op_type → 出现次数。</summary>
			internal readonly Dictionary<string, int> OpCounts = new Dictionary<string, int>(StringComparer.Ordinal);
		}

		/// <summary>
		/// ModelProto：1=ir_version(varint)、2=producer_name、3=producer_version、
		/// 5=model_version(varint)、7=graph(嵌套消息)；其余字段号跳过。
		/// </summary>
		private static MlModel ParseOnnx(byte[] data)
		{
			if (data.Length == 0)
			{
				throw new InvalidDataException("not a valid ONNX file");
			}
			MlModel model = new MlModel();
			model.Format = "ONNX";
			OnnxGraphStats stats = new OnnxGraphStats();
			ProtoReader reader = new ProtoReader(data, 0, data.Length);
			while (reader.HasMore)
			{
				int wireType;
				int field = reader.ReadTag(out wireType);
				switch (field)
				{
				case 1:
					if (wireType == 0)
					{
						model.Rows.Add(new MlRow { Path = "ir version", Value = reader.ReadVarint().ToString(CultureInfo.InvariantCulture), Kind = MlRowKind.Meta });
					}
					else
					{
						reader.SkipField(wireType);
					}
					break;
				case 2:
					if (wireType == 2)
					{
						model.Rows.Add(new MlRow { Path = "producer", Value = reader.ReadString(), Kind = MlRowKind.Meta });
					}
					else
					{
						reader.SkipField(wireType);
					}
					break;
				case 3:
					if (wireType == 2)
					{
						model.Rows.Add(new MlRow { Path = "producer version", Value = reader.ReadString(), Kind = MlRowKind.Meta });
					}
					else
					{
						reader.SkipField(wireType);
					}
					break;
				case 5:
					if (wireType == 0)
					{
						model.Rows.Add(new MlRow { Path = "model version", Value = reader.ReadVarint().ToString(CultureInfo.InvariantCulture), Kind = MlRowKind.Meta });
					}
					else
					{
						reader.SkipField(wireType);
					}
					break;
				case 7:
					if (wireType == 2)
					{
						ParseOnnxGraph(reader.ReadNested(), model, stats);
					}
					else
					{
						reader.SkipField(wireType);
					}
					break;
				default:
					reader.SkipField(wireType);
					break;
				}
			}
			AppendOnnxSummary(model, stats);
			return model;
		}

		/// <summary>
		/// GraphProto：1=node(重复 NodeProto)、2=name、5=initializer(重复 TensorProto)、
		/// 11=input / 12=output(重复 ValueInfoProto)；其余字段号跳过。
		/// </summary>
		private static void ParseOnnxGraph(ProtoReader graph, MlModel model, OnnxGraphStats stats)
		{
			while (graph.HasMore)
			{
				int wireType;
				int field = graph.ReadTag(out wireType);
				switch (field)
				{
				case 1:
					if (wireType == 2)
					{
						ParseOnnxNode(graph.ReadNested(), model, stats);
					}
					else
					{
						graph.SkipField(wireType);
					}
					break;
				case 2:
					if (wireType == 2)
					{
						model.Rows.Add(new MlRow { Path = "graph name", Value = graph.ReadString(), Kind = MlRowKind.Meta });
					}
					else
					{
						graph.SkipField(wireType);
					}
					break;
				case 5:
					if (wireType == 2)
					{
						ParseOnnxInitializer(graph.ReadNested(), model, stats);
					}
					else
					{
						graph.SkipField(wireType);
					}
					break;
				case 11:
					if (wireType == 2)
					{
						ParseOnnxValueInfo(graph.ReadNested(), model, "input", ref stats.InputCount);
					}
					else
					{
						graph.SkipField(wireType);
					}
					break;
				case 12:
					if (wireType == 2)
					{
						ParseOnnxValueInfo(graph.ReadNested(), model, "output", ref stats.OutputCount);
					}
					else
					{
						graph.SkipField(wireType);
					}
					break;
				default:
					graph.SkipField(wireType);
					break;
				}
			}
		}

		/// <summary>NodeProto：3=name、4=op_type（input / output 列表忽略）。行（Tensor 类）：
		/// "node[{i}]" = "op_type (name)"（无 name 就只 op_type）；超出上限只计数不排行。</summary>
		private static void ParseOnnxNode(ProtoReader node, MlModel model, OnnxGraphStats stats)
		{
			string name = null;
			string opType = null;
			while (node.HasMore)
			{
				int wireType;
				int field = node.ReadTag(out wireType);
				if (field == 3 && wireType == 2)
				{
					name = node.ReadString();
				}
				else if (field == 4 && wireType == 2)
				{
					opType = node.ReadString();
				}
				else
				{
					node.SkipField(wireType);
				}
			}
			stats.NodeCount++;
			if (!string.IsNullOrEmpty(opType))
			{
				stats.OpCounts[opType] = stats.OpCounts.TryGetValue(opType, out int count) ? count + 1 : 1;
			}
			if (stats.NodeCount <= OnnxListLimit)
			{
				string value = string.IsNullOrEmpty(name) ? (opType ?? string.Empty) : opType + " (" + name + ")";
				model.Rows.Add(new MlRow { Path = "node[" + (stats.NodeCount - 1).ToString(CultureInfo.InvariantCulture) + "]", Value = value, Kind = MlRowKind.Tensor });
			}
		}

		/// <summary>TensorProto(initializer)：8=name、1=dims(重复 varint，兼容 packed)、2=data_type(varint)。
		/// 行（Tensor 类）："initializer[{i}]" = "name : TYPE[dims]"；dims 乘积计入 ParamCount（截断后仍统计）。</summary>
		private static void ParseOnnxInitializer(ProtoReader tensor, MlModel model, OnnxGraphStats stats)
		{
			string name = null;
			int dataType = 0;
			List<long> dims = new List<long>();
			while (tensor.HasMore)
			{
				int wireType;
				int field = tensor.ReadTag(out wireType);
				switch (field)
				{
				case 1:
					if (wireType == 0)
					{
						dims.Add((long)tensor.ReadVarint());
					}
					else if (wireType == 2)
					{
						foreach (ulong value in tensor.ReadPackedVarints())
						{
							dims.Add((long)value);
						}
					}
					else
					{
						tensor.SkipField(wireType);
					}
					break;
				case 2:
					if (wireType == 0)
					{
						dataType = (int)tensor.ReadVarint();
					}
					else
					{
						tensor.SkipField(wireType);
					}
					break;
				case 8:
					if (wireType == 2)
					{
						name = tensor.ReadString();
					}
					else
					{
						tensor.SkipField(wireType);
					}
					break;
				default:
					tensor.SkipField(wireType);
					break;
				}
			}
			stats.InitializerCount++;
			model.ParamCount += ProductOf(dims);
			if (stats.InitializerCount <= OnnxListLimit)
			{
				model.Rows.Add(new MlRow
				{
					Path = "initializer[" + (stats.InitializerCount - 1).ToString(CultureInfo.InvariantCulture) + "]",
					Value = (name ?? string.Empty) + " : " + ElemTypeName(dataType) + "[" + JoinDims(dims) + "]",
					Kind = MlRowKind.Tensor
				});
			}
		}

		/// <summary>ValueInfoProto：1=name、2=type(TypeProto)。行（Tensor 类）：
		/// "input[{i}]" / "output[{i}]" = "name : TYPE[d0×d1×…]"（dim_param 维写成名字）。</summary>
		private static void ParseOnnxValueInfo(ProtoReader valueInfo, MlModel model, string prefix, ref int counter)
		{
			string name = null;
			string typeText = null;
			while (valueInfo.HasMore)
			{
				int wireType;
				int field = valueInfo.ReadTag(out wireType);
				if (field == 1 && wireType == 2)
				{
					name = valueInfo.ReadString();
				}
				else if (field == 2 && wireType == 2)
				{
					typeText = ParseOnnxType(valueInfo.ReadNested());
				}
				else
				{
					valueInfo.SkipField(wireType);
				}
			}
			counter++;
			if (counter > OnnxListLimit)
			{
				return;
			}
			model.Rows.Add(new MlRow
			{
				Path = prefix + "[" + (counter - 1).ToString(CultureInfo.InvariantCulture) + "]",
				Value = (name ?? string.Empty) + " : " + (typeText ?? "?"),
				Kind = MlRowKind.Tensor
			});
		}

		/// <summary>TypeProto：只认 1=tensor_type(Tensor)，其余（sequence / map 等 oneof 分支）跳过。</summary>
		private static string ParseOnnxType(ProtoReader type)
		{
			while (type.HasMore)
			{
				int wireType;
				int field = type.ReadTag(out wireType);
				if (field == 1 && wireType == 2)
				{
					return ParseOnnxTensorType(type.ReadNested());
				}
				type.SkipField(wireType);
			}
			return "?";
		}

		/// <summary>TypeProto.Tensor：1=elem_type(varint)、2=shape(TensorShapeProto)。</summary>
		private static string ParseOnnxTensorType(ProtoReader tensor)
		{
			int elemType = 0;
			bool hasShape = false;
			List<string> dims = new List<string>();
			while (tensor.HasMore)
			{
				int wireType;
				int field = tensor.ReadTag(out wireType);
				if (field == 1 && wireType == 0)
				{
					elemType = (int)tensor.ReadVarint();
				}
				else if (field == 2 && wireType == 2)
				{
					hasShape = true;
					ParseOnnxShape(tensor.ReadNested(), dims);
				}
				else
				{
					tensor.SkipField(wireType);
				}
			}
			string text = ElemTypeName(elemType);
			if (hasShape)
			{
				text = text + "[" + string.Join("×", dims) + "]";
			}
			return text;
		}

		/// <summary>TensorShapeProto：1=dim(重复 Dimension)。</summary>
		private static void ParseOnnxShape(ProtoReader shape, List<string> dims)
		{
			while (shape.HasMore)
			{
				int wireType;
				int field = shape.ReadTag(out wireType);
				if (field == 1 && wireType == 2)
				{
					dims.Add(ParseOnnxDimension(shape.ReadNested()));
				}
				else
				{
					shape.SkipField(wireType);
				}
			}
		}

		/// <summary>Dimension：1=dim_value(varint) / 2=dim_param(字符串)，二选一。</summary>
		private static string ParseOnnxDimension(ProtoReader dimension)
		{
			while (dimension.HasMore)
			{
				int wireType;
				int field = dimension.ReadTag(out wireType);
				if (field == 1 && wireType == 0)
				{
					return dimension.ReadVarint().ToString(CultureInfo.InvariantCulture);
				}
				if (field == 2 && wireType == 2)
				{
					return dimension.ReadString();
				}
				dimension.SkipField(wireType);
			}
			return "?";
		}

		/// <summary>图解析完成后补 Meta 汇总行：截断说明、"node count"、算子直方图（按数量降序、同数量按名排序）。</summary>
		private static void AppendOnnxSummary(MlModel model, OnnxGraphStats stats)
		{
			if (stats.NodeCount > OnnxListLimit)
			{
				model.Rows.Add(new MlRow { Path = "node list truncated to " + OnnxListLimit.ToString(CultureInfo.InvariantCulture), Value = stats.NodeCount.ToString(CultureInfo.InvariantCulture), Kind = MlRowKind.Meta });
			}
			if (stats.InputCount > OnnxListLimit)
			{
				model.Rows.Add(new MlRow { Path = "input list truncated to " + OnnxListLimit.ToString(CultureInfo.InvariantCulture), Value = stats.InputCount.ToString(CultureInfo.InvariantCulture), Kind = MlRowKind.Meta });
			}
			if (stats.OutputCount > OnnxListLimit)
			{
				model.Rows.Add(new MlRow { Path = "output list truncated to " + OnnxListLimit.ToString(CultureInfo.InvariantCulture), Value = stats.OutputCount.ToString(CultureInfo.InvariantCulture), Kind = MlRowKind.Meta });
			}
			if (stats.InitializerCount > OnnxListLimit)
			{
				model.Rows.Add(new MlRow { Path = "initializer list truncated to " + OnnxListLimit.ToString(CultureInfo.InvariantCulture), Value = stats.InitializerCount.ToString(CultureInfo.InvariantCulture), Kind = MlRowKind.Meta });
			}
			if (stats.NodeCount > 0)
			{
				model.Rows.Add(new MlRow { Path = "node count", Value = stats.NodeCount.ToString(CultureInfo.InvariantCulture), Kind = MlRowKind.Meta });
				List<KeyValuePair<string, int>> histogram = new List<KeyValuePair<string, int>>(stats.OpCounts);
				histogram.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
				{
					int byCount = b.Value.CompareTo(a.Value);
					return byCount != 0 ? byCount : string.CompareOrdinal(a.Key, b.Key);
				});
				foreach (KeyValuePair<string, int> entry in histogram)
				{
					model.Rows.Add(new MlRow { Path = "op count." + entry.Key, Value = entry.Value.ToString(CultureInfo.InvariantCulture), Kind = MlRowKind.Meta });
				}
			}
			model.TensorCount = stats.InitializerCount;
		}

		/// <summary>ONNX 元素类型映射；未知编号写 "type N"。</summary>
		private static string ElemTypeName(int elementType)
		{
			switch (elementType)
			{
			case 1:
				return "float";
			case 2:
				return "uint8";
			case 3:
				return "int8";
			case 4:
				return "uint16";
			case 5:
				return "int16";
			case 6:
				return "int32";
			case 7:
				return "int64";
			case 8:
				return "string";
			case 9:
				return "bool";
			case 10:
				return "float16";
			case 11:
				return "double";
			case 12:
				return "uint32";
			case 13:
				return "uint64";
			case 14:
				return "complex64";
			case 15:
				return "complex128";
			case 16:
				return "bfloat16";
			default:
				return "type " + elementType.ToString(CultureInfo.InvariantCulture);
			}
		}

		/// <summary>protobuf 线格式读取器：varint / 固定 32 / 64 位 / 长度分隔，
		/// tag = (field &lt;&lt; 3) | wireType。未知字段按 wireType 跳过，保证解析容错。</summary>
		private sealed class ProtoReader
		{
			private readonly byte[] _data;

			private readonly int _end;

			private int _position;

			internal ProtoReader(byte[] data, int offset, int length)
			{
				_data = data;
				_position = offset;
				_end = offset + length;
				if (offset < 0 || length < 0 || _end > data.Length)
				{
					throw new InvalidDataException("not a valid ONNX file");
				}
			}

			internal bool HasMore => _position < _end;

			/// <summary>读 tag 并拆出字段号；wireType 6 / 7 非法，字段号 0 非法。</summary>
			internal int ReadTag(out int wireType)
			{
				ulong tag = ReadVarint();
				wireType = (int)(tag & 7UL);
				int field = (int)(tag >> 3);
				if (field <= 0 || wireType >= 6)
				{
					throw new InvalidDataException("not a valid ONNX file");
				}
				return field;
			}

			/// <summary>读 varint（最多 10 字节，超长视为坏数据）。</summary>
			internal ulong ReadVarint()
			{
				ulong value = 0UL;
				int shift = 0;
				while (shift < 64)
				{
					if (_position >= _end)
					{
						throw new InvalidDataException("not a valid ONNX file");
					}
					byte current = _data[_position++];
					value |= (ulong)(current & 0x7F) << shift;
					if ((current & 0x80) == 0)
					{
						return value;
					}
					shift += 7;
				}
				throw new InvalidDataException("not a valid ONNX file");
			}

			/// <summary>按 wireType 跳过整个字段值。</summary>
			internal void SkipField(int wireType)
			{
				switch (wireType)
				{
				case 0:
					ReadVarint();
					return;
				case 1:
					Skip(8L);
					return;
				case 2:
					Skip((long)ReadVarint());
					return;
				case 3:
					SkipGroup();
					return;
				case 5:
					Skip(4L);
					return;
				default:
					throw new InvalidDataException("not a valid ONNX file");
				}
			}

			/// <summary>跳过已废弃的 group 编码：读到匹配的 end-group 为止（嵌套 group 递归消耗）。</summary>
			private void SkipGroup()
			{
				int depth = 1;
				while (depth > 0)
				{
					int wireType;
					ReadTag(out wireType);
					if (wireType == 4)
					{
						depth--;
					}
					else
					{
						SkipField(wireType);
					}
				}
			}

			private void Skip(long count)
			{
				if (count < 0L || count > _end - _position)
				{
					throw new InvalidDataException("not a valid ONNX file");
				}
				_position += (int)count;
			}

			/// <summary>读长度分隔的嵌套消息（与原数组共享底层数据，不拷贝）。</summary>
			internal ProtoReader ReadNested()
			{
				long length = (long)ReadVarint();
				if (length < 0L || length > _end - _position)
				{
					throw new InvalidDataException("not a valid ONNX file");
				}
				ProtoReader nested = new ProtoReader(_data, _position, (int)length);
				_position += (int)length;
				return nested;
			}

			/// <summary>读长度分隔的 UTF-8 字符串（坏字节按替换符宽容解码）。</summary>
			internal string ReadString()
			{
				long length = (long)ReadVarint();
				if (length < 0L || length > _end - _position)
				{
					throw new InvalidDataException("not a valid ONNX file");
				}
				string value = Encoding.UTF8.GetString(_data, _position, (int)length);
				_position += (int)length;
				return value;
			}

			/// <summary>读 packed 编码的 varint 列表（repeated 数值字段的紧凑写法）。</summary>
			internal List<ulong> ReadPackedVarints()
			{
				ProtoReader nested = ReadNested();
				List<ulong> values = new List<ulong>();
				while (nested.HasMore)
				{
					values.Add(nested.ReadVarint());
				}
				return values;
			}
		}

		// ---- SafeTensors ----

		/// <summary>前 8 字节小端 u64 = JSON 头长度 N；随后 N 字节 UTF-8 JSON；剩余是张量数据区。
		/// JSON 里 "__metadata__" 的各键 → "metadata.{k}"；其余条目是张量（dtype / shape / data_offsets）。</summary>
		private static MlModel ParseSafeTensors(byte[] data)
		{
			if (data.Length < 8)
			{
				throw new InvalidDataException("not a valid SafeTensors file");
			}
			ByteReader reader = new ByteReader(data);
			ulong headerLength = reader.ReadU64();
			if (headerLength > (ulong)reader.Remaining)
			{
				throw new InvalidDataException("not a valid SafeTensors file");
			}
			byte[] header = reader.ReadBytes((long)headerLength);
			MlModel model = new MlModel();
			model.Format = "SafeTensors";
			model.Rows.Add(new MlRow { Path = "data bytes", Value = reader.Remaining.ToString(CultureInfo.InvariantCulture), Kind = MlRowKind.Meta });
			using (JsonDocument document = JsonDocument.Parse(header))
			{
				JsonElement root = document.RootElement;
				if (root.ValueKind != JsonValueKind.Object)
				{
					throw new InvalidDataException("not a valid SafeTensors file");
				}
				foreach (JsonProperty property in root.EnumerateObject())
				{
					if (property.Name == "__metadata__")
					{
						if (property.Value.ValueKind == JsonValueKind.Object)
						{
							foreach (JsonProperty meta in property.Value.EnumerateObject())
							{
								string text = meta.Value.ValueKind == JsonValueKind.String ? meta.Value.GetString() : meta.Value.ToString();
								model.Rows.Add(new MlRow { Path = "metadata." + meta.Name, Value = text ?? string.Empty, Kind = MlRowKind.Meta });
							}
						}
						continue;
					}
					model.TensorCount++;
					JsonElement element = property.Value;
					if (element.ValueKind != JsonValueKind.Object)
					{
						continue;
					}
					string prefix = "tensor." + property.Name;
					List<long> shape = new List<long>();
					if (element.TryGetProperty("shape", out JsonElement shapeElement) && shapeElement.ValueKind == JsonValueKind.Array)
					{
						foreach (JsonElement dim in shapeElement.EnumerateArray())
						{
							shape.Add(dim.TryGetInt64(out long value) ? value : 0L);
						}
					}
					model.ParamCount += ProductOf(shape);
					if (element.TryGetProperty("dtype", out JsonElement dtype) && dtype.ValueKind == JsonValueKind.String)
					{
						model.Rows.Add(new MlRow { Path = prefix + ".dtype", Value = dtype.GetString() ?? string.Empty, Kind = MlRowKind.Tensor });
					}
					model.Rows.Add(new MlRow { Path = prefix + ".shape", Value = ShapeText(shape), Kind = MlRowKind.Tensor });
					if (element.TryGetProperty("data_offsets", out JsonElement offsets) && offsets.ValueKind == JsonValueKind.Array)
					{
						long begin = 0L;
						long end = 0L;
						int index = 0;
						foreach (JsonElement offset in offsets.EnumerateArray())
						{
							if (!offset.TryGetInt64(out long value))
							{
								continue;
							}
							if (index == 0)
							{
								begin = value;
							}
							else if (index == 1)
							{
								end = value;
							}
							index++;
						}
						if (index >= 2)
						{
							model.Rows.Add(new MlRow { Path = prefix + ".bytes", Value = (end - begin).ToString(CultureInfo.InvariantCulture), Kind = MlRowKind.Tensor });
						}
					}
				}
			}
			return model;
		}

		// ---- GGUF ----

		/// <summary>魔数 "GGUF" + 版本 u32（支持 2 / 3）+ tensor_count / kv_count u64 + KV 对 + 张量信息。</summary>
		private static MlModel ParseGguf(byte[] data)
		{
			ByteReader reader = new ByteReader(data);
			byte[] magic = reader.ReadBytes(4L);
			if (magic.Length < 4 || magic[0] != 0x47 || magic[1] != 0x47 || magic[2] != 0x55 || magic[3] != 0x46)
			{
				throw new InvalidDataException("not a valid GGUF file");
			}
			uint version = reader.ReadU32();
			if (version != 2U && version != 3U)
			{
				throw new InvalidDataException("unsupported GGUF version");
			}
			ulong tensorCount = reader.ReadU64();
			ulong kvCount = reader.ReadU64();
			MlModel model = new MlModel();
			model.Format = "GGUF";
			model.Rows.Add(new MlRow { Path = "version", Value = version.ToString(CultureInfo.InvariantCulture), Kind = MlRowKind.Meta });
			for (ulong i = 0UL; i < kvCount; i++)
			{
				string key = reader.ReadPrefixedString();
				uint valueType = reader.ReadU32();
				string value = ReadGgufValue(reader, valueType);
				model.Rows.Add(new MlRow { Path = "meta." + key, Value = value, Kind = MlRowKind.Meta });
			}
			for (ulong i = 0UL; i < tensorCount; i++)
			{
				string name = reader.ReadPrefixedString();
				uint dimCount = reader.ReadU32();
				List<long> dims = new List<long>();
				for (uint d = 0U; d < dimCount; d++)
				{
					dims.Add((long)reader.ReadU64());
				}
				uint tensorType = reader.ReadU32();
				ulong offset = reader.ReadU64();
				string prefix = "tensor." + name;
				model.Rows.Add(new MlRow { Path = prefix + ".type", Value = GgufTensorTypeName(tensorType), Kind = MlRowKind.Tensor });
				model.Rows.Add(new MlRow { Path = prefix + ".shape", Value = ShapeText(dims), Kind = MlRowKind.Tensor });
				model.Rows.Add(new MlRow { Path = prefix + ".offset", Value = offset.ToString(CultureInfo.InvariantCulture), Kind = MlRowKind.Tensor });
				model.TensorCount++;
				model.ParamCount += ProductOf(dims);
			}
			return model;
		}

		/// <summary>按 GGUF 值类型读取并渲染成字符串；数组截断到 8 个元素再加省略号与总数。</summary>
		private static string ReadGgufValue(ByteReader reader, uint valueType)
		{
			switch (valueType)
			{
			case 0:
				return reader.ReadU8().ToString(CultureInfo.InvariantCulture);
			case 1:
				return ((sbyte)reader.ReadU8()).ToString(CultureInfo.InvariantCulture);
			case 2:
				return reader.ReadU16().ToString(CultureInfo.InvariantCulture);
			case 3:
				return ((short)reader.ReadU16()).ToString(CultureInfo.InvariantCulture);
			case 4:
				return reader.ReadU32().ToString(CultureInfo.InvariantCulture);
			case 5:
				return ((int)reader.ReadU32()).ToString(CultureInfo.InvariantCulture);
			case 6:
				return reader.ReadF32().ToString(CultureInfo.InvariantCulture);
			case 7:
				return reader.ReadU8() != 0 ? "true" : "false";
			case 8:
				return reader.ReadPrefixedString();
			case 9:
				{
					uint elementType = reader.ReadU32();
					ulong count = reader.ReadU64();
					List<string> parts = new List<string>();
					ulong preview = count < (ulong)GgufArrayPreview ? count : (ulong)GgufArrayPreview;
					for (ulong i = 0UL; i < preview; i++)
					{
						parts.Add(ReadGgufValue(reader, elementType));
					}
					// 超出预览的元素仍要逐个读取（保持流位置正确），只是不再渲染。
					for (ulong i = preview; i < count; i++)
					{
						ReadGgufValue(reader, elementType);
					}
					if (count <= (ulong)GgufArrayPreview)
					{
						return string.Join(", ", parts);
					}
					return string.Join(", ", parts) + ", … (" + count.ToString(CultureInfo.InvariantCulture) + " items)";
				}
			case 10:
				return reader.ReadU64().ToString(CultureInfo.InvariantCulture);
			case 11:
				return ((long)reader.ReadU64()).ToString(CultureInfo.InvariantCulture);
			case 12:
				return reader.ReadF64().ToString(CultureInfo.InvariantCulture);
			default:
				throw new InvalidDataException("unsupported GGUF value type");
			}
		}

		/// <summary>GGUF 张量类型映射；未在安全映射表里的编号写 "type N"。</summary>
		private static string GgufTensorTypeName(uint tensorType)
		{
			switch (tensorType)
			{
			case 0:
				return "F32";
			case 1:
				return "F16";
			case 2:
				return "Q4_0";
			case 3:
				return "Q4_1";
			case 6:
				return "Q5_0";
			case 7:
				return "Q5_1";
			case 8:
				return "Q8_0";
			case 9:
				return "Q8_1";
			case 10:
				return "Q2_K";
			case 11:
				return "Q3_K";
			case 12:
				return "Q4_K";
			case 13:
				return "Q5_K";
			case 14:
				return "Q6_K";
			default:
				return "type " + tensorType.ToString(CultureInfo.InvariantCulture);
			}
		}

		// ---- 通用小工具 ----

		/// <summary>dims 乘积（空 = 标量记 1，含 0 记 0），溢出饱和到 long.MaxValue。</summary>
		private static long ProductOf(List<long> dims)
		{
			long product = 1L;
			foreach (long dim in dims)
			{
				if (dim <= 0L)
				{
					return 0L;
				}
				if (product > long.MaxValue / dim)
				{
					return long.MaxValue;
				}
				product *= dim;
			}
			return product;
		}

		/// <summary>维度列表 → "d0×d1×…"（空列表得空串，供 ONNX 拼进 TYPE[...]）。</summary>
		private static string JoinDims(List<long> dims)
		{
			string[] parts = new string[dims.Count];
			for (int i = 0; i < dims.Count; i++)
			{
				parts[i] = dims[i].ToString(CultureInfo.InvariantCulture);
			}
			return string.Join("×", parts);
		}

		/// <summary>独立的 shape 展示值：空 shape 写 "scalar"（GGUF / SafeTensors 用）。</summary>
		private static string ShapeText(List<long> dims)
		{
			return dims.Count > 0 ? JoinDims(dims) : "scalar";
		}

		/// <summary>小端字节读取器：SafeTensors / GGUF 的定长整数与长度前缀字符串都从这里走。</summary>
		private sealed class ByteReader
		{
			private readonly byte[] _data;

			private int _position;

			internal ByteReader(byte[] data)
			{
				_data = data ?? throw new ArgumentNullException(nameof(data));
			}

			/// <summary>剩余可读字节数。</summary>
			internal long Remaining => _data.Length - _position;

			private void Require(long count)
			{
				if (count < 0L || _position + count > _data.Length)
				{
					throw new InvalidDataException("unexpected end of file");
				}
			}

			internal byte ReadU8()
			{
				Require(1L);
				return _data[_position++];
			}

			internal ushort ReadU16()
			{
				Require(2L);
				ushort value = (ushort)((ushort)_data[_position] | ((ushort)_data[_position + 1] << 8));
				_position += 2;
				return value;
			}

			internal uint ReadU32()
			{
				Require(4L);
				uint value = (uint)(_data[_position] | (_data[_position + 1] << 8) | (_data[_position + 2] << 16) | (_data[_position + 3] << 24));
				_position += 4;
				return value;
			}

			internal ulong ReadU64()
			{
				Require(8L);
				ulong value = 0UL;
				for (int i = 7; i >= 0; i--)
				{
					value = (value << 8) | _data[_position + i];
				}
				_position += 8;
				return value;
			}

			internal float ReadF32()
			{
				return BitConverter.Int32BitsToSingle((int)ReadU32());
			}

			internal double ReadF64()
			{
				return BitConverter.Int64BitsToDouble((long)ReadU64());
			}

			internal byte[] ReadBytes(long count)
			{
				Require(count);
				byte[] copy = new byte[count];
				Buffer.BlockCopy(_data, _position, copy, 0, (int)count);
				_position += (int)count;
				return copy;
			}

			/// <summary>读定长 UTF-8 字符串（坏字节按替换符宽容解码）。</summary>
			internal string ReadString(long byteLength)
			{
				Require(byteLength);
				string value = Encoding.UTF8.GetString(_data, _position, (int)byteLength);
				_position += (int)byteLength;
				return value;
			}

			/// <summary>读 u64 长度前缀字符串（GGUF 的 key / tensor 名）。</summary>
			internal string ReadPrefixedString()
			{
				ulong length = ReadU64();
				return ReadString((long)length);
			}
		}
	}
}
