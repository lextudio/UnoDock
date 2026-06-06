using Microsoft.UI.Xaml;

namespace AvalonDock.Controls
{
	internal sealed class OverlayDropArea : IDropArea
	{
		internal OverlayDropArea(FrameworkElement areaElement, DropAreaType type)
		{
			AreaElement = areaElement;
			Type = type;
		}

		public FrameworkElement AreaElement { get; }

		public Windows.Foundation.Rect DetectionRect => AreaElement.GetScreenArea();

		public DropAreaType Type { get; }

		public Windows.Foundation.Point TransformToDeviceDPI(Windows.Foundation.Point dragPosition)
		{
			return AreaElement.TransformToDeviceDPI(dragPosition);
		}
	}
}
