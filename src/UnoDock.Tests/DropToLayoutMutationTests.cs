using System.Linq;
using System.Reflection;
using NUnit.Framework;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Orientation = System.Windows.Controls.Orientation;

namespace AvalonDockTest
{
	// Headless coverage of the decision -> layout-mutation contract for each
	// DropTargetType (docs/refactoring.md, Challenge 2 final deliverable).
	//
	// Each test drives OverlayWindow.ApplyDrop (the single switch that maps a drop
	// target to a shared LayoutRootMutations call) and asserts the resulting
	// LayoutRoot, with no real window, cursor, or rendered visual tree.
	[TestFixture]
	public class DropToLayoutMutationTests
	{
		private static readonly MethodInfo ApplyDropMethod =
			typeof(OverlayWindow).GetMethod("ApplyDrop", BindingFlags.Instance | BindingFlags.NonPublic);

		// ── DockingManager outer edges ──────────────────────────────────────────

		[TestCase(DropTargetType.DockingManagerDockLeft, Orientation.Horizontal, /*floatingFirst*/ true)]
		[TestCase(DropTargetType.DockingManagerDockRight, Orientation.Horizontal, false)]
		[TestCase(DropTargetType.DockingManagerDockTop, Orientation.Vertical, true)]
		[TestCase(DropTargetType.DockingManagerDockBottom, Orientation.Vertical, false)]
		public void ManagerOuterDrop_InsertsFloatingOnCorrectEdge(
			DropTargetType type, Orientation expectedOrientation, bool floatingFirst)
		{
			var manager = CreateManagerWithSingleDocument();
			var overlay = new OverlayWindow(new TestHost(manager));
			var floating = CreateDocumentFloatingWindowModel("Floating");
			((IOverlayWindow)overlay).DragEnter(new LayoutDocumentFloatingWindowControl(floating));

			var applied = InvokeApplyDrop(overlay, ManagerArea(), type, null, floating);

			Assert.That(applied, Is.True);
			var root = manager.Layout.RootPanel;
			Assert.That(root.Orientation, Is.EqualTo(expectedOrientation));
			Assert.That(root.ChildrenCount, Is.EqualTo(2));

			var docs = manager.Layout.Descendents().OfType<LayoutDocument>().Select(d => d.Title).ToList();
			Assert.That(docs, Is.EquivalentTo(new[] { "Doc1", "Floating" }));

			// The first edge child holds the floating doc for Left/Top, the existing doc for Right/Bottom.
			var firstChild = (ILayoutElement)root.Children.First();
			var firstPaneTitles = firstChild.Descendents().OfType<LayoutDocument>().Select(d => d.Title).ToList();
			Assert.That(firstPaneTitles, Does.Contain(floatingFirst ? "Floating" : "Doc1"));
			Assert.That(firstPaneTitles, Does.Not.Contain(floatingFirst ? "Doc1" : "Floating"));
		}

		// ── Beside a document pane (split) ──────────────────────────────────────

		[TestCase(DropTargetType.DocumentPaneDockLeft)]
		[TestCase(DropTargetType.DocumentPaneDockRight)]
		[TestCase(DropTargetType.DocumentPaneDockTop)]
		[TestCase(DropTargetType.DocumentPaneDockBottom)]
		public void DocumentPaneBesideDrop_AddsSiblingPane(DropTargetType type)
		{
			var manager = CreateManagerWithSingleDocument();
			var overlay = new OverlayWindow(new TestHost(manager));
			var floating = CreateDocumentFloatingWindowModel("Floating");
			((IOverlayWindow)overlay).DragEnter(new LayoutDocumentFloatingWindowControl(floating));

			var targetPane = manager.Layout.Descendents().OfType<LayoutDocumentPane>().First();
			var area = new OverlayDropArea(new TestLayoutControl(targetPane), DropAreaType.DocumentPane);

			var applied = InvokeApplyDrop(overlay, area, type, null, floating);

			Assert.That(applied, Is.True);
			var docs = manager.Layout.Descendents().OfType<LayoutDocument>().Select(d => d.Title).ToList();
			Assert.That(docs, Is.EquivalentTo(new[] { "Doc1", "Floating" }));
			// A beside-drop must keep the two documents in separate panes (a split),
			// never merged into one tabbed pane.
			Assert.That(manager.Layout.Descendents().OfType<LayoutDocumentPane>().Count(), Is.EqualTo(2));
		}

		// ── Inside a pane (tabbed) ──────────────────────────────────────────────

