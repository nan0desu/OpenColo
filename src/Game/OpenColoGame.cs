using IRB.VirtualCPU;

namespace OpenColo
{
	/// <summary>
	/// Root of the emulated game. Generated segment classes (Segment_N, Overlay_N) take this
	/// as their parent and reach the CPU and each other through it. The fields for them and
	/// the dispatch table are in the generated OpenColoGameSegments.cs.
	/// </summary>
	public partial class OpenColoGame
	{
		// Where DOS would have put things. The program's PSP is right below the load module,
		// the environment and a stub for the interrupt vectors are in low memory.
		public const ushort LoadSegment = 0x1000;
		private const ushort PSPSegment = LoadSegment - 0x10;
		private const ushort EnvironmentSegment = 0x0070;
		private const ushort InterruptStubSegment = 0x0060;
		// DGROUP at run time
		public const ushort DataSegment = 0x2b5a;

		// the game's INT 8 handler, see docs/timer.md
		private const uint TimerHandler = 0x1a29_0004;

		private LogWrapper oLog;
		private VCPU oCPU;
		private long lPITTicks = 0;

		public OpenColoGame()
		{
			this.oLog = new LogWrapper($"{VCPU.AssemblyPath}Log.txt", Environment.GetEnvironmentVariable("OPENCOLO_LOG") == "1");
			this.oCPU = new VCPU(this, this.oLog);
			this.oLog.CPU = this.oCPU;

			CreateSegments();
		}

		public LogWrapper Log
		{
			get { return this.oLog; }
		}

		public VCPU CPU
		{
			get { return this.oCPU; }
		}

		/// <summary>Runs on the game thread until the program exits.</summary>
		public void Start()
		{
			// Without a sound driver (-c sets the sound letter to N). Not -o: that tells the start menu
			// OPENING has left its picture on the screen, and the menu copies its background from there
			string arguments = Environment.GetEnvironmentVariable("OPENCOLO_ARGS") ?? "-c:N";

			SetUpLowMemory();
			LoadExecutable($"{VCPU.DefaultGamePath}VICEROY.EXE", arguments);

			this.oCPU.TimerEventHandler = this.Timer;
			this.oCPU.MouseEventHandler = this.MouseEvent;
			this.oCPU.EnableTimer = true;

			// main returns without setting AX, so a normal quit exits with whatever is left in it.
			// Dump when exit comes from deeper than Start (the game's error handler, exit(3), or the
			// C runtime giving up), or when main has left an error code in DS:822
			this.oCPU.DumpOnExit = code =>
				this.oLog.CallStack.Split(Environment.NewLine).Length > 2 ||
				this.oCPU.Memory.ReadUInt16(DataSegment, 0x822) != 0;

			// any #SOUND.COL, NSOUND.COL included, loads the host's stand-in
			SoundDriver sound = new(this);
			this.oCPU.LoadOverlayHandler = (name, segment) =>
			{
				if (name.Length != 10 || !name.EndsWith("SOUND.COL"))
					return false;

				sound.Load(segment);
				return true;
			};

			if (Environment.GetEnvironmentVariable("OPENCOLO_CHECKS") == "1")
				AddDebugChecks();

			// Start is the C runtime's entry, the MZ entry point is the overlay manager that
			// the generated code doesn't need (docs/memory.md)
			this.Segment_1d1d.Start();
		}

