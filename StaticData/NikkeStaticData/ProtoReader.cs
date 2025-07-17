using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NikkeStaticData;

public class ProtoReader
{
	private readonly byte[] buffer;
	private int position;

	public ProtoReader(byte[] data)
	{
		buffer = data;
		position = 0;
	}

	public bool HasMoreData => position < buffer.Length;

	public uint ReadVarint()
	{
		uint result = 0;
		int shift = 0;

		while (true)
		{
			byte b = buffer[position++];
			result |= (uint)(b & 0x7F) << shift;

			if ((b & 0x80) == 0)
				break;

			shift += 7;
		}

		return result;
	}

	public long ReadVarint64()
	{
		long result = 0;
		int shift = 0;

		while (true)
		{
			byte b = buffer[position++];
			result |= (long)(b & 0x7F) << shift;

			if ((b & 0x80) == 0)
				break;

			shift += 7;
		}

		return result;
	}

	public byte[] ReadLengthDelimitedBytes()
	{
		int length = (int)ReadVarint();
		byte[] result = new byte[length];
		Array.Copy(buffer, position, result, 0, length);
		position += length;
		return result;
	}

	public string ReadLengthDelimitedString()
	{
		var bytes = ReadLengthDelimitedBytes();
		return Encoding.UTF8.GetString(bytes);
	}

	public void SkipField(int wireType)
	{
		switch (wireType)
		{
			case 0: // varint
				ReadVarint();
				break;
			case 1: // 64-bit
				position += 8;
				break;
			case 2: // length-delimited
				int len = (int)ReadVarint();
				position += len;
				break;
			case 5: // 32-bit
				position += 4;
				break;
			default:
				throw new NotSupportedException($"Unsupported wire type {wireType}");
		}
	}
}