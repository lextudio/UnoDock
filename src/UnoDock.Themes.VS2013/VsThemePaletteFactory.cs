// Port of AvalonDock.Themes.VS2013/VsThemePaletteFactory.cs to WinUI / Uno.
// WPF types (System.Windows, System.Windows.Media) replaced with WinUI equivalents.
// Key type changed from ComponentResourceKey to plain string constants (ResourceKeys).
//
// Color-key mapping authority: DockPanel Suite VS2012PaletteFactory
//   (WeifenLuo.WinFormsUI.ThemeVS2012.VS2012PaletteFactory)
// See also: UnoDock/docs/vstheme-file-format.md

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using UnoDock.Themes.VS2013.Themes;
using Windows.UI;

namespace UnoDock.Themes.VS2013
{
    internal sealed class VsThemePaletteFactory
    {
        private readonly XElement _env;

        public VsThemePaletteFactory(byte[] gzipBytes)
        {
            using (var ms = new MemoryStream(gzipBytes))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            {
                var xml = XDocument.Load(gz);
                _env = xml.Root
                    .Element("Theme")
                    .Elements("Category")
                    .FirstOrDefault(c => (string)c.Attribute("Name") == "Environment")
                    ?? throw new InvalidOperationException("vstheme missing Environment category.");
            }
        }

        // ── Public entry point ────────────────────────────────────────────────

