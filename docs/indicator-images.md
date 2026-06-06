# Drop Indicator Visual System — Analysis & Porting Plan

## Overview

DockPanelSuite's VS2012 theme implements drop indicators using a **mask-based, theme-driven
compositing pipeline**. Masks are simple black-and-white PNG bitmaps (pure geometry); at
runtime the theme engine composites them with colours from an XML theme file (a VS `.vstheme`
exported colour palette) to produce the final coloured indicator images.

Our port will replicate this pipeline using **SVG path data** (instead of PNG masks) and
**WinUI geometry** (instead of GDI+ compositing), driven by the same colour keys from a
`IndicatorPalette` class that mirrors `DockPanelColorPalette.DockTargetPalette`.

---

## Mask files (32×32 RGB PNGs, black bg = transparent, white shape = coloured)

### Arrow masks — outer edge indicators

| File | Shape | SVG path (32×32 viewBox) |
|------|-------|--------------------------|
| `MaskArrowBottom` | White downward triangle at bottom-centre | `M 6,8 L 26,8 L 16,26 Z` |
| `MaskArrowTop`    | White upward triangle at top-centre      | `M 6,24 L 26,24 L 16,6 Z` |
| `MaskArrowLeft`   | White leftward triangle at left-centre   | `M 24,6 L 24,26 L 6,16 Z` |
| `MaskArrowRight`  | White rightward triangle at right-centre | `M 8,6 L 8,26 L 26,16 Z` |

Colour applied: `DockTarget.GlyphArrow`.

### Core masks — miniature "after-drop" layout shown on each compass button

| File | Shape | Meaning | SVG path (32×32) |
|------|-------|---------|-----------------|
| `MaskCoreCenter` | White square 20×20 centred | Tab join — full area | `M 6,6 h 20 v 20 h -20 Z` |
| `MaskCoreBottom` | White bar 20×6 at bottom   | New pane fills bottom | `M 6,22 h 20 v 6 h -20 Z` |
| `MaskCoreTop`    | White bar 20×6 at top      | New pane fills top | `M 6,4 h 20 v 6 h -20 Z` |
| `MaskCoreLeft`   | White bar 6×20 at left     | New pane fills left | `M 4,6 h 6 v 20 h -6 Z` |
| `MaskCoreRight`  | White bar 6×20 at right    | New pane fills right | `M 22,6 h 6 v 20 h -6 Z` |

Colour applied: `DockTarget.GlyphBackground`.

### Window masks — "existing window" context shape (behind the Core)

| File | Shape | Meaning | SVG path (32×32) |
|------|-------|---------|-----------------|
| `MaskWindowCenter` | Large white square 22×22 | Existing doc fills pane | `M 5,5 h 22 v 22 h -22 Z` |
| `MaskWindowBottom` | Wide bar at top 22×14    | Existing doc stays top | `M 5,5 h 22 v 14 h -22 Z` |
| `MaskWindowTop`    | Wide bar at bottom 22×14 | Existing doc stays bottom | `M 5,13 h 22 v 14 h -22 Z` |
| `MaskWindowLeft`   | Tall bar at right 14×22  | Existing doc stays right | `M 13,5 h 14 v 22 h -14 Z` |
| `MaskWindowRight`  | Tall bar at left 14×22   | Existing doc stays left | `M 5,5 h 14 v 22 h -14 Z` |

Colour applied: `DockTarget.GlyphBorder`.

### Dock background masks

| File | Size | Shape | Use |
|------|------|-------|-----|
| `MaskDock` | 32×32 | White rounded square with slight bevel | Per-button background (all 5 inner buttons) |
| `MaskDockFive` | 112×112 | White plus/cross on black | Cluster background shape for the 5-zone compass |

### Hit-test maps (inner 5-zone compass only)

| File | Size | Content |
|------|------|---------|
| `Dockindicator_PaneDiamond_Hotspot` | 112×112 RGB | Coloured zones: red=Top, green=Left, dark=Center, darkgrey=Right, blue=Bottom |
| `DockIndicator_PaneDiamond_HotspotIndex` | 3×3 RGB | 3×3 lookup: pixel at (col,row) maps zone index → `DockStyle` enum value |

