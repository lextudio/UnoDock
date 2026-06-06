// Converter used in tab-strip DataTemplates to highlight the active tab:
// receives the LayoutContent model item, compares it to the pane control's
// SelectedItem, and returns the active or inactive tab background brush.
// Since DataTemplate has no direct access to the parent ItemsControl's
// SelectedItem, we use a different trick: a custom ItemsControl subclass
// that sets each container's IsSelected property, or simpler — we re-render
// the whole tab strip whenever selection changes.
//
// For Phase 3 we use the simpler approach: each tab Border has a Tag set to
// the LayoutContent, and code-behind (OnApplyTemplate's PointerPressed handler)
// updates the visual state by walking children. The template itself uses a
// plain dark background; the active tab gets a different background via
// VisualStateManager or direct property set in the OnModelPropertyChanged update.

// This file is a placeholder; the real highlighting is done by rebuilding tab
// strip items in UpdateTabHighlight() called from SyncSelection().

namespace AvalonDock.Controls { }
