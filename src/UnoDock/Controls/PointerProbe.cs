namespace AvalonDock.Controls
{
	// Platform seam for reading the native pointer during a drag (docs/refactoring.md,
	// Challenge 4). Cursor position and left-button state are sampled from the OS, not
	// the framework (Uno pointer events are unreliable mid-drag). This interface
	// confines that platform concern so DockingManager's orchestration depends on the
	// abstraction, and tests can substitute a fake.
	internal interface IPointerProbe
	{
		/// <summary>Current cursor position in native screen coordinates
		/// (physical pixels on Windows, Quartz logical points on macOS).</summary>
		(double X, double Y) GetCursorScreen();

		/// <summary>Whether the primary (left) mouse button is currently held.</summary>
		bool IsLeftButtonDown();
	}

	// Factory + implementations. The single platform `#if` lives here, not scattered
	// through the drag orchestration. Implementations delegate to the existing,
	// already-proven native statics (no new P/Invoke), so this is a pure facade.
	internal static class PointerProbe
	{
		private static IPointerProbe _shared;

		public static IPointerProbe Shared => _shared ??= Create();

		public static IPointerProbe Create()
		{
#if !WINDOWS
			if (System.OperatingSystem.IsMacOS())
				return new MacOSPointerProbe();
#endif
			return new WindowsPointerProbe();
		}

		private sealed class WindowsPointerProbe : IPointerProbe
		{
			public (double X, double Y) GetCursorScreen() => WindowsFloatingWindowDragTracker.GetCursorScreen();
			public bool IsLeftButtonDown() => WindowsFloatingWindowDragTracker.IsLeftButtonHeld();
		}

#if !WINDOWS
		private sealed class MacOSPointerProbe : IPointerProbe
		{
			public (double X, double Y) GetCursorScreen()
				=> AvalonDock.Hosting.MacOSWindowTabbing.GetCursorLocationQuartz();
			public bool IsLeftButtonDown() => FloatingWindowDragTracker.IsLeftButtonHeld();
		}
#endif
	}
}
