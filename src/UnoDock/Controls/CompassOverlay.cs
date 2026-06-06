// // Simple 5-zone compass overlay for re-docking.
// // Displayed over the DockingManager during a floating-window drag.
// // Indicator visuals are template-driven (resource keyed) so each theme can
// // independently replace the docking buttons, similar to AvalonDock OverlayButtons.xaml.

// using System;
// using System.Text;
// using AvalonDock.Layout;
// using Microsoft.UI;
// using Microsoft.UI.Xaml;
// using Microsoft.UI.Xaml.Controls;
// using Microsoft.UI.Xaml.Markup;
// using Microsoft.UI.Xaml.Media;
// using Microsoft.UI.Xaml.Shapes;

// namespace AvalonDock.Controls
// {
// 	public enum CompassZone { None, Center, Left, Right, Top, Bottom }
// 		public enum CompassDragKind { Document, Anchorable, DocumentFull, DocumentAsAnchorable, ManagerOnly }

// 	internal sealed class CompassOverlay
// 	{
// 		private const string KeyDockingButtonWidth = "UnoDock_VS2013_DockingButtonWidth";
// 		private const string KeyDockingButtonHeight = "UnoDock_VS2013_DockingButtonHeight";

// 		private const string KeyManagerLeftTemplate = "UnoDock_VS2013_DockAnchorableLeftTemplate";
// 		private const string KeyManagerRightTemplate = "UnoDock_VS2013_DockAnchorableRightTemplate";
// 		private const string KeyManagerTopTemplate = "UnoDock_VS2013_DockAnchorableTopTemplate";
// 		private const string KeyManagerBottomTemplate = "UnoDock_VS2013_DockAnchorableBottomTemplate";

// 		private const string KeyDocumentLeftTemplate = "UnoDock_VS2013_DockDocumentLeftTemplate";
// 		private const string KeyDocumentRightTemplate = "UnoDock_VS2013_DockDocumentRightTemplate";
// 		private const string KeyDocumentTopTemplate = "UnoDock_VS2013_DockDocumentTopTemplate";
// 		private const string KeyDocumentBottomTemplate = "UnoDock_VS2013_DockDocumentBottomTemplate";
// 		private const string KeyDocumentCenterTemplate = "UnoDock_VS2013_DockDocumentInsideTemplate";

// 		private const string KeyAnchorableLeftTemplate = "UnoDock_VS2013_DockAnchorableLeftTemplate";
// 		private const string KeyAnchorableRightTemplate = "UnoDock_VS2013_DockAnchorableRightTemplate";
// 		private const string KeyAnchorableTopTemplate = "UnoDock_VS2013_DockAnchorableTopTemplate";
// 		private const string KeyAnchorableBottomTemplate = "UnoDock_VS2013_DockAnchorableBottomTemplate";
// 		private const string KeyAnchorableCenterTemplate = "UnoDock_VS2013_DockAnchorableInsideTemplate";

// 		private const string KeyDocumentAsAnchorableLeftTemplate = "UnoDock_VS2013_DockDocumentAsAnchorableLeftTemplate";
// 		private const string KeyDocumentAsAnchorableRightTemplate = "UnoDock_VS2013_DockDocumentAsAnchorableRightTemplate";
// 		private const string KeyDocumentAsAnchorableTopTemplate = "UnoDock_VS2013_DockDocumentAsAnchorableTopTemplate";
// 		private const string KeyDocumentAsAnchorableBottomTemplate = "UnoDock_VS2013_DockDocumentAsAnchorableBottomTemplate";

// 		private const string KeyDockingButtonBackground = "UnoDock_VS2013_DockingButtonBackgroundBrush";
// 		private const string KeyDockingButtonStarBorder = "UnoDock_VS2013_DockingButtonStarBorderBrush";
// 		private const string KeyPreviewBorder = "UnoDock_VS2013_PreviewBoxBorderBrush";
// 		private const string KeyPreviewBackground = "UnoDock_VS2013_PreviewBoxBackgroundBrush";
// 		private const string KeyDockPaneEmptyGeometry = "UnoDock_VS2013_DockPaneEmptyGeometry";
// 		private const string KeyDockPaneLargeEmptyGeometry = "UnoDock_VS2013_DockPaneLargeEmptyGeometry";
// 		private const string DockPaneEmptyData = "M 266.388,0.000223796L 277.612,0.000223796L 277.612,7.60856L 280.392,10.3883L 288,10.3883L 288,21.6122L 280.392,21.6122L 277.612,24.3919L 277.612,32.0002L 266.388,32.0002L 266.388,24.3919L 263.608,21.6122L 256,21.6122L 256,10.3883L 263.608,10.3883L 266.388,7.60856L 266.388,0.000223796 Z";
// 		private const string DockPaneLargeEmptyData = "M 192,19.4161L 192,12.5843L 202.892,12.5843L 204.584,10.8924L 204.584,0.000223796L 211.416,0.000223796L 211.416,10.8924L 213.108,12.5843L 224,12.5843L 224,19.4161L 213.108,19.4161L 211.416,21.108L 211.416,32.0002L 204.584,32.0002L 204.584,21.108L 202.892,19.4161L 192,19.4161 Z";

