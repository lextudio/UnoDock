# Visual Studio `.vstheme` File Format

Visual Studio ships its IDE color palette as `.vstheme` files.  DockPanel Suite
bundles the VS 2013 Blue, Dark, and Light palettes (GZIP-compressed) and reads
them at runtime to drive its `DockPanelColorPalette`.  UnoDock follows the same
approach: at theme construction time the compressed XML is decompressed,
parsed, and converted into a WinUI `ResourceDictionary` of `SolidColorBrush`
entries.

---

## 1 · Physical format

| Layer | Detail |
|---|---|
| File extension | `.vstheme` (often distributed as `.vstheme.gz`) |
| Compression | GZip (`System.IO.Compression.GZipStream`) |
| Payload | UTF-8 XML |

Decompressed sizes for the VS 2013 palette files:

| File | Compressed | Decompressed |
|---|---|---|
| `vs2013blue.vstheme.gz` | ~15 KB | ~187 KB |
| `vs2013dark.vstheme.gz` | ~19 KB | ~228 KB |
| `vs2013light.vstheme.gz` | ~14 KB | ~186 KB |

---

## 2 · XML schema

```xml
<Themes>
  <Theme Name="Blue (1)" GUID="{0454db5f-…}">
    <Category Name="Environment" GUID="{624ed9c3-…}">
      <Color Name="ToolWindowBackground">
        <Background Type="CT_RAW" Source="FFFFFFFF" />
        <!-- Foreground is omitted when not applicable -->
      </Color>
      <Color Name="TitleBarActive">
        <Background Type="CT_RAW" Source="FFFFF29D" />
        <Foreground Type="CT_RAW" Source="FF000000" />
      </Color>
      …
    </Category>
    <Category Name="Cider" GUID="{92d153ee-…}">…</Category>
    <!-- ~30 more categories (CodeLens, SearchControl, TeamExplorer, …) -->
  </Theme>
</Themes>
```

Key observations:

* There is exactly **one `<Theme>`** element per file.
* Each `<Category>` groups logically related colors.
* `<Background>` and `<Foreground>` are each optional sub-elements of `<Color>`.
* The `Type` attribute is always `CT_RAW`; other types existed in older VS
  versions but are not present in VS 2013 files.

### 2.1 · Color encoding

The `Source` attribute holds an **8-digit upper-case hex string** in
**ARGB order**:

```
Source = "AARRGGBB"
         │└──────── RGB components (standard order, NOT BGR)
         └───────── Alpha channel (FF = fully opaque)
```

Despite the label "BGR" sometimes seen in documentation of the format, the
`ColorTranslator.FromHtml($"#{source}")` call in DockPanel Suite's
`VS2012PaletteFactory` interprets the value directly as ARGB — no byte-swap
is performed.  All UnoDock parsing follows the same convention:

```csharp
// source = "FFFFF29D"
byte a = Convert.ToByte(source[0..2], 16);  // 0xFF
byte r = Convert.ToByte(source[2..4], 16);  // 0xFF
byte g = Convert.ToByte(source[4..6], 16);  // 0xF2
byte b = Convert.ToByte(source[6..8], 16);  // 0x9D
var color = Color.FromArgb(a, r, g, b);
```

---

## 3 · Relevant categories

| Category | Relevant content |
|---|---|
| **Environment** | All tool-window, document-tab, title-bar, auto-hide, and dock-target colors — the primary source for UnoDock |
| **Cider** | Blend/XAML Designer UI colors (not used by UnoDock) |
| Other categories | Diagnostics, CodeLens, TeamExplorer, etc. — not used by UnoDock |

---

## 4 · Environment color keys used by UnoDock

The table below lists every vstheme `Environment` key consumed by
`VsThemePaletteFactory` and the UnoDock `ResourceKeys` constant (brush key
`UnoDock_VS2013_<Suffix>`) it populates, together with the resolved hex color
for each of the three built-in variants.

> Colors are shown as `#AARRGGBB`.  Values marked **hardcoded** are not present
> in the vstheme and are set by the factory directly.

### 4.1 · General / frame

