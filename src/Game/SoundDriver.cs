using IRB.VirtualCPU;
using OpenColo.Audio;

namespace OpenColo
{
	/// <summary>
	/// The #SOUND.COL drivers (docs/sound.md). RSOUND.COL, the Roland driver, runs as generated code
	/// on an emulated MT-32 or CM-32L, when munt and the ROMs are there. Any other driver gets a
	/// stand-in: a stub image whose five entry points are host functions in the dispatch table,
	/// which plays the digitized effects and accepts music requests without playing them.
	/// The digitized effects always come from COLDIG.BIN through OpenAL.
	/// </summary>
	public class SoundDriver
	{
		// The sample table of the drivers (cs:019f in ASOUND.COL, the same in all four): offset and
		// length in COLDIG.BIN, unsigned 8-bit mono. Samples 0-4 play at 19050 Hz, the rest at 11025 Hz
		// (the rate chosen in the driver's player, cs:0ce2). The effects don't use 0-4, 15 and 23-26.
		private static readonly (int Offset, int Length)[] aSamples =
		[
			(0, 4960), (4960, 4952), (9912, 4833), (14745, 4683), (19428, 4718),
			(24146, 46745), (70891, 39737), (110628, 51364), (161992, 61684), (223676, 45999),
			(269675, 42299), (311974, 39737), (351711, 38912), (390623, 60739), (451362, 61684),
			(513046, 57327), (570373, 46480), (616853, 12040), (628893, 12545), (641438, 59707),
			(701145, 22418), (723563, 8557), (732120, 42496), (774616, 5894), (780510, 7941),
			(788451, 20992), (809443, 34623), (844066, 19299), (863365, 30207), (893572, 11071),
			(904643, 13629), (918272, 5455), (923727, 9407), (933134, 36299), (969433, 24322)
		];

		// Effect requests 40h-5Dh to samples, from the request entry of ASOUND.COL (the jump table at
		// cs:1bb9). -1: the effect has only the FM part, which isn't played.
		private static readonly int[] aEffects =
		[
			31, 32, 30, 27, 18, 17, -1, -1, 29, 34, 28, 33, 14, 10, 6, 11,
			7, 5, 12, 19, 13, 20, 9, 16, 21, -1, -1, 22, 8, -1
		];

		// The same for RSOUND.COL (cs:2b63): 5Ah plays sample 15, where ASOUND.COL has only FM
		private static readonly int[] aRolandEffects =
		[
			31, 32, 30, 27, 18, 17, -1, -1, 29, 34, 28, 33, 14, 10, 6, 11,
			7, 5, 12, 19, 13, 20, 9, 16, 21, -1, 15, 22, 8, -1
		];

		// where the stub image puts the entry points, after the header fields the loader reads
		private const ushort EntryOffset = 0x40;

		// The Roland driver's code is generated for this segment (the Disassembler's
		// ProcessSoundDriverRunnable), the top 48 KB of conventional memory
		private const ushort RolandSegment = 0x9400;
		private const ushort MPUPort = 0x330;

		private OpenColoGame oGame;
		private VCPU oCPU;
		private byte[]? aDigital = null;
		private uint[] aBuffers = new uint[aSamples.Length];
		private uint uiSource = 0;
		private bool bAudio = false;
		private bool bMusic = false;
		private MT32? oMT32 = null;
		private bool bRolandDigital = false;

		public SoundDriver(OpenColoGame game)
		{
			this.oGame = game;
			this.oCPU = game.CPU;

			string path = $"{VCPU.DefaultGamePath}COLDIG.BIN";

			if (File.Exists(path))
				this.aDigital = File.ReadAllBytes(path);
			else
				Report("COLDIG.BIN not found, no sound effects");

			try
			{
				IntPtr device = OpenAL.alcOpenDevice(null);
				IntPtr context = device != IntPtr.Zero ? OpenAL.alcCreateContext(device, IntPtr.Zero) : IntPtr.Zero;

				if (context != IntPtr.Zero && OpenAL.alcMakeContextCurrent(context))
				{
					OpenAL.alGenSources(1, out this.uiSource);
					this.bAudio = true;
					Report($"OpenAL ready, source {this.uiSource}, COLDIG.BIN {this.aDigital?.Length ?? 0} bytes");
				}
				else
				{
					Report($"OpenAL: no device (0x{device:x}) or context (0x{context:x}), no sound");
				}
			}
			catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
			{
				Report($"No OpenAL, no sound: {ex.Message}");
			}
		}

		/// <summary>To the log and stderr, while the sound support is new</summary>
		private void Report(string text)
		{
			this.oGame.Log.WriteLine($"Sound: {text}");
			Console.Error.WriteLine($"Sound: {text}");
		}

