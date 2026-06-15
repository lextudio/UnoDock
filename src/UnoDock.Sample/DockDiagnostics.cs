// DevFlow diagnostic actions for the UnoDock sample.
// These static methods are discovered by DevFlow's InvokeAction scanner.
// They use DispatcherQueue.TryEnqueue internally to marshal to the UI thread.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;
using AvalonDock.Serializer.Xml;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace UnoDock.Sample;

public static class DockDiagnostics
{
	private static DockingManager? GetDM()
		=> (Microsoft.UI.Xaml.Application.Current as App)?.DockManager;

	// Run an action on the UI thread and wait for it to complete (up to 10s).
	private static T RunOnUI<T>(Func<T> fn)
	{
		T result = default!;
		Exception? ex = null;
		using var ready = new ManualResetEventSlim(false);

		var app = Microsoft.UI.Xaml.Application.Current as App;
		var win = app?.MainWindow as MainWindow;
		var dq = win?.DispatcherQueue;
		if (dq == null) throw new InvalidOperationException("no DispatcherQueue");

		dq.TryEnqueue(DispatcherQueuePriority.Normal, () =>
		{
			try { result = fn(); }
			catch (Exception e) { ex = e; }
			finally { ready.Set(); }
		});

		ready.Wait(TimeSpan.FromSeconds(10));
		if (ex != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
		return result;
	}

	private static readonly string LayoutPath =
		Path.Combine(Path.GetTempPath(), "unodock_sample_layout.xml");

	[DevFlowAction("dock-save-layout",
		Description = "Serialize current DockingManager layout to " + "/tmp/unodock_sample_layout.xml")]
	public static string SaveLayout() => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		var serializer = new XmlLayoutSerializer(dm);
		serializer.Serialize(LayoutPath);
		var size = new FileInfo(LayoutPath).Length;
		return $"saved {size} bytes to {LayoutPath}";
	});

	[DevFlowAction("dock-load-layout",
		Description = "Deserialize layout from " + "/tmp/unodock_sample_layout.xml")]
	public static string LoadLayout() => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		if (!File.Exists(LayoutPath)) return $"file not found: {LayoutPath}";

		var serializer = new XmlLayoutSerializer(dm);
		// Wire callback to restore content for each deserialized LayoutContent item.
		serializer.LayoutSerializationCallback += (s, args) =>
		{
			// Find content by ContentId from the original layout.
			var contentId = args.Model.ContentId;
			// For the sample app, map ContentId back to the original XAML-defined content.
			// In a real app this would rehydrate the view model.
			args.Content = FindOriginalContent(dm, contentId);
			if (args.Content == null) args.Cancel = true; // skip if content not found
		};

		serializer.Deserialize(LayoutPath);

		var docPanes = dm.Layout?.Descendents().OfType<LayoutDocumentPane>().ToList() ?? new();
		return $"loaded from {LayoutPath} docPanes={docPanes.Count} " +
		       $"tabCounts=[{string.Join(",", docPanes.Select(p => p.ChildrenCount))}]";
	});

	private static object? FindOriginalContent(DockingManager dm, string contentId)
	{
		// Walk all XAML-defined content in the current (post-load) layout to find a
		// matching ContentId — after deserialization the model is rebuilt but Content
		// properties are null (the XML doesn't store XAML subtrees). We need to pull
		// content from somewhere. For the sample we store originals in a static dict.
		return _contentCache.TryGetValue(contentId, out var c) ? c : null;
	}

	// Cache original UIElement content before serialization so it can be restored.
	private static readonly System.Collections.Generic.Dictionary<string, object> _contentCache = new();

	[DevFlowAction("dock-cache-content",
		Description = "Cache current document/anchorable content before save (call before dock-save-layout).")]
	public static string CacheContent() => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		_contentCache.Clear();
		foreach (var item in dm.Layout?.Descendents().OfType<LayoutContent>() ?? Enumerable.Empty<LayoutContent>())
		{
			if (item.ContentId != null && item.Content != null)
				_contentCache[item.ContentId] = item.Content;
		}
		return $"cached {_contentCache.Count} items: [{string.Join(",", _contentCache.Keys)}]";
	});

	[DevFlowAction("dock-open-flyout",
		Description = "Open the auto-hide flyout for an anchorable by ContentId. " +
		              "The anchorable must be auto-hidden. Returns flyout open status.")]
	public static string OpenFlyout(string contentId) => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		// Find the anchor side control that has this anchorable
		var sides = new[]
		{
			dm.LeftSidePanel, dm.RightSidePanel, dm.TopSidePanel, dm.BottomSidePanel
		};
		foreach (var side in sides)
		{
			if (side is AvalonDock.Controls.LayoutAnchorSideControl asc)
			{
				var anc = dm.Layout?.Descendents().OfType<LayoutAnchorable>()
					.FirstOrDefault(a => a.ContentId == contentId && a.IsAutoHidden);
				if (anc != null)
				{
					asc.OpenFlyoutFor(anc);
					return $"flyout opened for {anc.Title} isOpen={asc.IsFlyoutOpen}";
				}
			}
		}
		return $"anchorable '{contentId}' not found or not auto-hidden";
	});

	[DevFlowAction("dock-toggle-autohide",
		Description = "Toggle auto-hide for anchorable with given ContentId (moves to/from side panel).")]
	public static string ToggleAutoHide(string contentId) => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		var anc = dm.Layout?.Descendents().OfType<LayoutAnchorable>()
			.FirstOrDefault(a => a.ContentId == contentId);
		if (anc == null) return $"anchorable '{contentId}' not found";
		var wasDocked = !anc.IsAutoHidden;
		anc.ToggleAutoHide();
		var leftCount  = dm.Layout?.LeftSide?.Children.Sum(g => g.Children.Count) ?? 0;
		var rightCount = dm.Layout?.RightSide?.Children.Sum(g => g.Children.Count) ?? 0;
		return $"{anc.Title}: was docked={wasDocked} now isAutoHidden={anc.IsAutoHidden} " +
		       $"leftSide={leftCount} rightSide={rightCount}";
	});

	[DevFlowAction("dock-hide-anchorable",
		Description = "Hide the anchorable with the given ContentId (moves it to LayoutRoot.Hidden).")]
	public static string HideAnchorable(string contentId) => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		var anc = dm.Layout?.Descendents().OfType<LayoutAnchorable>()
			.FirstOrDefault(a => a.ContentId == contentId);
		if (anc == null) return $"anchorable '{contentId}' not found";
		anc.Hide();
		return $"hidden: {anc.Title} isHidden={anc.IsHidden} hiddenCount={dm.Layout?.Hidden?.Count ?? 0}";
	});

	[DevFlowAction("dock-show-hidden",
		Description = "Show the first hidden anchorable (restores to previous container).")]
	public static string ShowHidden() => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		var hidden = dm.Layout?.Hidden?.ToList() ?? new();
		if (hidden.Count == 0) return "no hidden anchorables";
		var anc = hidden[0];
		var title = anc.Title;
		anc.Show();
		return $"shown: {title} isVisible={anc.IsVisible} hiddenRemaining={dm.Layout?.Hidden?.Count ?? 0}";
	});

	[DevFlowAction("dock-list-hidden",
		Description = "Lists all hidden anchorables.")]
	public static string ListHidden() => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		var hidden = dm.Layout?.Hidden?.ToList() ?? new();
		if (hidden.Count == 0) return "no hidden anchorables";
		return string.Join(", ", hidden.Select(a => $"{a.Title}[{a.ContentId}]"));
	});

	[DevFlowAction("dock-float-active",
		Description = "Float the currently active document tab.")]
	public static string FloatActive() => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		var pane = dm.Layout?.Descendents().OfType<LayoutDocumentPane>().FirstOrDefault();
		var content = pane?.SelectedContent;
		if (content == null) return "no selected content";
		dm.StartDraggingFloatingWindowForContent(content);
		var fwCount = dm.FloatingWindows?.Count() ?? -1;
		var layoutFwCount = dm.Layout?.FloatingWindows?.Count ?? -1;
		return $"floated: {content.Title} fwList={fwCount} layoutFw={layoutFwCount}";
	});

	[DevFlowAction("dock-float-anchorable",
		Description = "Float an anchorable/tool tab by ContentId.")]
	public static string FloatAnchorable(string contentId) => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		var content = dm.Layout?.Descendents().OfType<LayoutAnchorable>()
			.FirstOrDefault(a => a.ContentId == contentId);
		if (content == null) return $"anchorable '{contentId}' not found";
		dm.StartDraggingFloatingWindowForContent(content);
		var fwCount = dm.FloatingWindows?.Count() ?? -1;
		var layoutFwCount = dm.Layout?.FloatingWindows?.Count ?? -1;
		return $"floated: {content.Title} fwList={fwCount} layoutFw={layoutFwCount}";
	});

	[DevFlowAction("dock-tracker-status",
		Description = "Returns NSWindow handles of floating windows and whether tracker is alive.")]
	public static string TrackerStatus() => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		var fws = dm.FloatingWindows?.ToList() ?? new();
		var layoutFws = dm.Layout?.FloatingWindows?.Count ?? -1;
		if (fws.Count == 0) return $"no floating windows (layoutFwCount={layoutFws})";
		return string.Join("; ", fws.Select(f =>
			$"nsWindow=0x{f.NsWindowHandle:X} isVisible={f.IsVisible} model={f.Model?.GetType().Name}"));
	});

	[DevFlowAction("dock-drag-status",
		Description = "Returns OverlayWindow drag state: active drop target type, manager size, tracker status.")]
	public static string DragStatus() => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		return dm.GetDragStatus();
	});

	[DevFlowAction("dock-tab-target-geometry",
		Description = "Reports document tab raw and tab-strip-host-clamped rectangles used for tab-header drop targets.")]
	public static string TabTargetGeometry() => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";

		var pane = EnumerateVisualsOfType<LayoutDocumentPaneControl>(dm)
			.FirstOrDefault(c => c.Visibility == Visibility.Visible && c.ActualWidth > 0 && c.ActualHeight > 0);
		if (pane == null) return "no visible LayoutDocumentPaneControl";

		var host = EnumerateVisualsOfType<FrameworkElement>(pane)
			.FirstOrDefault(fe => fe.Name == "PART_TabStripHost");
		var strip = EnumerateVisualsOfType<FrameworkElement>(pane)
			.FirstOrDefault(fe => fe.Name == "PART_TabStrip");
		if (host == null || strip == null) return $"missing tab visuals host={host != null} strip={strip != null}";
		if (!TryGetScreenRect(pane, out var paneRect) ||
		    !TryGetScreenRect(host, out var hostRect) ||
		    !TryGetScreenRect(strip, out var stripRect))
			return "failed to resolve pane/tab screen rectangles";

		var rows = new System.Collections.Generic.List<string>
		{
			$"pane={FormatRect(paneRect)} host={FormatRect(hostRect)} strip={FormatRect(stripRect)}"
		};

		var seen = new HashSet<string>();
		foreach (var tab in EnumerateVisualsOfType<FrameworkElement>(strip)
			.Where(fe => fe.Tag is LayoutDocument && fe.ActualWidth > 0 && fe.ActualHeight > 0))
		{
			if (tab.Tag is not LayoutDocument document) continue;
			var key = document.ContentId ?? document.Title ?? "<untitled>";
			if (!seen.Add(key)) continue;
			if (!TryGetScreenRect(tab, out var rawRect)) continue;
			if (!TryConstrainToHost(rawRect, hostRect, out var constrainedRect)) continue;

			var index = (document.Parent as LayoutDocumentPane)?.Children.IndexOf(document) ?? -1;
			rows.Add($"tab[{index}] title='{document.Title}' raw={FormatRect(rawRect)} constrained={FormatRect(constrainedRect)}");
		}

		return string.Join("; ", rows);
	});

	[DevFlowAction("dock-measure-overlay",
		Description = "Walks the OverlayWindow visual tree and returns actual rendered sizes of all elements (width x height). Call after dock-show-compass.")]
	public static string MeasureOverlay() => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		return dm.MeasureOverlayVisualTree();
	});

	[DevFlowAction("dock-reorder-tab",
		Description = "Move a document tab from oldIndex to newIndex in the first document pane. E.g. args=[0,2]")]
	public static string ReorderTab(int oldIndex, int newIndex) => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		var pane = dm.Layout?.Descendents().OfType<LayoutDocumentPane>().FirstOrDefault();
		if (pane == null) return "no document pane";
		if (oldIndex < 0 || oldIndex >= pane.Children.Count) return $"oldIndex {oldIndex} out of range ({pane.Children.Count} tabs)";
		if (newIndex < 0 || newIndex >= pane.Children.Count) return $"newIndex {newIndex} out of range";
		var title = pane.Children[oldIndex].Title;
		pane.Children.Move(oldIndex, newIndex);
		var newOrder = string.Join(",", pane.Children.Select(c => c.Title));
		return $"moved '{title}' from {oldIndex} to {newIndex} → [{newOrder}]";
	});

	[DevFlowAction("dock-select-anchorable",
		Description = "Select an anchorable pane tab by title to reproduce the double-active bug.")]
	public static string SelectAnchorable(string title) => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		var anc = dm.Layout?.Descendents().OfType<LayoutAnchorable>()
			.FirstOrDefault(a => a.Title == title);
		if (anc == null) return $"anchorable '{title}' not found";
		var pane = anc.Parent as LayoutAnchorablePane;
		if (pane == null) return "no parent pane";
		var idx = pane.Children.IndexOf(anc);
		pane.SelectedContentIndex = idx;
		return $"selected '{anc.Title}' idx={idx}  pane children=[{string.Join(",", pane.Children.Select(c => $"{c.Title}(sel={c.IsSelected},act={c.IsActive})"))}]";
	});

	[DevFlowAction("dock-active-content",
		Description = "Returns the current ActiveContent of the DockingManager.")]
	public static string GetActiveContent() => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		var active = dm.ActiveContent;
		var layoutActive = dm.Layout?.ActiveContent;
		return $"ActiveContent={active?.GetType().Name ?? "null"} " +
		       $"LayoutActive={layoutActive?.Title ?? "null"}";
	});

	[DevFlowAction("dock-manager-border-probe",
		Description = "Report DockingManager template-root visual chain and border values.")]
	public static string ManagerBorderProbe() => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		dm.ApplyTemplate();
		return string.Join("; ", EnumerateVisuals(dm, 3).Select(DescribeVisual));
	});

	[DevFlowAction("dock-border-scan",
		Description = "Report visible Border elements under DockingManager up to the given visual-tree depth.")]
	public static string BorderScan(int maxDepth = 8) => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		dm.ApplyTemplate();
		var rows = EnumerateVisuals(dm, maxDepth)
			.Where(item => item.Visual is Microsoft.UI.Xaml.Controls.Border)
			.Select(DescribeVisual)
			.ToList();
		return rows.Count == 0 ? "no Border visuals" : string.Join("; ", rows);
	});

	[DevFlowAction("dock-tool-pane-border-probe",
		Description = "Report the first visible tool pane visual tree, including border layers and tab items.")]
	public static string ToolPaneBorderProbe(int maxDepth = 10) => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";

		dm.ApplyTemplate();
		var pane = EnumerateVisualsOfType<LayoutAnchorablePaneControl>(dm)
			.FirstOrDefault(p => p.Visibility == Visibility.Visible && p.ActualWidth > 0 && p.ActualHeight > 0);
		if (pane == null) return "no visible LayoutAnchorablePaneControl";

		pane.ApplyTemplate();
		var rows = EnumerateVisuals(pane, maxDepth)
			.Where(item => item.Visual is Microsoft.UI.Xaml.Controls.Border
			               || item.Visual is LayoutAnchorablePaneControl
			               || item.Visual.GetType().Name.Contains("ItemsControl"))
			.Select(DescribeVisual)
			.ToList();
		return rows.Count == 0 ? "no tool pane visuals" : string.Join("; ", rows);
	});

	[DevFlowAction("dock-pane-selection",
		Description = "Reports selected content for document and anchorable panes.")]
	public static string GetPaneSelection() => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		var anchorable = dm.Layout?.Descendents().OfType<LayoutAnchorablePane>()
			.Select(p => $"A(children={p.ChildrenCount}, index={p.SelectedContentIndex}, selected={p.SelectedContent?.Title ?? "null"})");
		var documents = dm.Layout?.Descendents().OfType<LayoutDocumentPane>()
			.Select(p => $"D(children={p.ChildrenCount}, index={p.SelectedContentIndex}, selected={p.SelectedContent?.Title ?? "null"})");
		return string.Join("; ", (anchorable ?? Enumerable.Empty<string>()).Concat(documents ?? Enumerable.Empty<string>()));
	});

	[DevFlowAction("dock-tab-tearoff",
		Description = "Simulate dragging the active document tab downward to float it. " +
		              "Uses DevFlow global drag: presses on the tab header then moves cursor below tab strip.")]
	public static string TabTearoff() => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		// Find the currently selected tab's position in the DevFlow tree.
		var pane = dm.Layout?.Descendents().OfType<LayoutDocumentPane>().FirstOrDefault();
		if (pane == null) return "no document pane";
		var content = pane.SelectedContent;
		if (content == null) return "no selected content";
		// The actual drag is injected via the DevFlow /api/v1/ui/actions/drag endpoint
		// after getting the tab's screen position. Return the tab info so the caller
		// can set up the drag coordinates.
		return $"ready: tabTitle={content.Title} contentId={content.ContentId} " +
		       $"tabsCount={pane.ChildrenCount} fwBefore={dm.FloatingWindows?.Count() ?? 0}";
	});

	[DevFlowAction("dock-start-tracker",
		Description = "Manually start the drag tracker for the first floating window (simulates title-bar grab). " +
		              "Used in DevFlow tests to compensate for CGEvent button state not being visible to " +
		              "CGEventSourceButtonState(CombinedSession) during injected drags.")]
	public static string StartTracker() => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		var fws = dm.FloatingWindows?.ToList() ?? new();
		if (fws.Count == 0) return "no floating windows";
		dm.StartTrackerForFloatingWindow(fws[0]);
		return $"tracker started for nsWindow=0x{fws[0].NsWindowHandle:X}";
	});

	[DevFlowAction("dock-simulate-drop",
		Description = "Drop first floating window at given zone (Center/Left/Right/Top/Bottom/OuterLeft/OuterRight/OuterTop/OuterBottom).")]
	public static string SimulateDrop(string zone = "Center") => RunOnUI(() =>
	{
		var dm = GetDM();
		if (dm == null) return "no DockingManager";
		var fws = dm.FloatingWindows?.ToList() ?? new();
		if (fws.Count == 0) return "no floating windows";
		if (!Enum.TryParse<AvalonDock.Layout.CompassDropZone>(zone, true, out var z))
			z = AvalonDock.Layout.CompassDropZone.Center;
		dm.SimulateDrop(fws[0], z);

		// Report result
		var docPanes = dm.Layout?.Descendents().OfType<LayoutDocumentPane>().ToList() ?? new();
		var remaining = dm.FloatingWindows?.Count() ?? 0;
		return $"done zone={zone} floatingRemaining={remaining} docPanes={docPanes.Count} " +
		       $"tabCounts=[{string.Join(",", docPanes.Select(p => p.ChildrenCount))}]";
	});

	private static IEnumerable<TControl> EnumerateVisualsOfType<TControl>(DependencyObject root)
		where TControl : DependencyObject
	{
		if (root == null) yield break;
		var stack = new Stack<DependencyObject>();
		stack.Push(root);
		while (stack.Count > 0)
		{
			var current = stack.Pop();
			if (current is TControl matched) yield return matched;

			var count = VisualTreeHelper.GetChildrenCount(current);
			for (var i = 0; i < count; i++)
				stack.Push(VisualTreeHelper.GetChild(current, i));
		}
	}

	private static IEnumerable<(DependencyObject Visual, int Depth)> EnumerateVisuals(DependencyObject root, int maxDepth)
	{
		var stack = new Stack<(DependencyObject Visual, int Depth)>();
		stack.Push((root, 0));
		while (stack.Count > 0)
		{
			var (visual, depth) = stack.Pop();
			yield return (visual, depth);
			if (depth >= maxDepth) continue;

			var count = VisualTreeHelper.GetChildrenCount(visual);
			for (var i = count - 1; i >= 0; i--)
				stack.Push((VisualTreeHelper.GetChild(visual, i), depth + 1));
		}
	}

	private static string DescribeVisual((DependencyObject Visual, int Depth) item)
	{
		var visual = item.Visual;
		var name = visual is FrameworkElement fe && !string.IsNullOrWhiteSpace(fe.Name)
			? $"#{fe.Name}"
			: "";
		var size = visual is FrameworkElement sizeElement
			? $" size={sizeElement.ActualWidth:F1}x{sizeElement.ActualHeight:F1}"
			: "";
		var grid = visual is FrameworkElement gridElement
			? $" grid=({Microsoft.UI.Xaml.Controls.Grid.GetRow(gridElement)},{Microsoft.UI.Xaml.Controls.Grid.GetColumn(gridElement)},rs={Microsoft.UI.Xaml.Controls.Grid.GetRowSpan(gridElement)},cs={Microsoft.UI.Xaml.Controls.Grid.GetColumnSpan(gridElement)})"
			: "";
		var border = visual is Microsoft.UI.Xaml.Controls.Border borderElement
			? $" bg={BrushText(borderElement.Background)} border={BrushText(borderElement.BorderBrush)} thickness={borderElement.BorderThickness}"
			: "";
		var control = visual is Microsoft.UI.Xaml.Controls.Control controlElement
			? $" bg={BrushText(controlElement.Background)} border={BrushText(controlElement.BorderBrush)} thickness={controlElement.BorderThickness}"
			: "";

		return $"d{item.Depth}:{visual.GetType().Name}{name}{size}{grid}{border}{control}";
	}

	private static string BrushText(Brush? brush)
		=> brush switch
		{
			null => "null",
			SolidColorBrush solid => solid.Color.ToString(),
			_ => brush.ToString()
		};

	private static bool TryGetScreenRect(FrameworkElement element, out Windows.Foundation.Rect rect)
	{
		try
		{
			var origin = element.TransformToVisual(null)
				.TransformPoint(new Windows.Foundation.Point(0, 0));
			rect = new Windows.Foundation.Rect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
			return true;
		}
		catch
		{
			rect = default;
			return false;
		}
	}

	private static bool TryConstrainToHost(
		Windows.Foundation.Rect rect,
		Windows.Foundation.Rect host,
		out Windows.Foundation.Rect constrained)
	{
		var left = Math.Max(rect.Left, host.Left);
		var right = Math.Min(rect.Right, host.Right);
		if (right <= left)
		{
			constrained = default;
			return false;
		}

		constrained = new Windows.Foundation.Rect(left, host.Top, right - left, host.Height);
		return true;
	}

	private static string FormatRect(Windows.Foundation.Rect rect)
		=> $"({rect.Left:F1},{rect.Top:F1},{rect.Width:F1},{rect.Height:F1})";
}
