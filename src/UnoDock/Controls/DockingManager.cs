// Forked from AvalonDock DockingManager.cs (2937 lines → ~250 lines for Phase 2).
// Phase 2 keeps: Layout DP, side-panel DPs, GridSplitter DPs, CreateUIElementForModel
//               for the four docked cases, Loaded handler wiring.
// Phase 2 stubs: floating windows, overlay, autohide, navigator, Win32, bindings,
//               LayoutItem attachment (no DataTemplate/LayoutItemTemplate yet).
// The Phase-1 stub partial class in Phase1ControlStubs.cs is superseded by this file.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using AvalonDock.Layout;
using AvalonDock.Controls;
using LayoutFloatingWindowControl = AvalonDock.Controls.LayoutFloatingWindowControl;
using AvalonDockLayoutPanel = AvalonDock.Layout.LayoutPanel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace AvalonDock
{
	[ContentProperty(Name = nameof(Layout))]
	public partial class DockingManager : Control, AvalonDock.Controls.IOverlayWindowHost
	{
		// Static callbacks must be declared before the DPs that reference them.
		private static readonly Microsoft.UI.Xaml.PropertyChangedCallback _onLayoutChanged =
			(d, e) => ((DockingManager)d).OnLayoutChangedInternal(e);

		public DockingManager()
		{
			DefaultStyleKey = typeof(DockingManager);
			Layout = new LayoutRoot
			{
				RootPanel = new AvalonDockLayoutPanel(new LayoutDocumentPaneGroup(new LayoutDocumentPane()))
			};
			Loaded += OnLoaded;
			Unloaded += OnUnloaded;
#if WINDOWS_APP_SDK
			SizeChanged += OnSizeChanged;
#endif
		}

#if WINDOWS_APP_SDK
		// Parity with WPF DockingManager.OnSizeChanged: push the manager's
		// window-clamped size down so fixed (pixel-width) panes shrink/grow with
		// the window. Required on WinUI because a Grid whose pixel columns exceed
		// the available slot keeps the overflowed total as its ActualWidth, so
		// LayoutGridControl's self-measured AdjustFixedChildrenPanelSizes never
		// sees the true available space. Uno's Grid clamps the arranged size to
		// the slot, so the per-grid adjustment is sufficient there.
		private void OnSizeChanged(object sender, SizeChangedEventArgs e)
		{
			if (LayoutRootPanel == null || RightSidePanel == null || LeftSidePanel == null
				|| TopSidePanel == null || BottomSidePanel == null)
				return;

			var width = Math.Max(ActualWidth - GridSplitterWidth - RightSidePanel.ActualWidth - LeftSidePanel.ActualWidth, 0);
			var height = Math.Max(ActualHeight - GridSplitterHeight - TopSidePanel.ActualHeight - BottomSidePanel.ActualHeight, 0);

			LayoutRootPanel.AdjustFixedChildrenPanelSizes(new Size(width, height));
		}
#endif

		// ── Layout ──────────────────────────────────────────────────────────────

		public static readonly DependencyProperty LayoutProperty =
			DependencyProperty.Register(nameof(Layout), typeof(LayoutRoot), typeof(DockingManager),
				new PropertyMetadata(null, _onLayoutChanged));

		public LayoutRoot Layout
		{
			get => (LayoutRoot)GetValue(LayoutProperty);
			set => SetValue(LayoutProperty, value);
		}


		private void OnLayoutChangedInternal(DependencyPropertyChangedEventArgs e)
		{
			var newLayout = (LayoutRoot)e.NewValue;
			var oldLayout = (LayoutRoot)e.OldValue;

			if (oldLayout != null && oldLayout.Manager == this)
				oldLayout.Manager = null;

			if (newLayout != null)
			{
				newLayout.Manager = this;
				if (IsLoaded) RebuildLayoutControls(newLayout);
				newLayout.PropertyChanged += OnLayoutPropertyChanged;
			}
		}

		// ── Side panel DPs ────────────────────────────────────────────────────

		public static readonly DependencyProperty LayoutRootPanelProperty =
			DependencyProperty.Register(nameof(LayoutRootPanel), typeof(Controls.LayoutPanelControl),
				typeof(DockingManager), new PropertyMetadata(null));

		public Controls.LayoutPanelControl LayoutRootPanel
		{
			get => (Controls.LayoutPanelControl)GetValue(LayoutRootPanelProperty);
			set => SetValue(LayoutRootPanelProperty, value);
		}

		public static readonly DependencyProperty LeftSidePanelProperty =
			DependencyProperty.Register(nameof(LeftSidePanel), typeof(Controls.LayoutAnchorSideControl),
				typeof(DockingManager), new PropertyMetadata(null));
		public Controls.LayoutAnchorSideControl LeftSidePanel
		{
			get => (Controls.LayoutAnchorSideControl)GetValue(LeftSidePanelProperty);
			set => SetValue(LeftSidePanelProperty, value);
		}

		public static readonly DependencyProperty RightSidePanelProperty =
			DependencyProperty.Register(nameof(RightSidePanel), typeof(Controls.LayoutAnchorSideControl),
				typeof(DockingManager), new PropertyMetadata(null));
		public Controls.LayoutAnchorSideControl RightSidePanel
		{
			get => (Controls.LayoutAnchorSideControl)GetValue(RightSidePanelProperty);
			set => SetValue(RightSidePanelProperty, value);
		}

		public static readonly DependencyProperty TopSidePanelProperty =
			DependencyProperty.Register(nameof(TopSidePanel), typeof(Controls.LayoutAnchorSideControl),
				typeof(DockingManager), new PropertyMetadata(null));
		public Controls.LayoutAnchorSideControl TopSidePanel
		{
			get => (Controls.LayoutAnchorSideControl)GetValue(TopSidePanelProperty);
			set => SetValue(TopSidePanelProperty, value);
		}

		public static readonly DependencyProperty BottomSidePanelProperty =
			DependencyProperty.Register(nameof(BottomSidePanel), typeof(Controls.LayoutAnchorSideControl),
				typeof(DockingManager), new PropertyMetadata(null));
		public Controls.LayoutAnchorSideControl BottomSidePanel
		{
			get => (Controls.LayoutAnchorSideControl)GetValue(BottomSidePanelProperty);
			set => SetValue(BottomSidePanelProperty, value);
		}

		// ── Grid splitter DPs ─────────────────────────────────────────────────

		public static readonly DependencyProperty GridSplitterWidthProperty =
			DependencyProperty.Register(nameof(GridSplitterWidth), typeof(double),
				typeof(DockingManager), new PropertyMetadata(6.0));
		public double GridSplitterWidth
		{
			get => (double)GetValue(GridSplitterWidthProperty);
			set => SetValue(GridSplitterWidthProperty, value);
		}

		public static readonly DependencyProperty GridSplitterHeightProperty =
			DependencyProperty.Register(nameof(GridSplitterHeight), typeof(double),
				typeof(DockingManager), new PropertyMetadata(6.0));
		public double GridSplitterHeight
		{
			get => (double)GetValue(GridSplitterHeightProperty);
			set => SetValue(GridSplitterHeightProperty, value);
		}

		public static readonly DependencyProperty GridSplitterVerticalStyleProperty =
			DependencyProperty.Register(nameof(GridSplitterVerticalStyle), typeof(Style),
				typeof(DockingManager), new PropertyMetadata(null));
		public Style GridSplitterVerticalStyle
		{
			get => (Style)GetValue(GridSplitterVerticalStyleProperty);
			set => SetValue(GridSplitterVerticalStyleProperty, value);
		}

		public static readonly DependencyProperty GridSplitterHorizontalStyleProperty =
			DependencyProperty.Register(nameof(GridSplitterHorizontalStyle), typeof(Style),
				typeof(DockingManager), new PropertyMetadata(null));
		public Style GridSplitterHorizontalStyle
		{
			get => (Style)GetValue(GridSplitterHorizontalStyleProperty);
			set => SetValue(GridSplitterHorizontalStyleProperty, value);
		}

		public static readonly DependencyProperty AllowMixedOrientationProperty =
			DependencyProperty.Register(nameof(AllowMixedOrientation), typeof(bool),
				typeof(DockingManager), new PropertyMetadata(false));

		public bool AllowMixedOrientation
		{
			get => (bool)GetValue(AllowMixedOrientationProperty);
			set => SetValue(AllowMixedOrientationProperty, value);
		}

		// ── Theme DP ──────────────────────────────────────────────────────────
		// When set, merges the theme's ResourceDictionary into DockingManager.Resources,
		// allowing VS2013 (and future) themes to override default control styles.

		private static readonly Microsoft.UI.Xaml.PropertyChangedCallback _onThemeChanged =
			(d, e) => ((DockingManager)d).OnThemeChangedInternal(e);

		public static readonly DependencyProperty ThemeProperty =
			DependencyProperty.Register(nameof(Theme), typeof(AvalonDock.Themes.Theme),
				typeof(DockingManager), new PropertyMetadata(null, _onThemeChanged));

		public AvalonDock.Themes.Theme Theme
		{
			get => (AvalonDock.Themes.Theme)GetValue(ThemeProperty);
			set => SetValue(ThemeProperty, value);
		}

		private ResourceDictionary _currentThemeDict;

		private void OnThemeChangedInternal(DependencyPropertyChangedEventArgs e)
		{
			// Remove previous theme dictionary from DockingManager and overlay root.
			if (_currentThemeDict != null)
			{
				Resources.MergedDictionaries.Remove(_currentThemeDict);
				RemoveFromOverlayRoot(_currentThemeDict);
				_currentThemeDict = null;
			}

			var newTheme = e.NewValue as AvalonDock.Themes.Theme;
			if (newTheme != null)
			{
				_currentThemeDict = newTheme is AvalonDock.Themes.DictionaryTheme dt
					? dt.ThemeResourceDictionary
					: new ResourceDictionary { Source = newTheme.GetResourceUri() };
				Resources.MergedDictionaries.Add(_currentThemeDict);
				AddToOverlayRoot(_currentThemeDict);
			}
		}

		private void AddToOverlayRoot(ResourceDictionary dict)
		{
			if (dict == null) return;
			_overlayWindowControl?.Resources.MergedDictionaries.Add(dict);
			if (_overlayNativeWindow?.Content is Grid root)
				root.Resources.MergedDictionaries.Add(dict);
#if !WINDOWS
			_overlayVisualHost?.Resources.MergedDictionaries.Add(dict);
#endif
		}

		private void RemoveFromOverlayRoot(ResourceDictionary dict)
		{
			if (dict == null) return;
			_overlayWindowControl?.Resources.MergedDictionaries.Remove(dict);
			if (_overlayNativeWindow?.Content is Grid root)
				root.Resources.MergedDictionaries.Remove(dict);
#if !WINDOWS
			_overlayVisualHost?.Resources.MergedDictionaries.Remove(dict);
#endif
		}

		// ── ILayoutUpdateStrategy ────────────────────────────────────────────

		public static readonly DependencyProperty LayoutUpdateStrategyProperty =
			DependencyProperty.Register(nameof(LayoutUpdateStrategy), typeof(ILayoutUpdateStrategy),
				typeof(DockingManager), new PropertyMetadata(null));
		public ILayoutUpdateStrategy LayoutUpdateStrategy
		{
			get => (ILayoutUpdateStrategy)GetValue(LayoutUpdateStrategyProperty);
			set => SetValue(LayoutUpdateStrategyProperty, value);
		}

		// ── ActiveContent DP ─────────────────────────────────────────────────
		// Mirrors LayoutRoot.ActiveContent; updated when any LayoutContent.IsActive changes.

		private static readonly Microsoft.UI.Xaml.PropertyChangedCallback _onActiveContentChanged =
			(d, e) => ((DockingManager)d).OnActiveContentChangedInternal(e);

		public static readonly DependencyProperty ActiveContentProperty =
			DependencyProperty.Register(nameof(ActiveContent), typeof(object),
				typeof(DockingManager), new PropertyMetadata(null, _onActiveContentChanged));

		public object ActiveContent
		{
			get => GetValue(ActiveContentProperty);
			set => SetValue(ActiveContentProperty, value);
		}

		public event EventHandler ActiveContentChanged;


		private void OnActiveContentChangedInternal(DependencyPropertyChangedEventArgs e)
		{
			// Sync back to the layout model (avoid re-entrancy).
			if (_insideInternalSetActiveContent) return;
			var lc = e.NewValue as LayoutContent;
			if (lc == null && e.NewValue != null)
				lc = Layout?.Descendents().OfType<LayoutContent>()
					.FirstOrDefault(c => c.Content == e.NewValue);
			_insideInternalSetActiveContent = true;
			if (Layout != null) Layout.ActiveContent = lc;
			_insideInternalSetActiveContent = false;
			DragLogW($"ActiveContentChanged → title='{lc?.Title}'");
			ActiveContentChanged?.Invoke(this, EventArgs.Empty);
		}

		private bool _insideInternalSetActiveContent;

		private void SyncActiveContentFromLayout()
		{
			if (_insideInternalSetActiveContent) return;
			var active = Layout?.ActiveContent;
			if (ActiveContent != active)
			{
				_insideInternalSetActiveContent = true;
				ActiveContent = active;
				_insideInternalSetActiveContent = false;
			}
		}

		// ── Suspend flags (model expects these) ───────────────────────────────

		public bool SuspendDocumentsSourceBinding { get; set; }
		public bool SuspendAnchorablesSourceBinding { get; set; }

		// ── Commands ─────────────────────────────────────────────────────────

		public void ExecuteCloseCommand(LayoutContent content)
		{
			// Raise the Closing event so callers can cancel (Phase 7).
			// For Phase 4 we remove unconditionally if CanClose.
			if (content == null || !content.CanClose) return;
			if (content is LayoutDocument doc)
				doc.Close();          // model removes itself from parent pane
			else if (content is LayoutAnchorable anc)
				anc.Hide();           // anchorables hide rather than close by default
		}

		public void ExecuteHideCommand(LayoutAnchorable anchorable)
		{
			// Call HideAnchorable directly (not Hide()) to avoid the infinite loop:
			// LayoutAnchorable.Hide() → Manager.ExecuteHideCommand() → Hide() → ...
			// HideAnchorable(cancelable: true) is the model-level implementation.
			anchorable?.HideAnchorable(true);
		}

		// ── Overlay drag-drop (Phase 6) ──────────────────────────────────────
		private Microsoft.UI.Xaml.Controls.Grid _templateRoot;
		private Controls.IOverlayWindow _overlayWindow;
		private readonly List<Controls.IDropArea> _overlayActiveAreas = new List<Controls.IDropArea>();
		private Controls.IDropTarget _overlayActiveTarget;

		// ── Active drag tracker ────────────────────────────────────────────────
#if !WINDOWS
		private FloatingWindowDragTracker _dragTracker;
		private LinuxFloatingWindowDragTracker _linuxDragTracker;
		private DispatcherTimer _watchdog;
		private nint _hostWindowHandle;

		// Snapshot of floating window NSWindow positions from the previous watchdog tick.
		// Used to detect when the user starts dragging (position changes while button down).
		private readonly System.Collections.Generic.Dictionary<nint, (double X, double Y)> _lastWinPos =
			new System.Collections.Generic.Dictionary<nint, (double X, double Y)>();
#endif
		private WindowsFloatingWindowDragTracker _windowsDragTracker;

		// ── Session 26: native OS-driven drag (default ON) ──────────────────────
		// drag-to-float hands off to the OS native window-move loop
		// (Controls/NativeWindowDrag.cs) instead of the polling DispatcherTimer
		// trackers above. See docs/drag-to-float.md Parts 3 & 4. Set
		// UNODOCK_TIMER_DRAG=1 to fall back to the legacy timer/watchdog path.
		private static readonly bool UseNativeDrag =
			Environment.GetEnvironmentVariable("UNODOCK_TIMER_DRAG") != "1";
		private INativeWindowDrag _nativeDrag;

		/// <summary>
		/// Platform-neutral origin of this manager's content area in native screen
		/// coordinates (Quartz Y-down on macOS, physical pixels on Windows).
		/// </summary>
		private (double X, double Y) ComputeScreenOrigin()
		{
#if !WINDOWS
			if (OperatingSystem.IsMacOS()) return ComputeScreenOriginQ();
			if (OperatingSystem.IsLinux()) return ComputeScreenOriginLinux();
#endif
			return ComputeScreenOriginW();
		}

		/// <summary>Current cursor in native screen coordinates.</summary>
		private static (double X, double Y) NativeCursorScreen()
		{
#if !WINDOWS
			if (OperatingSystem.IsMacOS())
				return AvalonDock.Hosting.MacOSWindowTabbing.GetCursorLocationQuartz();
			if (OperatingSystem.IsLinux())
				return LinuxFloatingWindowDragTracker.GetCursorScreen();
#endif
			return WindowsFloatingWindowDragTracker.GetCursorScreen();
		}

		/// <summary>Top-left of a floating window in native screen coordinates.</summary>
		private static (double X, double Y) NativeWindowTopLeft(LayoutFloatingWindowControl fwc)
		{
#if !WINDOWS
			if (OperatingSystem.IsMacOS() && fwc.NsWindowHandle != 0)
				return AvalonDock.Hosting.MacOSWindowTabbing.GetWindowPosition(fwc.NsWindowHandle);
#endif
			return fwc.GetWindowPosition();
		}

		/// <summary>Bring a floating window above the main window (cursor left the manager).</summary>
		private static void BringFloatingToFront(LayoutFloatingWindowControl fwc)
		{
#if !WINDOWS
			if (OperatingSystem.IsMacOS())
			{
				if (fwc.NsWindowHandle != 0)
					AvalonDock.Hosting.MacOSWindowTabbing.OrderWindowFront(fwc.NsWindowHandle);
				return;
			}
#endif
			fwc.BringToFrontWindows();
		}

		/// <summary>
		/// Session 26 native drag entry point. Hands off to the OS move loop and drives
		/// the docking overlay from the native Moving/Ended events — no timer/watchdog.
		/// Returns false if no native handle is available (caller should fall back).
		/// </summary>
		private bool StartNativeDrag(LayoutFloatingWindowControl fwc, Windows.Foundation.Point grabOffset)
		{
			DragLogW($"StartNativeDrag: grabOffset=({grabOffset.X:F0},{grabOffset.Y:F0})");
			var drag = fwc.CreateNativeDrag();
			if (drag == null) { DragLogW("StartNativeDrag: CreateNativeDrag returned null — falling back"); return false; }

			_nativeDrag?.Dispose();
			_nativeDrag = drag;

			var overlayHost = (Controls.IOverlayWindowHost)this;
			var overlay = overlayHost.ShowOverlayWindow(fwc);
			overlay?.DragEnter(fwc);
			_overlayActiveAreas.Clear();
			_overlayActiveTarget = null;

			BringFloatingToFront(fwc);

			drag.Moving += cursor =>
			{
				var (ox, oy) = ComputeScreenOrigin();
				double localX = cursor.X - ox;
				double localY = cursor.Y - oy;
				if (OperatingSystem.IsWindows())
				{
					var scale = XamlRoot?.RasterizationScale ?? 1.0;
					if (scale <= 0) scale = 1.0;
					localX /= scale;
					localY /= scale;
				}

				var w = ActualWidth;
				var h = ActualHeight;
				const double NearMargin = 64.0;
				bool inside = localX >= 0 && localX <= w && localY >= 0 && localY <= h;
				bool near = localX >= -NearMargin && localX <= w + NearMargin &&
					localY >= -NearMargin && localY <= h + NearMargin;
				if (inside || near)
					UpdateOverlayDragStateForPoint(localX, localY, fwc);
				else
				{
					ClearActiveOverlayTargets();
					BringFloatingToFront(fwc);
				}
			};

			drag.Ended += _ =>
			{
				var activeTarget = _overlayActiveTarget;
				var capturedOverlay = _overlayWindow;
				if (activeTarget != null && capturedOverlay != null)
					capturedOverlay.DragDrop(activeTarget);

				EndOverlayDrag(fwc);
				((Controls.IOverlayWindowHost)this).HideOverlayWindow();
				_nativeDrag?.Dispose();
				_nativeDrag = null;
			};

			drag.BeginDrag(new Windows.Foundation.Point(NativeCursorScreen().X, NativeCursorScreen().Y), grabOffset);
			return true;
		}

		/// <param name="initialScreenLeft">Optional initial screen-left in logical pixels; overrides FloatingLeft.</param>
		/// <param name="initialScreenTop">Optional initial screen-top in logical pixels; overrides FloatingTop.</param>
		public void StartDraggingFloatingWindowForContent(
			LayoutContent content, bool startDrag = true,
			double? initialScreenLeft = null, double? initialScreenTop = null)
		{
			if (content == null || !content.CanFloat) return;
			bool useNativeInitialPlacement = false;
#if !WINDOWS
			useNativeInitialPlacement = startDrag && OperatingSystem.IsMacOS();
#endif
			// Apply caller-supplied screen position so the window appears at the cursor.
			if (!useNativeInitialPlacement)
			{
				if (initialScreenLeft.HasValue) content.FloatingLeft = initialScreenLeft.Value;
				if (initialScreenTop.HasValue)  content.FloatingTop  = initialScreenTop.Value;
			}
			else
			{
				content.FloatingLeft = 0;
				content.FloatingTop = 0;
			}
			DragLogW(
				$"StartDraggingFloatingWindow: content={content.Title} " +
				$"FloatingLeft={content.FloatingLeft:F0} FloatingTop={content.FloatingTop:F0} " +
				$"nativeInitialPlacement={useNativeInitialPlacement}");
			var fwc = CreateFloatingWindow(content, false);
			if (fwc == null) return;
			fwc.ShowHiddenUntilPositioned = useNativeInitialPlacement;
			DragLogW(
				$"StartDraggingFloatingWindow created: size=({fwc.Width:F0}x{fwc.Height:F0}) " +
				$"leftTop=({fwc.Left:F0},{fwc.Top:F0}) startDrag={startDrag}");

			// Wire re-drag: when the user grabs the floating window's title bar (after the
			// initial tear-off or later), restart the native/timer drag from wherever the
			// cursor currently sits inside the window. This mirrors what WPF AvalonDock does
			// with its DragDelta — grab the cursor-relative offset and hand off to the OS.
			// Previously this block was #if !WINDOWS, leaving OnTitleBarDragStarted null on
			// Windows so subsequent drags did nothing.
			fwc.OnTitleBarDragStarted = () =>
			{
				DragLogW($"OnTitleBarDragStarted: IsWindows={OperatingSystem.IsWindows()} UseNativeDrag={UseNativeDrag}");
#if WINDOWS
				// True WinUI native window only: WM_NCLBUTTONDOWN enters the OS modal move
				// loop cleanly. NOT compiled for the Uno Skia (net10.0-desktop) target —
				// there Uno's own Win32 event pump consumes WM_LBUTTONUP, so the modal loop
				// never ends and the window chases the released cursor. See session26 notes.
				if (UseNativeDrag)
				{
					_nativeDrag?.Dispose();
					_nativeDrag = null;
					var (cx, cy) = NativeCursorScreen();
					var (wx, wy) = NativeWindowTopLeft(fwc);
					if (StartNativeDrag(fwc, new Windows.Foundation.Point(Math.Max(0, cx - wx), Math.Max(0, cy - wy))))
						return;
					// fall through to timer path if no native handle
				}
#endif

				if (OperatingSystem.IsWindows())
				{
					// Uno Skia desktop re-drag: use the timer tracker. It polls
					// GetAsyncKeyState(VK_LBUTTON) every tick and stops the moment the
					// button is released, so it cannot get stuck following the cursor —
					// unlike the native WM_NCLBUTTONDOWN loop above (WinUI-only).
					DragLogW("OnTitleBarDragStarted: starting timer tracker (Uno desktop re-drag)");
					_windowsDragTracker?.Stop();
					_windowsDragTracker = null;
					StartWindowsDragTracking(fwc, realDrag: true);
					return;
				}
#if !WINDOWS
				// Always restart tracking from a fresh state. If a previous drag left a stale
				// tracker running, keeping it would block all subsequent compass activations.
				if (OperatingSystem.IsMacOS())
				{
					AvalonDock.Hosting.MacOSWindowTabbing.DragLog(
						$"OnTitleBarDragStarted: nsWin=0x{fwc.NsWindowHandle:X} trackerActive={_dragTracker != null}");
					if (_dragTracker != null)
					{
						_dragTracker.Stop();
						_dragTracker = null;
					}
					StartDragTracking(fwc, realDrag: true);
					return;
				}

				if (OperatingSystem.IsLinux())
				{
					StartLinuxDragTracking(fwc, realDrag: true);
				}
#endif
			};

			// Windows initial tear-off: pre-position the window so the cursor lands a little
			// inside the custom 32px title bar — NOT centered. Centering on a wide pane's
			// title bar (width/2) shot the window off the left edge of the screen (observed
			// adjustedLeft=-287 for a 1582px pane). A small fixed offset keeps the cursor on
			// the title bar regardless of pane width. The timer tracker recomputes its own
			// grab offset from the live cursor in Start(), so this just places the window.
			Windows.Foundation.Point winInitialGrabOffset = new(60, 16);
			if (OperatingSystem.IsWindows() && startDrag && !useNativeInitialPlacement)
			{
				var scale = Math.Max(XamlRoot?.RasterizationScale ?? 1.0, 0.5);
				winInitialGrabOffset = new Windows.Foundation.Point(60.0 * scale, 16.0 * scale);
				fwc.Left -= winInitialGrabOffset.X;
				fwc.Top  -= winInitialGrabOffset.Y;
				DragLogW($"WinGrabOffset: scale={scale:F2} grabOffset=({winInitialGrabOffset.X:F0},{winInitialGrabOffset.Y:F0}) " +
					$"adjustedLeftTop=({fwc.Left:F0},{fwc.Top:F0})");
			}

			fwc.Show();
			if (startDrag && OperatingSystem.IsWindows())
			{
#if WINDOWS
				// WinUI-native only: hand off to the OS move loop. On Uno desktop this is
				// compiled out (the native loop never ends — see OnTitleBarDragStarted).
				if (UseNativeDrag && StartNativeDrag(fwc, winInitialGrabOffset))
				{
					// handed off to the OS move loop
				}
				else
#endif
				{
					DragLogW("Tear-off: starting timer tracker (Uno desktop)");
					StartWindowsDragTracking(fwc, realDrag: true);
				}
			}
#if !WINDOWS
			if (OperatingSystem.IsMacOS())
			{
				AvalonDock.Hosting.MacOSWindowTabbing.DragLog(
					$"fwc.Show() done: nsWin=0x{fwc.NsWindowHandle:X} startDrag={startDrag} " +
					$"actualPos=({AvalonDock.Hosting.MacOSWindowTabbing.GetWindowPosition(fwc.NsWindowHandle).X:F0}," +
					$"{AvalonDock.Hosting.MacOSWindowTabbing.GetWindowPosition(fwc.NsWindowHandle).Y:F0})");
				if (startDrag && fwc.NsWindowHandle != 0)
				{
					var (cursorX, cursorY) = AvalonDock.Hosting.MacOSWindowTabbing.GetCursorLocationQuartz();
					var titleBarGrabOffset = AvalonDock.Hosting.MacOSWindowTabbing.InitialTitleBarGrabOffset;
					var frame = AvalonDock.Hosting.MacOSWindowTabbing.GetWindowFrame(fwc.NsWindowHandle);
					var frameWidth = frame.Width > 0 ? frame.Width : fwc.Width;
					var contentFrame = AvalonDock.Hosting.MacOSWindowTabbing.GetContentViewFrame(fwc.NsWindowHandle);
					var grabOffsetX = contentFrame.Width > 0
						? contentFrame.X + contentFrame.Width / 2.0
						: frameWidth / 2.0;

					// Native path (session 26): BeginDrag positions the window under the
					// cursor at the grab offset, then hands off to AppKit's move loop.
					if (UseNativeDrag && StartNativeDrag(fwc,
						new Windows.Foundation.Point(grabOffsetX, titleBarGrabOffset)))
					{
						return;
					}

					AvalonDock.Hosting.MacOSWindowTabbing.MoveWindow(
						fwc.NsWindowHandle,
						cursorX,
						cursorY,
						grabOffsetX,
						titleBarGrabOffset);
					AvalonDock.Hosting.MacOSWindowTabbing.OrderWindowFront(fwc.NsWindowHandle);
					StartDragTracking(fwc, realDrag: true);
				}
				StartWatchdog();
			}
			else if (startDrag && OperatingSystem.IsLinux())
			{
				StartLinuxDragTracking(fwc, realDrag: true);
			}
#endif
		}

#if !WINDOWS
		// ── Watchdog: detects user title-bar drag and starts the tracker ────────
		// Fires every 200ms while there are visible floating windows.
		// When it sees left-button down AND a floating window has moved since
		// last tick → the user is dragging that window → start full tracker.

		private void StartWatchdog()
		{
			if (_watchdog != null) return; // already running
			_watchdog = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(200)
			};
			_watchdog.Tick += OnWatchdogTick;
			_watchdog.Start();
		}

		private void StopWatchdog()
		{
			if (_watchdog == null) return;
			_watchdog.Stop();
			_watchdog.Tick -= OnWatchdogTick;
			_watchdog = null;
			_lastWinPos.Clear();
		}

		private void OnWatchdogTick(object sender, object e)
		{
			// No floating windows → stop watchdog.
			if (_fwList.Count == 0) { StopWatchdog(); return; }

			bool buttonDown = FloatingWindowDragTracker.IsLeftButtonHeld();

			// Recover from a stale tracker that survived a previous drag end.
			if (_dragTracker != null && !buttonDown)
			{
				_dragTracker.Stop();
				_dragTracker = null;
				_overlayActiveTarget = null;
				_overlayActiveAreas.Clear();
				((Controls.IOverlayWindowHost)this).HideOverlayWindow();
			}

			// Tracker is already running → watchdog takes a back seat.
			if (_dragTracker != null) return;

			if (!buttonDown) { _lastWinPos.Clear(); return; }

			// Button is held. Check if any floating window has moved since last tick.
			foreach (var fwc in _fwList)
			{
				if (!fwc.IsVisible) continue;
				var nsWin = fwc.NsWindowHandle;
				if (nsWin == 0) continue;

				var pos = AvalonDock.Hosting.MacOSWindowTabbing.GetWindowPosition(nsWin);
				if (_lastWinPos.TryGetValue(nsWin, out var prev))
				{
					double dx = Math.Abs(pos.X - prev.X);
					double dy = Math.Abs(pos.Y - prev.Y);
					if (dx > 2 || dy > 2)
					{
						AvalonDock.Hosting.MacOSWindowTabbing.DragLog(
							$"Watchdog detected title drag: nsWin=0x{nsWin:X} prev=({prev.X:F0},{prev.Y:F0}) " +
							$"now=({pos.X:F0},{pos.Y:F0}) delta=({dx:F0},{dy:F0})");
						// The window moved while button is held → user is dragging the title bar.
						_lastWinPos.Clear();
						StartDragTracking(fwc, realDrag: true);
						return;
					}
				}
				_lastWinPos[nsWin] = pos;
			}
		}

		/// <summary>
		/// Returns this DockingManager's top-left corner in Quartz screen coordinates
		/// (top-left origin, Y-down). Used to translate cursor positions into manager-local
		/// coordinates for compass zone hit-testing.
		/// Reads from AppWindow.Position (physical pixels) + RasterizationScale.
		/// </summary>
		// Resolves the host (main) NSWindow handle independently of keyboard focus, and
		// keeps it sticky. Re-resolves only when unset OR when it has accidentally become
		// one of the live floating windows (which steal NSApp.mainWindow during a drag).
		// Mirrors the Windows side, which resolves the main HWND by process, not focus.
		private nint EnsureHostWindowHandle()
		{
			var floats = _fwList
				.Where(f => f != null && f.NsWindowHandle != 0)
				.Select(f => f.NsWindowHandle)
				.ToArray();

			var isFloating = false;
			foreach (var h in floats)
				if (h == _hostWindowHandle) { isFloating = true; break; }

			if (_hostWindowHandle == 0 || isFloating)
				_hostWindowHandle = AvalonDock.Hosting.MacOSWindowTabbing.GetMainAppWindow(floats);

			return _hostWindowHandle;
		}

		private (double X, double Y) ComputeScreenOriginQ()
		{
			try
			{
				var xamlRoot = XamlRoot;
				if (xamlRoot == null) return (0, 0);
				var transform = this.TransformToVisual(null); // null = XamlRoot
				var pt = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

				var host = EnsureHostWindowHandle();

				// Preferred path on macOS: ask Cocoa for the host window's content-area
				// origin in Quartz coords (contentView + convertRectToScreen — the macOS
				// equivalent of Win32 ClientToScreen), then add this manager's offset inside
				// XamlRoot. No hardcoded title-bar guess. The host is resolved focus-independently
				// by EnsureHostWindowHandle so a dragged child window can never be used here.
				var nativeOrigin = host != 0
					? AvalonDock.Hosting.MacOSWindowTabbing.GetWindowContentOriginViaConvert(host)
					: (0.0, 0.0);
				if (nativeOrigin.Item1 != 0 || nativeOrigin.Item2 != 0)
				{
					var result = (nativeOrigin.Item1 + pt.X, nativeOrigin.Item2 + pt.Y);
					DragLogWVerbose($"[OriginQ] host=0x{host:X} convOrigin=({nativeOrigin.Item1:F1},{nativeOrigin.Item2:F1}) " +
						$"pt=({pt.X:F1},{pt.Y:F1}) result=({result.Item1:F1},{result.Item2:F1})");
					return result;
				}

				// Fallback: derive from AppWindow position when native origin is unavailable.
				var scale = xamlRoot.RasterizationScale;
				if (scale <= 0) scale = 1.0;
				var window = Microsoft.UI.Xaml.Window.Current;
				var aw = window?.AppWindow;
				if (aw == null) return (0, 0);
				var pos = aw.Position;
				const double OsTitleBarH = 28.0;
				return (pos.X / scale + pt.X, pos.Y / scale + OsTitleBarH + pt.Y);
			}
			catch { return (0, 0); }
		}

		private void StartDragTracking(LayoutFloatingWindowControl fwc, bool realDrag = false)
		{
			_dragTracker?.Stop();
			var nsWindow = fwc.NsWindowHandle;
			if (nsWindow == 0) return;
			var overlayHost = (Controls.IOverlayWindowHost)this;
			var overlay = overlayHost.ShowOverlayWindow(fwc);
			overlay?.DragEnter(fwc);
			_overlayActiveAreas.Clear();
			_overlayActiveTarget = null;

			// For a real title-bar drag (detected by watchdog), skip the spurious-drop
			// protection (MinTicksBeforeDrop) since the button IS already held.
			var tracker = new FloatingWindowDragTracker(fwc, nsWindow, this, skipInitialDelay: realDrag)
			{
				ManagerScreenOriginQ = ComputeScreenOriginQ(),
				ManagerScreenOriginProvider = () => ComputeScreenOriginQ()
			};
			AvalonDock.Hosting.MacOSWindowTabbing.DragLog(
				$"StartDragTracking: nsWin=0x{nsWindow:X} realDrag={realDrag} " +
				$"managerOrigin=({tracker.ManagerScreenOriginQ.X:F0},{tracker.ManagerScreenOriginQ.Y:F0})");

			// One-shot geometry ground-truth dump for the HOST (main) window.
			AvalonDock.Hosting.MacOSWindowTabbing.DumpWindowGeometry(EnsureHostWindowHandle(), "host");

			tracker.OnCursorInManagerCoords = (hostX, hostY) =>
			{
				var w = ActualWidth;
				var h = ActualHeight;
				const double NearMargin = 64.0;

				bool inside = hostX >= 0 && hostX <= w && hostY >= 0 && hostY <= h;
				bool near = hostX >= -NearMargin && hostX <= w + NearMargin &&
					hostY >= -NearMargin && hostY <= h + NearMargin;
				AvalonDock.Hosting.MacOSWindowTabbing.DragLogVerbose(
					$"CursorInManager: ({hostX:F0},{hostY:F0}) size=({w:F0}x{h:F0}) inside={inside} near={near}");
				if (inside || near)
				{
					UpdateOverlayDragStateForPoint(hostX, hostY, fwc);
				}
				else
				{
					ClearActiveOverlayTargets();
					// Restore the floating window on top when cursor leaves the manager.
					if (fwc.NsWindowHandle != 0)
						AvalonDock.Hosting.MacOSWindowTabbing.OrderWindowFront(fwc.NsWindowHandle);
				}
			};

			tracker.OnDragEnded = () =>
			{
				var activeTarget = _overlayActiveTarget;
				var capturedOverlay = _overlayWindow;

				// Execute the drop BEFORE tearing down state: DragDrop needs both
				// _floatingWindow (cleared by DragLeave) and the overlay (nulled by HideOverlayWindow).
				if (activeTarget != null && capturedOverlay != null)
					capturedOverlay.DragDrop(activeTarget);

				EndOverlayDrag(fwc);
				((Controls.IOverlayWindowHost)this).HideOverlayWindow();
				_dragTracker = null;
			};

			_dragTracker = tracker;
			tracker.Start();
		}
#endif
		// Drag diagnostics. Enabled automatically in Debug builds (no env var); in Release
		// only when UNODOCK_DRAGLOG=1. On macOS everything funnels into the single
		// MacOSWindowTabbing drag log (/tmp/unodock-drag.log, reset per run) so native and
		// managed lines interleave in one file. On Windows it writes to %TEMP%.
		private static readonly bool DragLogEnabled =
#if DEBUG
			true;
#else
			Environment.GetEnvironmentVariable("UNODOCK_DRAGLOG") == "1";
#endif

		// Per-tick hot-path logging. OFF by default — the synchronous file writes at
		// ~60 fps were a primary cause of choppy drags. Opt in with UNODOCK_DRAGLOG_VERBOSE=1.
		private static readonly bool DragLogVerboseEnabled =
			DragLogEnabled && Environment.GetEnvironmentVariable("UNODOCK_DRAGLOG_VERBOSE") == "1";

		private static readonly object _dragLogLock = new object();
		private static string _dragLogPath;

		/// <summary>Per-tick / hot-path logging — no-op unless UNODOCK_DRAGLOG_VERBOSE=1.</summary>
		internal static void DragLogWVerbose(string msg)
		{
#if !WINDOWS
			if (OperatingSystem.IsMacOS())
			{
				AvalonDock.Hosting.MacOSWindowTabbing.DragLogVerbose(msg);
				return;
			}
#endif
			if (Environment.GetEnvironmentVariable("UNODOCK_DRAGLOG_VERBOSE") == "1") DragLogW(msg);
		}

		internal static void DragLogW(string msg)
		{
			if (!DragLogEnabled) return;
#if !WINDOWS
			if (OperatingSystem.IsMacOS())
			{
				AvalonDock.Hosting.MacOSWindowTabbing.DragLog(msg);
				return;
			}
#endif
			try
			{
				_dragLogPath ??= System.IO.Path.Combine(
					System.IO.Path.GetTempPath(), "unodock-drag.log");
				lock (_dragLogLock)
					System.IO.File.AppendAllText(_dragLogPath,
						DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine);
			}
			catch { }
		}

		private (double X, double Y) ComputeScreenOriginLinux()
		{
			try
			{
				var xamlRoot = XamlRoot;
				if (xamlRoot == null) return (0, 0);
				var pt = this.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
				var scale = xamlRoot.RasterizationScale;
				if (scale <= 0) scale = 1.0;
				var window = Microsoft.UI.Xaml.Window.Current;
				var aw = window?.AppWindow;
				if (aw == null)
					return (pt.X, pt.Y);
				var pos = aw.Position;
				return (pos.X / scale + pt.X, pos.Y / scale + pt.Y);
			}
			catch { return (0, 0); }
		}

		private (double X, double Y) ComputeScreenOriginW()
		{
			double scale = 1.0;
			Windows.Foundation.Point pt = default;
			try
			{
				scale = XamlRoot?.RasterizationScale ?? 1.0;
				if (scale <= 0) scale = 1.0;
				pt = this.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
			}
			catch { }

			// Candidate origins, in physical screen pixels. Both are tried/logged so the
			// correct one can be confirmed against live drag coordinates.
			//   pt        = manager offset from the visual root (DIPs)
			//   appPos    = AppWindow.Position = OUTER window frame origin (incl. title bar)
			//   clientPos = ClientToScreen((0,0)) = CLIENT-area origin (below title bar)
			// TransformToVisual(null) measures from the WinUI content/client area, so the
			// matching origin should be the CLIENT origin — appPos is short by the title-bar
			// height, which manifests as a vertical offset in the drop preview/detection.
			var aw = SafeGetAppWindow();
			(double X, double Y)? appOrigin = aw != null
				? (aw.Position.X + pt.X * scale, aw.Position.Y + pt.Y * scale)
				: null;

			(double X, double Y)? clientOrigin = null;
			var hwnd = SafeGetMainWindowHwnd();
			if (hwnd != IntPtr.Zero)
			{
				try
				{
					var cp = new POINT32 { X = 0, Y = 0 };
					if (ClientToScreen(hwnd, ref cp))
						clientOrigin = (cp.X + pt.X * scale, cp.Y + pt.Y * scale);
				}
				catch { }
			}

			if (DragLogEnabled)
			{
				string wr = "n/a";
				if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var rc))
					wr = $"[{rc.Left},{rc.Top} {rc.Right - rc.Left}x{rc.Bottom - rc.Top}]";
				DragLogW($"originW pt=({pt.X:F1},{pt.Y:F1}) scale={scale:F2} hwnd=0x{hwnd:X} " +
					$"winRect={wr} appOrigin={Fmt(appOrigin)} clientOrigin={Fmt(clientOrigin)}");
			}

			// Prefer the client-area origin (correct space); fall back to AppWindow, then pt only.
			if (clientOrigin.HasValue) return clientOrigin.Value;
			if (appOrigin.HasValue) return appOrigin.Value;
			return (pt.X * scale, pt.Y * scale);
		}

		private static string Fmt((double X, double Y)? p)
			=> p.HasValue ? $"({p.Value.X:F0},{p.Value.Y:F0})" : "null";

		private static Microsoft.UI.Windowing.AppWindow SafeGetAppWindow()
		{
			try { return Microsoft.UI.Xaml.Window.Current?.AppWindow; }
			catch { return null; }
		}

		// Resolve the HWND hosting this DockingManager (the main window). On Uno Skia
		// desktop WinRT.Interop may not yield a usable HWND, so fall back to matching the
		// current process's top-level window by its title — the same strategy the floating
		// window controls already use (FindWindowForCurrentProcess).
		private IntPtr SafeGetMainWindowHwnd()
		{
			try
			{
				var window = Microsoft.UI.Xaml.Window.Current;
				if (window != null)
				{
					// On Uno Skia desktop WinRT.Interop.WindowNative.GetWindowHandle returns a
					// bogus handle (observed 0x1) — validate with IsWindow before trusting it.
					try
					{
						var h = WinRT.Interop.WindowNative.GetWindowHandle(window);
						if (IsRealWindow(h)) return h;
					}
					catch { }

					// Match the current process's top-level window by its title — the same
					// strategy LayoutFloatingWindowControl.FindWindowForCurrentProcess uses.
					var title = window.Title;
					if (!string.IsNullOrEmpty(title))
					{
						var byTitle = FindTopLevelWindowByTitle(title);
						if (IsRealWindow(byTitle)) return byTitle;
					}
				}
			}
			catch { }

			try
			{
				var active = GetActiveWindow();
				if (IsRealWindow(active)) return active;
			}
			catch { }
			return IntPtr.Zero;
		}

		private void StartLinuxDragTracking(LayoutFloatingWindowControl fwc, bool realDrag = false)
		{
			if (fwc == null)
				return;

			_linuxDragTracker?.Stop();
			var overlayHost = (Controls.IOverlayWindowHost)this;
			var overlay = overlayHost.ShowOverlayWindow(fwc);
			overlay?.DragEnter(fwc);
			_overlayActiveAreas.Clear();
			_overlayActiveTarget = null;

			var tracker = new LinuxFloatingWindowDragTracker(fwc, this, skipInitialDelay: realDrag)
			{
				ManagerScreenOriginProvider = () => ComputeScreenOriginLinux(),
			};

			tracker.OnCursorInManagerCoords = (hostX, hostY) =>
			{
				var localX = hostX;
				var localY = hostY;
				var w = ActualWidth;
				var h = ActualHeight;
				const double NearMargin = 64.0;

				var inside = localX >= 0 && localX <= w && localY >= 0 && localY <= h;
				var near = localX >= -NearMargin && localX <= w + NearMargin
					&& localY >= -NearMargin && localY <= h + NearMargin;
				if (inside || near)
				{
					UpdateOverlayDragStateForPoint(localX, localY, fwc);
				}
				else
				{
					ClearActiveOverlayTargets();
				}
			};

			tracker.OnDragEnded = () =>
			{
				DragLogW("LinuxDragTracker.OnDragEnded: button released -> drag finished");
				var activeTarget = _overlayActiveTarget;
				var capturedOverlay = _overlayWindow;
				if (activeTarget != null && capturedOverlay != null)
					capturedOverlay.DragDrop(activeTarget);

				EndOverlayDrag(fwc);
				((Controls.IOverlayWindowHost)this).HideOverlayWindow();
				_linuxDragTracker = null;
			};

			_linuxDragTracker = tracker;
			DragLogW($"StartLinuxDragTracking: realDrag={realDrag} tracker started");
			tracker.Start();
		}

		// A usable top-level window: non-null handle that IsWindow confirms AND that yields a
		// non-empty client rect (rejects the bogus 0x1 handle WinRT.Interop hands back on Uno).
		private static bool IsRealWindow(IntPtr hwnd)
		{
			if (hwnd == IntPtr.Zero) return false;
			try
			{
				if (!IsWindow(hwnd)) return false;
				return GetWindowRect(hwnd, out var rc) && (rc.Right - rc.Left) > 0 && (rc.Bottom - rc.Top) > 0;
			}
			catch { return false; }
		}

		private static IntPtr FindTopLevelWindowByTitle(string title)
		{
			var currentPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
			var result = IntPtr.Zero;
			EnumWindows((hwnd, _) =>
			{
				if (!IsWindowVisible(hwnd)) return true;
				GetWindowThreadProcessId(hwnd, out var pid);
				if (pid != currentPid) return true;
				var sb = new StringBuilder(256);
				GetWindowText(hwnd, sb, sb.Capacity);
				if (sb.ToString() == title) { result = hwnd; return false; }
				return true;
			}, IntPtr.Zero);
			return result;
		}

		private void StartWindowsDragTracking(LayoutFloatingWindowControl fwc, bool realDrag = false)
		{
			if (fwc == null)
				return;

			_windowsDragTracker?.Stop();
			var overlayHost = (Controls.IOverlayWindowHost)this;
			var overlay = overlayHost.ShowOverlayWindow(fwc);
			overlay?.DragEnter(fwc);
			_overlayActiveAreas.Clear();
			_overlayActiveTarget = null;

			// Bring floating window above the main window immediately. The overlay window is
			// already HWND_TOPMOST (set in StyleWindowsOverlayWindow), so it sits above the
			// floating window automatically. Mirrors macOS OrderWindowFront in StartDragTracking.
			fwc.BringToFrontWindows();

			var tracker = new WindowsFloatingWindowDragTracker(fwc, this, skipInitialDelay: realDrag)
			{
				ManagerScreenOriginProvider = () => ComputeScreenOriginW(),
			};

			tracker.OnCursorInManagerCoords = (hostX, hostY) =>
			{
				var scale = XamlRoot?.RasterizationScale ?? 1.0;
				if (scale <= 0) scale = 1.0;
				var localX = hostX / scale;
				var localY = hostY / scale;
				var w = ActualWidth;
				var h = ActualHeight;
				const double NearMargin = 64.0;

				var inside = localX >= 0 && localX <= w && localY >= 0 && localY <= h;
				var near = localX >= -NearMargin && localX <= w + NearMargin
					&& localY >= -NearMargin && localY <= h + NearMargin;
				if (inside || near)
				{
					UpdateOverlayDragStateForPoint(localX, localY, fwc);
				}
				else
				{
					ClearActiveOverlayTargets();
					// Cursor left the manager area: restore floating window to the top of the
					// non-topmost tier so it's above the main window. Mirrors macOS OrderWindowFront.
					fwc.BringToFrontWindows();
				}
			};

			tracker.OnDragEnded = () =>
			{
				DragLogW("WindowsDragTracker.OnDragEnded: button released → drag finished");
				var activeTarget = _overlayActiveTarget;
				var capturedOverlay = _overlayWindow;
				if (activeTarget != null && capturedOverlay != null)
					capturedOverlay.DragDrop(activeTarget);

				EndOverlayDrag(fwc);
				((Controls.IOverlayWindowHost)this).HideOverlayWindow();
				_windowsDragTracker = null;
			};

			_windowsDragTracker = tracker;
			DragLogW($"StartWindowsDragTracking: realDrag={realDrag} tracker started");
			tracker.Start();
		}

		private void UpdateOverlayDragStateForPoint(double hostX, double hostY, LayoutFloatingWindowControl fwc)
		{
			if (_overlayWindow == null || _templateRoot == null)
				return;

			Windows.Foundation.Point dragPoint;
			try
			{
				var managerOriginInRoot = this.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
				dragPoint = new Windows.Foundation.Point(managerOriginInRoot.X + hostX, managerOriginInRoot.Y + hostY);
			}
			catch
			{
				return;
			}

			{
				if (DragLogVerboseEnabled)
				{
					var managerOriginInRoot = this.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
#if WINDOWS
					var (ox, oy) = ComputeScreenOriginW();
					DragLogWVerbose($"drag host=({hostX:F1},{hostY:F1}) dragPoint=({dragPoint.X:F1},{dragPoint.Y:F1}) " +
						$"mgrOriginRoot=({managerOriginInRoot.X:F1},{managerOriginInRoot.Y:F1}) originW=({ox:F0},{oy:F0})");
#else
					DragLogWVerbose($"[DragQ] host=({hostX:F1},{hostY:F1}) dragPoint=({dragPoint.X:F1},{dragPoint.Y:F1}) " +
						$"mgrOriginRoot=({managerOriginInRoot.X:F1},{managerOriginInRoot.Y:F1})");
#endif
				}
			}

			foreach (var area in _overlayActiveAreas)
				_overlayWindow.DragLeave(area);
			_overlayActiveAreas.Clear();

			var host = (Controls.IOverlayWindowHost)this;
			var allAreas = host.GetDropAreas(fwc).ToList();
			var selected = SelectActiveOverlayAreas(allAreas, dragPoint, hostX, hostY).ToList();
			foreach (var area in selected)
			{
				_overlayActiveAreas.Add(area);
				_overlayWindow.DragEnter(area);
			}
			DragLogWVerbose($"[Select] dragPoint=({dragPoint.X:F0},{dragPoint.Y:F0}) all={allAreas.Count}[{string.Join(",", allAreas.Select(a => a.Type))}] selected={selected.Count}[{string.Join(",", selected.Select(a => a.Type))}]");

			var newTarget = _overlayWindow.GetTargets().FirstOrDefault(dt => dt.HitTestScreen(dragPoint));
			if (!ReferenceEquals(_overlayActiveTarget, newTarget))
			{
				if (_overlayActiveTarget != null)
					_overlayWindow.DragLeave(_overlayActiveTarget);
				_overlayActiveTarget = newTarget;
				if (_overlayActiveTarget != null)
					_overlayWindow.DragEnter(_overlayActiveTarget);
			}

			// Mirror the updated compass (areas shown + active-target highlight) to the
			// native per-pixel-alpha window so it tracks the drag transparently and on top.
			// Call directly on both platforms (we are on the UI thread via the DispatcherTimer
			// tick): a Low-priority enqueue is starved by the 16ms tick during continuous
			// dragging and the compass never repaints. The in-flight guard inside
			// RefreshNativeLayeredOverlay coalesces overlapping captures.
			if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
				RefreshNativeLayeredOverlay();
		}

		private IEnumerable<Controls.IDropArea> SelectActiveOverlayAreas(
			IEnumerable<Controls.IDropArea> areas,
			Windows.Foundation.Point dragPoint,
			double hostX,
			double hostY)
		{
			var candidates = areas.Where(a => IsWithinOverlayAreaHitZone(a, dragPoint)).ToList();
			if (candidates.Count == 0)
				return candidates;

			// WPF behavior: when pointer is on splitter, only manager outer targets stay active.
			if (FindTopMostControlAtPoint<Controls.LayoutGridResizerControl>(hostX, hostY) != null)
				return candidates.Where(a => a.Type == Controls.DropAreaType.DockingManager).ToList();

			var paneCandidates = candidates
				.Where(IsPaneDropAreaType)
				.ToList();

			if (paneCandidates.Count <= 1)
				return candidates;

			// If cursor is inside one/more pane rects, prefer the tightest one (most specific).
			var strict = paneCandidates.Where(a => a.DetectionRect.Contains(dragPoint)).ToList();
			var bestPane = (strict.Count > 0 ? strict : paneCandidates)
				.OrderBy(a => GetRectDistanceToPointSquared(a.DetectionRect, dragPoint))
				.ThenBy(a => a.DetectionRect.Width * a.DetectionRect.Height)
				.FirstOrDefault();

			if (bestPane == null)
				return candidates;

			return candidates
				.Where(a => a.Type == Controls.DropAreaType.DockingManager || ReferenceEquals(a, bestPane))
				.ToList();
		}

		private static bool IsPaneDropAreaType(Controls.IDropArea area)
		{
			return area.Type == Controls.DropAreaType.DocumentPane
				|| area.Type == Controls.DropAreaType.DocumentPaneGroup
				|| area.Type == Controls.DropAreaType.AnchorablePane;
		}

		private static double GetRectDistanceToPointSquared(Windows.Foundation.Rect rect, Windows.Foundation.Point point)
		{
			var dx = 0.0;
			if (point.X < rect.Left) dx = rect.Left - point.X;
			else if (point.X > rect.Right) dx = point.X - rect.Right;

			var dy = 0.0;
			if (point.Y < rect.Top) dy = rect.Top - point.Y;
			else if (point.Y > rect.Bottom) dy = point.Y - rect.Bottom;

			return dx * dx + dy * dy;
		}

		private static bool IsWithinOverlayAreaHitZone(Controls.IDropArea area, Windows.Foundation.Point dragPoint)
		{
			var rect = area.DetectionRect;
			if (rect.Contains(dragPoint))
				return true;

			// Full-compass outer buttons can render slightly outside pane bounds.
			// Expand pane/group hit zones so pointer-over-button still keeps the area active.
			var inflate = area.Type switch
			{
				Controls.DropAreaType.DocumentPane => 64.0,
				Controls.DropAreaType.DocumentPaneGroup => 64.0,
				Controls.DropAreaType.AnchorablePane => 48.0,
				_ => 0.0,
			};

			if (inflate <= 0)
				return false;

			var expanded = new Windows.Foundation.Rect(
				rect.X - inflate,
				rect.Y - inflate,
				rect.Width + inflate * 2,
				rect.Height + inflate * 2);
			return expanded.Contains(dragPoint);
		}

		private void ClearActiveOverlayTargets()
		{
			if (_overlayWindow == null)
				return;

			if (_overlayActiveTarget != null)
				_overlayWindow.DragLeave(_overlayActiveTarget);
			_overlayActiveTarget = null;

			foreach (var area in _overlayActiveAreas)
				_overlayWindow.DragLeave(area);
			_overlayActiveAreas.Clear();
		}

		private void EndOverlayDrag(LayoutFloatingWindowControl fwc)
		{
			if (_overlayWindow == null)
				return;

			ClearActiveOverlayTargets();
			_overlayWindow.DragLeave(fwc);
		}

		private bool IsPointerOverControl<TControl>(double hostX, double hostY) where TControl : FrameworkElement
		{
			return FindTopMostControlAtPoint<TControl>(hostX, hostY) != null;
		}

		private TControl FindTopMostControlAtPoint<TControl>(double hostX, double hostY) where TControl : FrameworkElement
		{
			var visualRoot = (DependencyObject)_templateRoot ?? this;
			TControl best = null;
			double bestArea = double.MaxValue;
			foreach (var control in EnumerateVisualsOfType<TControl>(visualRoot))
			{
				if (control.Visibility != Visibility.Visible || control.ActualWidth <= 0 || control.ActualHeight <= 0)
					continue;

				try
				{
					var origin = control.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point(0, 0));
					if (hostX >= origin.X && hostX <= origin.X + control.ActualWidth
						&& hostY >= origin.Y && hostY <= origin.Y + control.ActualHeight)
					{
						var area = control.ActualWidth * control.ActualHeight;
						if (area < bestArea)
						{
							bestArea = area;
							best = control;
						}
					}
				}
				catch
				{
					// Ignore stale visuals during template churn.
				}
			}

			return best;
		}

		private static IEnumerable<TControl> EnumerateVisualsOfType<TControl>(DependencyObject root) where TControl : DependencyObject
		{
			if (root == null)
				yield break;

			var stack = new Stack<DependencyObject>();
			stack.Push(root);
			while (stack.Count > 0)
			{
				var current = stack.Pop();
				if (current is TControl matched)
					yield return matched;

				var count = VisualTreeHelper.GetChildrenCount(current);
				for (var i = 0; i < count; i++)
					stack.Push(VisualTreeHelper.GetChild(current, i));
			}
		}

		/// <summary>
		/// Remove empty panes and collapse single-child panels, then rebuild the visual tree.
		/// Call after any structural layout mutation (float-out, drop, close).
		/// </summary>
		private void RefreshAfterLayoutMutation()
		{
			Layout?.CollectGarbage();
			RebuildLayoutControls(Layout);
		}

		internal void RefreshAfterOverlayDrop()
		{
			RefreshAfterLayoutMutation();
		}

		internal void CompleteOverlayDrop(Controls.LayoutFloatingWindowControl floatingWindowControl)
		{
			if (floatingWindowControl == null)
				return;

			RemoveFloatingWindowAfterDrop(floatingWindowControl);
		}

		private LayoutContent ExtractPrimaryFloatingContent(LayoutFloatingWindowControl fwc)
		{
			if (fwc?.Model is LayoutAnchorableFloatingWindow afwModel)
				return afwModel.RootPanel?.Descendents().OfType<LayoutAnchorable>().FirstOrDefault();
			if (fwc?.Model is LayoutDocumentFloatingWindow dfwModel)
				return dfwModel.RootPanel?.Descendents().OfType<LayoutDocument>().FirstOrDefault();
			return null;
		}

		private void RemoveFloatingWindowAfterDrop(LayoutFloatingWindowControl floatingWindowControl)
		{
			floatingWindowControl.KeepContentVisibleOnClose = true;
			floatingWindowControl.InternalClose();
			_fwList.Remove(floatingWindowControl);
			Layout?.FloatingWindows.Remove(floatingWindowControl.Model as LayoutFloatingWindow);
#if !WINDOWS
			if (OperatingSystem.IsMacOS() && _fwList.Count == 0)
				StopWatchdog();
#endif
		}

		// InsertNewPane removed — logic now lives in LayoutRootMutations.InsertPane (shared layer).

		/// <summary>Force-shows the OverlayWindow with all drop-target groups visible,
		/// so the compass button visuals can be inspected without an actual drag in progress.</summary>
		public void ShowOverlayForDiagnostics()
		{
			var host = (Controls.IOverlayWindowHost)this;
			var overlay = host.ShowOverlayWindow(null);
			if (overlay is not Controls.OverlayWindow ow) return;

			// Defer one frame: ApplyTemplate() forces PART_ wiring but the layout pass that
			// actually measures and sizes the groups runs asynchronously. Waiting one Normal-priority
			// frame ensures all groups are sized before we position / show them.
			DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
			{
				ow.ShowForDiagnostics(new Controls.OverlayDropArea(this, Controls.DropAreaType.DockingManager));

				if (_templateRoot != null)
				{
					var docPane = EnumerateVisualsOfType<Controls.LayoutDocumentPaneControl>(_templateRoot)
						.FirstOrDefault(c => c.Visibility == Visibility.Visible && c.ActualWidth > 0);
					if (docPane != null)
						ow.ShowForDiagnostics(new Controls.OverlayDropArea(docPane, Controls.DropAreaType.DocumentPane));
				}

				// Mirror the compass to the native per-pixel-alpha window. Defer once more so the
				// just-shown groups complete their layout pass before capture — at Normal priority
				// (Low is starved by the drag timer and would never run during a drag).
				if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
					DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal,
						RefreshNativeLayeredOverlay);
			});
		}

		/// <summary>Programmatically execute a drop as if the user released the floating window
		/// over the given drop zone. Used by tests and DevFlow diagnostic actions.</summary>
		public string MeasureOverlayVisualTree()
			=> _overlayWindowControl?.MeasureVisualTree() ?? "overlayWindowControl=null";

		public string GetDragStatus()
		{
			var w = ActualWidth; var h = ActualHeight;
			var tmplOk = _templateRoot != null;
			var activeTargetType = _overlayActiveTarget?.Type.ToString() ?? "none";
#if !WINDOWS
			if (OperatingSystem.IsWindows())
			{
				var (wox, woy) = ComputeScreenOriginW();
				return $"managerSize={w:F0}x{h:F0} templateRoot={tmplOk} activeTarget={activeTargetType} trackerRunning={_windowsDragTracker != null} originComputed=({wox:F0},{woy:F0})";
			}

			if (OperatingSystem.IsLinux())
			{
				var (lox, loy) = ComputeScreenOriginLinux();
				return $"managerSize={w:F0}x{h:F0} templateRoot={tmplOk} activeTarget={activeTargetType} trackerRunning={_linuxDragTracker != null} originComputed=({lox:F0},{loy:F0})";
			}

			var (ox, oy) = ComputeScreenOriginQ();
			return $"managerSize={w:F0}x{h:F0} templateRoot={tmplOk} activeTarget={activeTargetType} trackerRunning={_dragTracker != null} watchdogRunning={_watchdog != null} originComputed=({ox:F0},{oy:F0})";
#else
			var (ox, oy) = ComputeScreenOriginW();
			return $"managerSize={w:F0}x{h:F0} templateRoot={tmplOk} activeTarget={activeTargetType} trackerRunning={_windowsDragTracker != null} originComputed=({ox:F0},{oy:F0})";
#endif
		}

		/// <summary>Manually start the drag tracker for a floating window.
		/// Used by DevFlow tests — CGEvent-injected drags don't update the hardware
		/// button state that CGEventSourceButtonState(CombinedSession) reads, so the
		/// watchdog never fires. Call this right before starting a DevFlow drag.</summary>
		public void StartTrackerForFloatingWindow(LayoutFloatingWindowControl fwc)
		{
#if !WINDOWS
			if (OperatingSystem.IsWindows())
			{
				StartWindowsDragTracking(fwc, realDrag: true);
				return;
			}

			if (OperatingSystem.IsLinux())
			{
				StartLinuxDragTracking(fwc, realDrag: true);
				return;
			}

			StartDragTracking(fwc, realDrag: true);
#else
			StartWindowsDragTracking(fwc, realDrag: true);
#endif
		}

		public void SimulateDrop(LayoutFloatingWindowControl fwc, CompassDropZone zone)
		{
			_windowsDragTracker?.Stop();
			_windowsDragTracker = null;
#if !WINDOWS
			_linuxDragTracker?.Stop();
			_linuxDragTracker = null;
			_dragTracker?.Stop();
			_dragTracker = null;
#endif
			if (zone == CompassDropZone.None || fwc == null) return;

			var content = ExtractPrimaryFloatingContent(fwc);
			if (content == null) return;

			RemoveFloatingWindowAfterDrop(fwc);

			LayoutRootMutations.InsertPane(Layout, content, zone);
			RefreshAfterLayoutMutation();
		}

		private readonly List<LayoutFloatingWindowControl> _fwList = new List<LayoutFloatingWindowControl>();

		public IEnumerable<LayoutFloatingWindowControl> FloatingWindows => _fwList;

		/// <summary>Close every floating window — called when the host app window closes.</summary>
		public void CloseAllFloatingWindows()
		{
			foreach (var fwc in _fwList.ToList())
				fwc.InternalClose();
			_fwList.Clear();
		}

		private LayoutFloatingWindowControl CreateFloatingWindow(LayoutContent content, bool isContentImmutable)
		{
			if (!content.CanFloat) return null;

			var parentPane = content.Parent as ILayoutPane;
			var parentPanePositionable = content.Parent as ILayoutPositionableElement;
			var parentPaneActualSize = content.Parent as ILayoutPositionableElementWithActualSize;
			var contentIndex = parentPane?.Children.ToList().IndexOf(content) ?? -1;

			if (content.FindParent<LayoutFloatingWindow>() == null)
			{
				((ILayoutPreviousContainer)content).PreviousContainer = parentPane;
				content.PreviousContainerIndex = contentIndex;
			}

			if (contentIndex >= 0)
			{
				parentPane.RemoveChildAt(contentIndex);
				// Prune empty containers immediately so the visual tree stays clean.
				RefreshAfterLayoutMutation();
			}

			var fwWidth  = content.FloatingWidth  != 0 ? content.FloatingWidth  : parentPaneActualSize?.ActualWidth  + 10 ?? 400;
			var fwHeight = content.FloatingHeight != 0 ? content.FloatingHeight : parentPaneActualSize?.ActualHeight + 10 ?? 300;

			if (OperatingSystem.IsLinux())
			{
				// GNOME Mutter auto-maximizes X11 windows that map at ≥ ~80% of the work
				// area, which turns a full-window-sized docked pane into a maximized float.
				// Clamp the initial float size below that threshold; the user can resize up.
				var rootSize = XamlRoot?.Size ?? default;
				if (rootSize.Width > 0 && rootSize.Height > 0)
				{
					fwWidth  = Math.Min(fwWidth,  rootSize.Width  * 0.7);
					fwHeight = Math.Min(fwHeight, rootSize.Height * 0.7);
				}
			}

			LayoutFloatingWindowControl fwc;

			if (content is LayoutAnchorable anchorable)
			{
				var fw = new LayoutAnchorableFloatingWindow
				{
					RootPanel = new LayoutAnchorablePaneGroup(
						new LayoutAnchorablePane(anchorable)
						{
							DockWidth  = parentPanePositionable?.DockWidth  ?? new GridLength(1, GridUnitType.Star),
							DockHeight = parentPanePositionable?.DockHeight ?? new GridLength(1, GridUnitType.Star),
						})
				};
				Layout.FloatingWindows.Add(fw);
				fwc = new LayoutAnchorableFloatingWindowControl(fw, isContentImmutable)
				{
					Width  = fwWidth,
					Height = fwHeight,
					Left   = content.FloatingLeft,
					Top    = content.FloatingTop,
				};
			}
			else if (content is LayoutDocument document)
			{
				var fw = new LayoutDocumentFloatingWindow
				{
					RootPanel = new LayoutDocumentPaneGroup(
						new LayoutDocumentPane(document)
						{
							DockWidth  = parentPanePositionable?.DockWidth  ?? new GridLength(1, GridUnitType.Star),
							DockHeight = parentPanePositionable?.DockHeight ?? new GridLength(1, GridUnitType.Star),
						})
				};
				Layout.FloatingWindows.Add(fw);
				fwc = new LayoutDocumentFloatingWindowControl(fw, isContentImmutable)
				{
					Width  = fwWidth,
					Height = fwHeight,
					Left   = content.FloatingLeft,
					Top    = content.FloatingTop,
				};
			}
			else return null;

			_fwList.Add(fwc);
			return fwc;
		}

		// ── Layout item lookup ───────────────────────────────────────────────

		private readonly List<Controls.LayoutItem> _layoutItems = new List<Controls.LayoutItem>();

		public Controls.LayoutItem GetLayoutItemFromModel(LayoutContent model)
			=> _layoutItems.FirstOrDefault(i => i.Model == model);

		// ── Loaded / Unloaded ────────────────────────────────────────────────

		private void OnLayoutPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(LayoutRoot.ActiveContent))
				SyncActiveContentFromLayout();
			else if (e.PropertyName == nameof(LayoutRoot.RootPanel))
			{
				if (Layout != null && (IsLoaded || LayoutRootPanel != null))
					RebuildLayoutControls(Layout);
			}
		}

		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			#if !WINDOWS
			// Capture the host window now, while no floating/overlay windows exist yet.
			if (OperatingSystem.IsMacOS())
				_hostWindowHandle = AvalonDock.Hosting.MacOSWindowTabbing.GetMainAppWindow(System.Array.Empty<nint>());
			#endif
			if (Layout != null) RebuildLayoutControls(Layout);
		}

		private void OnUnloaded(object sender, RoutedEventArgs e) { }

		protected override void OnApplyTemplate()
		{
			base.OnApplyTemplate();
			// Capture the template root Grid so we can overlay the compass canvas.
			_templateRoot = GetTemplateChild("PART_TemplateRoot") as Microsoft.UI.Xaml.Controls.Grid;
		}

		private void RebuildLayoutControls(LayoutRoot layout)
		{
			// Ensure Manager is set before CreateUIElementForModel so child controls
			// can access Manager.GridSplitterWidth etc. during UpdateChildren().
			if (layout.Manager != this) layout.Manager = this;

			LayoutRootPanel = CreateUIElementForModel(layout.RootPanel) as Controls.LayoutPanelControl;

			// Phase 11: real anchor side controls (auto-hide tabs along each edge).
			LeftSidePanel   = new Controls.LayoutAnchorSideControl(layout.LeftSide);
			RightSidePanel  = new Controls.LayoutAnchorSideControl(layout.RightSide);
			TopSidePanel    = new Controls.LayoutAnchorSideControl(layout.TopSide);
			BottomSidePanel = new Controls.LayoutAnchorSideControl(layout.BottomSide);

			// Phase 16: build LayoutItem wrappers for every content item so that
			// GetLayoutItemFromModel() returns real objects with working commands.
			_layoutItems.Clear();
			foreach (var content in layout.Descendents().OfType<LayoutContent>())
			{
				Controls.LayoutItem item = content is LayoutAnchorable anc
					? new Controls.LayoutAnchorableItem(anc, this)
					: content is LayoutDocument doc
						? new Controls.LayoutDocumentItem(doc, this)
						: new Controls.LayoutItem(content, this);
				_layoutItems.Add(item);
			}
		}

		// ── CreateUIElementForModel ──────────────────────────────────────────

		public UIElement CreateUIElementForModel(ILayoutElement model)
		{
			if (model is AvalonDockLayoutPanel p)
				return new Controls.LayoutPanelControl(p);
			if (model is LayoutAnchorablePaneGroup apg)
				return new Controls.LayoutAnchorablePaneGroupControl(apg);
			if (model is LayoutDocumentPaneGroup dpg)
				return new Controls.LayoutDocumentPaneGroupControl(dpg);
			if (model is LayoutDocumentPane dp)
				return new Controls.LayoutDocumentPaneControl(dp, true);
			if (model is LayoutAnchorablePane ap)
				return new Controls.LayoutAnchorablePaneControl(ap, true);

			// Floating windows — create the host control (shows in separate Uno Window).
			if (model is LayoutAnchorableFloatingWindow afwModel)
			{
				var fwc = new Controls.LayoutAnchorableFloatingWindowControl(afwModel);
				_fwList.Add(fwc);
				return null; // floating controls don't go into the main visual tree
			}
			if (model is LayoutDocumentFloatingWindow dfwModel)
			{
				var fwc = new Controls.LayoutDocumentFloatingWindowControl(dfwModel);
				_fwList.Add(fwc);
				return null;
			}

			// Anchor side controls: Phase 6.
			return null;
		}

		// ── IOverlayWindowHost (Phase 4 drag-drop) ───────────────────────────

		bool Controls.IOverlayWindowHost.HitTestScreen(Windows.Foundation.Point p)
		{
			if (_templateRoot == null)
				return false;

			var rect = _templateRoot.GetScreenArea();
			return rect.Contains(p);
		}

		// OverlayWindow is hosted in a transient OS window during drags so the
		// compass can appear above separate floating child windows, matching WPF.
		private Controls.OverlayWindow _overlayWindowControl;
		private Window _overlayNativeWindow;
		// On Windows the Uno overlay window cannot be made transparent (Win32 Skia GL
		// backend). It is therefore kept off-screen purely as a render+logic source, and the
		// compass is mirrored to this native per-pixel-alpha window positioned over the
		// manager and kept topmost (above the floating child window).
		private Controls.WindowsLayeredOverlay _layeredOverlay;
