using System;
using System.Collections.Generic;
using System.Text;

namespace NikkeStaticData;

public class ResStaticDataPackInfo : IProtoParsable<ResStaticDataPackInfo>
{
	public string Url { get; set; } = "";
	public long Size { get; set; }
	public byte[] Sha256Sum { get; set; } = Array.Empty<byte>();
	public byte[] Salt1 { get; set; } = Array.Empty<byte>();
	public byte[] Salt2 { get; set; } = Array.Empty<byte>();
	public string Version { get; set; } = "";

	public static ResStaticDataPackInfo ParseFrom(byte[] data)
	{
		var reader = new ProtoReader(data);
		var result = new ResStaticDataPackInfo();

		while (reader.HasMoreData)
		{
			uint tag = reader.ReadVarint();
			int fieldNumber = (int)(tag >> 3);
			int wireType = (int)(tag & 0x7);

			switch (fieldNumber)
			{
				case 1: // string url
					result.Url = reader.ReadLengthDelimitedString();
					break;
				case 2: // int64 size
					result.Size = reader.ReadVarint64();
					break;
				case 3: // bytes sha256_sum
					result.Sha256Sum = reader.ReadLengthDelimitedBytes();
					break;
				case 4: // bytes salt1
					result.Salt1 = reader.ReadLengthDelimitedBytes();
					break;
				case 5: // bytes salt2
					result.Salt2 = reader.ReadLengthDelimitedBytes();
					break;
				case 6: // string version
					result.Version = reader.ReadLengthDelimitedString();
					break;
				default:
					reader.SkipField(wireType);
					break;
			}
		}

		return result;
	}
}
