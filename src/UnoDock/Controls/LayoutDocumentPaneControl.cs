// Forked: wires model SelectedContent ↔ SelectedItem, tab click/close, accent line.

using System;
using System.ComponentModel;
using System.Collections.Generic;
using AvalonDock.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace AvalonDock.Controls
{
	public class LayoutDocumentPaneControl : TabControlEx, ILayoutControl
	{
		private readonly LayoutDocumentPane _model;
		private ItemsControl _tabStrip;
		private FrameworkElement _tabStripHost;
		private Button _overflowButton;
		private Microsoft.UI.Xaml.Shapes.Path _overflowGlyph;
		// 2px accent line at the bottom of the tab strip. Blue when this pane holds the
		// active document; gray when no tab here is active (focus is in another pane).
		private Border _accentLine;
		private readonly HashSet<Border> _wiredTabBorders = new HashSet<Border>();
		// Mutable brush boxes: PointerEntered lambda reads [0] so the brush can be
		// updated each time tab active-state changes without re-wiring events.
		private readonly Dictionary<Button, Brush[]> _closeBtnHoverBrushes = new();

		private const string KeyCloseBtnActiveHover = "UnoDock_VS2013_DocumentWellTabButtonSelectedActiveHoveredBackground";
		private const string KeyCloseBtnInactiveHover = "UnoDock_VS2013_DocumentWellTabButtonUnselectedTabHoveredButtonHoveredBackground";
		// B = #1C97EA (active tab close hover),  C = #52B0EF (inactive tab close hover)
		private static readonly SolidColorBrush FallbackCloseBtnActiveHoverBg =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x1C, 0x97, 0xEA));
		private static readonly SolidColorBrush FallbackCloseBtnInactiveHoverBg =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x52, 0xB0, 0xEF));
		private Border _hoveredTabBorder;

		public static readonly DependencyProperty SelectedContentProperty =
			DependencyProperty.Register(nameof(SelectedContent), typeof(object),
				typeof(LayoutDocumentPaneControl), new PropertyMetadata(null));

		public object SelectedContent
		{
			get => GetValue(SelectedContentProperty);
			set => SetValue(SelectedContentProperty, value);
		}

		// Template used to render SelectedContent when it is a view-model (DocumentsSource binding)
		// rather than a UIElement. Fed from DockingManager.LayoutItemTemplate at creation time; when
		// null, the ContentPresenter shows the content directly (legacy UIElement documents).
		public static readonly DependencyProperty SelectedContentTemplateProperty =
			DependencyProperty.Register(nameof(SelectedContentTemplate), typeof(Microsoft.UI.Xaml.DataTemplate),
				typeof(LayoutDocumentPaneControl), new PropertyMetadata(null));

		public Microsoft.UI.Xaml.DataTemplate SelectedContentTemplate
		{
			get => (Microsoft.UI.Xaml.DataTemplate)GetValue(SelectedContentTemplateProperty);
			set => SetValue(SelectedContentTemplateProperty, value);
		}

		internal LayoutDocumentPaneControl(LayoutDocumentPane model, bool isVirtualizing)
			: base(isVirtualizing)
		{
			_model = model ?? throw new ArgumentNullException(nameof(model));
			DefaultStyleKey = typeof(LayoutDocumentPaneControl);
			_panePressedHandler = OnPanePointerPressed;
			ItemsSource = _model.Children;
			SyncSelection();
			_model.PropertyChanged += OnModelPropertyChanged;
			SizeChanged += OnSizeChanged;
			WireContentActiveChanged();
			if (_model.Children is System.Collections.Specialized.INotifyCollectionChanged incc)
				incc.CollectionChanged += (_, _) =>
				{
					WireContentActiveChanged();
					UpdateOverflowButtonVisibility();
				};
		}

		// Repaint tab highlights whenever ANY hosted content's active state flips. The
		// DockingManager.ActiveContent → ActiveContentChanged DP chain does not fire reliably
		// across windows, so a floating pane would otherwise stay blue after its content lost
		// active state (e.g. user clicked a main-window tab). Subscribing to each content's
		// IsActiveChanged guarantees the floating tab turns gray the moment it deactivates.
		private readonly HashSet<LayoutContent> _wiredActiveContents = new HashSet<LayoutContent>();

		private void WireContentActiveChanged()
		{
			foreach (var child in _model.Children)
			{
				if (child is LayoutContent lc && _wiredActiveContents.Add(lc))
					lc.IsActiveChanged += OnContentIsActiveChanged;
			}
		}

		private void OnContentIsActiveChanged(object sender, EventArgs e)
			=> UpdateTabHighlights(_model.SelectedContent);

		[Bindable(false)]
		public ILayoutElement Model => _model;

		// Tab drag-reorder state
		private LayoutDocument _dragTab;
		private double _dragStartX;
		private const double DragThreshold = 12.0; // logical px before reorder starts

		protected override void OnApplyTemplate()
		{
			base.OnApplyTemplate();
			_tabStripHost = GetTemplateChild("PART_TabStripHost") as FrameworkElement;
			_tabStrip = GetTemplateChild("PART_TabStrip") as ItemsControl;
			_accentLine = GetTemplateChild("BD") as Border;
			_overflowButton = GetTemplateChild("PART_OverflowButton") as Button;
			_overflowGlyph = GetTemplateChild("PART_OverflowGlyph") as Microsoft.UI.Xaml.Shapes.Path;
			ApplyThemeResources();
			if (_overflowButton != null)
			{
				_overflowButton.Click += OnOverflowButtonClick;
				_overflowButton.PointerEntered += OnOverflowButtonPointerEntered;
				_overflowButton.PointerExited  += OnOverflowButtonPointerExited;
				_overflowButton.PointerPressed += OnOverflowButtonPointerPressed;
				_overflowButton.PointerReleased += OnOverflowButtonPointerReleased;
				UpdateOverflowButtonVisibility();
			}
			if (_tabStrip != null)
			{
				_tabStrip.PointerPressed  += OnTabStripPointerPressed;
				_tabStrip.PointerMoved    += OnTabStripPointerMoved;
				_tabStrip.PointerReleased += OnTabStripPointerReleased;
				_tabStrip.PointerCaptureLost += (_, _) => _dragTab = null;
				_tabStrip.RightTapped     += OnTabStripRightTapped;
			}
			// Activate this pane's selected content on ANY pointer press within the pane —
			// tab strip OR content area. Attached here (not OnLoaded) so it is guaranteed to
			// run for the main-window pane, matching the _tabStrip wiring above. Clicking the
			// document content must focus the pane just like clicking a tab does.
			var paneKind = _model.FindParent<LayoutFloatingWindow>() != null ? "FLOAT" : "MAIN";
			DockingManager.DragLogW($"PaneHandlerAttached pane={paneKind}");
			AddHandler(PointerPressedEvent, _panePressedHandler, handledEventsToo: true);
			// Ctrl+Tab / Ctrl+Shift+Tab: hook on the visual root so we intercept before
			// WinUI/Uno's own Tab-focus cycling (which steals Ctrl+Tab for toolbar focus).
			Loaded += OnLoaded;
			Unloaded += OnUnloaded;
			// Defer initial highlight so the ItemsControl visual tree is fully populated.
			Loaded += (_, _) =>
			{
				ApplyThemeResources();
				EnsureSelectedContent();
				SyncSelection();
				UpdateTabHighlights(_model.SelectedContent);
				UpdateFloatingSinglePaneChrome();
				UpdateOverflowButtonVisibility();
			};
			UpdateFloatingSinglePaneChrome();
		}

		private KeyEventHandler _ctrlTabHandler;

		private void OnLoaded(object sender, RoutedEventArgs e)
		{
			_ctrlTabHandler = (_, ke) =>
			{
				if (ke.Key != Windows.System.VirtualKey.Tab) return;
				var window = Microsoft.UI.Xaml.Window.Current;
				if (window?.Content is not UIElement) return;
				// Check Ctrl is held by inspecting the event's KeyStatus (modifiers are
				// unavailable directly, so use XamlRoot.Content AddHandler with KeyDown).
				// We handle this at the root level to pre-empt focus cycling.
				CycleDocument(reverse: false);
				ke.Handled = true;
			};
			// Use AddHandler on XamlRoot.Content with handledEventsToo so we see the
			// event even after WinUI has already marked it handled for focus cycling.
			if (XamlRoot?.Content is UIElement root)
				root.AddHandler(KeyDownEvent, _ctrlTabHandler, handledEventsToo: true);

			// Refresh tab highlights + accent line whenever the active content changes
			// anywhere — focus moving to another pane must turn THIS pane's accent line gray
			// even though no selection change fired here.
			_manager = _model.Root?.Manager;
			if (_manager != null)
				_manager.ActiveContentChanged += OnActiveContentChanged;
		}

		private DockingManager _manager;

		private readonly Microsoft.UI.Xaml.Input.PointerEventHandler _panePressedHandler;

		private void OnPanePointerPressed(object sender, PointerRoutedEventArgs e)
		{
			var sel = _model.SelectedContent;
			var paneKind = _model.FindParent<LayoutFloatingWindow>() != null ? "FLOAT" : "MAIN";
			DockingManager.DragLogW(
				$"PanePressed pane={paneKind} sel='{sel?.Title}' selIsActive={sel?.IsActive}");
			if (sel != null && !sel.IsActive)
				sel.IsActive = true;
		}

		private void OnActiveContentChanged(object sender, EventArgs e)
			=> UpdateTabHighlights(_model.SelectedContent);

		private void OnUnloaded(object sender, RoutedEventArgs e)
		{
			if (_manager != null)
			{
				_manager.ActiveContentChanged -= OnActiveContentChanged;
				_manager = null;
			}
			RemoveHandler(PointerPressedEvent, _panePressedHandler);
			if (_ctrlTabHandler is null) return;
			if (XamlRoot?.Content is UIElement root)
				root.RemoveHandler(KeyDownEvent, _ctrlTabHandler);
			_ctrlTabHandler = null;
		}

		private void CycleDocument(bool reverse)
		{
			int count = _model.Children.Count;
			if (count < 2) return;
			int cur = _model.SelectedContentIndex;
			int next = reverse ? (cur - 1 + count) % count : (cur + 1) % count;
			_model.SelectedContentIndex = next;
		}

		// Drag-to-float: if cursor moves this many logical px below the tab strip top, float the tab.
		private const double FloatDownThreshold = 40.0;
		private double _dragStartY;

		private void OnTabStripPointerMoved(object sender, PointerRoutedEventArgs e)
		{
			if (_dragTab == null || _tabStrip == null) return;
			// CGEvent-injected PointerMoved events report IsLeftButtonPressed=false due to
			// AppKit button-state timing. Accept Mouse pointer type unconditionally — if the
			// event routed here, the button was pressed (we set _dragTab in PointerPressed).
			var pt = e.GetCurrentPoint(_tabStrip);
			var isMouse = e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse;
			if (!pt.Properties.IsLeftButtonPressed && !isMouse) { _dragTab = null; return; }
			var curX = pt.Position.X;
			var curY = pt.Position.Y;

			// ── Drag-to-float: cursor moved well below the tab strip ─────────────────
			// Tab strip height ≈ 28px. If cursor Y is FloatDownThreshold below the strip
			// top (i.e. into the content area), tear off the tab into a floating window.
			if (curY > FloatDownThreshold && _dragTab.CanFloat)
			{
				var tabToFloat = _dragTab;
				_dragTab = null; // clear before calling float (avoids re-entrancy)
				_tabStrip?.ReleasePointerCapture(e.Pointer);
				var mgr = _model.Root?.Manager;

				// Position the floating window so the cursor lands in the middle of
				// the title bar — not at the top-left (which would land on traffic lights).
				// On macOS, the actual initial screen position is chosen later from the live
				// Quartz cursor after the NSWindow exists, so only resolve the expected size here.
				double? screenLeft = null, screenTop = null;
				try
				{
					var ptInWindow = e.GetCurrentPoint(null).Position;
					if (OperatingSystem.IsWindows())
					{
						var cursor = WindowsFloatingWindowDragTracker.GetCursorScreen();
						screenLeft = cursor.X;
						screenTop = cursor.Y;
					}

					var parentSize = _model.Parent as ILayoutPositionableElementWithActualSize;
					var fwW = tabToFloat.FloatingWidth > 0
						? tabToFloat.FloatingWidth
						: (parentSize?.ActualWidth ?? 390) + 10;
					var fwH = tabToFloat.FloatingHeight > 0
						? tabToFloat.FloatingHeight
						: (parentSize?.ActualHeight ?? 290) + 10;
					tabToFloat.FloatingWidth = fwW;
					tabToFloat.FloatingHeight = fwH;
					DockingManager.DragLogW(
						$"DragToFloat prepare: ptInWindow=({ptInWindow.X:F0},{ptInWindow.Y:F0}) " +
						$"fwSize=({fwW:F0}x{fwH:F0}) nativeInitialPlacement={OperatingSystem.IsMacOS()}");
					DockingManager.DragLogW(
						$"DragToFloat: ptInWindow=({ptInWindow.X:F0},{ptInWindow.Y:F0}) " +
						$"-> screen=({screenLeft:F0},{screenTop:F0})");
				}
				catch { /* best-effort */ }

				mgr?.StartDraggingFloatingWindowForContent(tabToFloat,
					initialScreenLeft: screenLeft, initialScreenTop: screenTop);
				e.Handled = true;
				return;
			}

			if (Math.Abs(curX - _dragStartX) < DragThreshold) return;

			// Find the tab the cursor is currently over.
			var oldIdx = _model.Children.IndexOf(_dragTab);
			if (oldIdx < 0) { _dragTab = null; return; }

			// Estimate target index by finding which container's center the cursor is past.
			int newIdx = oldIdx;
			if (_tabStrip.ItemsPanelRoot != null)
			{
				var children = _tabStrip.ItemsPanelRoot.Children;
				for (int i = 0; i < children.Count && i < _model.Children.Count; i++)
				{
					if (children[i] is FrameworkElement tab)
					{
						var left = Canvas.GetLeft(tab);
						if (double.IsNaN(left))
						{
							// StackPanel — use TransformToVisual
							try
							{
								var tabCtrPt = tab.TransformToVisual(_tabStrip).TransformPoint(new Windows.Foundation.Point(tab.ActualWidth / 2, 0));
								if (curX > tabCtrPt.X) newIdx = i;
								else break;
							}
							catch { }
						}
						else
						{
							if (curX > left + tab.ActualWidth / 2) newIdx = i;
							else break;
						}
					}
				}
			}

			if (newIdx != oldIdx && newIdx >= 0 && newIdx < _model.Children.Count)
			{
				_model.Children.Move(oldIdx, newIdx);
				_dragStartX = curX;
			}
		}

		private void OnTabStripPointerReleased(object sender, PointerRoutedEventArgs e)
		{
			_dragTab = null;
			_tabStrip?.ReleasePointerCapture(e.Pointer);
		}

		// Right-tap on a tab → show "Float" flyout
		private void OnTabStripRightTapped(object sender, RightTappedRoutedEventArgs e)
		{
			var el = e.OriginalSource as FrameworkElement;
			while (el != null && el != _tabStrip)
			{
				if (el.Tag is LayoutDocument lc && lc.CanFloat)
				{
					var flyout = new MenuFlyout();
					var floatItem = new MenuFlyoutItem { Text = "Float" };
					floatItem.Click += (_, _) =>
					{
						var mgr = _model.Root?.Manager;
						mgr?.StartDraggingFloatingWindowForContent(lc);
					};
					flyout.Items.Add(floatItem);

					if (lc.CanClose)
					{
						flyout.Items.Add(new MenuFlyoutSeparator());
						var closeItem = new MenuFlyoutItem { Text = "Close" };
						closeItem.Click += (_, _) => _model.Root?.Manager?.ExecuteCloseCommand(lc);
						flyout.Items.Add(closeItem);
					}

					flyout.ShowAt(el);
					e.Handled = true;
					return;
				}
				el = el.Parent as FrameworkElement;
			}
		}

		private void OnTabStripPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			var el = e.OriginalSource as FrameworkElement;
			while (el != null && el != _tabStrip)
			{
				if (el.Tag is LayoutDocument lc)
				{
					// If user pressed the close button (element named "CloseBtn"), close.
					if (IsCloseButton(el, e.OriginalSource as FrameworkElement))
					{
						var mgr = _model.Root?.Manager;
						mgr?.ExecuteCloseCommand(lc);
						e.Handled = true;
						return;
					}
					// Start drag-reorder/float tracking.
					_dragTab = lc;
					var startPt = e.GetCurrentPoint(_tabStrip).Position;
					_dragStartX = startPt.X;
					_dragStartY = startPt.Y;
					// Capture the pointer so PointerMoved keeps firing after the cursor
					// leaves the tab strip — essential for drag-to-float (threshold 40px
					// below strip top requires tracking into the content area).
					_tabStrip.CapturePointer(e.Pointer);
					// Also select.
					var idx = _model.Children.IndexOf(lc);
					if (idx >= 0) _model.SelectedContentIndex = idx;
					// Activate explicitly: clicking the ALREADY-selected tab does not change
					// SelectedContentIndex, so SyncSelection never runs and IsActive would stay
					// false. Set it here so any tab click focuses this pane's content.
					DockingManager.DragLogW($"TabPressed title='{lc.Title}' wasActive={lc.IsActive}");
					if (!lc.IsActive) lc.IsActive = true;
					break;
				}
				el = el.Parent as FrameworkElement;
			}
		}

		private static bool IsCloseButton(FrameworkElement tabBorder, FrameworkElement pressed)
		{
			// Walk from pressed element up to (but not including) tabBorder.
			// If we encounter a FrameworkElement named "CloseBtn", it's a close click.
			var cur = pressed;
			while (cur != null && cur != tabBorder)
			{
				if (cur.Name == "CloseBtn") return true;
				cur = cur.Parent as FrameworkElement;
			}
			return false;
		}

		protected override void OnSelectionChanged(Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
		{
			base.OnSelectionChanged(e);
			if (SelectedItem is LayoutContent lc)
			{
				var idx = _model.Children.IndexOf(lc as LayoutDocument);
				if (idx >= 0 && idx != _model.SelectedContentIndex)
					_model.SelectedContentIndex = idx;
			}
		}

		private void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName is nameof(ILayoutContentSelector.SelectedContent)
				or nameof(ILayoutContentSelector.SelectedContentIndex))
				SyncSelection();
		}

		private void SyncSelection()
		{
			EnsureSelectedContent();
			var sel = _model.SelectedContent;
			if (SelectedItem != sel) SelectedItem = sel;
			SelectedContent = sel?.Content;
			UpdateFloatingSinglePaneChrome();
			// Defer highlight update one frame so the DataTemplate visual tree is ready.
			if (DispatcherQueue != null)
				DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
					() => UpdateTabHighlights(sel));
			else
				UpdateTabHighlights(sel);
			// Propagate IsActive to the model so DockingManager.ActiveContent updates.
			var paneKind = _model.FindParent<LayoutFloatingWindow>() != null ? "FLOAT" : "MAIN";
			DockingManager.DragLogW(
				$"SyncSelection pane={paneKind} sel='{sel?.Title}' selIsActive={sel?.IsActive}");
			if (sel != null && !sel.IsActive) sel.IsActive = true;
		}

		private void UpdateFloatingSinglePaneChrome()
		{
			if (_tabStripHost == null)
				return;

			_tabStripHost.Visibility = ShouldHideFloatingSinglePaneChrome()
				? Visibility.Collapsed
				: Visibility.Visible;
		}

		private bool ShouldHideFloatingSinglePaneChrome()
			=> false;

		private void EnsureSelectedContent()
		{
			if (_model.SelectedContent != null || _model.Children.Count == 0)
				return;

			for (var index = 0; index < _model.Children.Count; index++)
			{
				if (_model.Children[index].IsEnabled)
				{
					_model.SelectedContentIndex = index;
					return;
				}
			}
		}

		private const string KeyTabBarBackground = "UnoDock_VS2013_TabBarBackground";
		private static readonly SolidColorBrush FallbackTabBarBackground =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x2D, 0x2D, 0x30));
		private const string KeyDocSelActiveBg = "UnoDock_VS2013_DocumentWellTabSelectedActiveBackground";
		private const string KeyDocSelActiveText = "UnoDock_VS2013_DocumentWellTabSelectedActiveText";
		private const string KeyDocSelInactiveBg = "UnoDock_VS2013_DocumentWellTabSelectedInactiveBackground";
		private const string KeyDocSelInactiveText = "UnoDock_VS2013_DocumentWellTabSelectedInactiveText";
		private const string KeyDocUnselectedBg = "UnoDock_VS2013_DocumentWellTabUnselectedBackground";
		private const string KeyDocUnselectedText = "UnoDock_VS2013_DocumentWellTabUnselectedText";
		private const string KeyDocUnselectedHoverBg = "UnoDock_VS2013_DocumentWellTabUnselectedHoveredBackground";
		private const string KeyDocUnselectedHoverText = "UnoDock_VS2013_DocumentWellTabUnselectedHoveredText";
		private const string KeyDocCloseGlyphActive = "UnoDock_VS2013_DocumentWellTabButtonSelectedActiveGlyph";
		private const string KeyDocCloseGlyphInactive = "UnoDock_VS2013_DocumentWellTabButtonSelectedInactiveGlyph";
		private const string KeyDocCloseGlyphHover = "UnoDock_VS2013_DocumentWellTabButtonUnselectedTabHoveredGlyph";

		private static readonly SolidColorBrush FallbackActiveTabBg =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x00, 0x7A, 0xCC));
		private static readonly SolidColorBrush FallbackInactiveTabBg =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x3F, 0x3F, 0x46));
		private static readonly SolidColorBrush FallbackUnselectedBg =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x2D, 0x2D, 0x30));
		private static readonly SolidColorBrush FallbackUnselectedHoverBg =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x1C, 0x97, 0xEA));
		private static readonly SolidColorBrush FallbackActiveText =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
		private static readonly SolidColorBrush FallbackInactiveText =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xF1, 0xF1, 0xF1));
		private static readonly SolidColorBrush FallbackUnselectedText =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xF1, 0xF1, 0xF1));
		private static readonly SolidColorBrush FallbackAccentBlue =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x00, 0x7A, 0xCC));
		private static readonly SolidColorBrush FallbackCloseHoverGlyph =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xD0, 0xE6, 0xF5));

		private void ApplyThemeResources()
		{
			var tabBarBackground = ResolveBrush(KeyTabBarBackground, FallbackTabBarBackground);
			switch (_tabStripHost)
			{
				case Border border:
					border.Background = tabBarBackground;
					break;
				case Panel panel:
					panel.Background = tabBarBackground;
					break;
				case Control control:
					control.Background = tabBarBackground;
					break;
			}
		}

		private Brush ResolveBrush(string key, Brush fallback)
		{
			if (Resources != null && Resources.TryGetValue(key, out var local) && local is Brush localBrush)
				return localBrush;

			DependencyObject current = this;
			while (current != null)
			{
				if (current is FrameworkElement fe
					&& fe.Resources != null
					&& fe.Resources.TryGetValue(key, out var scoped)
					&& scoped is Brush scopedBrush)
					return scopedBrush;

				current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
			}

			var appResources = Application.Current?.Resources;
			if (appResources != null && appResources.TryGetValue(key, out var app) && app is Brush appBrush)
				return appBrush;

			return fallback;
		}

		// Find the first Border descendant of a dependency object (one level deep via ContentPresenter).
		private static Border FindTabBorder(DependencyObject container)
		{
			if (container is Border b) return b;
			// ItemsControl wraps each item in a ContentPresenter; the Border is its content.
			if (container is ContentPresenter cp)
				return cp.Content as Border ?? FindInVisualChildren(cp);
			return FindInVisualChildren(container);
		}

		private static Border FindInVisualChildren(DependencyObject node)
		{
			int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
			for (int i = 0; i < count; i++)
			{
				var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i);
				if (child is Border b) return b;
				var found = FindInVisualChildren(child);
				if (found != null) return found;
			}
			return null;
		}

		private void UpdateTabHighlights(LayoutContent active)
		{
			if (_tabStrip?.ItemsPanelRoot == null) return;
			var selActiveBg = ResolveBrush(KeyDocSelActiveBg, FallbackActiveTabBg);
			var selActiveText = ResolveBrush(KeyDocSelActiveText, FallbackActiveText);
			var selInactiveBg = ResolveBrush(KeyDocSelInactiveBg, FallbackInactiveTabBg);
			var selInactiveText = ResolveBrush(KeyDocSelInactiveText, FallbackInactiveText);
			var unselectedBg = ResolveBrush(KeyDocUnselectedBg, FallbackUnselectedBg);
			var unselectedText = ResolveBrush(KeyDocUnselectedText, FallbackUnselectedText);
			var unselectedHoverBg = ResolveBrush(KeyDocUnselectedHoverBg, FallbackUnselectedHoverBg);
			var unselectedHoverText = ResolveBrush(KeyDocUnselectedHoverText, FallbackActiveText);
			var closeGlyphActive = ResolveBrush(KeyDocCloseGlyphActive, FallbackActiveText);
			var closeGlyphInactive = ResolveBrush(KeyDocCloseGlyphInactive, FallbackInactiveText);
			var closeGlyphHover = ResolveBrush(KeyDocCloseGlyphHover, FallbackCloseHoverGlyph);

			bool paneHasActiveTab = false;

			foreach (var container in _tabStrip.ItemsPanelRoot.Children)
			{
				var tabOuter = FindTabBorder(container);
				if (tabOuter is null) continue;
				WireTabHoverRefresh(tabOuter);
				bool isHovered = ReferenceEquals(_hoveredTabBorder, tabOuter);
				bool isSelected = tabOuter.Tag is LayoutDocument docSel && docSel.IsSelected;
				bool isActiveDoc = tabOuter.Tag is LayoutDocument docAct && docAct.IsActive;
				// A floating-window pane shows its active highlight when its window is focused
				// (IsActive tracks window focus via LayoutFloatingWindowControl.OnWindowActivated),
				// so it must NOT be suppressed here — only genuine focus loss turns it gray.
				var isActive = isSelected && isActiveDoc;
				if (isActive) paneHasActiveTab = true;
				if (isSelected)
					tabOuter.Background = isActive ? selActiveBg : selInactiveBg;
				else
					tabOuter.Background = isHovered ? unselectedHoverBg : unselectedBg;

				var tabText = FindTabTitleText(tabOuter);
				if (tabText != null)
				{
					if (isSelected)
						tabText.Foreground = isActive ? selActiveText : selInactiveText;
					else
						tabText.Foreground = isHovered ? unselectedHoverText : unselectedText;
				}

				SetCloseGlyph(tabOuter, isSelected ? (isActive ? closeGlyphActive : closeGlyphInactive) : closeGlyphHover);
				UpdateCloseButtonVisual(tabOuter, isActive);
			}

			// Bottom accent line: blue (active-tab color) only when this pane owns the
			// active document; otherwise gray (inactive selected-tab color), matching VS2013.
			if (_accentLine != null)
				_accentLine.BorderBrush = paneHasActiveTab
					? selActiveBg
					: ResolveBrush(KeyDocSelInactiveBg, FallbackInactiveTabBg);
		}

		private void WireTabHoverRefresh(Border tabOuter)
		{
			if (!_wiredTabBorders.Add(tabOuter))
				return;

			tabOuter.PointerEntered += (_, _) =>
			{
				_hoveredTabBorder = tabOuter;
				UpdateTabHighlights(_model.SelectedContent);
			};
			tabOuter.PointerExited += (_, _) =>
			{
				if (ReferenceEquals(_hoveredTabBorder, tabOuter))
					_hoveredTabBorder = null;
				UpdateTabHighlights(_model.SelectedContent);
			};
		}

		private static Button FindCloseButton(Border tabOuter)
		{
			var sp = tabOuter?.Child as StackPanel;
			if (sp == null) return null;

			for (int j = 0; j < sp.Children.Count; j++)
				if (sp.Children[j] is Button btn && btn.Name == "CloseBtn")
					return btn;

			return null;
		}

		private static TextBlock FindTabTitleText(Border tabOuter)
		{
			var sp = tabOuter?.Child as StackPanel;
			if (sp == null) return null;

			for (int j = 0; j < sp.Children.Count; j++)
				if (sp.Children[j] is TextBlock tb)
					return tb;

			return null;
		}

		private static void SetCloseGlyph(Border tabOuter, Brush brush)
		{
			var closeBtn = FindCloseButton(tabOuter);
			if (closeBtn?.Content is not TextBlock glyph)
				return;

			glyph.Foreground = brush;
		}

		private void UpdateCloseButtonVisual(Border tabOuter, bool isActive)
		{
			if (tabOuter?.Tag is not LayoutDocument doc)
				return;

			var closeBtn = FindCloseButton(tabOuter);
			if (closeBtn == null)
				return;

			if (!doc.CanClose)
			{
				// No close button at all — collapse so it takes no layout space.
				closeBtn.Visibility = Visibility.Collapsed;
				return;
			}

			// WinUI has no Visibility.Hidden. Use Opacity=0 + IsHitTestVisible=False to
			// preserve layout space (matching WPF Hidden), keeping tab width stable.
			bool show = isActive || ReferenceEquals(_hoveredTabBorder, tabOuter);
			closeBtn.Visibility     = Visibility.Visible;
			closeBtn.Opacity        = show ? 1.0 : 0.0;
			closeBtn.IsHitTestVisible = show;

			// Close button hover colors (rules: active→B=#1C97EA, inactive→C=#52B0EF).
			// Mutable box lets us update the brush each call without re-wiring.
			var hoverBg = isActive
				? ResolveBrush(KeyCloseBtnActiveHover,   FallbackCloseBtnActiveHoverBg)
				: ResolveBrush(KeyCloseBtnInactiveHover, FallbackCloseBtnInactiveHoverBg);

			if (!_closeBtnHoverBrushes.ContainsKey(closeBtn))
			{
				var box = new Brush[] { hoverBg };
				_closeBtnHoverBrushes[closeBtn] = box;
				var transparent = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
				closeBtn.PointerEntered += (_, _) => closeBtn.Background = box[0];
				closeBtn.PointerExited  += (_, _) => closeBtn.Background = transparent;
			}
			else
			{
				_closeBtnHoverBrushes[closeBtn][0] = hoverBg;
			}
		}

		// ── Overflow button ───────────────────────────────────────────────────

		private const string KeyOverflowDefaultGlyph     = "UnoDock_VS2013_DocumentWellOverflowButtonDefaultGlyph";
		private const string KeyOverflowHoveredBg        = "UnoDock_VS2013_DocumentWellOverflowButtonHoveredBackground";
		private const string KeyOverflowHoveredBorder    = "UnoDock_VS2013_DocumentWellOverflowButtonHoveredBorder";
		private const string KeyOverflowHoveredGlyph     = "UnoDock_VS2013_DocumentWellOverflowButtonHoveredGlyph";
		private const string KeyOverflowPressedBg        = "UnoDock_VS2013_DocumentWellOverflowButtonPressedBackground";
		private const string KeyOverflowPressedGlyph     = "UnoDock_VS2013_DocumentWellOverflowButtonPressedGlyph";

		private static readonly SolidColorBrush FallbackOverflowGlyph =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xCE, 0xD4, 0xDD));
		private static readonly SolidColorBrush FallbackOverflowHoverBg =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFC, 0xF4));
		private static readonly SolidColorBrush FallbackOverflowPressedBg =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xE8, 0xA6));
		private static readonly SolidColorBrush Transparent =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

		private void UpdateOverflowButtonVisibility()
		{
			if (_overflowButton == null) return;
			_overflowButton.Visibility = _model.Children.Count > 0
				? Visibility.Visible
				: Visibility.Collapsed;
		}

		private void OnOverflowButtonClick(object sender, RoutedEventArgs e)
		{
			if (_overflowButton == null) return;
			var flyout = new MenuFlyout();
			foreach (var child in _model.Children)
			{
				var doc = child;
				var item = new MenuFlyoutItem { Text = doc.Title ?? string.Empty };
				item.Click += (_, _) =>
				{
					var idx = _model.Children.IndexOf(doc);
					if (idx >= 0) _model.SelectedContentIndex = idx;
				};
				flyout.Items.Add(item);
			}
			flyout.ShowAt(_overflowButton);
		}

		private void OnOverflowButtonPointerEntered(object sender, PointerRoutedEventArgs e)
		{
			if (_overflowButton == null) return;
			_overflowButton.Background = ResolveBrush(KeyOverflowHoveredBg, FallbackOverflowHoverBg);
			if (_overflowGlyph != null)
				_overflowGlyph.Fill = ResolveBrush(KeyOverflowHoveredGlyph, FallbackOverflowGlyph);
		}

		private void OnOverflowButtonPointerExited(object sender, PointerRoutedEventArgs e)
		{
			if (_overflowButton == null) return;
			_overflowButton.Background = Transparent;
			if (_overflowGlyph != null)
				_overflowGlyph.Fill = ResolveBrush(KeyOverflowDefaultGlyph, FallbackOverflowGlyph);
		}

		private void OnOverflowButtonPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			if (_overflowButton == null) return;
			_overflowButton.Background = ResolveBrush(KeyOverflowPressedBg, FallbackOverflowPressedBg);
			if (_overflowGlyph != null)
				_overflowGlyph.Fill = ResolveBrush(KeyOverflowPressedGlyph, FallbackOverflowGlyph);
		}

		private void OnOverflowButtonPointerReleased(object sender, PointerRoutedEventArgs e)
		{
			// Revert to hovered state (pointer is still over the button after release).
			OnOverflowButtonPointerEntered(sender, e);
		}

		private void OnSizeChanged(object sender, SizeChangedEventArgs e)
		{
			(_model as ILayoutPositionableElementWithActualSize).ActualWidth = ActualWidth;
			(_model as ILayoutPositionableElementWithActualSize).ActualHeight = ActualHeight;
		}
	}
}
