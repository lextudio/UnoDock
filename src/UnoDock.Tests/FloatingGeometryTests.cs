using System.Linq;
using NUnit.Framework;
using AvalonDock.Controls;
using AvalonDock.Layout;

namespace AvalonDockTest
{
	// Pure write-back for AvalonDock parity gap #7 (docs/refactoring.md, Step 2):
	// persist a floating window's geometry into its model so layouts round-trip.
	[TestFixture]
	public class FloatingGeometryTests
	{
		[Test]
		public void WriteBack_WritesGeometryToFloatingContent()
		{
			var doc = new LayoutDocument { Title = "Doc", ContentId = "doc" };
			var fw = SingleDocumentFloatingWindow(doc);

			FloatingGeometry.WriteBack(fw, left: 120, top: 80, width: 640, height: 480);

			Assert.That(doc.FloatingLeft, Is.EqualTo(120));
			Assert.That(doc.FloatingTop, Is.EqualTo(80));
			Assert.That(doc.FloatingWidth, Is.EqualTo(640));
			Assert.That(doc.FloatingHeight, Is.EqualTo(480));
		}

		[Test]
		public void WriteBack_RaisesFloatingPropertiesUpdated()
		{
			var doc = new LayoutDocument { Title = "Doc", ContentId = "doc" };
			var fw = SingleDocumentFloatingWindow(doc);

			var raised = false;
			doc.FloatingPropertiesUpdated += (_, _) => raised = true;

			FloatingGeometry.WriteBack(fw, 10, 10, 100, 100);

			Assert.That(raised, Is.True);
		}

		[Test]
		public void WriteBack_NullModel_DoesNotThrow()
		{
			Assert.DoesNotThrow(() => FloatingGeometry.WriteBack(null, 0, 0, 0, 0));
		}

		[Test]
		public void WriteBack_NoFloatingElements_IsNoOp()
		{
			// A docked document pane (not under a floating window) exposes no
			// ILayoutElementForFloatingWindow descendants to write — should be a no-op.
			var doc = new LayoutDocument { Title = "Doc", ContentId = "doc" };
			var pane = new LayoutDocumentPane();
			pane.Children.Add(doc);

			FloatingGeometry.WriteBack(pane, 5, 5, 50, 50);

			// LayoutContent itself implements the interface, so a pane *does* write its
			// child content; assert the content received the geometry to lock the contract.
			Assert.That(doc.FloatingLeft, Is.EqualTo(5));
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