| UnoDock key suffix | vstheme key | Blue | Dark | Light |
|---|---|---|---|---|
| `Background` | `AutoHideTabBackgroundBegin` | `#FF293955` | `#FF2D2D30` | `#FFEEEEF2` |
| `PanelBorderBrush` | `ToolWindowBorder` | `#FF8E9BBC` | `#FF3F3F46` | `#FFCCCEDB` |
| `ContentBackground` | `ToolWindowBackground` | `#FFFFFFFF` | `#FF252526` | `#FFF5F5F5` |
| `ResizerBackground` | `AutoHideTabBackgroundBegin` | `#FF293955` | `#FF2D2D30` | `#FFEEEEF2` |
| `CloseButtonForeground` | `ToolWindowButtonInactiveGlyph` | `#FFCED4DD` | `#FFF1F1F1` | `#FF1E1E1E` |

### 4.2 · Auto-hide strip

| UnoDock key suffix | vstheme key | Blue | Dark | Light |
|---|---|---|---|---|
| `AutoHideTabDefaultBackground` | `AutoHideTabBackgroundBegin` | `#FF293955` | `#FF2D2D30` | `#FFEEEEF2` |
| `AutoHideTabDefaultBorder` | `AutoHideTabBorder` | `#FF465A7D` | `#FF3F3F46` | `#FFCCCEDB` |
| `AutoHideTabDefaultText` | `AutoHideTabText` | `#FFFFFFFF` | `#FFD0D0D0` | `#FF444444` |
| `AutoHideTabHoveredBackground` | `AutoHideTabMouseOverBackgroundBegin` | `#FF293955` | `#FF2D2D30` | `#FFEEEEF2` |
| `AutoHideTabHoveredBorder` | `AutoHideTabMouseOverBorder` | `#FF9BA7B7` | `#FF007ACC` | `#FF007ACC` |
| `AutoHideTabHoveredText` | `AutoHideTabMouseOverText` | `#FFFFFFFF` | `#FF0097FB` | `#FF0E70C0` |

### 4.3 · Tab bar (shared text / border)

| UnoDock key suffix | vstheme key | Blue | Dark | Light |
|---|---|---|---|---|
| `TabBarBackground` | `AutoHideTabBackgroundBegin` | `#FF293955` | `#FF2D2D30` | `#FFEEEEF2` |
| `TabBarBorderBrush` | `AutoHideTabBorder` | `#FF465A7D` | `#FF3F3F46` | `#FFCCCEDB` |
| `TabBorderBrush` | `AutoHideTabBorder` | `#FF465A7D` | `#FF3F3F46` | `#FFCCCEDB` |
| `TabText` | `AutoHideTabText` | `#FFFFFFFF` | `#FFD0D0D0` | `#FF444444` |

### 4.4 · Document well (file) tabs

| UnoDock key suffix | vstheme key | Blue | Dark | Light |
|---|---|---|---|---|
| `DocumentWellTabSelectedActiveBackground` | `FileTabSelectedBorder` | `#FFFFF29D` | `#FF007ACC` | `#FF007ACC` |
| `DocumentWellTabSelectedActiveText` | `FileTabSelectedText` | `#FF000000` | `#FFFFFFFF` | `#FFFFFFFF` |
| `DocumentWellTabSelectedInactiveBackground` | `FileTabInactiveBorder` | `#FF4D6082` | `#FF3F3F46` | `#FFCCCEDB` |
| `DocumentWellTabSelectedInactiveText` | `FileTabInactiveText` | `#FFFFFFFF` | `#FFF1F1F1` | `#FF717171` |
| `DocumentWellTabUnselectedBackground` | `FileTabBackground` | `#FF364E6F` | `#FF2D2D30` | `#FFEEEEF2` |
| `DocumentWellTabUnselectedText` | `FileTabText` | `#FFFFFFFF` | `#FFF1F1F1` | `#FF1E1E1E` |
| `DocumentWellTabUnselectedHoveredBackground` | `FileTabHotBorder` | `#FF5B7199` | `#FF1C97EA` | `#FF1C97EA` |
| `DocumentWellTabUnselectedHoveredText` | `FileTabHotText` | `#FFFFFFFF` | `#FFFFFFFF` | `#FFFFFFFF` |
| `DocumentWellTabButtonSelectedActiveGlyph` | `FileTabButtonSelectedActiveGlyph` | `#FF75633D` | `#FFD0E6F5` | `#FFD0E6F5` |
| `DocumentWellTabButtonSelectedInactiveGlyph` | `FileTabButtonSelectedInactiveGlyph` | `#FFCED4DD` | `#FF6D6D70` | `#FF6D6D70` |
| `DocumentWellTabButtonUnselectedTabHoveredGlyph` | `FileTabHotGlyph` | `#FFCED4DD` | `#FFD0E6F5` | `#FFD0E6F5` |

