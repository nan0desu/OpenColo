using System;

namespace IRB.VirtualCPU
{
	public class VCPUMemory
	{
		private static uint uiMinFreeAddress = 0x0;
		private uint uiMaxFreeAddress = 0x9ffff;
		private static uint uiMemorySize = 0xa0000;

		private byte[] aMemory = new byte[uiMemorySize];
		private List<VCPUMemoryRegion> aMemoryRegions = new List<VCPUMemoryRegion>();

		private VCPU oCPU;

		public VCPUMemory(VCPU cpu)
		{
			this.oCPU = cpu;
			this.aMemoryRegions.Add(new VCPUMemoryRegion(0, uiMaxFreeAddress, VCPUMemoryFlagsEnum.ReadWrite | 
				VCPUMemoryFlagsEnum.ReadWarning | VCPUMemoryFlagsEnum.WriteWarning));
		}

		public VCPU Parent
		{
			get { return this.oCPU; }
		}

		public byte[] MemoryContent
		{
			get { return this.aMemory; }
		}

		public VCPUMemoryRegion FreeMemory
		{
			get { return this.aMemoryRegions[0]; }
		}

		public List<VCPUMemoryRegion> MemoryRegions
		{
			get
			{
				return this.aMemoryRegions;
			}
		}

		#region Check Flag
		public bool CheckFlag(uint address, uint size, VCPUMemoryFlagsEnum flag)
		{
			uint uiCount = 0;

			for (int i = 0; i < size; i++)
			{
				bool bFound = false;
				for (int j = 0; j < this.aMemoryRegions.Count; j++)
				{
					if (this.aMemoryRegions[j].CheckBounds(address, 1))
					{
						bFound = true;
						if ((this.aMemoryRegions[j].AccessFlags & flag) == flag)
						{
							uiCount++;
							break;
						}
					}
				}
				// if address is not found in any region, assume full rights
				if (!bFound)
					uiCount++;
			}
			if (uiCount == size)
				return true;

			return false;
		}
		#endregion

		#region Read instructions
		#region 8 Bit
		public sbyte ReadInt8(ushort segment, ushort offset)
		{
			return (sbyte)this.ReadUInt8(VCPU.ToLinearAddress(segment, offset));
		}

		public sbyte ReadInt8(uint address)
		{
			return (sbyte)this.ReadUInt8(address);
		}

		public byte ReadUInt8(ushort segment, ushort offset)
		{
			return this.ReadUInt8(VCPU.ToLinearAddress(segment, offset));
		}

		/// <summary>
		/// Memory is flat: conventional memory and the VGA window, no access checks. The
		/// regions only keep track of DOS memory blocks. Anything else reads as 0xff, like
		/// an empty bus, and writes to it are dropped.
		/// </summary>
		public byte ReadUInt8(uint address)
		{
			if (address < uiMemorySize)
			{
				return this.aMemory[address];
			}

			if (this.oCPU.VGA.CheckBounds(address))
			{
				return this.oCPU.VGA.Framebuffer[address - VCPUVGA.FramebufferAddress];
			}

			return 0xff;
		}
		#endregion

		#region 16 bit
		public short ReadInt16(ushort segment, ushort offset)
		{
			return (short)this.ReadUInt16(VCPU.ToLinearAddress(segment, offset));
		}

		public short ReadInt16(uint address)
		{
			return (short)this.ReadUInt16(address);
		}

		public ushort ReadUInt16(ushort segment, ushort offset)
		{
			return this.ReadUInt16(VCPU.ToLinearAddress(segment, offset));
		}

		public ushort ReadUInt16(uint address)
		{
			if (address + 1 < uiMemorySize)
			{
				return (ushort)(this.aMemory[address] | (this.aMemory[address + 1] << 8));
			}

			return (ushort)(this.ReadUInt8(address) | (this.ReadUInt8(address + 1) << 8));
		}
		#endregion

		#region 32 bit
		public int ReadInt32(ushort segment, ushort offset)
		{
			return (int)this.ReadUInt32(VCPU.ToLinearAddress(segment, offset));
		}

		public int ReadInt32(uint address)
		{
			return (int)this.ReadUInt32(address);
		}

		public uint ReadUInt32(ushort segment, ushort offset)
		{
			return this.ReadUInt32(VCPU.ToLinearAddress(segment, offset));
		}

		public uint ReadUInt32(uint address)
		{
			return (uint)(this.ReadUInt16(address) | (this.ReadUInt16(address + 2) << 16));
		}
		#endregion
		#endregion

		#region Write instructions
		#region 8 bit
		public void WriteInt8(ushort segment, ushort offset, sbyte value)
		{
			this.WriteUInt8(VCPU.ToLinearAddress(segment, offset), (byte)value);
		}

		public void WriteInt8(uint address, sbyte value)
		{
			this.WriteUInt8(address, (byte)value);
		}

