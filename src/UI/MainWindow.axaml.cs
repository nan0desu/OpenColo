using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using IRB.VirtualCPU;
using OpenColo.Graphics;
using System.Runtime.InteropServices;

namespace OpenColo.UI
{
	public partial class MainWindow : Window
	{
		private bool closing = false;

		private OpenColoGame game;
		private DispatcherTimer timer;
		private Thread gameThread;
		private Exception? gameException = null;

		// We have two interchangeable buffer bitmaps
		private int bitmapIndex = 0;
		private WriteableBitmap[] bitmaps = new WriteableBitmap[2];
		private int[] pixels = new int[VCPUVGA.Width * VCPUVGA.Height];

		public MainWindow()
		{
			InitializeComponent();

			Thread.CurrentThread.Name = "OpenColo main thread";

			this.game = new OpenColoGame();

			for (int i = 0; i < this.bitmaps.Length; i++)
			{
				this.bitmaps[i] = new WriteableBitmap(new PixelSize(VCPUVGA.Width, VCPUVGA.Height), new Vector(96, 96), PixelFormat.Bgra8888);
			}

			this.Closing += this.MainWindow_Closing;
			this.KeyDown += this.MainWindow_KeyDown;
			this.mainImage.PointerMoved += this.MainImage_Pointer;
			this.mainImage.PointerPressed += this.MainImage_Pointer;
			this.mainImage.PointerReleased += this.MainImage_Pointer;

			// 70 Hz is what mode 13h refreshes at, anything close will do
			this.timer = new DispatcherTimer(DispatcherPriority.Render);
			this.timer.Interval = TimeSpan.FromMilliseconds(14);
			this.timer.Tick += this.Timer_Tick;
			this.timer.Start();

			this.gameThread = new Thread(new ThreadStart(GameThread));
			this.gameThread.Name = "OpenColo game thread";
			this.gameThread.IsBackground = true;
			this.gameThread.Start();
		}

		private void Timer_Tick(object? sender, EventArgs e)
		{
			if (!this.closing && !this.gameThread.IsAlive)
			{
				if (this.gameException != null)
				{
					// keep the last frame up so the state at the crash can be looked at
					this.timer.Stop();
					this.Title = $"OpenColo - stopped: {this.gameException.Message} (see Exception.log)";
					return;
				}

				this.closing = true;
				this.Close();
				return;
			}

			RedrawScreen();
		}

		private void RedrawScreen()
		{
			VCPUVGA vga = this.game.CPU.VGA;
			int[] palette = new int[256];

			lock (VCPU.GraphicsLock)
			{
				for (int i = 0; i < 256; i++)
				{
					// 6-bit DAC to 8-bit, replicating the top bits into the bottom ones
					int r = vga.Palette[i * 3];
					int g = vga.Palette[i * 3 + 1];
					int b = vga.Palette[i * 3 + 2];

					palette[i] = unchecked((int)0xff000000) | (((r << 2) | (r >> 4)) << 16) | (((g << 2) | (g >> 4)) << 8) | ((b << 2) | (b >> 4));
				}
			}

			byte[] framebuffer = vga.Framebuffer;

			for (int i = 0; i < this.pixels.Length; i++)
			{
				this.pixels[i] = palette[framebuffer[i]];
			}

			WriteableBitmap currentBitmap = this.bitmaps[this.bitmapIndex];

			using (ILockedFramebuffer l = currentBitmap.Lock())
			{
				for (int y = 0; y < VCPUVGA.Height; y++)
				{
					Marshal.Copy(this.pixels, y * VCPUVGA.Width, l.Address + y * l.RowBytes, VCPUVGA.Width);
				}
			}

			this.mainImage.Source = currentBitmap;

			this.bitmapIndex++;
			this.bitmapIndex %= 2;
		}

		#region Keyboard and Mouse events
		private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
		{
			// F12 writes Dump.bin and Dump.txt from the game thread, the game doesn't see it
			if (e.Key == Key.F12)
			{
				this.game.CPU.DumpRequested = true;
				e.Handled = true;
				return;
			}

			int key = KeyMap.ToBIOSKey(e);

			if (key >= 0)
			{
				lock (VCPU.KeyboardLock)
				{
					this.game.CPU.Keys.Enqueue(key);
				}

				e.Handled = true;
			}
		}

		private void MainImage_Pointer(object? sender, PointerEventArgs e)
		{
			PointerPoint pointer = e.GetCurrentPoint(this.mainImage);
			Point point = pointer.Position;

			// the image control is 320x240, the screen is 320x200
			int x = Math.Clamp((int)(point.X * VCPUVGA.Width / this.mainImage.Bounds.Width), 0, VCPUVGA.Width - 1);
			int y = Math.Clamp((int)(point.Y * VCPUVGA.Height / this.mainImage.Bounds.Height), 0, VCPUVGA.Height - 1);

			this.game.CPU.MouseLocation = new GPoint(x, y);
			this.game.CPU.MouseButtons = (pointer.Properties.IsLeftButtonPressed ? MouseButtonsEnum.Left : MouseButtonsEnum.None) |
				(pointer.Properties.IsRightButtonPressed ? MouseButtonsEnum.Right : MouseButtonsEnum.None);

			e.Handled = true;
		}
		#endregion

		private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
		{
			this.closing = true;
			this.game.CPU.OnApplicationExit();
		}

		private void GameThread()
		{
			try
			{
				this.game.Start();
			}
			catch (ApplicationExitException)
			{
			}
			catch (Exception ex)
			{
				try
				{
					using StreamWriter writer = new StreamWriter($"{VCPU.AssemblyPath}Exception.log", true);

					writer.WriteLine("---------------------------");
					writer.WriteLine($"Message: {ex.Message}");
					writer.WriteLine($"Stack trace: {ex.StackTrace}");
				}
				catch
				{
				}

				// Show exceptions on UI Thread
				this.gameException = ex;
			}
		}
	}
}
