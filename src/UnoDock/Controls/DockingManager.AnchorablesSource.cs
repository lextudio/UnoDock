// AnchorablesSource MVVM binding for UnoDock's DockingManager — the anchorable (tool-pane) analogue
// of DockingManager.DocumentsSource.cs. Setting AnchorablesSource to an IEnumerable (optionally
// INotifyCollectionChanged) creates one LayoutAnchorable per source item (Content = the view-model)
// and keeps the layout in sync as the collection changes.
//
// Unlike documents, tool-pane models carry an IsVisible flag and live in multiple panes:
//   * placement defers to ILayoutUpdateStrategy.BeforeInsertAnchorable (the consumer routes by
//     ContentId), falling back to the first LayoutAnchorablePane;
//   * a model with IsVisible == false is hidden (moved to Layout.Hidden) right after creation;
//   * IsVisible / IsActive / IsSelected are kept in two-way sync between the model and the
//     LayoutAnchorable, guarded by a re-entrancy flag so model→layout→model does not loop.
//
// UnoDock does not reference the consumer's model types; it uses reflection on the well-known
// property names (Title, ContentId, IsVisible, IsActive, IsSelected, IsCloseable), mirroring the
// Title reflection already used by DocumentsSource.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

using AvalonDock.Layout;

using Microsoft.UI.Xaml;

namespace AvalonDock
{
	public partial class DockingManager
	{
		// ── AnchorablesSource ──────────────────────────────────────────────────────
		public static readonly DependencyProperty AnchorablesSourceProperty =
			DependencyProperty.Register(nameof(AnchorablesSource), typeof(IEnumerable), typeof(DockingManager),
				new PropertyMetadata(null, (d, e) => ((DockingManager)d).OnAnchorablesSourceChanged(e)));

		/// <summary>Gets or sets the source collection for the <see cref="LayoutAnchorable"/> (tool pane) objects managed by this framework.</summary>
		public IEnumerable AnchorablesSource
		{
			get => (IEnumerable)GetValue(AnchorablesSourceProperty);
			set => SetValue(AnchorablesSourceProperty, value);
		}

		// ── AnchorableContentTemplate ──────────────────────────────────────────────
		// Separate from LayoutItemTemplate (used by documents): tool panes and documents render their
		// view-models differently, so the anchorable pane control uses this template instead.
		public static readonly DependencyProperty AnchorableContentTemplateProperty =
			DependencyProperty.Register(nameof(AnchorableContentTemplate), typeof(DataTemplate), typeof(DockingManager),
				new PropertyMetadata(null));

		/// <summary>Template used to render the content (view-model) of an anchorable (tool pane).</summary>
		public DataTemplate AnchorableContentTemplate
		{
			get => (DataTemplate)GetValue(AnchorableContentTemplateProperty);
			set => SetValue(AnchorableContentTemplateProperty, value);
		}

		// Per-anchorable handler bookkeeping so we can detach cleanly.
		private readonly Dictionary<LayoutAnchorable, PropertyChangedEventHandler> _anchorableModelHandlers = new();
		private readonly Dictionary<LayoutAnchorable, EventHandler> _anchorableActiveHandlers = new();
		private readonly Dictionary<LayoutAnchorable, EventHandler> _anchorableSelectedHandlers = new();
		private readonly Dictionary<LayoutAnchorable, PropertyChangedEventHandler> _anchorableVisibilityHandlers = new();

		// Re-entrancy guard for the two-way visibility/active/selected sync.
		private bool _inAnchorableSync;

		private void OnAnchorablesSourceChanged(DependencyPropertyChangedEventArgs e)
		{
			DetachAnchorablesSource(Layout, e.OldValue as IEnumerable);
			AttachAnchorablesSource(Layout, e.NewValue as IEnumerable);
		}

		private static IEnumerable<LayoutAnchorable> AllAnchorables(LayoutRoot layout)
			=> layout.Descendents().OfType<LayoutAnchorable>()
				.Concat(layout.Hidden ?? Enumerable.Empty<LayoutAnchorable>());

		private static LayoutAnchorablePane FindHostAnchorablePane(LayoutRoot layout)
			=> layout.Descendents().OfType<LayoutAnchorablePane>().FirstOrDefault();

