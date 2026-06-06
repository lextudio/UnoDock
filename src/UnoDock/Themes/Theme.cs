// Base class for UnoDock themes, mirroring AvalonDock's Theme abstraction.
// In WinUI / Uno the resource URI points to a ResourceDictionary that is merged
// into DockingManager.Resources when the Theme property changes.

using System;
using Microsoft.UI.Xaml;

namespace AvalonDock.Themes
{
	/// <summary>Base class for UnoDock themes.</summary>
	public abstract partial class Theme : DependencyObject
	{
		/// <summary>Returns the URI of the XAML ResourceDictionary for this theme.</summary>
		public abstract Uri GetResourceUri();
	}
}
