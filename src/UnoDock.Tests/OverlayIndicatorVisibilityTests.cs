using NUnit.Framework;
using AvalonDock.Controls;
using System.Windows.Controls;

namespace AvalonDockTest
{
	[TestFixture]
	public class OverlayIndicatorVisibilityTests
	{
		[Test]
		public void AnchorablePane_HidesCenter_WhenDropIntoIsIllegal()
		{
			var visibility = OverlayIndicatorVisibilityRules.ForAnchorablePane(canDropInto: false);

			Assert.That(visibility.CenterVisible, Is.False);
			Assert.That(visibility.InnerLeft, Is.True);
			Assert.That(visibility.InnerTop, Is.True);
			Assert.That(visibility.InnerRight, Is.True);
			Assert.That(visibility.InnerBottom, Is.True);
		}

		[Test]
		public void DocumentPaneGroup_ShowsOnlyCenter_WhenDropIntoIsLegal_ForDocumentDrag()
		{
			var visibility = OverlayIndicatorVisibilityRules.ForDocumentPaneGroup(canDropInto: true, isAnchorableDrag: false);

			Assert.That(visibility.CenterVisible, Is.True);
			Assert.That(visibility.InnerLeft, Is.False);
			Assert.That(visibility.InnerTop, Is.False);
			Assert.That(visibility.InnerRight, Is.False);
			Assert.That(visibility.InnerBottom, Is.False);
		}

		[Test]
		public void DocumentPaneGroup_HidesCenter_ForAnchorableDrag()
		{
			// An anchorable dragged over the document pane group cannot dock INTO it as a document
			// tab, so the group center is hidden (ILSpy shows only the outer manager edge arrows).
			var visibility = OverlayIndicatorVisibilityRules.ForDocumentPaneGroup(canDropInto: true, isAnchorableDrag: true);

			Assert.That(visibility.CenterVisible, Is.False);
			Assert.That(visibility.InnerLeft, Is.False);
			Assert.That(visibility.InnerTop, Is.False);
			Assert.That(visibility.InnerRight, Is.False);
			Assert.That(visibility.InnerBottom, Is.False);
		}

		[Test]
		public void DocumentPane_AnchorableDrag_ShowsOnlyAsAnchorableRing_NoInnerDiamondOrCenter()
		{
			// An anchorable dragged over a document pane can only dock as an anchorable around the
			// document area (the outer ring). The inner document-split diamond and the center "dock
			// into" target must be hidden — matching WPF AvalonDock/ILSpy, which shows only the outer
			// edge arrows (here AsLeft, because the pane is first in a horizontal group).
			var visibility = OverlayIndicatorVisibilityRules.ForDocumentPane(
				canDropInto: true,
				isAnchorableDrag: true,
				paneHostedInFloatingWindow: false,
				allowMixedOrientation: false,
				parentOrientation: Orientation.Horizontal,
				visibleSiblingCount: 2,
				isFirstVisible: true,
				isLastVisible: false,
				paneChildrenCount: 1);

			Assert.That(visibility.CenterVisible, Is.False);
			Assert.That(visibility.InnerLeft, Is.False);
			Assert.That(visibility.InnerTop, Is.False);
			Assert.That(visibility.InnerRight, Is.False);
			Assert.That(visibility.InnerBottom, Is.False);
			Assert.That(visibility.AsLeft, Is.True);
			Assert.That(visibility.AsRight, Is.False);
			Assert.That(visibility.AsTop, Is.False);
			Assert.That(visibility.AsBottom, Is.False);
		}

		[Test]
		public void DocumentPane_SinglePaneAnchorableDrag_ShowsFullAsAnchorableRing_NoInnerOrCenter()
		{
			// ILSpy's main case: a single document pane (the decompiler). Dragging the search
			// anchorable over it shows all four outer edge arrows and nothing in the centre.
			var visibility = OverlayIndicatorVisibilityRules.ForDocumentPane(
				canDropInto: true,
				isAnchorableDrag: true,
				paneHostedInFloatingWindow: false,
				allowMixedOrientation: false,
				parentOrientation: Orientation.Horizontal,
				visibleSiblingCount: 1,
				isFirstVisible: true,
				isLastVisible: true,
				paneChildrenCount: 1);

			Assert.That(visibility.CenterVisible, Is.False);
			Assert.That(visibility.InnerLeft, Is.False);
			Assert.That(visibility.InnerTop, Is.False);
			Assert.That(visibility.InnerRight, Is.False);
			Assert.That(visibility.InnerBottom, Is.False);
			Assert.That(visibility.AsLeft, Is.True);
			Assert.That(visibility.AsTop, Is.True);
			Assert.That(visibility.AsRight, Is.True);
			Assert.That(visibility.AsBottom, Is.True);
		}

		[Test]
		public void DocumentPane_NonAnchorableDrag_HidesAsAnchorableRing()
		{
			var visibility = OverlayIndicatorVisibilityRules.ForDocumentPane(
				canDropInto: true,
				isAnchorableDrag: false,
				paneHostedInFloatingWindow: false,
				allowMixedOrientation: true,
				parentOrientation: Orientation.Horizontal,
				visibleSiblingCount: 3,
				isFirstVisible: false,
				isLastVisible: false,
				paneChildrenCount: 1);

			Assert.That(visibility.AsLeft, Is.False);
			Assert.That(visibility.AsTop, Is.False);
			Assert.That(visibility.AsRight, Is.False);
			Assert.That(visibility.AsBottom, Is.False);
		}
	}
}