		#region Debug checks
		/// <summary>
		/// Checks on entry to functions, while chasing a bug: when one fails, Dump.bin/Dump.txt are
		/// written with the callers' stack frames still intact
		/// </summary>
		private void AddDebugChecks()
		{
			// the text width measure copies its string into an 80 byte stack buffer
			this.oLog.EntryChecks["'F23_0000_0486'(Cdecl, Near, Return) at 0x486"] = () =>
			{
				// near call: the return offset, then the far pointer to the text
				ushort offset = this.oCPU.Memory.ReadUInt16(this.oCPU.SS.UInt16, (ushort)(this.oCPU.SP.UInt16 + 2));
				ushort segment = this.oCPU.Memory.ReadUInt16(this.oCPU.SS.UInt16, (ushort)(this.oCPU.SP.UInt16 + 4));
				int length = 0;

				while (length < 0x100 && this.oCPU.Memory.ReadUInt8(segment, (ushort)(offset + length)) != 0)
					length++;

				if (length > 0x4f)
				{
					this.oLog.WriteLine($"F23_0000_0486: text at 0x{segment:x4}:0x{offset:x4} is {length} or more bytes long");
					this.oCPU.WriteDump(null);
				}
			};

			// draws a line of dialog text: AX = x, DX = y, BX bit 0 = highlighted, DS:1F62 = selected
			this.oLog.EntryChecks["'F23_0000_0538'(Cdecl, Near, Return) at 0x538"] = () =>
			{
				string[] calls = this.oLog.CallStack.Split(Environment.NewLine);

				DebugLog($"F23_0000_0538 x={(short)this.oCPU.AX.UInt16} y={(short)this.oCPU.DX.UInt16} " +
					$"bx=0x{this.oCPU.BX.UInt16:x4} 1f62={this.oCPU.Memory.ReadUInt16(DataSegment, 0x1f62)} " +
					$"from {(calls.Length > 1 ? calls[calls.Length - 2] : "?")}");
			};
		}

		/// <summary>One line to Checks.txt next to the executable</summary>
		private void DebugLog(string text)
		{
			File.AppendAllText($"{VCPU.AssemblyPath}Checks.txt", text + Environment.NewLine);
		}
		#endregion

		#region Loading
		/// <summary>The interrupt vector table, the BIOS data area and the environment</summary>
		private void SetUpLowMemory()
		{
			VCPUMemory memory = this.oCPU.Memory;

			// Every vector points to an IRET, nothing calls through them. The game checks that
			// INT 33h isn't one before it uses the mouse, so that one points to a NOP first.
			memory.WriteUInt8(InterruptStubSegment, 0, 0xcf);
			memory.WriteUInt8(InterruptStubSegment, 1, 0x90);
			memory.WriteUInt8(InterruptStubSegment, 2, 0xcf);

			for (int i = 0; i < 256; i++)
			{
				memory.WriteUInt16(0, (ushort)(i * 4), (ushort)(i == 0x33 ? 1 : 0));
				memory.WriteUInt16(0, (ushort)(i * 4 + 2), InterruptStubSegment);
			}

			// The environment: variables, then a count of 1 and the program's path. COLONIZE=MULTI
			// unlocks the hot-seat game (docs/startup.md), pass it through if the host has it.
			List<string> variables = ["COMSPEC=C:\\COMMAND.COM", "PATH=C:\\"];
			string? colonize = Environment.GetEnvironmentVariable("COLONIZE");

			if (!string.IsNullOrEmpty(colonize))
				variables.Add($"COLONIZE={colonize}");

			ushort offset = 0;

			foreach (string variable in variables)
				offset = WriteString(EnvironmentSegment, offset, variable);

			memory.WriteUInt8(EnvironmentSegment, offset++, 0);
			memory.WriteUInt16(EnvironmentSegment, offset, 1);
			WriteString(EnvironmentSegment, (ushort)(offset + 2), "C:\\COLONIZE\\VICEROY.EXE");
		}

		/// <summary>Writes a zero terminated string, returns the offset after it</summary>
		private ushort WriteString(ushort segment, ushort offset, string text)
		{
			foreach (char ch in text)
				this.oCPU.Memory.WriteUInt8(segment, offset++, (byte)ch);

			this.oCPU.Memory.WriteUInt8(segment, offset++, 0);

			return offset;
		}

