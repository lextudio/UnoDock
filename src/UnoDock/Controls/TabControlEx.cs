// Forked from AvalonDock Controls/TabControlEx.cs.
// Non-virtualizing path entirely gutted (all callers use isVirtualizing=true).
// WPF TabControl base → ControlsShims.cs stub; ItemContainerGenerator → stub.

using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Controls;

namespace AvalonDock.Controls
{
	[Microsoft.UI.Xaml.TemplatePart(Name = "PART_ItemsHolder", Type = typeof(Microsoft.UI.Xaml.Controls.Panel))]
	public class TabControlEx : TabControl
	{
		private readonly bool _IsVirtualizing;

		public TabControlEx(bool isVirtualizing) : this() => _IsVirtualizing = isVirtualizing;

		protected TabControlEx() : base() => _IsVirtualizing = true;

		[Bindable(false)]
		public bool IsVirtualiting => _IsVirtualizing;

		protected override void OnApplyTemplate()
		{
			base.OnApplyTemplate();
			// Virtualizing path: nothing extra to wire up — layout comes from XAML template.
		}

		protected virtual void OnItemsChanged(NotifyCollectionChangedEventArgs e)
		{
			// Virtualizing: no ItemsHolderPanel to maintain.
		}

		protected virtual void OnSelectionChanged(SelectionChangedEventArgs e)
		{
			// Virtualizing: selection is handled by XAML template / DockingManager.
		}

		protected TabItem GetSelectedTabItem() => SelectedItem as TabItem;
	}
}
