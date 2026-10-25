using System.Runtime.InteropServices;

namespace OpenColo.Audio
{
	/// <summary>
	/// The few calls of libmt32emu's C API (munt, https://github.com/munt/munt) that the Roland
	/// MT-32 / CM-32L needs. The library comes from the system (the mt32emu package); the ROMs
	/// come from the user.
	/// </summary>
	internal static class MT32Emu
	{
		private const string Library = "mt32emu";

		// mt32emu_return_code
		public const int RC_OK = 0;

		static MT32Emu()
		{
			NativeLibraries.Register();
		}

		/// <summary>mt32emu_rom_info: what the context has identified, null where nothing is loaded</summary>
		[StructLayout(LayoutKind.Sequential)]
		public struct ROMInfo
		{
			public IntPtr ControlROMID;
			public IntPtr ControlROMDescription;
			public IntPtr ControlROMSHA1;
			public IntPtr PCMROMID;
			public IntPtr PCMROMDescription;
			public IntPtr PCMROMSHA1;
		}

		[DllImport(Library)] public static extern IntPtr mt32emu_get_library_version_string();
		[DllImport(Library)] public static extern UIntPtr mt32emu_get_machine_ids(IntPtr[]? machineIDs, UIntPtr size);
		// the report handler is a union of interface pointers, null for the default one
		[DllImport(Library)] public static extern IntPtr mt32emu_create_context(IntPtr reportHandler, IntPtr instanceData);
		[DllImport(Library)] public static extern void mt32emu_free_context(IntPtr context);
		[DllImport(Library)] public static extern int mt32emu_add_machine_rom_file(IntPtr context, string machineID, string fileName);
		[DllImport(Library)] public static extern void mt32emu_get_rom_info(IntPtr context, out ROMInfo info);
		[DllImport(Library)] public static extern int mt32emu_open_synth(IntPtr context);
		[DllImport(Library)] public static extern void mt32emu_close_synth(IntPtr context);
		// 32000 Hz unless an analog output mode or rate that converts it is set
		[DllImport(Library)] public static extern uint mt32emu_get_actual_stereo_output_samplerate(IntPtr context);
		[DllImport(Library)] public static extern void mt32emu_parse_stream(IntPtr context, byte[] stream, uint length);
		[DllImport(Library)] public static extern void mt32emu_render_bit16s(IntPtr context, short[] stream, uint frames);

		/// <summary>The machines the library knows: mt32_1_07, cm32l_1_02, ...</summary>
		public static string[] MachineIDs()
		{
			int count = (int)mt32emu_get_machine_ids(null, UIntPtr.Zero);
			IntPtr[] ids = new IntPtr[count];

			mt32emu_get_machine_ids(ids, (UIntPtr)count);

			return ids.Select(id => Marshal.PtrToStringAnsi(id) ?? "").ToArray();
		}
	}
}