#if !WINDOWS
		private Controls.MacOSLayeredOverlay _macLayeredOverlay;
		private Canvas _overlayVisualHost;
#endif

		Controls.IOverlayWindow Controls.IOverlayWindowHost.ShowOverlayWindow(
			Controls.LayoutFloatingWindowControl w)
		{
			_ = w;
			if (_overlayWindowControl == null)
				_overlayWindowControl = new Controls.OverlayWindow(this);

			if (OperatingSystem.IsMacOS())
			{
#if !WINDOWS
				EnsureMacOverlayVisualHost();
				PositionOverlayNativeWindow();
				_overlayWindow = _overlayWindowControl;
				return _overlayWindow;
#endif
			}

			if (_overlayNativeWindow == null)
			{
				var root = new Grid
				{
					Width = Math.Max(1, ActualWidth),
					Height = Math.Max(1, ActualHeight),
					Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
				};

				// Forward the active theme ResourceDictionary directly into the OverlayWindow
				// control's Resources. TryResolveResource walks the visual tree via
				// VisualTreeHelper.GetParent, but the off-screen window hasn't been laid out yet
				// when ApplyTemplate is called, so the visual parent is null — the walk stops at
				// 'this'. By merging into the control itself, the very first check finds the
				// VS2013 DataTemplate keys and returns themed icons instead of fallback glyphs.
				// We keep the Grid merge too so ThemeResource bindings inside the DataTemplates
				// can also resolve once ContentPresenters enter the visual tree.
				if (_currentThemeDict != null)
				{
					_overlayWindowControl.Resources.MergedDictionaries.Add(_currentThemeDict);
					root.Resources.MergedDictionaries.Add(_currentThemeDict);
				}

				_overlayWindowControl.Width = root.Width;
				_overlayWindowControl.Height = root.Height;
				_overlayWindowControl.HorizontalAlignment = HorizontalAlignment.Stretch;
				_overlayWindowControl.VerticalAlignment = VerticalAlignment.Stretch;
				_overlayWindowControl.Visibility = Visibility.Visible;
				root.Children.Add(_overlayWindowControl);

				try
				{
					_overlayNativeWindow = new Window
					{
						Title = "UnoDock Overlay",
						Content = root,
					};
					// Linux: this window IS the visible compass surface (unlike Windows,
					// where it is parked off-screen as a render source for the native
					// layered window). Strip chrome and pin topmost BEFORE Activate so it
					// never flashes as a decorated normal window.
					if (OperatingSystem.IsLinux())
					{
						// The Uno X11 renderer clears each frame with Window.Background
						// (default opaque WHITE) onto its depth-32 ARGB visual, so a
						// transparent background is the only way to get a per-pixel
						// transparent window. The property is internal in Uno 6.5
						// (setter raises the change event the X11 host listens to),
						// hence reflection.
						try
						{
							var bgProp = typeof(Window).GetProperty(
								"Background",
								System.Reflection.BindingFlags.Instance
									| System.Reflection.BindingFlags.NonPublic
									| System.Reflection.BindingFlags.Public);
							bgProp?.SetValue(
								_overlayNativeWindow,
								new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)));
							DragLogW($"[LinuxOverlay] transparent background set={bgProp != null}");
						}
						catch (Exception ex)
						{
							DragLogW($"[LinuxOverlay] transparent background failed: {ex.Message}");
						}
						StyleLinuxOverlayWindow();
					}
					_overlayNativeWindow.Activate();
				}
				catch
				{
					// Headless tests can construct controls without a native Uno window
					// implementation. Keep the overlay control usable for pure model/control
					// tests; real apps will have created and activated the native window.
					_overlayNativeWindow = null;
				}
				_overlayWindowControl.ApplyTemplate();
				if (OperatingSystem.IsWindows())
					_layeredOverlay ??= new Controls.WindowsLayeredOverlay();
			}

			if (_overlayNativeWindow != null)
				PositionOverlayNativeWindow();
			_overlayWindow = _overlayWindowControl;
			return _overlayWindow;
		}

