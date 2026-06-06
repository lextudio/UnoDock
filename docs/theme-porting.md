# Theme Porting Guide: AvalonDock → UnoDock

This document explains how to port a theme from the AvalonDock WPF library (in
`externals/AvalonDock`) to UnoDock (WinUI / Uno Platform).

## Status

| Theme | Variants | Ported |
|---|---|---|
| VS2013 | Blue, Dark, Light | ✅ `UnoDock.Themes.VS2013` |
| VS2010 | (single) | ✅ `UnoDock.Themes.VS2010` |
| Aero | (single) | ❌ |
| Metro | (single) | ❌ |
| Expression | Dark, Light | ❌ |
| Arc | Dark, Light | ❌ |

---

## Architecture overview

Every UnoDock theme follows a three-tier structure mirroring AvalonDock's design:

```
ThemeClass.cs          C# class – returns the ms-appx:/// URI to the entry XAML
XxxTheme.xaml          Entry point – merges brush file(s) + Generic.xaml
XxxBrushs.xaml         Color/brush palette for one variant
Themes/Generic.xaml    All control styles and templates (shared across variants)
OverlayButtons.xaml    Docking compass/drop-target indicator shapes
Themes/ResourceKeys.cs String constants that name every brush key
```

Multi-variant themes (VS2013: Blue/Dark/Light) repeat the first three tiers once
per variant; `Generic.xaml` and `OverlayButtons.xaml` are shared.

---

## Step-by-step porting process

### 1. Create the project

Copy `src/UnoDock.Themes.VS2013/UnoDock.Themes.VS2013.csproj` to a new folder,
e.g. `src/UnoDock.Themes.VS2010/`. Rename the assembly and namespace in the
`<PropertyGroup>`:

```xml
<AssemblyName>UnoDock.Themes.VS2010</AssemblyName>
<RootNamespace>UnoDock.Themes.VS2010</RootNamespace>
```

Update `<ItemGroup>` entries to list only the files you will create (see §4–§6).

Add the new `.csproj` to the solution.

### 2. Create the C# theme class(es)

For each variant, create a file like `Vs2010Theme.cs`:

```csharp
using System;
using AvalonDock.Themes;

namespace UnoDock.Themes.VS2010
{
    public class Vs2010Theme : Theme
    {
        public override Uri GetResourceUri()
            => new Uri("ms-appx:///UnoDock.Themes.VS2010/Theme.xaml");
    }
}
```

Key difference from AvalonDock: use `ms-appx:///AssemblyName/File.xaml` instead
of the WPF pack URI `/AssemblyName;component/File.xaml`.

### 3. Create `ResourceKeys.cs`

Define one `public const string` per brush that `Generic.xaml` will reference.
Prefix every key with `UnoDock_<ThemeName>_` to avoid collisions:

```csharp
namespace UnoDock.Themes.VS2010.Themes
{
    internal static class ResourceKeys
    {
        public const string Background              = "UnoDock_VS2010_Background";
        public const string TabBarBackground        = "UnoDock_VS2010_TabBarBackground";
        // ... one per brush
    }
}
```

The file lives at `Themes/ResourceKeys.cs`.

### 4. Create the brush file(s)

Open the source `Brushes.xaml` (or `XxxBrushs.xaml`) in the AvalonDock theme
and map every color/brush to a semantically named key.

**WPF → WinUI brush differences:**

| AvalonDock (WPF) | UnoDock (WinUI/Uno) |
|---|---|
| `DrawingBrush` (tiled patterns) | Not supported – replace with a plain `SolidColorBrush` |
| `LinearGradientBrush` | Supported – keep or simplify to `SolidColorBrush` |
| Numbered keys (`BaseColor1Key`) | Semantic string keys (`UnoDock_VS2010_Background`) |
| `{x:Static reskeys:ResourceKeys.XxxKey}` | Plain key string e.g. `UnoDock_VS2010_Background` |

Brush file header (no `clr-namespace` declarations needed):

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <SolidColorBrush x:Key="UnoDock_VS2010_Background" Color="#2C3D5A" />
    ...
</ResourceDictionary>
```

### 5. Create the entry-point XAML

For single-variant themes:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
    <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="ms-appx:///UnoDock.Themes.VS2010/Brushes.xaml" />
        <ResourceDictionary Source="ms-appx:///UnoDock.Themes.VS2010/Themes/Generic.xaml" />
    </ResourceDictionary.MergedDictionaries>
</ResourceDictionary>
```

For multi-variant themes (one entry file per variant):

```xml
<!-- LightTheme.xaml -->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
    <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="ms-appx:///UnoDock.Themes.VS2013/LightBrushs.xaml" />
        <ResourceDictionary Source="ms-appx:///UnoDock.Themes.VS2013/Themes/Generic.xaml" />
    </ResourceDictionary.MergedDictionaries>
</ResourceDictionary>
```

### 6. Create `Themes/Generic.xaml`

This is the most labour-intensive step. Start from the corresponding
AvalonDock `Theme.xaml` (or `Themes/Generic.xaml`) and apply these transforms:

