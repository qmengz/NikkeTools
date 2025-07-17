using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using System.Text;
using System.Threading.Tasks;
using Mono.Cecil.Rocks;

namespace NikkeProtoDumper;

internal class TypeParser
{
	private List<ProtoEnum> protoEnums = new List<ProtoEnum>();
	public ProtoSchema DumpProtos(List<TypeDefinition> protoTypes)
	{
		ProtoSchema protoSchema = new ProtoSchema();
		foreach (TypeDefinition type in protoTypes)
		{
			protoSchema.protoMessages.Add(DumpType2Message(type, ref protoSchema));
		}
		foreach (ProtoEnum dumpedEnum in protoEnums)
		{
			protoSchema.protoEnums.Add(dumpedEnum);
		}
		return protoSchema;
	}

	private ProtoMessage DumpType2Message(TypeDefinition type, ref ProtoSchema schema)
	{
		var protoMessage = new ProtoMessage
		{
			protoName = type.Name,
			path = GetPath4Proto(type),
		};

		var tagMap = type.Fields
			.Where(f => f.IsLiteral && f.Name.EndsWith("FieldNumber"))
			.ToDictionary(
				f => f.Name.Replace("FieldNumber", ""),
				f => (uint)(int)f.Constant
			);

		var oneOfCaseEnum = type.NestedTypes.FirstOrDefault(t => t.Name.EndsWith("OneofCase"));
		var oneOfFieldNames = new HashSet<string>();

		var oneOfBackingField = type.Fields.FirstOrDefault(f =>
			f.FieldType.FullName == "System.Object" &&
			f.Name.EndsWith("_"));

		if (oneOfCaseEnum != null)
		{
			oneOfFieldNames.UnionWith(
				oneOfCaseEnum.Fields
					.Where(f => f.IsLiteral && f.Name != "None")
					.Select(f => f.Name)
			);

			var oneOf = new OneOf { oneOfName = oneOfBackingField!.Name.TrimEnd('_') };
			foreach (var oneOfField in oneOfFieldNames)
			{
				var tag = tagMap.ContainsKey(oneOfField) ? tagMap[oneOfField] : 0;
				var prop = type.Properties.First(p => p.Name == oneOfField);
				oneOf.fieldList.Add(new ProtoField
				{
					fieldName = char.ToLowerInvariant(oneOfField[0]) + oneOfField.Substring(1),
					fieldType = FieldType2ProtoString(prop.PropertyType),
					val = tag
				});
			}
			protoMessage.oneOfList.Add(oneOf);
		}

		foreach (var prop in type.Properties)
		{
			var cleanName = prop.Name;
			if (oneOfFieldNames.Contains(cleanName))
				continue;

			if (tagMap.TryGetValue(cleanName, out var num))
			{
				protoMessage.fieldList.Add(new ProtoField
				{
					fieldName = char.ToLowerInvariant(cleanName[0]) + cleanName.Substring(1),
					fieldType = FieldType2ProtoString(prop.PropertyType),
					val = num,
					isRepeated = prop.PropertyType.FullName.StartsWith("System.Collections.Generic.List<") ||
								 prop.PropertyType.FullName.StartsWith("Google.Protobuf.Collections.RepeatedField<")
				});
			}
		}

		if (type.HasNestedTypes)
		{
			var nestedTypes = type.NestedTypes.FirstOrDefault(t => t.Name == "Types")?.Resolve();
			if (nestedTypes != null)
			{
				foreach (var nestedType in nestedTypes.NestedTypes)
				{
					if (nestedType.IsEnum && nestedType != oneOfCaseEnum)
					{
						protoMessage.nestedEList.Add(DumpType2Enum(nestedType));
					}
					else
					{
						protoMessage.nestedList.Add(DumpType2Message(nestedType, ref schema));
					}
				}
			}
			foreach (var nestedType in type.NestedTypes)
			{
				if (nestedType.IsEnum && nestedType != oneOfCaseEnum)
				{
					protoMessage.nestedEList.Add(DumpType2Enum(nestedType));
				}
			}
		}


		return protoMessage;
	}

	private string FieldType2ProtoString(TypeReference type)
	{
		if (type.FullName.StartsWith("Google.Protobuf.Collections.MapField"))
		{
			GenericInstanceType genericInstance = (GenericInstanceType)type;
			TypeReference keyRef = genericInstance.GenericArguments[0];
			TypeReference valRef = genericInstance.GenericArguments[1];
			return $"map<{FieldType2ProtoString(keyRef)}, {FieldType2ProtoString(valRef)}>";
		}
		else if (type.FullName.StartsWith("Google.Protobuf.Collections.RepeatedField"))
		{
			GenericInstanceType genericInstance = (GenericInstanceType)type;
			TypeReference keyRef = genericInstance.GenericArguments[0];
			return $"repeated {FieldType2ProtoString(keyRef)}";
		}
		else if (type.FullName.StartsWith("System.Nullable"))
		{
			GenericInstanceType genericInstance = (GenericInstanceType)type;
			TypeReference keyRef = genericInstance.GenericArguments[0];
			return $"{FieldType2ProtoString(keyRef)}";
		}

		if (type.Resolve().IsEnum && !protoEnums.Any(e => e.enumName == type.Name))
		{
			protoEnums.Add(DumpType2Enum(type.Resolve()));
		}

		switch (type.FullName)
		{
			case "System.Int32":
				return "int32";
			case "Google.Protobuf.ByteString":
				return "bytes";
			case "Google.Protobuf.WellKnownTypes.Timestamp":
				return "google.protobuf.Timestamp";
			case "Google.Protobuf.WellKnownTypes.Duration":
				return "google.protobuf.Duration";
			case "System.UInt32":
				return "uint32";
			case "System.Int64":
				return "int64";
			case "System.UInt64":
				return "uint64";
			case "System.String":
				return "string";
			case "System.Boolean":
				return "bool";
			case "System.Single":
				return "float";
			case "System.Double":
				return "double";
			default:
				if (type.FullName.StartsWith("System."))
					Console.WriteLine($"[WARN] Unknown System type {type.FullName}");
				if (type.FullName.StartsWith("Google.Protobuf"))
					Console.WriteLine($"[WARN] Unknown Google.Protobuf type {type.FullName}");
				return type.Name;
		}
	}

	private ProtoEnum DumpType2Enum(TypeDefinition type)
	{
		ProtoEnum ret = new ProtoEnum();
		ret.enumName = type.Name;

		foreach (FieldDefinition fieldDef in type.Fields.Where(f => f.HasConstant))
		{
			ret.valDict.Add(fieldDef.Name, Convert.ToInt32(fieldDef.Constant));
		}

		return ret;
	}

	private static string GetPath4Proto(TypeDefinition type)
	{
		if (!type.HasCustomAttributes || !type.CustomAttributes.Any(i => i.AttributeType.Name == "PacketActionAttribute"))
			return string.Empty;

		CustomAttribute attr = type.CustomAttributes.First(i => i.AttributeType.Name == "PacketActionAttribute");
		return $"/{attr.ConstructorArguments.First(a => a.Type.Name == "String").Value.ToString()}";
	}
}