#if !WINDOWS
		private void EnsureMacOverlayVisualHost()
		{
			if (_templateRoot == null)
				return;

			if (_overlayVisualHost == null)
			{
				_overlayVisualHost = new Canvas
				{
					Width = 1,
					Height = 1,
					Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
					IsHitTestVisible = false,
					Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 1, 1) },
				};

				if (_currentThemeDict != null)
				{
					_overlayWindowControl.Resources.MergedDictionaries.Add(_currentThemeDict);
					_overlayVisualHost.Resources.MergedDictionaries.Add(_currentThemeDict);
				}

				_overlayWindowControl.Width = Math.Max(1, ActualWidth);
				_overlayWindowControl.Height = Math.Max(1, ActualHeight);
				_overlayWindowControl.HorizontalAlignment = HorizontalAlignment.Left;
				_overlayWindowControl.VerticalAlignment = VerticalAlignment.Top;
				_overlayWindowControl.Visibility = Visibility.Visible;
				_overlayVisualHost.Children.Add(_overlayWindowControl);
				_templateRoot.Children.Add(_overlayVisualHost);
				_overlayWindowControl.ApplyTemplate();
			}

			_macLayeredOverlay ??= new Controls.MacOSLayeredOverlay();
		}
#endif

		void Controls.IOverlayWindowHost.HideOverlayWindow()
		{
			try { _layeredOverlay?.Dispose(); } catch { }
			_layeredOverlay = null;
#if !WINDOWS
			try { _macLayeredOverlay?.Dispose(); } catch { }
			_macLayeredOverlay = null;
			try
			{
				if (_overlayVisualHost != null && _templateRoot != null)
					_templateRoot.Children.Remove(_overlayVisualHost);
			}
			catch { }
			_overlayVisualHost = null;
#endif
			try { _overlayNativeWindow?.Close(); } catch { }
			_overlayNativeWindow = null;
			_overlayWindowControl = null;
			_overlayWindow = null;
		}

		// Coalescing guard: RenderAsync can take longer than one 16ms tick, so direct
		// per-tick calls would overlap. While a refresh is in flight, later calls just set
		// the dirty flag; the in-flight refresh re-runs once more at the end to capture the
		// latest compass state. Guarantees the final frame matches the final drag state.
		private bool _refreshInFlight;
		private bool _refreshPending;

		// Render the off-screen overlay control to a bitmap and push it to the native
		// per-pixel-alpha window over the manager. Called whenever the compass visual changes.
		private async void RefreshNativeLayeredOverlay()
		{
			if (_refreshInFlight) { _refreshPending = true; return; }
			_refreshInFlight = true;
			try
			{
				do
				{
					_refreshPending = false;
					await RefreshNativeLayeredOverlayOnce();
				}
				while (_refreshPending);
			}
			finally { _refreshInFlight = false; }
		}

		private async System.Threading.Tasks.Task RefreshNativeLayeredOverlayOnce()
		{
			if (_overlayWindowControl == null)
			{ DragLogW("[Refresh] skip: overlayControl=null"); return; }
			if (OperatingSystem.IsWindows() && _layeredOverlay == null)
				return;
#if !WINDOWS
			if (OperatingSystem.IsMacOS() && _macLayeredOverlay == null)
			{ DragLogW("[Refresh] skip: macLayeredOverlay=null"); return; }
#endif
			try
			{
				var ow = _overlayWindowControl;
				if (ow.ActualWidth <= 0 || ow.ActualHeight <= 0)
				{ DragLogW($"[Refresh] skip: ow size=({ow.ActualWidth:F0}x{ow.ActualHeight:F0})"); return; }

				var rtb = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
				await rtb.RenderAsync(ow);
				int pw = rtb.PixelWidth, ph = rtb.PixelHeight;
				if (pw <= 0 || ph <= 0)
				{ DragLogW($"[Refresh] skip: rtb pw={pw} ph={ph}"); return; }

				var buffer = await rtb.GetPixelsAsync();
				var bytes = new byte[buffer.Length];
				using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer))
					reader.ReadBytes(bytes);
				if (bytes.Length < pw * ph * 4)
					return;

				if (OperatingSystem.IsWindows())
				{
					PremultiplyBgra(bytes);
					var origin = ComputeScreenOriginW();
					_layeredOverlay.Update(bytes, pw, ph,
						(int)Math.Round(origin.X), (int)Math.Round(origin.Y));
				}