		[Test]
		public void DocumentPaneDockInside_AppendsAsTab()
		{
			var manager = CreateManagerWithSingleDocument();
			var overlay = new OverlayWindow(new TestHost(manager));
			var floating = CreateDocumentFloatingWindowModel("Floating");
			((IOverlayWindow)overlay).DragEnter(new LayoutDocumentFloatingWindowControl(floating));

			var targetPane = manager.Layout.Descendents().OfType<LayoutDocumentPane>().First();
			var area = new OverlayDropArea(new TestLayoutControl(targetPane), DropAreaType.DocumentPane);

			var applied = InvokeApplyDrop(overlay, area, DropTargetType.DocumentPaneDockInside, /*tabIndex*/ 1, floating);

			Assert.That(applied, Is.True);
			// Both documents now share a single pane (tabbed), floating appended after Doc1.
			Assert.That(manager.Layout.Descendents().OfType<LayoutDocumentPane>().Count(), Is.EqualTo(1));
			Assert.That(targetPane.Children.Select(c => c.Title), Is.EqualTo(new[] { "Doc1", "Floating" }));
		}

		[Test]
		public void DocumentPaneDockInside_TabIndexZero_InsertsBeforeExisting()
		{
			var manager = CreateManagerWithSingleDocument();
			var overlay = new OverlayWindow(new TestHost(manager));
			var floating = CreateDocumentFloatingWindowModel("Floating");
			((IOverlayWindow)overlay).DragEnter(new LayoutDocumentFloatingWindowControl(floating));

			var targetPane = manager.Layout.Descendents().OfType<LayoutDocumentPane>().First();
			var area = new OverlayDropArea(new TestLayoutControl(targetPane), DropAreaType.DocumentPane);

			var applied = InvokeApplyDrop(overlay, area, DropTargetType.DocumentPaneDockInside, /*tabIndex*/ 0, floating);

			Assert.That(applied, Is.True);
			Assert.That(targetPane.Children.Select(c => c.Title), Is.EqualTo(new[] { "Floating", "Doc1" }));
		}

		[Test]
		public void AnchorablePaneDockInside_AppendsAnchorableAsTab()
		{
			var manager = CreateManagerWithDocumentHostAndLeftPane();
			var overlay = new OverlayWindow(new TestHost(manager));
			var floating = CreateAnchorableFloatingWindowModel("FloatingTool");
			((IOverlayWindow)overlay).DragEnter(new LayoutAnchorableFloatingWindowControl(floating));

			var targetPane = manager.Layout.Descendents().OfType<LayoutAnchorablePane>().First();
			var area = new OverlayDropArea(new TestLayoutControl(targetPane), DropAreaType.AnchorablePane);

			var applied = InvokeApplyDrop(overlay, area, DropTargetType.AnchorablePaneDockInside, null, floating);

			Assert.That(applied, Is.True);
			Assert.That(targetPane.Children.Select(c => c.Title), Does.Contain("FloatingTool"));
			Assert.That(targetPane.Children.Select(c => c.Title), Does.Contain("LeftTool"));
		}

		// ── "As anchorable" outer buttons mirror manager outer edges ────────────

		[Test]
		public void DocumentPaneDockAsAnchorableRight_MirrorsManagerOuterRight()
		{
			var viaAsAnchorable = CreateManagerWithSingleDocument();
			var viaManagerOuter = CreateManagerWithSingleDocument();

			ApplyOuter(viaAsAnchorable, DropTargetType.DocumentPaneDockAsAnchorableRight);
			ApplyOuter(viaManagerOuter, DropTargetType.DockingManagerDockRight);

			Assert.That(DescribeLayout(viaAsAnchorable.Layout.RootPanel),
				Is.EqualTo(DescribeLayout(viaManagerOuter.Layout.RootPanel)));
		}

		// ── Coordinate-driven: point selects the area, then drop mutates ────────

		[Test]
		public void CoordinatePicksTightestPane_ThenInsideDrop_Tabifies()
		{
			var manager = CreateManagerWithSingleDocument();
			var overlay = new OverlayWindow(new TestHost(manager));
			var floating = CreateDocumentFloatingWindowModel("Floating");
			((IOverlayWindow)overlay).DragEnter(new LayoutDocumentFloatingWindowControl(floating));

			var targetPane = manager.Layout.Descendents().OfType<LayoutDocumentPane>().First();
			var paneAreaForDrop = new OverlayDropArea(new TestLayoutControl(targetPane), DropAreaType.DocumentPane);

			// Decision layer: a drag point inside the pane rect selects the pane area
			// over the surrounding manager area.
			var candidates = new IDropArea[]
			{
				new RectDropArea(DropAreaType.DockingManager, new Rect(0, 0, 400, 400)),
				new RectDropArea(DropAreaType.DocumentPane, new Rect(100, 100, 80, 80)),
			};
			var selected = OverlayHitTester.SelectActiveAreas(candidates, new Point(140, 140), pointerOverSplitter: false);
			Assert.That(selected.Any(a => a.Type == DropAreaType.DocumentPane), Is.True);

			// Actuation/mutation layer: applying an inside drop on the selected pane tabifies.
			var applied = InvokeApplyDrop(overlay, paneAreaForDrop, DropTargetType.DocumentPaneDockInside, null, floating);

			Assert.That(applied, Is.True);
			Assert.That(targetPane.Children.Select(c => c.Title), Does.Contain("Floating"));
		}

		// ── Helpers ─────────────────────────────────────────────────────────────

