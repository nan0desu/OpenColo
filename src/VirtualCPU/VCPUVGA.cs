using System;

namespace IRB.VirtualCPU
{
	/// <summary>
	/// Minimal VGA for mode 13h: the 64K window at A000:0000 and the DAC palette.
	/// The UI thread reads Framebuffer and Palette under VCPU.GraphicsLock.
	/// </summary>
	public class VCPUVGA
	{
		public const uint FramebufferAddress = 0xa0000;
		public const int FramebufferSize = 0x10000;
		public const int Width = 320;
		public const int Height = 200;

		private byte[] aFramebuffer = new byte[FramebufferSize];
		// 256 entries of 6-bit R, G, B
		private byte[] aPalette = new byte[256 * 3];

		private int iDACWriteIndex = 0;
		private int iDACReadIndex = 0;
		private byte bDACState = 0;

		// 70 Hz frames, about half a millisecond of vertical retrace in each
		private static readonly long FrameTicks = System.Diagnostics.Stopwatch.Frequency / 70;
		private static readonly long RetraceTicks = System.Diagnostics.Stopwatch.Frequency / 2000;

		public byte[] Framebuffer
		{
			get { return this.aFramebuffer; }
		}

		public byte[] Palette
		{
			get { return this.aPalette; }
		}

		public bool CheckBounds(uint address)
		{
			return address >= FramebufferAddress && address < FramebufferAddress + FramebufferSize;
		}

		#region DAC
		public byte DACWriteAddress
		{
			get { return (byte)(this.iDACWriteIndex / 3); }
			set { this.iDACWriteIndex = value * 3; this.bDACState = 0; }
		}

		public byte DACReadAddress
		{
			set { this.iDACReadIndex = value * 3; this.bDACState = 3; }
		}

		public byte DACState
		{
			get { return this.bDACState; }
		}

		public void WriteDACData(byte value)
		{
			lock (VCPU.GraphicsLock)
			{
				this.aPalette[this.iDACWriteIndex] = (byte)(value & 0x3f);
			}

			this.iDACWriteIndex = (this.iDACWriteIndex + 1) % this.aPalette.Length;
		}

		public byte ReadDACData()
		{
			byte value = this.aPalette[this.iDACReadIndex];

			this.iDACReadIndex = (this.iDACReadIndex + 1) % this.aPalette.Length;

			return value;
		}
		#endregion

		/// <summary>
		/// Input Status #1. Bit 3 is vertical retrace, bit 0 display disabled. They follow real
		/// time at 70 frames per second: the game measures the frame length against the PIT
		/// (F30_0000_006a) and paces its palette uploads by it.
		/// </summary>
		public byte ReadInputStatus()
		{
			long ticks = VCPU.Clock.ElapsedTicks % FrameTicks;

			return (byte)(ticks >= FrameTicks - RetraceTicks ? 0x09 : 0x00);
		}
	}
}
