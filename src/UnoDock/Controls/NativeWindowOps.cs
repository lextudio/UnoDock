namespace AvalonDock.Controls
{
	// Platform seam for floating-window operations that differ macOS vs Windows
	// (docs/refactoring.md, Challenge 4): reading a window's screen position and
	// bringing it to front. Confines the platform `#if`/OS branch to this one file
	// so DockingManager's drag orchestration stays platform-agnostic.
	internal interface INativeWindowOps
	{
		/// <summary>Top-left of the floating window in native screen coordinates.</summary>
		(double X, double Y) GetWindowTopLeft(LayoutFloatingWindowControl fwc);

		/// <summary>Bring the floating window above the main window.</summary>
		void BringToFront(LayoutFloatingWindowControl fwc);
	}

	// Factory + implementations. Single platform `#if` lives here; implementations
	// delegate to the existing native helpers (no new native code).
	internal static class NativeWindowOps
	{
		private static INativeWindowOps _shared;

		public static INativeWindowOps Shared => _shared ??= Create();

		public static INativeWindowOps Create()
		{
#if !WINDOWS
			if (System.OperatingSystem.IsMacOS())
				return new MacOSNativeWindowOps();
#endif
			return new WindowsNativeWindowOps();
		}

		private sealed class WindowsNativeWindowOps : INativeWindowOps
		{
			public (double X, double Y) GetWindowTopLeft(LayoutFloatingWindowControl fwc)
				=> fwc.GetWindowPosition();

			public void BringToFront(LayoutFloatingWindowControl fwc)
				=> fwc.BringToFrontWindows();
		}

#if !WINDOWS
		private sealed class MacOSNativeWindowOps : INativeWindowOps
		{
			public (double X, double Y) GetWindowTopLeft(LayoutFloatingWindowControl fwc)
				=> fwc.NsWindowHandle != 0
					? AvalonDock.Hosting.MacOSWindowTabbing.GetWindowPosition(fwc.NsWindowHandle)
					: fwc.GetWindowPosition();

			public void BringToFront(LayoutFloatingWindowControl fwc)
			{
				if (fwc.NsWindowHandle != 0)
					AvalonDock.Hosting.MacOSWindowTabbing.OrderWindowFront(fwc.NsWindowHandle);
			}
		}
#endif
	}
}
