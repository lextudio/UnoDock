# UnoDock Refactoring Plan — Testability, Platforms, and Parity

> Scope: how to evolve the **current** codebase (not a rewrite) so that the five
> standing challenges become tractable. Grounded in what already exists today:
> the model layer is linked from upstream, the drag-to-float refactor
> (`INativeWindowDrag` / `NativeWindowDragBase` / `WindowsNativeWindowDrag` /
> `DockingManager.StartNativeDrag`) has shipped, headless NUnit tests run on
> `net10.0-desktop`, and there is a DevFlow-driven parity harness under
> `tools/parity`.

## The five challenges

1. **Drag-to-float ≠ pop-a-child-window-in-code.** The interactive tear-off path
   (gesture → native move loop / timer tracker → overlay → drop) shares almost no
   code with "create a floating window programmatically," so testing one does not
   exercise the other.
2. **Drop-to-dock is hard to test.** The drop decision depends on cursor screen
   coordinates, overlay hit-testing, and live window geometry.
3. **Child-window screenshots are hard.** Floating windows are *separate* OS
   windows; the main-window screenshot path (`/api/v1/ui/screenshot`) does not
   capture them, and macOS multi-window capture is fragile.
4. **Three platform matrices** — Windows/Skia, Windows/WinUI, macOS/Skia — must
   stay green with healthy structure (no `#if` sprawl).
5. **Parity + code reuse with AvalonDock.**

These are not five separate problems. **Challenges 1–3 are all symptoms of the
same root cause: the docking *logic* is entangled with real OS windows, real
cursors, and real pixels.** The fix is one architectural move applied
consistently — push every OS/pixel dependency behind a seam — and the platform
and parity goals fall out of the same seams. The drag refactor already proved
the pattern (`INativeWindowDrag` Strategy + `NativeWindowDragBase` Template
Method + `CreateNativeDrag` Factory). This plan generalizes it.

---

## Guiding principle: separate *decision* from *actuation*

Every docking interaction has two layers:

| Layer | Examples | Depends on | Should be |
|---|---|---|---|
| **Decision** | "this cursor point is over the left dock target", "dropping here splits the panel", "tear off when 40px below tab strip" | pure geometry + the layout model | **deterministic, headless-testable** |
| **Actuation** | move the native window, read the global cursor, capture a screenshot, run the OS move loop | a real OS window / pointer / compositor | **a thin platform shim behind an interface** |

Today the decision layer is *partially* extracted: `OverlayWindowBridgeTests`
already test drop outcomes by calling `OverlayWindow.ApplyDrop` /
`IOverlayWindow.DragDrop` directly, and `OverlayTabTargetRules` is pure math.
That is the model to extend everywhere. The refactor is mostly **moving decision
code out of the actuation classes**, not writing new algorithms.

---

## Challenge 1 — unify "drag-to-float" and "float-in-code"

### Problem
`StartDraggingFloatingWindowForContent` (the drag path) and any programmatic
"float this content" call diverge: the drag path creates the window, positions it
under the cursor, and starts tracking; a code path would just create + show. Bugs
in window creation, model mutation, or geometry write-back can be present in one
and absent in the other.

### Plan
Refactor `DockingManager` float logic into a **single core + thin entry points**:

```
FloatContentCore(content, FloatRequest request) -> LayoutFloatingWindowControl
    request.Origin     = { Interactive(cursor, grabOffset) | Programmatic(rect) }
    request.StartDrag  = bool
```