// 		// Size of each drop-zone indicator (logical pixels)
// 		private double _zoneWidth = 40;
// 		private double _zoneHeight = 40;
// 		private const double ZoneOffset = 55; // distance from center to adjacent zone
// 		private const double AsOffset = 100;
// 		private readonly double _outerScale = 1.15;

// 		private readonly Canvas _canvas;
// 		private readonly Border _zoneCenter, _zoneLeft, _zoneRight, _zoneTop, _zoneBottom;
// 		private readonly Border _asLeft, _asRight, _asTop, _asBottom;
// 		private readonly Border _mgrLeft, _mgrRight, _mgrTop, _mgrBottom;
// 		private readonly Path _centerBackdropSmall;
// 		private readonly Path _centerBackdropLarge;
// 		// Drop-preview rectangle — highlights the region where content would land.
// 		private readonly Border _preview;
// 		private CompassDragKind _dragKind = CompassDragKind.Document;

// 		private Brush _normalBrush;
// 		private Brush _hoverBrush;
// 		private Brush _previewFill;
// 		private Brush _previewBorder;
// 		private Brush _zoneBorderBrush;
// 		private bool _innerLeftVisible = true;
// 		private bool _innerTopVisible = true;
// 		private bool _innerRightVisible = true;
// 		private bool _innerBottomVisible = true;
// 		private bool _centerVisible = true;
// 		private bool _asLeftVisible = true;
// 		private bool _asTopVisible = true;
// 		private bool _asRightVisible = true;
// 		private bool _asBottomVisible = true;

// 		public Canvas Canvas => _canvas;
// 		public CompassZone HoveredZone { get; private set; } = CompassZone.None;

// 		public CompassOverlay()
// 		{
// 			UpdateThemeBrushes();

// 			_canvas = new Canvas
// 			{
// 				IsHitTestVisible = false, // compass itself doesn't capture pointer
// 				Visibility = Visibility.Collapsed,
// 			};

// 			// Preview rectangle goes beneath the zone buttons (inserted first).
// 			_preview = new Border
// 			{
// 				Background      = _previewFill,
// 				BorderBrush     = _previewBorder,
// 				BorderThickness = new Thickness(2),
// 				CornerRadius    = new CornerRadius(2),
// 				Visibility      = Visibility.Collapsed,
// 			};
// 			_canvas.Children.Add(_preview);

// 			_centerBackdropSmall = MakeBackdrop(KeyDockPaneEmptyGeometry, DockPaneEmptyData, 122, 122);
// 			_centerBackdropLarge = MakeBackdrop(KeyDockPaneLargeEmptyGeometry, DockPaneLargeEmptyData, 204, 122);
// 			_canvas.Children.Add(_centerBackdropSmall);
// 			_canvas.Children.Add(_centerBackdropLarge);

// 			_zoneCenter = MakeZone(KeyDocumentCenterTemplate, "⊕");
// 			_zoneLeft   = MakeZone(KeyDocumentLeftTemplate, "◀");
// 			_zoneRight  = MakeZone(KeyDocumentRightTemplate, "▶");
// 			_zoneTop    = MakeZone(KeyDocumentTopTemplate, "▲");
// 			_zoneBottom = MakeZone(KeyDocumentBottomTemplate, "▼");
// 			_asLeft     = MakeZone(KeyDocumentAsAnchorableLeftTemplate, "◀");
// 			_asRight    = MakeZone(KeyDocumentAsAnchorableRightTemplate, "▶");
// 			_asTop      = MakeZone(KeyDocumentAsAnchorableTopTemplate, "▲");
// 			_asBottom   = MakeZone(KeyDocumentAsAnchorableBottomTemplate, "▼");
// 			_mgrLeft    = MakeZone(KeyManagerLeftTemplate, "◀");
// 			_mgrRight   = MakeZone(KeyManagerRightTemplate, "▶");
// 			_mgrTop     = MakeZone(KeyManagerTopTemplate, "▲");
// 			_mgrBottom  = MakeZone(KeyManagerBottomTemplate, "▼");

// 			_canvas.Children.Add(_zoneCenter);
// 			_canvas.Children.Add(_zoneLeft);
// 			_canvas.Children.Add(_zoneRight);
// 			_canvas.Children.Add(_zoneTop);
// 			_canvas.Children.Add(_zoneBottom);
// 			_canvas.Children.Add(_asLeft);
// 			_canvas.Children.Add(_asRight);
// 			_canvas.Children.Add(_asTop);
// 			_canvas.Children.Add(_asBottom);
// 			_canvas.Children.Add(_mgrLeft);
// 			_canvas.Children.Add(_mgrRight);
// 			_canvas.Children.Add(_mgrTop);
// 			_canvas.Children.Add(_mgrBottom);
// 		}