		/// <summary>
		/// Loads the MZ load module at LoadSegment and relocates it, builds the PSP and sets the
		/// registers the way DOS leaves them. The overlays after the load module aren't loaded,
		/// their code is in the generated classes.
		/// </summary>
		private void LoadExecutable(string path, string arguments)
		{
			byte[] file = File.ReadAllBytes(path);
			VCPUMemory memory = this.oCPU.Memory;

			if (BitConverter.ToUInt16(file, 0) != 0x5a4d)
				throw new Exception($"'{path}' is not an MS-DOS executable");

			int lastPageBytes = BitConverter.ToUInt16(file, 2);
			int pages = BitConverter.ToUInt16(file, 4);
			int relocationCount = BitConverter.ToUInt16(file, 6);
			int headerSize = BitConverter.ToUInt16(file, 8) * 16;
			int relocationTable = BitConverter.ToUInt16(file, 0x18);
			int imageSize = (lastPageBytes == 0 ? pages * 512 : (pages - 1) * 512 + lastPageBytes) - headerSize;

			memory.WriteBlock(LoadSegment, 0, file, headerSize, imageSize);

			for (int i = 0; i < relocationCount; i++)
			{
				ushort offset = BitConverter.ToUInt16(file, relocationTable + i * 4);
				ushort segment = (ushort)(LoadSegment + BitConverter.ToUInt16(file, relocationTable + i * 4 + 2));

				memory.WriteUInt16(segment, offset, (ushort)(memory.ReadUInt16(segment, offset) + LoadSegment));
			}

			// The program owns all memory from its PSP up, the C runtime shrinks the block
			uint pspAddress = VCPU.ToLinearAddress(PSPSegment, 0);
			memory.AllocateMemoryBlock(pspAddress - 16, VCPUVGA.FramebufferAddress - (pspAddress - 16));

			// its MCB, the last one: 'Z', owned by the PSP, the size in paragraphs
			memory.WriteUInt8(PSPSegment - 1, 0, 0x5a);
			memory.WriteUInt16(PSPSegment - 1, 1, PSPSegment);
			memory.WriteUInt16(PSPSegment - 1, 3, (ushort)((VCPUVGA.FramebufferAddress >> 4) - PSPSegment));

			// PSP: INT 20h, the top of memory, the environment, two empty FCBs and the command tail
			memory.WriteUInt16(PSPSegment, 0, 0x20cd);
			memory.WriteUInt16(PSPSegment, 2, (ushort)(VCPUVGA.FramebufferAddress >> 4));
			memory.WriteUInt16(PSPSegment, 0x2c, EnvironmentSegment);

			for (int i = 0; i < 12; i++)
			{
				memory.WriteUInt8(PSPSegment, (ushort)(0x5c + i), (byte)(i == 0 ? 0 : ' '));
				memory.WriteUInt8(PSPSegment, (ushort)(0x6c + i), (byte)(i == 0 ? 0 : ' '));
			}

			string tail = arguments.Length > 0 ? $" {arguments}" : "";

			if (tail.Length > 126)
				throw new Exception("The command line is longer than 126 characters");

			memory.WriteUInt8(PSPSegment, 0x80, (byte)tail.Length);

			for (int i = 0; i < tail.Length; i++)
				memory.WriteUInt8(PSPSegment, (ushort)(0x81 + i), (byte)tail[i]);

			memory.WriteUInt8(PSPSegment, (ushort)(0x81 + tail.Length), 0x0d);

			this.oCPU.DS.UInt16 = PSPSegment;
			this.oCPU.ES.UInt16 = PSPSegment;
			this.oCPU.SS.UInt16 = (ushort)(LoadSegment + BitConverter.ToUInt16(file, 0xe));
			this.oCPU.SP.UInt16 = BitConverter.ToUInt16(file, 0x10);
			this.oCPU.AX.UInt16 = 0;
			this.oCPU.BP.UInt16 = 0;
			this.oCPU.Flags.D = false;

			this.oLog.WriteLine($"Loaded '{path}', {imageSize} bytes, {relocationCount} relocations, command line '{arguments}'");
		}
		#endregion

