using System.Linq;
using NUnit.Framework;
using AvalonDock.Controls;
using AvalonDock.Layout;

namespace AvalonDockTest
{
	// Pure decision for AvalonDock parity gap #6 (docs/refactoring.md, Step 2):
	// reuse an existing single-item floating window for the last torn-out document.
	[TestFixture]
	public class FloatingWindowReuseTests
	{
		[Test]
		public void FindReusable_ReturnsWindow_WhenItHostsOnlyThatDocument()
		{
			var doc = new LayoutDocument { Title = "Doc", ContentId = "doc" };
			var fw = SingleDocumentFloatingWindow(doc);

			var result = FloatingWindowReuse.FindReusable(new[] { fw }, doc);

			Assert.That(result, Is.SameAs(fw));
		}

		[Test]
		public void FindReusable_ReturnsNull_WhenContentNotAloneInItsPane()
		{
			// Two documents share the floating window's pane → tearing one out is not
			// "the last item", so no reuse.
			var doc1 = new LayoutDocument { Title = "Doc1", ContentId = "doc1" };
			var doc2 = new LayoutDocument { Title = "Doc2", ContentId = "doc2" };
			var pane = new LayoutDocumentPane();
			pane.Children.Add(doc1);
			pane.Children.Add(doc2);
			var group = new LayoutDocumentPaneGroup();
			group.Children.Add(pane);
			var fw = new LayoutDocumentFloatingWindow { RootPanel = group };

			var result = FloatingWindowReuse.FindReusable(new[] { fw }, doc1);

			Assert.That(result, Is.Null);
		}

		[Test]
		public void FindReusable_ReturnsNull_WhenNoFloatingWindowHostsTheContent()
		{
			var docked = new LayoutDocument { Title = "Docked", ContentId = "docked" };
			var dockedPane = new LayoutDocumentPane();
			dockedPane.Children.Add(docked);

			var other = new LayoutDocument { Title = "Other", ContentId = "other" };
			var fw = SingleDocumentFloatingWindow(other);

			var result = FloatingWindowReuse.FindReusable(new[] { fw }, docked);

			Assert.That(result, Is.Null);
		}

		[Test]
		public void FindReusable_ReturnsNull_ForNullInputs()
		{
			Assert.That(FloatingWindowReuse.FindReusable(null, new LayoutDocument()), Is.Null);
			Assert.That(FloatingWindowReuse.FindReusable(Enumerable.Empty<LayoutFloatingWindow>(), null), Is.Null);
		}

		private static LayoutDocumentFloatingWindow SingleDocumentFloatingWindow(LayoutDocument doc)
		{
			var pane = new LayoutDocumentPane();
			pane.Children.Add(doc);
			var group = new LayoutDocumentPaneGroup();
			group.Children.Add(pane);
			return new LayoutDocumentFloatingWindow { RootPanel = group };
		}
	}
}