		private static bool InvokeApplyDrop(
			OverlayWindow overlay, IDropArea area, DropTargetType type, int? tabIndex, LayoutFloatingWindow fw)
		{
			Assert.That(ApplyDropMethod, Is.Not.Null);
			return (bool)ApplyDropMethod.Invoke(overlay, new object[] { area, type, tabIndex, fw });
		}

		private static void ApplyOuter(DockingManager manager, DropTargetType type)
		{
			var overlay = new OverlayWindow(new TestHost(manager));
			var floating = CreateAnchorableFloatingWindowModel("FloatingTool");
			((IOverlayWindow)overlay).DragEnter(new LayoutAnchorableFloatingWindowControl(floating));
			InvokeApplyDrop(overlay, ManagerArea(), type, null, floating);
		}

		private static IDropArea ManagerArea()
			=> new RectDropArea(DropAreaType.DockingManager, new Rect(0, 0, 400, 400));

		private static DockingManager CreateManagerWithSingleDocument()
		{
			var pane = new LayoutDocumentPane();
			pane.Children.Add(new LayoutDocument { Title = "Doc1", ContentId = "doc1" });

			var manager = new DockingManager { Layout = new LayoutRoot { RootPanel = new LayoutPanel() } };
			manager.Layout.RootPanel.Children.Add(pane);
			return manager;
		}

		private static DockingManager CreateManagerWithDocumentHostAndLeftPane()
		{
			var leftPane = new LayoutAnchorablePane();
			leftPane.Children.Add(new LayoutAnchorable { Title = "LeftTool", ContentId = "left-tool" });

			var docPane = new LayoutDocumentPane();
			docPane.Children.Add(new LayoutDocument { Title = "Doc1", ContentId = "doc1" });

			var docGroup = new LayoutDocumentPaneGroup();
			docGroup.Children.Add(docPane);

			var rootPanel = new LayoutPanel();
			rootPanel.Children.Add(leftPane);
			rootPanel.Children.Add(docGroup);

			return new DockingManager { Layout = new LayoutRoot { RootPanel = rootPanel } };
		}

		private static LayoutDocumentFloatingWindow CreateDocumentFloatingWindowModel(string title)
		{
			var pane = new LayoutDocumentPane();
			pane.Children.Add(new LayoutDocument { Title = title, ContentId = title.ToLowerInvariant() });
			var rootPanel = new LayoutDocumentPaneGroup();
			rootPanel.Children.Add(pane);
			return new LayoutDocumentFloatingWindow { RootPanel = rootPanel };
		}

		private static LayoutAnchorableFloatingWindow CreateAnchorableFloatingWindowModel(string title)
		{
			var pane = new LayoutAnchorablePane();
			pane.Children.Add(new LayoutAnchorable { Title = title, ContentId = title.ToLowerInvariant() });
			var rootPanel = new LayoutAnchorablePaneGroup();
			rootPanel.Children.Add(pane);
			return new LayoutAnchorableFloatingWindow { RootPanel = rootPanel };
		}

		private static string DescribeLayout(ILayoutElement root)
		{
			if (root is LayoutDocumentPaneGroup docGroup)
				return "DocGroup(" + string.Join(",", docGroup.Children.OfType<ILayoutElement>().Select(DescribeLayout)) + ")";
			if (root is LayoutPanel panel)
				return "Panel[" + panel.Orientation + "](" + string.Join(",", panel.Children.OfType<ILayoutElement>().Select(DescribeLayout)) + ")";
			if (root is LayoutDocumentPane docPane)
				return "DocPane(" + string.Join(",", docPane.Children.Select(d => d.Title)) + ")";
			if (root is LayoutAnchorablePane anchorPane)
				return "AnchPane(" + string.Join(",", anchorPane.Children.Select(a => a.Title)) + ")";
			return root?.GetType().Name ?? "null";
		}

		private sealed class TestHost : IOverlayWindowHost
		{
			public TestHost(DockingManager manager) => Manager = manager;

			public DockingManager Manager { get; }

			public bool HitTestScreen(Point dragPoint) => false;

			public IOverlayWindow ShowOverlayWindow(LayoutFloatingWindowControl draggingWindow) => null;

			public void HideOverlayWindow() { }

			public System.Collections.Generic.IEnumerable<IDropArea> GetDropAreas(LayoutFloatingWindowControl draggingWindow)
			{
				yield break;
			}
		}

		private sealed class RectDropArea : IDropArea
		{
			public RectDropArea(DropAreaType type, Rect detectionRect)
			{
				Type = type;
				DetectionRect = detectionRect;
			}

			public Rect DetectionRect { get; }

			public DropAreaType Type { get; }

			public Point TransformToDeviceDPI(Point dragPosition) => dragPosition;
		}

		private sealed class TestLayoutControl : FrameworkElement, ILayoutControl
		{
			public TestLayoutControl(ILayoutElement model) => Model = model;

			public ILayoutElement Model { get; }
		}
	}
}
