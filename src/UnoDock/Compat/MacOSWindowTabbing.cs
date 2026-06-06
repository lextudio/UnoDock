#if !WINDOWS
using System;
using System.Runtime.InteropServices;

namespace AvalonDock.Hosting;

/// <summary>
/// macOS window management helpers:
/// - Disables NSWindow tab-bar merging (tabbingMode = Disallowed)
/// - Positions floating drag-preview windows via setFrameTopLeftPoint:
///   using NSEvent.mouseLocation (documented Cocoa screen coords, Y-up)
///   rather than AppWindow.Move which has an unknown coordinate mapping on Uno.
/// </summary>
internal static class MacOSWindowTabbing
{
    private const string ObjC = "/usr/lib/libobjc.dylib";
    private const nint NSWindowTabbingModeDisallowed = 2;
    internal const double InitialTitleBarGrabOffset = 18.0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NSPoint { public double X; public double Y; }

    // ── ObjC runtime ────────────────────────────────────────────────────────
    [DllImport(ObjC, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass(string name);

    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr Sel(string name);

    // This type is compiled into the cross-platform Skia build (#if !WINDOWS),
    // so it can run on macOS, Linux AND Windows. The selector/class fields below
    // are initialized eagerly by the type initializer; calling libobjc on a
    // non-macOS OS throws DllNotFoundException, which surfaces as a
    // TypeInitializationException the first time ANY member is touched (e.g. the
    // cross-platform DragLog helper). Guard the P/Invoke so the type initializer
    // is safe everywhere; the ObjC methods themselves are only ever called behind
    // OperatingSystem.IsMacOS() checks at the call sites.
    private static IntPtr SafeSel(string name) => OperatingSystem.IsMacOS() ? Sel(name) : IntPtr.Zero;
    private static IntPtr SafeClass(string name) => OperatingSystem.IsMacOS() ? GetClass(name) : IntPtr.Zero;

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSend(IntPtr rcv, IntPtr sel);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgSend_nint(IntPtr rcv, IntPtr sel, nint value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgSend_double(IntPtr rcv, IntPtr sel, double value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgSend_NSPoint(IntPtr rcv, IntPtr sel, NSPoint pt);

    // NSPoint-returning objc_msgSend: on ARM64 small structs come back in
    // the normal return registers, so the standard entrypoint works.
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern NSPoint MsgSend_retNSPoint(IntPtr rcv, IntPtr sel);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern double MsgSend_retDouble(IntPtr rcv, IntPtr sel);

    // ── Selectors (computed once) ────────────────────────────────────────────
    private static readonly IntPtr _selSharedApp           = SafeSel("sharedApplication");
    private static readonly IntPtr _selWindows             = SafeSel("windows");
    private static readonly IntPtr _selLastObject          = SafeSel("lastObject");
    private static readonly IntPtr _selTabbingMode         = SafeSel("setTabbingMode:");
    private static readonly IntPtr _selFrameTopLeft        = SafeSel("setFrameTopLeftPoint:");
    private static readonly IntPtr _selMouseLocation       = SafeSel("mouseLocation");
    private static readonly IntPtr _selMainScreen          = SafeSel("mainScreen");
    private static readonly IntPtr _selFrame               = SafeSel("frame");
    private static readonly IntPtr _selFrameSize           = SafeSel("frame");    // returns NSRect
    private static readonly IntPtr _clsNSApp               = SafeClass("NSApplication");
    private static readonly IntPtr _clsNSEvent             = SafeClass("NSEvent");
    private static readonly IntPtr _clsNSScreen            = SafeClass("NSScreen");
    private static readonly IntPtr _selCurrentEvent        = SafeSel("currentEvent");
    private static readonly IntPtr _selPerformWindowDrag   = SafeSel("performWindowDragWithEvent:");
    private static readonly IntPtr _selContentView         = SafeSel("contentView");
    private static readonly IntPtr _selSetLevel            = SafeSel("setLevel:");

    // ── NSRect (needed for frame) ────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct NSRect { public NSPoint Origin; public NSPoint Size; }

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern NSRect MsgSend_retNSRect(IntPtr rcv, IntPtr sel);

    // NSRect-argument, NSRect-returning objc_msgSend — for convertRectToScreen:
    // and convertRect:toView:. On ARM64 the NSRect arg/return travel in the
    // standard registers, so the default entrypoint works (no objc_msgSend_stret).
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern NSRect MsgSend_NSRect_retNSRect(IntPtr rcv, IntPtr sel, NSRect arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern NSRect MsgSend_NSRect_IntPtr_retNSRect(IntPtr rcv, IntPtr sel, NSRect arg, IntPtr view);

    private static readonly IntPtr _selBounds            = SafeSel("bounds");
    private static readonly IntPtr _selConvertRectToScreen = SafeSel("convertRectToScreen:");
    private static readonly IntPtr _selConvertRectToView = SafeSel("convertRect:toView:");
    private static readonly IntPtr _selBackingScaleFactor = SafeSel("backingScaleFactor");
    private static readonly IntPtr _selScreen            = SafeSel("screen");

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSend_nuint_retIntPtr(IntPtr rcv, IntPtr sel, nuint idx);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nuint MsgSend_retNUInt(IntPtr rcv, IntPtr sel);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern byte MsgSend_retBool(IntPtr rcv, IntPtr sel);

    private static readonly IntPtr _selCount         = SafeSel("count");
    private static readonly IntPtr _selObjectAtIndex = SafeSel("objectAtIndex:");
    private static readonly IntPtr _selStyleMask     = SafeSel("styleMask");
    private static readonly IntPtr _selIsVisible     = SafeSel("isVisible");

    // For creating synthetic NSEvent: mouseEventWithType:location:modifierFlags:
    //   timestamp:windowNumber:context:eventNumber:clickCount:pressure:
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSend_syntheticMouseEvent(
        IntPtr cls, IntPtr sel,
        nint type,         // NSEventTypeLeftMouseDown = 1
        NSPoint location,  // in window coordinates (Cocoa Y-up)
        nuint modFlags,    // modifier flags
        double timestamp,  // seconds since system boot
        nint windowNumber, // NSWindow.windowNumber
        IntPtr context,    // nil (deprecated)
        nint eventNumber,  // event sequence number
        nint clickCount,   // 1
        float pressure);   // 1.0

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint MsgSend_retNInt(IntPtr rcv, IntPtr sel);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern double MsgSend_retDouble2(IntPtr rcv, IntPtr sel);

    private static readonly IntPtr _selWindowNumber      = SafeSel("windowNumber");
    private static readonly IntPtr _selTimestamp         = SafeSel("timestamp");
    private static readonly IntPtr _selOrderFront        = SafeSel("orderFront:");
    private static readonly IntPtr _selSetAlphaValue     = SafeSel("setAlphaValue:");
    private static readonly IntPtr _selMouseEventFactory = SafeSel("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:");

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Sets NSWindowTabbingModeDisallowed on NSApp.windows.lastObject so it
    /// appears as an independent OS window, not a tab in the primary window.
    /// Returns the NSWindow handle for subsequent positioning calls.
    /// </summary>
    internal static nint DisableLastWindowTabbing()
    {
        try
        {
            var app  = MsgSend(_clsNSApp, _selSharedApp);
            if (app == IntPtr.Zero) return 0;
            var wins = MsgSend(app, _selWindows);
            if (wins == IntPtr.Zero) return 0;
            var win  = MsgSend(wins, _selLastObject);
            if (win == IntPtr.Zero) return 0;
            MsgSend_nint(win, _selTabbingMode, NSWindowTabbingModeDisallowed);
            return win;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Positions the floating drag-preview window so its top-left corner sits
    /// at (cursorX - offsetX, cursorY - offsetY) in window-content logical coords.
    ///
    /// Coordinate spaces:
    ///   • cursorX/Y  — Quartz global logical, top-left origin, Y-down
    ///                  (direct output of CGEventGetLocation, no flip)
    ///   • offsetX/Y  — logical points (pressOffset in physical ÷ scale)
    ///   • NSScreen   — Cocoa, primary-screen bottom-left origin, Y-up
    ///   • setFrameTopLeftPoint — takes Cocoa screen point (top-left of frame)
    ///
    /// Conversion: Cocoa_Y = screenHeight - Quartz_Y
    /// </summary>
    // CoreGraphics for screen height (proven to return logical points on Uno macOS)
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    [DllImport(CG)] private static extern uint CGMainDisplayID();
    [DllImport(CG)] private static extern nuint CGDisplayPixelsHigh(uint display);
    [DllImport(CG)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CG)] private static extern NSPoint CGEventGetLocation(IntPtr e);
    [DllImport(CG)] private static extern void CFRelease(IntPtr cf);

    internal static (double X, double Y) GetCursorLocationQuartz()
    {
        var evt = CGEventCreate(IntPtr.Zero);
        if (evt == IntPtr.Zero) return (0, 0);
        try
        {
            var pt = CGEventGetLocation(evt);
            return (pt.X, pt.Y);
        }
        catch { return (0, 0); }
        finally { CFRelease(evt); }
    }

    /// <summary>
    /// Positions the floating window so its top-left sits at
    /// (cursorX - offsetX, cursorY - offsetY) in Quartz global coords.
    ///
    /// Coordinate spaces (confirmed empirically on Uno macOS):
    ///   • cursorX/Y   — Quartz global logical, top-left origin, Y-DOWN
    ///                   (direct from CGEventGetLocation)
    ///   • screenH     — CGDisplayPixelsHigh = logical screen height (= Quartz_Y + Cocoa_Y)
    ///   • Cocoa       — setFrameTopLeftPoint uses Y-UP from primary screen bottom
    ///   • Conversion  — Cocoa_Y_of_window_top = screenH - (cursorY - offsetY)
    /// </summary>
    internal static void MoveWindow(nint nsWindow, double cursorX, double cursorY,
                                    double offsetX, double offsetY)
    {
        if (nsWindow == IntPtr.Zero) return;
        try
        {
            var screenH = (double)(long)CGDisplayPixelsHigh(CGMainDisplayID());
            var winX    = cursorX - offsetX;
            var winY    = cursorY - offsetY;            // Quartz: Y from top
            var cocoaY  = screenH - winY;               // Cocoa: Y from bottom
            MsgSend_NSPoint(nsWindow, _selFrameTopLeft,
                new NSPoint { X = winX, Y = cocoaY });
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Returns the current cursor position in Cocoa screen coordinates
    /// (primary screen bottom-left origin, Y-up) for diagnostics.
    /// </summary>
    private static readonly IntPtr _selClose    = SafeSel("close");
    private static readonly IntPtr _selOrderOut = SafeSel("orderOut:");

    /// <summary>
    /// Fully closes the NSWindow via <c>[nsWindow close]</c>.
    /// This removes it from <c>NSApp.windows</c>, fires the window-closed delegate
    /// chain (which includes ReactorWindow.Dispose / DockFloatingTracker cleanup),
    /// and prevents ghost floating windows from blocking future drag sessions.
    ///
    /// Must be called BEFORE <c>EndDragSession()</c> to avoid re-entrancy between
    /// the NSWindow teardown and the SessionChanged re-render.
    /// </summary>
    internal static void CloseWindow(nint nsWindow)
    {
        if (nsWindow == IntPtr.Zero) return;
        try
        {
            // [NSWindow close] — full close, removes from NSApp.windows,
            // fires windowWillClose/windowDidClose delegates, triggers WinUI
            // Window.Closed → ReactorWindow.Dispose / DockFloatingTracker cleanup.
            MsgSendVoid(nsWindow, _selClose);
        }
        catch { /* best-effort */ }
    }

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgSendVoid(IntPtr rcv, IntPtr sel);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgSend(IntPtr rcv, IntPtr sel, IntPtr arg);

    internal static (double X, double Y) GetMouseLocationCocoa()
    {
        try
        {
            var pt = MsgSend_retNSPoint(_clsNSEvent, _selMouseLocation);
            return (pt.X, pt.Y);
        }
        catch { return (0, 0); }
    }

    /// <summary>
    /// Cursor in Quartz coordinates (top-left, Y-down) via NSEvent.mouseLocation
    /// instead of CGEventCreate — avoids allocating+releasing a CGEvent per call.
    /// Uses the same screen-height source as <see cref="GetWindowFrame"/> for a
    /// consistent Cocoa→Quartz flip.
    /// </summary>
    internal static (double X, double Y) GetCursorLocationQuartzViaNSEvent()
    {
        try
        {
            var pt = MsgSend_retNSPoint(_clsNSEvent, _selMouseLocation); // Cocoa Y-up, bottom-left
            var screenH = (double)(long)CGDisplayPixelsHigh(CGMainDisplayID());
            return (pt.X, screenH - pt.Y);
        }
        catch { return (0, 0); }
    }

    // ── NSWindowWillMoveNotification registration ───────────────────────────────

    private static readonly IntPtr _selDefaultCenter     = SafeSel("defaultCenter");
    private static readonly IntPtr _selAddObserverBlock  = SafeSel("addObserverForName:object:queue:usingBlock:");
    private static readonly IntPtr _selRemoveObserver    = SafeSel("removeObserver:");
    private static readonly IntPtr _clsNSNotificationCenter = SafeClass("NSNotificationCenter");

    // ObjC block layout for the notification callback:
    // A minimal ObjC block = {isa*, flags, reserved, invoke(block*, notification*)}
    // On ARM64 the block struct is passed by pointer to addObserverForName:...:usingBlock:.
    // We use a trampoline: store a GCHandle to the C# callback in a static, then
    // the block's invoke function (a static delegate) reads it.
    // This is safe because the block is called on the same thread as the notification.

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSendGetBlockObserver(
        IntPtr rcv, IntPtr sel,
        IntPtr name,   // NSString* — the notification name
        IntPtr obj,    // NSWindow* — the object to observe (or nil for any)
        IntPtr queue,  // NSOperationQueue* — nil = main thread
        IntPtr block); // the ObjC block

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgSend_removeObserver(IntPtr rcv, IntPtr sel, IntPtr observer);

    [DllImport(ObjC, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClassNamed(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSend_stringWithCString(IntPtr cls, IntPtr sel, IntPtr cstr, int encoding);

    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "CFStringCreateWithCString")]
    private static extern IntPtr CFStringCreate(IntPtr allocator, string str, int encoding);

    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "CFRelease")]
    private static extern void CFReleaseStr(IntPtr cf);

    // ObjC block trampoline: a minimal block struct that can call a C# delegate.
    // https://clang.llvm.org/docs/Block-ABI-Apple.html
    [StructLayout(LayoutKind.Sequential)]
    private struct ObjCBlock
    {
        public IntPtr Isa;        // &_NSConcreteGlobalBlock (resolved via dlsym)
        public int    Flags;      // BLOCK_IS_GLOBAL = 1<<28
        public int    Reserved;
        public IntPtr Invoke;     // pointer to the trampoline function
        public IntPtr Descriptor; // &Block_descriptor (size). Required: AppKit's
                                  // addLocalMonitor sends -copy to the block as a
                                  // real ObjC message, so isa+descriptor must be valid.
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public IntPtr Reserved; // 0
        public IntPtr Size;     // sizeof(ObjCBlock)
    }

    private const int BLOCK_IS_GLOBAL = 1 << 28;

    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "dlsym")]
    private static extern IntPtr DlSym(IntPtr handle, string symbol);
    private static readonly IntPtr RTLD_DEFAULT = new IntPtr(-2);

    // &_NSConcreteGlobalBlock is a data symbol, NOT an ObjC class — it must be
    // resolved with dlsym, not objc_getClass. Using objc_getClass leaves isa null
    // and crashes the moment AppKit sends -copy to the block.
    private static readonly IntPtr _concreteGlobalBlockIsa =
        OperatingSystem.IsMacOS() ? DlSym(RTLD_DEFAULT, "_NSConcreteGlobalBlock") : IntPtr.Zero;

    private static BlockDescriptor _blockDescriptor;
    private static GCHandle _blockDescriptorHandle;

    /// <summary>Lazily allocates the shared, pinned block descriptor and returns its address.</summary>
    private static IntPtr BlockDescriptorPtr()
    {
        if (!_blockDescriptorHandle.IsAllocated)
        {
            _blockDescriptor = new BlockDescriptor
            {
                Reserved = IntPtr.Zero,
                Size = (IntPtr)System.Runtime.InteropServices.Marshal.SizeOf<ObjCBlock>(),
            };
            _blockDescriptorHandle = GCHandle.Alloc(_blockDescriptor, GCHandleType.Pinned);
        }
        return _blockDescriptorHandle.AddrOfPinnedObject();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BlockInvokeDelegate(IntPtr blockPtr, IntPtr notification);

    // Static storage so the GC doesn't collect the delegates/blocks.
    private static BlockInvokeDelegate? _blockInvokeDelegate;
    private static ObjCBlock            _block;
    private static GCHandle             _blockHandle;
    private static Action?              _pendingCallback;

    private static void BlockInvoke(IntPtr blockPtr, IntPtr notification)
    {
        try { _pendingCallback?.Invoke(); }
        catch { /* never throw from ObjC callback */ }
    }

    /// <summary>
    /// Registers a C# callback to fire when the given NSWindow is about to be moved
    /// (i.e. the user starts dragging its title bar). Returns an observer token that
    /// must be passed to <see cref="UnregisterWindowWillMove"/> to clean up.
    /// </summary>
    internal static IntPtr RegisterWindowWillMove(nint nsWindow, Action callback)
    {
        if (nsWindow == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            // Store the callback — simple static since only one floating window drags at a time.
            _pendingCallback = callback;

            // Create the block trampoline.
            if (_blockInvokeDelegate == null)
            {
                _blockInvokeDelegate = BlockInvoke;
                _block = new ObjCBlock
                {
                    Isa        = _concreteGlobalBlockIsa,
                    Flags      = BLOCK_IS_GLOBAL,
                    Reserved   = 0,
                    Invoke     = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_blockInvokeDelegate),
                    Descriptor = BlockDescriptorPtr(),
                };
                _blockHandle = GCHandle.Alloc(_block, GCHandleType.Pinned);
            }

            var center = MsgSend(_clsNSNotificationCenter, _selDefaultCenter);
            if (center == IntPtr.Zero) return IntPtr.Zero;

            // NSWindowWillMoveNotification as a CFString
            var nameStr = CFStringCreate(IntPtr.Zero, "NSWindowWillMoveNotification", 0x08000100); // kCFStringEncodingUTF8
            try
            {
                var blockPtr = _blockHandle.AddrOfPinnedObject();
                var observer = MsgSendGetBlockObserver(center, _selAddObserverBlock,
                    nameStr, nsWindow, IntPtr.Zero, blockPtr);
                return observer;
            }
            finally { CFReleaseStr(nameStr); }
        }
        catch { return IntPtr.Zero; }
    }

    /// <summary>Removes a previously registered NSWindowWillMove observer.</summary>
    internal static void UnregisterWindowWillMove(IntPtr observer)
    {
        if (observer == IntPtr.Zero) return;
        try
        {
            var center = MsgSend(_clsNSNotificationCenter, _selDefaultCenter);
            if (center != IntPtr.Zero)
                MsgSend_removeObserver(center, _selRemoveObserver, observer);
            _pendingCallback = null;
        }
        catch { }
    }

    /// <summary>
    /// Returns the top-left position of the given NSWindow in Quartz coordinates
    /// (top-left origin, Y-down), suitable for comparing across ticks to detect movement.
    /// </summary>
    // ── Drag log helper ────────────────────────────────────────────────────────
    // Single drag log for the whole sample. Enabled automatically in Debug builds
    // (no env var needed); in Release it stays on only if UNODOCK_DRAGLOG=1. The file
    // is truncated once per process so each run starts clean.
    // Log file location. macOS keeps the well-known /tmp path (dev-tailed; the
    // documented interleave target). Windows has no /tmp — a hardcoded "/tmp/..."
    // resolves to C:\tmp which usually does NOT exist, so the StreamWriter threw
    // DirectoryNotFoundException and the catch swallowed it → ZERO logging on the
    // net10.0-desktop (Uno Skia) target. Use the real temp dir on Windows.
    internal static readonly string DragLogPath =
        OperatingSystem.IsWindows()
            ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unodock-drag.log")
            : "/tmp/unodock-drag.log";
    internal static readonly bool DragLogEnabled =
#if DEBUG
        true;
#else
        Environment.GetEnvironmentVariable("UNODOCK_DRAGLOG") == "1";
#endif

    // Verbose = the per-tick hot-path lines (cursor/origin/overlay each frame).
    // OFF by default: at ~60 fps the synchronous file writes wreck drag smoothness.
    // Opt in with UNODOCK_DRAGLOG_VERBOSE=1 when diagnosing coordinate math.
    internal static readonly bool DragLogVerboseEnabled =
        DragLogEnabled && Environment.GetEnvironmentVariable("UNODOCK_DRAGLOG_VERBOSE") == "1";

    private static readonly object _dragLogLock = new object();
    private static System.IO.StreamWriter? _dragLogWriter;

    internal static void DragLog(string msg)
    {
        if (!DragLogEnabled) return;
        try
        {
            lock (_dragLogLock)
            {
                // Keep the writer open (AutoFlush) instead of open/append/close per
                // call — the per-call file handle churn was a measurable per-tick cost.
                if (_dragLogWriter == null)
                {
                    var dir = System.IO.Path.GetDirectoryName(DragLogPath);
                    if (!string.IsNullOrEmpty(dir))
                        System.IO.Directory.CreateDirectory(dir);
                    _dragLogWriter = new System.IO.StreamWriter(DragLogPath, append: false)
                    {
                        AutoFlush = true,
                    };
                    // Run-start banner so the latest run is unambiguous in the file.
                    _dragLogWriter.Write(
                        $"{DateTime.Now:HH:mm:ss.fff} ===== UnoDock drag log start " +
                        $"pid={Environment.ProcessId} path={DragLogPath} =====\n");
                }
                _dragLogWriter.Write($"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
            }
        }
        catch { }
    }

    /// <summary>Per-tick / hot-path logging — no-op unless UNODOCK_DRAGLOG_VERBOSE=1.</summary>
    internal static void DragLogVerbose(string msg)
    {
        if (DragLogVerboseEnabled) DragLog(msg);
    }

    /// <summary>
    /// Hands the in-progress drag to the NSWindow so it follows the cursor.
    /// Creates a synthetic left-mouse-down event with locationInWindow set to
    /// the CENTER of the title bar so the cursor appears there (not on the
    /// traffic-light buttons) after performWindowDragWithEvent: starts.
    /// </summary>
    internal static void PerformWindowDragFromTitleBarCenter(nint nsWindow, double windowWidth, double windowHeight)
    {
        if (nsWindow == IntPtr.Zero) return;
        try
        {
            // Cocoa Y-up: title bar is at the TOP of the window, so a larger
            // offset from the top places the synthetic grab point lower within
            // the title-bar rectangle.
            var locationInWindow = new NSPoint
            {
                X = windowWidth / 2.0,
                Y = windowHeight - InitialTitleBarGrabOffset,
            };

            // Get timestamp from current event so the synthetic event is plausible.
            var app      = MsgSend(_clsNSApp, _selSharedApp);
            var curEvent = MsgSend(app, _selCurrentEvent);
            double ts    = curEvent != IntPtr.Zero
                ? MsgSend_retDouble2(curEvent, _selTimestamp)
                : 0.0;
            var winNum   = MsgSend_retNInt((IntPtr)nsWindow, _selWindowNumber);

            DragLog($"PerformWindowDragFromTitleBarCenter: nsWin=0x{nsWindow:X} " +
                    $"location=({locationInWindow.X:F0},{locationInWindow.Y:F0}) " +
                    $"windowSize=({windowWidth:F0}x{windowHeight:F0}) winNum={winNum} ts={ts:F3}");

            var synthetic = MsgSend_syntheticMouseEvent(
                _clsNSEvent, _selMouseEventFactory,
                1,                    // NSEventTypeLeftMouseDown
                locationInWindow,
                0,                    // no modifier flags
                ts,
                winNum,
                IntPtr.Zero,          // context (nil)
                0,                    // eventNumber
                1,                    // clickCount
                1.0f);                // pressure

            if (synthetic != IntPtr.Zero)
                MsgSend((IntPtr)nsWindow, _selPerformWindowDrag, synthetic);
            else
                DragLog("PerformWindowDragFromTitleBarCenter: synthetic event is nil");
        }
        catch (Exception ex) { DragLog($"PerformWindowDragFromTitleBarCenter exception: {ex.Message}"); }
    }

    /// <summary>Bring an NSWindow to the front of all app windows.</summary>
    internal static void OrderWindowFront(nint nsWindow)
    {
        if (nsWindow == IntPtr.Zero) return;
        try { MsgSend((IntPtr)nsWindow, _selOrderFront, IntPtr.Zero); }
        catch { }
    }

    internal static void SetOverlayWindowLevel(nint nsWindow)
    {
        if (nsWindow == IntPtr.Zero) return;
        try { MsgSend_nint((IntPtr)nsWindow, _selSetLevel, 24); }
        catch { }
    }

    /// <summary>Hide an NSWindow without closing it.</summary>
    internal static void HideWindow(nint nsWindow)
    {
        if (nsWindow == IntPtr.Zero) return;
        try { MsgSend((IntPtr)nsWindow, _selOrderOut, IntPtr.Zero); }
        catch { }
    }

    /// <summary>Sets NSWindow alpha (0..1). Useful during drag to keep drop indicators visible.</summary>
    internal static void SetWindowAlpha(nint nsWindow, double alpha)
    {
        if (nsWindow == IntPtr.Zero) return;
        try
        {
            if (alpha < 0.0) alpha = 0.0;
            else if (alpha > 1.0) alpha = 1.0;
            MsgSend_double((IntPtr)nsWindow, _selSetAlphaValue, alpha);
        }
        catch { }
    }

    /// <summary>Returns the NSWindow handle of NSApp.mainWindow.</summary>
    /// <remarks>
    /// WARNING: NSApp.mainWindow follows keyboard focus. During a floating-window
    /// drag the dragged child window becomes main, so this returns the WRONG window.
    /// Use <see cref="GetMainAppWindow"/> for a focus-independent result.
    /// </remarks>
    internal static nint GetMainNsWindow()
    {
        try
        {
            var app = MsgSend(_clsNSApp, _selSharedApp);
            return (nint)MsgSend(app, Sel("mainWindow"));
        }
        catch { return 0; }
    }

    private const nuint NSWindowStyleMaskTitled = 1;

    /// <summary>
    /// Returns the application's primary (host) NSWindow, independent of keyboard
    /// focus — the macOS counterpart of Win32 FindWindowForCurrentProcess.
    ///
    /// Identifies it as the LARGEST titled, visible window after excluding the given
    /// handles (the live floating drag windows). This deliberately skips:
    ///   • the borderless per-pixel-alpha compass overlay (no NSWindowStyleMaskTitled),
    ///   • the floating windows passed in <paramref name="excludeHandles"/>,
    /// so a drag in progress can never be mistaken for the host window.
    /// </summary>
    internal static nint GetMainAppWindow(nint[] excludeHandles)
    {
        try
        {
            var app = MsgSend(_clsNSApp, _selSharedApp);
            if (app == IntPtr.Zero) return 0;
            var wins = MsgSend(app, _selWindows);
            if (wins == IntPtr.Zero) return 0;

            var count = (long)MsgSend_retNUInt(wins, _selCount);
            nint best = 0;
            double bestArea = -1;
            for (long i = 0; i < count; i++)
            {
                var w = (nint)MsgSend_nuint_retIntPtr(wins, _selObjectAtIndex, (nuint)i);
                if (w == 0) continue;

                if (excludeHandles != null)
                {
                    var skip = false;
                    foreach (var h in excludeHandles)
                        if (h == w) { skip = true; break; }
                    if (skip) continue;
                }

                // Borderless (overlay) → skip; only the host window is titled.
                var style = MsgSend_retNUInt((IntPtr)w, _selStyleMask);
                if ((style & NSWindowStyleMaskTitled) == 0) continue;
                if (MsgSend_retBool((IntPtr)w, _selIsVisible) == 0) continue;

                var frame = MsgSend_retNSRect((IntPtr)w, _selFrame);
                var area = frame.Size.X * frame.Size.Y;
                if (area > bestArea) { bestArea = area; best = w; }
            }
            return best;
        }
        catch { return 0; }
    }

    internal static (double X, double Y) GetWindowPosition(nint nsWindow)
    {
        if (nsWindow == IntPtr.Zero) return (0, 0);
        try
        {
            var frame   = MsgSend_retNSRect(nsWindow, _selFrame); // Cocoa Y-up frame
            var screenH = (double)(long)CGDisplayPixelsHigh(CGMainDisplayID());
            // Convert Cocoa bottom-left origin to Quartz top-left (Y-down):
            var quartzY = screenH - (frame.Origin.Y + frame.Size.Y);
            return (frame.Origin.X, quartzY);
        }
        catch { return (0, 0); }
    }

    internal static (double X, double Y, double Width, double Height) GetWindowFrame(nint nsWindow)
    {
        if (nsWindow == IntPtr.Zero) return (0, 0, 0, 0);
        try
        {
            var frame = MsgSend_retNSRect(nsWindow, _selFrame); // Cocoa Y-up frame
            var screenH = (double)(long)CGDisplayPixelsHigh(CGMainDisplayID());
            var quartzY = screenH - (frame.Origin.Y + frame.Size.Y);
            return (frame.Origin.X, quartzY, frame.Size.X, frame.Size.Y);
        }
        catch { return (0, 0, 0, 0); }
    }

    internal static (double X, double Y, double Width, double Height) GetContentViewFrame(nint nsWindow)
    {
        if (nsWindow == IntPtr.Zero) return (0, 0, 0, 0);
        try
        {
            var contentView = MsgSend((IntPtr)nsWindow, _selContentView);
            if (contentView == IntPtr.Zero) return (0, 0, 0, 0);
            var frame = MsgSend_retNSRect(contentView, _selFrame);
            return (frame.Origin.X, frame.Origin.Y, frame.Size.X, frame.Size.Y);
        }
        catch { return (0, 0, 0, 0); }
    }

    /// <summary>
    /// Returns the Quartz (top-left, Y-down) position of the HOST window's
    /// content area top-left corner, so callers can convert a Quartz cursor
    /// position to host-content coordinates.
    ///
    /// Uses NSApp.mainWindow.frame and the screen height.
    /// </summary>
    internal static (double X, double Y) GetMainWindowContentOrigin(double titleBarHeight = 32)
    {
        try
        {
            var app     = MsgSend(_clsNSApp, _selSharedApp);
            var mainWin = MsgSend(app, Sel("mainWindow"));
            if (mainWin == IntPtr.Zero) return (0, 0);
            var frame   = MsgSend_retNSRect(mainWin, Sel("frame")); // Cocoa: Y-up
            var screenH = (double)(long)CGDisplayPixelsHigh(CGMainDisplayID());
            // Window top-left in Quartz:
            var winX    = frame.Origin.X;
            var winTopQ = screenH - (frame.Origin.Y + frame.Size.Y);
            return (winX, winTopQ + titleBarHeight);
        }
        catch { return (0, 0); }
    }

    /// <summary>
    /// Returns the Quartz (top-left, Y-down) position of a specific NSWindow's
    /// content-area top-left corner.
    /// </summary>
    internal static (double X, double Y) GetWindowContentOrigin(nint nsWindow, double titleBarHeight = 32)
    {
        if (nsWindow == IntPtr.Zero) return (0, 0);
        try
        {
            var frame = MsgSend_retNSRect((IntPtr)nsWindow, Sel("frame")); // Cocoa: Y-up
            var screenH = (double)(long)CGDisplayPixelsHigh(CGMainDisplayID());
            var winX = frame.Origin.X;
            var winTopQ = screenH - (frame.Origin.Y + frame.Size.Y);
            return (winX, winTopQ + titleBarHeight);
        }
        catch { return (0, 0); }
    }

    /// <summary>
    /// Returns the Quartz (top-left, Y-down) position of a window's content-area
    /// top-left corner by asking Cocoa directly — the macOS equivalent of Win32
    /// ClientToScreen((0,0)). No hardcoded title-bar/chrome guess.
    ///
    ///   contentView.bounds → [contentView convertRect:toView:nil] (window base coords)
    ///                      → [window convertRectToScreen:] (Cocoa screen, Y-up)
    ///                      → flip to Quartz (Y-down) using the screen height.
    ///
    /// Returns (0,0) on failure so the caller can fall back.
    /// </summary>
    internal static (double X, double Y) GetWindowContentOriginViaConvert(nint nsWindow)
    {
        if (nsWindow == IntPtr.Zero) return (0, 0);
        try
        {
            var contentView = MsgSend((IntPtr)nsWindow, _selContentView);
            if (contentView == IntPtr.Zero) return (0, 0);

            var bounds = MsgSend_retNSRect(contentView, _selBounds);
            var inWindow = MsgSend_NSRect_IntPtr_retNSRect(contentView, _selConvertRectToView, bounds, IntPtr.Zero);
            var onScreen = MsgSend_NSRect_retNSRect((IntPtr)nsWindow, _selConvertRectToScreen, inWindow);

            var screenH = (double)(long)CGDisplayPixelsHigh(CGMainDisplayID());
            // Cocoa screen rect is Y-up from primary-screen bottom-left; the content
            // area's TOP edge is origin.Y + size.height. Flip to Quartz Y-down.
            var quartzTopY = screenH - (onScreen.Origin.Y + onScreen.Size.Y);
            return (onScreen.Origin.X, quartzTopY);
        }
        catch { return (0, 0); }
    }

    /// <summary>
    /// One-shot geometry dump (diagnostics): logs every raw native measurement so a
    /// coordinate offset can be diagnosed without guessing. Logged via DragLog.
    /// </summary>
    internal static void DumpWindowGeometry(nint nsWindow, string label)
    {
        try
        {
            var screenHpx = (double)(long)CGDisplayPixelsHigh(CGMainDisplayID());
            var frame = MsgSend_retNSRect((IntPtr)nsWindow, Sel("frame"));
            var cv = MsgSend((IntPtr)nsWindow, _selContentView);
            var cvFrame = cv != IntPtr.Zero ? MsgSend_retNSRect(cv, _selFrame) : default;
            var cvBounds = cv != IntPtr.Zero ? MsgSend_retNSRect(cv, _selBounds) : default;
            var scale = MsgSend_retDouble((IntPtr)nsWindow, _selBackingScaleFactor);

            // NSScreen of the window (the display it is actually on) and its frame.
            var screen = MsgSend((IntPtr)nsWindow, _selScreen);
            var screenFrame = screen != IntPtr.Zero ? MsgSend_retNSRect(screen, Sel("frame")) : default;

            var convOrigin = GetWindowContentOriginViaConvert(nsWindow);
            var chromeGuessOrigin = GetWindowContentOrigin(nsWindow, titleBarHeight: 52);
            var (curX, curY) = GetCursorLocationQuartz();

            DragLog(
                $"[Geometry {label}] nsWin=0x{nsWindow:X} scale={scale:F2} CGDisplayPixelsHigh={screenHpx:F1}\n" +
                $"  frame(Cocoa)=({frame.Origin.X:F1},{frame.Origin.Y:F1} {frame.Size.X:F1}x{frame.Size.Y:F1})\n" +
                $"  contentView.frame=({cvFrame.Origin.X:F1},{cvFrame.Origin.Y:F1} {cvFrame.Size.X:F1}x{cvFrame.Size.Y:F1})\n" +
                $"  contentView.bounds=({cvBounds.Origin.X:F1},{cvBounds.Origin.Y:F1} {cvBounds.Size.X:F1}x{cvBounds.Size.Y:F1})\n" +
                $"  NSScreen.frame(Cocoa)=({screenFrame.Origin.X:F1},{screenFrame.Origin.Y:F1} {screenFrame.Size.X:F1}x{screenFrame.Size.Y:F1})\n" +
                $"  origin via convertRectToScreen (Quartz top-left) = ({convOrigin.X:F1},{convOrigin.Y:F1})\n" +
                $"  origin via frame+52-chrome guess (Quartz top-left) = ({chromeGuessOrigin.X:F1},{chromeGuessOrigin.Y:F1})\n" +
                $"  cursor(Quartz)=({curX:F1},{curY:F1})");
        }
        catch (Exception ex) { DragLog($"[Geometry {label}] exception: {ex.Message}"); }
    }
}
#endif
