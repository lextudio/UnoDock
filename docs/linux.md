# UnoDock Linux Support

This document tracks the Linux desktop support plan for UnoDock and the safety rules used to avoid regressions on macOS, Windows, and WinUI.

## Goals

- Enable drag-to-float and overlay drop interaction on Linux desktop.
- Keep Windows and WinUI behavior unchanged.
- Keep macOS native behavior unchanged.
- Avoid cross-platform native interop leaks (no Win32 calls on Linux, no Cocoa calls on Linux).

## Architecture Rules

- Windows-only code must stay behind `OperatingSystem.IsWindows()` checks.
- macOS-only code must stay behind `OperatingSystem.IsMacOS()` checks.
- Linux path uses Uno APIs only (`Window`, `AppWindow`, `CoreWindow`, `DispatcherTimer`).
- Shared drag orchestration remains in `DockingManager`; each platform provides only cursor/button/origin/drag mechanics.

## Implemented in this pass

1. Added Linux timer drag tracker

- File: `src/UnoDock/Controls/LinuxFloatingWindowDragTracker.cs`
- Behavior:
	- Polls cursor and left-button state from `CoreWindow`.
	- Moves floating window every 16 ms while button is down.
	- Reports manager-relative cursor coordinates to overlay logic.
	- Ends drag on button release.

2. Added Linux drag orchestration in DockingManager

- File: `src/UnoDock/Controls/DockingManager.cs`
- Added `StartLinuxDragTracking(...)` with the same overlay lifecycle as Windows/macOS:
	- show overlay
	- update targets while dragging
	- perform drop on release
	- hide overlay and clear tracker

3. Added Linux origin and cursor routing

- `ComputeScreenOrigin()` now routes Linux to a Linux-specific origin helper.
- `NativeCursorScreen()` now routes Linux to `LinuxFloatingWindowDragTracker.GetCursorScreen()`.

4. Fixed macOS-only interop leakage on Linux

- Guarded macOS-only logging and startup code with `OperatingSystem.IsMacOS()`.
- Prevented `MacOSWindowTabbing` calls from running on Linux.

5. Updated status reporting and diagnostics

- `GetDragStatus()` now reports Linux tracker state and Linux origin.
- `StartTrackerForFloatingWindow(...)` supports Linux.
- `SimulateDrop(...)` now stops Linux tracker too.

## Validation Checklist

- Build `net10.0-desktop` on Linux.
- Drag a document tab downward to float.
- Move floating window across manager and verify compass updates.
- Drop into each main zone and verify expected docking result.
- Re-drag an existing floating window and verify drop still works.
- Repeat smoke checks on Windows and macOS to ensure unchanged behavior.

## Known Limitations

- Linux drag relies on `CoreWindow` cursor/button state; if backend behavior differs by compositor/windowing backend, diagnostics should be used to confirm reported coordinates and button transitions.
- Native Linux-specific window APIs are intentionally not used in this pass to keep the implementation portable across Uno Linux backends.