		#region Interrupts
		/// <summary>The registers an interrupt has to give back, kept on the host side</summary>
		private readonly struct Registers
		{
			private readonly ushort ax, bx, cx, dx, si, di, bp, sp, ds, es, ss, flags;

			public Registers(VCPU cpu)
			{
				this.ax = cpu.AX.UInt16; this.bx = cpu.BX.UInt16; this.cx = cpu.CX.UInt16; this.dx = cpu.DX.UInt16;
				this.si = cpu.SI.UInt16; this.di = cpu.DI.UInt16; this.bp = cpu.BP.UInt16; this.sp = cpu.SP.UInt16;
				this.ds = cpu.DS.UInt16; this.es = cpu.ES.UInt16; this.ss = cpu.SS.UInt16; this.flags = cpu.Flags.Value;
			}

			public void Restore(VCPU cpu)
			{
				cpu.AX.UInt16 = this.ax; cpu.BX.UInt16 = this.bx; cpu.CX.UInt16 = this.cx; cpu.DX.UInt16 = this.dx;
				cpu.SI.UInt16 = this.si; cpu.DI.UInt16 = this.di; cpu.BP.UInt16 = this.bp; cpu.SP.UInt16 = this.sp;
				cpu.DS.UInt16 = this.ds; cpu.ES.UInt16 = this.es; cpu.SS.UInt16 = this.ss; cpu.Flags.Value = this.flags;
			}
		}

		/// <summary>Calls a far function of the game the way an interrupt handler would</summary>
		private void CallFar(uint address)
		{
			// the return address, the generated RETF pops it
			this.oCPU.PUSH_UInt16(0);
			this.oCPU.PUSH_UInt16(0);
			this.Dispatch.CallFar(address);
		}

		/// <summary>
		/// Stands in for IRQ 0, from VCPU.DoEvents on the game thread. Delivers the timer
		/// interrupts that are due at the rate the PIT is programmed to: to the BIOS tick count,
		/// or once the game has installed its handler, to a C# copy of it (docs/timer.md).
		/// </summary>
		private void Timer()
		{
			long now = VCPU.PITTicks;
			int divisor = this.oCPU.PITDivisor;
			long due = (now - this.lPITTicks) / divisor;

			// after a stall (a debugger, a slow host), catch up at most one second
			if (due > VCPU.PITFrequency / divisor)
			{
				this.lPITTicks = now - (long)VCPU.PITFrequency;
				due = (long)(VCPU.PITFrequency / divisor);
			}

			this.lPITTicks += due * divisor;

			VCPUMemory memory = this.oCPU.Memory;
			uint vector = ((uint)memory.ReadUInt16(0, 0x22) << 16) | memory.ReadUInt16(0, 0x20);

			for (long i = 0; i < due; i++)
			{
				if (vector == TimerHandler)
					GameTimerInterrupt();
				else
					BIOSTimerInterrupt();
			}
		}

		/// <summary>
		/// INT 8 of the BIOS: the tick count at 0040:006C with the midnight flag, and the diskette
		/// motor timeout at 0040:0040, counted down to 0. The C runtime's screen dissolve
		/// (F0_1d1d_0016) sets that byte to 1 and waits for the change to sync with the timer.
		/// </summary>
		private void BIOSTimerInterrupt()
		{
			VCPUMemory memory = this.oCPU.Memory;
			byte motorTimeout = memory.ReadUInt8(0x40, 0x40);

			if (motorTimeout != 0)
			{
				memory.WriteUInt8(0x40, 0x40, (byte)(motorTimeout - 1));

				// motors off
				if (motorTimeout == 1)
					memory.WriteUInt8(0x40, 0x3f, (byte)(memory.ReadUInt8(0x40, 0x3f) & 0xf0));
			}

			uint ticks = memory.ReadUInt32(0x40, 0x6c) + 1;

			if (ticks >= 0x1800b0)
			{
				ticks = 0;
				memory.WriteUInt8(0x40, 0x70, 1);
			}

			memory.WriteUInt32(0x40, 0x6c, ticks);
		}