		/// <summary>INT 21h/4B03h for a ?SOUND.COL, at the segment of the block the game has allocated</summary>
		public void Load(string name, ushort segment)
		{
			if (name != "RSOUND.COL" || !LoadRoland(segment))
				LoadStub(segment);
		}

		/// <summary>
		/// RSOUND.COL, when there is an MT-32 to play it on. Its code is generated for RolandSegment,
		/// so the image goes there instead of into the game's block, and that memory is taken from
		/// DOS. The game's block keeps the header fields the game reads, which point it there.
		/// </summary>
		private bool LoadRoland(ushort segment)
		{
			string path = $"{VCPU.DefaultGamePath}RSOUND.COL";

			if (!this.bAudio || !File.Exists(path))
				return false;

			if (this.oMT32 == null)
			{
				string roms = Environment.GetEnvironmentVariable("OPENCOLO_MT32_ROMS") ??
					Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dosbox", "mt32-roms");

				this.oMT32 = MT32.Open(roms, Environment.GetEnvironmentVariable("OPENCOLO_MT32"), out string message);
				Report(message);

				if (this.oMT32 == null)
					return false;

				this.oMT32.Start();
			}

			VCPUMemory memory = this.oCPU.Memory;

			memory.ReserveTop(RolandSegment);
			OpenColoGame.LoadImage(memory, path, RolandSegment);

			// the entry segment, the image end (the block shrinks to the header) and the five entry offsets
			memory.WriteUInt16(segment, 0x28, RolandSegment);
			memory.WriteUInt16(segment, 0x2a, segment);
			memory.WriteUInt16(segment, 0x2c, EntryOffset);

			for (int i = 0; i < 5; i++)
				memory.WriteUInt16(segment, (ushort)(0x32 + i * 2), memory.ReadUInt16(RolandSegment, (ushort)(0x32 + i * 2)));

			this.oGame.CreateRSound();

			MPU401 mpu = new(MPUPort, this.oMT32);
			this.oCPU.PortInputHandler = mpu.Read;
			this.oCPU.PortOutputHandler = mpu.Write;

			WrapRolandEntries();

			Report($"RSOUND.COL at segment 0x{RolandSegment:x4}, {this.oMT32.Description} at port 0x{MPUPort:x3}");

			return true;
		}

		/// <summary>
		/// The driver plays the digitized effects through a Sound Blaster with the samples in EMS. The
		/// host plays them instead: init runs as if CONFIG.COL had no digital device, so the driver
		/// leaves both alone, and the request entry plays the effect's sample.
		/// </summary>
		private void WrapRolandEntries()
		{
			Dictionary<uint, Action> native = this.oGame.Dispatch.Native;
			uint init = VCPU.ToLinearAddress(RolandSegment, 0x0969);
			uint request = VCPU.ToLinearAddress(RolandSegment, 0x09c6);
			uint stop = VCPU.ToLinearAddress(RolandSegment, 0x0a3d);
			Action driverInit = native[init];
			Action driverRequest = native[request];
			Action driverStop = native[stop];

			native[init] = () =>
			{
				// the fifth argument, CONFIG.COL's DS:2612, is the digital device: 11h-2Fh start it (0969)
				ushort stack = this.oCPU.SS.UInt16;
				ushort argument = (ushort)(this.oCPU.SP.UInt16 + 12);
				ushort device = this.oCPU.Memory.ReadUInt16(stack, argument);

				this.bRolandDigital = device >= 0x11 && device <= 0x2f;
				this.oCPU.Memory.WriteUInt16(stack, argument, 0);

				driverInit();

				this.oCPU.Memory.WriteUInt16(stack, argument, device);
				Report($"init: AX=0x{this.oCPU.AX.UInt16:x4}, digitized effects {(this.bRolandDigital ? "on" : "off")}");
			};

			native[request] = () =>
			{
				int n = this.Argument;

				driverRequest();

				if (n != 8)
					Report($"request 0x{n:x2}");

				if (this.bRolandDigital && n >= 0x40 && n <= 0x5d && aRolandEffects[n - 0x40] >= 0)
					PlaySample(aRolandEffects[n - 0x40]);
			};

			native[stop] = () =>
			{
				driverStop();
				StopSamples();
			};
		}

