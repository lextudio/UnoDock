# Drag-to-Float in WPF AvalonDock

How AvalonDock tears a docked tab/pane out into a floating window, and how it
makes that brand-new floating window "stick" to the cursor so the user keeps
dragging seamlessly — no visible jump, no release-and-regrab.

All file references are to
[externals/AvalonDock/source/Components/AvalonDock](../externals/AvalonDock/source/Components/AvalonDock).

## The core trick (TL;DR)

WPF gives you no managed API to programmatically "pick up" a window and keep it
following the mouse. AvalonDock hands the job back to the operating system. It:

1. Creates the floating window and positions it under the cursor.
2. Sends the Win32 message **`WM_NCLBUTTONDOWN`** with `wParam = HTCAPTION` to
   the new window's HWND.

That message tells Windows: *"the user just pressed the left mouse button on my
title bar."* Because the physical left button is still held down (the user never
released it after the original drag gesture), Windows enters its native
**modal move loop** for the window — exactly as if the user had grabbed the title
bar. The window now follows the cursor using the OS's own drag machinery, and
AvalonDock just listens to the resulting `WM_MOVING` / `WM_EXITSIZEMOVE`
messages to drive docking overlays and the final drop.

This is why it feels native: it *is* native. The only WPF-side work is getting
the window created and placed under the cursor before the synthetic click fires.

## End-to-end flow

### 1. Gesture detection on the tab / title

The drag is recognized on the source control while the mouse button is held:

