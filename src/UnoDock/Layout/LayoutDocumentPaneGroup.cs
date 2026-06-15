/************************************************************************
   AvalonDock

   Copyright (C) 2007-2013 Xceed Software Inc.

   This program is provided to you under the terms of the Microsoft Public
   License (Ms-PL) as published at https://opensource.org/licenses/MS-PL
 ************************************************************************/

using System;
using System.Windows.Controls;
using System.Windows.Markup;

namespace AvalonDock.Layout
{
	/// <summary>
	/// Implements an element in the layout model that can contain and organize multiple
	/// <see cref="LayoutDocumentPane"/> elements, which in turn contain <see cref="LayoutDocument"/> elements.
	/// </summary>
	[ContentProperty(Name = nameof(Children))]
	[Serializable]
	public class LayoutDocumentPaneGroup : LayoutPositionableGroup<ILayoutDocumentPane>, ILayoutDocumentPane, ILayoutOrientableGroup
	{
		private Orientation _orientation;

		/// <summary>
		/// Initializes a new instance of the <see cref="LayoutDocumentPaneGroup"/> class.
		/// </summary>
		public LayoutDocumentPaneGroup()
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="LayoutDocumentPaneGroup"/> class.
		/// </summary>
		/// <param name="documentPane">The document pane.</param>
		public LayoutDocumentPaneGroup(LayoutDocumentPane documentPane)
		{
			Children.Add(documentPane);
		}

		/// <summary>Gets/sets the (Horizontal, Vertical) <see cref="System.Windows.Controls.Orientation"/> of this group.</summary>
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

		/// <inheritdoc />
		protected override bool GetVisibility() => true;

#if TRACE
		/// <inheritdoc />
		public override void ConsoleDump(int tab)
		{
			System.Diagnostics.Trace.Write(new string(' ', tab * 4));
			System.Diagnostics.Trace.WriteLine(string.Format("DocumentPaneGroup({0})", Orientation));

			foreach (LayoutElement child in Children)
				child.ConsoleDump(tab + 1);
		}
#endif

	}
}