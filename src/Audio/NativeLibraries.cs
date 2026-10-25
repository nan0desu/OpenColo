using System.Reflection;
using System.Runtime.InteropServices;

namespace OpenColo.Audio
{
	/// <summary>
	/// Finds the system libraries the audio uses under their names on each platform. An assembly
	/// can have only one resolver, so OpenAL and libmt32emu share this one.
	/// </summary>
	internal static class NativeLibraries
	{
		private static readonly Dictionary<string, string[]> aCandidates = new()
		{
			["openal"] = ["libopenal.so.1", "libopenal.so", "OpenAL32.dll", "soft_oal.dll", "libopenal.1.dylib", "libopenal.dylib"],
			["mt32emu"] = ["libmt32emu.so.2", "libmt32emu.so", "mt32emu.dll", "libmt32emu.2.dylib", "libmt32emu.dylib"]
		};

		private static readonly object oLock = new();
		private static bool bRegistered = false;

		public static void Register()
		{
			lock (oLock)
			{
				if (bRegistered)
					return;

				NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), Resolve);
				bRegistered = true;
			}
		}

		private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
		{
			if (!aCandidates.TryGetValue(name, out string[]? candidates))
				return IntPtr.Zero;

			foreach (string candidate in candidates)
			{
				if (NativeLibrary.TryLoad(candidate, assembly, path, out IntPtr handle))
					return handle;
			}

			return IntPtr.Zero;
		}
	}
}
