using System.Runtime.InteropServices;

namespace OpenColo.Audio
{
	/// <summary>
	/// The few OpenAL calls the sound needs. The library is the system's OpenAL (OpenAL Soft
	/// on Linux, OpenAL32.dll or soft_oal.dll on Windows); without one there is no sound.
	/// </summary>
	internal static class OpenAL
	{
		private const string Library = "openal";

		public const int AL_BUFFER = 0x1009;
		public const int AL_SOURCE_STATE = 0x1010;
		public const int AL_PLAYING = 0x1012;
		public const int AL_BUFFERS_PROCESSED = 0x1016;
		public const int AL_FORMAT_MONO8 = 0x1100;
		public const int AL_FORMAT_STEREO16 = 0x1103;

		static OpenAL()
		{
			NativeLibraries.Register();
		}

		[DllImport(Library)] public static extern IntPtr alcOpenDevice(string? name);
		[DllImport(Library)] public static extern IntPtr alcCreateContext(IntPtr device, IntPtr attributes);
		[DllImport(Library)] public static extern bool alcMakeContextCurrent(IntPtr context);
		[DllImport(Library)] public static extern void alGenSources(int count, out uint source);
		[DllImport(Library)] public static extern void alGenBuffers(int count, out uint buffer);
		[DllImport(Library)] public static extern void alBufferData(uint buffer, int format, byte[] data, int size, int frequency);
		[DllImport(Library)] public static extern void alBufferData(uint buffer, int format, short[] data, int size, int frequency);
		[DllImport(Library)] public static extern void alGenBuffers(int count, uint[] buffers);
		[DllImport(Library)] public static extern void alSourceQueueBuffers(uint source, int count, uint[] buffers);
		[DllImport(Library)] public static extern void alSourceQueueBuffers(uint source, int count, ref uint buffer);
		[DllImport(Library)] public static extern void alSourceUnqueueBuffers(uint source, int count, uint[] buffers);
		[DllImport(Library)] public static extern void alGetSourcei(uint source, int parameter, out int value);
		[DllImport(Library)] public static extern void alSourcei(uint source, int parameter, int value);
		[DllImport(Library)] public static extern void alSourcePlay(uint source);
		[DllImport(Library)] public static extern void alSourceStop(uint source);
		[DllImport(Library)] public static extern int alGetError();
	}
}
