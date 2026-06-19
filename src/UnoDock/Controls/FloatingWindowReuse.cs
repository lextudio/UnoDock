using System.Collections.Generic;
using System.Linq;
using AvalonDock.Layout;

namespace AvalonDock.Controls
{
	// Pure, headless-testable decision for AvalonDock parity gap #6 (drag-to-float.md):
	// when the last document is torn out of a single-item floating window, reuse that
	// existing window instead of creating a redundant new one.
	//
	// Mirrors the WPF reference branch in AvalonDock.DockingManager
	// .StartDraggingFloatingWindowForContent (the "For last document re-use floating
	// window" loop). DockingManager maps the returned model back to its control.
	internal static class FloatingWindowReuse
	{
		/// <summary>
		/// Returns the floating-window model that should be reused for floating
		/// <paramref name="content"/>, or null when a fresh window must be created.
		/// Reuse applies only when the content is alone in its current pane and an
		/// existing floating window hosts exactly that one content.
		/// </summary>
		public static LayoutFloatingWindow FindReusable(
			IEnumerable<LayoutFloatingWindow> floatingModels, LayoutContent content)
		{
			// Only the "last item in its pane" case can reuse — matches WPF's
			// contentModel.Parent.ChildrenCount == 1 guard.
			if (content?.Parent == null || content.Parent.ChildrenCount != 1)
				return null;
			if (floatingModels == null)
				return null;

			foreach (var fw in floatingModels)
			{
				var hostsContent = fw.Descendents().OfType<LayoutDocument>().Any(doc => doc == content);
				if (!hostsContent)
					continue;

				// Found the window hosting this content; reuse it only if it holds
				// exactly one content item total. (WPF breaks after the first match.)
				var itemCount = fw.Descendents().OfType<LayoutDocument>().Count()
					+ fw.Descendents().OfType<LayoutAnchorable>().Count();
				return itemCount == 1 ? fw : null;
			}

			return null;
		}
	}
}
