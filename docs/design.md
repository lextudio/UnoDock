# UnoDock — porting AvalonDock to Uno Platform

## Goal

Port **AvalonDock** (Dirkster99 fork, the maintained WPF docking library) to Uno
Platform so it runs on `net10.0-desktop` (Skia: macOS, Linux, Windows) and
`net10.0-windows10.0.19041.0` (WinUI 3), reusing as much upstream WPF source as
possible.

This is a *source port* (compile the real AvalonDock `.cs`/`.xaml` against a WPF
API surface), distinct from the sibling **UnoDocking** project (a Reactor-engine
docking renderer). The two share hard-won macOS techniques but are independent.

## Proven template (UnoEdit + WindowsShims)

We have already shipped a structurally identical port — **UnoEdit** (AvalonEdit →
Uno) — and a WPF API-surface shim — **WindowsShims** (`LeXtudio.Windows`). UnoDock
follows the same playbook:

| Concern | Mechanism (from UnoEdit) |
|---|---|
| Upstream source | git submodule `avalondock/` (Dirkster99/AvalonDock) |
| Clean WPF-free files | `<Compile Include Link>` straight from the submodule |
| Files needing edits | fork into `src/UnoDock/` (standalone) |
| Mixed shared/platform logic | `partial class` + `.uno.cs` / `.wpf.cs` suffixes |
| WPF type surface | depend on `LeXtudio.Windows` (WindowsShims) |
| WPF→WinUI name mapping | `GlobalUsings.cs` aliases |
| Platform divergence | `#if WINDOWS_APP_SDK` |
| Theming | `Themes/generic.xaml` ported to WinUI XAML |
| Dual build | `net10.0-desktop` + `net10.0-windows10.0.19041.0` (Windows only) |

WindowsShims supplies the WPF API surface UnoDock compiles against:
`DependencyProperty`/`DependencyObject` (aliased to `Microsoft.UI.Xaml`),
`FrameworkPropertyMetadata` (bridges WPF metadata → WinUI `PropertyMetadata`),
`RoutedEvent` + `AddHandler`/`RaiseEvent` (handler-bag side tables), `Freezable`
(no-op `Freeze()`), brushes/typography aliases, `Dispatcher` shim. Gaps UnoDock
needs that WindowsShims lacks will be contributed back there.

## AvalonDock architecture (what we're porting)

~164 C# files / ~33k LOC + 1 `generic.xaml` (1329 lines) + 6 theme packages.

**Two-layer design — the key to a tractable port:**

1. **Model layer (`Layout/*`, ~46 files)** — pure `INotifyPropertyChanged` tree:
   `LayoutRoot`, `LayoutPanel`, `LayoutDocument`, `LayoutAnchorable`,
   `LayoutDocumentPane`, `LayoutAnchorablePane`, `*Group`, `LayoutAnchorSide`,
   `LayoutFloatingWindow` (model). **Almost no WPF UI dependency** — only
   `DependencyProperty` + `XmlSerializer` (serialization). This layer ports first
   and is the natural baseline test target.

2. **Control layer (`Controls/*`, ~72 files)** — the WPF UI: `DockingManager`
   (root control), `LayoutDocumentPaneControl`, `LayoutAnchorablePaneControl`,
   `LayoutFloatingWindowControl` (derives from `Window`!), `LayoutAutoHideWindowControl`
   (derives from `HwndHost`!), `DragService`, the `DropTarget`/`OverlayWindow`
   drag-drop system. **This is where the WPF-isms live.**

Plus **Converters (14)**, **Commands (8)**, **`generic.xaml` + themes**.

## Porting difficulty map

| Subsystem | Difficulty | Strategy |
|---|---|---|
| `Layout/*` model | **Easy** | Link upstream; only needs DependencyProperty + XmlSerializer shims |
| Converters | Easy | Link; `IValueConverter` exists in WinUI |
| Commands | Medium | `RoutedCommand` partially in WindowsShims; may fork to `ICommand` |
| `generic.xaml` | **Hard** | 106 triggers/multibindings — WinUI XAML lacks WPF triggers; rewrite as VisualStateManager + converters + code-behind |
| `DockingManager` control | Medium-Hard | Template-heavy; needs the XAML rewrite + measure/arrange |
| Drag-drop (`DragService`, drop targets, `OverlayWindow`) | **Hard** | Custom WPF mouse-capture + overlay windows; reimplement with our macOS CGEvent + Uno multi-window techniques |
| `LayoutFloatingWindowControl : Window` | **Hardest** | WPF `Window` ≠ Uno `Window` (not a FrameworkElement). Re-architect: host floating content in a real Uno `Window` + a content control, not a templated `Window` subclass |
| `LayoutAutoHideWindowControl : HwndHost` | **Hardest** | No HwndHost in Uno. Reimplement auto-hide flyout as an in-window `Popup`/overlay panel |
| Win32 `Win32Helper` / `WindowChrome` / Shell | Drop on non-Windows | `#if WINDOWS_APP_SDK` for real Win32; on Skia use Uno windowing + custom chrome |

## Reuse from UnoDocking (macOS lessons already solved)

