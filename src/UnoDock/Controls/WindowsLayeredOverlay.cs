// Native Win32 per-pixel-alpha overlay window for Windows desktop.
//
// Uno's Win32 Skia backend renders windows through an OpenGL swap chain and creates
// plain opaque WS_OVERLAPPEDWINDOWs — there is no path to a transparent top-level Uno
// window. To draw the drag compass above the floating child window with true
// transparency, UnoDock hosts a separate raw Win32 window with WS_EX_LAYERED and pushes
// a premultiplied-BGRA bitmap to it via UpdateLayeredWindow (ULW_ALPHA). That bitmap is
// produced by RenderTargetBitmap-capturing the existing themed OverlayWindow control, so
// all compass visuals/themes are reused unchanged.
//
// The window is WS_EX_TRANSPARENT (click-through) + WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW
// and HWND_TOPMOST, so it floats above both the main and the floating child windows
// without stealing input — drag tracking continues to be driven by cursor polling.

#if WINDOWS || HAS_UNO_WINUI || true
using System;
using System.Runtime.InteropServices;

namespace AvalonDock.Controls
{
	internal sealed class WindowsLayeredOverlay : IDisposable
	{
		private const string ClassName = "UnoDockLayeredOverlay";
		private static IntPtr _hInstance;
		private static ushort _classAtom;
		private static WndProcDelegate _wndProc; // kept alive

		private IntPtr _hwnd;
		private bool _disposed;

		public IntPtr Handle => _hwnd;

		public WindowsLayeredOverlay()
		{
			EnsureClassRegistered();
			// Zero initial size/pos; positioned on first Update.
			_hwnd = CreateWindowExW(
				WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
				ClassName,
				string.Empty,
				WS_POPUP,
				0, 0, 0, 0,
				IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);
		}

		/// <summary>
		/// Push a premultiplied-BGRA, top-down pixel buffer to the layered window and place
		/// it at the given screen position. The window is shown (no activation) and kept
		/// topmost. <paramref name="bgraPremultiplied"/> must be width*height*4 bytes.
		/// </summary>
		public void Update(byte[] bgraPremultiplied, int width, int height, int screenX, int screenY)
		{
			if (_disposed || _hwnd == IntPtr.Zero || width <= 0 || height <= 0)
				return;
			if (bgraPremultiplied == null || bgraPremultiplied.Length < width * height * 4)
				return;

			var screenDc = GetDC(IntPtr.Zero);
			if (screenDc == IntPtr.Zero)
				return;

			var memDc = CreateCompatibleDC(screenDc);
			if (memDc == IntPtr.Zero) { ReleaseDC(IntPtr.Zero, screenDc); return; }

			IntPtr hBitmap = IntPtr.Zero, oldBitmap = IntPtr.Zero;
			try
			{
				var bmi = new BITMAPINFO
				{
					biSize = (uint)Marshal.SizeOf<BITMAPINFO>(),
					biWidth = width,
					biHeight = -height, // negative → top-down DIB
					biPlanes = 1,
					biBitCount = 32,
					biCompression = 0, // BI_RGB
				};

				hBitmap = CreateDIBSection(memDc, ref bmi, 0 /*DIB_RGB_COLORS*/, out var bits, IntPtr.Zero, 0);
				if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero)
					return;

				Marshal.Copy(bgraPremultiplied, 0, bits, width * height * 4);
				oldBitmap = SelectObject(memDc, hBitmap);

				var size = new SIZE { cx = width, cy = height };
				var srcPos = new POINT { x = 0, y = 0 };
				var dstPos = new POINT { x = screenX, y = screenY };
				var blend = new BLENDFUNCTION
				{
					BlendOp = AC_SRC_OVER,
					BlendFlags = 0,
					SourceConstantAlpha = 255,
					AlphaFormat = AC_SRC_ALPHA,
				};

				UpdateLayeredWindow(_hwnd, screenDc, ref dstPos, ref size, memDc, ref srcPos, 0, ref blend, ULW_ALPHA);

				// Keep topmost without activating/moving (position already set by ULW dstPos).
				SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
					SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
			}
			finally
			{
				if (oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap);
				if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
				DeleteDC(memDc);
				ReleaseDC(IntPtr.Zero, screenDc);
			}
		}