		public void WriteInt8(ushort segment, ushort offset, int value)
		{
			if (value < SByte.MinValue || value > SByte.MaxValue)
				throw new Exception($"Value {value} out of range for Int8");

			this.WriteUInt8(VCPU.ToLinearAddress(segment, offset), (byte)((sbyte)value));
		}

		public void WriteInt8(uint address, int value)
		{
			if (value < SByte.MinValue || value > SByte.MaxValue)
				throw new Exception($"Value {value} out of range for Int8");

			this.WriteUInt8(address, (byte)((sbyte)value));
		}

		public void WriteUInt8(ushort segment, ushort offset, int value)
		{
			if (value < 0 || value > Byte.MaxValue)
				throw new Exception($"Value {value} out of range for UInt8");

			this.WriteUInt8(VCPU.ToLinearAddress(segment, offset), (byte)value);
		}

		public void WriteUInt8(uint address, int value)
		{
			if (value < 0 || value > Byte.MaxValue)
				throw new Exception($"Value {value} out of range for UInt8");

			this.WriteUInt8(address, (byte)value);
		}

		public void WriteUInt8(ushort segment, ushort offset, byte value)
		{
			this.WriteUInt8(VCPU.ToLinearAddress(segment, offset), value);
		}

		public void WriteUInt8(uint address, byte value)
		{
			if (address < uiMemorySize)
			{
				this.aMemory[address] = value;
			}
			else if (this.oCPU.VGA.CheckBounds(address))
			{
				this.oCPU.VGA.Framebuffer[address - VCPUVGA.FramebufferAddress] = value;
			}
		}
		#endregion

		#region 16 bit
		public void WriteInt16(ushort segment, ushort offset, short value)
		{
			this.WriteUInt16(VCPU.ToLinearAddress(segment, offset), (ushort)value);
		}

		public void WriteInt16(uint address, short value)
		{
			this.WriteUInt16(address, (ushort)value);
		}

		public void WriteInt16(ushort segment, ushort offset, int value)
		{
			if (value < Int16.MinValue || value > Int16.MaxValue)
				throw new Exception($"Value {value} out of range for Int16");

			this.WriteUInt16(VCPU.ToLinearAddress(segment, offset), (ushort)((short)value));
		}

		public void WriteInt16(uint address, int value)
		{
			if (value < Int16.MinValue || value > Int16.MaxValue)
				throw new Exception($"Value {value} out of range for Int16");

			this.WriteUInt16(address, (ushort)((short)value));
		}

		public void WriteUInt16(ushort segment, ushort offset, int value)
		{
			if (value < 0 || value > UInt16.MaxValue)
				throw new Exception($"Value {value} out of range for UInt16");

			this.WriteUInt16(VCPU.ToLinearAddress(segment, offset), (ushort)value);
		}

		public void WriteUInt16(uint address, int value)
		{
			if (value < 0 || value > UInt16.MaxValue)
				throw new Exception($"Value {value} out of range for UInt16");

			this.WriteUInt16(address, (ushort)value);
		}

		public void WriteUInt16(ushort segment, ushort offset, ushort value)
		{
			this.WriteUInt16(VCPU.ToLinearAddress(segment, offset), value);
		}

		public void WriteUInt16(uint address, ushort value)
		{
			if (address + 1 < uiMemorySize)
			{
				this.aMemory[address] = (byte)(value & 0xff);
				this.aMemory[address + 1] = (byte)((value & 0xff00) >> 8);
			}
			else
			{
				this.WriteUInt8(address, (byte)(value & 0xff));
				this.WriteUInt8(address + 1, (byte)((value & 0xff00) >> 8));
			}
		}
		#endregion

		#region 32 bit
		public void WriteInt32(ushort segment, ushort offset, int value)
		{
			this.WriteUInt32(VCPU.ToLinearAddress(segment, offset), (uint)value);
		}

		public void WriteInt32(uint address, int value)
		{
			this.WriteUInt32(address, (uint)value);
		}

		public void WriteUInt32(ushort segment, ushort offset, uint value)
		{
			this.WriteUInt32(VCPU.ToLinearAddress(segment, offset), value);
		}

		public void WriteUInt32(uint address, uint value)
		{
			this.WriteUInt16(address, (ushort)(value & 0xffff));
			this.WriteUInt16(address + 2, (ushort)((value & 0xffff0000) >> 16));
		}
		#endregion
		#endregion

		#region Memory block instructions
		public void WriteBlock(ushort segment, ushort offset, byte[] srcData, int pos, int length)
		{
			WriteBlock(VCPU.ToLinearAddress(segment, offset), srcData, pos, length);
		}

		public void WriteBlock(uint address, byte[] srcData, int pos, int length)
		{
			Array.Copy(srcData, pos, this.aMemory, address, length);
			/*for (int i = 0; i < length; i++)
			{
				WriteUInt8((uint)(address + i), srcData[pos + i]);
			}*/
		}
		#endregion

