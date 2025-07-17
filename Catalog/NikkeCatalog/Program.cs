using System.Text;
using System.Security.Cryptography;
using NikkeCatalog;
using System.IO.Compression;

class Program
{
	static void Main()
	{
		string localLowPath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			"AppData", "LocalLow");

		string inputRoot = Path.Combine(localLowPath, "com_proximabeta", "NIKKE");
		string outputRoot = Path.Combine(Directory.GetCurrentDirectory(), "Decrypted");

		foreach (string inputFile in Directory.GetFiles(inputRoot, "*", SearchOption.AllDirectories))
		{
			string relativePath = Path.GetRelativePath(inputRoot, inputFile);
			string outputFilePath = Path.Combine(outputRoot, relativePath);

			// those two will be ignored 
			if (inputFile.EndsWith("ApasRemoteConfig") || inputFile.EndsWith("LipassRemoteConfig"))
				continue;

			Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);

			try
			{
				if (DecryptFile(inputFile, outputFilePath))
					Console.WriteLine($"Decrypted: {relativePath}");
			}
			catch (Exception ex)
			{
				if (ex.Message.Contains("Invalid magic"))
					continue;
				Console.WriteLine($"Failed: {relativePath} => {ex.Message}");
			}
		}

		Console.WriteLine("All files processed.");
	}

	static bool DecryptFile(string inputPath, string outputPath)
	{
		using var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read);
		using var reader = new BinaryReader(fs);

		var header = new NikkeDatabaseHeader
		{
			Magic = reader.ReadBytes(4),
			Version = ReadUInt32BigEndian(reader),
			AesKey = reader.ReadBytes(16),
			SegmentSize = ReadUInt32BigEndian(reader),
			SegmentCount = ReadUInt32BigEndian(reader)
		};

		if (!header.Magic.SequenceEqual(Encoding.ASCII.GetBytes("NKDB")))
			return false; // invalid magic

		if (header.Version != 1)
			return false; // invalid version

		int lengthByteCount = (header.SegmentSize * header.SegmentCount > 0xFFFFFFFF) ? 5 : 4;

		long ReadOffset()
		{
			byte[] offsetBytes = reader.ReadBytes(lengthByteCount);
			return offsetBytes.Aggregate(0L, (acc, b) => (acc << 8) | b);
		}

		long currentOffset = ReadOffset();
		var segments = new (long Offset, long Length, int Index)[header.SegmentCount];

		for (int i = 0; i < header.SegmentCount; i++)
		{
			long nextOffset = ReadOffset();
			segments[i] = (currentOffset, nextOffset - currentOffset, i);
			currentOffset = nextOffset;
		}

		using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write);

		foreach (var (offset, length, index) in segments)
		{
			fs.Seek(offset, SeekOrigin.Begin);
			byte[] segment = reader.ReadBytes((int)length);

			byte[] iv = new byte[16];
			BitConverter.GetBytes(index).CopyTo(iv, 0);
			BitConverter.GetBytes((int)offset).CopyTo(iv, 4);

			byte[] decrypted = DecryptAES_OFB(header.AesKey, iv, segment);

			using var ms = new MemoryStream(decrypted);
			using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
			zlib.CopyTo(output);

		}

		return true;
	}

	static uint ReadUInt32BigEndian(BinaryReader reader)
	{
		byte[] bytes = reader.ReadBytes(4);
		if (BitConverter.IsLittleEndian)
			Array.Reverse(bytes);
		return BitConverter.ToUInt32(bytes, 0);
	}

	static byte[] DecryptAES_OFB(byte[] key, byte[] iv, byte[] input)
	{
		/*
		Console.WriteLine(Convert.ToHexString(key));
		Console.WriteLine(Convert.ToHexString(iv));
		*/
		using Aes aes = Aes.Create();
		aes.Key = key;
		aes.IV = iv;
		MemoryStream testVectorStream = new MemoryStream(input);
		OFBStream testOFBStream = new OFBStream(testVectorStream, aes, CryptoStreamMode.Read);
		MemoryStream cipherTextStream = new MemoryStream();
		testOFBStream.CopyTo(cipherTextStream);
		return cipherTextStream.ToArray();
	}
}

struct NikkeDatabaseHeader
{
	public byte[] Magic;
	public uint Version;
	public byte[] AesKey;
	public uint SegmentSize;
	public uint SegmentCount;
}
