using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AvalonDock.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AvalonDock.Controls
{
	// Uno-specific OverlayWindow: a Control whose ControlTemplate (in Generic.xaml) matches
	// WPF's PART_ naming so VS2013 and other themes can replace indicator visuals via
	// DataTemplate resource keys (UnoDock_VS2013_Dock*Template).
	//
	// Architecture:
	//   • The control is added to DockingManager's PART_TemplateRoot (a 3×3 Grid) spanning
	//     all cells, invisible by default.
	//   • DragEnter(LayoutFloatingWindowControl) → Visibility = Visible.
	//   • DragEnter(IDropArea)  → show + position the right button group.
	//   • DragEnter(IDropTarget) → highlight the active button + show preview rect.
	//   • DragLeave variants reverse each step.
	//
	// Template in Generic.xaml provides:
	//   PART_DropTargetsContainer  — outer Grid (stretches over the manager)
	//   PART_PreviewBox            — Rectangle, positioned via Margin
	//   PART_DockingManagerDropTargets  — Grid; 4 edge buttons built in OnApplyTemplate
	//   PART_AnchorablePaneDropTargets  — Canvas; compass grid built in OnApplyTemplate
	//   PART_DocumentPaneDropTargets    — Canvas; compass grid built in OnApplyTemplate
	internal sealed class OverlayWindow : Control, IOverlayWindow
	{
		// ── Resource keys (matching VS2013 OverlayButtons.xaml) ────────────────
		private const string KeyWidth  = "UnoDock_VS2013_DockingButtonWidth";
		private const string KeyHeight = "UnoDock_VS2013_DockingButtonHeight";

		private const string KeyMgrLeft   = "UnoDock_VS2013_DockAnchorableLeftTemplate";
		private const string KeyMgrRight  = "UnoDock_VS2013_DockAnchorableRightTemplate";
		private const string KeyMgrTop    = "UnoDock_VS2013_DockAnchorableTopTemplate";
		private const string KeyMgrBottom = "UnoDock_VS2013_DockAnchorableBottomTemplate";

		// Anchorable pane compass uses DockDocumentAsAnchorable* (matches WPF VS2013 Generic.xaml
		// PART_AnchorablePaneDropTargets which uses DockDocumentAsAnchorableLeft/Right/Top/Bottom
		// + DockDocumentInside for the center button).
		private const string KeyAnchLeft   = "UnoDock_VS2013_DockDocumentAsAnchorableLeftTemplate";
		private const string KeyAnchRight  = "UnoDock_VS2013_DockDocumentAsAnchorableRightTemplate";
		private const string KeyAnchTop    = "UnoDock_VS2013_DockDocumentAsAnchorableTopTemplate";
		private const string KeyAnchBottom = "UnoDock_VS2013_DockDocumentAsAnchorableBottomTemplate";
		private const string KeyAnchInside = "UnoDock_VS2013_DockAnchorableInsideTemplate";

		private const string KeyDocLeft   = "UnoDock_VS2013_DockDocumentLeftTemplate";
		private const string KeyDocRight  = "UnoDock_VS2013_DockDocumentRightTemplate";
		private const string KeyDocTop    = "UnoDock_VS2013_DockDocumentTopTemplate";
		private const string KeyDocBottom = "UnoDock_VS2013_DockDocumentBottomTemplate";
		private const string KeyDocInside = "UnoDock_VS2013_DockDocumentInsideTemplate";

		// Outer "as anchorable pane" buttons in the 9-zone full compass
		private const string KeyAsAnchLeft   = "UnoDock_VS2013_DockDocumentAsAnchorableLeftTemplate";
		private const string KeyAsAnchRight  = "UnoDock_VS2013_DockDocumentAsAnchorableRightTemplate";
		private const string KeyAsAnchTop    = "UnoDock_VS2013_DockDocumentAsAnchorableTopTemplate";
		private const string KeyAsAnchBottom = "UnoDock_VS2013_DockDocumentAsAnchorableBottomTemplate";
		private const string KeyPreviewFill  = "UnoDock_VS2013_PreviewBoxBackgroundBrush";
		private const string KeyPreviewStroke = "UnoDock_VS2013_PreviewBoxBorderBrush";

		// ── Drop-logic state ────────────────────────────────────────────────────
		private readonly IOverlayWindowHost _host;
		private readonly List<IDropArea> _visibleAreas = new List<IDropArea>();
		private LayoutFloatingWindowControl _floatingWindow;

		// ── PART_ elements ──────────────────────────────────────────────────────
		private Microsoft.UI.Xaml.Shapes.Path _previewBox;
		private Grid   _mgrGroup;
		private Canvas _anchGroup;
		private Canvas _docGroup;
		private Canvas _tabDropDebugRects;

		// Manager edge buttons (hosted inside _mgrGroup)
		private Border _mgrLeft, _mgrTop, _mgrRight, _mgrBottom;

		// Pane compass buttons (hosted inside a 3×3 Grid inside _anchGroup / _docGroup)
		private Grid _anchCompass, _docCompass;
		private Border _anchLeft, _anchTop, _anchRight, _anchBottom, _anchInto;
		private Border _docLeft,  _docTop,  _docRight,  _docBottom,  _docInto;

		// 9-zone full compass (anchorable drag over document pane): 5×5 Grid
		private Canvas _docFullGroup;
		private Grid   _docFullCompass;
		// Inner 5 buttons (same targets as 5-zone)
		private Border _docFullLeft, _docFullTop, _docFullRight, _docFullBottom, _docFullInto;
		// Outer 4 "as anchorable pane" buttons
		private Border _docAsAnchLeft, _docAsAnchTop, _docAsAnchRight, _docAsAnchBottom;

		// Button size (can be overridden by theme resource)
		private double _btnW = 40, _btnH = 40;
		private const bool ShowTabDropDebugRects = true;

		// Compass backdrop sizes — match WPF VS2013 template measurements.
		private const double CompassBackdropSize      = 122.0; // 5-zone: Path height=122
		private const double FullCompassBackdropWidth = 204.0; // 9-zone: Path width=204

		// Path data fallbacks when theme resources are absent.
		private const string DockPaneEmptyFallback =
			"M 266.388,0.000223796L 277.612,0.000223796L 277.612,7.60856L 280.392,10.3883L 288,10.3883L" +
			" 288,21.6122L 280.392,21.6122L 277.612,24.3919L 277.612,32.0002L 266.388,32.0002L" +
			" 266.388,24.3919L 263.608,21.6122L 256,21.6122L 256,10.3883L 263.608,10.3883L" +
			" 266.388,7.60856L 266.388,0.000223796 Z";
		private const string DockPaneLargeEmptyFallback =
			"M 192,19.4161L 192,12.5843L 202.892,12.5843L 204.584,10.8924L 204.584,0.000223796L" +
			" 211.416,0.000223796L 211.416,10.8924L 213.108,12.5843L 224,12.5843L 224,19.4161L" +
			" 213.108,19.4161L 211.416,21.108L 211.416,32.0002L 204.584,32.0002L 204.584,21.108L" +
			" 202.892,19.4161L 192,19.4161 Z";

		internal OverlayWindow(IOverlayWindowHost host)
		{
			_host = host;
			DefaultStyleKey = typeof(OverlayWindow);
			IsHitTestVisible = false;
			Visibility = Visibility.Collapsed;
		}

		public bool IsHostedInFloatingWindow => false;

		// ── OnApplyTemplate ────────────────────────────────────────────────────

		protected override void OnApplyTemplate()
		{
			base.OnApplyTemplate();

			_btnW = ResolveDouble(KeyWidth,  40);
			_btnH = ResolveDouble(KeyHeight, 40);

			_previewBox = GetTemplateChild("PART_PreviewBox") as Microsoft.UI.Xaml.Shapes.Path;
			_mgrGroup   = GetTemplateChild("PART_DockingManagerDropTargets") as Grid;
			_anchGroup  = GetTemplateChild("PART_AnchorablePaneDropTargets") as Canvas;
			_docGroup   = GetTemplateChild("PART_DocumentPaneDropTargets") as Canvas;
			_tabDropDebugRects = GetTemplateChild("PART_TabDropDebugRects") as Canvas;

			if (_previewBox != null)
			{
				if (TryResolveResource(KeyPreviewFill, out var fill) && fill is Brush fillBrush)
					_previewBox.Fill = fillBrush;
				if (TryResolveResource(KeyPreviewStroke, out var stroke) && stroke is Brush strokeBrush)
					_previewBox.Stroke = strokeBrush;
			}

			if (_mgrGroup != null)
				BuildManagerButtons();
			if (_anchGroup != null)
				_anchCompass = BuildCompassGrid(_anchGroup, anchorable: true);
			if (_docGroup != null)
				_docCompass = BuildCompassGrid(_docGroup, anchorable: false);
			_docFullGroup = GetTemplateChild("PART_DocumentPaneFullDropTargets") as Canvas;
			if (_docFullGroup != null)
				_docFullCompass = BuildFullCompassGrid(_docFullGroup);
		}

		private void BuildManagerButtons()
		{
			_mgrLeft   = MakeButton(KeyMgrLeft,   "◀", HorizontalAlignment.Left,   VerticalAlignment.Center);
			_mgrTop    = MakeButton(KeyMgrTop,    "▲", HorizontalAlignment.Center, VerticalAlignment.Top);
			_mgrRight  = MakeButton(KeyMgrRight,  "▶", HorizontalAlignment.Right,  VerticalAlignment.Center);
			_mgrBottom = MakeButton(KeyMgrBottom, "▼", HorizontalAlignment.Center, VerticalAlignment.Bottom);
			_mgrGroup.Children.Add(_mgrLeft);
			_mgrGroup.Children.Add(_mgrTop);
			_mgrGroup.Children.Add(_mgrRight);
			_mgrGroup.Children.Add(_mgrBottom);
		}

		// Builds the compass group (backdrop + 5 buttons), adds it to the canvas, returns the container.
		// Matches WPF VS2013 Generic.xaml structure:
		//   container Grid
		//     ├─ Path (star backdrop, Height=122, Stretch=Uniform)
		//     └─ Grid HCenter/VCenter (3×3 fixed cells of _btnW × _btnH)
		private Grid BuildCompassGrid(Canvas parent, bool anchorable)
		{
			// ── Star backdrop ──────────────────────────────────────────────────
			var backdropPath = new Microsoft.UI.Xaml.Shapes.Path
			{
				Height = CompassBackdropSize,
				Stretch = Stretch.Uniform,
				IsHitTestVisible = false,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment   = VerticalAlignment.Center,
			};

			// Fill/stroke from theme resources (WPF: DockingButtonStarBackground/BorderBrush)
			const string KeyStarFill   = "UnoDock_VS2013_DockingButtonStarBackgroundBrush";
			const string KeyStarStroke = "UnoDock_VS2013_DockingButtonStarBorderBrush";
			backdropPath.Fill = TryResolveResource(KeyStarFill, out var fillVal) && fillVal is Brush fillBrush
				? fillBrush
				: new SolidColorBrush(Windows.UI.Color.FromArgb(0x20, 0x00, 0x00, 0x00));
			backdropPath.Stroke = TryResolveResource(KeyStarStroke, out var strokeVal) && strokeVal is Brush strokeBrush
				? strokeBrush
				: new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0x80, 0x80, 0x80));
			backdropPath.StrokeThickness = 1;

			// Geometry: try theme resource data string, fall back to embedded constant
			var pathData = DockPaneEmptyFallback;
			if (TryResolveResource("UnoDock_VS2013_DockPaneEmptyGeometryData", out var dataVal) && dataVal is string s)
				pathData = s;
			backdropPath.Data = ParseGeometry(pathData);

			// ── 3×3 button Grid ────────────────────────────────────────────────
			// Each cell = _btnW × _btnH, matching WPF's explicit Width/Height on ContentControls.
			var buttonGrid = new Grid
			{
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment   = VerticalAlignment.Center,
			};
			for (var i = 0; i < 3; i++)
			{
				buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width  = new GridLength(_btnW) });
				buttonGrid.RowDefinitions.Add(   new RowDefinition    { Height = new GridLength(_btnH) });
			}

			Border left, top, right, bottom, into;
			if (anchorable)
			{
				left   = MakeButton(KeyAnchLeft,   "◀");
				top    = MakeButton(KeyAnchTop,    "▲");
				right  = MakeButton(KeyAnchRight,  "▶");
				bottom = MakeButton(KeyAnchBottom, "▼");
				into   = MakeButton(KeyAnchInside, "⊕");
				_anchLeft = left; _anchTop = top; _anchRight = right; _anchBottom = bottom; _anchInto = into;
			}
			else
			{
				left   = MakeButton(KeyDocLeft,   "◀");
				top    = MakeButton(KeyDocTop,    "▲");
				right  = MakeButton(KeyDocRight,  "▶");
				bottom = MakeButton(KeyDocBottom, "▼");
				into   = MakeButton(KeyDocInside, "⊕");
				_docLeft = left; _docTop = top; _docRight = right; _docBottom = bottom; _docInto = into;
			}

			Grid.SetRow(top,    0); Grid.SetColumn(top,    1);
			Grid.SetRow(left,   1); Grid.SetColumn(left,   0);
			Grid.SetRow(into,   1); Grid.SetColumn(into,   1);
			Grid.SetRow(right,  1); Grid.SetColumn(right,  2);
			Grid.SetRow(bottom, 2); Grid.SetColumn(bottom, 1);
			buttonGrid.Children.Add(top);
			buttonGrid.Children.Add(left);
			buttonGrid.Children.Add(into);
			buttonGrid.Children.Add(right);
			buttonGrid.Children.Add(bottom);

			// ── Container Grid (backdrop + buttons, same structure as WPF template) ──
			var container = new Grid();
			container.Children.Add(backdropPath);
			container.Children.Add(buttonGrid);

			parent.Children.Add(container);
			return container;
		}

		// Builds the 9-zone full compass (anchorable drag over document pane).
		// 5×5 grid: inner 5 (rows 1-3, cols 1-3) + outer 4 (rows 0/4, cols 0/4).
		// Backdrop: DockPaneLargeEmpty (Width=204).
		private Grid BuildFullCompassGrid(Canvas parent)
		{
			// ── Large star backdrop ─────────────────────────────────────────────
			var backdropPath = new Microsoft.UI.Xaml.Shapes.Path
			{
				Width  = FullCompassBackdropWidth,
				Stretch = Stretch.Uniform,
				IsHitTestVisible = false,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment   = VerticalAlignment.Center,
			};
			const string KeyStarFill   = "UnoDock_VS2013_DockingButtonStarBackgroundBrush";
			const string KeyStarStroke = "UnoDock_VS2013_DockingButtonStarBorderBrush";
			backdropPath.Fill   = TryResolveResource(KeyStarFill,   out var f) && f is Brush fb ? fb
				: new SolidColorBrush(Windows.UI.Color.FromArgb(0x20, 0x00, 0x00, 0x00));
			backdropPath.Stroke = TryResolveResource(KeyStarStroke, out var s) && s is Brush sb ? sb
				: new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0x80, 0x80, 0x80));
			backdropPath.StrokeThickness = 1;
			var pathData = DockPaneLargeEmptyFallback;
			if (TryResolveResource("UnoDock_VS2013_DockPaneLargeEmptyGeometryData", out var dv) && dv is string sd)
				pathData = sd;
			backdropPath.Data = ParseGeometry(pathData);

			// ── 5×5 button Grid ─────────────────────────────────────────────────
			var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
			for (var i = 0; i < 5; i++)
			{
				grid.ColumnDefinitions.Add(new ColumnDefinition { Width  = new GridLength(_btnW) });
				grid.RowDefinitions.Add(   new RowDefinition    { Height = new GridLength(_btnH) });
			}

			// Inner 5 buttons (rows 1-3, cols 1-3)
			_docFullTop    = MakeButton(KeyDocTop,    "▲"); Grid.SetRow(_docFullTop,    1); Grid.SetColumn(_docFullTop,    2);
			_docFullLeft   = MakeButton(KeyDocLeft,   "◀"); Grid.SetRow(_docFullLeft,   2); Grid.SetColumn(_docFullLeft,   1);
			_docFullInto   = MakeButton(KeyDocInside, "⊕"); Grid.SetRow(_docFullInto,   2); Grid.SetColumn(_docFullInto,   2);
			_docFullRight  = MakeButton(KeyDocRight,  "▶"); Grid.SetRow(_docFullRight,  2); Grid.SetColumn(_docFullRight,  3);
			_docFullBottom = MakeButton(KeyDocBottom, "▼"); Grid.SetRow(_docFullBottom, 3); Grid.SetColumn(_docFullBottom, 2);

			// Outer 4 "as anchorable pane" buttons (rows 0/4, cols 0/4)
			_docAsAnchTop    = MakeButton(KeyAsAnchTop,    "▲"); Grid.SetRow(_docAsAnchTop,    0); Grid.SetColumn(_docAsAnchTop,    2);
			_docAsAnchLeft   = MakeButton(KeyAsAnchLeft,   "◀"); Grid.SetRow(_docAsAnchLeft,   2); Grid.SetColumn(_docAsAnchLeft,   0);
			_docAsAnchRight  = MakeButton(KeyAsAnchRight,  "▶"); Grid.SetRow(_docAsAnchRight,  2); Grid.SetColumn(_docAsAnchRight,  4);
			_docAsAnchBottom = MakeButton(KeyAsAnchBottom, "▼"); Grid.SetRow(_docAsAnchBottom, 4); Grid.SetColumn(_docAsAnchBottom, 2);

			foreach (var btn in new[] { _docFullTop, _docFullLeft, _docFullInto, _docFullRight, _docFullBottom,
			                            _docAsAnchTop, _docAsAnchLeft, _docAsAnchRight, _docAsAnchBottom })
				grid.Children.Add(btn);

			var container = new Grid();
			container.Children.Add(backdropPath);
			container.Children.Add(grid);
			parent.Children.Add(container);
			return container;
		}

		// Parse a WPF mini-path data string into a WinUI Geometry.
		// Loads a full <Path> element via XamlReader and extracts its Data property,
		// which works in both WinUI and Uno because Path.Data uses the standard
		// mini-language type converter (unlike creating PathGeometry directly).
		private static Microsoft.UI.Xaml.Media.Geometry ParseGeometry(string data)
		{
			try
			{
				var escaped = data.Replace("&", "&amp;").Replace("\"", "&quot;");
				var path = (Microsoft.UI.Xaml.Shapes.Path)
					Microsoft.UI.Xaml.Markup.XamlReader.Load(
						$"<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"" +
						$" Data=\"{escaped}\" />");
				return path?.Data;
			}
			catch { return null; }
		}

		// Overload for compass buttons (inside fixed-size grid cells — no explicit alignment needed).
		private Border MakeButton(string templateKey, string fallbackGlyph)
			=> MakeButton(templateKey, fallbackGlyph, HorizontalAlignment.Stretch, VerticalAlignment.Stretch);

		private Border MakeButton(string templateKey, string fallbackGlyph,
			HorizontalAlignment ha, VerticalAlignment va)
		{
			var hasTemplate = TryResolveResource(templateKey, out var res) && res is DataTemplate;

			// When a VS2013 DataTemplate exists, keep the button background transparent so the
			// template content controls all styling (matches WPF ContentControl Background=Transparent).
			// When falling back to a glyph, show the default semi-transparent gray background.
			var btn = new Border
			{
				Width  = _btnW,
				Height = _btnH,
				CornerRadius = new CornerRadius(4),
				Background = hasTemplate
					? new SolidColorBrush(Microsoft.UI.Colors.Transparent)
					: new SolidColorBrush(Windows.UI.Color.FromArgb(0x30, 0x80, 0x80, 0x80)),
				BorderBrush = hasTemplate
					? new SolidColorBrush(Microsoft.UI.Colors.Transparent)
					: new SolidColorBrush(Windows.UI.Color.FromArgb(0x60, 0x80, 0x80, 0x80)),
				BorderThickness = new Thickness(1),
				Opacity = 0.9,
				HorizontalAlignment = ha,
				VerticalAlignment = va,
				// Tag = true → has VS template (hover: blue overlay; normal: transparent)
				// Tag = null → fallback glyph  (hover: blue;          normal: gray)
				Tag = hasTemplate ? (object)true : null,
				Child = LoadButtonContent(templateKey, fallbackGlyph),
			};
			return btn;
		}

		private UIElement LoadButtonContent(string key, string fallback)
		{
			if (TryResolveResource(key, out var value) && value is DataTemplate dt)
			{
				// Use ContentPresenter so the DataTemplate is expanded lazily after the element
				// enters the visual tree, letting {ThemeResource} bindings resolve correctly.
				// (Calling dt.LoadContent() here would create elements outside the tree and
				// leave all ThemeResource brushes unresolved → wrong/invisible icons.)
				return new ContentPresenter
				{
					ContentTemplate            = dt,
					HorizontalAlignment        = HorizontalAlignment.Stretch,
					VerticalAlignment          = VerticalAlignment.Stretch,
					HorizontalContentAlignment = HorizontalAlignment.Stretch,
					VerticalContentAlignment   = VerticalAlignment.Stretch,
				};
			}
			return new TextBlock
			{
				Text = fallback,
				FontSize = 18,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment   = VerticalAlignment.Center,
			};
		}

		// ── IOverlayWindow ─────────────────────────────────────────────────────

		IEnumerable<IDropTarget> IOverlayWindow.GetTargets()
		{
			if (_floatingWindow?.Model is not LayoutFloatingWindow floatingModel)
			{
				ClearTabDropDebugRects();
				yield break;
			}

			var targets = new List<IDropTarget>();
			foreach (var area in _visibleAreas)
			{
				foreach (var target in BuildTargets(area, floatingModel))
					targets.Add(target);
			}

			UpdateTabDropDebugRects(targets.OfType<UnoOverlayDropTarget>());
			foreach (var target in targets)
				yield return target;
		}

		/// <summary>Diagnostic helper: show the overlay with the given drop area active,
		/// without needing a real floating window drag in progress.</summary>
		internal void ShowForDiagnostics(IDropArea area)
		{
			Visibility = Visibility.Visible;
			((IOverlayWindow)this).DragEnter(area);
		}

		/// <summary>
		/// Walks the overlay's visual tree and returns the actual rendered size of every
		/// FrameworkElement that has a non-zero size, grouped by depth.
		/// Useful for diagnosing button/compass layout without needing pixel screenshots.
		/// </summary>
		internal string MeasureVisualTree()
		{
			var sb = new System.Text.StringBuilder();
			DumpElement(this, 0, sb);
			return sb.Length > 0 ? sb.ToString() : "empty";
		}

		private static void DumpElement(DependencyObject node, int depth, System.Text.StringBuilder sb)
		{
			if (node is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
			{
				var tag = fe is Border b && b.Tag != null ? $"[tag={b.Tag}]" : "";
				sb.AppendLine($"{new string(' ', depth * 2)}{fe.GetType().Name}{tag} {fe.ActualWidth:F0}x{fe.ActualHeight:F0}");
			}
			var count = VisualTreeHelper.GetChildrenCount(node);
			for (var i = 0; i < count; i++)
				DumpElement(VisualTreeHelper.GetChild(node, i), depth + 1, sb);
		}

		void IOverlayWindow.DragEnter(LayoutFloatingWindowControl floatingWindow)
		{
			_floatingWindow = floatingWindow;
			Visibility = Visibility.Visible;
		}

		void IOverlayWindow.DragLeave(LayoutFloatingWindowControl floatingWindow)
		{
			if (ReferenceEquals(_floatingWindow, floatingWindow))
				_floatingWindow = null;
			_visibleAreas.Clear();
			HideAllGroups();
			HidePreview();
			ClearTabDropDebugRects();
			Visibility = Visibility.Collapsed;
		}

		void IOverlayWindow.DragEnter(IDropArea area)
		{
			if (area == null || _visibleAreas.Contains(area)) return;
			_visibleAreas.Add(area);
			ShowGroupForArea(area);
		}

		void IOverlayWindow.DragLeave(IDropArea area)
		{
			if (area == null) return;
			_visibleAreas.Remove(area);
			HideGroupForArea(area);
			if (_visibleAreas.Count == 0)
				ClearTabDropDebugRects();
		}

		void IOverlayWindow.DragEnter(IDropTarget target)
		{
			HighlightTarget(target, hover: true);
			if (target is UnoOverlayDropTarget unoTarget)
				ShowPreviewForTarget(unoTarget);
		}

		void IOverlayWindow.DragLeave(IDropTarget _)
		{
			UnhighlightAll();
			HidePreview();
		}

		void IOverlayWindow.DragDrop(IDropTarget target)
		{
			if (_floatingWindow?.Model is LayoutFloatingWindow floatingModel && target != null)
				target.Drop(floatingModel);
		}

		// ── Visual helpers ─────────────────────────────────────────────────────

		private bool IsAnchorableDrag => _floatingWindow?.Model is LayoutAnchorableFloatingWindow;

		private void ShowGroupForArea(IDropArea area)
		{
			var origin = GetManagerOriginInRoot();
			if (!origin.HasValue) return;

			switch (area.Type)
			{
				case DropAreaType.DockingManager:
					if (_mgrGroup != null) _mgrGroup.Visibility = Visibility.Visible;
					break;

				case DropAreaType.AnchorablePane:
					ShowCompassGroup(_anchGroup, _anchCompass, area.DetectionRect, origin.Value);
					break;

				case DropAreaType.DocumentPane:
				case DropAreaType.DocumentPaneGroup:
					// Anchorable drag over doc pane → 9-zone full compass; document drag → 5-zone.
					if (IsAnchorableDrag && _docFullGroup != null)
						ShowCompassGroup(_docFullGroup, _docFullCompass, area.DetectionRect, origin.Value, FullCompassBackdropWidth);
					else
						ShowCompassGroup(_docGroup, _docCompass, area.DetectionRect, origin.Value);
					break;
			}
		}

		private void HideGroupForArea(IDropArea area)
		{
			switch (area.Type)
			{
				case DropAreaType.DockingManager:
					if (_mgrGroup != null) _mgrGroup.Visibility = Visibility.Collapsed;
					break;
				case DropAreaType.AnchorablePane:
					if (_anchGroup != null) _anchGroup.Visibility = Visibility.Collapsed;
					break;
				case DropAreaType.DocumentPane:
				case DropAreaType.DocumentPaneGroup:
					if (_docGroup     != null) _docGroup.Visibility     = Visibility.Collapsed;
					if (_docFullGroup != null) _docFullGroup.Visibility = Visibility.Collapsed;
					break;
			}
		}

		private void HideAllGroups()
		{
			if (_mgrGroup     != null) _mgrGroup.Visibility     = Visibility.Collapsed;
			if (_anchGroup    != null) _anchGroup.Visibility    = Visibility.Collapsed;
			if (_docGroup     != null) _docGroup.Visibility     = Visibility.Collapsed;
			if (_docFullGroup != null) _docFullGroup.Visibility = Visibility.Collapsed;
		}

		private static void ShowCompassGroup(Canvas group, Grid container,
			Windows.Foundation.Rect screenRect, Windows.Foundation.Point origin,
			double backdropSize = CompassBackdropSize)
		{
			if (group == null || container == null) return;

			var localX = screenRect.X - origin.X;
			var localY = screenRect.Y - origin.Y;
			var cx = localX + screenRect.Width  / 2 - backdropSize / 2;
			var cy = localY + screenRect.Height / 2 - backdropSize / 2;

			Canvas.SetLeft(container, cx);
			Canvas.SetTop(container,  cy);
			group.Visibility = Visibility.Visible;
		}

		private void ShowPreviewForTarget(UnoOverlayDropTarget target)
		{
			if (target == null) { HidePreview(); return; }

			if (target.ShouldUseTabPreviewShape)
				ShowTabPreviewAt(target.PreviewRect, target.DetectionRect);
			else
				ShowRectPreviewAt(target.PreviewRect);
		}

		private void ShowRectPreviewAt(Windows.Foundation.Rect screenRect)
		{
			if (_previewBox == null) return;
			var origin = GetManagerOriginInRoot();
			if (!origin.HasValue) { HidePreview(); return; }

			var rect = ToLocalRect(screenRect, origin.Value);
			_previewBox.Margin = new Thickness(0);
			_previewBox.Width = double.NaN;
			_previewBox.Height = double.NaN;
			_previewBox.Data = ParseGeometry(RectPath(rect));
			_previewBox.Visibility = Visibility.Visible;
		}

		private void ShowTabPreviewAt(Windows.Foundation.Rect paneScreenRect, Windows.Foundation.Rect tabScreenRect)
		{
			if (_previewBox == null) return;
			var origin = GetManagerOriginInRoot();
			if (!origin.HasValue) { HidePreview(); return; }

			var pane = ToLocalRect(paneScreenRect, origin.Value);
			var tab = ToLocalRect(tabScreenRect, origin.Value);

			var data = string.Format(CultureInfo.InvariantCulture,
				"M {0},{1} L {0},{2} L {3},{2} L {3},{4} L {5},{4} L {5},{2} L {6},{2} L {6},{1} Z",
				pane.Right, pane.Bottom, tab.Bottom, tab.Right, tab.Top, tab.Left, pane.Left);

			_previewBox.Margin = new Thickness(0);
			_previewBox.Width = double.NaN;
			_previewBox.Height = double.NaN;
			_previewBox.Data = ParseGeometry(data);
			_previewBox.Visibility = Visibility.Visible;
		}

		private void HidePreview()
		{
			if (_previewBox != null) _previewBox.Visibility = Visibility.Collapsed;
		}

		private void UpdateTabDropDebugRects(IEnumerable<UnoOverlayDropTarget> targets)
		{
			if (!ShowTabDropDebugRects || _tabDropDebugRects == null)
				return;

			var origin = GetManagerOriginInRoot();
			if (!origin.HasValue)
			{
				ClearTabDropDebugRects();
				return;
			}

			_tabDropDebugRects.Children.Clear();
			foreach (var target in targets.Where(t => t.ShouldUseTabPreviewShape))
			{
				var rect = ToLocalRect(target.DetectionRect, origin.Value);
				if (rect.Width <= 0 || rect.Height <= 0)
					continue;

				var outline = new Microsoft.UI.Xaml.Shapes.Rectangle
				{
					Width = rect.Width,
					Height = rect.Height,
					Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x00, 0x00)),
					StrokeThickness = 2,
					Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0x22, 0xFF, 0x00, 0x00)),
					IsHitTestVisible = false,
				};

				Canvas.SetLeft(outline, rect.Left);
				Canvas.SetTop(outline, rect.Top);
				_tabDropDebugRects.Children.Add(outline);
			}

			_tabDropDebugRects.Visibility = _tabDropDebugRects.Children.Count > 0
				? Visibility.Visible
				: Visibility.Collapsed;
		}

		private void ClearTabDropDebugRects()
		{
			if (_tabDropDebugRects == null)
				return;

			_tabDropDebugRects.Children.Clear();
			_tabDropDebugRects.Visibility = Visibility.Collapsed;
		}

		private static Windows.Foundation.Rect ToLocalRect(
			Windows.Foundation.Rect screenRect, Windows.Foundation.Point origin)
			=> new Windows.Foundation.Rect(
				screenRect.X - origin.X,
				screenRect.Y - origin.Y,
				Math.Max(0, screenRect.Width),
				Math.Max(0, screenRect.Height));

		private static string RectPath(Windows.Foundation.Rect rect)
			=> string.Format(CultureInfo.InvariantCulture,
				"M {0},{1} L {2},{1} L {2},{3} L {0},{3} Z",
				rect.Left, rect.Top, rect.Right, rect.Bottom);

		private static readonly Windows.UI.Color HoverFill  = Windows.UI.Color.FromArgb(0x60, 0x00, 0x7A, 0xCC);
		private static readonly Windows.UI.Color GlyphFill  = Windows.UI.Color.FromArgb(0x30, 0x80, 0x80, 0x80);

		private void HighlightTarget(IDropTarget target, bool hover)
		{
			if (target == null) return;
			var btn = ResolveButtonForTarget(target.Type);
			if (btn != null)
				btn.Background = hover
					? new SolidColorBrush(HoverFill)
					: NormalBackground(btn);
		}

		private void UnhighlightAll()
		{
			foreach (var btn in AllButtons())
				if (btn != null) btn.Background = NormalBackground(btn);
		}

		// Normal background depends on whether the button uses a VS template (transparent) or glyph fallback (gray).
		private static Brush NormalBackground(Border btn)
			=> btn.Tag is true
				? new SolidColorBrush(Microsoft.UI.Colors.Transparent)
				: new SolidColorBrush(GlyphFill);

		private Border ResolveButtonForTarget(DropTargetType type) => type switch
		{
			DropTargetType.DockingManagerDockLeft     => _mgrLeft,
			DropTargetType.DockingManagerDockTop      => _mgrTop,
			DropTargetType.DockingManagerDockRight    => _mgrRight,
			DropTargetType.DockingManagerDockBottom   => _mgrBottom,
			DropTargetType.AnchorablePaneDockLeft     => _anchLeft,
			DropTargetType.AnchorablePaneDockTop      => _anchTop,
			DropTargetType.AnchorablePaneDockRight    => _anchRight,
			DropTargetType.AnchorablePaneDockBottom   => _anchBottom,
			DropTargetType.AnchorablePaneDockInside   => _anchInto,
			DropTargetType.DocumentPaneDockLeft       => IsAnchorableDrag ? _docFullLeft   : _docLeft,
			DropTargetType.DocumentPaneDockTop        => IsAnchorableDrag ? _docFullTop    : _docTop,
			DropTargetType.DocumentPaneDockRight      => IsAnchorableDrag ? _docFullRight  : _docRight,
			DropTargetType.DocumentPaneDockBottom     => IsAnchorableDrag ? _docFullBottom : _docBottom,
			DropTargetType.DocumentPaneDockInside     => IsAnchorableDrag ? _docFullInto   : _docInto,
			DropTargetType.DocumentPaneGroupDockInside => _docInto,
			DropTargetType.DocumentPaneDockAsAnchorableLeft   => _docAsAnchLeft,
			DropTargetType.DocumentPaneDockAsAnchorableTop    => _docAsAnchTop,
			DropTargetType.DocumentPaneDockAsAnchorableRight  => _docAsAnchRight,
			DropTargetType.DocumentPaneDockAsAnchorableBottom => _docAsAnchBottom,
			_ => null,
		};

		private IEnumerable<Border> AllButtons()
		{
			yield return _mgrLeft;  yield return _mgrTop;
			yield return _mgrRight; yield return _mgrBottom;
			yield return _anchLeft; yield return _anchTop;
			yield return _anchRight; yield return _anchBottom; yield return _anchInto;
			yield return _docLeft;  yield return _docTop;
			yield return _docRight; yield return _docBottom;   yield return _docInto;
			yield return _docFullLeft;  yield return _docFullTop;
			yield return _docFullRight; yield return _docFullBottom; yield return _docFullInto;
			yield return _docAsAnchLeft; yield return _docAsAnchTop;
			yield return _docAsAnchRight; yield return _docAsAnchBottom;
		}

		private Windows.Foundation.Point? GetManagerOriginInRoot()
		{
			if (_host?.Manager == null) return null;
			try
			{
				return _host.Manager.TransformToVisual(null)
					.TransformPoint(new Windows.Foundation.Point(0, 0));
			}
			catch { return null; }
		}

		// ── Resource helpers ───────────────────────────────────────────────────

		private double ResolveDouble(string key, double fallback)
		{
			if (TryResolveResource(key, out var v))
			{
				if (v is double d) return d;
				if (v is float  f) return f;
				if (v is int    i) return i;
			}
			return fallback;
		}

		private bool TryResolveResource(string key, out object value)
		{
			// Walk visual tree (this control → parent → … → app)
			DependencyObject current = this;
			while (current != null)
			{
				if (current is FrameworkElement fe && fe.Resources != null &&
					fe.Resources.TryGetValue(key, out var scoped))
				{
					value = scoped;
					return true;
				}
				current = VisualTreeHelper.GetParent(current);
			}
			var app = Application.Current?.Resources;
			if (app != null && app.TryGetValue(key, out var appVal))
			{
				value = appVal;
				return true;
			}
			value = null;
			return false;
		}

		// ── Drop-logic (unchanged) ─────────────────────────────────────────────

		private IEnumerable<IDropTarget> BuildTargets(IDropArea area, LayoutFloatingWindow floatingModel)
		{
			if (area == null) yield break;
			var rect = area.DetectionRect;
			if (rect.Width <= 0 || rect.Height <= 0) yield break;

			switch (area.Type)
			{
				case DropAreaType.DockingManager:
					foreach (var t in BuildDockingManagerTargets(area, rect, floatingModel)) yield return t;
					break;
				case DropAreaType.DocumentPane:
				case DropAreaType.AnchorablePane:
				{
					var targetPane = ResolveTargetPane(area);
					if (targetPane is not ILayoutPositionableElement positionable ||
						!OverlayDropRules.ShouldShowDropTargetInto(positionable, floatingModel))
						yield break;
					foreach (var t in BuildPaneTargets(area, rect, targetPane, floatingModel)) yield return t;
					break;
				}
				case DropAreaType.DocumentPaneGroup:
				{
					var repPane = ResolveRepresentativeDocumentPane(area);
					if (repPane == null ||
						!OverlayDropRules.ShouldShowDropTargetInto(repPane, floatingModel) ||
						floatingModel is LayoutAnchorableFloatingWindow)
						yield break;
					yield return new UnoOverlayDropTarget(this, area, DropTargetType.DocumentPaneGroupDockInside, rect, rect, null);
					break;
				}
			}
		}

		private IEnumerable<IDropTarget> BuildDockingManagerTargets(IDropArea area, Windows.Foundation.Rect rect,
			LayoutFloatingWindow floatingModel)
		{
			// WPF uses the button's GetScreenArea() as the detection rect.
			// The button is _btnW × _btnH (default 40×40), placed at the center of each edge
			// with a small inset margin — matching where PART_DockingManagerDropTarget* renders.
			const double EdgeMargin = 10.0;
			var bw = _btnW;
			var bh = _btnH;
			var cx = rect.Left + rect.Width  / 2.0 - bw / 2.0;
			var cy = rect.Top  + rect.Height / 2.0 - bh / 2.0;

			var preferredW = GetPreferredDockWidth(floatingModel,  rect.Width  / 2.0);
			var preferredH = GetPreferredDockHeight(floatingModel, rect.Height / 2.0);

			foreach (var type in new[]
			{
				DropTargetType.DockingManagerDockLeft,
				DropTargetType.DockingManagerDockTop,
				DropTargetType.DockingManagerDockRight,
				DropTargetType.DockingManagerDockBottom,
			})
			{
				var preferred = (type == DropTargetType.DockingManagerDockLeft ||
				                 type == DropTargetType.DockingManagerDockRight)
					? preferredW : preferredH;

				OverlayPreviewRules.TryComputeManagerPreviewRect(
					type, rect.Width, rect.Height, preferred,
					out var pl, out var pt, out var pw, out var ph);

				// Detection rect = button-sized zone at each edge center (matches button's GetScreenArea in WPF).
				var detectionRect = type switch
				{
					DropTargetType.DockingManagerDockLeft   => new Windows.Foundation.Rect(rect.Left  + EdgeMargin,         cy, bw, bh),
					DropTargetType.DockingManagerDockTop    => new Windows.Foundation.Rect(cx,  rect.Top   + EdgeMargin,    bw, bh),
					DropTargetType.DockingManagerDockRight  => new Windows.Foundation.Rect(rect.Right  - EdgeMargin - bw,   cy, bw, bh),
					_                                       => new Windows.Foundation.Rect(cx,  rect.Bottom - EdgeMargin - bh, bw, bh),
				};
				var previewRect = new Windows.Foundation.Rect(rect.Left + pl, rect.Top + pt, pw, ph);

				yield return new UnoOverlayDropTarget(this, area, type, detectionRect, previewRect, null);
			}
		}

		// Preferred dock size helpers (mirrors WPF DockingManagerDropTarget.GetPreviewPath).
		private static double GetPreferredDockWidth(LayoutFloatingWindow fw, double fallback)
		{
			var pane  = (fw as LayoutAnchorableFloatingWindow)?.RootPanel as ILayoutPositionableElement;
			if (pane?.DockWidth.IsAbsolute == true) return pane.DockWidth.Value;
			var sized = (fw as LayoutAnchorableFloatingWindow)?.RootPanel as ILayoutPositionableElementWithActualSize;
			if (sized?.ActualWidth > 0) return sized.ActualWidth;
			return fallback;
		}

		private static double GetPreferredDockHeight(LayoutFloatingWindow fw, double fallback)
		{
			var pane  = (fw as LayoutAnchorableFloatingWindow)?.RootPanel as ILayoutPositionableElement;
			if (pane?.DockHeight.IsAbsolute == true) return pane.DockHeight.Value;
			var sized = (fw as LayoutAnchorableFloatingWindow)?.RootPanel as ILayoutPositionableElementWithActualSize;
			if (sized?.ActualHeight > 0) return sized.ActualHeight;
			return fallback;
		}

		private IEnumerable<IDropTarget> BuildPaneTargets(IDropArea area, Windows.Foundation.Rect rect,
			ILayoutPanelElement targetPane, LayoutFloatingWindow floatingModel)
		{
			var canInside = CanDropInside(targetPane, floatingModel);
			var isAnch = area.Type == DropAreaType.AnchorablePane;
			// When dragging anchorable over document pane → use 9-zone full compass.
			var isAnchOverDoc = !isAnch && floatingModel is LayoutAnchorableFloatingWindow;

			// Detection rects come from the ACTUAL rendered button positions (GetScreenArea),
			// exactly like WPF's OverlayWindow.GetTargets — never computed from formulas. This
			// guarantees the hit zone always matches what the user sees, regardless of layout.
			//
			// On Windows the OverlayWindow control is off-screen (parked at -32000,-32000) so
			// button.GetScreenArea() returns off-screen-window coordinates. The drag-point used
			// for hit-testing is in main-window coordinates: add back GetManagerOriginInRoot()
			// to translate canvas-relative -> main-window. On macOS the render source is a
			// clipped child in the main visual tree, so GetScreenArea() is already in the same
			// root coordinate space as dragPoint and must not be offset again.
			var buttonOffset = OperatingSystem.IsWindows()
				? GetManagerOriginInRoot() ?? new Windows.Foundation.Point(0, 0)
				: new Windows.Foundation.Point(0, 0);

			Windows.Foundation.Rect ButtonDet(Border button)
			{
				var r = button.GetScreenArea();
				return new Windows.Foundation.Rect(r.X + buttonOffset.X, r.Y + buttonOffset.Y, r.Width, r.Height);
			}

			UnoOverlayDropTarget Target(DropTargetType type, Border button, DropTargetType previewType)
			{
				var det = ButtonDet(button);
				OverlayPreviewRules.TryComputePanePreviewRect(previewType, rect.Width, rect.Height,
					out var pl, out var pt, out var pw, out var ph);
				return new UnoOverlayDropTarget(this, area, type, det,
					new Windows.Foundation.Rect(rect.Left + pl, rect.Top + pt, pw, ph), null);
			}

			UnoOverlayDropTarget TargetAsManagerOuter(DropTargetType type, Border button, DropTargetType managerType)
			{
				var det = ButtonDet(button);
				var managerRect = _host?.Manager?.GetScreenArea() ?? rect;
				if (managerRect.Width <= 0 || managerRect.Height <= 0)
					managerRect = rect;

				var preferred = (managerType == DropTargetType.DockingManagerDockLeft ||
					managerType == DropTargetType.DockingManagerDockRight)
					? GetPreferredDockWidth(floatingModel, managerRect.Width / 2.0)
					: GetPreferredDockHeight(floatingModel, managerRect.Height / 2.0);

				OverlayPreviewRules.TryComputeManagerPreviewRect(
					managerType,
					managerRect.Width,
					managerRect.Height,
					preferred,
					out var pl,
					out var pt,
					out var pw,
					out var ph);

				return new UnoOverlayDropTarget(
					this,
					area,
					type,
					det,
					new Windows.Foundation.Rect(managerRect.Left + pl, managerRect.Top + pt, pw, ph),
					null);
			}

			if (isAnchOverDoc)
			{
				// Inner 5: split/join the document pane (DockDocument*)
				yield return Target(DropTargetType.DocumentPaneDockLeft,   _docFullLeft,   DropTargetType.DocumentPaneDockLeft);
				yield return Target(DropTargetType.DocumentPaneDockTop,    _docFullTop,    DropTargetType.DocumentPaneDockTop);
				yield return Target(DropTargetType.DocumentPaneDockRight,  _docFullRight,  DropTargetType.DocumentPaneDockRight);
				yield return Target(DropTargetType.DocumentPaneDockBottom, _docFullBottom, DropTargetType.DocumentPaneDockBottom);
				if (canInside)
					yield return Target(DropTargetType.DocumentPaneDockInside, _docFullInto, DropTargetType.DocumentPaneDockInside);

				// Outer 4 "as anchorable pane": align with manager outer-edge behavior
				// for both action and preview geometry.
				yield return TargetAsManagerOuter(DropTargetType.DocumentPaneDockAsAnchorableLeft,   _docAsAnchLeft,   DropTargetType.DockingManagerDockLeft);
				yield return TargetAsManagerOuter(DropTargetType.DocumentPaneDockAsAnchorableTop,    _docAsAnchTop,    DropTargetType.DockingManagerDockTop);
				yield return TargetAsManagerOuter(DropTargetType.DocumentPaneDockAsAnchorableRight,  _docAsAnchRight,  DropTargetType.DockingManagerDockRight);
				yield return TargetAsManagerOuter(DropTargetType.DocumentPaneDockAsAnchorableBottom, _docAsAnchBottom, DropTargetType.DockingManagerDockBottom);
				yield break;
			}

			// Standard 5-zone compass (document drag over doc pane, or anchorable pane)
			if (isAnch)
			{
				yield return Target(DropTargetType.AnchorablePaneDockLeft,   _anchLeft,   DropTargetType.AnchorablePaneDockLeft);
				yield return Target(DropTargetType.AnchorablePaneDockTop,    _anchTop,    DropTargetType.AnchorablePaneDockTop);
				yield return Target(DropTargetType.AnchorablePaneDockRight,  _anchRight,  DropTargetType.AnchorablePaneDockRight);
				yield return Target(DropTargetType.AnchorablePaneDockBottom, _anchBottom, DropTargetType.AnchorablePaneDockBottom);
				if (canInside)
				{
					yield return Target(DropTargetType.AnchorablePaneDockInside, _anchInto, DropTargetType.AnchorablePaneDockInside);
					foreach (var t in BuildPaneTabTargets(area, rect, targetPane)) yield return t;
				}
			}
			else
			{
				yield return Target(DropTargetType.DocumentPaneDockLeft,   _docLeft,   DropTargetType.DocumentPaneDockLeft);
				yield return Target(DropTargetType.DocumentPaneDockTop,    _docTop,    DropTargetType.DocumentPaneDockTop);
				yield return Target(DropTargetType.DocumentPaneDockRight,  _docRight,  DropTargetType.DocumentPaneDockRight);
				yield return Target(DropTargetType.DocumentPaneDockBottom, _docBottom, DropTargetType.DocumentPaneDockBottom);
				if (canInside)
				{
					yield return Target(DropTargetType.DocumentPaneDockInside, _docInto, DropTargetType.DocumentPaneDockInside);
					foreach (var t in BuildPaneTabTargets(area, rect, targetPane)) yield return t;
				}
			}
		}

		private IEnumerable<IDropTarget> BuildPaneTabTargets(IDropArea area,
			Windows.Foundation.Rect paneRect, ILayoutPanelElement targetPane)
		{
			if (area is not OverlayDropArea overlayArea) yield break;
			var paneControl = overlayArea.AreaElement;
			if (paneControl == null) yield break;

			var tabStrip = EnumerateVisualsOfType<FrameworkElement>(paneControl)
				.FirstOrDefault(fe => fe.Name == "PART_TabStrip");
			if (tabStrip == null) yield break;

			var tabStripHost = EnumerateVisualsOfType<FrameworkElement>(paneControl)
				.FirstOrDefault(fe => fe.Name == "PART_TabStripHost") ?? tabStrip;
			var tabStripHostRect = tabStripHost.GetScreenArea();
			if (tabStripHostRect.Width <= 0 || tabStripHostRect.Height <= 0) yield break;

#if !WINDOWS
			// macOS diagnostic: log key rects to help diagnose tab-header drop-zone Y offset.
			DockingManager.DragLogW(
				$"[TabTargets] paneRect=({paneRect.Left:F1},{paneRect.Top:F1} {paneRect.Width:F1}x{paneRect.Height:F1}) " +
				$"tabStripHostRect=({tabStripHostRect.Left:F1},{tabStripHostRect.Top:F1} {tabStripHostRect.Width:F1}x{tabStripHostRect.Height:F1}) " +
				$"paneControl={paneControl.GetType().Name} w={paneControl.ActualWidth:F1} h={paneControl.ActualHeight:F1}");
			var paneControlOrigin = paneControl.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
			DockingManager.DragLogW(
				$"[TabTargets] paneControl.TransformToVisual(null)=({paneControlOrigin.X:F1},{paneControlOrigin.Y:F1}) " +
				$"tabStrip.w={tabStrip.ActualWidth:F1} tabStrip.h={tabStrip.ActualHeight:F1}");
#endif

			var insideType = area.Type == DropAreaType.AnchorablePane
				? DropTargetType.AnchorablePaneDockInside
				: DropTargetType.DocumentPaneDockInside;

			if (area.Type == DropAreaType.AnchorablePane)
			{
				foreach (var t in BuildTabTargetsForContent<LayoutAnchorable>(
					area, targetPane, tabStrip, tabStripHostRect, paneRect, insideType, c => c.ContentId))
					yield return t;
			}
			else
			{
				foreach (var t in BuildTabTargetsForContent<LayoutDocument>(
					area, targetPane, tabStrip, tabStripHostRect, paneRect, insideType, c => c.ContentId))
					yield return t;
			}
		}

		private IEnumerable<IDropTarget> BuildTabTargetsForContent<TContent>(
			IDropArea area, ILayoutPanelElement targetPane, FrameworkElement tabStrip,
			Windows.Foundation.Rect tabStripHostRect, Windows.Foundation.Rect paneRect, DropTargetType targetType,
			Func<TContent, string> keySelector)
			where TContent : LayoutContent
		{
			var seen = new HashSet<string>();
			var tabAreas = new List<(Windows.Foundation.Rect Rect, int Index)>();

			foreach (var tab in EnumerateVisualsOfType<FrameworkElement>(tabStrip)
				.Where(fe => fe.Tag is TContent && fe.ActualWidth > 0 && fe.ActualHeight > 0))
			{
				if (tab.Tag is not TContent content) continue;
				var key = keySelector(content) ?? content.Title;
				if (key == null || !seen.Add(key)) continue;

				Windows.Foundation.Rect tabRect;
				try
				{
					var o = tab.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
					tabRect = new Windows.Foundation.Rect(o.X, o.Y, tab.ActualWidth, tab.ActualHeight);
				}
				catch { continue; }

#if !WINDOWS
				DockingManager.DragLogW(
					$"[TabTargets]   tab '{content.Title}' raw=({tabRect.Left:F1},{tabRect.Top:F1} {tabRect.Width:F1}x{tabRect.Height:F1}) " +
					$"hostTop={tabStripHostRect.Top:F1} hostH={tabStripHostRect.Height:F1}");
#endif
				if (!TryConstrainToTabStripHost(tabRect, tabStripHostRect, out tabRect))
					continue;
#if !WINDOWS
				DockingManager.DragLogW(
					$"[TabTargets]   tab '{content.Title}' constrained=({tabRect.Left:F1},{tabRect.Top:F1} {tabRect.Width:F1}x{tabRect.Height:F1})");
#endif

				var index = targetPane switch
				{
					LayoutDocumentPane dp  => dp.Children.IndexOf(content as LayoutDocument),
					LayoutAnchorablePane ap => ap.Children.IndexOf(content as LayoutAnchorable),
					_ => -1,
				};
				if (index < 0) continue;
				tabAreas.Add((tabRect, index));
			}

			if (tabAreas.Count == 0) yield break;
			tabAreas.Sort((a, b) => a.Rect.Left.CompareTo(b.Rect.Left));

			double? trailingRight = null;
			Windows.Foundation.Rect trailingRect = default;
			foreach (var ta in tabAreas)
			{
				yield return new UnoOverlayDropTarget(this, area, targetType, ta.Rect, paneRect, ta.Index);
				if (OverlayTabTargetRules.ShouldUseAsTrailingTabCandidate(trailingRight, ta.Rect.Right))
				{
					trailingRight = ta.Rect.Right;
					trailingRect  = ta.Rect;
				}
			}

			if (!trailingRight.HasValue) yield break;
			if (OverlayTabTargetRules.TryComputeTrailingTabDropArea(
				trailingRect.Left, trailingRect.Top, trailingRect.Right, trailingRect.Bottom,
				paneRect.Right,
				out var al, out var at, out var ar, out var ab))
			{
				var trailing = new Windows.Foundation.Rect(al, at, ar - al, ab - at);
				var appendIdx = targetPane switch
				{
					LayoutDocumentPane dp  => dp.ChildrenCount,
					LayoutAnchorablePane ap => ap.ChildrenCount,
					_ => 0,
				};
				yield return new UnoOverlayDropTarget(this, area, targetType, trailing, paneRect, appendIdx);
			}
		}

		private static bool TryConstrainToTabStripHost(
			Windows.Foundation.Rect tabRect,
			Windows.Foundation.Rect tabStripHostRect,
			out Windows.Foundation.Rect constrained)
		{
			var left = Math.Max(tabRect.Left, tabStripHostRect.Left);
			var right = Math.Min(tabRect.Right, tabStripHostRect.Right);
			if (right <= left)
			{
				constrained = default;
				return false;
			}

			constrained = new Windows.Foundation.Rect(
				left,
				tabStripHostRect.Top,
				right - left,
				tabStripHostRect.Height);
			return true;
		}

		private static IEnumerable<TControl> EnumerateVisualsOfType<TControl>(DependencyObject root)
			where TControl : DependencyObject
		{
			if (root == null) yield break;
			var stack = new Stack<DependencyObject>();
			stack.Push(root);
			while (stack.Count > 0)
			{
				var cur = stack.Pop();
				if (cur is TControl matched) yield return matched;
				var n = VisualTreeHelper.GetChildrenCount(cur);
				for (var i = 0; i < n; i++) stack.Push(VisualTreeHelper.GetChild(cur, i));
			}
		}

		private static bool CanDropInside(ILayoutPanelElement pane, LayoutFloatingWindow fw)
		{
			if (pane is LayoutDocumentPane)  return fw is LayoutDocumentFloatingWindow;
			if (pane is LayoutAnchorablePane) return fw is LayoutAnchorableFloatingWindow;
			return false;
		}

		private static ILayoutPanelElement ResolveTargetPane(IDropArea area)
		{
			if (area is OverlayDropArea oda && oda.AreaElement is ILayoutControl lc)
				return lc.Model as ILayoutPanelElement;
			return null;
		}

		private static LayoutDocumentPane ResolveRepresentativeDocumentPane(IDropArea area)
		{
			if (area is OverlayDropArea oda && oda.AreaElement is ILayoutControl lc &&
				lc.Model is LayoutDocumentPaneGroup g)
				return g.Descendents().OfType<LayoutDocumentPane>().FirstOrDefault();
			return null;
		}

		private bool ApplyDrop(IDropArea area, DropTargetType type, int? tabIndex, LayoutFloatingWindow fw)
		{
			if (_host?.Manager?.Layout == null || fw == null) return false;
			var content = ExtractPrimaryContent(fw);
			if (content == null) return false;

			bool applied;
			switch (type)
			{
				case DropTargetType.DockingManagerDockLeft:
					LayoutRootMutations.InsertPane(_host.Manager.Layout, content, CompassDropZone.OuterLeft);
					applied = true; break;
				case DropTargetType.DockingManagerDockTop:
					LayoutRootMutations.InsertPane(_host.Manager.Layout, content, CompassDropZone.OuterTop);
					applied = true; break;
				case DropTargetType.DockingManagerDockRight:
					LayoutRootMutations.InsertPane(_host.Manager.Layout, content, CompassDropZone.OuterRight);
					applied = true; break;
				case DropTargetType.DockingManagerDockBottom:
					LayoutRootMutations.InsertPane(_host.Manager.Layout, content, CompassDropZone.OuterBottom);
					applied = true; break;
				case DropTargetType.DocumentPaneDockInside:
				case DropTargetType.AnchorablePaneDockInside:
				{
					var pane = ResolveTargetPane(area);
					applied = pane != null && LayoutRootMutations.TryInsertIntoTargetPane(content, pane, tabIndex);
					break;
				}
				case DropTargetType.DocumentPaneGroupDockInside:
				{
					var pane = ResolveRepresentativeDocumentPane(area);
					applied = pane != null && LayoutRootMutations.TryInsertIntoTargetPane(content, pane, tabIndex);
					break;
				}
				case DropTargetType.DocumentPaneDockLeft:
				case DropTargetType.AnchorablePaneDockLeft:
					applied = TryInsertBeside(area, content, CompassDropZone.Left); break;
				case DropTargetType.DocumentPaneDockTop:
				case DropTargetType.AnchorablePaneDockTop:
					applied = TryInsertBeside(area, content, CompassDropZone.Top); break;
				case DropTargetType.DocumentPaneDockRight:
				case DropTargetType.AnchorablePaneDockRight:
					applied = TryInsertBeside(area, content, CompassDropZone.Right); break;
				case DropTargetType.DocumentPaneDockBottom:
				case DropTargetType.AnchorablePaneDockBottom:
					applied = TryInsertBeside(area, content, CompassDropZone.Bottom); break;
				// "As anchorable pane" outer buttons: explicitly mirror manager outer-edge actions.
				case DropTargetType.DocumentPaneDockAsAnchorableLeft:
					LayoutRootMutations.InsertPane(_host.Manager.Layout, content, CompassDropZone.OuterLeft);
					applied = true; break;
				case DropTargetType.DocumentPaneDockAsAnchorableTop:
					LayoutRootMutations.InsertPane(_host.Manager.Layout, content, CompassDropZone.OuterTop);
					applied = true; break;
				case DropTargetType.DocumentPaneDockAsAnchorableRight:
					LayoutRootMutations.InsertPane(_host.Manager.Layout, content, CompassDropZone.OuterRight);
					applied = true; break;
				case DropTargetType.DocumentPaneDockAsAnchorableBottom:
					LayoutRootMutations.InsertPane(_host.Manager.Layout, content, CompassDropZone.OuterBottom);
					applied = true; break;
				default:
					applied = false; break;
			}

			if (applied)
			{
				// Select + activate the just-dropped content (Uno-side, so the shared
				// AvalonDock LayoutRootMutations stays unmodified). Setting IsActive=true
				// makes the content the active tab and, via its setter, IsSelected=true →
				// the pane's SelectedContentIndex jumps to this content's real index — fixing
				// the helper's "select last tab" behaviour for mid-strip drops.
				content.IsActive = true;
			}

			if (applied && _host.Manager is DockingManager dm)
			{
				dm.CompleteOverlayDrop(_floatingWindow);
				dm.RefreshAfterOverlayDrop();
			}
			return applied;
		}

		private static bool TryInsertBeside(IDropArea area, LayoutContent content, CompassDropZone zone)
		{
			var pane = ResolveTargetPane(area);
			return pane != null && LayoutRootMutations.TryInsertBesideTarget(content, pane, zone);
		}

		private static LayoutContent ExtractPrimaryContent(LayoutFloatingWindow fw)
		{
			if (fw is LayoutDocumentFloatingWindow dfw)
				return dfw.RootPanel?.Descendents().OfType<LayoutDocument>().FirstOrDefault();
			if (fw is LayoutAnchorableFloatingWindow afw)
				return afw.RootPanel?.Descendents().OfType<LayoutAnchorable>().FirstOrDefault();
			return null;
		}

		// ── Nested drop-target ──────────────────────────────────────────────────

		private sealed class UnoOverlayDropTarget : IDropTarget
		{
			private readonly OverlayWindow _owner;
			private readonly IDropArea _area;
			// Hit-test zone (where the button sits — 1/3 of the pane for directional targets).
			internal readonly Windows.Foundation.Rect _detectionRect;
			// Preview zone (what content would occupy after drop — matches WPF GetPreviewPath rules).
			internal readonly Windows.Foundation.Rect _previewRect;
			private readonly int? _tabIndex;

			public UnoOverlayDropTarget(OverlayWindow owner, IDropArea area,
				DropTargetType type, Windows.Foundation.Rect detectionRect,
				Windows.Foundation.Rect previewRect, int? tabIndex)
			{
				_owner = owner; _area = area;
				Type = type;
				_detectionRect = detectionRect;
				_previewRect   = previewRect;
				_tabIndex = tabIndex;
			}

			public DropTargetType Type { get; }

			/// <summary>Screen-coordinate rect used for hit testing the drop target.</summary>
			internal Windows.Foundation.Rect DetectionRect => _detectionRect;

			/// <summary>Screen-coordinate rect shown as the drop preview highlight.</summary>
			internal Windows.Foundation.Rect PreviewRect => _previewRect;

			internal bool ShouldUseTabPreviewShape =>
				_tabIndex.HasValue &&
				(Type == DropTargetType.DocumentPaneDockInside ||
				 Type == DropTargetType.AnchorablePaneDockInside);

			public System.Windows.Media.Geometry GetPreviewPath(
				OverlayWindow overlayWindow, LayoutFloatingWindow floatingWindowModel) => null;

			public bool HitTestScreen(Windows.Foundation.Point dragPoint)
			{
				var hit = _detectionRect.Contains(dragPoint);
				if (ShouldUseTabPreviewShape)
					DockingManager.DragLogW(
						$"  tabTarget {Type} idx={_tabIndex} det=[{_detectionRect.Left:F1},{_detectionRect.Top:F1} " +
						$"{_detectionRect.Width:F1}x{_detectionRect.Height:F1}] pt=({dragPoint.X:F1},{dragPoint.Y:F1}) hit={hit}");
				return hit;
			}

			public void Drop(LayoutFloatingWindow floatingWindow)
				=> _owner.ApplyDrop(_area, Type, _tabIndex, floatingWindow);

			public void DragEnter() { }
			public void DragLeave() { }
		}
	}
}
