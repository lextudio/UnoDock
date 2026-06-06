// Port of AvalonDock.Themes.VS2013/Vs2013BlueTheme.cs to WinUI / Uno.
using AvalonDock.Themes;

namespace UnoDock.Themes.VS2013
{
    /// <summary>VS2013 Blue theme for UnoDock.</summary>
    public class Vs2013BlueTheme : DictionaryTheme
    {
        /// <inheritdoc/>
        public Vs2013BlueTheme()
            : base(VsThemePaletteFactory.BuildDictionary(VsThemeResources.Blue)) { }
    }
}
