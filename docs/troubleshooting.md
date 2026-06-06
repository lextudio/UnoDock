# UnoDock Troubleshooting Guide

Tips and techniques accumulated across the development sessions. Covers Uno Platform /
macOS / WinUI 3 specifics encountered while porting AvalonDock.

---

## Diagnosing with file-based logging

Tests and DevFlow actions run on the UI thread but stdout is unreliable for timing.
Write to a fixed path instead:

```csharp
private static void Log(string msg)
{
    try { System.IO.File.AppendAllText("/private/tmp/unodock_foo.log", $"{DateTime.Now:HH:mm:ss.fff} {msg}\n"); }
    catch { }
}
```

- On macOS, `System.IO.Path.GetTempPath()` returns `/var/folders/…`, not `/tmp/`.
  Use `/private/tmp/` directly for a predictable path.
- Clear the log before each test run: `rm -f /private/tmp/unodock_foo.log`.
- Log every event entry/exit, not just the happy path, so you can tell whether a
  handler was reached at all.

---

## DevFlow HTTP API (running sample)

DevFlow exposes `http://localhost:9223/api/v1/` when the sample is running.

| Route | Use |
| ----- | --- |
| `GET  /api/v1/ui/screenshot` | Take a PNG of the main window |
| `GET  /api/v1/ui/tree` | Full accessibility element tree (JSON `{elements:[…]}`) |
| `GET  /api/v1/invoke/actions` | List all registered `[DevFlowAction]` methods |
| `POST /api/v1/invoke/actions/{name}` | Invoke an action: `{"args":["arg1"]}` |
| `POST /api/v1/ui/actions/click` | Click at screen coords: `{"x":100,"y":200}` |

Find the port first if unsure:

```bash
lsof -i TCP -s TCP:LISTEN 2>/dev/null | grep UnoDock
```

Parse the element tree with Python one-liners:

```bash
curl -s http://localhost:9223/api/v1/ui/tree | python3 -c "
import sys, json
d = json.load(sys.stdin)
def dump(node, depth=0):
    if node is None: return
    b = node.get('bounds') or {}
    aid = node.get('automationId') or ''
    print('  '*depth + f\"{node.get('className','?')} [{aid}] w={b.get('width',0):.0f} h={b.get('height',0):.0f}\")
    for c in (node.get('children') or []):
        dump(c, depth+1)
for el in d['elements']:
    dump(el)
" | head -60
```

---

## Pointer events in Uno / WinUI on macOS

### PointerMoved stops when cursor leaves the element

`PointerMoved` only fires while the cursor is over the element **or** while the element
holds pointer capture. Without capture, events stop at the element boundary — breaking
drag-to-float (which needs to detect cursor going 40 px below the tab strip).

```csharp
// In OnTabStripPointerPressed:
_tabStrip.CapturePointer(e.Pointer);

// In OnTabStripPointerReleased / drag-complete path:
_tabStrip.ReleasePointerCapture(e.Pointer);
```

### PointerPressed / Tapped reliability

On Uno/Skia macOS, `PointerPressed` on a `Border` is reliable when the `Border` has a
non-null `Background`. Without a background, hit-testing may fail silently.

`Tapped` fires after a complete press+release cycle; wire **both** `PointerPressed` and
`Tapped` for maximum compatibility.

### Global dismiss handler fires on the same click that opened it

If you attach a root-level `PointerPressed` handler synchronously inside a click handler,
the same pointer-press event is still bubbling — the new handler fires immediately and
undoes what you just did. Defer with `DispatcherQueue.TryEnqueue(Low, ...)`.

---

## Screen coordinates on macOS (Quartz vs WinUI)

macOS uses two coordinate systems:

- **Quartz / CoreGraphics**: origin at **top-left of primary screen**, Y increases **down**.
- **Cocoa / AppKit (NSWindow, NSEvent)**: origin at **bottom-left of primary screen**, Y increases **up**.
- **WinUI / Uno**: origin at **top-left of the window**, Y increases **down**.

### AppWindow.Position

Returns the window's **top-left corner in physical pixels** (Quartz coordinates).
Divide by `XamlRoot.RasterizationScale` (2.0 on Retina) to get logical pixels:

```csharp
var pos      = Window.Current.AppWindow.Position;  // physical px
var scale    = XamlRoot.RasterizationScale;        // 2.0 on Retina
var logicalX = pos.X / scale;
var logicalY = pos.Y / scale;
```

