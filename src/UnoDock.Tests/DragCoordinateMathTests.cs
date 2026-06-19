using NUnit.Framework;
using AvalonDock.Controls;
using Windows.Foundation;

namespace AvalonDockTest
{
	[TestFixture]
	public class DragCoordinateMathTests
	{
		[Test]
		public void CombineOrigin_ScalesManagerOffsetIntoNativePixelSpace()
		{
			// Native client origin at (1000,500) px; manager 50,30 DIPs into the root at 2x.
			var (x, y) = DragCoordinateMath.CombineOrigin(1000, 500, 50, 30, scale: 2.0);

			Assert.That(x, Is.EqualTo(1100));
			Assert.That(y, Is.EqualTo(560));
		}

		[Test]
		public void CombineOrigin_LogicalPoints_NoScaling()
		{
			var (x, y) = DragCoordinateMath.CombineOrigin(800, 400, 12, 18, scale: 1.0);

			Assert.That(x, Is.EqualTo(812));
			Assert.That(y, Is.EqualTo(418));
		}

		[Test]
		public void CombineOrigin_NonPositiveScale_TreatedAsOne()
		{
			var (x, y) = DragCoordinateMath.CombineOrigin(100, 100, 10, 10, scale: 0);

			Assert.That(x, Is.EqualTo(110));
			Assert.That(y, Is.EqualTo(110));
		}

		[Test]
		public void ScreenToManagerLocal_SubtractsOriginThenDividesByScale()
		{
			// Cursor at (1300,800) px, origin (1100,560) px, 2x → local DIPs.
			var local = DragCoordinateMath.ScreenToManagerLocal(new Point(1300, 800), 1100, 560, scale: 2.0);

			Assert.That(local.X, Is.EqualTo(100));
			Assert.That(local.Y, Is.EqualTo(120));
		}

		[Test]
		public void ScreenToManagerLocal_RoundTripsWithCombineOrigin()
		{
			// Origin computed by CombineOrigin, then the manager's own offset point maps
			// back to exactly (offsetX, offsetY) in local space.
			const double scale = 1.5;
			var (ox, oy) = DragCoordinateMath.CombineOrigin(900, 450, 40, 20, scale);

			// A cursor sitting at the manager-local point (40,20) DIPs is at native:
			var nativeCursor = new Point(900 + (40 + 40) * scale, 450 + (20 + 20) * scale);
			var local = DragCoordinateMath.ScreenToManagerLocal(nativeCursor, ox, oy, scale);

			Assert.That(local.X, Is.EqualTo(40).Within(1e-9));
			Assert.That(local.Y, Is.EqualTo(20).Within(1e-9));
		}

		[Test]
		public void ScreenToManagerLocal_MacOsLogicalPoints_PureSubtraction()
		{
			var local = DragCoordinateMath.ScreenToManagerLocal(new Point(812, 418), 800, 400, scale: 1.0);

			Assert.That(local.X, Is.EqualTo(12));
			Assert.That(local.Y, Is.EqualTo(18));
		}
	}
}