		/// <summary>
		/// The stand-in: writes the image header fields the game reads into the block at the load
		/// segment and points the five entries at host code
		/// </summary>
		private void LoadStub(ushort segment)
		{
			VCPUMemory memory = this.oCPU.Memory;

			// F0_2047_000a sizes the block from 2Ah (segment) and 2Ch (offset) of the image end,
			// F0_2047_00b8 takes the entry segment from 28h and five offsets from 32h
			memory.WriteUInt16(segment, 0x28, segment);
			memory.WriteUInt16(segment, 0x2a, segment);
			memory.WriteUInt16(segment, 0x2c, (ushort)(EntryOffset + 5));

			Action[] entries = [Init, Request, Stop, Status, Tick];

			for (int i = 0; i < entries.Length; i++)
			{
				ushort offset = (ushort)(EntryOffset + i);
				Action entry = entries[i];

				memory.WriteUInt16(segment, (ushort)(0x32 + i * 2), offset);

				// the game reaches the entries with JMP FAR from segment 1059, after a far call
				this.oGame.Dispatch.Native[VCPU.ToLinearAddress(segment, offset)] = () =>
				{
					entry();
					this.oCPU.SP.UInt16 = (ushort)(this.oCPU.SP.UInt16 + 4);
				};
			}

			Report($"Sound driver stub at segment 0x{segment:x4}");
		}

		/// <summary>The first word after the far return address</summary>
		private ushort Argument
		{
			get => this.oCPU.Memory.ReadUInt16(this.oCPU.SS.UInt16, (ushort)(this.oCPU.SP.UInt16 + 4));
		}

		/// <summary>1059:0006, init with the CONFIG.COL values; 0 is success</summary>
		private void Init()
		{
			this.oCPU.AX.UInt16 = 0;
		}

		/// <summary>1059:000a(n): 0-8 commands, 20h-3Fh music, 40h-5Dh effects</summary>
		private void Request()
		{
			int request = this.Argument;
			int sample = request >= 0x40 && request <= 0x5d ? aEffects[request - 0x40] : -1;

			this.oCPU.AX.UInt16 = 0;

			switch (request)
			{
				case 0:
				case 1:
					// 0 silences all channels, 1 stops the music
					this.bMusic = false;
					break;

				case 8:
					// Is music playing: the driver ORs its channels' active flags. The game's music
					// manager (F0_129f_00f6) starts the next piece as soon as this is 0, so while
					// there is no music it plays forever once started.
					this.oCPU.AX.UInt16 = (ushort)(this.bMusic ? 1 : 0);
					return;

				default:
					if (request >= 0x20 && request <= 0x3f)
						this.bMusic = true;
					break;
			}

			Report($"request 0x{request:x2}" + (sample >= 0 ? $", sample {sample}" : ""));

			if (sample >= 0)
				PlaySample(sample);
		}

		/// <summary>1059:005f, before unloading and exec</summary>
		private void Stop()
		{
			StopSamples();

			this.bMusic = false;
			this.oCPU.AX.UInt16 = 0;
		}

		private void StopSamples()
		{
			if (this.bAudio)
			{
				OpenAL.alSourceStop(this.uiSource);
				OpenAL.alSourcei(this.uiSource, OpenAL.AL_BUFFER, 0);
			}
		}

		/// <summary>1059:0063, called by the timer; 0 leaves the music flag DS:92F4 as it is</summary>
		private void Status()
		{
			this.oCPU.AX.UInt16 = 0;
		}

		/// <summary>1059:0067, the music tick</summary>
		private void Tick()
		{
		}

		/// <summary>The driver queues digitized samples and plays them one after another</summary>
		private void PlaySample(int sample)
		{
			if (!this.bAudio || this.aDigital == null)
				return;

			(int offset, int length) = aSamples[sample];

			if (offset + length > this.aDigital.Length)
				return;

			if (this.aBuffers[sample] == 0)
			{
				OpenAL.alGenBuffers(1, out this.aBuffers[sample]);
				OpenAL.alBufferData(this.aBuffers[sample], OpenAL.AL_FORMAT_MONO8, this.aDigital[offset..(offset + length)],
					length, sample < 5 ? 19050 : 11025);
			}

			// drop what has been played, then queue the new one
			OpenAL.alGetSourcei(this.uiSource, OpenAL.AL_BUFFERS_PROCESSED, out int processed);

			if (processed > 0)
				OpenAL.alSourceUnqueueBuffers(this.uiSource, processed, new uint[processed]);

			OpenAL.alSourceQueueBuffers(this.uiSource, 1, ref this.aBuffers[sample]);
			OpenAL.alGetSourcei(this.uiSource, OpenAL.AL_SOURCE_STATE, out int state);

			if (state != OpenAL.AL_PLAYING)
				OpenAL.alSourcePlay(this.uiSource);

			int error = OpenAL.alGetError();

			if (error != 0)
				Report($"OpenAL error 0x{error:x4} playing sample {sample}");
		}
	}
}
