using NUnit.Framework;
using AvalonDock.Controls;

namespace AvalonDockTest
{
	// Pure preview-rect rules (decision layer; docs/refactoring.md Challenge 2).
	// These compute the highlighted rectangle shown while dragging over a target —
	// UI-agnostic math, fully headless-testable.
	[TestFixture]
	public class OverlayPreviewRulesTests
	{
		[Test]
		public void Pane_DockLeft_IsLeftHalf()
		{
			var ok = OverlayPreviewRules.TryComputePanePreviewRect(
				DropTargetType.DocumentPaneDockLeft, 200, 100, out var l, out var t, out var w, out var h);

			Assert.That(ok, Is.True);
			Assert.That((l, t, w, h), Is.EqualTo((0.0, 0.0, 100.0, 100.0)));
		}

		[Test]
		public void Pane_DockRight_IsRightHalf()
		{
			OverlayPreviewRules.TryComputePanePreviewRect(
				DropTargetType.AnchorablePaneDockRight, 200, 100, out var l, out var t, out var w, out var h);

			Assert.That((l, t, w, h), Is.EqualTo((100.0, 0.0, 100.0, 100.0)));
		}

		[Test]
		public void Pane_DockTop_IsTopHalf()
		{
			OverlayPreviewRules.TryComputePanePreviewRect(
				DropTargetType.DocumentPaneDockTop, 200, 100, out var l, out var t, out var w, out var h);

			Assert.That((l, t, w, h), Is.EqualTo((0.0, 0.0, 200.0, 50.0)));
		}

		[Test]
		public void Pane_DockBottom_IsBottomHalf()
		{
			OverlayPreviewRules.TryComputePanePreviewRect(
				DropTargetType.AnchorablePaneDockBottom, 200, 100, out var l, out var t, out var w, out var h);

			Assert.That((l, t, w, h), Is.EqualTo((0.0, 50.0, 200.0, 50.0)));
		}

		[Test]
		public void Pane_DockInside_IsFullPane()
		{
			OverlayPreviewRules.TryComputePanePreviewRect(
				DropTargetType.DocumentPaneDockInside, 200, 100, out var l, out var t, out var w, out var h);

			Assert.That((l, t, w, h), Is.EqualTo((0.0, 0.0, 200.0, 100.0)));
		}

		[Test]
		public void Pane_UnrecognizedTarget_ReturnsFalse()
		{
			var ok = OverlayPreviewRules.TryComputePanePreviewRect(
				DropTargetType.DockingManagerDockLeft, 200, 100, out _, out _, out _, out _);

			Assert.That(ok, Is.False);
		}

		[Test]
		public void Manager_DockLeft_ClampsToPreferredSize()
		{
			// preferred 120 < areaWidth/2 (500) → use 120.
			OverlayPreviewRules.TryComputeManagerPreviewRect(
				DropTargetType.DockingManagerDockLeft, 1000, 600, preferredSize: 120,
				out var l, out var t, out var w, out var h);

			Assert.That((l, t, w, h), Is.EqualTo((0.0, 0.0, 120.0, 600.0)));
		}

		[Test]
		public void Manager_DockLeft_ClampsToHalfWidth_WhenPreferredTooLarge()
		{
			// preferred 900 > areaWidth/2 (500) → clamp to 500.
			OverlayPreviewRules.TryComputeManagerPreviewRect(
				DropTargetType.DockingManagerDockLeft, 1000, 600, preferredSize: 900,
				out _, out _, out var w, out _);

			Assert.That(w, Is.EqualTo(500.0));
		}

		[Test]
		public void Manager_DockRight_IsRightAligned()
		{
			OverlayPreviewRules.TryComputeManagerPreviewRect(
				DropTargetType.DockingManagerDockRight, 1000, 600, preferredSize: 120,
				out var l, out var t, out var w, out var h);

			Assert.That((l, t, w, h), Is.EqualTo((880.0, 0.0, 120.0, 600.0)));
		}

		[Test]
		public void Manager_DockBottom_IsBottomAligned()
		{
			OverlayPreviewRules.TryComputeManagerPreviewRect(
				DropTargetType.DockingManagerDockBottom, 1000, 600, preferredSize: 100,
				out var l, out var t, out var w, out var h);

			Assert.That((l, t, w, h), Is.EqualTo((0.0, 500.0, 1000.0, 100.0)));
		}

		[Test]
		public void Manager_UnrecognizedTarget_ReturnsFalse()
		{
			var ok = OverlayPreviewRules.TryComputeManagerPreviewRect(
				DropTargetType.DocumentPaneDockInside, 1000, 600, 100, out _, out _, out _, out _);

			Assert.That(ok, Is.False);
		}
	}
}
