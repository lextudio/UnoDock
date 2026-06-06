// WPF's CommandManager re-queries CanExecute on input events. WinUI has no
// equivalent. AvalonDock's RelayCommand and DockingManager both call
// CommandManager.InvalidateRequerySuggested(); stub it as a no-op.

using System;

namespace System.Windows.Input
{
	internal static class CommandManager
	{
		public static event EventHandler RequerySuggested { add { } remove { } }

		public static void InvalidateRequerySuggested() { }
	}
}
