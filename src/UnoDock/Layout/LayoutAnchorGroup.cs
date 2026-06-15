/************************************************************************
   AvalonDock

   Copyright (C) 2007-2013 Xceed Software Inc.

   This program is provided to you under the terms of the Microsoft Public
   License (Ms-PL) as published at https://opensource.org/licenses/MS-PL
 ************************************************************************/

using System;
using System.Windows.Markup;
using System.Xml.Serialization;

namespace AvalonDock.Layout
{
	/// <summary>
	/// Implements the layout model for the <see cref="Controls.LayoutAnchorGroupControl"/>.
	/// </summary>
	[ContentProperty(Name = nameof(Children))]
	[Serializable]
	public class LayoutAnchorGroup : LayoutGroup<LayoutAnchorable>, ILayoutPreviousContainer, ILayoutPaneSerializable, Core.Serialization.ISerializableLayoutPane
	{
		/// <inheritdoc />
		protected override bool GetVisibility() => Children.Count > 0;

		[field: NonSerialized]
		private ILayoutContainer _previousContainer = null;

		/// <inheritdoc />
		[XmlIgnore]
		ILayoutContainer ILayoutPreviousContainer.PreviousContainer
		{
			get => _previousContainer;
			set
			{
				if (value == _previousContainer) return;
				_previousContainer = value;
				RaisePropertyChanged(nameof(ILayoutPreviousContainer.PreviousContainer));
				if (_previousContainer is ILayoutPaneSerializable paneSerializable && paneSerializable.Id == null)
					paneSerializable.Id = Guid.NewGuid().ToString();
			}
		}

		/// <inheritdoc />
		string ILayoutPreviousContainer.PreviousContainerId { get; set; }

		private string _id;

		/// <inheritdoc />
		string ILayoutPaneSerializable.Id { get => _id; set => _id = value; }

		/// <inheritdoc />
		string Core.Serialization.ISerializableLayoutPane.Id { get => _id; set => _id = value; }
	}
}