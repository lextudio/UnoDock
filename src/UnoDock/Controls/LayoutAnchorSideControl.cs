// Real implementation of the auto-hide side panel tab bar.
// Replaces the Phase-1 FrameworkElement stub.
//
// Each LayoutAnchorSide (Left/Right/Top/Bottom) contains LayoutAnchorGroups,
// each of which contains LayoutAnchorables. This control renders a thin strip
// of tab buttons — one per LayoutAnchorable — along the appropriate edge.
// Clicking a tab opens the flyout Popup showing the content.

using System;
using System.Collections.Specialized;
using System.Linq;
using AvalonDock.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace AvalonDock.Controls
{
	public sealed class LayoutAnchorSideControl : Control, ILayoutControl
	{
		private const string KeyTabBarBackground = "UnoDock_VS2013_TabBarBackground";
		private const string KeyTabBarBorderBrush = "UnoDock_VS2013_TabBarBorderBrush";
		private const string KeyTabText = "UnoDock_VS2013_TabText";
		private const string KeyContentBackground = "UnoDock_VS2013_ContentBackground";
		private const string KeyPanelBorderBrush = "UnoDock_VS2013_PanelBorderBrush";
		private const string KeyAccentBrush = "UnoDock_VS2013_ControlAccentBrush";
		private const string KeyResizerBackground = "UnoDock_VS2013_ResizerBackground";

		private static readonly SolidColorBrush FallbackTabBarBackground =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x2D, 0x2D, 0x30));
		private static readonly SolidColorBrush FallbackTabBarBorder =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x3F, 0x3F, 0x46));
		private static readonly SolidColorBrush FallbackTabText =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xD4, 0xD4, 0xD4));
		private static readonly SolidColorBrush FallbackContentBackground =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E));
		private static readonly SolidColorBrush FallbackPanelBorder =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x3F, 0x3F, 0x46));
		private static readonly SolidColorBrush FallbackAccent =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x00, 0x7A, 0xCC));
		private static readonly SolidColorBrush FallbackResizer =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x3F, 0x3F, 0x46));

		private readonly LayoutAnchorSide _model;
		private StackPanel _tabPanel;
		private Popup _flyout;
		private LayoutAnchorable _flyoutContent;
		// Slide animation state — kept so HideFlyout can play the reverse animation.
		private TranslateTransform _flyoutTranslate;
		private double _flyoutSlideFrom;
		private bool _flyoutHorizontal;
		// Global pointer-press handler used to close the flyout when clicking outside.
		private PointerEventHandler _globalDismissHandler;

		ILayoutElement ILayoutControl.Model => _model;

		public LayoutAnchorSideControl(LayoutAnchorSide model)
		{
			_model = model ?? throw new ArgumentNullException(nameof(model));
			DefaultStyleKey = typeof(LayoutAnchorSideControl);
			_model.Children.CollectionChanged += OnGroupsChanged;
			Loaded += (_, _) =>
			{
				ApplyThemeResources();
				RebuildGroups();
			};
		}

		protected override void OnApplyTemplate()
		{
			base.OnApplyTemplate();
			_tabPanel = GetTemplateChild("PART_TabPanel") as StackPanel;
			ApplyThemeResources();
			UpdateTabPanelOrientation();
			RebuildGroups();
		}

		private void ApplyThemeResources()
		{
			Background = ResolveBrush(KeyTabBarBackground, FallbackTabBarBackground);
		}

		private void OnGroupsChanged(object sender, NotifyCollectionChangedEventArgs e)
			=> RebuildGroups();

		private void RebuildGroups()
		{
			if (_tabPanel == null) return;
			_tabPanel.Children.Clear();
			UpdateTabPanelOrientation();
			var separatorBrush = ResolveBrush(KeyTabBarBorderBrush, FallbackTabBarBorder);

			bool isVertical = _model.Side == AnchorSide.Left || _model.Side == AnchorSide.Right;
			bool first = true;
			foreach (var group in _model.Children)
			{
				if (!first)
				{
					// Separator between groups (4px gap with a subtle divider line)
					_tabPanel.Children.Add(new Border
					{
						Background = separatorBrush,
						Width      = isVertical ? double.NaN : 1,
						Height     = isVertical ? 1 : double.NaN,
						Margin     = isVertical ? new Thickness(2, 4, 2, 4) : new Thickness(4, 2, 4, 2),
					});
				}
				first = false;
				_tabPanel.Children.Add(new LayoutAnchorGroupControl(group, this));
			}

			Visibility = Visibility.Visible;
		}

		private void UpdateTabPanelOrientation()
		{
			if (_tabPanel == null) return;
			_tabPanel.Orientation =
				_model.Side == AnchorSide.Left || _model.Side == AnchorSide.Right
					? Microsoft.UI.Xaml.Controls.Orientation.Vertical
					: Microsoft.UI.Xaml.Controls.Orientation.Horizontal;
		}

		// Hover open/close timers — VS-style: enter a tab → open after short delay;
		// leave tab → close after short delay (gives time to move cursor into flyout).
		// Internal so LayoutAnchorControl can call them directly.
		private DispatcherTimer _hoverOpenTimer;
		private DispatcherTimer _hoverCloseTimer;

		internal void ScheduleHoverOpen(LayoutAnchorable anc, FrameworkElement anchor)
		{
			CancelHoverOpen();
			CancelHoverClose();
			if (IsFlyoutOpen && _flyoutContent == anc) return; // already showing this one
			_hoverOpenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
			_hoverOpenTimer.Tick += (_, _) =>
			{
				CancelHoverOpen();
				ShowFlyout(anc, anchor);
			};
			_hoverOpenTimer.Start();
		}

		internal void CancelHoverOpen()
		{
			_hoverOpenTimer?.Stop();
			_hoverOpenTimer = null;
		}

		internal void ScheduleHoverClose()
		{
			if (!IsFlyoutOpen) return;
			CancelHoverClose();
			_hoverCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
			_hoverCloseTimer.Tick += (_, _) =>
			{
				CancelHoverClose();
				HideFlyout();
			};
			_hoverCloseTimer.Start();
		}

		internal void CancelHoverClose()
		{
			_hoverCloseTimer?.Stop();
			_hoverCloseTimer = null;
		}

		internal void ShowFlyout(LayoutAnchorable anc, FrameworkElement anchor)
		{
			HideFlyout();
			_flyoutContent = anc;
			var tabBarBg = ResolveBrush(KeyTabBarBackground, FallbackTabBarBackground);
			var tabText = ResolveBrush(KeyTabText, FallbackTabText);
			var contentBg = ResolveBrush(KeyContentBackground, FallbackContentBackground);
			var panelBorder = ResolveBrush(KeyPanelBorderBrush, FallbackPanelBorder);
			var accent = ResolveBrush(KeyAccentBrush, FallbackAccent);
			var resizerBg = ResolveBrush(KeyResizerBackground, FallbackResizer);

			var side = _model.Side;
			double flyoutW = Math.Max(anc.AutoHideWidth > 0 ? anc.AutoHideWidth : 250, 150);
			double flyoutH = Math.Max(anc.AutoHideHeight > 0 ? anc.AutoHideHeight : 250, 150);

			// For left/right strips the flyout spans the full height of the strip;
			// for top/bottom strips it spans the full width. Use the strip control's
			// actual measured size, falling back to the saved per-side dimension.
			if (side == AnchorSide.Left || side == AnchorSide.Right)
				flyoutH = ActualHeight > 0 ? ActualHeight : flyoutH;
			else
				flyoutW = ActualWidth > 0 ? ActualWidth : flyoutW;

			var header = new Border
			{
				Background = tabBarBg,
				Height = 28,
				Child = new Grid
				{
					Children =
					{
						new TextBlock
						{
							Text = anc.Title,
							Foreground = tabText,
							VerticalAlignment = VerticalAlignment.Center,
							Margin = new Thickness(8, 0, 0, 0),
							FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
							FontSize = 12,
						},
					}
				}
			};

			// "Pin" button to toggle auto-hide off (restore to pane)
			var pinBtn = new Button
			{
				Content = new Path
				{
					Width = 11,
					Height = 11,
					Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
						typeof(Geometry),
						"M 128,17.475L 130.842,17.475L 130.842,2.91001L 130.842,0.608195L 130.842,0.000223796L 139.593,0.000223796L 145.003,0.000223796L 145.424,0.000223796L 145.424,17.475L 148.413,17.475L 148.413,20.3848L 139.684,20.3848L 139.684,32.0003L 136.752,32.0003L 136.752,20.3848L 128,20.3848L 128,17.475 Z M 133.774,2.91007L 133.774,17.475L 139.593,17.475L 139.593,2.91007L 133.774,2.91007 Z"),
					Fill = accent,
					Stretch = Stretch.Uniform,
				},
				Padding = new Thickness(4, 2, 4, 2),
				Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
				BorderThickness = new Thickness(0),
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 4, 0),
			};
			ToolTipService.SetToolTip(pinBtn, "Pin (restore to pane)");
			pinBtn.Click += (_, _) =>
			{
				HideFlyout();
				anc.ToggleAutoHide();
			};
			(header.Child as Grid)!.Children.Add(pinBtn);

			var contentArea = new Border
			{
				Background = contentBg,
				Child = anc.Content as UIElement ?? new TextBlock
				{
					Text = $"[{anc.Title}]",
					Foreground = tabText,
					Margin = new Thickness(8),
					FontSize = 12,
				},
			};

			// Outer resizable container: a Grid with a Thumb splitter on the inner edge
			// so the user can drag to resize. Resize persists to AutoHideWidth/Height.
			bool isVerticalStrip = side == AnchorSide.Left || side == AnchorSide.Right;
			var splitter = new Thumb
			{
				Background = resizerBg,
			};
			var resizeCursorShape = isVerticalStrip
				? Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast
				: Microsoft.UI.Input.InputSystemCursorShape.SizeNorthSouth;
			splitter.PointerEntered += (_, _) =>
				ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(resizeCursorShape);
			splitter.PointerExited += (_, _) =>
				ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(
					Microsoft.UI.Input.InputSystemCursorShape.Arrow);

			Grid outerGrid;
			if (isVerticalStrip)
			{
				// Left/Right: splitter on the far edge (right for Left, left for Right)
				splitter.Width = 4;
				splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
				splitter.VerticalAlignment = VerticalAlignment.Stretch;
				outerGrid = new Grid { Height = flyoutH };
				if (side == AnchorSide.Left)
				{
					outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(flyoutW) });
					outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
				}
				else
				{
					outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
					outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(flyoutW) });
				}
			}
			else
			{
				// Top/Bottom: splitter on the far edge
				splitter.Height = 4;
				splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
				splitter.VerticalAlignment = VerticalAlignment.Stretch;
				outerGrid = new Grid { Width = flyoutW };
				if (side == AnchorSide.Top)
				{
					outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(flyoutH) });
					outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
				}
				else
				{
					outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
					outerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(flyoutH) });
				}
			}

			var panel = new Grid();
			panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
			panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
			Grid.SetRow(header, 0); panel.Children.Add(header);
			Grid.SetRow(contentArea, 1); panel.Children.Add(contentArea);

			// Arrange inner panel and splitter in the outer resizing grid
			int panelCol = 0, panelRow = 0, splitterCol = 0, splitterRow = 0;
			if (isVerticalStrip)
			{
				panelCol    = side == AnchorSide.Left ? 0 : 1;
				splitterCol = side == AnchorSide.Left ? 1 : 0;
				Grid.SetColumn(panel,    panelCol);
				Grid.SetColumn(splitter, splitterCol);
			}
			else
			{
				panelRow    = side == AnchorSide.Top ? 0 : 1;
				splitterRow = side == AnchorSide.Top ? 1 : 0;
				Grid.SetRow(panel,    panelRow);
				Grid.SetRow(splitter, splitterRow);
			}
			outerGrid.Children.Add(panel);
			outerGrid.Children.Add(splitter);

			// Thumb drag: update the panel column/row size as the user drags.
			splitter.DragDelta += (_, de) =>
			{
				if (isVerticalStrip)
				{
					var col = outerGrid.ColumnDefinitions[panelCol];
					double sign = side == AnchorSide.Left ? 1 : -1;
					var newW = Math.Max(100, col.ActualWidth + de.HorizontalChange * sign);
					col.Width = new GridLength(newW);
				}
				else
				{
					var row = outerGrid.RowDefinitions[panelRow];
					double sign = side == AnchorSide.Top ? 1 : -1;
					var newH = Math.Max(60, row.ActualHeight + de.VerticalChange * sign);
					row.Height = new GridLength(newH);
				}
			};
			// Persist dimensions when drag completes.
			splitter.DragCompleted += (_, _) =>
			{
				if (isVerticalStrip)
				{
					var w = outerGrid.ColumnDefinitions[panelCol].ActualWidth;
					if (w > 0) anc.AutoHideWidth = w;
				}
				else
				{
					var h = outerGrid.RowDefinitions[panelRow].ActualHeight;
					if (h > 0) anc.AutoHideHeight = h;
				}
			};

			var outerBorder = new Border
			{
				Background = contentBg,
				BorderBrush = panelBorder,
				BorderThickness = new Thickness(1),
				Child = outerGrid,
			};

			// Position flyout on the inner side of the anchor strip.
			var placement = _model.Side switch
			{
				AnchorSide.Left   => PopupPlacementMode.RightEdgeAlignedTop,
				AnchorSide.Right  => PopupPlacementMode.LeftEdgeAlignedTop,
				AnchorSide.Top    => PopupPlacementMode.BottomEdgeAlignedLeft,
				AnchorSide.Bottom => PopupPlacementMode.TopEdgeAlignedLeft,
				_                 => PopupPlacementMode.Auto,
			};

			// Wrap the flyout content in a clip container so the slide animation cannot
			// bleed over the anchor tab strip. The Popup renders above all XamlRoot
			// content, so without clipping the translated content covers the anchor tabs.
			// outerBorder total size includes 1px border on each side plus the splitter
			// (4px) on the inner edge: flyoutW+6 wide or flyoutH+6 tall.
			// The slideFrom offset is extended by the same amount so the content is
			// completely hidden before the animation starts.
			const int outerExtra = 6; // 1px border × 2 + 4px splitter
			double clipW = isVerticalStrip ? flyoutW + outerExtra : flyoutW + 2;
			double clipH = isVerticalStrip ? flyoutH + 2 : flyoutH + outerExtra;
			var clipHost = new Grid
			{
				Width  = clipW,
				Height = clipH,
				Clip   = new RectangleGeometry
				{
					Rect = new Windows.Foundation.Rect(0, 0, clipW, clipH),
				},
			};
			clipHost.Children.Add(outerBorder);

			// Slide-in animation: translate from completely outside the clip inward to 0.
			var translate = new TranslateTransform();
			double slideFrom = _model.Side switch
			{
				AnchorSide.Left   => -(flyoutW + outerExtra),
				AnchorSide.Right  =>  (flyoutW + outerExtra),
				AnchorSide.Top    => -(flyoutH + outerExtra),
				AnchorSide.Bottom =>  (flyoutH + outerExtra),
				_                 => 0,
			};
			bool horizontal = _model.Side == AnchorSide.Left || _model.Side == AnchorSide.Right;
			outerBorder.RenderTransform = translate;
			// Store for slide-out animation in HideFlyout.
			_flyoutTranslate  = translate;
			_flyoutSlideFrom  = slideFrom;
			_flyoutHorizontal = horizontal;

			var animIn = new DoubleAnimation
			{
				From     = slideFrom,
				To       = 0,
				Duration = new Duration(TimeSpan.FromMilliseconds(150)),
				EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
			};
			Storyboard.SetTarget(animIn, translate);
			Storyboard.SetTargetProperty(animIn, horizontal ? "X" : "Y");
			var sb = new Storyboard();
			sb.Children.Add(animIn);

			// Anchor the popup to the strip control (not the individual tab) so that
			// RightEdgeAlignedTop / LeftEdgeAlignedTop align the flyout to the top of
			// the whole strip, giving the VS full-height panel behaviour.
			// IsLightDismissEnabled=false so hovering over the flyout doesn't close it;
			// we close manually via a global PointerPressed handler (see below).
			_flyout = new Popup
			{
				Child = clipHost,
				IsLightDismissEnabled = false,
				PlacementTarget = this,
				DesiredPlacement = placement,
				XamlRoot = XamlRoot,
			};
			// Cancel hover-close when cursor enters the flyout; re-arm when it leaves.
			outerBorder.PointerEntered += (_, _) => CancelHoverClose();
			outerBorder.PointerExited  += (_, _) => ScheduleHoverClose();

			_flyout.Closed += (_, _) => UnwireGlobalDismiss();
			_flyout.IsOpen = true;
			// Start animation on the next dispatcher frame so the popup is in the tree.
			DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High,
				() =>
				{
					if (horizontal) translate.X = slideFrom;
					else            translate.Y = slideFrom;
					sb.Begin();
				});
			// Defer wiring until the next frame so the current pointer-press event
			// (the tab click that opened the flyout) has finished bubbling and won't
			// immediately trigger the dismiss handler.
			DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
				() => WireGlobalDismiss(clipHost));

			// Keep pin button visible across themes.
			if ((header.Child as Grid)?.Children.LastOrDefault() is Button pin)
			{
				pin.Foreground = accent;
				if (pin.Content is Path pinGlyph)
					pinGlyph.Fill = accent;
			}
		}

		internal Brush ResolveBrush(string key, Brush fallback)
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

		private void WireGlobalDismiss(FrameworkElement flyoutRoot)
		{
			UnwireGlobalDismiss();
			_globalDismissHandler = (_, e) =>
			{
				if (_flyout is null) return;
				// Close only when the press lands outside the flyout content.
				var pt = e.GetCurrentPoint(flyoutRoot).Position;
				bool insideFlyout = pt.X >= 0 && pt.Y >= 0
				                 && pt.X <= flyoutRoot.ActualWidth
				                 && pt.Y <= flyoutRoot.ActualHeight;
				if (!insideFlyout)
					HideFlyout();
			};
			if (XamlRoot?.Content is UIElement root)
				root.AddHandler(PointerPressedEvent, _globalDismissHandler, handledEventsToo: true);
		}

		private void UnwireGlobalDismiss()
		{
			if (_globalDismissHandler is null) return;
			if (XamlRoot?.Content is UIElement root)
				root.RemoveHandler(PointerPressedEvent, _globalDismissHandler);
			_globalDismissHandler = null;
		}

		private void HideFlyout()
		{
			CancelHoverClose();
			if (_flyout is null) return;

			// Play the slide-out (reverse) animation, then close when it completes.
			var flyoutToClose = _flyout;
			_flyout = null;        // prevent re-entrancy
			_flyoutContent = null;

			if (_flyoutTranslate is not null)
			{
				var animOut = new DoubleAnimation
				{
					From     = _flyoutHorizontal ? _flyoutTranslate.X : _flyoutTranslate.Y,
					To       = _flyoutSlideFrom,
					Duration = new Duration(TimeSpan.FromMilliseconds(120)),
					EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
				};
				Storyboard.SetTarget(animOut, _flyoutTranslate);
				Storyboard.SetTargetProperty(animOut, _flyoutHorizontal ? "X" : "Y");
				var sbOut = new Storyboard();
				sbOut.Children.Add(animOut);
				sbOut.Completed += (_, _) =>
				{
					flyoutToClose.IsOpen = false;
					UnwireGlobalDismiss();
				};
				sbOut.Begin();
			}
			else
			{
				flyoutToClose.IsOpen = false;
				UnwireGlobalDismiss();
			}
			_flyoutTranslate = null;
		}

		/// <summary>Is the flyout currently showing for this anchorable?</summary>
		public bool IsFlyoutOpen => _flyout?.IsOpen == true;

		/// <summary>Programmatically open the flyout for a specific anchorable.
		/// Used by DevFlow actions and keyboard shortcuts.</summary>
		public void OpenFlyoutFor(LayoutAnchorable anc)
		{
			if (_tabPanel == null) RebuildGroups();
			var anchorCtrl = _tabPanel?.Children
				.OfType<LayoutAnchorGroupControl>()
				.SelectMany(g => g.AnchorPanel.Children.OfType<LayoutAnchorControl>())
				.FirstOrDefault(c => c.Model == anc);
			if (anchorCtrl?.Content is FrameworkElement fe)
				ShowFlyout(anc, fe);
			else
				ShowFlyout(anc, this);
		}

	}

}