// 		private Border MakeZone(string templateKey, string fallbackGlyph) => new Border
// 		{
// 			Width   = _zoneWidth,
// 			Height  = _zoneHeight,
// 			CornerRadius = new CornerRadius(4),
// 			Background = _normalBrush,
// 			BorderBrush = _zoneBorderBrush,
// 			BorderThickness = new Thickness(1),
// 			Opacity = 0.9,
// 			Child = CreateZoneContent(templateKey, fallbackGlyph)
// 		};

// 		private UIElement CreateZoneContent(string templateKey, string fallbackGlyph)
// 		{
// 			if (TryLoadTemplate(templateKey, out var templateContent))
// 			{
// 				return new Viewbox
// 				{
// 					Stretch = Stretch.Uniform,
// 					Child = templateContent,
// 				};
// 			}

// 			return new TextBlock
// 			{
// 				Text = fallbackGlyph,
// 				FontSize = 20,
// 				HorizontalAlignment = HorizontalAlignment.Center,
// 				VerticalAlignment = VerticalAlignment.Center,
// 			};
// 		}

// 		private Path MakeBackdrop(string geometryKey, string fallbackGeometryData, double width, double height)
// 		{
// 			var path = new Path
// 			{
// 				Width = width,
// 				Height = height,
// 				Stretch = Stretch.Uniform,
// 				IsHitTestVisible = false,
// 				Visibility = Visibility.Collapsed,
// 			};

// 			if (TryResolveResource(geometryKey, out var geom) && geom is Geometry geometry)
// 			{
// 				path.Data = geometry;
// 			}
// 			else
// 			{
// 				path.Data = ParseGeometry(fallbackGeometryData);
// 			}

// 			return path;
// 		}

// 		private static Geometry ParseGeometry(string data)
// 		{
// 			try
// 			{
// 				var escaped = data.Replace("&", "&amp;").Replace("\"", "&quot;");
// 				return (Geometry)XamlReader.Load(
// 					$"<PathGeometry xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Figures=\"{escaped}\" />");
// 			}
// 			catch
// 			{
// 				return null;
// 			}
// 		}

// 		private bool TryLoadTemplate(string key, out UIElement content)
// 		{
// 			content = null;
// 			if (TryResolveResource(key, out var value) && value is DataTemplate template)
// 			{
// 				content = template.LoadContent() as UIElement;
// 				return content != null;
// 			}

// 			return false;
// 		}

// 		public void SetDragKind(CompassDragKind kind)
// 		{
// 			if (_dragKind == kind)
// 				return;

// 			_dragKind = kind;
// 			RefreshZoneTemplates();
// 		}

// 		public void SetDirectionalVisibility(
// 			bool innerLeft,
// 			bool innerTop,
// 			bool innerRight,
// 			bool innerBottom,
// 			bool asLeft,
// 			bool asTop,
// 			bool asRight,
// 			bool asBottom)
// 		{
// 			_innerLeftVisible = innerLeft;
// 			_innerTopVisible = innerTop;
// 			_innerRightVisible = innerRight;
// 			_innerBottomVisible = innerBottom;
// 			_asLeftVisible = asLeft;
// 			_asTopVisible = asTop;
// 			_asRightVisible = asRight;
// 			_asBottomVisible = asBottom;
// 			ApplyDirectionalVisibility();
// 		}

// 		public void SetCenterVisibility(bool visible)
// 		{
// 			_centerVisible = visible;
// 			ApplyDirectionalVisibility();
// 		}

// 		private void RefreshZoneTemplates()
// 		{
// 			var left = KeyDocumentLeftTemplate;
// 			var right = KeyDocumentRightTemplate;
// 			var top = KeyDocumentTopTemplate;
// 			var bottom = KeyDocumentBottomTemplate;
// 			var center = KeyDocumentCenterTemplate;

// 			if (_dragKind == CompassDragKind.Anchorable)
// 			{
// 				// WPF PART_AnchorablePaneDropTargets uses DockDocumentAsAnchorable* + DockDocumentInside.
// 				left = KeyDocumentAsAnchorableLeftTemplate;
// 				right = KeyDocumentAsAnchorableRightTemplate;
// 				top = KeyDocumentAsAnchorableTopTemplate;
// 				bottom = KeyDocumentAsAnchorableBottomTemplate;
// 				center = KeyDocumentCenterTemplate;
// 			}

// 			_zoneCenter.Child = CreateZoneContent(center, "⊕");
// 			_zoneLeft.Child = CreateZoneContent(left, "◀");
// 			_zoneRight.Child = CreateZoneContent(right, "▶");
// 			_zoneTop.Child = CreateZoneContent(top, "▲");
// 			_zoneBottom.Child = CreateZoneContent(bottom, "▼");

// 			_asLeft.Child = CreateZoneContent(KeyDocumentAsAnchorableLeftTemplate, "◀");
// 			_asRight.Child = CreateZoneContent(KeyDocumentAsAnchorableRightTemplate, "▶");
// 			_asTop.Child = CreateZoneContent(KeyDocumentAsAnchorableTopTemplate, "▲");
// 			_asBottom.Child = CreateZoneContent(KeyDocumentAsAnchorableBottomTemplate, "▼");

