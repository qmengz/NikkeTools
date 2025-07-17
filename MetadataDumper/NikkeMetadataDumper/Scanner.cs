using Iced.Intel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using static NikkeMetadataDumper.Win32Api;

namespace NikkeMetadataDumper;

internal class Scanner
{
	// il2cpp::vm::MetadataCache::Initialize
	private static readonly string pattern = "48 89 5C 24 ?? 57 48 83 EC ?? 48 8B F9 48 8B DA 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 89 05";
	private static List<Instruction> instructions = new List<Instruction>();
	private static readonly byte[] data = MainApp.moduleBytes;
	private static List<PEHeader.SectionTable> sections = new List<PEHeader.SectionTable>();
	private static ulong baseAddress = 0x0;

	public static void Init()
	{
		// ideally i would disassemble through il2cpp_init, but im too lazy for that
		long matchOffset = FindPattern(data, ParsePattern(pattern))!;

		BinaryReader br = new BinaryReader(new MemoryStream(data));
		sections = Misc.GetSections(br);
		br.BaseStream.Position = 0;
		baseAddress = Misc.GetOptionalHeader(br);

		Console.WriteLine($"Pattern found at offset: 0x{matchOffset:X}");

		Il2cppFunctionAddressData addr = new Il2cppFunctionAddressData((uint)matchOffset);
		instructions = GetInstructions(addr, new ByteArrayCodeReader(data), true);
	}

	public static ulong GetMetadataOffset()
	{
		ulong offset = 0;

		/*
			63EE70 | lea rcx,[84CD8D8h] <- load the string "global-metadata.dat" to the RCX (first arg)
			63EE77 | call 00000000006AE430h <- call vm::MetadataLoader::LoadMetadataFile using it
			63EE7C | mov[9364208h],rax <- RAX will be the output, stored in the RVA 0x9364208
		*/

		bool isFirstMovDone = false;
		bool isCallDone = false;

		foreach (Instruction instr in instructions)
		{
			switch (instr.Mnemonic)
			{
				case Mnemonic.Mov:
				case Mnemonic.Lea: // usually i dont put them together. but again, im too lazy
					if (isFirstMovDone && isCallDone)
					{
						if (instr.Op1Kind == OpKind.Register &&
							instr.Op1Register == Register.RAX)
							return instr.MemoryDisplacement32;
						else
						{
							isFirstMovDone = false;
							isCallDone = false;
						}
						break;
					}
					else
					{
						if (instr.Op0Kind == OpKind.Register &&
							instr.Op1Kind == OpKind.Memory &&
							instr.Op0Register == Register.RCX)
						{
							isFirstMovDone = true;
						}
						else
						{
							// reset everything.
							isFirstMovDone = false;
							isCallDone = false;
						}
					}
					break;
				case Mnemonic.Call:
					if (isFirstMovDone)
						isCallDone = true; // too lazy to verify
					break;
			}
		}

		return offset;
	}