### 4.5 · Tool window captions (title bar)

| UnoDock key suffix | vstheme key | Blue | Dark | Light |
|---|---|---|---|---|
| `ToolWindowCaptionActiveBackground` | `TitleBarActiveBorder` | `#FFFFF29D` | `#FF007ACC` | `#FF007ACC` |
| `ToolWindowCaptionActiveText` | `TitleBarActiveText` | `#FF000000` | `#FFFFFFFF` | `#FFFFFFFF` |
| `ToolWindowCaptionActiveGrip` | `TitleBarDragHandleActive` | `#FFFFF29D` | `#FF59A8DE` | `#FF59A8DE` |
| `ToolWindowCaptionInactiveBackground` | `TitleBarInactive` | `#FF4D6082` | `#FF2D2D30` | `#FFEEEEF2` |
| `ToolWindowCaptionInactiveText` | `TitleBarInactiveText` | `#FFFFFFFF` | `#FFD0D0D0` | `#FF444444` |
| `ToolWindowCaptionInactiveGrip` | `TitleBarDragHandle` | `#FF4D6082` | `#FF46464A` | `#FF999999` |
| `ToolWindowCaptionButtonActiveGlyph` | `ToolWindowButtonActiveGlyph` | `#FF75633D` | `#FFFFFFFF` | `#FFFFFFFF` |
| `ToolWindowCaptionButtonInactiveGlyph` | `ToolWindowButtonInactiveGlyph` | `#FFCED4DD` | `#FFF1F1F1` | `#FF1E1E1E` |
| `ToolWindowCaptionButtonActiveHoveredBackground` | `ToolWindowButtonHoverActive` | `#FFFFFCF4` | `#FF52B0EF` | `#FF52B0EF` |
| `ToolWindowCaptionButtonActiveHoveredBorder` | `ToolWindowButtonHoverActiveBorder` | `#FFE5C365` | `#FF52B0EF` | `#FF52B0EF` |
| `ToolWindowCaptionButtonActiveHoveredGlyph` | `ToolWindowButtonHoverActiveGlyph` | `#FF000000` | `#FFFFFFFF` | `#FFFFFFFF` |
| `ToolWindowCaptionButtonActivePressedBackground` | `ToolWindowButtonDown` | `#FFFFE8A6` | `#FF0E6198` | `#FF0E6198` |
| `ToolWindowCaptionButtonActivePressedBorder` | `ToolWindowButtonDownBorder` | `#FFE5C365` | `#FF0E6198` | `#FF0E6198` |
| `ToolWindowCaptionButtonActivePressedGlyph` | `ToolWindowButtonDownActiveGlyph` | `#FF000000` | `#FFFFFFFF` | `#FFFFFFFF` |
| `ToolWindowCaptionButtonInactiveHoveredBackground` | `ToolWindowButtonHoverInactive` | `#FFFFFCF4` | `#FF393939` | `#FFF7F7F9` |
| `ToolWindowCaptionButtonInactiveHoveredBorder` | `ToolWindowButtonHoverInactiveBorder` | `#FFE5C365` | `#FF393939` | `#FFF7F7F9` |
| `ToolWindowCaptionButtonInactiveHoveredGlyph` | `ToolWindowButtonHoverInactiveGlyph` | `#FF000000` | `#FFF1F1F1` | `#FF717171` |
| `ToolWindowCaptionButtonInactivePressedBackground` | `ToolWindowButtonDown` | `#FFFFE8A6` | `#FF0E6198` | `#FF0E6198` |
| `ToolWindowCaptionButtonInactivePressedBorder` | `ToolWindowButtonDownBorder` | `#FFE5C365` | `#FF0E6198` | `#FF0E6198` |
| `ToolWindowCaptionButtonInactivePressedGlyph` | `ToolWindowButtonDownActiveGlyph` | `#FF000000` | `#FFFFFFFF` | `#FFFFFFFF` |