// 			_mgrLeft.Child = CreateZoneContent(KeyManagerLeftTemplate, "◀");
// 			_mgrRight.Child = CreateZoneContent(KeyManagerRightTemplate, "▶");
// 			_mgrTop.Child = CreateZoneContent(KeyManagerTopTemplate, "▲");
// 			_mgrBottom.Child = CreateZoneContent(KeyManagerBottomTemplate, "▼");

// 			var showInner = _dragKind != CompassDragKind.DocumentAsAnchorable && _dragKind != CompassDragKind.ManagerOnly;
// 			var showAsRing = _dragKind == CompassDragKind.DocumentFull || _dragKind == CompassDragKind.DocumentAsAnchorable;
// 			var showSmallBackdrop = _dragKind == CompassDragKind.Document || _dragKind == CompassDragKind.Anchorable;
// 			var showLargeBackdrop = _dragKind == CompassDragKind.DocumentFull || _dragKind == CompassDragKind.DocumentAsAnchorable;
// 			var innerVisibility = showInner ? Visibility.Visible : Visibility.Collapsed;
// 			var asVisibility = showAsRing ? Visibility.Visible : Visibility.Collapsed;
// 			_centerBackdropSmall.Visibility = showSmallBackdrop ? Visibility.Visible : Visibility.Collapsed;
// 			_centerBackdropLarge.Visibility = showLargeBackdrop ? Visibility.Visible : Visibility.Collapsed;
// 			_zoneCenter.Visibility = innerVisibility;
// 			_zoneLeft.Visibility = innerVisibility;
// 			_zoneRight.Visibility = innerVisibility;
// 			_zoneTop.Visibility = innerVisibility;
// 			_zoneBottom.Visibility = innerVisibility;
// 			_asLeft.Visibility = asVisibility;
// 			_asRight.Visibility = asVisibility;
// 			_asTop.Visibility = asVisibility;
// 			_asBottom.Visibility = asVisibility;
// 			ApplyDirectionalVisibility();
// 		}

// 		private void ApplyDirectionalVisibility()
// 		{
// 			if (_zoneCenter.Visibility == Visibility.Visible && !_centerVisible)
// 				_zoneCenter.Visibility = Visibility.Collapsed;

// 			if (_zoneLeft.Visibility == Visibility.Visible && !_innerLeftVisible)
// 				_zoneLeft.Visibility = Visibility.Collapsed;
// 			if (_zoneTop.Visibility == Visibility.Visible && !_innerTopVisible)
// 				_zoneTop.Visibility = Visibility.Collapsed;
// 			if (_zoneRight.Visibility == Visibility.Visible && !_innerRightVisible)
// 				_zoneRight.Visibility = Visibility.Collapsed;
// 			if (_zoneBottom.Visibility == Visibility.Visible && !_innerBottomVisible)
// 				_zoneBottom.Visibility = Visibility.Collapsed;

// 			if (_asLeft.Visibility == Visibility.Visible && !_asLeftVisible)
// 				_asLeft.Visibility = Visibility.Collapsed;
// 			if (_asTop.Visibility == Visibility.Visible && !_asTopVisible)
// 				_asTop.Visibility = Visibility.Collapsed;
// 			if (_asRight.Visibility == Visibility.Visible && !_asRightVisible)
// 				_asRight.Visibility = Visibility.Collapsed;
// 			if (_asBottom.Visibility == Visibility.Visible && !_asBottomVisible)
// 				_asBottom.Visibility = Visibility.Collapsed;
// 		}

// 		private void UpdateThemeBrushes()
// 		{
// 			var widthValue = ResolveDouble(KeyDockingButtonWidth, 40);
// 			var heightValue = ResolveDouble(KeyDockingButtonHeight, 40);
// 			_zoneWidth = widthValue > 0 ? widthValue : 40;
// 			_zoneHeight = heightValue > 0 ? heightValue : 40;

// 			var dockingBg = ResolveBrush(KeyDockingButtonBackground,
// 				new SolidColorBrush(Windows.UI.Color.FromArgb(0x20, 0x00, 0x00, 0x00)));
// 			var dockingStarBorder = ResolveBrush(KeyDockingButtonStarBorder,
// 				new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0x80, 0x80, 0x80)));
// 			var previewBg = ResolveBrush(KeyPreviewBackground,
// 				new SolidColorBrush(Windows.UI.Color.FromArgb(0x80, 0x00, 0x7A, 0xCC)));
// 			var previewBorder = ResolveBrush(KeyPreviewBorder,
// 				new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x00, 0x7A, 0xCC)));

// 			_normalBrush = new SolidColorBrush(Colors.Transparent);
// 			_hoverBrush = dockingBg;
// 			_zoneBorderBrush = dockingStarBorder;
// 			_previewFill = previewBg;
// 			_previewBorder = previewBorder;
// 		}