	public static byte[] GetMetadataFromOffset(IntPtr metadataPtr)
	{
		IntPtr metadataPtrAddress = MainApp.ModuleHandle + metadataPtr;
		metadataPtr = Marshal.ReadIntPtr(metadataPtrAddress);
		Console.WriteLine($"MetadataPtr: 0x{metadataPtr.ToInt64():X}");

		VirtualQuery(metadataPtr, out MEMORY_BASIC_INFORMATION mbi, Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION)));

		IntPtr regionBase = mbi.BaseAddress;
		long fullRegionSize = mbi.RegionSize.ToInt64();

		long offsetIntoRegion = metadataPtr.ToInt64() - regionBase.ToInt64();
		long readableSize = fullRegionSize - offsetIntoRegion;

		//Console.WriteLine($"Region base: 0x{regionBase.ToInt64():X}");
		//Console.WriteLine($"Region size: 0x{fullRegionSize:X}");
		//Console.WriteLine($"Offset:      0x{offsetIntoRegion:X}");
		//Console.WriteLine($"Readable:    0x{readableSize:X}");

		if (readableSize <= 0 || readableSize > int.MaxValue)
			throw new InvalidOperationException("Calculated size is invalid.");

		byte[] buffer = new byte[readableSize];
		Marshal.Copy(metadataPtr, buffer, 0, (int)readableSize);

		return buffer;
	}

	public static byte[] GetStringsFromMetadata(IntPtr metadataPtr)
	{
		// MetadataHeader -> stringOffset
		IntPtr metadataPtrAddress = Marshal.ReadIntPtr(MainApp.ModuleHandle + metadataPtr);
		int stringOffset = Marshal.ReadInt32(metadataPtrAddress, 0x18);
		IntPtr stringsDataPtr = metadataPtrAddress + stringOffset;

		//Console.WriteLine($"[Strings] Offset: 0x{stringOffset:X}");
		//Console.WriteLine($"[Strings] Pointer: 0x{stringsDataPtr.ToInt64():X}");

		VirtualQuery(stringsDataPtr, out MEMORY_BASIC_INFORMATION mbi, Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION)));

		IntPtr regionBase = mbi.BaseAddress;
		long regionSize = mbi.RegionSize.ToInt64();

		long offsetIntoRegion = stringsDataPtr.ToInt64() - regionBase.ToInt64();
		long readableSize = regionSize - offsetIntoRegion;

		//Console.WriteLine($"[Strings] Region base: 0x{regionBase.ToInt64():X}");
		//Console.WriteLine($"[Strings] Region size: 0x{regionSize:X}");
		//Console.WriteLine($"[Strings] Offset into region: 0x{offsetIntoRegion:X}");
		//Console.WriteLine($"[Strings] Readable bytes: 0x{readableSize:X}");

		if (readableSize <= 0 || readableSize > int.MaxValue)
			throw new InvalidOperationException("Invalid string region size.");

		byte[] stringData = new byte[readableSize];
		Marshal.Copy(stringsDataPtr, stringData, 0, (int)readableSize);

		return stringData;
	}

	public static byte[] FixAndMerge(byte[] metaData, byte[] strData)
	{
		ProcessStringLiterals(metaData);
		var trimmedMeta = TrimTrailingZerosIn16ByteBlocks(metaData);
		BitConverter.GetBytes(trimmedMeta.Length).CopyTo(trimmedMeta, 24);
		var merged = new byte[trimmedMeta.Length + strData.Length];
		Buffer.BlockCopy(trimmedMeta, 0, merged, 0, trimmedMeta.Length);
		Buffer.BlockCopy(strData, 0, merged, trimmedMeta.Length, strData.Length);
		return TrimTrailingZerosIn16ByteBlocks(merged);
	}

	private static void ProcessStringLiterals(byte[] metaData)
	{
		uint literalCount = ReadUInt32(metaData, 12);
		uint infoOffset = ReadUInt32(metaData, 8);
		uint dataOffset = ReadUInt32(metaData, 16);
		int entryCount = (int)(literalCount / 8);

		for (int i = 0; i < entryCount; i++)
		{
			int entryOffset = (int)infoOffset + i * 8;
			uint length = ReadUInt32(metaData, entryOffset);
			uint offset = ReadUInt32(metaData, entryOffset + 4);

			int dataPos = (int)dataOffset + (int)offset;
			if (dataPos + length > metaData.Length)
			{
				Console.WriteLine($"Skipping invalid offset: {offset}, length: {length}");
				continue;
			}

			var xorKey = (byte)(length ^ 0x2E);
			for (int j = 0; j < length; j++)
			{
				metaData[dataPos + j] ^= xorKey;
			}
		}
	}

	private static byte[] TrimTrailingZerosIn16ByteBlocks(byte[] data)
	{
		int newLength = data.Length;

		while (newLength >= 16 && IsBlockAllZeros(data, newLength - 16, 16))
			newLength -= 16;

		if (newLength == data.Length)
			return data;

		var trimmed = new byte[newLength];
		Buffer.BlockCopy(data, 0, trimmed, 0, newLength);
		return trimmed;
	}

	private static bool IsBlockAllZeros(byte[] data, int offset, int length)
	{
		for (int i = offset; i < offset + length; i++)
			if (data[i] != 0) return false;

		return true;
	}

	private static uint ReadUInt32(byte[] buffer, int offset)
	{
		return BitConverter.ToUInt32(buffer, offset);
	}

	static byte?[] ParsePattern(string pattern)
	{
		string[] tokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		byte?[] result = new byte?[tokens.Length];
		for (int i = 0; i < tokens.Length; i++)
			result[i] = tokens[i] == "??" ? null : Convert.ToByte(tokens[i], 16);
		return result;
	}

	static long FindPattern(byte[] data, byte?[] pattern)
	{
		int limit = data.Length - pattern.Length;
		for (int i = 0; i <= limit; i++)
		{
			bool matched = true;
			for (int j = 0; j < pattern.Length; j++)
			{
				if (pattern[j] is byte b && data[i + j] != b)
				{
					matched = false;
					break;
				}
			}
			if (matched)
				return i;
		}
		return -1;
	}

	public class Il2cppFunctionAddressData
	{
		public uint RVA { get; set; }
		public uint Offset { get; set; }
		public ulong VA { get; set; }

		public Il2cppFunctionAddressData(uint fileOffset)
		{
			foreach (PEHeader.SectionTable section in sections)
			{
				uint start = section.ptrToRawData, end = start + section.sizeOfRawData;
				if (fileOffset >= start && fileOffset < end)
				{
					uint rva = section.virtualAddr + (fileOffset - start);
					RVA = rva;
					Offset = fileOffset;
					VA = (baseAddress + rva);
					return;
				}
			}

			Console.WriteLine($"Couldn't find section for offset 0x{fileOffset:X}");
			Environment.Exit(0);
		}
	}

	public static List<Instruction> GetInstructions(Il2cppFunctionAddressData address, ByteArrayCodeReader codeReader, bool? isDebug = false)
	{
		codeReader.Position = Convert.ToInt32(address.Offset);
		Iced.Intel.Decoder decoder = Iced.Intel.Decoder.Create(IntPtr.Size * 8, codeReader);
		decoder.IP = Convert.ToUInt64(address.RVA);
		List<Instruction> instructions = new List<Instruction>();
		bool debug = isDebug ?? false;

		if (debug) Console.WriteLine("/*");
		while (true)
		{
			Instruction instruction = decoder.Decode();
			if (debug) Console.WriteLine($"\t{instruction.IP:X} | {instruction}");
			instructions.Add(instruction);
			if (instruction.Mnemonic == Mnemonic.Ret) break;
		}
		if (debug) Console.WriteLine("*/");

		return instructions;
	}
}
