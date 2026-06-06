// Compatibility shims for types referenced by the shared AvalonDock model layer.
// Each section is intentionally minimal: the Uno port uses different mechanisms
// (compass overlay, code-behind tab rendering) so these types only need to satisfy
// the compiler — not provide full WPF behaviour.

namespace AvalonDock
{
	// WeakReferenceExtensions: utility used by the shared model layer (Layout/*.cs).
	internal static class WeakReferenceExtensions
	{
		public static V GetValueOrDefault<V>(this System.WeakReference wr)
			=> wr == null || !wr.IsAlive ? default : (V)wr.Target;
	}
}

namespace AvalonDock.Controls
{
	// ── Tab header types ─────────────────────────────────────────────────────
	// LayoutContent.TabItem stores a LayoutDocumentTabItem reference so the WPF
	// control layer can position tab headers. In the Uno port tab headers are
	// rendered via DataTemplate in LayoutDocumentPaneControl / LayoutAnchorablePaneControl
	// (code-behind), so these types only need to compile.
	public class LayoutDocumentTabItem : Microsoft.UI.Xaml.Controls.ContentControl
	{
		public AvalonDock.Layout.LayoutContent Model { get; set; }
	}

	// LayoutContent.IsSelected calls CancelMouseLeave() to cancel WPF auto-hide
	// mouse-leave timers. Uno uses DispatcherTimer in LayoutAnchorSideControl instead.
	public class LayoutAnchorableTabItem : Microsoft.UI.Xaml.Controls.ContentControl
	{
		public static void CancelMouseLeave() { }
	}

	// ── Implemented in their own files ──────────────────────────────────────
	// LayoutItem / LayoutAnchorableItem / LayoutDocumentItem → Controls/LayoutItem.cs
	// LayoutAnchorSideControl                                → Controls/LayoutAnchorSideControl.cs
	// LayoutAnchorGroupControl                               → Controls/LayoutAnchorGroupControl.cs
	// LayoutAnchorControl                                    → Controls/LayoutAnchorControl.cs
	// LayoutFloatingWindowControl (and subclasses)           → Controls/LayoutFloatingWindowControl.cs

	// ── Overlay / drop-area contracts ────────────────────────────────────────
	// The concrete Uno OverlayWindow implementation lives in Controls/OverlayWindow.uno.cs.
	public enum DropAreaType
	{
		DockingManager,
		DocumentPane,
		DocumentPaneGroup,
		AnchorablePane,
	}

	public interface IDropArea
	{
		Windows.Foundation.Rect DetectionRect { get; }
		DropAreaType Type { get; }
		Windows.Foundation.Point TransformToDeviceDPI(Windows.Foundation.Point dragPosition);
	}
}

namespace System.Windows.Media
{
	// Geometry: return type of IDropTarget.GetPreviewPath() in the shared model.
	// Drop previews in Uno use CompassOverlay._preview (a WinUI Border) instead.
	public abstract class Geometry { }
}
