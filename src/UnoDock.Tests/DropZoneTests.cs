// Tests for drop-zone commit logic and layout garbage collection.
// Covers the "white area" bugs caused by empty panes left after float-out or drop.

using System.Linq;
using System.Reflection;
using NUnit.Framework;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;

namespace AvalonDockTest
{
	[TestFixture]
	public class DropZoneTests
	{
		// ── Helpers ─────────────────────────────────────────────────────────────

		private static DockingManager MakeManager(params string[] docTitles)
		{
			var pane = new LayoutDocumentPane();
			foreach (var t in docTitles)
				pane.Children.Add(new LayoutDocument { Title = t, ContentId = t.ToLower().Replace(" ", "-") });
			var manager = new DockingManager
			{
				Layout = new LayoutRoot
				{
					RootPanel = new LayoutPanel { Orientation = System.Windows.Controls.Orientation.Horizontal }
				}
			};
			manager.Layout.RootPanel.Children.Add(pane);
			return manager;
		}

		private static void SimulateFloat(DockingManager manager, LayoutDocument doc)
		{
			// Mirror CreateFloatingWindow's pane-removal + RefreshAfterLayoutMutation path
			// without actually creating an OS window.
			var pane = doc.Parent as ILayoutPane;
			var idx = pane?.Children.ToList().IndexOf(doc) ?? -1;
			if (idx >= 0)
			{
				pane.RemoveChildAt(idx);
				InvokeRefresh(manager);
			}
		}

		private static void SimulateCommitDrop(DockingManager manager, LayoutDocument doc, CompassDropZone zone)
		{
			var refresh = typeof(DockingManager).GetMethod(
				"RefreshAfterLayoutMutation", BindingFlags.NonPublic | BindingFlags.Instance);
			LayoutRootMutations.InsertPane(manager.Layout, doc, zone);
			refresh.Invoke(manager, null);
		}

		private static void InvokeRefresh(DockingManager manager)
			=> typeof(DockingManager)
				.GetMethod("RefreshAfterLayoutMutation", BindingFlags.NonPublic | BindingFlags.Instance)
				.Invoke(manager, null);

		private static int CountDocumentPanes(LayoutRoot root)
			=> root.Descendents().OfType<LayoutDocumentPane>().Count();

		private static int CountEmptyDocumentPanes(LayoutRoot root)
			=> root.Descendents().OfType<LayoutDocumentPane>().Count(p => p.Children.Count == 0);

		// ── CollectGarbage ───────────────────────────────────────────────────────

		[Test]
		public void CollectGarbage_RemovesEmptyDocumentPane()
		{
			var manager = MakeManager("Doc1", "Doc2");
			var doc1 = manager.Layout.Descendents().OfType<LayoutDocument>().First(d => d.Title == "Doc1");

			SimulateFloat(manager, doc1);

			// After float-out, empty panes should be gone.
			Assert.That(CountEmptyDocumentPanes(manager.Layout), Is.Zero,
				"Empty LayoutDocumentPane should be removed after float-out");
		}

		[Test]
		public void CollectGarbage_PreservesNonEmptyPane()
		{
			var manager = MakeManager("Doc1", "Doc2");
			var doc1 = manager.Layout.Descendents().OfType<LayoutDocument>().First(d => d.Title == "Doc1");

			SimulateFloat(manager, doc1);

			var remaining = manager.Layout.Descendents().OfType<LayoutDocument>().ToList();
			Assert.That(remaining.Count, Is.EqualTo(1));
			Assert.That(remaining[0].Title, Is.EqualTo("Doc2"));
		}