- Document tab: [Controls/LayoutDocumentTabItem.cs:114](../externals/AvalonDock/source/Components/AvalonDock/Controls/LayoutDocumentTabItem.cs#L114) (`OnMouseMove`)
- Anchorable tab: [Controls/AnchorablePaneTabPanel.cs](../externals/AvalonDock/source/Components/AvalonDock/Controls/AnchorablePaneTabPanel.cs)
- Tool-window title bar: [Controls/AnchorablePaneTitle.cs:100](../externals/AvalonDock/source/Components/AvalonDock/Controls/AnchorablePaneTitle.cs#L100)

On `MouseLeftButtonDown` the control captures the mouse and records
`_mouseDownPoint`. On `MouseMove`, once the pointer travels past
`SystemParameters.MinimumHorizontalDragDistance` /
`MinimumVerticalDragDistance`, it flips into drag mode. When the cursor leaves
the tab strip's screen area (`_parentDocumentTabPanelScreenArea`, inflated
vertically to avoid accidental tear-off while merely reordering tabs), it
releases the WPF mouse capture and calls into the manager:

```
ReleaseMouseCapture();
manager.StartDraggingFloatingWindowForContent(Model);
```

Releasing the WPF capture is important — the OS move loop needs the button input,
and WPF must not be holding it.

### 2. Manager creates the floating window

[DockingManager.cs:1730](../externals/AvalonDock/source/Components/AvalonDock/DockingManager.cs#L1730)
`StartDraggingFloatingWindowForContent` (and the pane variant
`StartDraggingFloatingWindowForPane` at line 1785):

1. Bails out if `CanFloat` is false, or if a `ContentFloating` event handler
   cancels.
2. Reuses an existing single-item floating window if the content is the last one
   in its pane; otherwise calls `CreateFloatingWindow(...)`.
3. Schedules the show + drag attach on the dispatcher:

```csharp
Dispatcher.BeginInvoke(new Action(() =>
{
    if (startDrag) fwc.AttachDrag();   // arm the follow-the-cursor behavior
    fwc.Show();
    ContentFloated?.Invoke(...);
}), DispatcherPriority.Send);
```

Note the pane path (`StartDraggingFloatingWindowForPane`) calls
`AttachDrag()` then `Show()` directly rather than via `BeginInvoke`.

### 3. `AttachDrag` — arming the behavior

[Controls/LayoutFloatingWindowControl.cs:347](../externals/AvalonDock/source/Components/AvalonDock/Controls/LayoutFloatingWindowControl.cs#L347)

```csharp
internal void AttachDrag(bool onActivated = true)
{
    if (onActivated)
    {
        _attachDrag = true;
        Activated += OnActivated;     // defer until the window is actually shown/activated
    }
    else
    {
        // window already exists & is active: synthesize the title-bar press now
        var windowHandle = new WindowInteropHelper(this).Handle;
        var lParam = new IntPtr(((int)Left & 0xFFFF) | ((int)Top << 16));
        Win32Helper.SendMessage(windowHandle, WM_NCLBUTTONDOWN, new IntPtr(HT_CAPTION), lParam);
    }
}
```

The default `onActivated: true` path does **not** start the drag immediately —
the HWND may not exist yet. Instead it sets `_attachDrag = true` and subscribes
to `Activated`. The actual cursor-follow logic runs once WPF raises `Activated`
after `Show()`.

### 4. `OnActivated` / `InternalOnActivated` — making it follow the cursor

This is where the new window is snapped under the cursor and the OS move loop is
launched.
[Controls/LayoutFloatingWindowControl.cs:689](../externals/AvalonDock/source/Components/AvalonDock/Controls/LayoutFloatingWindowControl.cs#L689):

```csharp
private void InternalOnActivated(object sender, EventArgs e, int retryCount = 0)
{
    Activated -= OnActivated;                 // one-shot

    // Abort if the button was already released or drag was never armed
    if (!_attachDrag || Mouse.LeftButton != MouseButtonState.Pressed)
        return;

    var windowHandle = new WindowInteropHelper(this).Handle;

    // Multi-DPI guard: if the visual isn't connected to a PresentationSource yet,
    // defer ~10ms and retry up to 5 times (avoids InvalidOperationException).
    if (PresentationSource.FromVisual(this) == null) { /* Task.Delay(10) + retry */ return; }

    // Where is the cursor, in screen DPI coordinates?
    var mousePosition = this.PointToScreenDPI(Mouse.GetPosition(this));
    var area = this.GetScreenArea();

    // Offset so the cursor lands on the title bar, not the window corner.
    if (DragDelta == default) DragDelta = new Point(3, 3);   // BugFix Issue #6 fallback
    Left = mousePosition.X - DragDelta.X;
    Top  = mousePosition.Y - DragDelta.Y;

    // If moving the window changed its size, we crossed into a different-DPI
    // monitor — recompute the mouse position and reposition.
    if (this.GetScreenArea().Size != area.Size)
    {
        if (PresentationSource.FromVisual(this) != null)
        {
            mousePosition = this.PointToScreenDPI(Mouse.GetPosition(this));
            Left = mousePosition.X - DragDelta.X;
            Top  = mousePosition.Y - DragDelta.Y;
        }
    }

    _attachDrag = false;
    Show();

    // THE handoff: tell Windows the title bar was just clicked → native move loop.
    var lParam = new IntPtr(((int)mousePosition.X & 0xFFFF) | ((int)mousePosition.Y << 16));
    Win32Helper.SendMessage(windowHandle, WM_NCLBUTTONDOWN, new IntPtr(HT_CAPTION), lParam);
}
```

Key points that ensure the window "follows the cursor when it first appears":

- **Positioned before shown to the user as draggable.** `Left`/`Top` are set so
  the window's title bar sits directly under the pointer *before* the synthetic
  click, so there is no visible jump between the torn-off tab and the new window.
- **`DragDelta`** is the offset of the grab point within the title bar. It keeps
  the cursor at the same relative spot it grabbed, rather than snapping the
  window's top-left corner to the pointer. When not explicitly provided it falls
  back to `(3, 3)`.
- **Button-still-pressed precondition.** The whole thing only works because
  `Mouse.LeftButton == MouseButtonState.Pressed`. If the user already let go, it
  aborts — there is nothing to follow.
- **DPI safety.** Both the deferred-retry guard and the post-move size-change
  check exist for multi-monitor / per-monitor-DPI setups, where positioning a
  window can move it to a monitor with a different scale factor and shift where
  the cursor maps.

### 5. The OS move loop drives docking; AvalonDock listens

Once `WM_NCLBUTTONDOWN`/`HTCAPTION` is sent, Windows runs its modal move loop and
streams messages into the window's HWND hook,
`FilterMessage` ([Controls/LayoutFloatingWindowControl.cs:362](../externals/AvalonDock/source/Components/AvalonDock/Controls/LayoutFloatingWindowControl.cs#L362)),
registered in `OnLoaded` via `_hwndSrc.AddHook`:

- **`WM_MOVING`** → `UpdateDragPosition()`
  ([line 802](../externals/AvalonDock/source/Components/AvalonDock/Controls/LayoutFloatingWindowControl.cs#L802)).
  On the first move it lazily creates a `DragService` (see
  [Controls/DragService.cs](../externals/AvalonDock/source/Components/AvalonDock/Controls/DragService.cs)),
  marks `IsDragging`, then calls `_dragService.UpdateMouseLocation(...)` each
  frame. The `DragService` shows the overlay window and dock-target indicators
  and tracks which drop target the cursor is over.
- **`WM_EXITSIZEMOVE`** → the user released the button. AvalonDock calls
  `_dragService.Drop(mousePosition, out dropFlag)`. If the drop landed on a dock
  target, `dropFlag` is true and the floating window is re-docked and closed via
  `InternalClose()`; otherwise it stays floating where it was dropped.
- **`WM_LBUTTONUP`** with the button released → `_dragService.Abort()`
  (cancels an in-progress drag cleanly, e.g. after a context menu).

`DragDelta` itself, in this codebase, is essentially always the `(3, 3)`
fallback — the comment "A second chance back up plan if DragDelta is not set"
reflects that the property is the offset hook for callers that want to preserve
the exact grab point, but the default tear-off path relies on the fallback.

## Why this design

| Concern | Solution |
| --- | --- |
| WPF has no "start dragging this window" API | Synthesize `WM_NCLBUTTONDOWN`/`HTCAPTION` and let the OS run its native move loop |
| Window must appear already under the cursor | Set `Left`/`Top = mousePosition − DragDelta` *before* sending the synthetic click |
| HWND may not exist when float is requested | Defer via `Activated` event (`_attachDrag` flag), not an immediate send |
| Multi-DPI / multi-monitor jumps | Retry until `PresentationSource` is connected; re-position if size changes after the move |
| User released the button early | Guard on `Mouse.LeftButton == Pressed`; abort otherwise |
| Reordering tabs shouldn't tear off | Inflate the tab-strip hit area vertically before deciding to float |

## Key files

- [Controls/LayoutFloatingWindowControl.cs](../externals/AvalonDock/source/Components/AvalonDock/Controls/LayoutFloatingWindowControl.cs) — `AttachDrag`, `InternalOnActivated`, `FilterMessage`, `UpdateDragPosition`
- [DockingManager.cs](../externals/AvalonDock/source/Components/AvalonDock/DockingManager.cs) — `StartDraggingFloatingWindowForContent` / `...ForPane`, `CreateFloatingWindow`
- [Controls/LayoutDocumentTabItem.cs](../externals/AvalonDock/source/Components/AvalonDock/Controls/LayoutDocumentTabItem.cs), [Controls/AnchorablePaneTitle.cs](../externals/AvalonDock/source/Components/AvalonDock/Controls/AnchorablePaneTitle.cs) — gesture detection / tear-off trigger
- [Controls/DragService.cs](../externals/AvalonDock/source/Components/AvalonDock/Controls/DragService.cs) — overlay + drop-target tracking during the OS move loop
- `Win32Helper` — `WM_NCLBUTTONDOWN`, `HT_CAPTION`, `WM_MOVING`, `WM_EXITSIZEMOVE`, `SendMessage`, `GetMousePosition`

---

# Part 2 — The UnoDock port (Windows + macOS) and where it diverges

UnoDock re-implements this behavior on Uno Platform, targeting WinAppSDK on
Windows and AppKit on macOS. The port cannot reuse the central WPF trick, which
changes the entire architecture and opens a number of gaps. This part documents
how UnoDock actually works today and the details still missing versus the WPF
reference.

UnoDock source lives under [src/UnoDock](../src/UnoDock):

- [Controls/LayoutFloatingWindowControl.cs](../src/UnoDock/Controls/LayoutFloatingWindowControl.cs) — hosts each floating window as a *real Uno `Window`* (not a subclass).
- [Controls/DockingManager.cs](../src/UnoDock/Controls/DockingManager.cs) — `StartDraggingFloatingWindowForContent`, watchdog, `StartDragTracking` / `StartWindowsDragTracking`, origin computation.
- [Controls/FloatingWindowDragTracker.cs](../src/UnoDock/Controls/FloatingWindowDragTracker.cs) — macOS 16 ms timer tracker.
- [Controls/WindowsFloatingWindowDragTracker.cs](../src/UnoDock/Controls/WindowsFloatingWindowDragTracker.cs) — Windows 16 ms timer tracker.
- [Compat/MacOSWindowTabbing.cs](../src/UnoDock/Compat/MacOSWindowTabbing.cs) — Objective-C / CoreGraphics interop.

## The fundamental architectural difference

**WPF hands the drag to the OS; UnoDock drives it from a timer.**

WPF cannot be copied here for two reasons:

1. **Uno `Window` is not a `FrameworkElement`.** WPF's
   `LayoutFloatingWindowControl : Window` can be subclassed, add an HWND hook
   (`FilterMessage`), and react to `WM_MOVING` / `WM_EXITSIZEMOVE`. In Uno the
   `Window` is opaque — UnoDock instead hosts a normal `Window` whose `Content`
   is a plain visual tree ([LayoutFloatingWindowControl.cs:37](../src/UnoDock/Controls/LayoutFloatingWindowControl.cs#L37)).
   There is no message hook, so the OS move-loop messages are simply not
   observable.

2. **No portable "start dragging this window" handoff is wired up.** Instead of
   sending one message and letting the OS run its modal move loop, UnoDock runs
   a **`DispatcherTimer` at ~16 ms** that, every tick:
   - reads the native cursor position,
   - moves the floating window to `cursor − grabOffset`,
   - translates the cursor into manager-local coordinates and drives the compass
     overlay,
   - reads the native mouse-button state and, when released, commits or cancels
     the drop.

   See [FloatingWindowDragTracker.OnTickCore](../src/UnoDock/Controls/FloatingWindowDragTracker.cs#L150)
   (macOS) and [WindowsFloatingWindowDragTracker.OnTickCore](../src/UnoDock/Controls/WindowsFloatingWindowDragTracker.cs#L83)
   (Windows).

Everything below follows from this one decision: because nothing native is
driving the drag, UnoDock has to *poll* and *reconstruct* every signal WPF got
for free from the OS move loop.

### Side-by-side

| Concern | WPF AvalonDock | UnoDock |
| --- | --- | --- |
| Who moves the window | OS modal move loop (`WM_NCLBUTTONDOWN`/`HTCAPTION`) | App-side `DispatcherTimer` @ 16 ms calling `MoveWindow` each tick |
| Cursor source | `Mouse.GetPosition` (managed) | `GetCursorPos` (Win32) / `CGEventGetLocation` (Quartz) |
| Button-released signal | `WM_EXITSIZEMOVE` / `WM_LBUTTONUP` | Polled `GetAsyncKeyState` / `CGEventSourceButtonState` |
| Drag-move signal | `WM_MOVING` → `DragService` | Each timer tick computes overlay state |
| Window type | `Window` subclass with HWND hook | Opaque Uno `Window` + custom content root |
| Re-grab existing float | Native (the window is already draggable) | macOS: `NSWindowWillMove` + 200 ms watchdog; Windows: title-bar `PointerPressed` |

## How UnoDock initiates a tear-off

1. **Tab gesture** — [LayoutDocumentPaneControl.OnTabStripPointerMoved](../src/UnoDock/Controls/LayoutDocumentPaneControl.cs#L214)
   (and the anchorable equivalent). Unlike WPF's "leave the tab-strip rect"
   test, UnoDock floats when the cursor drops `FloatDownThreshold = 40` logical
   px **below** the tab strip. It must special-case Uno pointer quirks: CGEvent-
   injected `PointerMoved` events report `IsLeftButtonPressed = false`, so it
   treats any mouse-type move as button-down.
2. **Manager** — [DockingManager.StartDraggingFloatingWindowForContent](../src/UnoDock/Controls/DockingManager.cs#L336)
   creates the window, then:
   - **Windows:** sets the window position to the screen cursor (passed in as
     `initialScreenLeft/Top`), shows it, and starts `WindowsFloatingWindowDragTracker`.
   - **macOS:** uses *native initial placement* — it shows the window hidden
     (`ShowHiddenUntilPositioned`), then once the `NSWindow` exists reads the live
     Quartz cursor, computes a grab offset from the content-view center plus
     `InitialTitleBarGrabOffset = 18`, calls `MoveWindow`, `OrderWindowFront`,
     and starts the timer tracker.

So UnoDock has **three** entry points into tracking where WPF has effectively
one: tab tear-off (`startDrag`), title-bar re-grab of an already-floating window
(macOS `NSWindowWillMove` callback / Windows title-bar `PointerPressed`), and the
macOS **watchdog** that polls for window movement as a backstop.

## Gaps identified versus the WPF reference

These are the details the WPF implementation has that the UnoDock port is missing
or only partially approximates. Roughly ordered by user-visible impact.

### 1. No native move-loop handoff → visible lag and jitter

The timer approach moves the window *after* the cursor each tick, so the window
trails the pointer by up to one frame and can visibly stutter under load,
whereas the WPF window is glued to the cursor by the compositor. This is the
single biggest fidelity gap.

**Notably, the macOS native equivalent already exists but is dead code.**
[`MacOSWindowTabbing.PerformWindowDragFromTitleBarCenter`](../src/UnoDock/Compat/MacOSWindowTabbing.cs#L434)
synthesizes an `NSEventTypeLeftMouseDown` and calls
`-[NSWindow performWindowDragWithEvent:]` — the exact macOS analogue of WPF's
`WM_NCLBUTTONDOWN`/`HTCAPTION` trick. It is never called from the drag flow
(confirmed: no references outside its own file). Wiring it up would let AppKit
run its native window drag and eliminate the macOS timer entirely. **Windows has
no equivalent attempt at all** — there is no code sending `WM_NCLBUTTONDOWN` to
the floating HWND, even though the same trick works in WinAppSDK.

### 2. Drop lifecycle reconstructed from polling, not OS events

WPF gets a clean, atomic `WM_EXITSIZEMOVE` to commit the drop and `WM_LBUTTONUP`
to abort. UnoDock infers "drag ended" purely from the polled button state going
up between two ticks. Consequences:

- **Spurious-drop heuristic.** UnoDock needs `MinTicksBeforeDrop` (3 ticks, 0
  when a real drag is already confirmed) to avoid an instant false drop when a
  float is invoked from code with the button already released
  ([FloatingWindowDragTracker.cs:135](../src/UnoDock/Controls/FloatingWindowDragTracker.cs#L135)).
  WPF needs nothing equivalent.
- **No explicit abort path.** WPF's `WM_LBUTTONUP` → `_dragService.Abort()`
  (e.g. cancel after a context menu) has no direct counterpart; everything funnels
  through the same button-up tick.
- **200 ms watchdog latency (macOS).** Re-grabbing the title bar of an
  already-floating window is detected either by the `NSWindowWillMove`
  notification or, as a backstop, by a 200 ms polling watchdog
  ([DockingManager.cs:449](../src/UnoDock/Controls/DockingManager.cs#L449)).
  When the notification path misses, the first ~200 ms of a re-grab drag is
  untracked (no overlay), a window WPF never has.

### 3. Button & cursor state come from native polling, not the framework

WPF reads `Mouse.LeftButton`/`Mouse.GetPosition` directly. UnoDock cannot trust
Uno pointer events mid-drag, so it P/Invokes `GetAsyncKeyState`/`GetCursorPos`
(Windows) and `CGEventSourceButtonState`/`CGEventGetLocation` (macOS). This is a
gap in *robustness*: button state is sampled, not event-accurate, so a very fast
press-release between ticks can be missed, and the trackers must defensively
swallow all exceptions (`catch { }`) to avoid killing the timer.

### 4. Per-monitor DPI changes during the drag are not handled

WPF re-reads `GetScreenArea()` after repositioning and, if the window's size
changed (meaning it crossed onto a monitor with a different DPI), recomputes the
mouse position and re-places the window
([Part 1, step 4](#4-onactivated--internalonactivated--making-it-follow-the-cursor)).
It also retries up to 5× until `PresentationSource` is connected, for multi-DPI
init races.

UnoDock has **no equivalent mid-drag DPI re-check.** The grab offset is measured
once at `Start()` and reused for the whole drag. Dragging a floating window
across monitors with different scale factors can therefore drift the cursor away
from its original grab point. The three coordinate systems in play make this
fragile:

- macOS Quartz (top-left, Y-down) for the cursor,
- Cocoa (bottom-left, Y-up) for `setFrameTopLeftPoint:` — flipped inside
  `MacOSWindowTabbing.MoveWindow`,
- XAML logical units scaled by `RasterizationScale` for compass hit-testing.

The manager origin is recomputed every tick via
`ComputeScreenOriginQ`/`ComputeScreenOriginW` to limit drift, but the *grab
offset* is not.

### 5. Grab-offset precision

WPF's `DragDelta` keeps the cursor at the exact spot it grabbed (fallback
`(3,3)`). UnoDock approximates: the tracker measures `cursor − windowTopLeft` at
`Start()`, and the macOS tear-off path uses a synthesized offset (content-view
center X + `InitialTitleBarGrabOffset = 18` Y). If the window has not finished
positioning when the offset is sampled, the fallback puts the grab point at the
window's horizontal center / a fixed Y, so the window can "jump" under the cursor
on the first frame — the very artifact WPF's pre-positioning avoids.

### 6. No window reuse for the last content item

WPF's `StartDraggingFloatingWindowForContent` reuses an existing single-item
floating window when the content being dragged is the last in its pane
([DockingManager.cs:1746](../externals/AvalonDock/source/Components/AvalonDock/DockingManager.cs#L1746)).
UnoDock's version always calls `CreateFloatingWindow` unconditionally — dragging
the last tab out of a floating window creates a redundant new window instead of
reusing the existing one.

### 7. Missing lifecycle / model-sync features

- **`ContentFloating` (cancelable) and `ContentFloated` events** — WPF raises
  these around the float so hosts can veto or react. UnoDock's path does not.
- **Persisting position/size back to the model** — WPF's
  `UpdatePositionAndSizeOfPanes` (driven by `WM_EXITSIZEMOVE`) writes
  `FloatingLeft/Top/Width/Height` and raises `RaiseFloatingPropertiesUpdated`
  after a drag. UnoDock seeds `FloatingWidth/Height` before float but has no
  equivalent write-back of the final dropped geometry to the layout model, so
  serialized layouts can lose the floated window's last position.
- **Maximize/restore state restore** — WPF restores `IsMaximized` on load and
  keeps the model in sync (`UpdateMaximizedState`). UnoDock has a custom caption
  maximize button but no model round-trip.
- **Keyboard window moving** — WPF supports
  `AllowMovingFloatingWindowWithKeyboard` (arrow keys nudge the window). No
  UnoDock counterpart.

### 8. Window chrome & ownership differences (consequences, not bugs)

Because the OS title bar can't be used as the drag surface the same way, UnoDock
strips native chrome (`HideNativeWindowChrome`: `SetBorderAndTitleBar(false,false)`
plus Win32 `WS_CAPTION` removal) and draws its own title bar on Windows, and on
macOS must disable window tabbing (`DisableLastWindowTabbing`) so the new window
isn't merged into the main window's tab bar. WPF relies on owner/Z-order
(`GetWindowZOrder`, `Owner`); UnoDock manually calls `OrderWindowFront` /
`BringToFrontWindows` each time the cursor leaves the manager and keeps the
compass overlay in a separate `HWND_TOPMOST` / high-`NSWindowLevel` window.
These are working substitutes, but they are hand-maintained Z-order logic where
WPF leaned on the window manager.

## Suggested priorities

1. **Wire up native window dragging** — call
   `PerformWindowDragFromTitleBarCenter` on macOS and add the
   `WM_NCLBUTTONDOWN`/`HTCAPTION` send on Windows, retiring the timer trackers
   (or keeping them only as the overlay/hit-test driver). Removes gaps #1, #2,
   #3 in one move and matches WPF fidelity.
2. **Add window reuse** (#6) and **geometry write-back** (#7) — small, correctness-
   affecting, independent of the drag mechanism.
3. **Mid-drag DPI re-check** (#4) and **grab-offset precision** (#5) — only if the
   timer approach is retained; the native-drag handoff makes both moot.

---

# Part 3 — Refactoring proposal: native OS-driven dragging

> **Implementation status (session 26).** This refactor shipped, but with one
> platform outcome different from the proposal below:
> - **Windows** — native handoff implemented and used (`WM_NCLBUTTONDOWN`/
>   `HTCAPTION` + HWND subclass). As designed.
> - **macOS** — native handoff **abandoned; macOS keeps the timer tracker.**
>   `-[NSWindow performWindowDragWithEvent:]` only starts a drag when called from
>   the *live* `mouseDown:` `NSEvent`. UnoDock's tear-off comes from an Uno
>   `PointerMoved` (no real `NSEvent`) and the button-down is owned by the main
>   window's tab, so a synthesized event never attaches a drag (window stays put,
>   no `NSWindowDidMove`, no overlay). macOS has no `WM_NCLBUTTONDOWN`-style
>   message usable outside the event handler. The "macOS implementation" section
>   below is therefore **not viable as written** — kept for the record. See
>   [session26.md](session26.md).
>
> The Strategy/Template-Method/Factory structure (Part 4) still pays off: macOS's
> `CreateNativeDrag()` returns null and the manager transparently falls back to
> the proven `FloatingWindowDragTracker`, with no `#if` in the orchestration.

This part proposes the "big refactor" to bring UnoDock's drag-to-float as close
to WPF AvalonDock as the platforms allow. The thesis is simple:

> **Stop driving the drag from a timer. Let the OS run its native window-move
> loop, exactly like AvalonDock does, and merely *observe* the native move/end
> events to drive the docking overlay and commit the drop.**

The earlier assumption that "Uno `Window` is not a `FrameworkElement`, so we
can't do what WPF does" is only half true. WPF's mechanism does **not** actually
require a managed `Window` subclass — it requires (a) the ability to send
`WM_NCLBUTTONDOWN` to the window's native handle, and (b) the ability to receive
the window's native messages. UnoDock already has the native handle on both
platforms (`_windowsHwnd`, `_nsWindow`). We can hook the native handle directly,
**below** the Uno `Window` abstraction, and never need to subclass anything
managed.

## Design goals

1. **Native fidelity** — the window is glued to the cursor by the compositor, no
   timer lag/jitter (closes Gap #1).
2. **Event-driven drop lifecycle** — commit/abort come from real OS end-of-move
   signals, not polled button transitions (closes Gaps #2, #3).
3. **One code path, two thin platform shims** — a single
   `INativeWindowDrag` abstraction with a Windows and a macOS implementation, so
   `DockingManager` orchestration is platform-agnostic.
4. **Behavioral parity** — pre-position under the cursor with a real `DragDelta`,
   reuse the last floating window, raise `ContentFloating`/`ContentFloated`, and
   write geometry back to the model (closes Gaps #4–#7).
5. **Delete code** — remove `FloatingWindowDragTracker`,
   `WindowsFloatingWindowDragTracker`, the watchdog, and the `MinTicksBeforeDrop`
   heuristic. Less code, fewer race conditions.

## Target architecture

Introduce a platform-neutral abstraction that mirrors the role AvalonDock's
`FilterMessage` + `AttachDrag` play:

```csharp
namespace AvalonDock.Controls
{
    // What DockingManager talks to. One instance per active drag.
    internal interface INativeWindowDrag : IDisposable
    {
        // Position the window so the cursor sits at grabOffset inside it, then
        // hand control to the OS native move loop. Mirrors AvalonDock.AttachDrag
        // + the WM_NCLBUTTONDOWN/HTCAPTION send.
        void BeginDrag(Point cursorScreen, Point grabOffset);

        // Raised continuously while the OS moves the window (mirrors WM_MOVING /
        // NSWindowDidMoveNotification). Carries the live cursor in screen coords.
        event Action<Point> Moving;

        // Raised once when the OS move loop ends on mouse-up
        // (mirrors WM_EXITSIZEMOVE). Carries the final cursor position.
        event Action<Point> Ended;
    }
}
```

`DockingManager` keeps its existing overlay/compass logic but subscribes to these
two events instead of owning a timer:

```csharp
// Replaces StartDragTracking / StartWindowsDragTracking entirely.
private void StartNativeDrag(LayoutFloatingWindowControl fwc, Point grabOffset)
{
    var drag = fwc.CreateNativeDrag();              // platform factory on the control
    var overlay = ((IOverlayWindowHost)this).ShowOverlayWindow(fwc);
    overlay?.DragEnter(fwc);

    drag.Moving += cursor =>
    {
        var (ox, oy) = ComputeScreenOrigin();        // unified W/macOS origin
        UpdateOverlayDragStateForPoint(cursor.X - ox, cursor.Y - oy, fwc);
    };
    drag.Ended += _ =>
    {
        var target = _overlayActiveTarget;
        if (target != null) _overlayWindow?.DragDrop(target);
        EndOverlayDrag(fwc);
        ((IOverlayWindowHost)this).HideOverlayWindow();
        UpdatePositionAndSizeOfPanes(fwc);           // NEW: geometry write-back
        drag.Dispose();
    };

    drag.BeginDrag(NativeCursor(), grabOffset);
}
```

No `DispatcherTimer`, no watchdog, no `MinTicksBeforeDrop`, no button polling.

## Windows implementation

The Win32 mechanism is a direct port of AvalonDock. Two pieces:

### a) Hand off to the OS move loop

After positioning the window, send the same message WPF sends:

```csharp
// WindowsNativeWindowDrag.BeginDrag
MoveWindow(cursorScreen.X - grabOffset.X, cursorScreen.Y - grabOffset.Y);
var lParam = ((int)cursorScreen.Y << 16) | ((int)cursorScreen.X & 0xFFFF);
SendMessage(_hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, (IntPtr)lParam);
```

Because the physical button is still down (the tear-off gesture never released
it), Windows enters its modal move loop — identical to Part 1.

### b) Observe native messages via an HWND subclass

This is the piece that replaces the timer. Subclass the floating window's HWND
and intercept the move-loop messages — the WinUI equivalent of AvalonDock's
`_hwndSrc.AddHook(FilterMessage)`:

```csharp
// Install once, right after the HWND is known (EnsureWindowsHwnd):
_origWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _newWndProcPtr);

IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
{
    switch (msg)
    {
        case WM_MOVING:        // RECT* in lParam → raise Moving(cursor)
            Moving?.Invoke(GetCursorScreen());
            break;
        case WM_EXITSIZEMOVE:  // mouse released, move loop ended
            Ended?.Invoke(GetCursorScreen());
            break;
    }
    return CallWindowProc(_origWndProc, hwnd, msg, w, l);
}
```

> Prefer `SetWindowSubclass`/`DefSubclassProc` (comctl32) over raw
> `GWLP_WNDPROC` swapping — it composes safely with WinUI's own subclassing and
> avoids ordering bugs on teardown. Either gives the same messages.

This is strictly more faithful than today's `WindowsFloatingWindowDragTracker`
and removes `GetAsyncKeyState`/`GetCursorPos` polling.

## macOS implementation

> **⚠ Not viable — abandoned in session 26.** The approach below was implemented
> and tested, but `performWindowDragWithEvent:` requires the live `mouseDown:`
> `NSEvent` and does not attach a drag from a synthesized event in UnoDock's
> pipeline. macOS keeps the timer tracker. Retained here only to explain why.
> See the status banner at the top of Part 3.

The macOS analogue **already exists** — it just isn't wired up.

### a) Hand off to AppKit's drag loop

`MacOSWindowTabbing.PerformWindowDragFromTitleBarCenter`
([Compat/MacOSWindowTabbing.cs:434](../src/UnoDock/Compat/MacOSWindowTabbing.cs#L434))
synthesizes an `NSEventTypeLeftMouseDown` and calls
`-[NSWindow performWindowDragWithEvent:]`. That is the exact counterpart of
`WM_NCLBUTTONDOWN`/`HTCAPTION`: AppKit takes over and drags the window natively
until mouse-up. Generalize it to accept an explicit grab point so it matches the
`DragDelta` chosen at tear-off, then call it from `BeginDrag`.

### b) Observe native move + end

UnoDock already registers `NSWindowWillMoveNotification` via a block observer
([RegisterWindowWillMove](../src/UnoDock/Compat/MacOSWindowTabbing.cs#L339)).
Reuse the *same* block-observer machinery for two more notifications:

- **`NSWindowDidMoveNotification`** → raise `Moving` (read the live cursor with
  `CGEventGetLocation`). This replaces the 16 ms tick.
- **End of drag** → AppKit posts no "drag ended" notification, so install a
  one-shot local monitor for `NSEventMaskLeftMouseUp`
  (`+[NSEvent addLocalMonitorForEventsMatchingMask:handler:]`) and raise `Ended`
  from it, then remove the monitor. (Alternative: observe the window's frame
  going quiet, but the event monitor is precise and cheap.)

This deletes both `FloatingWindowDragTracker` **and** the 200 ms watchdog: with a
real native drag, re-grabbing an existing floating window's title bar is handled
by AppKit directly — the window is already draggable, and `NSWindowDidMove` fires
without any polling backstop.

## Shared orchestration & parity fixes

Fold the remaining gaps into the same refactor while the code is open:

### DragDelta / pre-positioning (Gaps #4, #5)

Compute a real grab offset at tear-off, exactly like AvalonDock:

- In the tab gesture handler ([LayoutDocumentPaneControl.OnTabStripPointerMoved](../src/UnoDock/Controls/LayoutDocumentPaneControl.cs#L214)),
  capture the pointer position **relative to the dragged tab/title** at the
  moment the threshold is crossed. That vector *is* `DragDelta`.
- Pass it through `StartDraggingFloatingWindowForContent` to `BeginDrag`, which
  positions the window at `cursor − DragDelta` before the OS handoff. No more
  content-center / `InitialTitleBarGrabOffset = 18` guess, no first-frame jump.
- Per-monitor DPI: once the OS owns the move, the compositor handles DPI
  crossings, so the WPF re-check loop is no longer needed at all.

### Window reuse (Gap #6)

Port AvalonDock's "reuse the last single-item floating window" branch
([DockingManager.cs:1746](../externals/AvalonDock/source/Components/AvalonDock/DockingManager.cs#L1746))
into UnoDock's `StartDraggingFloatingWindowForContent`: scan `_fwList` for a
floating window that already hosts only this content and re-drag it instead of
creating a new one.

### Lifecycle events & geometry write-back (Gap #7)

- Raise a cancelable `ContentFloating` before creating the window and
  `ContentFloated` after; honor `Cancel`.
- Add `UpdatePositionAndSizeOfPanes(fwc)` (port of the WPF method) on `Ended`,
  writing `FloatingLeft/Top/Width/Height` back to every
  `ILayoutElementForFloatingWindow` and raising `RaiseFloatingPropertiesUpdated`,
  so serialized layouts round-trip the floated geometry.
- Optionally restore maximize state and add keyboard window moving for full
  parity (lower priority).

## Migration plan (incremental, each step shippable)

> **As executed (session 26):** steps 1–4 were done in one pass with the flag
> defaulting **on** (override with `UNODOCK_TIMER_DRAG=1`). The macOS shim was
> built then removed once on-device testing proved the handoff non-viable; the
> macOS timer trackers and watchdog are **retained**, not deleted. Only the
> Windows timer tracker is superseded by the native path.

1. **Introduce `INativeWindowDrag` + the two platform shims** behind a feature
   flag (`UseNativeDrag`), defaulting **off**. Keep the timer trackers in place.
   Add the Windows HWND subclass and macOS `NSWindowDidMove` + mouse-up monitor;
   wire `PerformWindowDragFromTitleBarCenter` (generalized) and the Windows
   `WM_NCLBUTTONDOWN` send.
2. **Move overlay/compass driving onto the `Moving`/`Ended` events.** Validate
   the overlay still lights up correctly with the flag on, side-by-side with the
   timer path off.
3. **Add real `DragDelta` capture** at tear-off and pre-positioning in
   `BeginDrag`.
4. **Flip the flag on** for both platforms; delete `FloatingWindowDragTracker`,
   `WindowsFloatingWindowDragTracker`, `StartWatchdog`/`OnWatchdogTick`,
   `_lastWinPos`, and `MinTicksBeforeDrop`.
5. **Land the parity fixes** — window reuse, `ContentFloating`/`ContentFloated`,
   geometry write-back.
6. **Remove the feature flag** and the now-dead origin/`skipInitialDelay`
   plumbing.

## Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| `WM_NCLBUTTONDOWN` requires the button to *still* be physically down at handoff | The tear-off gesture already holds it; ensure no `await`/dispatcher hop releases the gesture between detecting the threshold and `BeginDrag` (WPF uses `DispatcherPriority.Send` for the same reason). |
| HWND subclass conflicts with WinUI's own window proc | Use `SetWindowSubclass`/`DefSubclassProc` and remove the subclass on `Closed`. |
| macOS has no "drag ended" notification | One-shot `NSEventMaskLeftMouseUp` local monitor; remove it in `Ended`. |
| `performWindowDragWithEvent:` needs the synthetic event's `windowNumber`/timestamp to be plausible | Already handled in `PerformWindowDragFromTitleBarCenter` (pulls `NSApp.currentEvent` timestamp + `windowNumber`). |
| Borderless chrome (`WS_CAPTION` stripped) might affect `HTCAPTION` move | `WM_NCLBUTTONDOWN`/`HTCAPTION` works regardless of a visible caption; verify against the existing `HideNativeWindowChrome` window. |
| Regression risk during transition | Feature flag + side-by-side validation in steps 1–3 before deleting the timer path. |

## Expected outcome

> **Actual outcome (session 26).** Achieved on **Windows** (native OS-driven
> drag); **not** on macOS — see the Part 3 status banner. The paragraph below
> describes the original both-platforms goal.

After the refactor, UnoDock's drag-to-float matches AvalonDock structurally:
the OS owns window movement on both platforms, UnoDock only *observes* and drives
the docking overlay, and the timer/watchdog/polling scaffolding is gone. The
remaining UnoDock-specific code is the thin native-handle plumbing
(`SendMessage`/subclass on Windows, `performWindowDragWithEvent:` + notifications
on macOS) and the custom title-bar chrome — both unavoidable consequences of Uno
`Window` not being a `FrameworkElement`, neither of which blocks native-fidelity
dragging.

On macOS the timer tracker (`FloatingWindowDragTracker`) remains the drag driver;
the watchdog and polling scaffolding stay. The realistic macOS win is incremental
(smoother offset handling, the parity fixes), not the native move-loop — that
door is closed by AppKit's requirement for a live `mouseDown:` event.

---

# Part 4 — Structuring the platform split with design patterns

Part 3 introduced `INativeWindowDrag` with two implementations. That alone is a
**Strategy** (DockingManager depends on the abstraction; the platform is a
swappable strategy). But a flat interface with two from-scratch implementations
would duplicate the *invariant* part of the algorithm — pre-position, hand off,
observe, raise `Moving`/`Ended`, tear down — in both classes. That shared
skeleton is exactly what **Template Method** is for.

The recommendation is to **combine three small patterns**, each doing one job:

| Pattern | Role here |
| --- | --- |
| **Strategy** | `INativeWindowDrag` — what `DockingManager` talks to; decouples orchestration from platform. |
| **Template Method** | `NativeWindowDragBase` — fixes the drag *algorithm* once; subclasses fill only the native *primitives*. |
| **Factory Method** | `LayoutFloatingWindowControl.CreateNativeDrag()` — picks the concrete subclass per OS; callers never see `#if WINDOWS`. |

## The invariant skeleton (Template Method)

The base class owns the algorithm and the event plumbing. Everything that is
*identical* across platforms lives here exactly once:

```csharp
internal abstract class NativeWindowDragBase : INativeWindowDrag
{
    public event Action<Point> Moving;
    public event Action<Point> Ended;

    private bool _ended;

    // ── Template method: the invariant algorithm. Not overridable. ──
    public void BeginDrag(Point cursorScreen, Point grabOffset)
    {
        // 1. Pre-position under the cursor (DragDelta), like AvalonDock step 4.
        MoveWindowNative(cursorScreen.X - grabOffset.X,
                         cursorScreen.Y - grabOffset.Y);

        // 2. Start observing native move/end BEFORE the handoff so no frame is lost.
        InstallObservers();

        // 3. Hand control to the OS native move loop.
        HandOffToNativeMoveLoop(cursorScreen, grabOffset);
    }

    // Called by subclasses from their native callbacks — funnels into the events.
    protected void RaiseMoving(Point cursor) => Moving?.Invoke(cursor);

    protected void RaiseEnded(Point cursor)
    {
        if (_ended) return;          // idempotent: end fires exactly once
        _ended = true;
        RemoveObservers();
        Ended?.Invoke(cursor);
    }

    public void Dispose()
    {
        RemoveObservers();           // safe to call twice
        DisposeCore();
    }

    // ── Primitive operations: the ONLY things platforms implement. ──
    protected abstract void MoveWindowNative(double x, double y);
    protected abstract void HandOffToNativeMoveLoop(Point cursor, Point grab);
    protected abstract void InstallObservers();
    protected abstract void RemoveObservers();
    protected abstract Point GetCursorScreen();
    protected virtual  void DisposeCore() { }
}
```

Note what is *not* in the subclasses anymore: the `Moving`/`Ended` events, the
fire-once guard, the observe-before-handoff ordering, and `Dispose` idempotency.
Those are the bug-prone bits, and Template Method guarantees both platforms get
them identically.

## The platform primitives (concrete subclasses)

Each subclass is now tiny — just the native calls already sketched in Part 3:

```csharp
// Windows: WM_NCLBUTTONDOWN handoff + HWND subclass observer.
internal sealed class WindowsNativeWindowDrag : NativeWindowDragBase
{
    private readonly IntPtr _hwnd;
    private SUBCLASSPROC _proc;

    protected override void MoveWindowNative(double x, double y) =>
        SetWindowPos(_hwnd, IntPtr.Zero, (int)x, (int)y, 0, 0,
                     SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    protected override void HandOffToNativeMoveLoop(Point c, Point _)
    {
        var l = ((int)c.Y << 16) | ((int)c.X & 0xFFFF);
        SendMessage(_hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, (IntPtr)l);
    }

    protected override void InstallObservers() =>
        SetWindowSubclass(_hwnd, _proc = WndProc, IntPtr.Zero, IntPtr.Zero);

    protected override void RemoveObservers() =>
        RemoveWindowSubclass(_hwnd, _proc, IntPtr.Zero);

    protected override Point GetCursorScreen() { GetCursorPos(out var p); return new(p.X, p.Y); }

    private IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l, ...)
    {
        if (msg == WM_MOVING)       RaiseMoving(GetCursorScreen());
        if (msg == WM_EXITSIZEMOVE) RaiseEnded(GetCursorScreen());
        return DefSubclassProc(h, msg, w, l);
    }
}

// macOS: performWindowDragWithEvent: handoff + NSWindowDidMove / mouse-up monitor.
// NOTE: This subclass was built and then REMOVED in session 26 — the handoff is
// not viable (AppKit needs the live mouseDown: event). Shown for illustration;
// in the shipped code CreateNativeDrag() returns null on macOS and the timer
// tracker drives the drag.
internal sealed class MacOSNativeWindowDrag : NativeWindowDragBase
{
    private readonly nint _nsWindow;
    private IntPtr _didMoveObserver, _mouseUpMonitor;

    protected override void MoveWindowNative(double x, double y) =>
        MacOSWindowTabbing.MoveWindow(_nsWindow, x, y, 0, 0);

    protected override void HandOffToNativeMoveLoop(Point _, Point grab) =>
        MacOSWindowTabbing.PerformWindowDrag(_nsWindow, grab);   // generalized

    protected override void InstallObservers()
    {
        _didMoveObserver = MacOSWindowTabbing.RegisterWindowDidMove(
            _nsWindow, () => RaiseMoving(GetCursorScreen()));
        _mouseUpMonitor = MacOSWindowTabbing.AddLeftMouseUpMonitor(
            () => RaiseEnded(GetCursorScreen()));
    }

    protected override void RemoveObservers()
    {
        MacOSWindowTabbing.UnregisterObserver(_didMoveObserver);
        MacOSWindowTabbing.RemoveEventMonitor(_mouseUpMonitor);
    }

    protected override Point GetCursorScreen()
    {
        var (x, y) = MacOSWindowTabbing.GetCursorLocationQuartz();
        return new(x, y);
    }
}
```

## Wiring it (Factory Method)

The control already knows the OS and owns the handles, so it is the natural
factory. This keeps the single `#if` in one place instead of scattered through
`DockingManager`:

```csharp
// LayoutFloatingWindowControl — proposed form (macOS branch later dropped)
internal INativeWindowDrag CreateNativeDrag()
{
#if WINDOWS
    return new WindowsNativeWindowDrag(EnsureWindowsHwnd());
#else
    return OperatingSystem.IsMacOS()
        ? new MacOSNativeWindowDrag(_nsWindow)
        : new WindowsNativeWindowDrag(EnsureWindowsHwnd());
#endif
}
```

In the shipped code (session 26) the macOS branch returns **null** — the factory
only constructs `WindowsNativeWindowDrag`, and macOS falls through to the timer
tracker:

```csharp
internal INativeWindowDrag CreateNativeDrag()
{
    // macOS: not supported (no live mouseDown: NSEvent) → null → timer fallback.
    if (OperatingSystem.IsWindows())
    {
        EnsureWindowsHwnd();
        return _windowsHwnd != IntPtr.Zero ? new WindowsNativeWindowDrag(_windowsHwnd) : null;
    }
    return null;
}
```

`DockingManager.StartNativeDrag` (Part 3) is now fully platform-agnostic — it
calls `fwc.CreateNativeDrag()` and subscribes to `Moving`/`Ended`. No `#if`, no
`OperatingSystem.IsX()` in the orchestration.

## Why this split (and what to avoid)

- **Template Method over copy-paste:** the ordering rule "install observers
  *before* the OS handoff" and the "end fires exactly once" guard are subtle and
  identical on both platforms. Centralizing them in the base class is the whole
  point — a second implementation can't silently get them wrong.
- **Keep the interface (Strategy) too:** `DockingManager` should depend on
  `INativeWindowDrag`, not `NativeWindowDragBase`. That keeps the door open for a
  test double (a `FakeNativeWindowDrag` that raises `Moving`/`Ended` on command),
  which lets the overlay/drop logic be unit-tested with **no real window at all**
  — something impossible with today's timer-and-native-handle design.
- **Don't over-engineer:** two platforms do not need an Abstract Factory, a DI
  container, or a registry. One abstract base + two subclasses + one factory
  method is the right amount of structure. Resist adding a third abstraction
  layer "for future platforms" until a third platform actually exists (Uno's
  GTK/WASM targets do not support native window dragging anyway and would fall
  back to a non-draggable float).
- **Primitives must stay primitive:** each abstract method should be a thin
  native call with no branching. If a subclass primitive starts containing
  algorithm logic, that logic probably belongs back in the template method.

This pattern split slots directly into the Part 3 migration plan: Step 1 becomes
"introduce `NativeWindowDragBase` + the two subclasses + `CreateNativeDrag()`",
and the rest is unchanged.
