using Windows.Foundation;

namespace AvalonDock.Controls
{
	// The coordinate-space seam for drag/drop (see docs/refactoring.md, Challenge 2 & 4).
	//
	// During a drag, every signal arrives in *native screen* coordinates (physical
	// pixels on Windows, Quartz logical points on macOS) but the overlay/hit-test
	// logic works in the manager's *local* coordinate space. This interface isolates
	// that translation behind one abstraction so:
	//   • DockingManager's drag orchestration depends on the abstraction, not on
	//     platform-specific origin math scattered through the code, and
	//   • tests can supply a fake space (fixed origin + scale) and exercise the
	//     overlay/drop logic with no real window.
	internal interface IDragCoordinateSpace
	{
		/// <summary>
		/// Origin of this manager's content area in native screen coordinates.
		/// </summary>
		(double X, double Y) GetScreenOrigin();

		/// <summary>
		/// Translate a native-screen point into manager-local (DIP) coordinates.
		/// </summary>
		Point ScreenToManagerLocal(Point screen);
	}

	// Pure, headless-testable coordinate math. No OS or XAML state — the OS lookups
	// (ClientToScreen, Cocoa convertRectToScreen, AppWindow.Position) stay in
	// DockingManager and feed their results into these functions.
	internal static class DragCoordinateMath
	{
		/// <summary>
		/// Combine a native window-origin with this manager's offset inside the
		/// visual root. The offset is measured in DIPs (TransformToVisual), so it is
		/// scaled to the native origin's pixel space before adding.
		/// </summary>
		public static (double X, double Y) CombineOrigin(
			double nativeOriginX, double nativeOriginY,
			double managerOffsetX, double managerOffsetY,
			double scale)
		{
			if (scale <= 0) scale = 1.0;
			return (nativeOriginX + managerOffsetX * scale,
			        nativeOriginY + managerOffsetY * scale);
		}

		/// <summary>
		/// Convert a native-screen point to manager-local (DIP) coordinates given the
		/// manager's screen origin and rasterization scale. On macOS callers pass
		/// scale = 1 (cursor and origin are both logical points).
		/// </summary>
		public static Point ScreenToManagerLocal(
			Point screen, double originX, double originY, double scale)
		{
			if (scale <= 0) scale = 1.0;
			return new Point((screen.X - originX) / scale, (screen.Y - originY) / scale);
		}
	}
}