### Converting pointer position to screen logical coords

```csharp
var ptInWindow = e.GetCurrentPoint(null).Position; // window-local logical px
var appWin     = Window.Current.AppWindow;
var dpi        = XamlRoot.RasterizationScale;
var screenX    = appWin.Position.X / dpi + ptInWindow.X;
var screenY    = appWin.Position.Y / dpi + ptInWindow.Y;
```

`AppWindow.Position` can return `(0, 0)` on the FIRST launch of an Uno window before
the OS has assigned a real position. Check and fall back to `GetMainWindowContentOrigin`.

### setFrameTopLeftPoint vs AppWindow.Move

`AppWindow.Move(PointInt32)` takes **physical pixels**. `[NSWindow setFrameTopLeftPoint:]`
takes **Cocoa screen coordinates** (Y-up, logical). These are different — use the right
one for the right API.

---

## performWindowDragWithEvent pitfalls

`[NSWindow performWindowDragWithEvent:]` starts a native window drag loop. The event's
`locationInWindow` determines the **drag offset** — where the cursor is relative to the
window origin.

### Wrong coordinates when event is from a different window

If you pass `[NSApp currentEvent]` while the event originated in a **different** window
(e.g. the main window), `locationInWindow` is in that window's coordinate space. macOS
interprets this as the grab point on the **floating** window, putting the cursor at a
wrong position (often on the traffic-light buttons).

**Fix**: create a synthetic `NSEvent` with `locationInWindow` at the desired grab point
within the floating window (e.g. center of the title bar in Cocoa Y-up coords):

```csharp
// Cocoa Y-up: title bar top = windowHeight, title bar center Y = windowHeight - titleBarH/2
var locationInWindow = new NSPoint
{
    X = windowWidth / 2.0,
    Y = windowHeight - 14.0,   // 14 = titleBarH(28) / 2
};
// Create NSEvent via [NSEvent mouseEventWithType:location:…]
// then call [nsWindow performWindowDragWithEvent:syntheticEvent]
```

---

## Z-order: floating window covers main-window overlays

When a floating Uno `Window` is in front, overlays (compass, drop zones) that live in
the **main window's** visual tree are hidden behind it.

**Fix**: when the compass needs to be visible, temporarily bring the main NSWindow to
the front with `[nsWindow orderFront:]`. When the cursor leaves the compass area,
restore the floating window to the front.

```csharp
var mainNsWin = MacOSWindowTabbing.GetMainNsWindow();
MacOSWindowTabbing.OrderWindowFront(mainNsWin);          // show compass
// later:
MacOSWindowTabbing.OrderWindowFront(fwc.NsWindowHandle); // restore floating
```

---

## RenderTransform does not affect layout in WinUI/Uno

`UIElement.RenderTransform` rotates/scales the **rendered pixels only** — it does not
change how the element is measured or arranged. The element's layout bounds stay at
the pre-transform size.

**Consequence**: rotating a `TextBlock` 270° to show vertical text leaves the layout
bounds as a wide horizontal rectangle, causing clipping in a narrow container.

**Fix**: use a `Canvas` sized to the **post-rotation visual dimensions** and position
the TextBlock so its rendered center aligns with the canvas center:

```csharp
// Post-rotation: a (textW x lineH) element becomes (lineH x textW) visually.
// Canvas layout size = post-rotation size: Width=lineH, Height=textW.
var canvas = new Canvas { Width = lineH, Height = textW };
Canvas.SetLeft(tb, (lineH - textW) / 2.0);
Canvas.SetTop(tb,  (textW - lineH) / 2.0);
tb.Width = textW;
tb.RenderTransformOrigin = new Point(0.5, 0.5);
tb.RenderTransform = new RotateTransform { Angle = 270 };
canvas.Children.Add(tb);
```

---

## ItemsControl / ContentPresenter wrapping

When iterating `ItemsControl.ItemsPanelRoot.Children`, each child is a
**`ContentPresenter`** (the item container), NOT the `DataTemplate`-generated element
directly. `element is Border` checks will silently skip everything.

**Fix**: walk into the visual children to find the Border:

