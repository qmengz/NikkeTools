using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static NikkeMetadataDumper.Win32Api;

namespace NikkeMetadataDumper;

public class MainApp
{
	public static IntPtr ModuleHandle = IntPtr.Zero;
	public static byte[] moduleBytes = [];

	private static uint RunThread()
	{
		Run();
		return 0;
	}

	private static unsafe void Run()
	{
		GCSettings.LatencyMode = GCLatencyMode.Batch;
		AllocConsole();
		while (ModuleHandle == IntPtr.Zero)
		{
			if (GetModuleHandle("GameAssembly.dll") != IntPtr.Zero)
			{
				ModuleHandle = GetModuleHandle("GameAssembly.dll");
				break;
			}

			Console.WriteLine("Waiting for GameAssembly.dll to load...");

			Thread.Sleep(2000);
		}
		Console.WriteLine($"GameAssembly loaded!");

		Console.WriteLine($"{ModuleHandle:X}");

		string exepath = Process.GetCurrentProcess().MainModule!.FileName;
		string dllpath = Path.Combine(Path.GetDirectoryName(exepath)!, "GameAssembly.dll");
		moduleBytes = File.ReadAllBytes(dllpath);
		Scanner.Init();

		ulong metadataRVA = Scanner.GetMetadataOffset();
		byte[] metadataBytes = Scanner.GetMetadataFromOffset((IntPtr)metadataRVA);
		byte[] stringsBytes = Scanner.GetStringsFromMetadata((IntPtr)metadataRVA);
		byte[] finalData = Scanner.FixAndMerge(metadataBytes, stringsBytes);
		
		File.WriteAllBytes("global-metadata.dat", finalData);
		Console.WriteLine("Dumped metadata successfully.");
	}

	[UnmanagedCallersOnly(EntryPoint = "DllMain", CallConvs = [typeof(CallConvStdcall)])]
	public static bool DllMain(IntPtr hinstDLL, uint fdwReason, IntPtr lpvReserved)
	{
		switch (fdwReason)
		{
			case 1:
				{
					IntPtr threadHandle = Win32Api.CreateThread(IntPtr.Zero, 0, RunThread, IntPtr.Zero, 0, out _);
					if (threadHandle != IntPtr.Zero)
						Win32Api.CloseHandle(threadHandle);
					break;
				}
		}

		return true;
	}
}