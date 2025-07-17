using System;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace NikkeProtoDumper;

public class MainApp
{
	static string CustomNameSpace = "";

	public static void Main(string[] args)
	{
		string dummyDllPath = args.Length > 0 ? args[0] : "DummyDll";
		CustomNameSpace = args.Length > 1 ? args[1] : "";

		List<TypeDefinition> protoTypes = new List<TypeDefinition>();
		DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
		resolver.AddSearchDirectory(dummyDllPath);
		ReaderParameters readerParameters = new ReaderParameters() { AssemblyResolver = resolver };
		ModuleDefinition protoDllModule = AssemblyDefinition.ReadAssembly(Path.Combine(dummyDllPath, "NK.Network.Packet.Runtime.dll"), readerParameters).MainModule;
		protoTypes = protoDllModule.Types.Where(t => isSubClassOf(t, "Google.Protobuf.IMessage")).ToList();
		ProtoSchema schema = new TypeParser().DumpProtos(protoTypes);
		Output2Path(schema, "Nikke.proto");
	}

	private static bool isSubClassOf(TypeDefinition targetType, string subType)
	{
		if (!targetType.HasInterfaces)
			return false;

		return targetType.Interfaces.Any(i => i.InterfaceType.FullName == subType);
	}

	private static void Output2Path(ProtoSchema schema, string outPath)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("syntax = \"proto3\";");

		if (!string.IsNullOrEmpty(CustomNameSpace))
		{
			sb.AppendLine($"namespace {CustomNameSpace};\n");
		}
		else { sb.AppendLine(); }


		foreach (ProtoMessage message in schema.protoMessages)
		{
			sb.AppendLine(Message2String(message));
		}

		foreach (ProtoEnum emessage in schema.protoEnums)
		{
			sb.AppendLine(Enum2String(emessage));
		}

		File.WriteAllText(outPath, sb.ToString());
	}

	private static string Message2String(ProtoMessage message, int indentLevel = 0)
	{
		var sb = new StringBuilder();

		if (!string.IsNullOrEmpty(message.path))
			sb.AppendLine($"{Indent(indentLevel)}// Path: {message.path}");
		sb.AppendLine($"{Indent(indentLevel)}message {message.protoName} {{");

		foreach (var field in message.fieldList)
		{
			sb.AppendLine($"{Indent(indentLevel + 1)}{field.fieldType} {CamelToSnake(field.fieldName)} = {field.val};");
		}

		foreach (var oneOf in message.oneOfList)
		{
			sb.AppendLine($"{Indent(indentLevel + 1)}oneof {oneOf.oneOfName} {{");
			foreach (var f in oneOf.fieldList)
			{
				sb.AppendLine($"{Indent(indentLevel + 2)}{f.fieldType} {CamelToSnake(f.fieldName)} = {f.val};");
			}
			sb.AppendLine($"{Indent(indentLevel + 1)}}}");
		}

		foreach (var nested in message.nestedList)
		{
			sb.Append(Message2String(nested, indentLevel + 1));
		}

		foreach (var nestedEnum in message.nestedEList)
		{
			sb.Append(Enum2String(nestedEnum, indentLevel + 1));
		}

		sb.AppendLine($"{Indent(indentLevel)}}}");
		return sb.ToString();
	}

	private static string Enum2String(ProtoEnum emessage, int indentLevel = 0)
	{
		var sb = new StringBuilder();

		sb.AppendLine($"{Indent(indentLevel)}enum {emessage.enumName} {{");

		foreach (var field in emessage.valDict)
		{
			string pre = indentLevel != 0 ? "" : $"{emessage.enumName}_";
			sb.AppendLine($"{Indent(indentLevel + 1)}{pre}{field.Key} = {field.Value};");
		}

		sb.AppendLine($"{Indent(indentLevel)}}}");

		return sb.ToString();
	}

	private static string Indent(int level) => new string(' ', level*2);

	private static string CamelToSnake(string camelStr)
	{
		bool isAllUppercase = camelStr.All(char.IsUpper); // Beebyte
		if (string.IsNullOrEmpty(camelStr) || isAllUppercase)
			return camelStr;
		return Regex.Replace(camelStr, @"(([a-z])(?=[A-Z][a-zA-Z])|([A-Z])(?=[A-Z][a-z]))", "$1_").ToLower();
	}
}