// Individual auto-hide anchor tab — icon + rotated title, hover/click to show flyout.
// Mirrors AvalonDock's LayoutAnchorControl (WPF) ported to WinUI/Uno code-behind.

using System;
using System.ComponentModel;
using AvalonDock.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AvalonDock.Controls
{
	public sealed class LayoutAnchorControl : ContentControl, ILayoutControl
	{
		private const string KeyTabBarBackground = "UnoDock_VS2013_TabBarBackground";
		private const string KeyAutoHideTabDefaultBorder = "UnoDock_VS2013_AutoHideTabDefaultBorder";
		private const string KeyAutoHideTabDefaultText = "UnoDock_VS2013_AutoHideTabDefaultText";
		private const string KeyAutoHideTabHoveredBackground = "UnoDock_VS2013_AutoHideTabHoveredBackground";
		private const string KeyAutoHideTabHoveredBorder = "UnoDock_VS2013_AutoHideTabHoveredBorder";
		private const string KeyAutoHideTabHoveredText = "UnoDock_VS2013_AutoHideTabHoveredText";

		private static readonly SolidColorBrush FallbackTabBg =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x2D, 0x2D, 0x30));
		private static readonly SolidColorBrush FallbackTabHover =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x3E, 0x3E, 0x42));
		private static readonly SolidColorBrush FallbackTabBorder =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x3F, 0x3F, 0x46));
		private static readonly SolidColorBrush FallbackTabFg =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xD4, 0xD4, 0xD4));
		private static readonly SolidColorBrush FallbackHoverBorder =
			new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x00, 0x7A, 0xCC));

		private readonly LayoutAnchorable _model;
		private readonly LayoutAnchorSideControl _side;
		private Border _root;

		ILayoutElement ILayoutControl.Model => _model;
		public LayoutAnchorable Model => _model;

		internal LayoutAnchorControl(LayoutAnchorable model, LayoutAnchorSideControl side)
		{
			_model = model ?? throw new ArgumentNullException(nameof(model));
			_side  = side  ?? throw new ArgumentNullException(nameof(side));

			_root = BuildRoot();
			Content = _root;

			_model.PropertyChanged += OnModelPropertyChanged;
			Loaded += (_, _) => UpdateRoot();
		}

		// Called by LayoutAnchorGroupControl when the side is known.
		internal AnchorSide Side => _model.FindParent<LayoutAnchorSide>()?.Side ?? AnchorSide.Left;

		private Border BuildRoot()
		{
			var tabBg = ResolveBrush(KeyTabBarBackground, FallbackTabBg);
			var tabBorder = ResolveBrush(KeyAutoHideTabDefaultBorder, FallbackTabBorder);
			var tabFg = ResolveBrush(KeyAutoHideTabDefaultText, FallbackTabFg);
			var tabHover = ResolveBrush(KeyAutoHideTabHoveredBackground, FallbackTabHover);
			var tabHoverBorder = ResolveBrush(KeyAutoHideTabHoveredBorder, FallbackHoverBorder);
			var tabHoverFg = ResolveBrush(KeyAutoHideTabHoveredText, FallbackHoverBorder);

			var side = Side;
			bool isVertical = side == AnchorSide.Left || side == AnchorSide.Right;

			var accentThickness = side switch
			{
				AnchorSide.Left   => new Thickness(6, 0, 0, 0),
				AnchorSide.Right  => new Thickness(0, 0, 6, 0),
				AnchorSide.Top    => new Thickness(0, 6, 0, 0),
				AnchorSide.Bottom => new Thickness(0, 0, 0, 6),
				_                 => new Thickness(0),
			};

			var content = BuildContent(isVertical);
			if (content is TextBlock text)
				text.Foreground = tabFg;

			// WPF uses Margin="0,0,12,0" on the Border; after the group LayoutTransform(90°)
			// the right margin becomes a bottom margin giving 12px between anchors.
			// In Uno we render vertically directly, so add 12px bottom margin.
			var tab = new Border
			{
				Background      = tabBg,
				BorderBrush     = tabBorder,
				BorderThickness = accentThickness,
				Padding         = isVertical ? new Thickness(2, 8, 4, 8) : new Thickness(8, 2, 8, 4),
				Margin          = isVertical ? new Thickness(0, 0, 0, 12) : new Thickness(0, 0, 12, 0),
				Child           = content,
				Tag             = _model,
				Name            = $"anchor-{_model.ContentId}",
			};
			Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(tab, $"anchor-{_model.ContentId}");

			tab.PointerPressed += (_, _) => OnAnchorClicked(tab);
			tab.Tapped         += (_, _) => OnAnchorClicked(tab);
			tab.PointerEntered += (_, _) =>
			{
				tab.Background = tabHover;
				tab.BorderBrush = tabHoverBorder;
				SetTextForeground(content, tabHoverFg);
				_side.ScheduleHoverOpen(_model, tab);
			};
			tab.PointerExited += (_, _) =>
			{
				tab.Background = tabBg;
				tab.BorderBrush = tabBorder;
				SetTextForeground(content, tabFg);
				_side.CancelHoverOpen();
				_side.ScheduleHoverClose();
			};

			return tab;
		}

		private UIElement BuildContent(bool isVertical)
		{
			var tb = new TextBlock
			{
				Text         = _model.Title ?? string.Empty,
				FontSize     = 11,
				Foreground   = ResolveBrush(KeyAutoHideTabDefaultText, FallbackTabFg),
				TextWrapping = TextWrapping.NoWrap,
			};

			UIElement titleEl;
			if (isVertical)
			{
				const double lineH = 20.0;
				const double charW = 7.0;
				double side_ = Side == AnchorSide.Left ? 90.0 : 270.0;
				// Use estimated width initially; Loaded event corrects to actual measured width.
				double textW = Math.Max(lineH, ((_model.Title ?? string.Empty).Length * charW));

				tb.Width = textW;
				tb.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
				tb.RenderTransform = new RotateTransform { Angle = side_ };

				var canvas = new Canvas { Width = lineH, Height = textW };
				Canvas.SetLeft(tb, (lineH - textW) / 2.0);
				Canvas.SetTop(tb,  (textW - lineH) / 2.0);
				canvas.Children.Add(tb);

				// After layout, remeasure with actual font metrics and resize the canvas.
				// Must clear tb.Width first — Measure() on a TextBlock with an explicit Width
				// returns that Width unchanged, so the correction below would never fire.
				canvas.Loaded += (_, _) =>
				{
					tb.Width = double.NaN;
					tb.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
					double actualW = tb.DesiredSize.Width;
					if (actualW > 0)
					{
						tb.Width = actualW;
						canvas.Height = actualW;
						Canvas.SetLeft(tb, (lineH - actualW) / 2.0);
						Canvas.SetTop(tb,  (actualW - lineH) / 2.0);
					}
					else
					{
						tb.Width = textW; // restore estimate if measure returned nothing
					}
				};

				titleEl = canvas;
			}
			else
			{
				titleEl = tb;
			}

			return titleEl;
		}

		private static void SetTextForeground(UIElement node, Brush brush)
		{
			if (node == null || brush == null)
				return;

			if (node is TextBlock tb)
				tb.Foreground = brush;

			var childrenCount = VisualTreeHelper.GetChildrenCount(node);
			for (var i = 0; i < childrenCount; i++)
				if (VisualTreeHelper.GetChild(node, i) is UIElement child)
					SetTextForeground(child, brush);
		}

		private void UpdateRoot()
		{
			_root = BuildRoot();
			Content = _root;
		}

		private void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName is nameof(LayoutAnchorable.Title) or nameof(LayoutAnchorable.IconSource))
				DispatcherQueue?.TryEnqueue(() => UpdateRoot());
		}

		private void OnAnchorClicked(Border tab)
		{
			_side.CancelHoverOpen();
			_side.CancelHoverClose();
			_side.ShowFlyout(_model, tab);
		}

		private Brush ResolveBrush(string key, Brush fallback)
		{
			if (Resources != null && Resources.TryGetValue(key, out var local) && local is Brush localBrush)
				return localBrush;

			if (_side != null)
				return _side.ResolveBrush(key, fallback);

			var appResources = Application.Current?.Resources;
			if (appResources != null && appResources.TryGetValue(key, out var app) && app is Brush appBrush)
				return appBrush;

			return fallback;
		}
	}
}
