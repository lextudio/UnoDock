// Shims for WPF Control-layer types that have no direct WinUI equivalent.
// These are the minimal members used by forked controls in Phase 2.
// They will be replaced or expanded when the full control layer is wired up.

using System;
using System.Collections;
using System.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace System.Windows.Controls
{
	// WPF TabControl: base class for TabControlEx. WinUI has no TabControl with the
	// same API; this stub lets TabControlEx compile. The actual rendering is done by
	// WinUI XAML templates, not by this base class machinery.
	public class TabControl : ContentControl
	{
		public IEnumerable ItemsSource
		{
			get => (IEnumerable)GetValue(ItemsSourceProperty);
			set => SetValue(ItemsSourceProperty, value);
		}
		public static readonly DependencyProperty ItemsSourceProperty =
			DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(TabControl), null);

		public object SelectedItem
		{
			get => GetValue(SelectedItemProperty);
			set => SetValue(SelectedItemProperty, value);
		}
		public static readonly DependencyProperty SelectedItemProperty =
			DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(TabControl), null);

		public int SelectedIndex
		{
			get => (int)GetValue(SelectedIndexProperty);
			set => SetValue(SelectedIndexProperty, value);
		}
		public static readonly DependencyProperty SelectedIndexProperty =
			DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(TabControl), null);

		public DataTemplate SelectedContentTemplate { get; set; }
		public DataTemplateSelector SelectedContentTemplateSelector { get; set; }
		public string SelectedContentStringFormat { get; set; }

		public ItemContainerGenerator ItemContainerGenerator { get; } = new ItemContainerGenerator();

		// WPF calls GetVisualChild(0) to find the top-level Grid template part.
		// In WinUI this goes through VisualTreeHelper; return null for Phase 2
		// (the virtualizing path never calls this).
		protected virtual UIElement GetVisualChild(int index) => null;
	}

	// Stub: WPF's Binding used only in TabControlEx.UpdateSelectedItem() to set
	// ContentPresenter.ContentTemplate. Phase 2 doesn't exercise this path.
	public class Binding
	{
		public Binding(string path) { Path = path; }
		public string Path { get; }
		public RelativeSource RelativeSource { get; set; }
	}

	// Stub: RelativeSource used in Binding above.
	public class RelativeSource
	{
		public static readonly RelativeSource TemplatedParent = new RelativeSource();
	}
}

namespace System.Windows.Controls
{
	// ItemContainerGenerator: WPF-only mechanism for mapping items ↔ containers.
	// TabControlEx uses it to find the TabItem for a selected item, but only in
	// the non-virtualizing path (_IsVirtualizing = false). All callers in our
	// ported controls use isVirtualizing = true, so this stub's methods are
	// never invoked at runtime.
	public class ItemContainerGenerator
	{
		#pragma warning disable CS0067
		public event EventHandler StatusChanged;
		#pragma warning restore CS0067
		public GeneratorStatus Status => GeneratorStatus.ContainersGenerated;
		public UIElement ContainerFromIndex(int index) => null;
		public UIElement ContainerFromItem(object item) => null;
	}

	public enum GeneratorStatus
	{
		NotStarted,
		GeneratingContainers,
		ContainersGenerated,
		Error,
	}

	// Stub: WPF TabItem — only used as a type-check in TabControlEx.
	public class TabItem : ContentControl { }
}

namespace System.Windows
{
	// Stub: WPF's DesignerProperties.GetIsInDesignMode(). WinUI uses
	// Windows.ApplicationModel.DesignMode.DesignModeEnabled. Returns false
	// (we're always at runtime in Uno headless or Skia).
	public static class DesignerProperties
	{
		public static bool GetIsInDesignMode(DependencyObject element) => false;
	}
}