		private LayoutAnchorable CreateAnchorableForModel(object model, LayoutAnchorablePane pane)
		{
			var anchorable = new LayoutAnchorable { Content = model };

			var contentId = GetModelString(model, "ContentId");
			if (!string.IsNullOrEmpty(contentId))
				anchorable.ContentId = contentId;

			// IsCloseable on the model drives both the close (X) button and whether the user may hide it.
			var closeable = GetModelBool(model, "IsCloseable");
			anchorable.CanClose = closeable;
			anchorable.CanHide = closeable;
			anchorable.CanDockAsTabbedDocument = false;

			UpdateAnchorableTitle(anchorable, model);

			var added = false;
			if (LayoutUpdateStrategy != null)
				added = LayoutUpdateStrategy.BeforeInsertAnchorable(Layout, anchorable, pane);

			if (!added)
			{
				if (pane == null)
				{
					pane = new LayoutAnchorablePane();
					Layout.RootPanel?.Children.Add(pane);
				}
				pane.Children.Add(anchorable);
			}

			LayoutUpdateStrategy?.AfterInsertAnchorable(Layout, anchorable);

			// Models that start hidden are moved to Layout.Hidden after they have a PreviousContainer.
			if (!GetModelBool(model, "IsVisible"))
				anchorable.HideAnchorable(false);

			// Wire two-way sync AFTER the initial placement/hide so creation does not feed back.
			WireAnchorableSync(anchorable, model);
			return anchorable;
		}

		// ── Two-way sync (reflection + re-entrancy guard) ──────────────────────────
		private void WireAnchorableSync(LayoutAnchorable anchorable, object model)
		{
			if (model is INotifyPropertyChanged modelNotifier)
			{
				PropertyChangedEventHandler modelHandler = (_, e) => OnAnchorableModelPropertyChanged(anchorable, model, e);
				_anchorableModelHandlers[anchorable] = modelHandler;
				modelNotifier.PropertyChanged += modelHandler;
			}

			EventHandler activeHandler = (_, _) => SyncToModel(anchorable, model, "IsActive", anchorable.IsActive);
			anchorable.IsActiveChanged += activeHandler;
			_anchorableActiveHandlers[anchorable] = activeHandler;

			EventHandler selectedHandler = (_, _) => SyncToModel(anchorable, model, "IsSelected", anchorable.IsSelected);
			anchorable.IsSelectedChanged += selectedHandler;
			_anchorableSelectedHandlers[anchorable] = selectedHandler;

			PropertyChangedEventHandler visibilityHandler = (_, e) =>
			{
				if (e.PropertyName == nameof(LayoutAnchorable.IsVisible))
					SyncToModel(anchorable, model, "IsVisible", anchorable.IsVisible);
			};
			anchorable.PropertyChanged += visibilityHandler;
			_anchorableVisibilityHandlers[anchorable] = visibilityHandler;
		}

		private void UnwireAnchorableSync(LayoutAnchorable anchorable)
		{
			if (_anchorableModelHandlers.Remove(anchorable, out var modelHandler)
				&& anchorable.Content is INotifyPropertyChanged modelNotifier)
				modelNotifier.PropertyChanged -= modelHandler;
			if (_anchorableActiveHandlers.Remove(anchorable, out var activeHandler))
				anchorable.IsActiveChanged -= activeHandler;
			if (_anchorableSelectedHandlers.Remove(anchorable, out var selectedHandler))
				anchorable.IsSelectedChanged -= selectedHandler;
			if (_anchorableVisibilityHandlers.Remove(anchorable, out var visibilityHandler))
				anchorable.PropertyChanged -= visibilityHandler;
		}

		// model → layout
		private void OnAnchorableModelPropertyChanged(LayoutAnchorable anchorable, object model, PropertyChangedEventArgs e)
		{
			if (_inAnchorableSync)
				return;
			_inAnchorableSync = true;
			try
			{
				switch (e.PropertyName)
				{
					case null or "" or "Title":
						UpdateAnchorableTitle(anchorable, model);
						if (e.PropertyName is "Title") break;
						goto case "IsVisible";
					case "IsVisible":
						var visible = GetModelBool(model, "IsVisible");
						if (visible != anchorable.IsVisible)
						{
							if (visible) anchorable.Show();
							else anchorable.HideAnchorable(false);
						}
						if (e.PropertyName is "IsVisible") break;
						goto case "IsActive";
					case "IsActive":
						var active = GetModelBool(model, "IsActive");
						if (active != anchorable.IsActive) anchorable.IsActive = active;
						if (e.PropertyName is "IsActive") break;
						goto case "IsSelected";
					case "IsSelected":
						var selected = GetModelBool(model, "IsSelected");
						if (selected != anchorable.IsSelected) anchorable.IsSelected = selected;
						break;
				}
			}
			finally
			{
				_inAnchorableSync = false;
			}
		}

		// layout → model
		private void SyncToModel(LayoutAnchorable anchorable, object model, string property, bool value)
		{
			if (_inAnchorableSync)
				return;
			_inAnchorableSync = true;
			try
			{
				if (GetModelBool(model, property) != value)
					SetModelBool(model, property, value);
			}
			finally
			{
				_inAnchorableSync = false;
			}
		}