#### Namespace declarations

Remove all WPF-only `xmlns` entries. The minimal set for UnoDock:

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:AvalonDock.Controls"
    xmlns:dock="using:AvalonDock">
```

#### Resource key references

| AvalonDock | UnoDock |
|---|---|
| `{DynamicResource {x:Static avalonDockAero:AeroColors.BaseColor5Key}}` | `{StaticResource UnoDock_VS2010_PanelBorderBrush}` |
| `{DynamicResource {x:Static avalonDockVs2013:ResourceKeys.XxxBrush}}` | `{StaticResource UnoDock_VS2013_XxxBrush}` |

Use `StaticResource` (not `DynamicResource`) because all brush files are merged
before `Generic.xaml` is loaded.

#### WPF constructs that have no WinUI equivalent

| WPF | Action |
|---|---|
| `{x:Type SomeControl}` as a style key | Replace with `x:Key="..."` + explicit `TargetType` |
| `{x:Static SystemColors.GrayTextBrushKey}` | Replace with a literal color brush |
| `{x:Static ToolBar.ButtonStyleKey}` / `ToggleButtonStyleKey` | Replace with a simple `<Style TargetType="Button">` or `<Style TargetType="ToggleButton">` |
| `shell:WindowChrome` | UnoDock uses its own floating-window chrome; omit the setter |
| `SplineBorder` (Aero custom control) | Replace with a plain `<Border CornerRadius="...">` |
| `ContextMenuEx` / `MenuItemEx` | UnoDock provides `ContextMenuEx` / `MenuItemEx` in `AvalonDock.Controls` |
| `DropDownButton` / `DropDownControlArea` | Available in `AvalonDock.Controls` |
| WPF image paths (`Images/Pin.png`) | Replace with `Path` geometry data from `OverlayButtons.xaml`, or omit for icon-less buttons |
| `RecognizesAccessKey="True"` | Not supported in WinUI – remove the attribute |
| `SnapsToDevicePixels` | Not supported – remove |

#### Control type mapping

| AvalonDock WPF type | UnoDock WinUI type |
|---|---|
| `avalonDockControls:LayoutDocumentPaneControl` | `controls:LayoutDocumentPaneControl` |
| `avalonDockControls:LayoutAnchorablePaneControl` | `controls:LayoutAnchorablePaneControl` |
| `avalonDockControls:LayoutGridResizerControl` | `controls:LayoutGridResizerControl` |
| `avalonDockControls:OverlayWindow` | `controls:OverlayWindow` |
| `avalonDock:DockingManager` | `dock:DockingManager` |

#### DockingManager style key

AvalonDock uses `x:Key="{x:Type avalonDock:DockingManager}"`. WinUI does not
support type-keyed implicit styles in ResourceDictionary at the theme level.
Use an explicit key instead and apply it in `DockingManager.cs`:

```xml
<Style x:Key="DefaultDockingManagerStyle" TargetType="dock:DockingManager">
```

UnoDock's `DockingManager` loads the theme dictionary and applies the style
automatically via `ThemeChanged()`.

### 7. Create `OverlayButtons.xaml`

Copy `OverlayButtons.xaml` from `UnoDock.Themes.VS2013` as a starting point.
The shapes use `Path` geometry data and do not need colour changes for most
themes. Adjust fill/stroke brushes to reference the new theme's keys.

### 8. Wire up the project file

Add every `.cs` to `<Compile>` and every `.xaml` to `<Page>`:

```xml
<ItemGroup>
  <Compile Include="Themes\ResourceKeys.cs" />
  <Compile Include="Vs2010Theme.cs" />
</ItemGroup>
<ItemGroup>
  <Page Include="Brushes.xaml"          SubType="Designer" Generator="MSBuild:Compile" />
  <Page Include="Theme.xaml"            SubType="Designer" Generator="MSBuild:Compile" />
  <Page Include="OverlayButtons.xaml"   SubType="Designer" Generator="MSBuild:Compile" />
  <Page Include="Themes\Generic.xaml"   SubType="Designer" Generator="MSBuild:Compile" />
</ItemGroup>
```

### 9. Register in the sample

In `UnoDock.Sample`, add a reference to the new theme project and wire it up
alongside the VS2013 theme selector in `MainPage.xaml` / the theme picker.

---

## Common pitfalls

- **`ms-appx:///` URIs** are case-sensitive on non-Windows targets. Match the
  exact assembly name and file name.
- **`StaticResource` vs `DynamicResource`**: brush files are always merged
  before `Generic.xaml`, so `StaticResource` is correct and faster.
- **`DrawingBrush` (WPF tiled patterns)**: completely unsupported in WinUI/Uno.
  Replace with a nearest `SolidColorBrush`.
- **Floating-window chrome**: UnoDock handles the native window border itself
  on Windows; omit any `shell:WindowChrome` setters.
- **`{x:Type …}` style keys**: must be replaced with explicit string keys for
  WinUI/Uno resource dictionaries.
