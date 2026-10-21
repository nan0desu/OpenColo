using System;
using System.Collections.Generic;
using System.IO;
using IRB.VirtualCPU;

namespace OpenColo
{
	public class LogWrapper
	{
		private StreamWriter? oLog = null;
		private int iLogTabLevel = 0;
		private VCPU? oCPU = null;
		private Stack<ushort> aStack = new Stack<ushort>();
		// the functions being executed, kept with the log off too, for the dump
		private List<string> aCalls = new List<string>();

		public LogWrapper(string path, bool enableLog)
		{
			if (enableLog)
				this.oLog = new StreamWriter(path);
		}

		public VCPU? CPU
		{
			get { return this.oCPU; }
			set { this.oCPU = value; }
		}

		public void EnterBlock(string text)
		{
			EnterBlock(text, false);
		}

		/// <summary>The functions being executed, outermost first</summary>
		public string CallStack
		{
			get { return string.Join(Environment.NewLine, this.aCalls); }
		}

		/// <summary>
		/// Debug checks run on entry to a function, by the text the generated code passes in
		/// ("'F23_0000_0486'(Cdecl, Near, Return) at 0x486"), before its first instruction
		/// </summary>
		public Dictionary<string, Action> EntryChecks { get; } = new();

		public void EnterBlock(string text, bool showRegisters)
		{
			this.aCalls.Add(text);

			if (this.EntryChecks.Count > 0 && this.EntryChecks.TryGetValue(text, out Action? check))
				check();

			if (this.oLog != null)
			{
				if (this.aStack.Count != this.iLogTabLevel)
					throw new Exception("Unbalanced EnterBlock or ExitBlock");

				if (this.oCPU != null)
					this.aStack.Push(this.oCPU.SP.UInt16);

				WriteTabs(this.iLogTabLevel);
				this.oLog.Write($"{text}");

				if (this.oCPU != null && showRegisters)
					this.oLog.Write($" // Stack: 0x{this.oCPU.SS.UInt16:x4}:0x{this.oCPU.SP.UInt16:x4}, DS 0x{this.oCPU.DS.UInt16:x4}, BP 0x{this.oCPU.BP.UInt16:x4}");

				this.oLog.WriteLine();

				WriteTabs(this.iLogTabLevel);
				this.oLog.WriteLine("{");
				this.oLog.Flush();
				this.iLogTabLevel++;
			}
		}

		public void ExitBlock(string text, int returnValue)
		{
			if (this.oLog != null)
			{
				this.WriteLine($"// Function returns: {returnValue} (0x{returnValue:x})");

				this.ExitBlock(text);
			}
		}

		public void ExitBlock(string text)
		{
			if (this.aCalls.Count > 0)
				this.aCalls.RemoveAt(this.aCalls.Count - 1);

			if (this.oLog != null)
			{
				if (this.aStack.Count != this.iLogTabLevel)
					throw new Exception("Unbalanced EnterBlock or ExitBlock");

				this.iLogTabLevel = Math.Max(0, this.iLogTabLevel - 1);
				WriteTabs(this.iLogTabLevel);
				this.oLog.Write("}");

				//if (this.oCPU != null)
				//	this.oLog.Write($" // Stack: 0x{this.oCPU.SS.Word:x4}:0x{this.oCPU.SP.Word:x4}, DS 0x{this.oCPU.DS.Word:x4}, BP 0x{this.oCPU.BP.Word:x4}");

				ushort usStack = this.aStack.Pop();
				// The function pops its own return address, and with RET n its arguments, so the
				// stack only ends up higher than on entry. _setargv pushes permanent data on the stack!
				if (this.oCPU != null &&
					!text.Equals("_setargv", StringComparison.InvariantCultureIgnoreCase) &&
					!text.Equals("start", StringComparison.InvariantCultureIgnoreCase) &&
					this.oCPU.SP.UInt16 < usStack)
				{
					this.oLog.Write($", Error: Stack leak detected in function '{text}', " +
						$"stack position should be above 0x{usStack:x4}, but is 0x{this.oCPU.SP.UInt16:x4}");
				}

				this.oLog.WriteLine();

				this.oLog.Flush();
			}
		}

		public void WriteLine(string text)
		{
			if (this.oLog != null)
			{
				WriteTabs(this.iLogTabLevel);
				this.oLog.WriteLine(text);
				this.oLog.Flush();
			}
		}

		public void Write(string text)
		{
			if (this.oLog != null)
			{
				this.oLog.Write(text);
				this.oLog.Flush();
			}
		}

		private void WriteTabs(int level)
		{
			if (this.oLog != null)
				this.oLog.Write($"{new string('\t', level)}");
		}
	}
}