---

## Compositing pipeline (`ImageService.cs` + `IImageService.cs`)

### `GetImage(mask, glyphColour, background, border?)` — simple icon

Used for: tab close buttons, tool-window caption buttons (`MaskTabClose`, `MaskToolWindowClose`, etc.).

1. Create solid bitmap filled with `glyphColour`.
2. Per pixel: `output.alpha = mask.red` (white mask pixel = fully opaque glyph).
3. Composite over `background` fill rect with optional 1px `border` frame.

**SVG/WinUI equivalent**:
```xml
<Border Background="background" BorderBrush="border" BorderThickness="1">
  <Path Fill="glyphColour" Data="M …mask shape…" />
</Border>
```

### `GetDockIcon(maskArrow, layerArrow, maskWindow, layerWindow, maskBack, background, maskCore?, layerCore?, separator?)` — compass button

For each of the 5 inner compass buttons, four layers are composited (back→window→core→arrow):

| Layer | Mask | Colour | Always? |
|-------|------|--------|---------|
| Background | `MaskDock` (rounded square) | `ButtonBackground` | Yes |
| Window rect | `MaskWindow*` | `GlyphBorder` (lighter) | Yes |
| Core rect | `MaskCore*` | `GlyphBackground` (darker) | Only when `GlyphBackground ≠ ButtonBackground` |
| Arrow | `MaskArrow*` | `GlyphArrow` | Only for directional buttons (not Center) |
| Separator | horizontal/vertical line at centre | `ButtonBorder` | Yes |

Visual result for the **Left** button:
```
┌──────────────────────────────────┐
│  ButtonBackground                │
│  ┌───────────┬──────────────┐    │
│  │ CoreLeft  │  WindowLeft  │    │  ← GlyphBackground | GlyphBorder
│  │ (left bar)│ (right block)│    │
│  └───────────┴──────────────┘    │
│  ◀ GlyphArrow triangle (left)   │
└──────────────────────────────────┘
```

### `GetFiveBackground(maskDockFive, innerBorder, outerBorder)` — 112×112 cluster background

Composites the `MaskDockFive` (white cross) with:
- `innerBorder` (= `DockTarget.Background`) as the cross fill
- `outerBorder` (= `DockTarget.Border`) as a 1px outline around each arm

### `CombineFive(five, bottom, center, left, right, top)` — assemble the compass

Places 5 individual 32×32 button images into the 112×112 cross at fixed offsets:

| Button | Position in 112×112 (x, y) |
|--------|---------------------------|
| Center | (40, 40) |
| Top    | (40,  4) |
| Bottom | (40, 76) |
| Left   | ( 4, 40) |
| Right  | (76, 40) |

Button size 32×32; gap between buttons = 40 - 32 - 4 = 4px.

---

## Colour palette (`DockTargetPalette` keys → VS colour tokens)

Read from VS `.vstheme` XML, `Category Name="Environment"`:

| Palette key | VS colour token | VS2012 Dark | VS2012 Light | Role |
|-------------|----------------|-------------|--------------|------|
| `Background` | `DockTargetBackground` | `#1B1B1C` | `#F5F5F5` | Cross arm fill |
| `Border` | `DockTargetBorder` | `#3F3F46` | `#C8C8C8` | Cluster outer border |
| `ButtonBackground` | `DockTargetButtonBackgroundBegin` | `#2D2D30` | `#EEEEF2` | Per-button bg |
| `ButtonBorder` | `DockTargetButtonBorder` | `#3F3F46` | `#CCCEDB` | Separator lines |
| `GlyphBackground` | `DockTargetGlyphBackgroundBegin` | `#1E1E1E` | `#E5EEF9` | Core rect (dark area) |
| `GlyphArrow` | `DockTargetGlyphArrow` | `#B4B4B4` | `#717171` | Arrow triangle fill |
| `GlyphBorder` | `DockTargetGlyphBorder` | `#D4D4D4` | `#222222` | Window rect fill (light) |

