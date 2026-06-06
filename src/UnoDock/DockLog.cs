// Opt-in debug logger for UnoDock.
// Disabled by default — no file I/O, no allocations.
//
// To enable from your app:
//   AvalonDock.DockLog.Enable();               // start logging
//   AvalonDock.DockLog.Enable("C:\my.log");    // custom path
//
// The log resets (truncates) each time Enable() is called.
// Disable again with AvalonDock.DockLog.Disable().

using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace AvalonDock
{
	public static class DockLog
	{
		private static string _path = Path.Combine(Path.GetTempPath(), "undock_debug.log");
		private static bool _enabled;

		/// <summary>True when logging is active.</summary>
		public static bool Enabled => _enabled;

		/// <summary>
		/// Enable logging, optionally to a custom file path.
		/// Resets (truncates) the log file immediately.
		/// </summary>
		public static void Enable(string path = null)
		{
			if (path != null) _path = path;
			_enabled = true;
			Reset();
		}

		/// <summary>Disable logging. Subsequent Write() calls are no-ops.</summary>
		public static void Disable() => _enabled = false;

		/// <summary>Truncate the log file and write a header line.</summary>
		public static void Reset()
		{
			if (!_enabled) return;
			try { File.WriteAllText(_path, $"[{Now()}] === UnoDock log start  path={_path} ===\n"); }
			catch { }
		}

		/// <summary>Append a timestamped line. No-op when disabled.</summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Write(string msg)
		{
			if (!_enabled) return;
			try { File.AppendAllText(_path, $"[{Now()}] {msg}\n"); }
			catch { }
		}

		private static string Now() => DateTime.Now.ToString("HH:mm:ss.fff");
	}
}