		[Test]
		public void CollectGarbage_FloatOnlyDoc_PreservesLastEmptyPane()
		{
			// CollectGarbage intentionally keeps the last empty LayoutDocumentPane in the
			// main window (AvalonDock rule: always keep one drop target for documents).
			// Verify: exactly one (the preserved empty pane) after floating the only doc.
			var manager = MakeManager("Doc1");
			var doc1 = manager.Layout.Descendents().OfType<LayoutDocument>().First();

			SimulateFloat(manager, doc1);

			Assert.That(CountEmptyDocumentPanes(manager.Layout), Is.EqualTo(1),
				"The last empty LayoutDocumentPane should be preserved as a drop target");
			// And the floating document is gone from the main layout.
			Assert.That(manager.Layout.Descendents().OfType<LayoutDocument>().Count(), Is.Zero);
		}

		[Test]
		public void CollectGarbage_WhenMultiplePanes_RemovesEmptyOne()
		{
			// When there are multiple doc panes, empty ones ARE removed.
			var manager = MakeManager("Doc1", "Doc2");
			// Add a second pane manually so there are two document panes.
			var secondPane = new LayoutDocumentPane();
			manager.Layout.RootPanel.Children.Add(secondPane);

			// Remove Doc1, leaving first pane empty; second pane still exists.
			var doc1 = manager.Layout.Descendents().OfType<LayoutDocument>().First(d => d.Title == "Doc1");
			SimulateFloat(manager, doc1);

			// The empty first pane should have been removed because a second pane exists.
			Assert.That(CountEmptyDocumentPanes(manager.Layout), Is.Zero,
				"Empty pane should be removed when another non-empty pane exists");
		}

		// ── Drop zone: Center ────────────────────────────────────────────────────

		[Test]
		public void DropCenter_AddsDocumentToExistingPane()
		{
			var manager = MakeManager("Doc1");
			var floating = new LayoutDocument { Title = "Floating", ContentId = "floating" };

			SimulateCommitDrop(manager, floating, CompassDropZone.Center);

			var docs = manager.Layout.Descendents().OfType<LayoutDocument>().ToList();
			Assert.That(docs.Count, Is.EqualTo(2));
			Assert.That(docs.Any(d => d.Title == "Floating"), Is.True);
		}

		[Test]
		public void DropCenter_NoSpuriousEmptyPanes()
		{
			var manager = MakeManager("Doc1");
			var floating = new LayoutDocument { Title = "Floating", ContentId = "floating" };

			SimulateCommitDrop(manager, floating, CompassDropZone.Center);

			Assert.That(CountEmptyDocumentPanes(manager.Layout), Is.Zero);
		}

		// ── Drop zone: Top ───────────────────────────────────────────────────────

		[Test]
		public void DropTop_InsertsAdjacentToDocPane_NotOutsideToolPanes()
		{
			// AvalonDock: Top splits within the document area (relative to the doc pane),
			// not at the root panel level. Tool panes on outer edges are unaffected.
			var manager = MakeManager("Doc1");
			var leftTools  = new LayoutAnchorablePane(new LayoutAnchorable { Title = "Explorer", ContentId = "explorer" });
			var rightTools = new LayoutAnchorablePane(new LayoutAnchorable { Title = "Props",    ContentId = "props" });
			var docPane = manager.Layout.Descendents().OfType<LayoutDocumentPane>().First();
			var docPaneIdx = manager.Layout.RootPanel.Children.ToList().IndexOf(docPane);
			manager.Layout.RootPanel.Children.Insert(docPaneIdx, leftTools);
			manager.Layout.RootPanel.Children.Add(rightTools);

			var floating = new LayoutDocument { Title = "Floating", ContentId = "floating" };
			SimulateCommitDrop(manager, floating, CompassDropZone.Top);

			// Tool panes must remain the outermost root children.
			var root = manager.Layout.RootPanel;
			Assert.That(root.Children[0].Descendents().OfType<LayoutAnchorable>().Any(a => a.ContentId == "explorer"),
				Is.True, "Left tool pane must stay outermost left");
			Assert.That(root.Children[root.Children.Count - 1].Descendents().OfType<LayoutAnchorable>().Any(a => a.ContentId == "props"),
				Is.True, "Right tool pane must stay outermost right");

			// Floating doc must be above the original doc pane (in a sub-panel, not at root).
			Assert.That(manager.Layout.Descendents().OfType<LayoutDocument>().Count(), Is.EqualTo(2));
			Assert.That(CountEmptyDocumentPanes(manager.Layout), Is.Zero);
		}

