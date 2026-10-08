using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace ForkPlus.Plugins.ManagedAssembly
{
	/// <summary>
	/// 托管程序集（.NET assembly）解析（纯托管，只用共享框架内置的
	/// <c>System.Reflection.Metadata</c> / <c>System.Reflection.PortableExecutable</c>）。
	///
	/// 只做「对比」需要的最小解析，产出三套键路径行：
	/// <list type="bullet">
	/// <item><b>标识</b>——程序集名 / 版本 / 区域 / 公钥标记 / 标志 / 元数据版本 / 目标框架，
	/// 以及 PE 层面的 machine / CorFlags。</item>
	/// <item><b>类型</b>——命名空间 → 类型 → 方法（含参数类型签名）/ 字段（含字段类型）。</item>
	/// <item><b>引用</b>——AssemblyRef 清单（外部程序集引用及版本）。</item>
	/// </list>
	///
	/// 出于对比展示的安全阈值：类型最多 5000 个、每类型成员最多 200 个、引用最多 500 条，
	/// 超出置 <see cref="ManagedDocument.Truncated"/>。非托管 PE（无 CLI 元数据）归类为失败。
	///
	/// 对外入口 <see cref="Parse"/> 不抛异常：失败返回 null 并回填 error，视图按「无法解析」提示。
	/// </summary>
	internal static class ManagedAssemblyParser
	{
		/// <summary>类型读取上限。</summary>
		private const int MaxTypes = 5000;

		/// <summary>单类型成员（方法 + 字段）读取上限。</summary>
		private const int MaxMembersPerType = 200;

		/// <summary>外部程序集引用上限。</summary>
		private const int MaxReferences = 500;

		/// <summary>成功返回文档，失败返回 null 并回填 error（error = 异常类型名 + 消息）。</summary>
		internal static ManagedDocument Parse(byte[] data, out string error)
		{
			error = null;
			try
			{
				return ParseCore(data);
			}
			catch (Exception ex)
			{
				error = ex.GetType().Name + ": " + ex.Message;
				return null;
			}
		}

		private static ManagedDocument ParseCore(byte[] data)
		{
			if (data == null || data.Length < 128)
			{
				throw new InvalidDataException("not a valid PE file: file too small");
			}
			using (MemoryStream stream = new MemoryStream(data, false))
			using (PEReader pe = new PEReader(stream))
			{
				if (!pe.HasMetadata)
				{
					throw new InvalidDataException("not a managed assembly: no CLI metadata");
				}
				MetadataReader reader = pe.GetMetadataReader();
				ManagedDocument document = new ManagedDocument();
				BuildIdentity(reader, pe, document);
				BuildTypes(reader, document);
				BuildReferences(reader, document);
				return document;
			}
		}

		// ---- 标识口径 ----

		private static void BuildIdentity(MetadataReader reader, PEReader pe, ManagedDocument document)
		{
			List<ManagedRow> rows = document.IdentityRows;
			if (reader.IsAssembly)
			{
				AssemblyDefinition assembly = reader.GetAssemblyDefinition();
				string name = reader.GetString(assembly.Name);
				string culture = reader.GetString(assembly.Culture);
				byte[] publicKey = assembly.PublicKey.IsNil ? Array.Empty<byte>() : reader.GetBlobBytes(assembly.PublicKey);
				document.AssemblyName = name;
				document.AssemblyVersion = assembly.Version.ToString();

				Add(rows, "assembly.name", name);
				Add(rows, "assembly.version", assembly.Version.ToString());
				Add(rows, "assembly.culture", string.IsNullOrEmpty(culture) ? "neutral" : culture);
				Add(rows, "assembly.public key token", PublicKeyToken(publicKey));
				Add(rows, "assembly.flags", DescribeAssemblyFlags(assembly.Flags));
				Add(rows, "assembly.hash algorithm", assembly.HashAlgorithm.ToString());
			}
			else
			{
				// 只有元数据、没有 Assembly 行（如部分 netmodule）：仍给出提示而非空白。
				Add(rows, "assembly.name", "(no assembly manifest)");
			}

			Add(rows, "metadata.version", reader.MetadataVersion);
			Add(rows, "target framework", ReadTargetFramework(reader));
			Add(rows, "machine", pe.PEHeaders.CoffHeader.Machine.ToString());
			Add(rows, "characteristics", pe.PEHeaders.CoffHeader.Characteristics.ToString());
			CorHeader cor = pe.PEHeaders.CorHeader;
			if (cor != null)
			{
				Add(rows, "corflags", cor.Flags.ToString());
			}
		}

		/// <summary>COM 公钥标记：公钥 SHA-1 的后 8 字节逆序，全小写十六进制。</summary>
		private static string PublicKeyToken(byte[] publicKey)
		{
			if (publicKey == null || publicKey.Length == 0)
			{
				return "null";
			}
			using (SHA1 sha1 = SHA1.Create())
			{
				byte[] hash = sha1.ComputeHash(publicKey);
				StringBuilder builder = new StringBuilder(16);
				for (int i = hash.Length - 1; i >= hash.Length - 8; i--)
				{
					builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
				}
				return builder.ToString();
			}
		}

		private static string DescribeAssemblyFlags(AssemblyFlags flags)
		{
			return (int)flags == 0 ? "None" : flags.ToString();
		}

		/// <summary>从程序集自定义特性里取 TargetFrameworkAttribute 的框架名（取不到回 "(unknown)"）。</summary>
		private static string ReadTargetFramework(MetadataReader reader)
		{
			if (!reader.IsAssembly)
			{
				return "(unknown)";
			}
			AssemblyDefinition assembly = reader.GetAssemblyDefinition();
			foreach (CustomAttributeHandle handle in assembly.GetCustomAttributes())
			{
				CustomAttribute attribute = reader.GetCustomAttribute(handle);
				string typeName = AttributeTypeName(reader, attribute);
				if (!string.Equals(typeName, "TargetFrameworkAttribute", StringComparison.Ordinal))
				{
					continue;
				}
				if (attribute.Constructor.Kind != HandleKind.MemberReference)
				{
					continue;
				}
				MemberReference ctor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
				BlobReader blob = reader.GetBlobReader(attribute.Value);
				// 自定义特性值：2 字节 prolog（0x0001）后跟定长参数（此处即一个 SerString）。
				if (blob.ReadUInt16() != 1)
				{
					continue;
				}
				string value = blob.ReadSerializedString();
				if (!string.IsNullOrEmpty(value))
				{
					return value;
				}
			}
			return "(unknown)";
		}

		private static string AttributeTypeName(MetadataReader reader, CustomAttribute attribute)
		{
			try
			{
				switch (attribute.Constructor.Kind)
				{
				case HandleKind.MemberReference:
				{
					MemberReference reference = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
					return reference.Parent.Kind == HandleKind.TypeReference
						? reader.GetString(reader.GetTypeReference((TypeReferenceHandle)reference.Parent).Name)
						: string.Empty;
				}
				case HandleKind.MethodDefinition:
				{
					MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
					TypeDefinition type = reader.GetTypeDefinition(method.GetDeclaringType());
					return reader.GetString(type.Name);
				}
				default:
					return string.Empty;
				}
			}
			catch (Exception)
			{
				return string.Empty;
			}
		}

		// ---- 类型口径 ----

		private static void BuildTypes(MetadataReader reader, ManagedDocument document)
		{
			List<ManagedRow> rows = document.TypeRows;
			TypeNameProvider provider = new TypeNameProvider(reader);
			int typeCount = 0;
			foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
			{
				if (typeCount >= MaxTypes)
				{
					document.Truncated = true;
					break;
				}
				TypeDefinition definition = reader.GetTypeDefinition(handle);
				string name = reader.GetString(definition.Name);
				// <Module> 是每程序集都有的伪类型，跳过。
				if (string.Equals(name, "<Module>", StringComparison.Ordinal))
				{
					continue;
				}
				typeCount++;
				document.TypeCount++;

				string ns = reader.GetString(definition.Namespace);
				string fullName = string.IsNullOrEmpty(ns) ? name : ns + "." + name;

				int methodCount = 0;
				int fieldCount = 0;
				foreach (MethodDefinitionHandle methodHandle in definition.GetMethods())
				{
					methodCount++;
				}
				foreach (FieldDefinitionHandle fieldHandle in definition.GetFields())
				{
					fieldCount++;
				}
				document.MethodCount += methodCount;
				document.FieldCount += fieldCount;

				Add(rows, "type." + fullName, Kind(definition.Attributes) + " · " + Plural(methodCount, "method") + " · " + Plural(fieldCount, "field"));

				int members = 0;
				foreach (MethodDefinitionHandle methodHandle in definition.GetMethods())
				{
					if (members >= MaxMembersPerType)
					{
						document.Truncated = true;
						break;
					}
					members++;
					MethodDefinition method = reader.GetMethodDefinition(methodHandle);
					string methodName = reader.GetString(method.Name);
					MethodSignature<string> signature = method.DecodeSignature(provider, null);
					string parameters = string.Join(", ", signature.ParameterTypes);
					string visibility = DescribeMethodVisibility(method.Attributes);
					Add(rows, "type." + fullName + ".method." + methodName + "(" + parameters + ")", visibility + " → " + signature.ReturnType);
				}
				members = 0;
				foreach (FieldDefinitionHandle fieldHandle in definition.GetFields())
				{
					if (members >= MaxMembersPerType)
					{
						document.Truncated = true;
						break;
					}
					members++;
					FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
					string fieldName = reader.GetString(field.Name);
					string fieldType = field.DecodeSignature(provider, null);
					Add(rows, "type." + fullName + ".field." + fieldName, DescribeFieldVisibility(field.Attributes) + " " + fieldType);
				}
			}
		}

		private static string Kind(TypeAttributes attributes)
		{
			return (attributes & TypeAttributes.Interface) != 0 ? "interface" : "type";
		}

		private static string DescribeMethodVisibility(MethodAttributes attributes)
		{
			switch (attributes & MethodAttributes.MemberAccessMask)
			{
			case MethodAttributes.Public:
				return "public";
			case MethodAttributes.Private:
				return "private";
			case MethodAttributes.Family:
				return "protected";
			case MethodAttributes.Assembly:
				return "internal";
			case MethodAttributes.FamORAssem:
				return "protected internal";
			case MethodAttributes.FamANDAssem:
				return "private protected";
			default:
				return string.Empty;
			}
		}

		private static string DescribeFieldVisibility(FieldAttributes attributes)
		{
			switch (attributes & FieldAttributes.FieldAccessMask)
			{
			case FieldAttributes.Public:
				return "public";
			case FieldAttributes.Private:
				return "private";
			case FieldAttributes.Family:
				return "protected";
			case FieldAttributes.Assembly:
				return "internal";
			case FieldAttributes.FamORAssem:
				return "protected internal";
			case FieldAttributes.FamANDAssem:
				return "private protected";
			default:
				return string.Empty;
			}
		}

		// ---- 引用口径 ----

		private static void BuildReferences(MetadataReader reader, ManagedDocument document)
		{
			List<ManagedRow> rows = document.ReferenceRows;
			int count = 0;
			foreach (AssemblyReferenceHandle handle in reader.AssemblyReferences)
			{
				if (count >= MaxReferences)
				{
					document.Truncated = true;
					break;
				}
				count++;
				document.ReferenceCount++;
				AssemblyReference reference = reader.GetAssemblyReference(handle);
				string name = reader.GetString(reference.Name);
				string culture = reader.GetString(reference.Culture);
				byte[] token = reference.PublicKeyOrToken.IsNil ? Array.Empty<byte>() : reader.GetBlobBytes(reference.PublicKeyOrToken);
				string description = reference.Version.ToString();
				if (!string.IsNullOrEmpty(culture))
				{
					description = description + " · " + culture;
				}
				if (token.Length > 0)
				{
					description = description + " · " + Hex(token);
				}
				Add(rows, "reference." + name, description);
			}
		}

		// ---- 小工具 ----

		private static void Add(List<ManagedRow> rows, string path, string value)
		{
			rows.Add(new ManagedRow { Path = path, Value = value ?? string.Empty });
		}

		private static string Plural(int count, string noun)
		{
			return count.ToString(CultureInfo.InvariantCulture) + " " + noun + (count == 1 ? string.Empty : "s");
		}

		private static string Hex(byte[] bytes)
		{
			StringBuilder builder = new StringBuilder(bytes.Length * 2);
			foreach (byte b in bytes)
			{
				builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
			}
			return builder.ToString();
		}

		/// <summary>
		/// 把元数据签名里的类型引用渲染成可读字符串（命名空间.名字 / 泛型 / 数组 / 指针）。
		/// 拿到的是「类型 token」，此处只做展示，不追求与 C# 语法逐字一致。
		/// </summary>
		private sealed class TypeNameProvider : ISignatureTypeProvider<string, object>
		{
			private readonly MetadataReader _reader;

			internal TypeNameProvider(MetadataReader reader)
			{
				_reader = reader;
			}

			public string GetPrimitiveType(PrimitiveTypeCode typeCode)
			{
				return typeCode.ToString();
			}

			public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
			{
				TypeDefinition definition = reader.GetTypeDefinition(handle);
				string ns = reader.GetString(definition.Namespace);
				string name = reader.GetString(definition.Name);
				return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
			}

			public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
			{
				TypeReference reference = reader.GetTypeReference(handle);
				string ns = reader.GetString(reference.Namespace);
				string name = reader.GetString(reference.Name);
				return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
			}

			public string GetTypeFromSpecification(MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
			{
				return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
			}

			public string GetSZArrayType(string elementType)
			{
				return elementType + "[]";
			}

			public string GetArrayType(string elementType, ArrayShape shape)
			{
				return elementType + "[" + new string(',', shape.Rank - 1) + "]";
			}

			public string GetByReferenceType(string elementType)
			{
				return "ref " + elementType;
			}

			public string GetPointerType(string elementType)
			{
				return elementType + "*";
			}

			public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
			{
				return genericType + "<" + string.Join(", ", typeArguments) + ">";
			}

			public string GetGenericMethodParameter(object genericContext, int index)
			{
				return "!!" + index.ToString(CultureInfo.InvariantCulture);
			}

			public string GetGenericTypeParameter(object genericContext, int index)
			{
				return "!" + index.ToString(CultureInfo.InvariantCulture);
			}

			public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
			{
				return unmodifiedType;
			}

			public string GetPinnedType(string elementType)
			{
				return elementType;
			}

			public string GetFunctionPointerType(MethodSignature<string> signature)
			{
				return "delegate*<" + string.Join(", ", signature.ParameterTypes) + ", " + signature.ReturnType + ">";
			}
		}
	}
}