// 		private double ResolveDouble(string key, double fallback)
// 		{
// 			if (TryResolveResource(key, out var value))
// 			{
// 				if (value is double d) return d;
// 				if (value is float f) return f;
// 				if (value is int i) return i;
// 			}

// 			return fallback;
// 		}

// 		private Brush ResolveBrush(string key, Brush fallback)
// 		{
// 			if (TryResolveResource(key, out var value) && value is Brush brush)
// 				return brush;

// 			return fallback;
// 		}

// 		private bool TryResolveResource(string key, out object value)
// 		{
// 			if (_canvas?.Resources != null && _canvas.Resources.TryGetValue(key, out var local))
// 			{
// 				value = local;
// 				return true;
// 			}

// 			DependencyObject current = _canvas;
// 			while (current != null)
// 			{
// 				if (current is FrameworkElement fe
// 					&& fe.Resources != null
// 					&& fe.Resources.TryGetValue(key, out var scoped))
// 				{
// 					value = scoped;
// 					return true;
// 				}

// 				current = VisualTreeHelper.GetParent(current);
// 			}

// 			var appResources = Application.Current?.Resources;
// 			if (appResources != null && appResources.TryGetValue(key, out var app))
// 			{
// 				value = app;
// 				return true;
// 			}

// 			value = null;
// 			return false;
// 		}

// 		/// <summary>Position zones relative to the center of the DockingManager.</summary>
// 		public void Layout(double managerWidth, double managerHeight)
// 		{
// 			_zoneCenter.Width = _zoneWidth;
// 			_zoneCenter.Height = _zoneHeight;
// 			_zoneLeft.Width = _zoneWidth;
// 			_zoneLeft.Height = _zoneHeight;
// 			_zoneRight.Width = _zoneWidth;
// 			_zoneRight.Height = _zoneHeight;
// 			_zoneTop.Width = _zoneWidth;
// 			_zoneTop.Height = _zoneHeight;
// 			_zoneBottom.Width = _zoneWidth;
// 			_zoneBottom.Height = _zoneHeight;
// 			_asLeft.Width = _zoneWidth;
// 			_asLeft.Height = _zoneHeight;
// 			_asRight.Width = _zoneWidth;
// 			_asRight.Height = _zoneHeight;
// 			_asTop.Width = _zoneWidth;
// 			_asTop.Height = _zoneHeight;
// 			_asBottom.Width = _zoneWidth;
// 			_asBottom.Height = _zoneHeight;
// 			_mgrLeft.Width = _zoneWidth * _outerScale;
// 			_mgrLeft.Height = _zoneHeight * _outerScale;
// 			_mgrRight.Width = _zoneWidth * _outerScale;
// 			_mgrRight.Height = _zoneHeight * _outerScale;
// 			_mgrTop.Width = _zoneWidth * _outerScale;
// 			_mgrTop.Height = _zoneHeight * _outerScale;
// 			_mgrBottom.Width = _zoneWidth * _outerScale;
// 			_mgrBottom.Height = _zoneHeight * _outerScale;

// 			double cx = managerWidth  / 2 - _zoneWidth / 2;
// 			double cy = managerHeight / 2 - _zoneHeight / 2;
// 			double edgeMargin = 10;

// 			SetZone(_zoneCenter, cx,                        cy);
// 			SetZone(_zoneLeft,   cx - ZoneOffset - 5,       cy);
// 			SetZone(_zoneRight,  cx + ZoneOffset + 5,       cy);
// 			SetZone(_zoneTop,    cx,                        cy - ZoneOffset - 5);
// 			SetZone(_zoneBottom, cx,                        cy + ZoneOffset + 5);
// 			SetZone(_asLeft,     cx - AsOffset,            cy);
// 			SetZone(_asRight,    cx + AsOffset,            cy);
// 			SetZone(_asTop,      cx,                       cy - AsOffset);
// 			SetZone(_asBottom,   cx,                       cy + AsOffset);
// 			Canvas.SetLeft(_centerBackdropSmall, managerWidth / 2 - _centerBackdropSmall.Width / 2);
// 			Canvas.SetTop(_centerBackdropSmall, managerHeight / 2 - _centerBackdropSmall.Height / 2);
// 			Canvas.SetLeft(_centerBackdropLarge, managerWidth / 2 - _centerBackdropLarge.Width / 2);
// 			Canvas.SetTop(_centerBackdropLarge, managerHeight / 2 - _centerBackdropLarge.Height / 2);
// 			SetZone(_mgrLeft,    edgeMargin,                                   managerHeight / 2 - _mgrLeft.Height / 2);
// 			SetZone(_mgrRight,   managerWidth - _mgrRight.Width - edgeMargin, managerHeight / 2 - _mgrRight.Height / 2);
// 			SetZone(_mgrTop,     managerWidth / 2 - _mgrTop.Width / 2,        edgeMargin);
// 			SetZone(_mgrBottom,  managerWidth / 2 - _mgrBottom.Width / 2,      managerHeight - _mgrBottom.Height - edgeMargin);
// 		}

