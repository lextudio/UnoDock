// Port of AvalonDock.Themes.VS2013/Vs2013LightTheme.cs to WinUI / Uno.
using AvalonDock.Themes;

namespace UnoDock.Themes.VS2013
{
    /// <summary>VS2013 Light theme for UnoDock.</summary>
    public class Vs2013LightTheme : DictionaryTheme
    {
        /// <inheritdoc/>
        public Vs2013LightTheme()
            : base(VsThemePaletteFactory.BuildDictionary(VsThemeResources.Light)) { }
    }
}
