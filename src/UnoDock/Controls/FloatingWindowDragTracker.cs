// Tracks a floating window being dragged by the user (title-bar drag on macOS).
//
// Strategy (same as UnoDocking's proven approach, simplified):
//   1. DispatcherTimer fires at 16 ms.
//   2. On each tick: read cursor via CGEventGetLocation (Quartz: top-left Y-down).
//   3. Move the floating NSWindow to follow the cursor (using MacOSWindowTabbing.MoveWindow).
//   4. If cursor is over the DockingManager's screen rect → notify the compass overlay.
//   5. When left button released → commit or cancel drop.
//
// Coordinate system: ALL positions in Quartz logical points (top-left, Y-down).
// The NSWindow is moved using setFrameTopLeftPoint: which needs Cocoa Y-up;
// MacOSWindowTabbing.MoveWindow handles the flip internally.
//
// Careful avoidance of UnoDocking ghost-window bug:
//   - When drop is confirmed: CloseWindow(_nsWindow) FIRST, THEN remove model.
//   - Never call Uno Window.Close() directly; it can race with Uno's compositor.

#if !WINDOWS
using System;
using System.Runtime.InteropServices;
using AvalonDock.Hosting;
using AvalonDock.Layout;
using Microsoft.UI.Xaml;

namespace AvalonDock.Controls
{
	internal sealed class FloatingWindowDragTracker
	{
		// Public static: used by the DockingManager watchdog.
		public static bool IsLeftButtonHeld() => CGEventSourceButtonState(CombinedSession, LeftButton);

		// ── CoreGraphics cursor / button state ──────────────────────────────

		private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

		[StructLayout(LayoutKind.Sequential)]
		private struct CGPoint { public double X; public double Y; }

		[DllImport(CG)] private static extern IntPtr CGEventCreate(IntPtr source);
		[DllImport(CG)] private static extern CGPoint CGEventGetLocation(IntPtr e);
		[DllImport(CG)] private static extern void CFRelease(IntPtr cf);
		[DllImport(CG)] private static extern bool CGEventSourceButtonState(int stateID, uint button);

		private const int  CombinedSession = 0;
		private const uint LeftButton       = 0;

		// Opt-in (UNODOCK_CURSOR_NSEVENT=1): read the cursor via NSEvent.mouseLocation
		// instead of allocating a CGEvent each tick. Off by default until validated.
		private static readonly bool UseNsEventCursor =
			Environment.GetEnvironmentVariable("UNODOCK_CURSOR_NSEVENT") == "1";

		// Opt-in (UNODOCK_OVERLAY_THROTTLE=N): update the docking overlay/compass only
		// every N ticks while still moving the window every tick. 1 = every tick
		// (default, unchanged). Higher = cheaper hit-testing at the cost of overlay
		// responsiveness. The drop still uses the most recent overlay state.
		private static readonly int OverlayThrottleTicks = ResolveOverlayThrottle();

		private static int ResolveOverlayThrottle()
		{
			var raw = Environment.GetEnvironmentVariable("UNODOCK_OVERLAY_THROTTLE");
			return int.TryParse(raw, out var n) && n > 1 ? n : 1;
		}

		private static (double X, double Y) GetCursorLogical()
		{
			if (UseNsEventCursor)
				return MacOSWindowTabbing.GetCursorLocationQuartzViaNSEvent();

			var evt = CGEventCreate(IntPtr.Zero);
			if (evt == IntPtr.Zero) return (0, 0);
			try { var p = CGEventGetLocation(evt); return (p.X, p.Y); }
			finally { CFRelease(evt); }
		}

		private static bool IsLeftButtonDown()
			=> CGEventSourceButtonState(CombinedSession, LeftButton);

		// ── Instance state ──────────────────────────────────────────────────

		private readonly LayoutFloatingWindowControl _fwc;
		private readonly nint _nsWindow;
		private readonly DockingManager _manager;
		private readonly bool _skipInitialDelay;
		private DispatcherTimer _timer;

		// Cursor offset from window top-left recorded when drag starts.
		private double _offsetX, _offsetY;
		private bool _started;

		// Callbacks wired by DockingManager:
		public Action<double, double> OnCursorInManagerCoords; // (hostX, hostY) in manager's local coords
		public Action OnDragEnded;

		/// <param name="skipInitialDelay">When true, skip MinTicksBeforeDrop (button is already held).</param>
		public FloatingWindowDragTracker(
			LayoutFloatingWindowControl fwc,
			nint nsWindow,
			DockingManager manager,
			bool skipInitialDelay = false)
		{
			_fwc              = fwc;
			_nsWindow         = nsWindow;
			_manager          = manager;
			_skipInitialDelay = skipInitialDelay;
		}

		/// <summary>Start tracking. Call immediately after the floating window's
		/// NSWindow is positioned (i.e. after Show() + DisableLastWindowTabbing()).</summary>
		public void Start()
		{
			if (_started) return;
			_started = true;

			// Measure initial cursor offset from window top-left.
			var (cx, cy) = GetCursorLogical();
			var (winX, winY) = MacOSWindowTabbing.GetWindowPosition(_nsWindow);
			if (winX != 0 || winY != 0)
			{
				_offsetX = Math.Max(0, cx - winX);
				_offsetY = Math.Max(0, cy - winY);
			}
			else
			{
				_offsetX = _fwc.Width > 0 ? _fwc.Width / 2.0 : 200.0;
				_offsetY = 14.0;
			}

			MacOSWindowTabbing.DragLog(
				$"Tracker.Start: nsWin=0x{_nsWindow:X} cursor=({cx:F0},{cy:F0}) " +
				$"winPos=({winX:F0},{winY:F0}) offset=({_offsetX:F0},{_offsetY:F0}) skipInitialDelay={_skipInitialDelay}");

			_timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
			_timer.Tick += OnTick;
			_timer.Start();
		}

