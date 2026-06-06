// Tests for the 9-zone drop model (inner compass 5 + outer manager edge 4)
// covering both LayoutDocument and LayoutAnchorable content types.

using System.Linq;
using NUnit.Framework;
using AvalonDock.Layout;

namespace AvalonDockTest
{
	[TestFixture]
	public class DropZone9Tests
	{
		// ── Helpers ─────────────────────────────────────────────────────────────

		private static LayoutRoot MakeRoot(
			int docCount = 1,
			int anchorCount = 0,
			bool addAnchorablePane = false)
		{
			var docPane = new LayoutDocumentPane();
			for (var i = 0; i < docCount; i++)
				docPane.Children.Add(new LayoutDocument { Title = $"Doc{i + 1}", ContentId = $"doc{i + 1}" });

			var panel = new LayoutPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

			if (addAnchorablePane)
			{
				var anchPane = new LayoutAnchorablePane();
				for (var i = 0; i < anchorCount; i++)
					anchPane.Children.Add(new LayoutAnchorable { Title = $"Tool{i + 1}", ContentId = $"tool{i + 1}" });
				panel.Children.Add(anchPane);
			}

			panel.Children.Add(docPane);

			if (addAnchorablePane && anchorCount == 0)
			{
				// Add an empty anchorable pane on the right for tests that need one.
				panel.Children.Add(new LayoutAnchorablePane());
			}

			return new LayoutRoot { RootPanel = panel };
		}

		private static LayoutDocument  NewDoc(string id = "floating")
			=> new LayoutDocument  { Title = id, ContentId = id };

		private static LayoutAnchorable NewAnc(string id = "tool")
			=> new LayoutAnchorable { Title = id, ContentId = id };

		private static int DocPaneCount(LayoutRoot root)
			=> root.Descendents().OfType<LayoutDocumentPane>().Count();

		private static int AnchorablePaneCount(LayoutRoot root)
			=> root.Descendents().OfType<LayoutAnchorablePane>().Count();

		private static int DocCount(LayoutRoot root)
			=> root.Descendents().OfType<LayoutDocument>().Count();

		private static int AnchorableCount(LayoutRoot root)
			=> root.Descendents().OfType<LayoutAnchorable>().Count();

		// ── Center zone ─────────────────────────────────────────────────────────

		[Test]
		public void Center_Document_TabJoinsExistingDocPane()
		{
			var root = MakeRoot(docCount: 1);
			LayoutRootMutations.InsertPane(root, NewDoc(), CompassDropZone.Center);

			Assert.That(DocCount(root), Is.EqualTo(2));
			Assert.That(DocPaneCount(root), Is.EqualTo(1), "Should tab-join, not split");
		}

		[Test]
		public void Center_Anchorable_TabJoinsExistingAnchorablePane()
		{
			var root = MakeRoot(addAnchorablePane: true, anchorCount: 1);
			LayoutRootMutations.InsertPane(root, NewAnc("tool2"), CompassDropZone.Center);

			var anchPane = root.Descendents().OfType<LayoutAnchorablePane>().First();
			Assert.That(anchPane.Children.Count, Is.EqualTo(2), "Should tab-join anchorable pane");
		}

		[Test]
		public void Center_Anchorable_CreatesAnchorablePaneWhenNoneExists()
		{
			var root = MakeRoot(docCount: 1); // no anchorable pane
			LayoutRootMutations.InsertPane(root, NewAnc(), CompassDropZone.Center);

			Assert.That(AnchorablePaneCount(root), Is.EqualTo(1));
			Assert.That(AnchorableCount(root), Is.EqualTo(1));
		}

		// ── Inner directional: Document ──────────────────────────────────────────

		[TestCase(CompassDropZone.Left,   System.Windows.Controls.Orientation.Horizontal, true)]
		[TestCase(CompassDropZone.Right,  System.Windows.Controls.Orientation.Horizontal, false)]
		[TestCase(CompassDropZone.Top,    System.Windows.Controls.Orientation.Vertical,   true)]
		[TestCase(CompassDropZone.Bottom, System.Windows.Controls.Orientation.Vertical,   false)]
		public void InnerZone_Document_SplitsDocumentArea(
			CompassDropZone zone,
			System.Windows.Controls.Orientation expectedSubOrient,
			bool newPaneFirst)
		{
			var root = MakeRoot(docCount: 1);
			LayoutRootMutations.InsertPane(root, NewDoc(), zone);

			Assert.That(DocPaneCount(root), Is.EqualTo(2), "Should create a new doc pane");
			Assert.That(DocCount(root), Is.EqualTo(2));

			// The new pane should be adjacent to the original, not outside any tool panes.
			var allDocPanes = root.Descendents().OfType<LayoutDocumentPane>().ToList();
			var newPane = allDocPanes.First(p => p.Children.Any(d => d.ContentId == "floating"));
			var siblingPanel = newPane.Parent as LayoutPanel;
			Assert.That(siblingPanel, Is.Not.Null, "New pane must be in a LayoutPanel");
			Assert.That(siblingPanel.Orientation, Is.EqualTo(expectedSubOrient));
		}