#if !WINDOWS
				else if (OperatingSystem.IsMacOS())
				{
					ConvertPremultipliedBgraToRgba(bytes);
					var origin = ComputeScreenOriginQ();
					var scale = XamlRoot?.RasterizationScale ?? 1.0;
					if (scale <= 0) scale = 1.0;
					DragLogW($"[Refresh] update pw={pw} ph={ph} origin=({origin.X:F0},{origin.Y:F0}) areas={_overlayActiveAreas.Count} target={(_overlayActiveTarget != null)}");
					_macLayeredOverlay.Update(bytes, pw, ph, pw / scale, ph / scale, origin.X, origin.Y);
				}
#endif
			}
			catch (Exception ex) { DragLogW($"[Refresh] EXCEPTION: {ex.GetType().Name}: {ex.Message}"); }
		}

		// UpdateLayeredWindow (ULW_ALPHA) requires premultiplied BGRA.
		// Keep this pass on the Windows path only; macOS hands AppKit RGBA premul pixels.
		private static void PremultiplyBgra(byte[] bgra)
		{
			for (int i = 0; i + 3 < bgra.Length; i += 4)
			{
				byte a = bgra[i + 3];
				if (a == 255) continue;
				if (a == 0) { bgra[i] = bgra[i + 1] = bgra[i + 2] = 0; continue; }
				bgra[i]     = (byte)(bgra[i]     * a / 255);
				bgra[i + 1] = (byte)(bgra[i + 1] * a / 255);
				bgra[i + 2] = (byte)(bgra[i + 2] * a / 255);
			}
		}

