// Forked: wires model SelectedContent ↔ SelectedItem, tab click, accent line.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using AvalonDock;
using AvalonDock.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace AvalonDock.Controls
{
	public class LayoutAnchorablePaneControl : TabControlEx, ILayoutControl
	{
		private readonly LayoutAnchorablePane _model;
		private ItemsControl _tabStrip;
		private FrameworkElement _tabStripHost;
		private readonly HashSet<Border> _wiredTabBorders = new HashSet<Border>();
		private Border _hoveredTabBorder;

		private Border _headerBorder;
		private TextBlock _headerTitle;
		private Button _headerMenuButton;
		private Button _headerAutoHideButton;
		private Button _headerCloseButton;
		private Path _headerGrip;
		private int _headerGripTileCount = -1;

		private enum HeaderButtonState
		{
			Normal,
			Hover,
			Pressed,
		}

		// Drag-to-float state (mirrors LayoutDocumentPaneControl)
		private LayoutAnchorable _dragTab;
		private double _dragStartX;
		private double _dragStartY;
		private const double FloatDownThreshold = 40.0;
		private const double FloatDragThreshold = 24.0;

		public static readonly DependencyProperty SelectedContentProperty =
			DependencyProperty.Register(nameof(SelectedContent), typeof(object),
				typeof(LayoutAnchorablePaneControl), new PropertyMetadata(null));

		public object SelectedContent
		{
			get => GetValue(SelectedContentProperty);
			set => SetValue(SelectedContentProperty, value);
		}

		internal LayoutAnchorablePaneControl(LayoutAnchorablePane model, bool isVirtualizing)
			: base(isVirtualizing)
		{
			_model = model ?? throw new ArgumentNullException(nameof(model));
			DefaultStyleKey = typeof(LayoutAnchorablePaneControl);
			ItemsSource = _model.Children;
			SyncSelection();
			_model.PropertyChanged += OnModelPropertyChanged;
			SizeChanged += OnSizeChanged;
			// Log every IsSelected/IsActive change on each child so we can
			// trace whether they change after UpdateTabHighlights runs.
			foreach (var child in _model.Children)
				child.PropertyChanged += OnChildPropertyChanged;
			_model.Children.CollectionChanged += (_, ce) =>
			{
				if (ce.NewItems != null)
					foreach (LayoutAnchorable c in ce.NewItems)
						c.PropertyChanged += OnChildPropertyChanged;
			};
		}

		private void OnChildPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName is nameof(LayoutContent.IsSelected) or nameof(LayoutContent.IsActive))
				DockLog.Write($"[ChildProp] {(sender as LayoutContent)?.Title} .{e.PropertyName}=" +
					(e.PropertyName == nameof(LayoutContent.IsSelected)
						? $"{(sender as LayoutContent)?.IsSelected}"
						: $"{(sender as LayoutContent)?.IsActive}"));
		}

		[Bindable(false)]
		public ILayoutElement Model => _model;

		protected override void OnApplyTemplate()
		{
			base.OnApplyTemplate();
			_headerBorder = GetTemplateChild("PART_HeaderBorder") as Border;
			_headerTitle = GetTemplateChild("PART_HeaderTitle") as TextBlock;
			_headerGrip = GetTemplateChild("PART_HeaderGrip") as Path;
			if (_headerGrip != null)
				_headerGrip.SizeChanged += OnHeaderGripSizeChanged;
			_headerMenuButton = GetTemplateChild("PART_HeaderMenu") as Button;
			_headerAutoHideButton = GetTemplateChild("PART_HeaderAutoHide") as Button;
			_headerCloseButton = GetTemplateChild("PART_HeaderClose") as Button;
			_tabStripHost = GetTemplateChild("PART_TabStripHost") as FrameworkElement;

			if (_headerMenuButton != null)
				_headerMenuButton.Click += OnHeaderMenuClick;
			if (_headerAutoHideButton != null)
				_headerAutoHideButton.Click += OnHeaderAutoHideClick;
			if (_headerCloseButton != null)
				_headerCloseButton.Click += OnHeaderCloseClick;

			WireHeaderButtonVisualState(_headerMenuButton);
			WireHeaderButtonVisualState(_headerAutoHideButton);
			WireHeaderButtonVisualState(_headerCloseButton);

			_tabStrip = GetTemplateChild("PART_TabStrip") as ItemsControl;
			if (_tabStrip != null)
			{
				_tabStrip.PointerPressed  += OnTabStripPointerPressed;
				_tabStrip.PointerMoved    += OnTabStripPointerMoved;
				_tabStrip.PointerReleased += OnTabStripPointerReleased;
				_tabStrip.PointerCaptureLost += (_, _) => _dragTab = null;
				_tabStrip.RightTapped     += OnTabStripRightTapped;
			}
			Loaded += (_, _) =>
			{
				EnsureSelectedContent();
				SyncSelection();
				UpdateTabHighlights(_model.SelectedContent);
				UpdateHeaderChrome(_model.SelectedContent as LayoutAnchorable);
				UpdateFloatingSinglePaneChrome();
			};
			UpdateFloatingSinglePaneChrome();
		}

		// WinUI/Uno have no tiling brush, so the header drag-grip dots (WPF VS
		// DragHandleTexture, a 4×4 DrawingBrush tile) are regenerated here to
		// span whatever width the header grip column currently has.
		private void OnHeaderGripSizeChanged(object sender, SizeChangedEventArgs e)
		{
			var width = e.NewSize.Width;
			var tiles = Math.Max(0, (int)(width / 4.0));
			if (tiles == _headerGripTileCount) return;
			_headerGripTileCount = tiles;

			var dots = new GeometryGroup();
			for (var x = 0.0; x + 1 <= width; x += 4)
				dots.Children.Add(new RectangleGeometry { Rect = new Windows.Foundation.Rect(x, 0, 1, 1) });
			for (var x = 2.0; x + 1 <= width; x += 4)
				dots.Children.Add(new RectangleGeometry { Rect = new Windows.Foundation.Rect(x, 2, 1, 1) });
			_headerGrip.Data = dots;
		}

		private LayoutAnchorable GetSelectedAnchorable() => _model.SelectedContent as LayoutAnchorable;

		private MenuFlyout BuildAnchorableMenu(LayoutAnchorable la)
		{
			var flyout = new MenuFlyout();

			var autoHideText = la.IsAutoHidden ? "Dock" : "Auto Hide";
			var autoHideItem = new MenuFlyoutItem { Text = autoHideText };
			autoHideItem.Click += (_, _) => la.ToggleAutoHide();
			flyout.Items.Add(autoHideItem);

			if (la.CanFloat)
			{
				var floatItem = new MenuFlyoutItem { Text = "Float" };
				floatItem.Click += (_, _) =>
					_model.Root?.Manager?.StartDraggingFloatingWindowForContent(la);
				flyout.Items.Add(floatItem);
			}

			flyout.Items.Add(new MenuFlyoutSeparator());

			if (la.CanHide)
			{
				var hideItem = new MenuFlyoutItem { Text = "Hide" };
				hideItem.Click += (_, _) => la.Hide();
				flyout.Items.Add(hideItem);
			}

			return flyout;
		}

		private void OnHeaderMenuClick(object sender, RoutedEventArgs e)
		{
			var selected = GetSelectedAnchorable();
			if (selected == null || sender is not FrameworkElement fe)
				return;

			BuildAnchorableMenu(selected).ShowAt(fe);
		}

		private void OnHeaderAutoHideClick(object sender, RoutedEventArgs e)
		{
			var selected = GetSelectedAnchorable();
			if (selected?.CanAutoHide == true)
				selected.ToggleAutoHide();
		}

		private void OnHeaderCloseClick(object sender, RoutedEventArgs e)
		{
			var selected = GetSelectedAnchorable();
			if (selected == null)
				return;

			if (selected.CanClose)
				_model.Root?.Manager?.ExecuteCloseCommand(selected);
			else if (selected.CanHide)
				_model.Root?.Manager?.ExecuteHideCommand(selected);
		}

		private void OnTabStripRightTapped(object sender, RightTappedRoutedEventArgs e)
		{
			var el = e.OriginalSource as FrameworkElement;
			while (el != null && el != _tabStrip)
			{
				if (el.Tag is LayoutAnchorable la)
				{
					BuildAnchorableMenu(la).ShowAt(el);
					e.Handled = true;
					return;
				}
				el = el.Parent as FrameworkElement;
			}
		}

		private void OnTabStripPointerMoved(object sender, PointerRoutedEventArgs e)
		{
			if (_dragTab == null || _tabStrip == null) return;
			var pt = e.GetCurrentPoint(_tabStrip);
			var isMouse = e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse;
			if (!pt.Properties.IsLeftButtonPressed && !isMouse) { _dragTab = null; return; }

			var curX = pt.Position.X;
			var curY = pt.Position.Y;
			var dx = Math.Abs(curX - _dragStartX);
			var dy = Math.Abs(curY - _dragStartY);
			var shouldFloat = OperatingSystem.IsWindows()
				? dx >= FloatDragThreshold || dy >= FloatDragThreshold
				: curY > FloatDownThreshold;
			if (!shouldFloat) return;
			if (!_dragTab.CanFloat) { _dragTab = null; return; }

			var tabToFloat = _dragTab;
			_dragTab = null;
			_tabStrip.ReleasePointerCapture(e.Pointer);

			var mgr = _model.Root?.Manager;
			// Size the floating window to approximately the current pane size
			if (tabToFloat.FloatingWidth <= 0)
				tabToFloat.FloatingWidth = (_model as ILayoutPositionableElementWithActualSize)?.ActualWidth ?? 390;
			if (tabToFloat.FloatingHeight <= 0)
				tabToFloat.FloatingHeight = (_model as ILayoutPositionableElementWithActualSize)?.ActualHeight ?? 290;

			double? screenLeft = null, screenTop = null;
			if (OperatingSystem.IsWindows())
			{
				var cursor = WindowsFloatingWindowDragTracker.GetCursorScreen();
				screenLeft = cursor.X;
				screenTop = cursor.Y;
			}

			mgr?.StartDraggingFloatingWindowForContent(tabToFloat,
				initialScreenLeft: screenLeft, initialScreenTop: screenTop);
			e.Handled = true;
		}

		private void OnTabStripPointerReleased(object sender, PointerRoutedEventArgs e)
		{
			_dragTab = null;
			_tabStrip?.ReleasePointerCapture(e.Pointer);
		}

		private void OnTabStripPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			var el = e.OriginalSource as FrameworkElement;
			while (el != null && el != _tabStrip)
			{
				if (el.Tag is LayoutAnchorable la)
				{
					var idx = _model.Children.IndexOf(la);
					if (idx >= 0) _model.SelectedContentIndex = idx;
					// Start tracking for drag-to-float
					_dragTab = la;
					var startPt = e.GetCurrentPoint(_tabStrip).Position;
					_dragStartX = startPt.X;
					_dragStartY = startPt.Y;
					_tabStrip?.CapturePointer(e.Pointer);
					break;
				}
				el = el.Parent as FrameworkElement;
			}
		}

		protected override void OnSelectionChanged(Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
		{
			base.OnSelectionChanged(e);
			if (SelectedItem is LayoutAnchorable la)
			{
				var idx = _model.Children.IndexOf(la);
				if (idx >= 0 && idx != _model.SelectedContentIndex)
					_model.SelectedContentIndex = idx;
			}
		}

		private void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			DockLog.Write($"[AnchorablePane] PropChanged={e.PropertyName}  " +
				$"children=[{string.Join(",", _model.Children.Select(c => $"{c.Title}(sel={c.IsSelected},act={c.IsActive})"))}]");
			if (e.PropertyName is nameof(ILayoutContentSelector.SelectedContent)
				or nameof(ILayoutContentSelector.SelectedContentIndex))
				SyncSelection();
		}

		private void SyncSelection()
		{
			EnsureSelectedContent();
			var sel = _model.SelectedContent;
			DockLog.Write($"[SyncSelection] sel={sel?.Title}  " +
				$"children=[{string.Join(",", _model.Children.Select(c => $"{c.Title}(sel={c.IsSelected},act={c.IsActive}"))}]");
			if (SelectedItem != sel) SelectedItem = sel;
			SelectedContent = sel?.Content;
			UpdateHeaderChrome(sel as LayoutAnchorable);
			UpdateFloatingSinglePaneChrome();
			if (DispatcherQueue != null)
				DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
					() => UpdateTabHighlights(sel));
			else
				UpdateTabHighlights(sel);
		}

		private void UpdateFloatingSinglePaneChrome()
		{
			var hide = _model.IsDirectlyHostedInFloatingWindow && _model.Children.Count == 1;
			var visibility = hide ? Visibility.Collapsed : Visibility.Visible;

			if (_headerBorder != null)
				_headerBorder.Visibility = visibility;
			if (_tabStripHost != null)
				_tabStripHost.Visibility = visibility;
		}

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

		private void WireHeaderButtonVisualState(Button button)
		{
			if (button == null)
				return;

			button.PointerEntered += (_, _) =>
				ApplyHeaderButtonVisual(button, GetSelectedAnchorable(), HeaderButtonState.Hover);
			button.PointerExited += (_, _) =>
				ApplyHeaderButtonVisual(button, GetSelectedAnchorable(), HeaderButtonState.Normal);
			button.PointerPressed += (_, _) =>
				ApplyHeaderButtonVisual(button, GetSelectedAnchorable(), HeaderButtonState.Pressed);
			button.PointerReleased += (_, _) =>
				ApplyHeaderButtonVisual(button, GetSelectedAnchorable(), HeaderButtonState.Hover);
		}

		private void UpdateHeaderChrome(LayoutAnchorable selected)
		{
			var isActive = selected?.IsActive == true;
			var captionBg = ResolveBrush(
				isActive ? "UnoDock_VS2013_ToolWindowCaptionActiveBackground" : "UnoDock_VS2013_ToolWindowCaptionInactiveBackground",
				FallbackActiveTabBg);
			var captionText = ResolveBrush(
				isActive ? "UnoDock_VS2013_ToolWindowCaptionActiveText" : "UnoDock_VS2013_ToolWindowCaptionInactiveText",
				FallbackActiveTabText);

			if (_headerBorder != null)
				_headerBorder.Background = captionBg;

			if (_headerTitle != null)
			{
				_headerTitle.Text = selected?.Title
					?? _model.SelectedContent?.Title
					?? _model.Children.FirstOrDefault(c => c.IsEnabled)?.Title
					?? "Tool Window";
				_headerTitle.Foreground = captionText;
			}

			if (_headerMenuButton != null)
				_headerMenuButton.IsEnabled = selected != null;

			if (_headerAutoHideButton != null)
				_headerAutoHideButton.IsEnabled = selected?.CanAutoHide == true;

			if (_headerCloseButton != null)
				_headerCloseButton.IsEnabled = selected?.CanClose == true || selected?.CanHide == true;

			ApplyHeaderButtonVisual(_headerMenuButton, selected, HeaderButtonState.Normal);
			ApplyHeaderButtonVisual(_headerAutoHideButton, selected, HeaderButtonState.Normal);
			ApplyHeaderButtonVisual(_headerCloseButton, selected, HeaderButtonState.Normal);
		}

		private void ApplyHeaderButtonVisual(Button button, LayoutAnchorable selected, HeaderButtonState state)
		{
			if (button == null)
				return;

			var isActive = selected?.IsActive == true;
			var bgKey = isActive
				? (state == HeaderButtonState.Pressed
					? "UnoDock_VS2013_ToolWindowCaptionButtonActivePressedBackground"
					: state == HeaderButtonState.Hover
						? "UnoDock_VS2013_ToolWindowCaptionButtonActiveHoveredBackground"
						: null)
				: (state == HeaderButtonState.Pressed
					? "UnoDock_VS2013_ToolWindowCaptionButtonInactivePressedBackground"
					: state == HeaderButtonState.Hover
						? "UnoDock_VS2013_ToolWindowCaptionButtonInactiveHoveredBackground"
						: null);

			var borderKey = isActive
				? (state == HeaderButtonState.Pressed
					? "UnoDock_VS2013_ToolWindowCaptionButtonActivePressedBorder"
					: state == HeaderButtonState.Hover
						? "UnoDock_VS2013_ToolWindowCaptionButtonActiveHoveredBorder"
						: null)
				: (state == HeaderButtonState.Pressed
					? "UnoDock_VS2013_ToolWindowCaptionButtonInactivePressedBorder"
					: state == HeaderButtonState.Hover
						? "UnoDock_VS2013_ToolWindowCaptionButtonInactiveHoveredBorder"
						: null);

			var glyphKey = isActive
				? (state == HeaderButtonState.Pressed
					? "UnoDock_VS2013_ToolWindowCaptionButtonActivePressedGlyph"
					: state == HeaderButtonState.Hover
						? "UnoDock_VS2013_ToolWindowCaptionButtonActiveHoveredGlyph"
						: "UnoDock_VS2013_ToolWindowCaptionButtonActiveGlyph")
				: (state == HeaderButtonState.Pressed
					? "UnoDock_VS2013_ToolWindowCaptionButtonInactivePressedGlyph"
					: state == HeaderButtonState.Hover
						? "UnoDock_VS2013_ToolWindowCaptionButtonInactiveHoveredGlyph"
						: "UnoDock_VS2013_ToolWindowCaptionButtonInactiveGlyph");

			button.Background = bgKey != null ? ResolveBrush(bgKey, TransparentBrush) : TransparentBrush;
			button.BorderBrush = borderKey != null ? ResolveBrush(borderKey, TransparentBrush) : TransparentBrush;
			if (button.Content is TextBlock tb)
				tb.Foreground = ResolveBrush(glyphKey, FallbackActiveTabText);
			else if (button.Content is Path path)
				path.Fill = ResolveBrush(glyphKey, FallbackActiveTabText);
		}

		private const string KeyToolTabSelectedActiveBackground = "UnoDock_VS2013_ToolWindowTabSelectedActiveBackground";
		private const string KeyToolTabSelectedActiveText = "UnoDock_VS2013_ToolWindowTabSelectedActiveText";
		private const string KeyToolTabSelectedInactiveBackground = "UnoDock_VS2013_ToolWindowTabSelectedInactiveBackground";
		private const string KeyToolTabSelectedInactiveText = "UnoDock_VS2013_ToolWindowTabSelectedInactiveText";
		private const string KeyToolTabUnselectedBackground = "UnoDock_VS2013_ToolWindowTabUnselectedBackground";
		private const string KeyToolTabUnselectedText = "UnoDock_VS2013_ToolWindowTabUnselectedText";
		private const string KeyToolTabUnselectedHoveredBackground = "UnoDock_VS2013_ToolWindowTabUnselectedHoveredBackground";
		private const string KeyToolTabUnselectedHoveredText = "UnoDock_VS2013_ToolWindowTabUnselectedHoveredText";

		private static readonly SolidColorBrush FallbackActiveTabBg =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x25, 0x25, 0x26));
		private static readonly SolidColorBrush FallbackInactiveTabBg =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x2D, 0x2D, 0x30));
		private static readonly SolidColorBrush FallbackActiveTabText =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x00, 0x97, 0xFB));
		private static readonly SolidColorBrush FallbackInactiveTabText =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xD0, 0xD0, 0xD0));
		private static readonly SolidColorBrush FallbackHoverTabBg =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x3E, 0x3E, 0x40));
		private static readonly SolidColorBrush FallbackHoverTabText =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x55, 0xAA, 0xFF));
		private static readonly SolidColorBrush TransparentBrush =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0x00, 0x00, 0x00, 0x00));

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

		// Find the tab's outer Border — the one with Tag set to the LayoutAnchorable.
		// The DataTemplate root is now Grid{SelectedBD (no Tag), TabOuter (has Tag)},
		// so we must skip Borders without a Tag.
		private static Border FindTabBorder(DependencyObject container)
		{
			if (container is Border b && b.Tag != null) return b;
			if (container is ContentPresenter cp)
				return FindInVisualChildren(cp);
			return FindInVisualChildren(container);
		}

		private static Border FindInVisualChildren(DependencyObject node)
		{
			int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
			for (int i = 0; i < count; i++)
			{
				var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i);
				if (child is Border b && b.Tag != null) return b;
				var found = FindInVisualChildren(child);
				if (found != null) return found;
			}
			return null;
		}

		private void UpdateTabHighlights(LayoutContent active)
		{
			if (_tabStrip?.ItemsPanelRoot == null)
			{
				DockLog.Write($"[UpdateTabHighlights] EARLY EXIT — ItemsPanelRoot null  active={active?.Title}");
				return;
			}
			DockLog.Write($"[UpdateTabHighlights] START  active={active?.Title}  items={_tabStrip.ItemsPanelRoot.Children.Count}");
			var selectedActiveBg = ResolveBrush(KeyToolTabSelectedActiveBackground, FallbackActiveTabBg);
			var selectedActiveText = ResolveBrush(KeyToolTabSelectedActiveText, FallbackActiveTabText);
			var selectedInactiveBg = ResolveBrush(KeyToolTabSelectedInactiveBackground, FallbackActiveTabBg);
			var selectedInactiveText = ResolveBrush(KeyToolTabSelectedInactiveText, FallbackActiveTabText);
			var unselectedBg = ResolveBrush(KeyToolTabUnselectedBackground, FallbackInactiveTabBg);
			var unselectedText = ResolveBrush(KeyToolTabUnselectedText, FallbackInactiveTabText);
			var hoverBg = ResolveBrush(KeyToolTabUnselectedHoveredBackground, FallbackHoverTabBg);
			var hoverText = ResolveBrush(KeyToolTabUnselectedHoveredText, FallbackHoverTabText);
			var panelBorder = ResolveBrush("UnoDock_VS2013_PanelBorderBrush", FallbackInactiveTabBg);
			var transparentBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

			foreach (var container in _tabStrip.ItemsPanelRoot.Children)
			{
				var tabOuter = FindTabBorder(container);
				if (tabOuter is null)
				{
					DockLog.Write($"  [tab] FindTabBorder returned null for container={container?.GetType().Name}");
					continue;
				}
				WireTabHoverRefresh(tabOuter);
				bool isHovered = ReferenceEquals(_hoveredTabBorder, tabOuter);
				bool isSelected = tabOuter.Tag is LayoutAnchorable laSel && laSel.IsSelected;
				bool isActiveTab = tabOuter.Tag is LayoutAnchorable laAct && laAct.IsActive;
				var tagTitle = (tabOuter.Tag as LayoutAnchorable)?.Title ?? "(no tag)";
				DockLog.Write($"  [tab] {tagTitle}  isSelected={isSelected}  isActive={isActiveTab}  isHovered={isHovered}  tagType={tabOuter.Tag?.GetType().Name}");

				// Tab background
				Brush tabBg;
				if (isSelected)
					tabBg = isActiveTab ? selectedActiveBg : selectedInactiveBg;
				else
					tabBg = isHovered ? hoverBg : unselectedBg;
				tabOuter.Background = tabBg;

				// L/R/B border: visible (PanelBorderBrush) only for selected tab.
				// For all other states use the same color as the tab background so the
				// 1px border slots are seamless — matching WPF BorderBrush=Background.
				tabOuter.BorderBrush = isSelected ? panelBorder : tabBg;

				// Text color: blue for selected, normal for unselected
				var text = FindTabTitleText(tabOuter);
				if (text != null)
				{
					if (isSelected)
						text.Foreground = isActiveTab ? selectedActiveText : selectedInactiveText;
					else
						text.Foreground = isHovered ? hoverText : unselectedText;
				}

				// SelectedBD: erases the strip's top separator at the active tab position.
				// It is the FIRST child of the wrapping Grid (sibling of TabOuter).
				if (tabOuter.Parent is Grid cellGrid && cellGrid.Children.Count >= 1
					&& cellGrid.Children[0] is Border selectedBd)
				{
					selectedBd.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
					if (isSelected)
						selectedBd.BorderBrush = isActiveTab ? selectedActiveBg : selectedInactiveBg;
				}
			}
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

		private static TextBlock FindTabTitleText(Border tabOuter)
			=> tabOuter?.Child as TextBlock;

		private void OnSizeChanged(object sender, SizeChangedEventArgs e)
		{
			(_model as ILayoutPositionableElementWithActualSize).ActualWidth = ActualWidth;
			(_model as ILayoutPositionableElementWithActualSize).ActualHeight = ActualHeight;
		}
	}
}