		[Test]
		public void DropTop_NoEmptyPanesAfterDrop()
		{
			var manager = MakeManager("Doc1");
			var floating = new LayoutDocument { Title = "Floating", ContentId = "floating" };
			SimulateCommitDrop(manager, floating, CompassDropZone.Top);
			Assert.That(CountEmptyDocumentPanes(manager.Layout), Is.Zero);
		}

		// ── Drop zone: Bottom ────────────────────────────────────────────────────

		[Test]
		public void DropBottom_InsertsAdjacentToDocPane_NotOutsideToolPanes()
		{
			var manager = MakeManager("Doc1");
			var leftTools  = new LayoutAnchorablePane(new LayoutAnchorable { Title = "Explorer", ContentId = "explorer" });
			var rightTools = new LayoutAnchorablePane(new LayoutAnchorable { Title = "Props",    ContentId = "props" });
			var docPane = manager.Layout.Descendents().OfType<LayoutDocumentPane>().First();
			var docPaneIdx = manager.Layout.RootPanel.Children.ToList().IndexOf(docPane);
			manager.Layout.RootPanel.Children.Insert(docPaneIdx, leftTools);
			manager.Layout.RootPanel.Children.Add(rightTools);

			var floating = new LayoutDocument { Title = "Floating", ContentId = "floating" };
			SimulateCommitDrop(manager, floating, CompassDropZone.Bottom);

			var root = manager.Layout.RootPanel;
			Assert.That(root.Children[0].Descendents().OfType<LayoutAnchorable>().Any(a => a.ContentId == "explorer"),
				Is.True, "Left tool pane must stay outermost left");
			Assert.That(root.Children[root.Children.Count - 1].Descendents().OfType<LayoutAnchorable>().Any(a => a.ContentId == "props"),
				Is.True, "Right tool pane must stay outermost right");

			Assert.That(manager.Layout.Descendents().OfType<LayoutDocument>().Count(), Is.EqualTo(2));
			Assert.That(CountEmptyDocumentPanes(manager.Layout), Is.Zero);
		}

		// ── Float-then-drop round-trip ────────────────────────────────────────────

		[Test]
		public void FloatThenDropTop_NoWhiteAreaAfterSecondFloat()
		{
			// Regression: float Doc1 → drop to Top → float Doc1 again → drop to Bottom
			// Bug was: the empty pane from step 3 remained, causing white area.
			var manager = MakeManager("Doc1", "Doc2");
			var doc1 = manager.Layout.Descendents().OfType<LayoutDocument>().First(d => d.Title == "Doc1");

			// Step 1: float out Doc1
			SimulateFloat(manager, doc1);
			Assert.That(CountEmptyDocumentPanes(manager.Layout), Is.Zero, "after first float");

			// Step 2: drop to Top
			SimulateCommitDrop(manager, doc1, CompassDropZone.Top);
			Assert.That(CountEmptyDocumentPanes(manager.Layout), Is.Zero, "after drop Top");
			Assert.That(manager.Layout.Descendents().OfType<LayoutDocument>().Count(), Is.EqualTo(2));

			// Step 3: float out Doc1 again
			SimulateFloat(manager, doc1);
			Assert.That(CountEmptyDocumentPanes(manager.Layout), Is.Zero,
				"REGRESSION: empty pane must be removed after second float-out");

			// Step 4: drop to Bottom
			SimulateCommitDrop(manager, doc1, CompassDropZone.Bottom);
			Assert.That(CountEmptyDocumentPanes(manager.Layout), Is.Zero, "after drop Bottom");
			Assert.That(manager.Layout.Descendents().OfType<LayoutDocument>().Count(), Is.EqualTo(2));
		}