        public ResourceDictionary Build()
        {
            var dict = new ResourceDictionary();
            PopulateBrushes(dict);
            dict.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("ms-appx:///UnoDock.Themes.VS2013/OverlayButtons.xaml")
            });
            dict.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("ms-appx:///UnoDock.Themes.VS2013/Themes/Generic.xaml")
            });
            return dict;
        }

        public static ResourceDictionary BuildDictionary(byte[] gzipBytes)
            => new VsThemePaletteFactory(gzipBytes).Build();

        // ── VS2013 color-key mapping (mirrors VS2012PaletteFactory) ──────────

        private void PopulateBrushes(ResourceDictionary d)
        {
            // Accent
            d[ResourceKeys.ControlAccentBrush] = Brush("FileTabSelectedBorder");

            // General / frame
            d[ResourceKeys.Background]        = Brush("AutoHideTabBackgroundBegin");
            d[ResourceKeys.PanelBorderBrush]  = Brush("ToolWindowBorder");
            d[ResourceKeys.ContentBackground] = Brush("ToolWindowBackground");
            d[ResourceKeys.ResizerBackground] = Brush("AutoHideTabBackgroundBegin");
            d[ResourceKeys.CloseButtonForeground] = Brush("ToolWindowButtonInactiveGlyph");

            // Tab bar
            d[ResourceKeys.TabBarBackground]  = Brush("AutoHideTabBackgroundBegin");
            d[ResourceKeys.TabBarBorderBrush] = Brush("AutoHideTabBorder");
            d[ResourceKeys.TabBorderBrush]    = Brush("AutoHideTabBorder");
            d[ResourceKeys.TabText]           = Brush("AutoHideTabText");

            // Auto-hide strip
            d[ResourceKeys.AutoHideTabDefaultBackground] = Brush("AutoHideTabBackgroundBegin");
            d[ResourceKeys.AutoHideTabDefaultBorder]     = Brush("AutoHideTabBorder");
            d[ResourceKeys.AutoHideTabDefaultText]       = Brush("AutoHideTabText");
            d[ResourceKeys.AutoHideTabHoveredBackground] = Brush("AutoHideTabMouseOverBackgroundBegin");
            d[ResourceKeys.AutoHideTabHoveredBorder]     = Brush("AutoHideTabMouseOverBorder");
            d[ResourceKeys.AutoHideTabHoveredText]       = Brush("AutoHideTabMouseOverText");

            // Context / flyout menus
            d[ResourceKeys.MenuBackground]            = Brush("CommandBarMenuBackgroundGradientBegin");
            d[ResourceKeys.MenuBorder]                = Brush("CommandBarMenuBorder");
            d[ResourceKeys.MenuSeparator]             = Brush("CommandBarMenuSeparator");
            d[ResourceKeys.MenuText]                  = Brush("CommandBarTextActive");
            d[ResourceKeys.MenuTextInactive]          = Brush("CommandBarTextInactive");
            d[ResourceKeys.MenuItemHoveredBackground] = Brush("CommandBarMenuItemMouseOver");
            d[ResourceKeys.MenuItemHoveredText]       = Brush("CommandBarMenuItemMouseOver", foreground: true);

            // Document well — overflow button
            d[ResourceKeys.DocumentWellOverflowButtonDefaultGlyph]     = Brush("DocWellOverflowButtonGlyph");
            d[ResourceKeys.DocumentWellOverflowButtonHoveredBackground] = Brush("DocWellOverflowButtonMouseOverBackground");
            d[ResourceKeys.DocumentWellOverflowButtonHoveredBorder]     = Brush("DocWellOverflowButtonMouseOverBorder");
            d[ResourceKeys.DocumentWellOverflowButtonHoveredGlyph]      = Brush("DocWellOverflowButtonMouseOverGlyph");
            d[ResourceKeys.DocumentWellOverflowButtonPressedBackground] = Brush("DocWellOverflowButtonMouseDownBackground");
            d[ResourceKeys.DocumentWellOverflowButtonPressedBorder]     = Brush("DocWellOverflowButtonMouseDownBorder");
            d[ResourceKeys.DocumentWellOverflowButtonPressedGlyph]      = Brush("DocWellOverflowButtonMouseDownGlyph");

            // Document well — selected active/inactive tabs
            d[ResourceKeys.DocumentWellTabSelectedActiveBackground]   = Brush("FileTabSelectedBorder");
            d[ResourceKeys.DocumentWellTabSelectedActiveText]         = Brush("FileTabSelectedText");
            d[ResourceKeys.DocumentWellTabSelectedInactiveBackground] = Brush("FileTabInactiveBorder");
            d[ResourceKeys.DocumentWellTabSelectedInactiveText]       = Brush("FileTabInactiveText");
            d[ResourceKeys.DocumentWellTabUnselectedBackground]       = Brush("FileTabBackground");
            d[ResourceKeys.DocumentWellTabUnselectedText]             = Brush("FileTabText");
            d[ResourceKeys.DocumentWellTabUnselectedHoveredBackground]= Brush("FileTabHotBorder");
            d[ResourceKeys.DocumentWellTabUnselectedHoveredText]      = Brush("FileTabHotText");

            // Document well — close button glyphs
            d[ResourceKeys.DocumentWellTabButtonSelectedActiveGlyph]         = Brush("FileTabButtonSelectedActiveGlyph");
            d[ResourceKeys.DocumentWellTabButtonSelectedInactiveGlyph]       = Brush("FileTabButtonSelectedInactiveGlyph");
            d[ResourceKeys.DocumentWellTabButtonUnselectedTabHoveredGlyph]   = Brush("FileTabHotGlyph");

            // Tool window captions
            d[ResourceKeys.ToolWindowCaptionActiveBackground]   = Brush("TitleBarActiveBorder");
            d[ResourceKeys.ToolWindowCaptionActiveText]         = Brush("TitleBarActiveText");
            d[ResourceKeys.ToolWindowCaptionActiveGrip]         = Brush("TitleBarDragHandleActive");
            d[ResourceKeys.ToolWindowCaptionInactiveBackground] = Brush("TitleBarInactive");
            d[ResourceKeys.ToolWindowCaptionInactiveText]       = Brush("TitleBarInactiveText");
            d[ResourceKeys.ToolWindowCaptionInactiveGrip]       = Brush("TitleBarDragHandle");

            d[ResourceKeys.ToolWindowCaptionButtonActiveGlyph]   = Brush("ToolWindowButtonActiveGlyph");
            d[ResourceKeys.ToolWindowCaptionButtonInactiveGlyph] = Brush("ToolWindowButtonInactiveGlyph");

            d[ResourceKeys.ToolWindowCaptionButtonActiveHoveredBackground] = Brush("ToolWindowButtonHoverActive");
            d[ResourceKeys.ToolWindowCaptionButtonActiveHoveredBorder]     = Brush("ToolWindowButtonHoverActiveBorder");
            d[ResourceKeys.ToolWindowCaptionButtonActiveHoveredGlyph]      = Brush("ToolWindowButtonHoverActiveGlyph");
            d[ResourceKeys.ToolWindowCaptionButtonActivePressedBackground] = Brush("ToolWindowButtonDown");
            d[ResourceKeys.ToolWindowCaptionButtonActivePressedBorder]     = Brush("ToolWindowButtonDownBorder");
            d[ResourceKeys.ToolWindowCaptionButtonActivePressedGlyph]      = Brush("ToolWindowButtonDownActiveGlyph");

            d[ResourceKeys.ToolWindowCaptionButtonInactiveHoveredBackground] = Brush("ToolWindowButtonHoverInactive");
            d[ResourceKeys.ToolWindowCaptionButtonInactiveHoveredBorder]     = Brush("ToolWindowButtonHoverInactiveBorder");
            d[ResourceKeys.ToolWindowCaptionButtonInactiveHoveredGlyph]      = Brush("ToolWindowButtonHoverInactiveGlyph");
            d[ResourceKeys.ToolWindowCaptionButtonInactivePressedBackground] = Brush("ToolWindowButtonDown");
            d[ResourceKeys.ToolWindowCaptionButtonInactivePressedBorder]     = Brush("ToolWindowButtonDownBorder");
            d[ResourceKeys.ToolWindowCaptionButtonInactivePressedGlyph]      = Brush("ToolWindowButtonDownActiveGlyph");

            // Tool window tabs
            d[ResourceKeys.ToolWindowTabSelectedActiveBackground]    = Brush("ToolWindowTabSelectedTab");
            d[ResourceKeys.ToolWindowTabSelectedActiveText]          = Brush("ToolWindowTabSelectedActiveText");
            d[ResourceKeys.ToolWindowTabSelectedInactiveBackground]  = Brush("ToolWindowTabSelectedTab");
            d[ResourceKeys.ToolWindowTabSelectedInactiveText]        = Brush("ToolWindowTabSelectedText");
            d[ResourceKeys.ToolWindowTabUnselectedBackground]        = Brush("ToolWindowTabGradientBegin");
            d[ResourceKeys.ToolWindowTabUnselectedText]              = Brush("ToolWindowTabText");
            d[ResourceKeys.ToolWindowTabUnselectedHoveredBackground] = Brush("ToolWindowTabMouseOverBackgroundBegin");
            d[ResourceKeys.ToolWindowTabUnselectedHoveredText]       = Brush("ToolWindowTabMouseOverText");

            // Dock indicator / drop-zone
            d[ResourceKeys.DockingButtonForegroundBrush]     = Brush("DockTargetGlyphBorder");
            d[ResourceKeys.DockingButtonForegroundArrowBrush]= Brush("DockTargetGlyphArrow");
            d[ResourceKeys.PreviewBoxBorderBrush]            = Brush("DockTargetGlyphBorder");
            d[ResourceKeys.PreviewBoxBackgroundBrush]        = BrushWithAlpha("DockTargetGlyphBorder", 0x80);

            d[ResourceKeys.DockingButtonBackgroundBrush]    = Raw(0x20, 0x00, 0x00, 0x00);
            d[ResourceKeys.DockingButtonStarBorderBrush]    = Raw(0x40, 0x80, 0x80, 0x80);
            d[ResourceKeys.DockingButtonStarBackgroundBrush]= Raw(0x20, 0x00, 0x00, 0x00);
        }

        // ── Color helpers ─────────────────────────────────────────────────────

        private SolidColorBrush Brush(string key, bool foreground = false)
            => new SolidColorBrush(GetColor(key, foreground));

        private SolidColorBrush BrushWithAlpha(string key, byte alpha)
        {
            var c = GetColor(key);
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }

        private static SolidColorBrush Raw(byte a, byte r, byte g, byte b)
            => new SolidColorBrush(Color.FromArgb(a, r, g, b));

        private Color GetColor(string name, bool foreground = false)
        {
            var colorEl = _env.Elements("Color")
                .FirstOrDefault(c => (string)c.Attribute("Name") == name);
            var src = (string)colorEl
                ?.Element(foreground ? "Foreground" : "Background")?.Attribute("Source");
            if (src == null || src.Length != 8) return Colors.Transparent;
            return Color.FromArgb(
                Convert.ToByte(src.Substring(0, 2), 16),
                Convert.ToByte(src.Substring(2, 2), 16),
                Convert.ToByte(src.Substring(4, 2), 16),
                Convert.ToByte(src.Substring(6, 2), 16));
        }
    }
}
