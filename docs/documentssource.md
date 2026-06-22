# DockingManager source bindings: `DocumentsSource` and `AnchorablesSource`

UnoDock's `DockingManager` can be driven from observable collections of view-models,
mirroring WPF AvalonDock:

- **`DocumentsSource`** (implemented) — an `IEnumerable` of document view-models. Each item
  becomes a `LayoutDocument` in the document well; its `Title` is reflected from the model and
  its content is rendered via `LayoutItemTemplate`.
- **`AnchorablesSource`** (this design) — an `IEnumerable` of tool-pane view-models. Each item
  becomes a `LayoutAnchorable`, positioned by `ILayoutUpdateStrategy`, hidden/shown per the
  model's `IsVisible`, with two-way `IsActive`/`IsSelected`/`IsVisible` sync.

The consumer (ILSpy/Roma) exposes `DockWorkspace.TabPages` → `DocumentsSource` and
`DockWorkspace.ToolPanes` → `AnchorablesSource`.

## How `DocumentsSource` works (the pattern to mirror)

File: `src/UnoDock/Controls/DockingManager.DocumentsSource.cs`.

- `DocumentsSourceProperty` DP → `OnDocumentsSourceChanged` → `Detach`(old)/`Attach`(new).
- `AttachDocumentsSource(layout, source)`: imports items not already present, creating a
  `LayoutDocument` per item via `CreateDocumentForModel`; subscribes to `CollectionChanged`.