		public void Hide()
		{
			if (_disposed || _hwnd == IntPtr.Zero) return;
			ShowWindow(_hwnd, SW_HIDE);
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			if (_hwnd != IntPtr.Zero)
			{
				DestroyWindow(_hwnd);
				_hwnd = IntPtr.Zero;
			}
		}

		private static void EnsureClassRegistered()
		{
			if (_classAtom != 0) return;
			_hInstance = GetModuleHandleW(null);
			_wndProc = DefWindowProcW; // click-through, no custom handling needed
			var wc = new WNDCLASSEXW
			{
				cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
				style = 0,
				lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
				hInstance = _hInstance,
				lpszClassName = ClassName,
			};
			_classAtom = RegisterClassExW(ref wc);
		}

		// ── P/Invoke ────────────────────────────────────────────────────────────────
		private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

		private const int WS_POPUP = unchecked((int)0x80000000);
		private const int WS_EX_LAYERED = 0x00080000;
		private const int WS_EX_TRANSPARENT = 0x00000020;
		private const int WS_EX_TOOLWINDOW = 0x00000080;
		private const int WS_EX_NOACTIVATE = 0x08000000;
		private const int WS_EX_TOPMOST = 0x00000008;

		private const int SW_HIDE = 0;
		private const uint ULW_ALPHA = 0x00000002;
		private const byte AC_SRC_OVER = 0x00;
		private const byte AC_SRC_ALPHA = 0x01;

		private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
		private const uint SWP_NOSIZE = 0x0001;
		private const uint SWP_NOMOVE = 0x0002;
		private const uint SWP_NOACTIVATE = 0x0010;
		private const uint SWP_SHOWWINDOW = 0x0040;

		[StructLayout(LayoutKind.Sequential)]
		private struct POINT { public int x; public int y; }

		[StructLayout(LayoutKind.Sequential)]
		private struct SIZE { public int cx; public int cy; }

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		private struct BLENDFUNCTION
		{
			public byte BlendOp;
			public byte BlendFlags;
			public byte SourceConstantAlpha;
			public byte AlphaFormat;
		}

		// Exactly a BITMAPINFOHEADER (40 bytes). BI_RGB 32bpp needs no palette/color masks,
		// so biSize must be the header size — CreateDIBSection uses it to parse the header.
		[StructLayout(LayoutKind.Sequential)]
		private struct BITMAPINFO
		{
			public uint biSize;
			public int biWidth;
			public int biHeight;
			public ushort biPlanes;
			public ushort biBitCount;
			public uint biCompression;
			public uint biSizeImage;
			public int biXPelsPerMeter;
			public int biYPelsPerMeter;
			public uint biClrUsed;
			public uint biClrImportant;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct WNDCLASSEXW
		{
			public uint cbSize;
			public uint style;
			public IntPtr lpfnWndProc;
			public int cbClsExtra;
			public int cbWndExtra;
			public IntPtr hInstance;
			public IntPtr hIcon;
			public IntPtr hCursor;
			public IntPtr hbrBackground;
			[MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
			[MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
			public IntPtr hIconSm;
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		private static extern IntPtr GetModuleHandleW(string lpModuleName);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		private static extern IntPtr CreateWindowExW(
			int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
			int x, int y, int nWidth, int nHeight,
			IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

		[DllImport("user32.dll")]
		private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll")]
		private static extern bool DestroyWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		[DllImport("user32.dll")]
		private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

		[DllImport("user32.dll")]
		private static extern IntPtr GetDC(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

		[DllImport("user32.dll")]
		private static extern bool UpdateLayeredWindow(
			IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
			IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

		[DllImport("gdi32.dll")]
		private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

		[DllImport("gdi32.dll")]
		private static extern bool DeleteDC(IntPtr hdc);

		[DllImport("gdi32.dll")]
		private static extern IntPtr CreateDIBSection(
			IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

		[DllImport("gdi32.dll")]
		private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

		[DllImport("gdi32.dll")]
		private static extern bool DeleteObject(IntPtr hObject);
	}
}
#endif
