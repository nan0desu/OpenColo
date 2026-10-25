using System.Runtime.InteropServices;

namespace OpenColo.Audio
{
	/// <summary>
	/// A Roland MT-32 or CM-32L, emulated by munt and played through an OpenAL streaming source.
	/// MIDI comes in on the game thread (from the MPU-401); the samples are rendered on a thread
	/// of their own, which munt allows without locking.
	/// </summary>
	internal class MT32 : IDisposable
	{
		// the machines tried when none is asked for: the CM-32L first, as recommended for the game
		private static readonly string[] aPreferredMachines =
		[
			"cm32l_1_02", "cm32l_1_00", "mt32_2_07", "mt32_2_06", "mt32_2_04", "mt32_2_03",
			"mt32_1_07", "mt32_1_06", "mt32_1_05", "mt32_1_04", "mt32_bluer"
		];

		// 6 buffers of 8 ms: about 50 ms from a MIDI message to the speaker
		private const int BufferCount = 6;
		private const int BufferFrames = 256;

		private IntPtr pContext;
		private int iSampleRate;
		private uint uiSource = 0;
		private Thread? oThread = null;
		private volatile bool bRunning = false;
		private byte[] aByte = new byte[1];

		private MT32(IntPtr context, string description)
		{
			this.pContext = context;
			this.iSampleRate = (int)MT32Emu.mt32emu_get_actual_stereo_output_samplerate(context);
			this.Description = description;
		}

		/// <summary>The machine and its ROMs, for the log</summary>
		public string Description { get; }

		/// <summary>
		/// Opens the synth with the ROMs found in directory: the given machine (a munt machine ID,
		/// cm32l_1_02, mt32_2_07, ...), or the first of the preferred ones the ROMs are there for.
		/// Null, and why in message, when there is no library or no complete set of ROMs.
		/// </summary>
		public static MT32? Open(string directory, string? machine, out string message)
		{
			try
			{
				if (!Directory.Exists(directory))
				{
					message = $"no MT-32 ROM directory {directory}";
					return null;
				}

				string[] known = MT32Emu.MachineIDs();
				string[] files = Directory.GetFiles(directory);
				string[] machines = machine != null ? [machine] : aPreferredMachines.Where(known.Contains).ToArray();

				foreach (string candidate in machines)
				{
					IntPtr context = MT32Emu.mt32emu_create_context(IntPtr.Zero, IntPtr.Zero);

					// the files of other machines are ignored, halves of a ROM are merged
					foreach (string file in files)
						MT32Emu.mt32emu_add_machine_rom_file(context, candidate, file);

					if (MT32Emu.mt32emu_open_synth(context) == MT32Emu.RC_OK)
					{
						MT32Emu.mt32emu_get_rom_info(context, out MT32Emu.ROMInfo info);
						string version = Marshal.PtrToStringAnsi(MT32Emu.mt32emu_get_library_version_string()) ?? "";

						message = $"munt {version}, {Marshal.PtrToStringAnsi(info.ControlROMDescription)}, " +
							$"{Marshal.PtrToStringAnsi(info.PCMROMDescription)}";
						return new MT32(context, candidate);
					}

					MT32Emu.mt32emu_free_context(context);
				}

				message = $"no complete ROM set for {string.Join(", ", machines)} in {directory}";
				return null;
			}
			catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
			{
				message = $"no libmt32emu: {ex.Message}";
				return null;
			}
		}

		/// <summary>Starts the output, once OpenAL has a current context</summary>
		public void Start()
		{
			OpenAL.alGenSources(1, out this.uiSource);

			uint[] buffers = new uint[BufferCount];
			short[] samples = new short[BufferFrames * 2];

			OpenAL.alGenBuffers(BufferCount, buffers);

			foreach (uint buffer in buffers)
				Render(buffer, samples);

			OpenAL.alSourceQueueBuffers(this.uiSource, BufferCount, buffers);
			OpenAL.alSourcePlay(this.uiSource);

			this.bRunning = true;
			this.oThread = new Thread(Stream) { Name = "MT-32", IsBackground = true, Priority = ThreadPriority.AboveNormal };
			this.oThread.Start();
		}

		/// <summary>One byte of the MIDI stream; running status and SysEx are taken care of by munt</summary>
		public void Write(byte value)
		{
			this.aByte[0] = value;
			MT32Emu.mt32emu_parse_stream(this.pContext, this.aByte, 1);
		}

		private void Render(uint buffer, short[] samples)
		{
			MT32Emu.mt32emu_render_bit16s(this.pContext, samples, BufferFrames);
			OpenAL.alBufferData(buffer, OpenAL.AL_FORMAT_STEREO16, samples, samples.Length * sizeof(short), this.iSampleRate);
		}

		/// <summary>Refills the buffers OpenAL has played</summary>
		private void Stream()
		{
			short[] samples = new short[BufferFrames * 2];
			uint[] buffer = new uint[1];

			while (this.bRunning)
			{
				OpenAL.alGetSourcei(this.uiSource, OpenAL.AL_BUFFERS_PROCESSED, out int processed);

				for (int i = 0; i < processed; i++)
				{
					OpenAL.alSourceUnqueueBuffers(this.uiSource, 1, buffer);
					Render(buffer[0], samples);
					OpenAL.alSourceQueueBuffers(this.uiSource, 1, buffer);
				}

				// it stops when it runs dry
				OpenAL.alGetSourcei(this.uiSource, OpenAL.AL_SOURCE_STATE, out int state);

				if (state != OpenAL.AL_PLAYING)
					OpenAL.alSourcePlay(this.uiSource);

				Thread.Sleep(2);
			}
		}

		public void Dispose()
		{
			this.bRunning = false;
			this.oThread?.Join();

			if (this.uiSource != 0)
				OpenAL.alSourceStop(this.uiSource);

			if (this.pContext != IntPtr.Zero)
			{
				MT32Emu.mt32emu_close_synth(this.pContext);
				MT32Emu.mt32emu_free_context(this.pContext);
				this.pContext = IntPtr.Zero;
			}
		}
	}
}