// 		private static void SetZone(Border b, double left, double top)
// 		{
// 			Canvas.SetLeft(b, left);
// 			Canvas.SetTop(b, top);
// 		}

// 		/// <summary>Update which zone is hovered based on cursor position in manager coords.</summary>
// 		public CompassZone UpdateHover(double hostX, double hostY, double managerWidth, double managerHeight)
// 		{
// 			if (HitTest(_zoneCenter, hostX, hostY))      HoveredZone = CompassZone.Center;
// 			else if (HitTest(_zoneLeft, hostX, hostY) || HitTest(_asLeft, hostX, hostY) || HitTest(_mgrLeft, hostX, hostY)) HoveredZone = CompassZone.Left;
// 			else if (HitTest(_zoneRight, hostX, hostY) || HitTest(_asRight, hostX, hostY) || HitTest(_mgrRight, hostX, hostY)) HoveredZone = CompassZone.Right;
// 			else if (HitTest(_zoneTop, hostX, hostY) || HitTest(_asTop, hostX, hostY) || HitTest(_mgrTop, hostX, hostY)) HoveredZone = CompassZone.Top;
// 			else if (HitTest(_zoneBottom, hostX, hostY) || HitTest(_asBottom, hostX, hostY) || HitTest(_mgrBottom, hostX, hostY)) HoveredZone = CompassZone.Bottom;
// 			else                                          HoveredZone = CompassZone.None;

// 			if (!IsZoneVisible(HoveredZone))
// 				HoveredZone = CompassZone.None;

// 			// Update zone button highlights.
// 			RefreshZone(_zoneCenter, HoveredZone == CompassZone.Center);
// 			RefreshZone(_zoneLeft,   HoveredZone == CompassZone.Left);
// 			RefreshZone(_zoneRight,  HoveredZone == CompassZone.Right);
// 			RefreshZone(_zoneTop,    HoveredZone == CompassZone.Top);
// 			RefreshZone(_zoneBottom, HoveredZone == CompassZone.Bottom);
// 			RefreshZone(_asLeft,     HoveredZone == CompassZone.Left);
// 			RefreshZone(_asRight,    HoveredZone == CompassZone.Right);
// 			RefreshZone(_asTop,      HoveredZone == CompassZone.Top);
// 			RefreshZone(_asBottom,   HoveredZone == CompassZone.Bottom);
// 			RefreshZone(_mgrLeft,    HoveredZone == CompassZone.Left);
// 			RefreshZone(_mgrRight,   HoveredZone == CompassZone.Right);
// 			RefreshZone(_mgrTop,     HoveredZone == CompassZone.Top);
// 			RefreshZone(_mgrBottom,  HoveredZone == CompassZone.Bottom);

// 			// Update drop-preview rectangle.
// 			UpdatePreview(HoveredZone, managerWidth, managerHeight);

// 			return HoveredZone;
// 		}

// 		private bool IsZoneVisible(CompassZone zone)
// 		{
// 			return zone switch
// 			{
// 				CompassZone.Center => _zoneCenter.Visibility == Visibility.Visible,
// 				CompassZone.Left => _zoneLeft.Visibility == Visibility.Visible || _asLeft.Visibility == Visibility.Visible || _mgrLeft.Visibility == Visibility.Visible,
// 				CompassZone.Right => _zoneRight.Visibility == Visibility.Visible || _asRight.Visibility == Visibility.Visible || _mgrRight.Visibility == Visibility.Visible,
// 				CompassZone.Top => _zoneTop.Visibility == Visibility.Visible || _asTop.Visibility == Visibility.Visible || _mgrTop.Visibility == Visibility.Visible,
// 				CompassZone.Bottom => _zoneBottom.Visibility == Visibility.Visible || _asBottom.Visibility == Visibility.Visible || _mgrBottom.Visibility == Visibility.Visible,
// 				_ => false,
// 			};
// 		}

// 		// The preview covers the portion of the manager where content would be inserted.
// 		// Outer thirds -> split (1/3 of manager); Center -> full manager area.

// 		private void UpdatePreview(CompassZone zone, double w, double h)
// 		{
// 			if (!OverlayPreviewRules.TryComputePreviewRect(
// 				MapZone(zone),
// 				w,
// 				h,
// 				out var left,
// 				out var top,
// 				out var width,
// 				out var height))
// 			{
// 				_preview.Visibility = Visibility.Collapsed;
// 				return;
// 			}

// 			Canvas.SetLeft(_preview, left);
// 			Canvas.SetTop(_preview,  top);
// 			_preview.Width  = width;
// 			_preview.Height = height;
// 			_preview.Visibility = Visibility.Visible;
// 		}

