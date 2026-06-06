// Native OS-driven floating-window dragging (session 26 refactor).
//
// Replaces the polling DispatcherTimer trackers (FloatingWindowDragTracker /
// WindowsFloatingWindowDragTracker) with a hand-off to the OS native window-move
// loop — the same approach WPF AvalonDock uses (WM_NCLBUTTONDOWN/HTCAPTION).
// See docs/drag-to-float.md Parts 3 & 4 for the full design.
//
// Pattern split:
//   • Strategy        — INativeWindowDrag is what DockingManager depends on.
//   • Template Method — NativeWindowDragBase fixes the invariant algorithm
//                       (pre-position → observe → hand off → raise events → tear
//                       down) once; platform subclasses fill only the native
//                       primitives.
//   • Factory Method  — LayoutFloatingWindowControl.CreateNativeDrag() picks the
//                       concrete subclass per OS.

using System;
using Windows.Foundation;

namespace AvalonDock.Controls
{
	/// <summary>
	/// One active native drag of a floating window. DockingManager subscribes to
	/// <see cref="Moving"/> / <see cref="Ended"/> instead of running a timer.
	/// All coordinates are physical screen pixels (top-left origin) on Windows and
	/// Quartz logical points (top-left origin, Y-down) on macOS.
	/// </summary>
	internal interface INativeWindowDrag : IDisposable
	{
		/// <summary>
		/// Position the window so the cursor sits at <paramref name="grabOffset"/>
		/// inside it, then hand control to the OS native move loop. The physical
		/// mouse button must still be down (the tear-off gesture never released it).
		/// </summary>
		void BeginDrag(Point cursorScreen, Point grabOffset);

		/// <summary>Raised continuously while the OS moves the window. Carries the
		/// live cursor in screen coordinates. (Mirrors WM_MOVING / NSWindowDidMove.)</summary>
		event Action<Point> Moving;

		/// <summary>Raised exactly once when the OS move loop ends on mouse-up.
		/// Carries the final cursor position. (Mirrors WM_EXITSIZEMOVE.)</summary>
		event Action<Point> Ended;
	}

	/// <summary>
	/// Template Method base: owns the invariant drag algorithm and the event
	/// plumbing. Platform subclasses implement only the native primitives.
	/// </summary>
	internal abstract class NativeWindowDragBase : INativeWindowDrag
	{
		public event Action<Point> Moving;
		public event Action<Point> Ended;

		private bool _ended;
		private bool _disposed;

		// ── Template method: the invariant algorithm. Not overridable. ──
		public void BeginDrag(Point cursorScreen, Point grabOffset)
		{
			// 1. Pre-position under the cursor (DragDelta) — AvalonDock OnActivated step.
			MoveWindowNative(cursorScreen.X - grabOffset.X, cursorScreen.Y - grabOffset.Y);

			// 2. Observe native move/end BEFORE the hand-off so no frame is lost.
			InstallObservers();

			// 3. Hand control to the OS native move loop.
			HandOffToNativeMoveLoop(cursorScreen, grabOffset);
		}

		/// <summary>Subclasses call this from their native move callback.</summary>
		protected void RaiseMoving(Point cursor)
		{
			if (_ended || _disposed) return;
			Moving?.Invoke(cursor);
		}

		/// <summary>Subclasses call this from their native end callback. Idempotent —
		/// fires exactly once even if move and end signals race.</summary>
		protected void RaiseEnded(Point cursor)
		{
			if (_ended) return;
			_ended = true;
			RemoveObservers();
			Ended?.Invoke(cursor);
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			RemoveObservers(); // safe to call twice
			DisposeCore();
		}

		// ── Primitive operations: the ONLY things platforms implement. ──

		/// <summary>Move the window's top-left corner to the given screen position.</summary>
		protected abstract void MoveWindowNative(double x, double y);

		/// <summary>Trigger the OS native window-move loop (WM_NCLBUTTONDOWN /
		/// performWindowDragWithEvent:).</summary>
		protected abstract void HandOffToNativeMoveLoop(Point cursor, Point grabOffset);

		/// <summary>Start listening for native move + end signals.</summary>
		protected abstract void InstallObservers();

		/// <summary>Stop listening. Must be safe to call more than once.</summary>
		protected abstract void RemoveObservers();

		/// <summary>Read the current cursor position in screen coordinates.</summary>
		protected abstract Point GetCursorScreen();

		/// <summary>Optional extra teardown.</summary>
		protected virtual void DisposeCore() { }
	}
}