		/// <summary>
		/// The game's INT 8 handler at 1a29:0004, step by step with the same variables. Sound
		/// driver calls are left out, there is no driver.
		/// </summary>
		private void GameTimerInterrupt()
		{
			VCPUMemory memory = this.oCPU.Memory;

			// 608.8 Hz, every other tick does the rest
			uint fastTicks = memory.ReadUInt32(DataSegment, 0x8338) + 1;
			memory.WriteUInt32(DataSegment, 0x8338, fastTicks);

			if ((fastTicks & 1) != 0)
				return;

			byte divider = (byte)(memory.ReadUInt8(DataSegment, 0x376) - 1);
			memory.WriteUInt8(DataSegment, 0x376, divider);

			if (divider != 0)
			{
				// a BIOS tick postponed while the callback ran
				if (memory.ReadUInt16(DataSegment, 0x92ec) != 0)
				{
					memory.WriteUInt16(DataSegment, 0x92ec, 0);
					ChainToBIOS();
				}

				return;
			}

			// 60.88 Hz: the game tick
			memory.WriteUInt8(DataSegment, 0x376, 5);
			memory.WriteUInt32(DataSegment, 0x92e8, memory.ReadUInt32(DataSegment, 0x92e8) + 1);

			if (memory.ReadUInt16(DataSegment, 0x92f0) == 0 || memory.ReadUInt16(DataSegment, 0x92f2) != 0)
			{
				// no callback, or it is still running: count the miss
				if (memory.ReadUInt16(DataSegment, 0x92f0) != 0)
					memory.WriteUInt16(DataSegment, 0x92ee, (ushort)(memory.ReadUInt16(DataSegment, 0x92ee) + 1));

				byte biosDivider = (byte)(memory.ReadUInt8(DataSegment, 0x377) - 1);
				memory.WriteUInt8(DataSegment, 0x377, biosDivider);

				if (biosDivider == 0)
					ChainToBIOS();

				return;
			}

			byte divider3 = (byte)(memory.ReadUInt8(DataSegment, 0x377) - 1);
			memory.WriteUInt8(DataSegment, 0x377, divider3);

			if (divider3 == 0)
				memory.WriteUInt16(DataSegment, 0x92ec, 0xffff);

			// the callback (water color cycling) runs on the handler's second stack
			Registers registers = new(this.oCPU);

			memory.WriteUInt16(DataSegment, 0x92f2, 1);
			this.oCPU.DS.UInt16 = DataSegment;
			this.oCPU.SS.UInt16 = DataSegment;
			this.oCPU.SP.UInt16 = 0x384 + 0xfe;
			this.oCPU.Flags.D = false;
			CallFar(memory.ReadUInt32(DataSegment, 0x92e4));
			memory.WriteUInt16(DataSegment, 0x92f2, 0);

			registers.Restore(this.oCPU);
		}

		/// <summary>Every third game tick goes to the BIOS, which also has NumLock turned off</summary>
		private void ChainToBIOS()
		{
			this.oCPU.Memory.WriteUInt8(DataSegment, 0x377, 3);
			this.oCPU.Memory.WriteUInt8(0x40, 0x17, (byte)(this.oCPU.Memory.ReadUInt8(0x40, 0x17) & 0xdf));
			BIOSTimerInterrupt();
		}

		/// <summary>
		/// The INT 33h event handler call, from VCPU.DoEvents with AX (event mask), BX, CX and DX
		/// already set and the other registers saved.
		/// </summary>
		private void MouseEvent()
		{
			Registers registers = new(this.oCPU);

			CallFar(this.oCPU.MouseHandler);

			registers.Restore(this.oCPU);
		}
		#endregion
	}
}
