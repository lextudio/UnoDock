// Windows native floating-window drag (session 26 refactor). See docs/drag-to-float.md
// Part 4. Direct port of AvalonDock: send WM_NCLBUTTONDOWN/HTCAPTION to enter the OS
// modal move loop, and subclass the HWND to observe WM_MOVING (move) / WM_EXITSIZEMOVE
// (end). The Win32 P/Invokes compile on every target; calls are made only on Windows.

using System;
using System.Runtime.InteropServices;
using Windows.Foundation;

namespace AvalonDock.Controls
{
	internal sealed class WindowsNativeWindowDrag : NativeWindowDragBase
	{
		private const uint WM_MOVING = 0x0216;
		private const uint WM_EXITSIZEMOVE = 0x0232;
		private const uint WM_NCLBUTTONDOWN = 0x00A1;
		private const int HTCAPTION = 2;

		private const uint SWP_NOSIZE = 0x0001;
		private const uint SWP_NOZORDER = 0x0004;
		private const uint SWP_NOACTIVATE = 0x0010;

		// Arbitrary subclass id, unique within this HWND.
		private static readonly UIntPtr SubclassId = (UIntPtr)0xD0C4;

		private readonly IntPtr _hwnd;
		private SUBCLASSPROC _proc; // kept alive for the lifetime of the subclass

		public WindowsNativeWindowDrag(IntPtr hwnd) => _hwnd = hwnd;

		protected override void MoveWindowNative(double x, double y)
		{
			if (_hwnd == IntPtr.Zero) return;
			SetWindowPos(_hwnd, IntPtr.Zero, (int)x, (int)y, 0, 0,
				SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
		}

		protected override void HandOffToNativeMoveLoop(Point cursor, Point grabOffset)
		{
			if (_hwnd == IntPtr.Zero) return;
			// SendMessage is synchronous: DefWindowProc runs the modal move loop and
			// only returns on mouse-up. WM_MOVING fires re-entrantly into our subclass
			// during the loop. After it returns the drag is over.
			var lParam = (IntPtr)(((int)cursor.Y << 16) | ((int)cursor.X & 0xFFFF));
			SendMessage(_hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, lParam);
			RaiseEnded(GetCursorScreen());
		}

		protected override void InstallObservers()
		{
			if (_hwnd == IntPtr.Zero) return;
			_proc = SubclassProc;
			SetWindowSubclass(_hwnd, _proc, SubclassId, IntPtr.Zero);
		}

		protected override void RemoveObservers()
		{
			if (_hwnd == IntPtr.Zero || _proc == null) return;
			RemoveWindowSubclass(_hwnd, _proc, SubclassId);
			_proc = null;
		}

		protected override Point GetCursorScreen()
		{
			return GetCursorPos(out var p) ? new Point(p.X, p.Y) : new Point(0, 0);
		}

		private IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
			UIntPtr uIdSubclass, IntPtr dwRefData)
		{
			switch (uMsg)
			{
				case WM_MOVING:
					RaiseMoving(GetCursorScreen());
					break;
				case WM_EXITSIZEMOVE:
					RaiseEnded(GetCursorScreen());
					break;
			}

			return DefSubclassProc(hWnd, uMsg, wParam, lParam);
		}

		// ── Win32 interop ───────────────────────────────────────────────────────

		[StructLayout(LayoutKind.Sequential)]
		private struct POINT { public int X; public int Y; }

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
			UIntPtr uIdSubclass, IntPtr dwRefData);

		[DllImport("comctl32.dll", SetLastError = true)]
		private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass,
			UIntPtr uIdSubclass, IntPtr dwRefData);

		[DllImport("comctl32.dll", SetLastError = true)]
		private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass,
			UIntPtr uIdSubclass);

		[DllImport("comctl32.dll")]
		private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll")]
		private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll")]
		private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
			int X, int Y, int cx, int cy, uint uFlags);

		[DllImport("user32.dll")]
		private static extern bool GetCursorPos(out POINT lpPoint);
	}
}
