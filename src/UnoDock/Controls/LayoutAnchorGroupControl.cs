// A group of LayoutAnchorControls within one LayoutAnchorSide strip.
// Renders them stacked and separated from adjacent groups by a small gap.

using System;
using System.Collections.Specialized;
using System.Linq;
using AvalonDock.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AvalonDock.Controls
{
	public sealed class LayoutAnchorGroupControl : ContentControl, ILayoutControl
	{
		private readonly LayoutAnchorGroup _model;
		private readonly LayoutAnchorSideControl _side;
		internal StackPanel AnchorPanel { get; private set; }

		ILayoutElement ILayoutControl.Model => _model;

		internal LayoutAnchorGroupControl(LayoutAnchorGroup model, LayoutAnchorSideControl side)
		{
			_model = model ?? throw new ArgumentNullException(nameof(model));
			_side  = side  ?? throw new ArgumentNullException(nameof(side));

			bool isVertical = IsVerticalSide();
			AnchorPanel = new StackPanel
			{
				Orientation = isVertical ? Microsoft.UI.Xaml.Controls.Orientation.Vertical
				                         : Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
				Spacing     = 0,  // spacing handled by Margin on each LayoutAnchorControl
			};

			RebuildChildren();
			Content = AnchorPanel;

			_model.Children.CollectionChanged += OnChildrenChanged;
		}

		private bool IsVerticalSide()
		{
			var s = _model.FindParent<LayoutAnchorSide>()?.Side ?? AnchorSide.Left;
			return s == AnchorSide.Left || s == AnchorSide.Right;
		}

		private void RebuildChildren()
		{
			AnchorPanel.Children.Clear();
			foreach (var anc in _model.Children)
				AnchorPanel.Children.Add(new LayoutAnchorControl(anc, _side));
		}

		private void OnChildrenChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.Action == NotifyCollectionChangedAction.Reset)
			{
				RebuildChildren();
				return;
			}

			if (e.OldItems != null &&
				(e.Action == NotifyCollectionChangedAction.Remove ||
				 e.Action == NotifyCollectionChangedAction.Replace))
			{
				foreach (LayoutAnchorable removed in e.OldItems)
				{
					var ctrl = AnchorPanel.Children.OfType<LayoutAnchorControl>()
						.FirstOrDefault(c => c.Model == removed);
					if (ctrl != null) AnchorPanel.Children.Remove(ctrl);
				}
			}

			if (e.NewItems != null &&
				(e.Action == NotifyCollectionChangedAction.Add ||
				 e.Action == NotifyCollectionChangedAction.Replace))
			{
				int idx = e.NewStartingIndex;
				foreach (LayoutAnchorable added in e.NewItems)
					AnchorPanel.Children.Insert(idx++, new LayoutAnchorControl(added, _side));
			}
		}
	}
}
