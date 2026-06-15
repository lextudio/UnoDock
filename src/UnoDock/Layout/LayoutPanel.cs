/************************************************************************
   AvalonDock

   Copyright (C) 2007-2013 Xceed Software Inc.

   This program is provided to you under the terms of the Microsoft Public
   License (Ms-PL) as published at https://opensource.org/licenses/MS-PL
 ************************************************************************/

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace AvalonDock.Layout
{
	/// <summary>Implements the layout model for the <see cref="Controls.LayoutPanelControl"/>.</summary>
	[ContentProperty(Name = nameof(Children))]
	[Serializable]
	public class LayoutPanel : LayoutPositionableGroup<ILayoutPanelElement>, ILayoutPanelElement, ILayoutOrientableGroup
	{
		private Orientation _orientation;

		/// <summary>
		/// Initializes a new instance of the <see cref="LayoutPanel"/> class.
		/// </summary>
		public LayoutPanel()
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="LayoutPanel"/> class.
		/// </summary>
		/// <param name="firstChild">The first child.</param>
		public LayoutPanel(ILayoutPanelElement firstChild)
		{
			Children.Add(firstChild);
		}

		/// <summary>Gets/sets the orientation for this panel.</summary>
		public Orientation Orientation
		{
			get => _orientation;
			set
			{
				if (value == _orientation) return;
				RaisePropertyChanging(nameof(Orientation));
				_orientation = value;
				RaisePropertyChanged(nameof(Orientation));
			}
		}

		/// <summary>
		/// Using a DependencyProperty as the backing store for thhe <see cref="CanDock"/> property.
		/// </summary>
		public static readonly DependencyProperty CanDockProperty =
			DependencyProperty.Register("CanDock", typeof(bool),
				typeof(LayoutPanel), new PropertyMetadata(true));

		/// <summary>
		/// Gets/sets dependency property that determines whether docking of dragged items
		/// is enabled or not. This property can be used disable/enable docking of
		/// dragged FloatingWindowControls.
		///
		/// This property should only be set to false if:
		/// <see cref="LayoutAnchorable.CanMove"/> and <see cref="LayoutDocument.CanMove"/>
		/// are false since users will otherwise be able to:
		/// 1) Drag an item away
		/// 2) But won't be able to dock it agin.
		/// </summary>
		public bool CanDock
		{
			get { return (bool)GetValue(CanDockProperty); }
			set { SetValue(CanDockProperty, value); }
		}

		/// <inheritdoc />
		protected override bool GetVisibility() => Children.Any(c => c.IsVisible);

#if TRACE
		/// <inheritdoc />
		public override void ConsoleDump(int tab)
		{
			System.Diagnostics.Trace.Write(new string(' ', tab * 4));
			System.Diagnostics.Trace.WriteLine(string.Format("Panel({0})", Orientation));

			foreach (LayoutElement child in Children)
				child.ConsoleDump(tab + 1);
		}
#endif

	}
}