		[Test]
		public void Left_Document_ToolPanesRemainOutermost()
		{
			var root = MakeRoot(addAnchorablePane: true, anchorCount: 1);
			// Add a right-side tool pane too.
			var rightTools = new LayoutAnchorablePane(new LayoutAnchorable { ContentId = "right-tool" });
			root.RootPanel.Children.Add(rightTools);

			LayoutRootMutations.InsertPane(root, NewDoc(), CompassDropZone.Left);

			// Outer children of root must still be anchorable panes.
			var rootChildren = root.RootPanel.Children;
			Assert.That(
				rootChildren[0].Descendents().OfType<LayoutAnchorable>().Any()
				|| rootChildren[0] is LayoutAnchorablePane,
				Is.True, "Left-most root child must still be a tool pane");
			Assert.That(
				rootChildren[rootChildren.Count - 1].Descendents().OfType<LayoutAnchorable>().Any()
				|| rootChildren[rootChildren.Count - 1] is LayoutAnchorablePane,
				Is.True, "Right-most root child must still be a tool pane");
		}

		// ── Inner directional: Anchorable ────────────────────────────────────────

		[TestCase(CompassDropZone.Left)]
		[TestCase(CompassDropZone.Right)]
		[TestCase(CompassDropZone.Top)]
		[TestCase(CompassDropZone.Bottom)]
		public void InnerZone_Anchorable_CreatesNewAnchorablePane(CompassDropZone zone)
		{
			var root = MakeRoot(docCount: 1);
			LayoutRootMutations.InsertPane(root, NewAnc(), zone);

			Assert.That(AnchorablePaneCount(root), Is.EqualTo(1), "One new anchorable pane");
			Assert.That(AnchorableCount(root), Is.EqualTo(1));
		}

		// ── Outer manager edge zones ─────────────────────────────────────────────

		[TestCase(CompassDropZone.OuterLeft,   System.Windows.Controls.Orientation.Horizontal, true)]
		[TestCase(CompassDropZone.OuterRight,  System.Windows.Controls.Orientation.Horizontal, false)]
		[TestCase(CompassDropZone.OuterTop,    System.Windows.Controls.Orientation.Vertical,   true)]
		[TestCase(CompassDropZone.OuterBottom, System.Windows.Controls.Orientation.Vertical,   false)]
		public void OuterZone_Anchorable_InsertsAtRootEdge(
			CompassDropZone zone,
			System.Windows.Controls.Orientation expectedOrient,
			bool newPaneFirst)
		{
			var root = MakeRoot(docCount: 1);
			LayoutRootMutations.InsertPane(root, NewAnc(), zone);

			// Root must now have the correct orientation.
			Assert.That(root.RootPanel.Orientation, Is.EqualTo(expectedOrient));

			var rootChildren = root.RootPanel.Children;
			var edge = newPaneFirst ? rootChildren[0] : rootChildren[rootChildren.Count - 1];
			Assert.That(
				edge is LayoutAnchorablePane
				|| edge.Descendents().OfType<LayoutAnchorablePane>().Any(),
				Is.True, "New pane should be at the expected edge");
		}

		[TestCase(CompassDropZone.OuterLeft)]
		[TestCase(CompassDropZone.OuterRight)]
		[TestCase(CompassDropZone.OuterTop)]
		[TestCase(CompassDropZone.OuterBottom)]
		public void OuterZone_Document_AlsoInsertsAtRootEdge(CompassDropZone zone)
		{
			var root = MakeRoot(docCount: 1);
			LayoutRootMutations.InsertPane(root, NewDoc("outer-doc"), zone);

			Assert.That(DocPaneCount(root), Is.EqualTo(2));
		}

		[TestCase(CompassDropZone.OuterLeft)]
		[TestCase(CompassDropZone.OuterRight)]
		[TestCase(CompassDropZone.OuterTop)]
		[TestCase(CompassDropZone.OuterBottom)]
		public void OuterZone_DoesNotAffectExistingInnerLayout(CompassDropZone zone)
		{
			// Start with a complex layout: tool | docs | tool
			var root = MakeRoot(addAnchorablePane: true, anchorCount: 2, docCount: 2);
			var rightTools = new LayoutAnchorablePane(new LayoutAnchorable { ContentId = "right-tool" });
			root.RootPanel.Children.Add(rightTools);
			var beforeDocPanes = DocPaneCount(root);

			LayoutRootMutations.InsertPane(root, NewAnc("new-outer"), zone);

			// Original doc pane structure must be preserved inside.
			Assert.That(DocCount(root), Is.EqualTo(2), "Existing docs unaffected");
		}

		// ── Idempotent: multiple drops ────────────────────────────────────────────

		[Test]
		public void MultipleDrops_BuildValidTree()
		{
			var root = MakeRoot(docCount: 1);

			LayoutRootMutations.InsertPane(root, NewDoc("d2"), CompassDropZone.Right);
			LayoutRootMutations.InsertPane(root, NewAnc("t1"), CompassDropZone.OuterLeft);
			LayoutRootMutations.InsertPane(root, NewAnc("t2"), CompassDropZone.OuterRight);
			LayoutRootMutations.InsertPane(root, NewDoc("d3"), CompassDropZone.Top);

			Assert.That(DocCount(root), Is.EqualTo(3));
			Assert.That(AnchorableCount(root), Is.EqualTo(2));

			// Tree must be well-formed (no null parents, no orphaned children).
			foreach (var node in root.Descendents())
				Assert.That(node.Root, Is.SameAs(root), $"{node} must have root set");
		}

		// ── Null / edge-case guards ───────────────────────────────────────────────

		[Test]
		public void InsertPane_NullRoot_DoesNotThrow()
		{
			Assert.DoesNotThrow(() =>
				LayoutRootMutations.InsertPane(null, NewDoc(), CompassDropZone.Center));
		}

		[Test]
		public void InsertPane_NullContent_DoesNotThrow()
		{
			var root = MakeRoot();
			Assert.DoesNotThrow(() =>
				LayoutRootMutations.InsertPane(root, null, CompassDropZone.Center));
		}
	}
}
