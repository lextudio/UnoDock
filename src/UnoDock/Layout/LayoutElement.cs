/************************************************************************
   AvalonDock

   Copyright (C) 2007-2013 Xceed Software Inc.

   This program is provided to you under the terms of the Microsoft Public
   License (Ms-PL) as published at https://opensource.org/licenses/MS-PL
 ************************************************************************/

using System;
using System.ComponentModel;
using System.Windows;
using System.Xml.Serialization;

namespace AvalonDock.Layout
{
	/// <summary>
	/// Implements an abstract base class for almost all layout models in the AvalonDock.Layout namespace.
	///
	/// This base inherites from <see cref="DependencyObject"/> and implements <see cref="PropertyChanged"/>
	/// and <see cref="PropertyChanging"/> events. Deriving classes can, therefore, implement
	/// depedency object and/or viewmodel specific functionalities.
	/// class supports both
	/// </summary>
	[Serializable]
	public abstract partial class LayoutElement : DependencyObject, ILayoutElement, Core.Serialization.ISerializableLayoutElement
	{
		[NonSerialized]
		private ILayoutContainer _parent = null;

		[NonSerialized]
		private ILayoutRoot _root = null;

		/// <summary>
		/// Initializes a new instance of the <see cref="LayoutElement"/> class.
		/// </summary>
		internal LayoutElement()
		{
		}

		// UnoDock shim: WPF's DependencyObject.SetCurrentValue has no WinUI/Uno
		// equivalent. LayoutContent calls it as an implicit-this instance method,
		// so we expose it on the shared base. SetCurrentValue differs from SetValue
		// in WPF (it doesn't change the value source/precedence), but for the
		// model layer the distinction is immaterial.
		protected internal void SetCurrentValue(DependencyProperty dp, object value)
			=> SetValue(dp, value);

		/// <summary>Raised when a property has changed (after the change has taken place).</summary>
		[field: NonSerialized]
		[field: XmlIgnore]
		public event PropertyChangedEventHandler PropertyChanged;

		/// <summary>Raised when a property is about to change (raised before the actual change).</summary>
		[field: NonSerialized]
		[field: XmlIgnore]
		public event PropertyChangingEventHandler PropertyChanging;

		/// <summary>Gets or sets the parent container of the element</summary>
		[XmlIgnore]
		public ILayoutContainer Parent
		{
			get => _parent;
			set
			{
				if (_parent == value) return;
				var oldValue = _parent;
				var oldRoot = _root;
				RaisePropertyChanging(nameof(Parent));
				OnParentChanging(oldValue, value);
				_parent = value;
				OnParentChanged(oldValue, value);

				_root = Root;
				if (oldRoot != _root) OnRootChanged(oldRoot, _root);
				RaisePropertyChanged(nameof(Parent));
				if (Root is LayoutRoot root) root.FireLayoutUpdated();
			}
		}

		/// <summary>Gets or sets the layout root of the element.</summary>
		public ILayoutRoot Root
		{
			get
			{
				var parent = Parent;
				while (parent != null && (!(parent is ILayoutRoot))) parent = parent.Parent;
				return parent as ILayoutRoot;
			}
		}

#if TRACE
		/// <summary>
		/// Dumps this layout element to the trace output.
		/// </summary>
		/// <param name="tab">The indentation level.</param>
		public virtual void ConsoleDump(int tab)
		{
			System.Diagnostics.Trace.Write(new string(' ', tab * 4));
			System.Diagnostics.Trace.WriteLine(this.ToString());
		}
#endif

		/// <summary>
		/// When deserializing layout enclosing element parent is set later than this parent
		/// We need to update it, otherwise when deleting this element <see cref="LayoutRoot.ElementRemoved" /> will no be called
		/// </summary>
		public void FixCachedRootOnDeserialize()
		{
			if (_root == null)
				_root = Root;
		}

		/// <summary>
		/// Executes the on parent changing operation.
		/// </summary>
		/// <param name="oldValue">The previous value.</param>
		/// <param name="newValue">The new value.</param>
		protected virtual void OnParentChanging(ILayoutContainer oldValue, ILayoutContainer newValue)
		{
		}

		/// <summary>
		/// Executes the on parent changed operation.
		/// </summary>
		/// <param name="oldValue">The previous value.</param>
		/// <param name="newValue">The new value.</param>
		protected virtual void OnParentChanged(ILayoutContainer oldValue, ILayoutContainer newValue)
		{
		}

		/// <summary>
		/// Executes the on root changed operation.
		/// </summary>
		/// <param name="oldRoot">The old root.</param>
		/// <param name="newRoot">The new root.</param>
		protected virtual void OnRootChanged(ILayoutRoot oldRoot, ILayoutRoot newRoot)
		{
			((LayoutRoot)oldRoot)?.OnLayoutElementRemoved(this);
			((LayoutRoot)newRoot)?.OnLayoutElementAdded(this);
		}

		/// <summary>
		/// Raises the property changed.
		/// </summary>
		/// <param name="propertyName">The property name.</param>
		protected virtual void RaisePropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

		/// <summary>
		/// Raises the property changing.
		/// </summary>
		/// <param name="propertyName">The property name.</param>
		protected virtual void RaisePropertyChanging(string propertyName) => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
	}
}