```csharp
private static Border FindTabBorder(DependencyObject container)
{
    if (container is Border b) return b;
    if (container is ContentPresenter cp)
        return cp.Content as Border ?? FindInVisualChildren(cp);
    return FindInVisualChildren(container);
}

private static Border FindInVisualChildren(DependencyObject node)
{
    int count = VisualTreeHelper.GetChildrenCount(node);
    for (int i = 0; i < count; i++)
    {
        var child = VisualTreeHelper.GetChild(node, i);
        if (child is Border b2) return b2;
        var found = FindInVisualChildren(child);
        if (found != null) return found;
    }
    return null;
}
```

Also defer `UpdateTabHighlights` via `DispatcherQueue.TryEnqueue(Low, …)` so the
DataTemplate visual tree is fully populated before you walk it.

---

## Popup requires XamlRoot on Uno/macOS

A standalone `Popup` (not inside the visual tree) needs `XamlRoot` set before
`IsOpen = true` — otherwise the open is silently ignored:

```csharp
_flyout = new Popup
{
    XamlRoot = XamlRoot,   // required on Uno/macOS
    IsOpen   = true,
};
```

### Popup.Opened event may not fire in Uno

Start post-open animations via `DispatcherQueue.TryEnqueue(High, …)` immediately after
setting `IsOpen = true` rather than relying on `Opened`:

```csharp
_flyout.IsOpen = true;
DispatcherQueue?.TryEnqueue(DispatcherQueuePriority.High, () =>
{
    translate.X = slideFrom;
    sb.Begin();
});
```

---

## ProtectedCursor is protected — can't set from outside

`UIElement.ProtectedCursor` can only be set from within the class itself. You cannot
do `someElement.ProtectedCursor = …` from a different class.

**Fix**: Set `ProtectedCursor` on `this` (the containing control) via
`PointerEntered`/`PointerExited` handlers on the child element:

```csharp
splitter.PointerEntered += (_, _) =>
    ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
splitter.PointerExited += (_, _) =>
    ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
```

Alternatively, subclass the control and set `ProtectedCursor` in the constructor —
but note that `Thumb` is **sealed** in Uno and cannot be subclassed.

---

## KeyboardAccelerator intercept by WinUI focus cycling

`KeyboardAccelerator` with `Ctrl+Tab` is intercepted by WinUI/Uno's own focus-cycle
system (which moves focus through toolbar buttons).

**Fix**: use `AddHandler(KeyDownEvent, handler, handledEventsToo: true)` on
`XamlRoot.Content` so you receive the event even after it's been marked handled,
and set `e.Handled = true` to suppress the default focus-cycle behavior:

```csharp
private void OnLoaded(object sender, RoutedEventArgs e)
{
    _ctrlTabHandler = (_, ke) =>
    {
        if (ke.Key != VirtualKey.Tab) return;
        CycleDocument(reverse: false);
        ke.Handled = true;
    };
    if (XamlRoot?.Content is UIElement root)
        root.AddHandler(KeyDownEvent, _ctrlTabHandler, handledEventsToo: true);
}
```

---

## Storyboard animations on standalone popups

`Storyboard.Begin()` requires the animated element to be in the visual tree. Start the
animation after `IsOpen = true` (not before), giving Uno one dispatcher frame to attach
the popup to the visual tree.

```csharp
// Wrong: translate.X = slideFrom BEFORE IsOpen = true — content invisible on first frame
// Right:
_flyout.IsOpen = true;
DispatcherQueue?.TryEnqueue(High, () =>
{
    translate.X = slideFrom;
    sb.Begin();             // now element is in tree
});
```

---

## Native P/Invoke on macOS (arm64)

**Message dispatch**: use the standard message-send entrypoint for everything.
The large-struct-return variant (`_stret`) does not exist on arm64 — all structs
up to two pointer widths are returned in registers through the normal entrypoint.

```csharp
// All variants map to the same underlying symbol on arm64:
[DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
private static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector);
```

**NSRect**: 32 bytes on arm64, returned in registers. Declare a matching C# struct and
a P/Invoke that returns it directly; no special entrypoint needed.

**Selectors**: register with `sel_registerName` once and cache the `IntPtr`. Calling it
repeatedly is safe but allocates on every call.

**Current event**: `[NSApp currentEvent]` returns `nil` when called outside an active
event handler (e.g. from a timer tick). Always null-check before passing the result to
`performWindowDragWithEvent:`.