### 4.6 · Tool window tabs

| UnoDock key suffix | vstheme key | Blue | Dark | Light |
|---|---|---|---|---|
| `ToolWindowTabSelectedActiveBackground` | `ToolWindowTabSelectedTab` | `#FFFFFFFF` | `#FF252526` | `#FFF5F5F5` |
| `ToolWindowTabSelectedActiveText` | `ToolWindowTabSelectedActiveText` | `#FF000000` | `#FF0097FB` | `#FF0E70C0` |
| `ToolWindowTabSelectedInactiveBackground` | `ToolWindowTabSelectedTab` | `#FFFFFFFF` | `#FF252526` | `#FFF5F5F5` |
| `ToolWindowTabSelectedInactiveText` | `ToolWindowTabSelectedText` | `#FF000000` | `#FF0097FB` | `#FF0E70C0` |
| `ToolWindowTabUnselectedBackground` | `ToolWindowTabGradientBegin` | `#FF4D6082` | `#FF2D2D30` | `#FFEEEEF2` |
| `ToolWindowTabUnselectedText` | `ToolWindowTabText` | `#FFFFFFFF` | `#FFD0D0D0` | `#FF444444` |
| `ToolWindowTabUnselectedHoveredBackground` | `ToolWindowTabMouseOverBackgroundBegin` | `#FF4B5C74` | `#FF3E3E40` | `#FFC9DEF5` |
| `ToolWindowTabUnselectedHoveredText` | `ToolWindowTabMouseOverText` | `#FFFFFFFF` | `#FF55AAFF` | `#FF1E1E1E` |

### 4.7 · Dock indicator / drop-zone (partial vstheme derivation)

The docking compass and preview-box indicators are drawn with XAML `Path`
geometry in `OverlayButtons.xaml`.  The vstheme `DockTarget*` keys cover a
WinForms overlay popup which has a different design; only the glyph-arrow and
border colors are reused.

| UnoDock key suffix | Source | Blue | Dark | Light |
|---|---|---|---|---|
| `ControlAccentBrush` | `FileTabSelectedBorder` | `#FFFFF29D` | `#FF007ACC` | `#FF007ACC` |
| `DockingButtonForegroundArrowBrush` | `DockTargetGlyphArrow` | `#FF445879` | `#FFF1F1F1` | `#FF1E1E1E` |
| `DockingButtonForegroundBrush` | `DockTargetGlyphBorder` | `#FF445879` | `#FF007ACC` | `#FF007ACC` |
| `DockingButtonBackgroundBrush` | **hardcoded** | `#20000000` | `#20000000` | `#20000000` |
| `DockingButtonStarBorderBrush` | **hardcoded** | `#40808080` | `#40808080` | `#40808080` |
| `DockingButtonStarBackgroundBrush` | **hardcoded** | `#20000000` | `#4C000000` | `#20000000` |
| `PreviewBoxBorderBrush` | `DockTargetGlyphBorder` | `#FF445879` | `#FF007ACC` | `#FF007ACC` |
| `PreviewBoxBackgroundBrush` | `DockTargetGlyphBorder` (80 alpha) | `#80445879` | `#80007ACC` | `#80007ACC` |

> **Note on Blue ControlAccentBrush:** VS 2013 Blue's `FileTabSelectedBorder`
> is `#FFFFF29D` (yellow), which is used for the active tool-window caption and
> selected document tab.  The docking compass uses the same color to match the
> accent.  The original hand-coded XAML used `#FF1BA1E2` (sky blue) instead —
> a color that does not appear in the Blue vstheme's Environment category.
> The vstheme-derived value (`#FFFFF29D`) is the authoritative choice.

---

## 5 · Color values that changed when migrating from hand-coded XAML

The following colors differ between the previous hand-coded brush XAML files
and the vstheme-derived values.  All changes bring UnoDock in line with the
official VS 2013 palette as used by DockPanel Suite.