// 		private static CompassDropZone MapZone(CompassZone zone)
// 		{
// 			return zone switch
// 			{
// 				CompassZone.Left => CompassDropZone.Left,
// 				CompassZone.Right => CompassDropZone.Right,
// 				CompassZone.Top => CompassDropZone.Top,
// 				CompassZone.Bottom => CompassDropZone.Bottom,
// 				CompassZone.Center => CompassDropZone.Center,
// 				_ => CompassDropZone.None,
// 			};
// 		}

// 		private static bool HitTest(Border b, double x, double y)
// 		{
// 			if (b.Visibility != Visibility.Visible)
// 				return false;

// 			double left = Canvas.GetLeft(b);
// 			double top  = Canvas.GetTop(b);
// 			return x >= left && x <= left + b.Width
// 				&& y >= top  && y <= top  + b.Height;
// 		}

// 		private void RefreshZone(Border b, bool hovered)
// 		{
// 			b.Background = hovered ? _hoverBrush : _normalBrush;
// 			b.BorderBrush = _zoneBorderBrush;
// 			b.Opacity = hovered ? 1.0 : 0.9;
// 		}

// 		public void ForceShow(double managerWidth, double managerHeight, CompassZone zone)
// 		{
// 			Layout(managerWidth, managerHeight);
// 			HoveredZone = zone;
// 			Show();
// 			RefreshZone(_zoneCenter, HoveredZone == CompassZone.Center);
// 			RefreshZone(_zoneLeft, HoveredZone == CompassZone.Left);
// 			RefreshZone(_zoneRight, HoveredZone == CompassZone.Right);
// 			RefreshZone(_zoneTop, HoveredZone == CompassZone.Top);
// 			RefreshZone(_zoneBottom, HoveredZone == CompassZone.Bottom);
// 			UpdatePreview(HoveredZone, managerWidth, managerHeight);
// 			_canvas.Visibility = Visibility.Visible;
// 		}

// 		public string GetDiagnostics()
// 		{
// 			var sb = new StringBuilder();
// 			sb.Append("mode=template-driven-multifamily");
// 			sb.Append($" dragKind={_dragKind}");
// 			sb.Append($" canvasVisible={_canvas.Visibility}");
// 			sb.Append($" previewVisible={_preview.Visibility}");
// 			sb.Append($" zone={HoveredZone}");
// 			sb.Append($" zoneSize={_zoneWidth:F0}x{_zoneHeight:F0}");

// 			sb.Append($" tplMgrL={DescribeResource(KeyManagerLeftTemplate)}");
// 			sb.Append($" tplMgrR={DescribeResource(KeyManagerRightTemplate)}");
// 			sb.Append($" tplMgrT={DescribeResource(KeyManagerTopTemplate)}");
// 			sb.Append($" tplMgrB={DescribeResource(KeyManagerBottomTemplate)}");
// 			sb.Append($" tplDocL={DescribeResource(KeyDocumentLeftTemplate)}");
// 			sb.Append($" tplDocR={DescribeResource(KeyDocumentRightTemplate)}");
// 			sb.Append($" tplDocT={DescribeResource(KeyDocumentTopTemplate)}");
// 			sb.Append($" tplDocB={DescribeResource(KeyDocumentBottomTemplate)}");
// 			sb.Append($" tplDocC={DescribeResource(KeyDocumentCenterTemplate)}");
// 			sb.Append($" tplAncC={DescribeResource(KeyAnchorableCenterTemplate)}");
// 			sb.Append($" tplAsL={DescribeResource(KeyDocumentAsAnchorableLeftTemplate)}");
// 			sb.Append($" tplAsR={DescribeResource(KeyDocumentAsAnchorableRightTemplate)}");
// 			sb.Append($" tplAsT={DescribeResource(KeyDocumentAsAnchorableTopTemplate)}");
// 			sb.Append($" tplAsB={DescribeResource(KeyDocumentAsAnchorableBottomTemplate)}");

// 			sb.Append($" widthRes={DescribeResource(KeyDockingButtonWidth)}");
// 			sb.Append($" heightRes={DescribeResource(KeyDockingButtonHeight)}");

// 			sb.Append($" zoneL={DescribeZone(_zoneLeft)}");
// 			sb.Append($" zoneR={DescribeZone(_zoneRight)}");
// 			sb.Append($" zoneT={DescribeZone(_zoneTop)}");
// 			sb.Append($" zoneB={DescribeZone(_zoneBottom)}");
// 			sb.Append($" zoneC={DescribeZone(_zoneCenter)}");
// 			sb.Append($" asL={DescribeZone(_asLeft)}");
// 			sb.Append($" asR={DescribeZone(_asRight)}");
// 			sb.Append($" asT={DescribeZone(_asTop)}");
// 			sb.Append($" asB={DescribeZone(_asBottom)}");
// 			sb.Append($" mgrL={DescribeZone(_mgrLeft)}");
// 			sb.Append($" mgrR={DescribeZone(_mgrRight)}");
// 			sb.Append($" mgrT={DescribeZone(_mgrTop)}");
// 			sb.Append($" mgrB={DescribeZone(_mgrBottom)}");
// 			sb.Append($" visIn={_zoneLeft.Visibility}/{_zoneTop.Visibility}/{_zoneRight.Visibility}/{_zoneBottom.Visibility}");
// 			sb.Append($" visCenter={_zoneCenter.Visibility}");
// 			sb.Append($" visAs={_asLeft.Visibility}/{_asTop.Visibility}/{_asRight.Visibility}/{_asBottom.Visibility}");

