// DocumentsSource MVVM binding for UnoDock's DockingManager, ported from upstream AvalonDock
// (Components/AvalonDock/DockingManager.cs). Setting DocumentsSource to an IEnumerable (optionally
// INotifyCollectionChanged) creates one LayoutDocument per source item — Content = the source item
// (a view-model) — and keeps the layout's first LayoutDocumentPane in sync as the collection
// changes. The item's content is rendered through LayoutItemTemplate (applied by the document pane
// control as its selected-content template); the tab header binds to the item's Title.
//
// Differences from upstream: UnoDock builds tab containers directly from LayoutDocumentPane.Children
// (its LayoutDocumentPaneControl uses Children as ItemsSource), so the upstream LayoutItem creation
// pipeline (CreateDocumentLayoutItem / RemoveViewFromLogicalChild / _suspendLayoutItemCreation) is
// not needed here — adding/removing the LayoutDocument is sufficient.

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
		// ── DocumentsSource ───────────────────────────────────────────────────────
		public static readonly DependencyProperty DocumentsSourceProperty =
			DependencyProperty.Register(nameof(DocumentsSource), typeof(IEnumerable), typeof(DockingManager),
				new PropertyMetadata(null, (d, e) => ((DockingManager)d).OnDocumentsSourceChanged(e)));

		/// <summary>Gets or sets the source collection for the <see cref="LayoutDocument"/> objects managed by this framework.</summary>
		public IEnumerable DocumentsSource
		{
			get => (IEnumerable)GetValue(DocumentsSourceProperty);
			set => SetValue(DocumentsSourceProperty, value);
		}

		// ── LayoutItemTemplate ─────────────────────────────────────────────────────
		public static readonly DependencyProperty LayoutItemTemplateProperty =
			DependencyProperty.Register(nameof(LayoutItemTemplate), typeof(DataTemplate), typeof(DockingManager),
				new PropertyMetadata(null));

		/// <summary>Template used to render the content (view-model) of a document or anchorable.</summary>
		public DataTemplate LayoutItemTemplate
		{
			get => (DataTemplate)GetValue(LayoutItemTemplateProperty);
			set => SetValue(LayoutItemTemplateProperty, value);
		}

		private readonly Dictionary<LayoutDocument, PropertyChangedEventHandler> _documentTitleHandlers = new();

		private void OnDocumentsSourceChanged(DependencyPropertyChangedEventArgs e)
		{
			DetachDocumentsSource(Layout, e.OldValue as IEnumerable);
			AttachDocumentsSource(Layout, e.NewValue as IEnumerable);
		}

		private static LayoutDocumentPane FindHostDocumentPane(LayoutRoot layout)
		{
			LayoutDocumentPane pane = null;
			if (layout.LastFocusedDocument != null)
				pane = layout.LastFocusedDocument.Parent as LayoutDocumentPane;
			return pane ?? layout.Descendents().OfType<LayoutDocumentPane>().FirstOrDefault();
		}

		private LayoutDocument CreateDocumentForModel(object model, LayoutDocumentPane pane)
		{
			var document = new LayoutDocument { Content = model };

			// LayoutDocument is a layout model, not a FrameworkElement, so it has no SetBinding
			// on WinUI targets. Mirror the source model's Title manually instead.
			AttachDocumentTitle(document, model);

			var added = false;
			if (LayoutUpdateStrategy != null)
				added = LayoutUpdateStrategy.BeforeInsertDocument(Layout, document, pane);

			if (!added)
			{
				if (pane == null)
					throw new InvalidOperationException("Layout must contain at least one LayoutDocumentPane in order to host documents.");
				pane.Children.Add(document);
			}

			LayoutUpdateStrategy?.AfterInsertDocument(Layout, document);
			return document;
		}

		private void AttachDocumentTitle(LayoutDocument document, object model)
		{
			UpdateDocumentTitle(document, model);

			if (model is not INotifyPropertyChanged notifier)
				return;

			PropertyChangedEventHandler handler = (_, e) =>
			{
				if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == "Title")
					UpdateDocumentTitle(document, model);
			};
			_documentTitleHandlers[document] = handler;
			notifier.PropertyChanged += handler;
		}

		private void DetachDocumentTitle(LayoutDocument document)
		{
			if (document.Content is INotifyPropertyChanged notifier &&
				_documentTitleHandlers.Remove(document, out var handler))
			{
				notifier.PropertyChanged -= handler;
			}
		}

		private static void UpdateDocumentTitle(LayoutDocument document, object model)
		{
			var title = GetModelTitle(model);
			if (title is not null)
				document.Title = title;
		}

		private static string GetModelTitle(object model)
		{
			var property = model.GetType().GetRuntimeProperty("Title");
			return property?.GetValue(model)?.ToString();
		}

		private void AttachDocumentsSource(LayoutRoot layout, IEnumerable documentsSource)
		{
			if (documentsSource == null || layout == null)
				return;

			var alreadyImported = layout.Descendents().OfType<LayoutDocument>().Select(d => d.Content).ToArray();
			var toImport = documentsSource.OfType<object>().Where(m => !alreadyImported.Contains(m)).ToList();

			var pane = FindHostDocumentPane(layout);
			foreach (var model in toImport)
				CreateDocumentForModel(model, pane);

			if (documentsSource is INotifyCollectionChanged notifier)
				notifier.CollectionChanged += DocumentsSourceElementsChanged;
		}

		private void DetachDocumentsSource(LayoutRoot layout, IEnumerable documentsSource)
		{
			if (documentsSource == null || layout == null)
				return;

			var toRemove = layout.Descendents().OfType<LayoutDocument>()
				.Where(d => documentsSource.OfType<object>().Contains(d.Content)).ToArray();
			foreach (var document in toRemove)
			{
				DetachDocumentTitle(document);
				document.Content = null;
				(document.Parent as ILayoutContainer)?.RemoveChild(document);
			}

			if (documentsSource is INotifyCollectionChanged notifier)
				notifier.CollectionChanged -= DocumentsSourceElementsChanged;
		}

		private void DocumentsSourceElementsChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (Layout == null || SuspendDocumentsSourceBinding)
				return;

			if (e.OldItems != null && (e.Action == NotifyCollectionChangedAction.Remove || e.Action == NotifyCollectionChangedAction.Replace))
			{
				var toRemove = Layout.Descendents().OfType<LayoutDocument>().Where(d => e.OldItems.Contains(d.Content)).ToArray();
				foreach (var document in toRemove)
				{
					DetachDocumentTitle(document);
					document.Content = null;
					(document.Parent as ILayoutContainer)?.RemoveChild(document);
				}
			}

			if (e.NewItems != null && (e.Action == NotifyCollectionChangedAction.Add || e.Action == NotifyCollectionChangedAction.Replace))
			{
				var pane = FindHostDocumentPane(Layout);
				foreach (var model in e.NewItems)
					CreateDocumentForModel(model, pane);
			}

			if (e.Action == NotifyCollectionChangedAction.Reset)
			{
				var remaining = new HashSet<object>((DocumentsSource ?? Array.Empty<object>()).OfType<object>());
				var toRemove = Layout.Descendents().OfType<LayoutDocument>().Where(d => !remaining.Contains(d.Content)).ToArray();
				foreach (var document in toRemove)
				{
					DetachDocumentTitle(document);
					document.Content = null;
					(document.Parent as ILayoutContainer)?.RemoveChild(document);
				}
			}

			Layout?.CollectGarbage();
		}
	}
}
