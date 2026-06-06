// WPF-only: PresentationSource / CompositionTarget / TransformToAncestor.
// On Uno/Skia there is no DPI transform pipeline; logical and device pixels are
// the same. All methods return the untransformed value so drag/resize math still
// works (off slightly on HiDPI, acceptable for Phase 2 static layout).

using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace AvalonDock.Controls
{
	internal static class TransformExtensions
	{
		public static Point PointToScreenDPI(this UIElement visual, Point pt) => pt;

		public static Point PointToScreenDPIWithoutFlowDirection(this FrameworkElement element, Point point)
			=> element.PointToScreenDPI(point);

		public static Rect GetScreenArea(this FrameworkElement element)
		{
			if (element == null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
				return new Rect(new Point(), new Size());

			try
			{
				var origin = element.TransformToVisual(null).TransformPoint(new Point(0, 0));
				return new Rect(origin, new Size(element.ActualWidth, element.ActualHeight));
			}
			catch
			{
				return new Rect(new Point(), new Size(element.ActualWidth, element.ActualHeight));
			}
		}

		public static Point TransformToDeviceDPI(this UIElement visual, Point pt) => pt;
		public static Size TransformFromDeviceDPI(this UIElement visual, Size size) => size;
		public static Point TransformFromDeviceDPI(this UIElement visual, Point pt) => pt;
		public static bool CanTransform(this UIElement visual) => false;

		public static Size TransformActualSizeToAncestor(this FrameworkElement element)
			=> new Size(element.ActualWidth, element.ActualHeight);

		public static Size TransformSizeToAncestor(this FrameworkElement element, Size sizeToTransform)
			=> sizeToTransform;

		public static GeneralTransform TansformToAncestor(this FrameworkElement element)
			=> new MatrixTransform();
	}
}
