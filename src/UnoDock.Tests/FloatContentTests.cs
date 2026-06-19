using System.Linq;
using NUnit.Framework;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;
using Windows.Foundation;

namespace AvalonDockTest
{
	// Step 2 (docs/refactoring.md, Challenge 1): the float core shared by the
	// interactive tear-off and the programmatic Float(...) path — its CanFloat gate
	// and ContentFloating/ContentFloated events — exercised headlessly. Paths that
	// actually create/show a window are not driven here (no UI thread); the cancel
	// path returns before any window is created, so it is fully testable.
	[TestFixture]
	public class FloatContentTests
	{
		[Test]
		public void Float_RaisesContentFloating_WithTheContent()
		{
			var manager = CreateManagerWithSingleDocument();
			var content = manager.Layout.Descendents().OfType<LayoutDocument>().First();

			LayoutContent seen = null;
			manager.ContentFloating += (_, e) =>
			{
				seen = e.Content as LayoutContent;
				e.Cancel = true; // veto so no real window is created in the headless test
			};

			manager.Float(content);

			Assert.That(seen, Is.SameAs(content));
		}

		[Test]
		public void Float_WhenContentFloatingCanceled_DoesNotFloat()
		{
			var manager = CreateManagerWithSingleDocument();
			var content = manager.Layout.Descendents().OfType<LayoutDocument>().First();
			var originalPane = content.Parent;

			manager.ContentFloating += (_, e) => e.Cancel = true;
			var floatedRaised = false;
			manager.ContentFloated += (_, _) => floatedRaised = true;

			manager.Float(content);

			Assert.That(manager.Layout.FloatingWindows.Count(), Is.Zero);
			Assert.That(content.Parent, Is.SameAs(originalPane));
			Assert.That(floatedRaised, Is.False);
		}

		[Test]
		public void Float_WhenContentCannotFloat_DoesNotRaiseFloating()
		{
			var manager = CreateManagerWithSingleDocument();
			var content = manager.Layout.Descendents().OfType<LayoutDocument>().First();
			content.CanFloat = false;

			var floatingRaised = false;
			manager.ContentFloating += (_, _) => floatingRaised = true;

			manager.Float(content);

			Assert.That(floatingRaised, Is.False);
			Assert.That(manager.Layout.FloatingWindows.Count(), Is.Zero);
		}

		[Test]
		public void Float_WithBounds_SeedsFloatingSizeBeforeCancel()
		{
			var manager = CreateManagerWithSingleDocument();
			var content = manager.Layout.Descendents().OfType<LayoutDocument>().First();
			manager.ContentFloating += (_, e) => e.Cancel = true;

			manager.Float(content, new Rect(10, 20, 640, 480));

			Assert.That(content.FloatingWidth, Is.EqualTo(640));
			Assert.That(content.FloatingHeight, Is.EqualTo(480));
		}

		private static DockingManager CreateManagerWithSingleDocument()
		{
			var pane = new LayoutDocumentPane();
			pane.Children.Add(new LayoutDocument { Title = "Doc1", ContentId = "doc1" });
			var manager = new DockingManager { Layout = new LayoutRoot { RootPanel = new LayoutPanel() } };
			manager.Layout.RootPanel.Children.Add(pane);
			return manager;
		}
	}

	// Verifies the FakeNativeWindowDrag double drives the real NativeWindowDragBase
	// invariants — the guarantees the Template Method base exists to enforce.
	[TestFixture]
	public class NativeWindowDragBaseTests
	{
		[Test]
		public void BeginDrag_InstallsObserversBeforeHandOff_AndPrePositions()
		{
			var drag = new FakeNativeWindowDrag();

			drag.BeginDrag(new Point(200, 100), new Point(8, 6));

			Assert.That(drag.ObserversInstalledBeforeHandOff, Is.True);
			Assert.That(drag.InstallObserversCount, Is.EqualTo(1));
			Assert.That(drag.HandOffCount, Is.EqualTo(1));
			// Pre-position to cursor - grabOffset.
			Assert.That(drag.Moves.Single(), Is.EqualTo((192.0, 94.0)));
		}

		[Test]
		public void Ended_FiresExactlyOnce_AndSuppressesLaterMoving()
		{
			var drag = new FakeNativeWindowDrag();
			drag.BeginDrag(new Point(0, 0), new Point(0, 0));

			var moves = 0;
			var ends = 0;
			drag.Moving += _ => moves++;
			drag.Ended += _ => ends++;

			drag.Move(new Point(10, 10));
			drag.End(new Point(20, 20));
			drag.End(new Point(30, 30)); // ignored — idempotent
			drag.Move(new Point(40, 40)); // ignored — already ended

			Assert.That(moves, Is.EqualTo(1));
			Assert.That(ends, Is.EqualTo(1));
		}

		[Test]
		public void Dispose_RemovesObservers_AndIsIdempotent()
		{
			var drag = new FakeNativeWindowDrag();
			drag.BeginDrag(new Point(0, 0), new Point(0, 0));

			drag.Dispose();
			drag.Dispose();

			Assert.That(drag.RemoveObserversCount, Is.GreaterThanOrEqualTo(1));
		}
	}
}
