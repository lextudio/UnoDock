using System.Linq;
using NUnit.Framework;
using AvalonDock.Controls;
using Windows.Foundation;

namespace AvalonDockTest
{
	[TestFixture]
	public class OverlayHitTesterTests
	{
		[Test]
		public void SelectActiveAreas_NoCandidates_WhenPointOutsideAllZones()
		{
			var areas = new[]
			{
				Area(DropAreaType.DocumentPane, new Rect(0, 0, 100, 100)),
			};

			var selected = OverlayHitTester.SelectActiveAreas(areas, new Point(500, 500), pointerOverSplitter: false);

			Assert.That(selected, Is.Empty);
		}

		[Test]
		public void SelectActiveAreas_OverSplitter_KeepsOnlyManagerTargets()
		{
			var areas = new[]
			{
				Area(DropAreaType.DockingManager, new Rect(0, 0, 300, 300)),
				Area(DropAreaType.DocumentPane, new Rect(0, 0, 300, 300)),
			};

			var selected = OverlayHitTester.SelectActiveAreas(areas, new Point(150, 150), pointerOverSplitter: true);

			Assert.That(selected.Select(a => a.Type), Is.EqualTo(new[] { DropAreaType.DockingManager }));
		}

		[Test]
		public void SelectActiveAreas_SinglePane_KeepsAllCandidates()
		{
			var areas = new[]
			{
				Area(DropAreaType.DockingManager, new Rect(0, 0, 300, 300)),
				Area(DropAreaType.DocumentPane, new Rect(50, 50, 100, 100)),
			};

			var selected = OverlayHitTester.SelectActiveAreas(areas, new Point(100, 100), pointerOverSplitter: false);

			Assert.That(selected.Select(a => a.Type), Does.Contain(DropAreaType.DockingManager));
			Assert.That(selected.Select(a => a.Type), Does.Contain(DropAreaType.DocumentPane));
		}

		[Test]
		public void SelectActiveAreas_OverlappingPanes_PrefersTightestContainingPane()
		{
			var big = Area(DropAreaType.DocumentPane, new Rect(0, 0, 300, 300));
			var small = Area(DropAreaType.DocumentPane, new Rect(90, 90, 40, 40));
			var manager = Area(DropAreaType.DockingManager, new Rect(0, 0, 300, 300));

			var selected = OverlayHitTester.SelectActiveAreas(
				new[] { manager, big, small }, new Point(100, 100), pointerOverSplitter: false);

			// Manager stays; among panes only the tightest containing one survives.
			Assert.That(selected, Does.Contain(small));
			Assert.That(selected, Does.Not.Contain(big));
			Assert.That(selected, Does.Contain(manager));
		}

		[Test]
		public void IsWithinHitZone_PaneZoneInflatesForOuterButtons()
		{
			var pane = Area(DropAreaType.DocumentPane, new Rect(0, 0, 100, 100));

			// 40px outside the right edge: within the 64px document-pane inflation.
			Assert.That(OverlayHitTester.IsWithinHitZone(pane, new Point(140, 50)), Is.True);
			// 80px outside: beyond the inflation.
			Assert.That(OverlayHitTester.IsWithinHitZone(pane, new Point(180, 50)), Is.False);
		}

		[Test]
		public void IsWithinHitZone_ManagerZoneDoesNotInflate()
		{
			var manager = Area(DropAreaType.DockingManager, new Rect(0, 0, 100, 100));

			Assert.That(OverlayHitTester.IsWithinHitZone(manager, new Point(50, 50)), Is.True);
			Assert.That(OverlayHitTester.IsWithinHitZone(manager, new Point(110, 50)), Is.False);
		}

		private static IDropArea Area(DropAreaType type, Rect rect) => new FakeDropArea(type, rect);

		private sealed class FakeDropArea : IDropArea
		{
			public FakeDropArea(DropAreaType type, Rect detectionRect)
			{
				Type = type;
				DetectionRect = detectionRect;
			}

			public Rect DetectionRect { get; }

			public DropAreaType Type { get; }

			public Point TransformToDeviceDPI(Point dragPosition) => dragPosition;
		}
	}
}