| Variant | UnoDock key suffix | Old (hand-coded) | New (from vstheme) |
|---|---|---|---|
| Blue | `DocumentWellTabSelectedInactiveBackground` | `#FFFEFEFE` | `#FF4D6082` |
| Blue | `ControlAccentBrush` | `#FF1BA1E2` | `#FFFFF29D` |
| Blue | `DockingButtonForegroundBrush` | `#FF1BA1E2` | `#FF445879` |
| Blue | `DockingButtonForegroundArrowBrush` | `#FF000000` | `#FF445879` |
| Blue | `PreviewBoxBorderBrush` | `#FF1BA1E2` | `#FF445879` |
| Blue | `PreviewBoxBackgroundBrush` | `#801BA1E2` | `#80445879` |
| Dark | `DocumentWellTabSelectedInactiveBackground` | `#FF68217A` | `#FF3F3F46` |
| Dark | `ContentBackground` | `#FF1E1E1E` | `#FF252526` |
| Dark | `DockingButtonStarBackgroundBrush` | `#4C000000` | `#20000000` |

---

## 6 · DockPanel Suite reference mapping

This table cross-references DockPanel Suite's `VS2012PaletteFactory`
(`WeifenLuo.WinFormsUI.ThemeVS2012`) to show the authoritative origin of each
vstheme key used by UnoDock.

| DockPanel Suite palette property | vstheme key |
|---|---|
| `AutoHideStripDefault.Background` | `AutoHideTabBackgroundBegin` |
| `AutoHideStripDefault.Border` | `AutoHideTabBorder` |
| `AutoHideStripDefault.Text` | `AutoHideTabText` |
| `AutoHideStripHovered.Background` | `AutoHideTabMouseOverBackgroundBegin` |
| `AutoHideStripHovered.Border` | `AutoHideTabMouseOverBorder` |
| `AutoHideStripHovered.Text` | `AutoHideTabMouseOverText` |
| `TabSelectedActive.Background` | `FileTabSelectedBorder` |
| `TabSelectedActive.Button` | `FileTabButtonSelectedActiveGlyph` |
| `TabSelectedActive.Text` | `FileTabSelectedText` |
| `TabSelectedInactive.Background` | `FileTabInactiveBorder` |
| `TabSelectedInactive.Button` | `FileTabButtonSelectedInactiveGlyph` |
| `TabSelectedInactive.Text` | `FileTabInactiveText` |
| `TabUnselected.Background` | `FileTabBackground` |
| `TabUnselected.Text` | `FileTabText` |
| `TabUnselectedHovered.Background` | `FileTabHotBorder` |
| `TabUnselectedHovered.Button` | `FileTabHotGlyph` |
| `TabUnselectedHovered.Text` | `FileTabHotText` |
| `ToolWindowCaptionActive.Background` | `TitleBarActiveBorder` |
| `ToolWindowCaptionActive.Button` | `ToolWindowButtonActiveGlyph` |
| `ToolWindowCaptionActive.Grip` | `TitleBarDragHandleActive` |
| `ToolWindowCaptionActive.Text` | `TitleBarActiveText` |
| `ToolWindowCaptionInactive.Background` | `TitleBarInactive` |
| `ToolWindowCaptionInactive.Button` | `ToolWindowButtonInactiveGlyph` |
| `ToolWindowCaptionInactive.Grip` | `TitleBarDragHandle` |
| `ToolWindowCaptionInactive.Text` | `TitleBarInactiveText` |
| `ToolWindowCaptionButtonActiveHovered.Background` | `ToolWindowButtonHoverActive` |
| `ToolWindowCaptionButtonActiveHovered.Border` | `ToolWindowButtonHoverActiveBorder` |
| `ToolWindowCaptionButtonActiveHovered.Glyph` | `ToolWindowButtonHoverActiveGlyph` |
| `ToolWindowCaptionButtonPressed.Background` | `ToolWindowButtonDown` |
| `ToolWindowCaptionButtonPressed.Border` | `ToolWindowButtonDownBorder` |
| `ToolWindowCaptionButtonPressed.Glyph` | `ToolWindowButtonDownActiveGlyph` |
| `ToolWindowCaptionButtonInactiveHovered.Background` | `ToolWindowButtonHoverInactive` |
| `ToolWindowCaptionButtonInactiveHovered.Border` | `ToolWindowButtonHoverInactiveBorder` |
| `ToolWindowCaptionButtonInactiveHovered.Glyph` | `ToolWindowButtonHoverInactiveGlyph` |
| `ToolWindowTabSelectedActive.Background` | `ToolWindowTabSelectedTab` |
| `ToolWindowTabSelectedActive.Text` | `ToolWindowTabSelectedActiveText` |
| `ToolWindowTabSelectedInactive.Background` | `ToolWindowTabSelectedTab` |
| `ToolWindowTabSelectedInactive.Text` | `ToolWindowTabSelectedText` |
| `ToolWindowTabUnselected.Background` | `ToolWindowTabGradientBegin` |
| `ToolWindowTabUnselected.Text` | `ToolWindowTabText` |
| `ToolWindowTabUnselectedHovered.Background` | `ToolWindowTabMouseOverBackgroundBegin` |
| `ToolWindowTabUnselectedHovered.Text` | `ToolWindowTabMouseOverText` |
| `ToolWindowSeparator` | `ToolWindowTabSeparator` |
| `ToolWindowBorder` | `ToolWindowBorder` |
| `DockTarget.Background` | `DockTargetBackground` |
| `DockTarget.Border` | `DockTargetBorder` |
| `DockTarget.ButtonBackground` | `DockTargetButtonBackgroundBegin` |
| `DockTarget.ButtonBorder` | `DockTargetButtonBorder` |
| `DockTarget.GlyphBackground` | `DockTargetGlyphBackgroundBegin` |
| `DockTarget.GlyphArrow` | `DockTargetGlyphArrow` |
| `DockTarget.GlyphBorder` | `DockTargetGlyphBorder` |

