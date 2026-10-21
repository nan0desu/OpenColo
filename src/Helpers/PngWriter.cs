using System.IO.Compression;
using System.Text;

namespace OpenColo
{
	/// <summary>Writes an 8-bit RGB PNG, enough for screenshots of the emulated screen</summary>
	public static class PngWriter
	{
		private static readonly uint[] aCrcTable = MakeCrcTable();

		/// <summary>Writes width x height pixels, 3 bytes (R, G, B) each, row by row</summary>
		public static void Write(string path, int width, int height, byte[] rgb)
		{
			using FileStream file = new(path, FileMode.Create, FileAccess.Write);

			file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0d, 0x0a, 0x1a, 0x0a]);

			byte[] header = new byte[13];
			WriteUInt32(header, 0, (uint)width);
			WriteUInt32(header, 4, (uint)height);
			header[8] = 8;  // bits per sample
			header[9] = 2;  // RGB
			WriteChunk(file, "IHDR", header);

			// each row starts with filter type 0 (none)
			using MemoryStream compressed = new();
			using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, true))
			{
				for (int y = 0; y < height; y++)
				{
					zlib.WriteByte(0);
					zlib.Write(rgb, y * width * 3, width * 3);
				}
			}

			WriteChunk(file, "IDAT", compressed.ToArray());
			WriteChunk(file, "IEND", []);
		}

		private static void WriteChunk(Stream stream, string type, byte[] data)
		{
			byte[] buffer = new byte[4];
			byte[] typeBytes = Encoding.ASCII.GetBytes(type);

			WriteUInt32(buffer, 0, (uint)data.Length);
			stream.Write(buffer);
			stream.Write(typeBytes);
			stream.Write(data);

			uint crc = 0xffffffff;
			foreach (byte b in typeBytes)
				crc = aCrcTable[(crc ^ b) & 0xff] ^ (crc >> 8);
			foreach (byte b in data)
				crc = aCrcTable[(crc ^ b) & 0xff] ^ (crc >> 8);

			WriteUInt32(buffer, 0, crc ^ 0xffffffff);
			stream.Write(buffer);
		}

		private static void WriteUInt32(byte[] buffer, int offset, uint value)
		{
			buffer[offset] = (byte)(value >> 24);
			buffer[offset + 1] = (byte)(value >> 16);
			buffer[offset + 2] = (byte)(value >> 8);
			buffer[offset + 3] = (byte)value;
		}

		private static uint[] MakeCrcTable()
		{
			uint[] table = new uint[256];

			for (uint n = 0; n < 256; n++)
			{
				uint c = n;

				for (int k = 0; k < 8; k++)
					c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1;

				table[n] = c;
			}

			return table;
		}
	}
}
