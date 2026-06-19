using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;

namespace AvalonDock.Controls
{
	// Pure, headless-testable decision layer for overlay drop-area selection.
	//
	// This is the "decision" half of the drag/drop split (see docs/refactoring.md):
	// given a set of candidate drop areas and a drag point — both already in the
	// same coordinate space — it decides which areas should be active. It owns no
	// WinUI/OS state, so it can be unit-tested without a real window, cursor, or
	// visual tree.
	//
	// DockingManager.SelectActiveOverlayAreas delegates here, supplying the single
	// UI-dependent fact (is the pointer over a splitter?) as a bool.
	internal static class OverlayHitTester
	{
		// Hit-zone inflation per area type. Full-compass outer buttons can render
		// slightly outside pane bounds, so pane/group zones are expanded to keep
		// the area active while the pointer is over such a button.
		private const double DocumentPaneInflate = 64.0;
		private const double DocumentPaneGroupInflate = 64.0;
		private const double AnchorablePaneInflate = 48.0;

		/// <summary>
		/// Decide which drop areas should be active for the given drag point.
		/// </summary>
		/// <param name="areas">All candidate areas (screen/host coordinates).</param>
		/// <param name="dragPoint">Drag point in the same coordinate space as the areas' DetectionRects.</param>
		/// <param name="pointerOverSplitter">
		/// True when the pointer is over a layout splitter. Mirrors WPF: only the
		/// manager outer targets stay active in that case.
		/// </param>
		public static IReadOnlyList<IDropArea> SelectActiveAreas(
			IEnumerable<IDropArea> areas,
			Point dragPoint,
			bool pointerOverSplitter)
		{
			var candidates = areas.Where(a => IsWithinHitZone(a, dragPoint)).ToList();
			if (candidates.Count == 0)
				return candidates;

			// WPF behavior: when the pointer is on a splitter, only manager outer targets stay active.
			if (pointerOverSplitter)
				return candidates.Where(a => a.Type == DropAreaType.DockingManager).ToList();

			var paneCandidates = candidates.Where(IsPaneDropAreaType).ToList();
			if (paneCandidates.Count <= 1)
				return candidates;

			// If the cursor is inside one/more pane rects, prefer the tightest one (most specific).
			var strict = paneCandidates.Where(a => a.DetectionRect.Contains(dragPoint)).ToList();
			var bestPane = (strict.Count > 0 ? strict : paneCandidates)
				.OrderBy(a => RectDistanceToPointSquared(a.DetectionRect, dragPoint))
				.ThenBy(a => a.DetectionRect.Width * a.DetectionRect.Height)
				.FirstOrDefault();

			if (bestPane == null)
				return candidates;

			return candidates
				.Where(a => a.Type == DropAreaType.DockingManager || ReferenceEquals(a, bestPane))
				.ToList();
		}

		public static bool IsPaneDropAreaType(IDropArea area)
			=> area.Type == DropAreaType.DocumentPane
				|| area.Type == DropAreaType.DocumentPaneGroup
				|| area.Type == DropAreaType.AnchorablePane;

		public static double RectDistanceToPointSquared(Rect rect, Point point)
		{
			var dx = 0.0;
			if (point.X < rect.Left) dx = rect.Left - point.X;
			else if (point.X > rect.Right) dx = point.X - rect.Right;

			var dy = 0.0;
			if (point.Y < rect.Top) dy = rect.Top - point.Y;
			else if (point.Y > rect.Bottom) dy = point.Y - rect.Bottom;

			return dx * dx + dy * dy;
		}

		public static bool IsWithinHitZone(IDropArea area, Point dragPoint)
		{
			var rect = area.DetectionRect;
			if (rect.Contains(dragPoint))
				return true;

			var inflate = area.Type switch
			{
				DropAreaType.DocumentPane => DocumentPaneInflate,
				DropAreaType.DocumentPaneGroup => DocumentPaneGroupInflate,
				DropAreaType.AnchorablePane => AnchorablePaneInflate,
				_ => 0.0,
			};

			if (inflate <= 0)
				return false;

			var expanded = new Rect(
				rect.X - inflate,
				rect.Y - inflate,
				rect.Width + inflate * 2,
				rect.Height + inflate * 2);
			return expanded.Contains(dragPoint);
		}
	}
}