		public void Stop()
		{
			if (_timer == null) return;
			_timer.Stop();
			_timer.Tick -= OnTick;
			_timer = null;
		}

		private int _tickCount;

		private void OnTick(object sender, object e)
		{
			try { OnTickCore(); }
			catch { /* never crash the timer */ }
		}

		// Minimum ticks before firing OnDragEnded (prevents spurious drop on code-invoked float).
		// Set to 0 when skipInitialDelay=true (watchdog confirmed a real drag).
		private int MinTicksBeforeDrop => _skipInitialDelay ? 0 : 3;

		/// <summary>
		/// Called by DockingManager to give the tracker the manager's screen origin in
		/// Quartz coordinates (top-left, Y-down). Must be set before Start() for accurate
		/// compass hit-testing. Default is (0,0) = unknown.
		/// </summary>
		public (double X, double Y) ManagerScreenOriginQ { get; set; }

		/// <summary>
		/// Optional dynamic provider for the manager's Quartz screen origin.
		/// When available, this is queried on every tick to avoid stale origin drift.
		/// </summary>
		public Func<(double X, double Y)> ManagerScreenOriginProvider { get; set; }

		private void OnTickCore()
		{
			// Switch to 16 ms after first tick
			if (_tickCount == 0 && _timer?.Interval.TotalMilliseconds != 16)
				if (_timer != null) _timer.Interval = TimeSpan.FromMilliseconds(16);

			bool buttonDown = IsLeftButtonDown();

			// Phase-in: only commit/cancel after MinTicksBeforeDrop ticks.
			// This prevents a spurious drop when the tracker starts while
			// button is already released (code-invoked float with no real drag).
			if (_tickCount < MinTicksBeforeDrop)
			{
				_tickCount++;
				if (!buttonDown) return; // don't fire OnDragEnded yet
			}
			else
			{
				_tickCount++;
			}

			// Move the floating window to follow the cursor
			var (cx, cy) = GetCursorLogical();
			if (_tickCount <= 3)
			{
				var before = MacOSWindowTabbing.GetWindowPosition(_nsWindow);
				MacOSWindowTabbing.MoveWindow(_nsWindow, cx, cy, _offsetX, _offsetY);
				var after = MacOSWindowTabbing.GetWindowPosition(_nsWindow);
				MacOSWindowTabbing.DragLog(
					$"Tracker.Tick#{_tickCount}: cursor=({cx:F0},{cy:F0}) before=({before.X:F0},{before.Y:F0}) " +
					$"after=({after.X:F0},{after.Y:F0}) offset=({_offsetX:F0},{_offsetY:F0}) buttonDown={buttonDown}");
			}
			else
			{
				MacOSWindowTabbing.MoveWindow(_nsWindow, cx, cy, _offsetX, _offsetY);
			}

			// Notify compass only when button is actually down (real drag).
			// Optionally throttle the overlay update (window still moves every tick).
			if (buttonDown && (_tickCount % OverlayThrottleTicks == 0))
			{
				var (originX, originY) = ResolveOrigin();
				var hostX = cx - originX;
				var hostY = cy - originY;
				MacOSWindowTabbing.DragLogVerbose(
					$"[Tracker] cursor=({cx:F1},{cy:F1}) origin=({originX:F1},{originY:F1}) host=({hostX:F1},{hostY:F1})");
				OnCursorInManagerCoords?.Invoke(hostX, hostY);
			}

			if (!buttonDown)
			{
				Stop();
				OnDragEnded?.Invoke();
			}
		}

		// Recompute the manager screen origin at most every N ticks. The origin only
		// changes if the MAIN window moves, which it does not during a floating drag —
		// so recomputing the expensive convertRectToScreen + window enumeration every
		// 16 ms was pure overhead. ~15 ticks ≈ 240 ms is plenty to absorb the rare case.
		private const int OriginRecomputeEveryTicks = 15;
		private bool _haveCachedOrigin;
		private (double X, double Y) _cachedOrigin;

		private (double X, double Y) ResolveOrigin()
		{
			if (_haveCachedOrigin && _tickCount % OriginRecomputeEveryTicks != 0)
				return _cachedOrigin;

			(double X, double Y) origin = (0, 0);
			if (ManagerScreenOriginProvider != null)
			{
				try { origin = ManagerScreenOriginProvider(); }
				catch { origin = (0, 0); }
			}
			if (origin.X == 0 && origin.Y == 0)
				origin = ManagerScreenOriginQ;
			if (origin.X == 0 && origin.Y == 0)
			{
				// Last resort: resolve the host window focus-independently (excluding the
				// window being dragged) and ask Cocoa for its content origin.
				var host = MacOSWindowTabbing.GetMainAppWindow(new[] { _nsWindow });
				if (host != 0)
					origin = MacOSWindowTabbing.GetWindowContentOriginViaConvert(host);
			}

			// Only cache a real (non-zero) origin so we keep retrying until one resolves.
			if (origin.X != 0 || origin.Y != 0)
			{
				_cachedOrigin = origin;
				_haveCachedOrigin = true;
			}
			return origin;
		}
	}
}
#endif