The Reactor-based UnoDocking sessions 1–3 already cracked the macOS-hard problems.
UnoDock reuses these directly:

- **Independent floating windows on macOS**: `new Window()` + ObjC
  `setTabbingMode:NSWindowTabbingModeDisallowed` (else Uno merges windows into one
  tab bar). See `UnoReactor/Compat/MacOSWindowTabbing.cs`.
- **Global cursor during drag**: CoreGraphics `CGEventGetLocation` (logical points,
  top-left, Y-down) + `CGEventSourceButtonState`. No Y-flip.
- **Floating window positioning**: `[NSWindow setFrameTopLeftPoint:]`
  (`Cocoa_Y = screenH − Quartz_Y`), not `AppWindow.Move` (unreliable mapping on Uno).
- **Full window close**: `[NSWindow close]` (not `orderOut:`) to avoid ghost windows.
- **DevFlow drag injection**: `POST /api/v1/ui/actions/drag` posts real CGEvents so
  drag/drop is testable headless-ish (needs Accessibility/TCC permission).
- **Coordinate calibration**: content-origin = window frame + title-bar + chrome,
  measured via `CGWindowListCopyWindowInfo`.

## References available

- **Uno source** at `/Users/lextm/uno-tools/uno` — read when a WinUI/Uno API
  behaves unexpectedly (e.g. how `Window`, `AppWindow`, `Popup`, pointer events,
  `DispatcherTimer` are implemented on Skia macOS). Do **not** modify it; the
  product can only consume released Uno 6.5 NuGet.
- **DevFlow for Uno** at `/Users/lextm/uno-tools/wpf-labs/src/DevFlow` — runtime
  introspection (`/ui/tree`, `/ui/elements` with bounds, `/ui/screenshot`) and
  input injection (`/ui/actions/tap|drag`) for live debugging the running sample.

## Decisions

1. **Floating windows are real Uno `Window`s, not `Window` subclasses.** AvalonDock's
   `LayoutFloatingWindowControl : Window` can't port 1:1 (Uno `Window` is sealed-ish
   and not a `Control`). We fork it to a `ContentControl`-based host placed inside a
   real Uno `Window`, reusing UnoDocking's macOS multi-window plumbing. This is the
   single biggest divergence and is isolated to a handful of `*FloatingWindowControl`
   files.

2. **Auto-hide uses an in-tree overlay, not `HwndHost`.** Reimplement
   `LayoutAutoHideWindowControl` as a `Popup`/Canvas overlay in the `DockingManager`'s
   own visual tree.

3. **`generic.xaml` is rewritten, not linked.** WPF triggers/multibindings don't
   exist in WinUI XAML. Port the resource dictionary to WinUI XAML using
   `VisualStateManager`, value converters, and minimal code-behind. The VS2013 theme
   (closest to our target look) is the first/reference theme.

4. **Win32/Shell chrome is Windows-only.** `Win32Helper`, `WindowChrome`,
   `Controls/Shell/*` compile only under `#if WINDOWS_APP_SDK`; Skia gets Uno-native
   windowing + a simple custom chrome.

5. **Serialization stays.** `XmlLayoutSerializer` + `LayoutRoot` `IXmlSerializable`
   use `System.Xml.Serialization`, which runs on .NET 10 — link as-is, test round-trip.

6. **Baseline tests first.** Like UnoDocking (575 ported Reactor tests) and UnoEdit
   (281 tests), port AvalonDock's model/serialization tests to a `net10.0-desktop`
   xUnit/NUnit project as the regression baseline before touching the control layer.

## Phased plan

- **Phase 0 — scaffold**: 3 projects (`UnoDock`, `UnoDock.Sample`, `UnoDock.Tests`),
  `Uno.Sdk`, dual-target, reference `LeXtudio.Windows`, submodule wired. Get an empty
  build green.
- **Phase 1 — model layer**: link `Layout/*` + converters + commands; add
  `GlobalUsings.cs`; fix compile errors with shims. Port model/serialization tests →
  green baseline.
- **Phase 2 — theming**: rewrite `generic.xaml` (VS2013) to WinUI XAML.
- **Phase 3 — docked controls**: `DockingManager`, pane/tab controls, splitters —
  static docked layout rendering (no drag yet). Verify via DevFlow screenshot.
- **Phase 4 — drag-drop + overlay compass**: `DragService`, drop targets,
  `OverlayWindow`, reusing UnoDocking's CGEvent/coordinate work.
- **Phase 5 — floating windows**: real Uno windows + macOS tabbing/position/close.
- **Phase 6 — auto-hide**: overlay reimplementation.
- **Phase 7 — polish**: remaining themes, accessibility, Windows (WinUI 3) target.

Each phase ends with a DevFlow-verified runtime check and a session log.

## Success criteria

- `UnoDock` builds for `net10.0-desktop` on macOS.
- Model + serialization test baseline green (ported AvalonDock tests).
- Sample app shows a VS-style docked layout (documents + tool windows) on macOS.
- Drag a tool window → overlay compass → re-dock; tear out → floating window;
  auto-hide → flyout. All verified live via DevFlow.
- Windows (WinUI 3) target builds and runs with the same source.