---

## 7 · Implementation in UnoDock

### 7.1 · Files

| Path | Role |
|---|---|
| `UnoDock.Themes.VS2013/Resources/vs2013blue.vstheme.gz` | Embedded resource — Blue palette |
| `UnoDock.Themes.VS2013/Resources/vs2013dark.vstheme.gz` | Embedded resource — Dark palette |
| `UnoDock.Themes.VS2013/Resources/vs2013light.vstheme.gz` | Embedded resource — Light palette |
| `UnoDock.Themes.VS2013/VsThemePaletteFactory.cs` | Reads vstheme bytes → `ResourceDictionary` |
| `UnoDock/Themes/Theme.cs` | Adds `virtual GetResourceDictionary()` |
| `UnoDock/Controls/DockingManager.cs` | Calls `GetResourceDictionary()` instead of creating from Uri |

### 7.2 · Parsing pseudocode

```csharp
// 1. Decompress
using var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
var xml = XDocument.Load(gz);

// 2. Locate Environment category
var env = xml.Root
    .Element("Theme")
    .Elements("Category")
    .First(c => c.Attribute("Name").Value == "Environment");

// 3. Helper: read one color
Color Get(string name) {
    var src = env.Elements("Color")
        .FirstOrDefault(c => c.Attribute("Name").Value == name)
        ?.Element("Background")?.Attribute("Source")?.Value;
    if (src == null) return Colors.Transparent;
    return Color.FromArgb(
        Convert.ToByte(src[0..2], 16),
        Convert.ToByte(src[2..4], 16),
        Convert.ToByte(src[4..6], 16),
        Convert.ToByte(src[6..8], 16));
}

// 4. Build ResourceDictionary
var dict = new ResourceDictionary();
dict[ResourceKeys.Background] = new SolidColorBrush(Get("AutoHideTabBackgroundBegin"));
// … (see §4 mapping table for all entries) …

// 5. Merge style sheets (no colors — they use StaticResource)
dict.MergedDictionaries.Add(new ResourceDictionary {
    Source = new Uri("ms-appx:///UnoDock.Themes.VS2013/Themes/Generic.xaml")
});
dict.MergedDictionaries.Add(new ResourceDictionary {
    Source = new Uri("ms-appx:///UnoDock.Themes.VS2013/OverlayButtons.xaml")
});
```

### 7.3 · Theme class changes

```csharp
// Theme.cs (base)
public virtual ResourceDictionary GetResourceDictionary()
    => new ResourceDictionary { Source = GetResourceUri() };

// Vs2013BlueTheme.cs
public override ResourceDictionary GetResourceDictionary()
    => VsThemePaletteFactory.BuildDictionary(Resources.vs2013blue_vstheme);
```

DockingManager calls `theme.GetResourceDictionary()` everywhere it previously
called `new ResourceDictionary { Source = theme.GetResourceUri() }`.