// 			return sb.ToString();
// 		}

// 		private string DescribeResource(string key)
// 		{
// 			if (!TryResolveResource(key, out var value) || value == null)
// 				return "missing";

// 			if (value is DataTemplate dt)
// 			{
// 				var loaded = dt.LoadContent();
// 				return $"DataTemplate->{loaded?.GetType().Name ?? "null"}";
// 			}

// 			return value.GetType().Name;
// 		}

// 		private static string DescribeZone(Border zone)
// 		{
// 			if (zone.Child is Viewbox vb)
// 				return $"Viewbox->{vb.Child?.GetType().Name ?? "null"}";

// 			return zone.Child?.GetType().Name ?? "null";
// 		}

// 		public void Show()
// 		{
// 			UpdateThemeBrushes();

// 			// Recreate zone visuals from template resources so theme changes can replace
// 			// indicator geometry independently from color brushes.
// 			RefreshZoneTemplates();

// 			_preview.Background = _previewFill;
// 			_preview.BorderBrush = _previewBorder;
// 			_centerBackdropSmall.Fill = _hoverBrush;
// 			_centerBackdropSmall.Stroke = _zoneBorderBrush;
// 			_centerBackdropSmall.StrokeThickness = 1;
// 			_centerBackdropLarge.Fill = _hoverBrush;
// 			_centerBackdropLarge.Stroke = _zoneBorderBrush;
// 			_centerBackdropLarge.StrokeThickness = 1;

// 			// Re-apply zone brushes in case theme changed while hidden.
// 			RefreshZone(_zoneCenter, HoveredZone == CompassZone.Center);
// 			RefreshZone(_zoneLeft, HoveredZone == CompassZone.Left);
// 			RefreshZone(_zoneRight, HoveredZone == CompassZone.Right);
// 			RefreshZone(_zoneTop, HoveredZone == CompassZone.Top);
// 			RefreshZone(_zoneBottom, HoveredZone == CompassZone.Bottom);
// 			RefreshZone(_asLeft, HoveredZone == CompassZone.Left);
// 			RefreshZone(_asRight, HoveredZone == CompassZone.Right);
// 			RefreshZone(_asTop, HoveredZone == CompassZone.Top);
// 			RefreshZone(_asBottom, HoveredZone == CompassZone.Bottom);
// 			RefreshZone(_mgrLeft, HoveredZone == CompassZone.Left);
// 			RefreshZone(_mgrRight, HoveredZone == CompassZone.Right);
// 			RefreshZone(_mgrTop, HoveredZone == CompassZone.Top);
// 			RefreshZone(_mgrBottom, HoveredZone == CompassZone.Bottom);

// 			_zoneCenter.BorderBrush = _zoneBorderBrush;
// 			_zoneLeft.BorderBrush = _zoneBorderBrush;
// 			_zoneRight.BorderBrush = _zoneBorderBrush;
// 			_zoneTop.BorderBrush = _zoneBorderBrush;
// 			_zoneBottom.BorderBrush = _zoneBorderBrush;
// 			_asLeft.BorderBrush = _zoneBorderBrush;
// 			_asRight.BorderBrush = _zoneBorderBrush;
// 			_asTop.BorderBrush = _zoneBorderBrush;
// 			_asBottom.BorderBrush = _zoneBorderBrush;
// 			_mgrLeft.BorderBrush = _zoneBorderBrush;
// 			_mgrRight.BorderBrush = _zoneBorderBrush;
// 			_mgrTop.BorderBrush = _zoneBorderBrush;
// 			_mgrBottom.BorderBrush = _zoneBorderBrush;

// 			_canvas.Visibility = Visibility.Visible;
// 		}
// 		public void Hide()
// 		{
// 			_canvas.Visibility = Visibility.Collapsed;
// 			HoveredZone = CompassZone.None;
// 			_preview.Visibility = Visibility.Collapsed;
// 			RefreshZone(_zoneCenter, false);
// 			RefreshZone(_zoneLeft,   false);
// 			RefreshZone(_zoneRight,  false);
// 			RefreshZone(_zoneTop,    false);
// 			RefreshZone(_zoneBottom, false);
// 			RefreshZone(_asLeft,     false);
// 			RefreshZone(_asRight,    false);
// 			RefreshZone(_asTop,      false);
// 			RefreshZone(_asBottom,   false);
// 			RefreshZone(_mgrLeft,    false);
// 			RefreshZone(_mgrRight,   false);
// 			RefreshZone(_mgrTop,     false);
// 			RefreshZone(_mgrBottom,  false);
// 		}
// 	}
// }
