# UnoDock WinUI 3 Port Plan

## Goal

Make WinUI 3 a supported, first-class target for UnoDock, not just an aspirational
or partially wired target. In this repository, that means:

- Every relevant project declares `net10.0-windows10.0.19041.0`.
- WinUI builds use the right toolchain (`msbuild`/`dotnet msbuild` for XAML projects).
- The sample app and theme packages build against the Windows App SDK target.
- Public docs stop saying WinUI 3 is "planned later" once the build path is real.

## Current State

As of 2026-06-09:

- The core library already contains substantial WinUI-specific code paths.
- `UnoDock.csproj` already has a Windows-targeted property group gated on
  `net10.0-windows10.0.19041.0`.
- The sample and theme projects also have WinUI property groups.
- But the projects only declared `TargetFrameworks>net10.0-desktop</...>`, so the
  Windows target was not actually part of the build graph.
- `dotnet build -f net10.0-windows10.0.19041.0` on the library fails with
  `UNOB0008`, which is expected for WinUI class libraries with XAML; those builds
  must go through `msbuild` or `dotnet msbuild`.

## Plan

1. Make Windows App SDK explicit in project target frameworks.
   Update the core library, sample app, tests, and theme packages to declare both:
   `net10.0-desktop;net10.0-windows10.0.19041.0`.

2. Verify the WinUI build path with the correct tool.
   Use `dotnet msbuild` instead of `dotnet build` for XAML-bearing WinUI projects.

3. Keep library, sample, and theme targets aligned.
   Ensure project references can restore and build for the Windows TFM without
   `NETSDK1005` asset-target mismatches.

4. Update user-facing documentation.
   Remove outdated README language that says WinUI 3 is not targeted yet, and
   document the correct Windows build command.

5. Leave runtime-specific polish for follow-up work only if build verification
   surfaces real WinUI-only behavior gaps.

## Execution Log

### 2026-06-09

- Confirmed `UnoDock.csproj` already contains WinUI property groups.
- Confirmed the codebase already includes Windows App SDK and WinUI-specific logic.
- Reproduced the real blockers:
  - `UNOB0008` when using `dotnet build` for the WinUI class library.
  - `NETSDK1005` because the sample did not actually declare the Windows TFM.
- Added `net10.0-windows10.0.19041.0` to the core library, sample app, tests,
  and both theme packages.
- Verified the core library builds for WinUI 3 with Visual Studio 2026
  `MSBuild.exe`.
- Fixed WinUI-only compile issues in floating-window drag, diagnostics logging,
  and activation-state handling.
- Reworked the sample app so its initial docking layout is created in code-behind
  instead of XAML. This avoids the current WinUI XAML compiler limitation around
  the custom AvalonDock layout model types while still giving us a working sample.
- Verified the sample app and both theme packages build for WinUI 3 with Visual
  Studio 2026 `MSBuild.exe`.

### 2026-06-10

- Fixed runtime bug: docked panes did not follow window size changes on WinUI 3.
  When the window shrank, pixel-width panes kept their sizes, the layout grid
  overflowed its slot, and the right-most pane rendered partially (clipped at
  the window edge). Growing the window back did not restore pane sizes either.
- Root cause is a Grid layout difference between WinUI 3 and Uno:
  - On Uno (Skia), a Grid whose fixed columns exceed the available slot is
    clamped to the slot, so `ActualWidth` reflects the true available space and
    `LayoutGridControl.AdjustFixedChildrenPanelSizes` self-corrects.
  - On WinUI 3, the same Grid keeps the overflowed total as its `ActualWidth`,
    so the self-correction sees a bogus "available" size and concludes nothing
    needs to shrink. Worse, the grid's own `SizeChanged` (queued from the same
    layout pass) could revert a correct adjustment back to the overflowed state.
- Fix, in two parts, both guarded `#if WINDOWS_APP_SDK`:
  - `DockingManager` now subscribes `SizeChanged` and pushes its window-clamped
    size into `LayoutRootPanel.AdjustFixedChildrenPanelSizes(...)` — direct
    parity with WPF AvalonDock's `DockingManager.OnSizeChanged`.
  - `LayoutGridControl` captures the measure constraint in `MeasureOverride`
    and clamps the self-measured available size with it, so per-grid
    adjustments can never operate on an overflowed `ActualWidth`.
- Verified at runtime with DevFlow on the WinUI sample: shrink 1600→700 px
  proportionally shrinks fixed panes (228/520 → 193/436) keeping every pane
  visible; growing back to 1600 px restores the original 228/520 widths.
- Fixed (same day, follow-up): on WinUI the initial layout pixelized the side
  panes at their 25 px minimum because the dispatched children refresh reaches
  `OnFixChildrenDockLengths` after the grid is arranged but before the new pane
  controls are, so each pane model still reports `ActualWidth == 0` and got
  pinned at `max(0, min)`. On Uno that refresh runs before the grid is arranged,
  so the `ActualWidth == 0` early-return is taken and panes simply stay
  star-sized (balanced thirds). Fix (`#if WINDOWS_APP_SDK`, in
  `LayoutPanelControl.OnFixChildrenDockLengths`): skip the star→pixel
  conversion for any pane that has never been arranged. Startup now shows the
  same balanced thirds as Uno.
- Fixed pane-header grip length: the VS2010/VS2013 themes drew the caption
  drag-grip dots as a `Path` with a fixed-length hard-coded geometry
  (~40/200 px) because WinUI has no tiling `DrawingBrush`. The dots now stop
  short on wide panes and would overflow narrow ones. Fix: the grip `Path` is
  named `PART_HeaderGrip`, stretched, and `LayoutAnchorablePaneControl`
  regenerates its geometry (1×1 px rects, 4 px pitch, two offset rows — same
  pattern as WPF's DragHandleTexture) to span the actual header width on every
  size change.
- Known remaining issue (pre-existing): on WinUI the very first frame after
  launch can render the bottom ~50 px of the window blank (status bar and tab
  strips missing) until the first window resize or re-layout. Layout values are
  correct (verified via element probes); it looks like a first-render
  composition glitch. Needs separate investigation.

## Current Result

WinUI 3 is now a real build target in this repository.

- `UnoDock` builds for `net10.0-windows10.0.19041.0`.
- `UnoDock.Sample` builds for `net10.0-windows10.0.19041.0`.
- `UnoDock.Themes.VS2010` and `UnoDock.Themes.VS2013` build for
  `net10.0-windows10.0.19041.0`.
- The recommended Windows build entry point is Visual Studio 2026
  `MSBuild.exe`, not `dotnet build`, for XAML-bearing WinUI projects.

## Follow-Up

- Investigate whether the sample's original declarative layout XAML can be
  restored once the WinUI XAML compiler can consume the custom layout-model
  surface cleanly.
- Triage remaining warnings separately. They are mostly XML documentation and
  legacy analyzer warnings, not WinUI port blockers.
