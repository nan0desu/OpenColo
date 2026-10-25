namespace OpenColo.Audio
{
	/// <summary>
	/// A Roland MPU-401 as far as a UART mode driver uses it: commands on the status port are
	/// acknowledged with FEh on the data port, everything written to the data port is MIDI for the
	/// synth. It is always ready to take a byte.
	/// </summary>
	internal class MPU401
	{
		private const byte Acknowledge = 0xfe;

		private readonly ushort usDataPort;
		private readonly ushort usStatusPort;
		private readonly MT32 oSynth;
		private readonly Queue<byte> aInput = new();

		public MPU401(ushort port, MT32 synth)
		{
			this.usDataPort = port;
			this.usStatusPort = (ushort)(port + 1);
			this.oSynth = synth;
		}

		/// <summary>IN from one of its ports, null for other ports</summary>
		public byte? Read(ushort port)
		{
			if (port == this.usDataPort)
				return this.aInput.Count > 0 ? this.aInput.Dequeue() : (byte)0xff;

			// bit 6 clear: ready for a byte, bit 7 clear: there is a byte to read
			if (port == this.usStatusPort)
				return (byte)(this.aInput.Count > 0 ? 0x00 : 0x80);

			return null;
		}

		/// <summary>OUT to one of its ports; false for other ports</summary>
		public bool Write(ushort port, byte value)
		{
			if (port == this.usDataPort)
			{
				this.oSynth.Write(value);
				return true;
			}

			if (port == this.usStatusPort)
			{
				// FFh reset, 3Fh UART mode: the only commands a UART driver sends, each acknowledged
				if (value == 0xff)
					this.aInput.Clear();

				this.aInput.Enqueue(Acknowledge);
				return true;
			}

			return false;
		}
	}
}
