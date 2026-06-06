// Port of AvalonDock.Themes.VS2013/Vs2013DarkTheme.cs to WinUI / Uno.
using AvalonDock.Themes;

namespace UnoDock.Themes.VS2013
{
    /// <summary>VS2013 Dark theme for UnoDock.</summary>
    public class Vs2013DarkTheme : DictionaryTheme
    {
        /// <inheritdoc/>
        public Vs2013DarkTheme()
            : base(VsThemePaletteFactory.BuildDictionary(VsThemeResources.Dark)) { }
    }
}
