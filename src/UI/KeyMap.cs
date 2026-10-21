using Avalonia.Input;

namespace OpenColo.UI
{
	/// <summary>Turns Avalonia key events into BIOS keystroke words, scan code << 8 | ASCII.</summary>
	public static class KeyMap
	{
		// PC/XT scan codes (set 1)
		private static readonly Dictionary<PhysicalKey, byte> aScanCodes = new()
		{
			{ PhysicalKey.Escape, 0x01 },
			{ PhysicalKey.Digit1, 0x02 }, { PhysicalKey.Digit2, 0x03 }, { PhysicalKey.Digit3, 0x04 },
			{ PhysicalKey.Digit4, 0x05 }, { PhysicalKey.Digit5, 0x06 }, { PhysicalKey.Digit6, 0x07 },
			{ PhysicalKey.Digit7, 0x08 }, { PhysicalKey.Digit8, 0x09 }, { PhysicalKey.Digit9, 0x0a },
			{ PhysicalKey.Digit0, 0x0b }, { PhysicalKey.Minus, 0x0c }, { PhysicalKey.Equal, 0x0d },
			{ PhysicalKey.Backspace, 0x0e }, { PhysicalKey.Tab, 0x0f },
			{ PhysicalKey.Q, 0x10 }, { PhysicalKey.W, 0x11 }, { PhysicalKey.E, 0x12 }, { PhysicalKey.R, 0x13 },
			{ PhysicalKey.T, 0x14 }, { PhysicalKey.Y, 0x15 }, { PhysicalKey.U, 0x16 }, { PhysicalKey.I, 0x17 },
			{ PhysicalKey.O, 0x18 }, { PhysicalKey.P, 0x19 }, { PhysicalKey.BracketLeft, 0x1a }, { PhysicalKey.BracketRight, 0x1b },
			{ PhysicalKey.Enter, 0x1c },
			{ PhysicalKey.A, 0x1e }, { PhysicalKey.S, 0x1f }, { PhysicalKey.D, 0x20 }, { PhysicalKey.F, 0x21 },
			{ PhysicalKey.G, 0x22 }, { PhysicalKey.H, 0x23 }, { PhysicalKey.J, 0x24 }, { PhysicalKey.K, 0x25 },
			{ PhysicalKey.L, 0x26 }, { PhysicalKey.Semicolon, 0x27 }, { PhysicalKey.Quote, 0x28 }, { PhysicalKey.Backquote, 0x29 },
			{ PhysicalKey.Backslash, 0x2b },
			{ PhysicalKey.Z, 0x2c }, { PhysicalKey.X, 0x2d }, { PhysicalKey.C, 0x2e }, { PhysicalKey.V, 0x2f },
			{ PhysicalKey.B, 0x30 }, { PhysicalKey.N, 0x31 }, { PhysicalKey.M, 0x32 }, { PhysicalKey.Comma, 0x33 },
			{ PhysicalKey.Period, 0x34 }, { PhysicalKey.Slash, 0x35 }, { PhysicalKey.NumPadMultiply, 0x37 },
			{ PhysicalKey.Space, 0x39 },
			{ PhysicalKey.F1, 0x3b }, { PhysicalKey.F2, 0x3c }, { PhysicalKey.F3, 0x3d }, { PhysicalKey.F4, 0x3e },
			{ PhysicalKey.F5, 0x3f }, { PhysicalKey.F6, 0x40 }, { PhysicalKey.F7, 0x41 }, { PhysicalKey.F8, 0x42 },
			{ PhysicalKey.F9, 0x43 }, { PhysicalKey.F10, 0x44 },
			{ PhysicalKey.NumPad7, 0x47 }, { PhysicalKey.NumPad8, 0x48 }, { PhysicalKey.NumPad9, 0x49 }, { PhysicalKey.NumPadSubtract, 0x4a },
			{ PhysicalKey.NumPad4, 0x4b }, { PhysicalKey.NumPad5, 0x4c }, { PhysicalKey.NumPad6, 0x4d }, { PhysicalKey.NumPadAdd, 0x4e },
			{ PhysicalKey.NumPad1, 0x4f }, { PhysicalKey.NumPad2, 0x50 }, { PhysicalKey.NumPad3, 0x51 },
			{ PhysicalKey.NumPad0, 0x52 }, { PhysicalKey.NumPadDecimal, 0x53 }, { PhysicalKey.NumPadEnter, 0x1c },
			// the gray cursor block shares scan codes with the keypad but never has ASCII
			{ PhysicalKey.Home, 0x47 }, { PhysicalKey.ArrowUp, 0x48 }, { PhysicalKey.PageUp, 0x49 },
			{ PhysicalKey.ArrowLeft, 0x4b }, { PhysicalKey.ArrowRight, 0x4d },
			{ PhysicalKey.End, 0x4f }, { PhysicalKey.ArrowDown, 0x50 }, { PhysicalKey.PageDown, 0x51 },
			{ PhysicalKey.Insert, 0x52 }, { PhysicalKey.Delete, 0x53 },
			{ PhysicalKey.F11, 0x85 }, { PhysicalKey.F12, 0x86 },
		};

		private static readonly HashSet<PhysicalKey> aNoASCII = new()
		{
			PhysicalKey.Home, PhysicalKey.ArrowUp, PhysicalKey.PageUp, PhysicalKey.ArrowLeft, PhysicalKey.ArrowRight,
			PhysicalKey.End, PhysicalKey.ArrowDown, PhysicalKey.PageDown, PhysicalKey.Insert, PhysicalKey.Delete,
		};

		/// <summary>Returns the BIOS word for the key, or -1 if it doesn't produce a keystroke.</summary>
		public static int ToBIOSKey(KeyEventArgs e)
		{
			if (!aScanCodes.TryGetValue(e.PhysicalKey, out byte scanCode))
				return -1;

			byte ascii = 0;

			if (aNoASCII.Contains(e.PhysicalKey) || (e.KeyModifiers & KeyModifiers.Alt) != 0)
			{
				// extended key, or Alt+key which BIOS reports with ASCII 0
			}
			else if ((e.KeyModifiers & KeyModifiers.Control) != 0)
			{
				// Key.A..Key.Z are contiguous, PhysicalKey letters are not guaranteed to be
				if (e.Key >= Key.A && e.Key <= Key.Z)
					ascii = (byte)(e.Key - Key.A + 1);
			}
			else
			{
				switch (e.PhysicalKey)
				{
					case PhysicalKey.Escape: ascii = 0x1b; break;
					case PhysicalKey.Enter:
					case PhysicalKey.NumPadEnter: ascii = 0x0d; break;
					case PhysicalKey.Backspace: ascii = 0x08; break;
					case PhysicalKey.Tab: ascii = 0x09; break;
					default:
						if (e.KeySymbol is { Length: 1 } && e.KeySymbol[0] < 0x80)
							ascii = (byte)e.KeySymbol[0];
						break;
				}
			}

			return (scanCode << 8) | ascii;
		}
	}
}
