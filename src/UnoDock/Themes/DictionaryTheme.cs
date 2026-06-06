// Port of AvalonDock's DictionaryTheme (WPF) to WinUI / Uno.
// Allows themes to supply a ResourceDictionary built at runtime
// (e.g. from an embedded vstheme file) instead of a XAML pack URI.

using System;
using Microsoft.UI.Xaml;

namespace AvalonDock.Themes
{
	/// <summary>
	/// Base class for themes that supply their <see cref="ResourceDictionary"/> programmatically
	/// rather than via a XAML pack URI.  Mirrors AvalonDock's WPF <c>DictionaryTheme</c>.
	/// </summary>
	public abstract class DictionaryTheme : Theme
	{
		protected DictionaryTheme(ResourceDictionary themeResourceDictionary)
		{
			ThemeResourceDictionary = themeResourceDictionary;
		}

		/// <summary>The ResourceDictionary that defines this theme.</summary>
		public ResourceDictionary ThemeResourceDictionary { get; }

		/// <inheritdoc/>
		public override Uri GetResourceUri() => null;
	}
}
