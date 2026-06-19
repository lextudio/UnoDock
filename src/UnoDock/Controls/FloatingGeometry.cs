using System.Linq;
using AvalonDock.Layout;

namespace AvalonDock.Controls
{
	// Pure, headless-testable write-back for AvalonDock parity gap #7 (drag-to-float.md):
	// persist a floating window's final geometry to its layout model so serialized
	// layouts round-trip the floated position/size.
	//
	// Mirrors WPF AvalonDock.Controls.LayoutFloatingWindowControl.UpdatePositionAndSizeOfPanes.
	internal static class FloatingGeometry
	{
		/// <summary>
		/// Write <paramref name="left"/>/<paramref name="top"/>/<paramref name="width"/>/
		/// <paramref name="height"/> into every floating-window element under
		/// <paramref name="model"/> and raise their FloatingPropertiesUpdated.
		/// </summary>
		public static void WriteBack(ILayoutElement model, double left, double top, double width, double height)
		{
			if (model == null) return;
			foreach (var posElement in model.Descendents().OfType<ILayoutElementForFloatingWindow>())
			{
				posElement.FloatingLeft = left;
				posElement.FloatingTop = top;
				posElement.FloatingWidth = width;
				posElement.FloatingHeight = height;
				posElement.RaiseFloatingPropertiesUpdated();
			}
		}
	}
}