---

## SVG `MaskDockFive` cross path (112×112 viewBox)

The cross shape from `MaskDockFive.png` (white plus on black, arm width = 36px):

```
M 36,0 L 76,0 L 76,36 L 112,36 L 112,76
L 76,76 L 76,112 L 36,112 L 36,76 L 0,76
L 0,36 L 36,36 Z
```

The four corner squares (0,0)–(36,36), (76,0)–(112,36), etc. are the black (cut-out) areas.

---

## Implementation plan for UnoDock

### Files

```
src/UnoDock/Controls/IndicatorPalette.cs   — colour keys + VS2012 Dark/Light defaults
src/UnoDock/Controls/IndicatorPaths.cs     — all SVG path strings (masks as geometry)
src/UnoDock/Controls/CompassButton.cs      — single button: path layers + palette colours
src/UnoDock/Controls/CompassOverlay.cs     — 9-zone overlay (refactored to use above)
```

### `IndicatorPaths.cs` — SVG strings per mask

```csharp
internal static class IndicatorPaths
{
    // Arrow masks (32×32 viewBox)
    public const string ArrowDown  = "M 6,8 L 26,8 L 16,26 Z";
    public const string ArrowUp    = "M 6,24 L 26,24 L 16,6 Z";
    public const string ArrowLeft  = "M 24,6 L 24,26 L 6,16 Z";
    public const string ArrowRight = "M 8,6 L 8,26 L 26,16 Z";

    // Core masks (32×32 viewBox) — after-drop layout mini-preview
    public const string CoreCenter = "M 6,6 h 20 v 20 h -20 Z";
    public const string CoreBottom = "M 6,22 h 20 v 6 h -20 Z";
    public const string CoreTop    = "M 6,4 h 20 v 6 h -20 Z";
    public const string CoreLeft   = "M 4,6 h 6 v 20 h -6 Z";
    public const string CoreRight  = "M 22,6 h 6 v 20 h -6 Z";

    // Window masks (32×32 viewBox) — existing-window context
    public const string WinCenter = "M 5,5 h 22 v 22 h -22 Z";
    public const string WinBottom = "M 5,5 h 22 v 14 h -22 Z";
    public const string WinTop    = "M 5,13 h 22 v 14 h -22 Z";
    public const string WinLeft   = "M 13,5 h 14 v 22 h -14 Z";
    public const string WinRight  = "M 5,5 h 14 v 22 h -14 Z";

    // Cluster cross background (112×112 viewBox)
    public const string DockFive  =
        "M 36,0 L 76,0 L 76,36 L 112,36 L 112,76 " +
        "L 76,76 L 76,112 L 36,112 L 36,76 L 0,76 " +
        "L 0,36 L 36,36 Z";
}
```

### `CompassButton.cs` — renders one compass button

Each button is a WinUI `Canvas` (32×32) with layers:
1. `Border` (ButtonBackground + ButtonBorder)
2. `Path` for Window mask (GlyphBorder colour)
3. `Path` for Core mask (GlyphBackground colour) — if different from button bg
4. `Path` for Arrow mask (GlyphArrow colour) — directional only
5. Separator `Line` at centre (ButtonBorder colour)

On hover: ButtonBackground → hover blue (`#007ACC`), all glyph colours → white.

### `CompassOverlay.cs` — assembles the 9-zone overlay

- **5 inner buttons** arranged in the 3×3 cross grid using `MaskDockFive` positions
- **4 outer arrows** at manager edges
- **Cross background** Border clipped to `DockFive` path
- **2 preview rectangles** (inner/outer, distinct colours)

### Theme XML wiring (future)

The `IndicatorPalette` can be populated from a VS `.vstheme` XML exactly as
`VS2012PaletteFactory` does, mapping the same `DockTarget*` token names.
This allows seamless theme switching (Dark/Light/Blue/Custom) without code changes.
