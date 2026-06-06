using System;
using AvalonDock.Themes;

namespace UnoDock.Themes.VS2010
{
    /// <summary>VS2010 theme for UnoDock.</summary>
    public class Vs2010Theme : Theme
    {
        /// <inheritdoc/>
        public override Uri GetResourceUri()
            => new Uri("ms-appx:///UnoDock.Themes.VS2010/Theme.xaml");
    }
}