- `CreateDocumentForModel`: `new LayoutDocument { Content = model }`, `AttachDocumentTitle`
  (reflects the model's `Title` property + `INotifyPropertyChanged`), then
  `LayoutUpdateStrategy?.BeforeInsertDocument` / fallback add to pane / `AfterInsertDocument`.
- `DocumentsSourceElementsChanged`: Add/Remove/Replace/Reset, then `Layout.CollectGarbage()`.
- Content templating: the document pane control gets `SelectedContentTemplate = LayoutItemTemplate`
  (DockingManager.cs ~1787), and `LayoutDocumentPaneControl` applies it to its
  `PART_ContentPresenter` from code-behind (`ApplyContentTemplate()` called in `OnApplyTemplate`)
  because Uno does not reliably refresh a `{TemplateBinding ContentTemplate}` set after the
  template is applied.

UnoDock does **not** reference the consumer's model types; it uses **reflection** on well-known
property names (`Title`, etc.). `AnchorablesSource` keeps this contract.

## `AnchorablesSource` design

Tool panes differ from documents in three ways that the implementation must handle:

1. **Multiple panes & placement.** Anchorables can live in several `LayoutAnchorablePane`s on
   different sides. Placement defers to `ILayoutUpdateStrategy.BeforeInsertAnchorable`; only when
   that returns `false` do we fall back to the first `LayoutAnchorablePane` (creating one under
   `RootPanel` if none exists, mirroring `LayoutAnchorable.InternalDock`).
2. **Visibility.** A tool-pane model has an `IsVisible` flag. When `false`, the anchorable must end
   up in `Layout.Hidden` (via `HideAnchorable(false)`, which records `PreviousContainer` so a later
   `Show()` restores its place) — not in a visible pane.
3. **Two-way state.** `IsActive`/`IsSelected`/`IsVisible` sync both directions between model and
   `LayoutAnchorable`, guarded against re-entrant loops.

### DockingManager.cs

- Remove the stub `public IEnumerable AnchorablesSource { get; set; }` (~line 88); the real DP
  lives in the new partial.
- In `OnLayoutChangedInternal` (~129–149), add `DetachAnchorablesSource(oldLayout, …)` and
  `AttachAnchorablesSource(newLayout, …)` beside the documents calls (~139 / ~148), so the binding
  re-homes when the layout instance changes.
- Reuse `LayoutItemTemplate`, `LayoutUpdateStrategy`, and the existing
  `SuspendAnchorablesSourceBinding` flag.
- When creating the anchorable pane control (~line 1788), pass
  `SelectedContentTemplate = LayoutItemTemplate` (mirror documents).

### New file: DockingManager.AnchorablesSource.cs

Symmetric to `DockingManager.DocumentsSource.cs`:

- `AnchorablesSourceProperty` DP (`IEnumerable`) + `OnAnchorablesSourceChanged`.
- `AttachAnchorablesSource(layout, source)` / `DetachAnchorablesSource` — Detach searches **both**
  `layout.Descendents()` and `layout.Hidden`.
- `FindHostAnchorablePane(layout)` — first `LayoutAnchorablePane` via `Descendents()` (nullable).
- `CreateAnchorableForModel(model, layout)`:
  1. `new LayoutAnchorable { Content = model }`.
  2. Copy `ContentId` (reflection); copy `CanClose`/`CanHide` if present; set
     `CanDockAsTabbedDocument = false` (tool windows never merge into the document well).
  3. `AttachAnchorableTitle` (reflect `Title` + `INotifyPropertyChanged`).
  4. `added = LayoutUpdateStrategy?.BeforeInsertAnchorable(layout, a, pane) ?? false`; if not added,
     add to `pane.Children` (or create a pane).
  5. `LayoutUpdateStrategy?.AfterInsertAnchorable(layout, a)`.
  6. Wire two-way sync handlers.
  7. If model `IsVisible == false` (reflection) → `a.HideAnchorable(false)`.
- `AnchorablesSourceElementsChanged` — honor `SuspendAnchorablesSourceBinding`; Add/Remove/Replace/
  Reset over Descendents+Hidden; finally `Layout?.CollectGarbage()`.

### Two-way sync (reflection + re-entrancy guard)

A private `_inAnchorableSync` flag wraps every cross-write in try/finally; each handler returns
early if it is set, preventing `model → layout → model` loops.

- **Model → Layout** (model `PropertyChanged`): `Title` → update; `IsVisible` → `a.Show()` /
  `a.HideAnchorable(false)`; `IsActive`/`IsSelected` → set on `a`.
- **Layout → Model** (`a.IsActiveChanged` / `a.IsSelectedChanged`, and the anchorable's `IsVisible`
  changes) → write back to the model's settable properties via reflection.

### LayoutAnchorablePaneControl content templating

`LayoutAnchorablePaneControl` currently has `SelectedContent` but no template. Add (mirroring
`LayoutDocumentPaneControl` 48–124):

- `SelectedContentTemplate` DP whose callback calls `ApplyContentTemplate()`.
- `ApplyContentTemplate()` sets `PART_ContentPresenter.ContentTemplate` (retype `_contentPresenter`
  to `ContentPresenter`). Call it from `OnApplyTemplate` (after `PART_ContentPresenter` is fetched)
  and a `ResolveContentTemplateFromManager()` (pulls `manager.LayoutItemTemplate`) in the ctor and
  on `Loaded`.

### VS2013 theme

`UnoDock.Themes.VS2013/Themes/Generic.xaml` — the anchorable control template's
`PART_ContentPresenter` (~line 437) needs
`ContentTemplate="{TemplateBinding SelectedContentTemplate}"` (belt-and-suspenders; the code-behind
`ApplyContentTemplate()` is authoritative — Uno does not reliably honor the template binding set
after apply, the same issue previously fixed on the document pane control).

## Risks

| Risk | Mitigation |
|---|---|
| Two-way sync infinite loop | `_inAnchorableSync` re-entrancy guard (try/finally) |
| Content template not applied (Uno timing) | Code-behind `ApplyContentTemplate()` from `OnApplyTemplate` |
| Hidden-on-attach lands in a visible pane | Create + insert (records PreviousContainer) then `HideAnchorable(false)` |
| Empty target panes pruned before first insert | Consumer seeds target panes / structural discovery (see Roma side) |
| Layout serialization | `ContentId` copied onto the LayoutAnchorable so `LayoutSerializationCallback` re-binds |
