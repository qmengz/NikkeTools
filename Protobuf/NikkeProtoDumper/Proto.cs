using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

namespace NikkeProtoDumper;

public class ProtoSchema
{
	public List<ProtoMessage> protoMessages = new();
	public List<ProtoEnum> protoEnums = new();
}

public class ProtoMessage
{
	public string path = "";
	public string protoName = "";
	public List<string> importNameList = new();
	public List<ProtoField> fieldList = new();
	public List<OneOf> oneOfList = new();
	public List<ProtoMessage> nestedList = new();
	public List<ProtoEnum> nestedEList = new();
}

public class ProtoField
{
	public string fieldName = "";
	public string fieldType = "";
	public string? mapKey;
	public string? mapValue;
	public bool isRepeated;
	public uint val;
}

public class OneOf
{
	public string oneOfName = "";
	public List<ProtoField> fieldList = new();
}

public class ProtoEnum
{
	public string enumName = "";
	public Dictionary<string, int> valDict = new();
}