using IRB.VirtualCPU;
using OpenColo.Audio;

namespace OpenColo
{
	/// <summary>
	/// Stands in for the #SOUND.COL drivers (docs/sound.md). When the game loads one, a stub image
	/// goes into its memory block whose five entry points are host functions in the dispatch table.
	/// So far it plays the digitized effects from COLDIG.BIN; music requests are accepted and ignored.
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

		// where the stub image puts the entry points, after the header fields the loader reads
		private const ushort EntryOffset = 0x40;

		private OpenColoGame oGame;
		private VCPU oCPU;
		private byte[]? aDigital = null;
		private uint[] aBuffers = new uint[aSamples.Length];
		private uint uiSource = 0;
		private bool bAudio = false;
		private bool bMusic = false;

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

		/// <summary>
		/// INT 21h/4B03h for a ?SOUND.COL: writes the image header fields the game reads into the
		/// block at the load segment and points the five entries at host code
		/// </summary>
		public void Load(ushort segment)
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
			if (this.bAudio)
			{
				OpenAL.alSourceStop(this.uiSource);
				OpenAL.alSourcei(this.uiSource, OpenAL.AL_BUFFER, 0);
			}

			this.bMusic = false;
			this.oCPU.AX.UInt16 = 0;
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