		[Test]
		public void DropLeft_InsertsAdjacentToDocPane_NotOutsideToolPanes()
		{
			// Simulates the real layout: Horizontal root with tool panes on outer edges
			// and a document pane in the middle. Dropping Left should insert the new pane
			// to the LEFT of the existing doc pane — BETWEEN the left tool pane and the docs.
			var manager = MakeManager("Doc1");
			// Add anchorable panes to simulate the VS layout.
			var leftTools  = new LayoutAnchorablePane(new LayoutAnchorable { Title = "Explorer", ContentId = "explorer" });
			var rightTools = new LayoutAnchorablePane(new LayoutAnchorable { Title = "Props",    ContentId = "props" });
			var docPane = manager.Layout.Descendents().OfType<LayoutDocumentPane>().First();
			var docPaneIdx = manager.Layout.RootPanel.Children.ToList().IndexOf(docPane);
			manager.Layout.RootPanel.Children.Insert(docPaneIdx, leftTools);
			manager.Layout.RootPanel.Children.Add(rightTools);
			// Layout is now: [LeftTools][DocPane(Doc1)][RightTools]

			var floating = new LayoutDocument { Title = "Floating", ContentId = "floating" };
			SimulateCommitDrop(manager, floating, CompassDropZone.Left);

			// LeftTools and RightTools should still be outermost.
			var root = manager.Layout.RootPanel;
			var firstChild = root.Children[0];
			var lastChild  = root.Children[root.Children.Count - 1];
			Assert.That(firstChild.Descendents().OfType<LayoutAnchorable>().Any(a => a.ContentId == "explorer"),
				Is.True, "Left tool pane should remain the outermost left child");
			Assert.That(lastChild.Descendents().OfType<LayoutAnchorable>().Any(a => a.ContentId == "props"),
				Is.True, "Right tool pane should remain the outermost right child");

			// The floating doc should be inside (not at root outer edges).
			var floatingPaneParent = manager.Layout.Descendents()
				.OfType<LayoutDocumentPane>()
				.First(p => p.Children.Any(d => d.Title == "Floating"))
				.Parent;
			Assert.That(floatingPaneParent, Is.Not.SameAs(leftTools),
				"New doc pane must not displace the tool pane");
		}

		[Test]
		public void DropRight_InsertsAdjacentToDocPane_NotOutsideToolPanes()
		{
			var manager = MakeManager("Doc1");
			var leftTools  = new LayoutAnchorablePane(new LayoutAnchorable { Title = "Explorer", ContentId = "explorer" });
			var rightTools = new LayoutAnchorablePane(new LayoutAnchorable { Title = "Props",    ContentId = "props" });
			var docPane = manager.Layout.Descendents().OfType<LayoutDocumentPane>().First();
			var docPaneIdx = manager.Layout.RootPanel.Children.ToList().IndexOf(docPane);
			manager.Layout.RootPanel.Children.Insert(docPaneIdx, leftTools);
			manager.Layout.RootPanel.Children.Add(rightTools);

			var floating = new LayoutDocument { Title = "Floating", ContentId = "floating" };
			SimulateCommitDrop(manager, floating, CompassDropZone.Right);

			var root = manager.Layout.RootPanel;
			var firstChild = root.Children[0];
			var lastChild  = root.Children[root.Children.Count - 1];
			Assert.That(firstChild.Descendents().OfType<LayoutAnchorable>().Any(a => a.ContentId == "explorer"),
				Is.True, "Left tool pane should remain the outermost left child");
			Assert.That(lastChild.Descendents().OfType<LayoutAnchorable>().Any(a => a.ContentId == "props"),
				Is.True, "Right tool pane should remain the outermost right child");
		}
	}
}