		private static void UpdateAnchorableTitle(LayoutAnchorable anchorable, object model)
		{
			var title = GetModelString(model, "Title");
			if (title is not null)
				anchorable.Title = title;
		}

		private void AttachAnchorablesSource(LayoutRoot layout, IEnumerable anchorablesSource)
		{
			if (anchorablesSource == null || layout == null)
				return;

			var existing = AllAnchorables(layout).ToList();
			var pane = FindHostAnchorablePane(layout);
			foreach (var model in anchorablesSource.OfType<object>())
			{
				var anchorable = existing.FirstOrDefault(a => Equals(a.Content, model));
				if (anchorable != null)
					AdoptExistingAnchorable(anchorable, model); // restored from a persisted layout
				else
					CreateAnchorableForModel(model, pane);
			}

			if (anchorablesSource is INotifyCollectionChanged notifier)
				notifier.CollectionChanged += AnchorablesSourceElementsChanged;
		}

		// A LayoutAnchorable deserialized from a saved layout already hosts the model (the host's
		// LayoutSerializationCallback set Content by ContentId). Wire the two-way sync and reconcile
		// the model's visibility/active/selected FROM the restored anchorable (the saved layout is the
		// source of truth for those), so subsequent Show/Hide via the model work.
		private void AdoptExistingAnchorable(LayoutAnchorable anchorable, object model)
		{
			if (_anchorableModelHandlers.ContainsKey(anchorable))
				return; // already adopted

			var contentId = GetModelString(model, "ContentId");
			if (!string.IsNullOrEmpty(contentId) && string.IsNullOrEmpty(anchorable.ContentId))
				anchorable.ContentId = contentId;

			WireAnchorableSync(anchorable, model);
			SyncToModel(anchorable, model, "IsVisible", anchorable.IsVisible);
			SyncToModel(anchorable, model, "IsSelected", anchorable.IsSelected);
			SyncToModel(anchorable, model, "IsActive", anchorable.IsActive);
		}

		private void DetachAnchorablesSource(LayoutRoot layout, IEnumerable anchorablesSource)
		{
			if (anchorablesSource == null || layout == null)
				return;

			var sourceItems = anchorablesSource.OfType<object>().ToArray();
			var toRemove = AllAnchorables(layout).Where(a => sourceItems.Contains(a.Content)).ToArray();
			foreach (var anchorable in toRemove)
				RemoveAnchorable(anchorable);

			if (anchorablesSource is INotifyCollectionChanged notifier)
				notifier.CollectionChanged -= AnchorablesSourceElementsChanged;
		}

		private void RemoveAnchorable(LayoutAnchorable anchorable)
		{
			UnwireAnchorableSync(anchorable);
			anchorable.Content = null;
			(anchorable.Parent as ILayoutContainer)?.RemoveChild(anchorable);
		}

		private void AnchorablesSourceElementsChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (Layout == null || SuspendAnchorablesSourceBinding)
				return;

			if (e.OldItems != null && (e.Action == NotifyCollectionChangedAction.Remove || e.Action == NotifyCollectionChangedAction.Replace))
			{
				var toRemove = AllAnchorables(Layout).Where(a => e.OldItems.Contains(a.Content)).ToArray();
				foreach (var anchorable in toRemove)
					RemoveAnchorable(anchorable);
			}

			if (e.NewItems != null && (e.Action == NotifyCollectionChangedAction.Add || e.Action == NotifyCollectionChangedAction.Replace))
			{
				var pane = FindHostAnchorablePane(Layout);
				foreach (var model in e.NewItems)
					CreateAnchorableForModel(model, pane);
			}

			if (e.Action == NotifyCollectionChangedAction.Reset)
			{
				var remaining = new HashSet<object>((AnchorablesSource ?? Array.Empty<object>()).OfType<object>());
				var toRemove = AllAnchorables(Layout).Where(a => !remaining.Contains(a.Content)).ToArray();
				foreach (var anchorable in toRemove)
					RemoveAnchorable(anchorable);
			}

			Layout?.CollectGarbage();
		}

		// ── Reflection helpers (UnoDock stays decoupled from the consumer's model types) ──
		private static bool GetModelBool(object model, string name)
			=> model.GetType().GetRuntimeProperty(name)?.GetValue(model) is bool b && b;

		private static void SetModelBool(object model, string name, bool value)
		{
			var property = model.GetType().GetRuntimeProperty(name);
			if (property?.CanWrite == true)
				property.SetValue(model, value);
		}

		private static string GetModelString(object model, string name)
			=> model.GetType().GetRuntimeProperty(name)?.GetValue(model)?.ToString();
	}
}