		#region Memory allocation
		public bool AllocateMemoryBlock(uint address, uint size)
		{
			return AllocateMemoryBlock(address, size, VCPUMemoryFlagsEnum.ReadWrite | VCPUMemoryFlagsEnum.WriteWarning);
		}

		public bool AllocateMemoryBlock(uint address, uint size, VCPUMemoryFlagsEnum flags)
		{
			for (int i = 1; i < this.aMemoryRegions.Count; i++)
			{
				if (this.aMemoryRegions[i].CheckBounds(address, size))
				{
					return false;
				}
			}

			this.aMemoryRegions.Add(new VCPUMemoryRegion(address, size, flags));

			AdjustFreeMemoryRange();

			return true;
		}

		// DOS blocks, by segment: like DOS, each block has its memory control block in the paragraph
		// before it, and programs write there (F0_2047_000a names the sound driver's block). The regions
		// cover the MCB and the block.

		/// <summary>Paragraphs a new block can have at most</summary>
		public ushort LargestFreeBlock
		{
			get => (ushort)Math.Min(0xffff, Math.Max(0, (int)(this.aMemoryRegions[0].Size >> 4) - 1));
		}

		public bool AllocateMemoryBlock(ushort segmentCount, out ushort startSegment)
		{
			uint uiSize = VCPU.ToLinearAddress((ushort)(segmentCount + 1), 0);

			// Just allocate next available block, don't search between blocks for now
			// Is there enough room for allocation?
			if (segmentCount == 0xffff || this.aMemoryRegions[0].Size < uiSize)
			{
				startSegment = this.LargestFreeBlock;
				return false;
			}

			// allocate block, after its MCB: 'M', the owner, the size in paragraphs
			uint uiMCB = this.aMemoryRegions[0].Start;
			startSegment = (ushort)(((uiMCB >> 4) + 1) & 0xffff);
			this.aMemoryRegions.Add(new VCPUMemoryRegion(uiMCB, uiSize, VCPUMemoryFlagsEnum.ReadWrite | VCPUMemoryFlagsEnum.WriteWarning));

			WriteUInt8(uiMCB, 0x4d);
			WriteUInt16(uiMCB + 1, startSegment);
			WriteUInt16(uiMCB + 3, segmentCount);

			AdjustFreeMemoryRange();

			return true;
		}

		public bool ResizeMemoryBlock(ushort blockSegment, ushort segmentCount)
		{
			uint uiMCB = VCPU.ToLinearAddress((ushort)(blockSegment - 1), 0);

			if (!ResizeMemoryBlock(uiMCB, (uint)(segmentCount + 1) << 4))
				return false;

			WriteUInt16(uiMCB + 3, segmentCount);

			return true;
		}

		public bool ResizeMemoryBlock(uint address, uint size)
		{
			for (int i = 1; i < this.aMemoryRegions.Count; i++)
			{
				if (this.aMemoryRegions[i].Start == address)
				{
					// found the block
					// check for overlapping
					for (int j = 1; j < this.aMemoryRegions.Count; j++)
					{
						if (j != i && this.aMemoryRegions[j].CheckOverlap(address, size))
							return false;
					}

					this.aMemoryRegions[i].Size = size;

					AdjustFreeMemoryRange();

					return true;
				}
			}

			return false;
		}

		public bool FreeMemoryBlock(ushort segment)
		{
			uint uiAddress = VCPU.ToLinearAddress((ushort)(segment - 1), 0);

			for (int i = 1; i < this.aMemoryRegions.Count; i++)
			{
				if (this.aMemoryRegions[i].Start == uiAddress)
				{
					// found the block
					this.aMemoryRegions.RemoveAt(i);

					AdjustFreeMemoryRange();

					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Takes the memory from segment up away from DOS, for the host's own use: new blocks
		/// end below it. A block that already reaches into it has to shrink out of it.
		/// </summary>
		public void ReserveTop(ushort segment)
		{
			this.uiMaxFreeAddress = Math.Min(this.uiMaxFreeAddress, VCPU.ToLinearAddress(segment, 0) - 1);

			AdjustFreeMemoryRange();
		}

		private void AdjustFreeMemoryRange()
		{
			// free memory is everything above the highest block
			uint uiStart = uiMinFreeAddress;

			for (int i = 1; i < this.aMemoryRegions.Count; i++)
			{
				uiStart = Math.Max(uiStart, this.aMemoryRegions[i].End + 1);
			}

			VCPUMemoryRegion oFree = this.aMemoryRegions[0];
			oFree.Start = uiStart;
			oFree.Size = uiStart <= uiMaxFreeAddress ? uiMaxFreeAddress + 1 - uiStart : 0;
		}
		#endregion
	}
}