#if !WINDOWS
		private static void ConvertPremultipliedBgraToRgba(byte[] bgra)
		{
			for (int i = 0; i + 3 < bgra.Length; i += 4)
			{
				var b = bgra[i];
				bgra[i] = bgra[i + 2];
				bgra[i + 2] = b;
			}
		}
#endif

		private void PositionOverlayNativeWindow()
		{
			if (OperatingSystem.IsMacOS())
			{
#if !WINDOWS
				if (_overlayVisualHost != null)
				{
					_overlayVisualHost.Width = 1;
					_overlayVisualHost.Height = 1;
					_overlayVisualHost.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 1, 1) };
					if (_overlayWindowControl != null)
					{
						_overlayWindowControl.Width = Math.Max(1, ActualWidth);
						_overlayWindowControl.Height = Math.Max(1, ActualHeight);
					}
				}
#endif
				return;
			}

			if (_overlayNativeWindow?.AppWindow == null)
				return;

			var scale = XamlRoot?.RasterizationScale ?? 1.0;
			if (scale <= 0) scale = 1.0;
			var width = Math.Max(1, (int)Math.Round(ActualWidth * scale));
			var height = Math.Max(1, (int)Math.Round(ActualHeight * scale));

			if (_overlayNativeWindow.Content is Grid root)
			{
				root.Width = Math.Max(1, ActualWidth);
				root.Height = Math.Max(1, ActualHeight);
				if (_overlayWindowControl != null)
				{
					_overlayWindowControl.Width = root.Width;
					_overlayWindowControl.Height = root.Height;
				}
			}

			try
			{
				_overlayNativeWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = width, Height = height });
				if (OperatingSystem.IsWindows())
				{
					// Park the Uno overlay window far off-screen: it is only a render+logic
					// source for RenderTargetBitmap. The visible, transparent compass is drawn
					// by the native layered window positioned at the manager origin.
					_overlayNativeWindow.AppWindow.Move(new Windows.Graphics.PointInt32 { X = -32000, Y = -32000 });
					StyleWindowsOverlayWindow();
				}
				else if (OperatingSystem.IsLinux())
				{
					// The overlay window is the visible compass surface: align it exactly
					// over the manager. ComputeScreenOriginLinux returns logical pixels;
					// AppWindow.Move expects physical.
					var (ox, oy) = ComputeScreenOriginLinux();
					_overlayNativeWindow.AppWindow.Move(new Windows.Graphics.PointInt32
					{
						X = (int)Math.Round(ox * scale),
						Y = (int)Math.Round(oy * scale),
					});
					StyleLinuxOverlayWindow();
					DragLogW($"[LinuxOverlay] positioned at ({ox * scale:F0},{oy * scale:F0}) size={width}x{height}");
				}
			}
			catch { }
		}

		private void StyleLinuxOverlayWindow()
		{
			var aw = _overlayNativeWindow?.AppWindow;
			if (aw?.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter)
				return;

			try { presenter.SetBorderAndTitleBar(false, false); }
			catch (Exception ex) { DragLogW($"[LinuxOverlay] SetBorderAndTitleBar failed: {ex.Message}"); }
			try { presenter.IsAlwaysOnTop = true; }
			catch (Exception ex) { DragLogW($"[LinuxOverlay] IsAlwaysOnTop failed: {ex.Message}"); }
			try { presenter.IsResizable = false; }
			catch { }
		}

		private void StyleWindowsOverlayWindow()
		{
			// Visual transparency on the Win32 Skia backend is not possible for an Uno-hosted
			// window (plain WS_OVERLAPPEDWINDOW rendered through an OpenGL swap chain — no
			// per-pixel-alpha path; DWM blur-behind does not take). The compass visual is
			// therefore rendered by a separate native WS_EX_LAYERED window (see
			// WindowsLayeredOverlay); this Uno window is only the off-screen render+logic
			// source, so no styling is needed here.
		}

		private const int GWL_STYLE = -16;
		private const int GWL_EXSTYLE = -20;
		private const long WS_POPUP = unchecked((long)0x80000000);
		private const long WS_CAPTION = 0x00C00000L;
		private const long WS_SYSMENU = 0x00080000L;
		private const long WS_THICKFRAME = 0x00040000L;
		private const long WS_MINIMIZEBOX = 0x00020000L;
		private const long WS_MAXIMIZEBOX = 0x00010000L;
		private const long WS_EX_LAYERED = 0x00080000L;
		private const long WS_EX_TRANSPARENT = 0x00000020L;
		private const long WS_EX_TOOLWINDOW = 0x00000080L;
		private const long WS_EX_NOACTIVATE = 0x08000000L;
		private const uint LWA_ALPHA = 0x00000002;
		private const uint SWP_NOSIZE = 0x0001;
		private const uint SWP_NOMOVE = 0x0002;
		private const uint SWP_NOZORDER = 0x0004;
		private const uint SWP_NOACTIVATE = 0x0010;
		private const uint SWP_FRAMECHANGED = 0x0020;
		private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

		private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

		[StructLayout(LayoutKind.Sequential)]
		private struct POINT32 { public int X; public int Y; }

		[StructLayout(LayoutKind.Sequential)]
		private struct RECT32 { public int Left; public int Top; public int Right; public int Bottom; }

		[DllImport("user32.dll")]
		private static extern bool ClientToScreen(IntPtr hWnd, ref POINT32 lpPoint);

		[DllImport("user32.dll")]
		private static extern bool GetWindowRect(IntPtr hWnd, out RECT32 lpRect);

		[DllImport("user32.dll")]
		private static extern IntPtr GetActiveWindow();

		[DllImport("user32.dll")]
		private static extern bool IsWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

		[DllImport("user32.dll")]
		private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

		[DllImport("user32.dll")]
		private static extern bool IsWindowVisible(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

		[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
		private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
		private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

		private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
			=> IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

		[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
		private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

		[DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
		private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

		private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
			=> IntPtr.Size == 8
				? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
				: new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

		[DllImport("user32.dll")]
		private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

		[DllImport("user32.dll")]
		private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

		private static IntPtr FindOverlayWindowForCurrentProcess()
		{
			var currentPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
			var result = IntPtr.Zero;
			EnumWindows((hwnd, _) =>
			{
				if (!IsWindowVisible(hwnd))
					return true;

				GetWindowThreadProcessId(hwnd, out var pid);
				if (pid != currentPid)
					return true;

				var sb = new StringBuilder(256);
				GetWindowText(hwnd, sb, sb.Capacity);
				if (sb.ToString() == "UnoDock Overlay")
				{
					result = hwnd;
					return false;
				}

				return true;
			}, IntPtr.Zero);
			return result;
		}

		IEnumerable<Controls.IDropArea> Controls.IOverlayWindowHost.GetDropAreas(
			Controls.LayoutFloatingWindowControl w)
		{
			if (_templateRoot == null)
				yield break;

			// WPF rule: manager outer-edge targets and anchorable-pane targets are only shown
			// when dragging an anchorable (tool window). Document drags skip them entirely.
			var isDraggingDocument = w?.Model is LayoutDocumentFloatingWindow;

			if (!isDraggingDocument)
			{
				yield return new Controls.OverlayDropArea(this, Controls.DropAreaType.DockingManager);

				foreach (var area in EnumerateVisualsOfType<Controls.LayoutAnchorablePaneControl>(_templateRoot)
					.Where(c => c.Visibility == Visibility.Visible && c.ActualWidth > 0 && c.ActualHeight > 0))
				{
					yield return new Controls.OverlayDropArea(area, Controls.DropAreaType.AnchorablePane);
				}
			}

			foreach (var area in EnumerateVisualsOfType<Controls.LayoutDocumentPaneGroupControl>(_templateRoot)
				.Where(c => c.Visibility == Visibility.Visible && c.ActualWidth > 0 && c.ActualHeight > 0))
			{
				yield return new Controls.OverlayDropArea(area, Controls.DropAreaType.DocumentPaneGroup);
			}

			foreach (var area in EnumerateVisualsOfType<Controls.LayoutDocumentPaneControl>(_templateRoot)
				.Where(c => c.Visibility == Visibility.Visible && c.ActualWidth > 0 && c.ActualHeight > 0))
			{
				yield return new Controls.OverlayDropArea(area, Controls.DropAreaType.DocumentPane);
			}
		}

		DockingManager Controls.IOverlayWindowHost.Manager => this;
	}
}