- `FloatContentCore` owns: `CanFloat` check, `ContentFloating`/`ContentFloated`
  events (gap #7 in drag-to-float.md), window-reuse for the last item (gap #6),
  window creation, and the final geometry write-back contract.
- The **interactive** entry point supplies `Interactive` origin + `StartDrag=true`
  → calls `StartNativeDrag` (or the timer tracker fallback).
- A new **public `Float(content, rect?)`** entry point supplies `Programmatic`
  origin + `StartDrag=false` → shows the window at `rect`, no tracker.

**Test seam:** `FloatContentCore` is exercised headlessly with a
`FakeNativeWindowDrag : INativeWindowDrag` (raises `Moving`/`Ended` on command —
the design doc Part 4 already anticipates this double). One test body drives both
the code-float and the drag-float by swapping only `request.Origin`, so the two
paths can never silently diverge.

**Deliverables**
- [x] Public `DockingManager.Float(content, bounds?)` — code-driven float routed
      through the same path as the interactive tear-off (closes the missing-API gap).
- [x] `ContentFloating` (cancelable) / `ContentFloated` events — promoted from
      no-op interface stubs to real events, gated before window creation and raised
      after show. Honors `Cancel`. *(drag-to-float.md gap #7, events portion.)*
- [x] `FakeNativeWindowDrag` test double (subclasses the real
      `NativeWindowDragBase`) + headless tests: `FloatContentTests` (event/cancel/
      CanFloat/bounds-seed contract) and `NativeWindowDragBaseTests` (observe-before-
      handoff, end-fires-once, dispose idempotency). Full suite: 123 green.
- [x] Geometry write-back on drop end (`FloatingLeft/Top/Width/Height`) — gap #7
      remainder. *(Done: pure `Controls/FloatingGeometry.WriteBack` mirrors WPF
      `UpdatePositionAndSizeOfPanes`; `LayoutFloatingWindowControl.WritePositionAndSizeToModel`
      exposes it; `DockingManager.PersistFloatingGeometry` refreshes Left/Top from
      the live native window position and is called from all three drag-end handlers
      (native + macOS/Windows trackers) when the window stays floating. 4 headless
      tests; 131 headless green; 3 live DevFlow round-trips green, no regression.)*
- [x] Window reuse for the last single-item floating window (gap #6).
      *(Done: pure `Controls/FloatingWindowReuse.FindReusable` mirrors the WPF
      reference branch — content alone in its pane + an existing floating window
      hosting only it → reuse instead of `CreateFloatingWindow`. Wired into
      `StartDraggingFloatingWindowForContent`. 4 headless tests; full suite 127
      headless green; 3 live DevFlow round-trips green (no regression).)*
- [ ] Extract a named `FloatContentCore`/`FloatRequest` if the shared path grows
      beyond the current thin gate+events seam (deferred — current unification via
      `StartDraggingFloatingWindowForContent(startDrag:false)` already prevents
      divergence).

---

## Challenge 2 — make drop-to-dock testable without a real cursor

### Problem
The drop outcome depends on (a) where the cursor is in *screen* coords, (b) the
overlay's hit-test of drop areas, (c) the resulting layout-model mutation. (a)
and (b) need real windows; (c) is pure.

### Plan
The seam already half-exists (`IOverlayWindowHost`, `IDropArea`,
`IOverlayWindow`, `OverlayWindow.ApplyDrop`). Finish it:

1. **Define a pure hit-test:** `OverlayHitTester.Resolve(IEnumerable<IDropArea>,
   Point local) -> IDropTarget?`. Move all geometry/priority logic out of the
   live overlay window into this static function (mirrors `OverlayTabTargetRules`).
2. **Drive drops by coordinate, not by cursor:** keep `ApplyDrop` taking an
   explicit target + floating model (already tested). Add a coordinate-level test:
   feed a synthetic drop-area set + a local point through `OverlayHitTester` →
   `ApplyDrop`, assert the mutated `LayoutRoot` signature (the
   `DescribeLayout` helper in `OverlayWindowBridgeTests` is the template).
3. **One coordinate translator** behind an interface
   (`IDragCoordinateSpace.ScreenToManagerLocal`) so the screen→local step (the
   only OS-dependent part) is faked in tests and real in `StartNativeDrag` /
   trackers. This collapses `ComputeScreenOriginQ`/`ComputeScreenOriginW` into one
   testable abstraction.

**Deliverables**
- [x] `OverlayHitTester` pure resolver + unit tests. *(Done: extracted
      `SelectActiveOverlayAreas` + hit-zone/inflation/tightest-pane logic out of
      `DockingManager` into `Controls/OverlayHitTester.cs`; `DockingManager` now
      passes only the splitter bool. 6 headless tests in `OverlayHitTesterTests`.)*
- [x] `IDragCoordinateSpace` extracted from the per-tick origin computation.
      *(Done: `Controls/DragCoordinateSpace.cs` defines the `IDragCoordinateSpace`
      seam + pure `DragCoordinateMath` (CombineOrigin / ScreenToManagerLocal).
      `DockingManager` implements the seam; `ComputeScreenOriginQ/W` and the
      `Moving` handler now route through the pure math instead of inline
      per-platform arithmetic. 6 headless tests in `DragCoordinateMathTests`.)*
- [x] Coordinate→drop→layout-mutation tests for each `DropTargetType`.
      *(Done: `DropToLayoutMutationTests` — 13 headless tests over `ApplyDrop`
      covering manager outer edges, beside-pane splits, inside-pane tabbing
      (with tab-index ordering), anchorable inside, "as-anchorable" mirroring,
      and a coordinate→`OverlayHitTester`→drop pipeline test. Full suite: 116
      green on net10.0-desktop.)*

This makes the *entire* drop decision (challenge 2) headless; only the final
window move/close remains actuation.

---

## DevFlow as a second test tier (integration)

The headless NUnit tests cover the **decision** layer; DevFlow covers **actuation**
— does the live app actually render, float, dock. The two are complementary, and
the repo already leans this way (`tools/parity`, the `[DevFlowAction]` verbs in
`UnoDock.Sample/DockDiagnostics.cs`). Made reliable (not flaky) via three rules:

1. **Assert on structured state, not screenshots.** Added a `dock-query-layout`
   verb returning deterministic JSON (document/anchorable panes, floating windows,
   hidden) — docked panes scoped to `RootPanel` so floated content is reported only
   under `floatingWindows`. Screenshots stay for human-reviewed parity only.
2. **Drive deterministic verbs**, not raw cursor coordinates, for the structural
   tests (raw injected drags reserved for a few true end-to-end drag tests).
3. **One reusable client + opt-in execution.** `UnoDock.Tests/Integration/`:
   `DevFlowClient` (status/tree/invoke, tolerant of the `returnValue` envelope) +
   `DockLayoutSnapshot` parser + `FloatRoundTripIntegrationTests`. Gated on
   `DEVFLOW_TEST_PORT` → **skipped** in the default headless run, so CI stays green
   without the app.

**Status: working end-to-end.** Three live round-trip tests, all green against a
running sample and skipped cleanly when none is running (full suite: 123 headless
passed, 3 integration skipped / green-when-live):
- `QueryLayout_ReturnsParseableSnapshot` — structured query is parseable.
- `FloatAnchorable_MovesItIntoAFloatingWindow` — float tears a tool out of its
  docked pane into a floating window.
- `FloatDocument_ThenDropCenter_DocksBackIntoADocumentPane` — full float→drop
  loop: float the active document, then `dock-simulate-drop Center` re-docks it.

Tests are **order-independent**: `SetUp` re-docks any windows a prior test left
floating, avoiding the cumulative-state trap that makes integration suites flaky.
These are the live counterparts of `FloatContentTests`/`DropToLayoutMutationTests`
and the foundation for Challenge 3 / plan Step 5.

Two bugs were caught *only* by running live (not by static reasoning): the DevFlow
response envelope field is `returnValue` (client parser fixed), and
`dock-query-layout` initially double-counted floated tools because
`layout.Descendents()` walks into floating windows' internal panes (fixed by
scoping docked panes to `RootPanel`). Evidence that the integration tier earns its
keep.

## Challenge 3 — child-window screenshots

### Problem
DevFlow's screenshot captures the main window's visual tree. Floating windows are
separate OS windows and are invisible to it; macOS multi-window capture is
fragile.

### Plan (two independent tracks)

**Track A — make screenshots unnecessary for most tests.** Once challenges 1–2
land, the *content* and *layout* of a floating window are fully assertable from
the model (titles, panes, geometry write-back) and from `/api/v1/ui/tree`
scoped to the floating window's own element subtree. Prefer tree/model assertions
over pixels — this is already the DevFlow philosophy in CLAUDE.md ("only when
necessary, fetch a screenshot").

**Track B — when pixels are genuinely needed, register floating windows with
DevFlow.** Each floating window is a real Uno `Window`; DevFlow can enumerate
windows. Concretely:
- [ ] Have `LayoutFloatingWindowControl` register its `Window`/native handle with
      the DevFlow agent on show and deregister on close.
- [ ] Extend the screenshot endpoint usage to accept a window/element id that
      resolves to a floating window's root, capturing that window's compositor
      surface rather than the main window.
- [ ] On macOS, capture by `NSWindow` (the handle is already held in
      `MacOSWindowTabbing`); avoid `CGWindowList` full-screen scraping.

Track A is the priority; Track B is a DevFlow enhancement (lives in
`wpf-labs/src/DevFlow`) used only for true visual-parity scenes.

---

## Challenge 4 — three platforms, healthy structure

### Current risk
`#if WINDOWS_APP_SDK` / `OperatingSystem.IsMacOS()` checks risk spreading through
`DockingManager`. The drag refactor already showed the antidote: the Factory
(`CreateNativeDrag`) confines the single `#if` to one method and the orchestrator
stays platform-agnostic.

### Plan — apply the same discipline to every OS touchpoint
Enumerate the OS-dependent operations and give each **one** interface + per-platform
implementation, selected by a factory; **zero `#if` in `DockingManager`**:

| Concern | Interface | Win/WinUI | Win/Skia | macOS/Skia |
|---|---|---|---|---|
| Native drag handoff | `INativeWindowDrag` ✅ exists | `WM_NCLBUTTONDOWN` ✅ | (timer fallback) | timer tracker |
| Cursor / button read | `IPointerProbe` (new) | `GetCursorPos`/`GetAsyncKeyState` | same Win32 | `CGEvent*` |
| Window move/order/chrome | `INativeWindowOps` (new) | Win32 / WinAppSDK | Win32 | `MacOSWindowTabbing` |
| Coordinate space | `IDragCoordinateSpace` (new, ch.2) | Win32 DPI | Win32 DPI | Quartz/Cocoa flip |

- **Windows/Skia vs Windows/WinUI**: these differ by *renderer/SDK surface*, not
  Win32 — most native code is shared; divergence is layout (the WinUI Grid
  `ActualWidth` overflow already documented in winui.md) and chrome. Add a CI leg
  for **each** of the three so a green build means all three.
- **Document the matrix** and gate platform-only code through the factories so a
  missing platform degrades to a defined fallback (e.g. non-draggable float on
  WASM/GTK) rather than a compile break.

**Deliverables**
- [x] `IDragCoordinateSpace` (Step 1) and `IPointerProbe` extracted. *(Done:
      `Controls/PointerProbe.cs` — `IPointerProbe` (cursor + left-button) with a
      `PointerProbe.Create()`/`.Shared` factory confining the macOS `#if` to one
      place; impls delegate to the existing native statics (no new P/Invoke).
      `DockingManager.NativeCursorScreen` routes through it; `FakePointerProbe` +
      `PointerProbeTests` for the seam. 134 headless green, 3 live round-trips green.)*
- [ ] `INativeWindowOps` (window move/order/chrome) — the larger remaining seam;
      ~24 `OperatingSystem.Is*` branches still live in `DockingManager`'s overlay/
      window plumbing (deep macOS-vs-Windows native code, untestable on a Windows
      box — deferred for incremental extraction, not a single risky pass).
- [x] CI: three build+test legs. *(Done: `.github/workflows/ci.yml` — Windows/Skia
      headless tests, Windows/WinUI build via **VS MSBuild** (`microsoft/setup-msbuild`;
      `dotnet msbuild` also trips UNOB0008 — verified locally, only VS MSBuild.exe
      works), macOS/Skia build+tests. Both Windows legs verified locally this
      session: net10.0-desktop suite green (134) and the WinUI
      net10.0-windows10.0.19041.0 build green (0 errors, DLL produced). macOS leg
      needs a live CI run.)*

---

## Challenge 5 — parity + reuse with AvalonDock

### Plan
1. **Keep the source-port discipline** (design.md): model/converters/commands
   linked from the submodule; only fork what truly can't compile. Track a
   "fork ledger" — every forked file lists *why* it diverged so re-syncs are cheap.
2. **Port AvalonDock's own tests** as the parity oracle. The model/serialization
   tests are already linked (`LayoutModelTests`, `LayoutSerializationUnitTests`,
   etc.). Extend coverage to the decision-layer logic extracted above by porting
   AvalonDock's drop/docking regression tests against `OverlayHitTester` +
   `ApplyDrop`.
3. **Behavioral parity ledger** in drag-to-float.md is the authority for runtime
   gaps; fold the remaining items (#6 reuse, #7 events/geometry) into Challenge 1's
   `FloatContentCore`.
4. **Visual parity** via `tools/parity` (DevFlow side-by-side vs the WPF sample).
   Add floating-window scenes once Challenge 3 Track B lands.

---

## Sequencing

The order is chosen so each step unlocks tests for the next, and risky window
work is always done behind an already-tested decision layer.

1. **Decision-layer extraction (ch. 2 core).** `OverlayHitTester`,
   `IDragCoordinateSpace`. Pure, high test ROI, no behavior change. *Unlocks
   headless drop tests.*
2. **`FloatContentCore` + `Float(...)` + `FakeNativeWindowDrag` (ch. 1).**
   Unifies the two float paths; lands events + geometry write-back. *Unlocks the
   "code-float == drag-float" guarantee and AvalonDock parity items #6/#7.*
3. **Platform-concern interfaces + factories (ch. 4).** Group existing native
   code; remove `#if` from the orchestrator; stand up the 3-leg CI.
4. **Parity test port (ch. 5).** Run AvalonDock drop/docking tests against the
   extracted decision layer.
5. **DevFlow floating-window capture (ch. 3 Track B).** Only after the model/tree
   assertions (Track A) prove insufficient for a given scene.

Each step ends with: green NUnit on `net10.0-desktop`, green build on all three
platform legs, and (steps 2/5) a DevFlow-verified runtime check + session log.

## Success criteria

- A single parameterized test proves code-float and drag-float produce identical
  model mutations and geometry write-back.
- Every `DropTargetType` outcome is asserted headlessly from cursor coordinate →
  layout mutation, with **no real window**.
- `DockingManager` contains no `#if`/`OperatingSystem.Is*` in its drag/float
  orchestration; all OS access is behind the four interfaces.
- Three CI legs (Win/WinUI, Win/Skia, macOS/Skia) green.
- AvalonDock's ported drop/docking regression tests pass against UnoDock's
  decision layer.
- Floating-window content/layout is verifiable from `/api/v1/ui/tree`; pixel
  capture exists for floating windows when a scene truly needs it.
</content>
</invoke>
