// Forked from AvalonDock Controls/LayoutGridResizerControl.cs.
// WinUI's Thumb is sealed so we cannot subclass it.
// Instead, subclass Control and replicate the two DPs + expose Thumb-compatible
// drag events by hosting an inner Thumb. The DragStarted/Delta/Completed events
// are forwarded so LayoutGridControl wires up identically.

using System;
using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace AvalonDock.Controls
{
	public class LayoutGridResizerControl : Control
	{
		private Thumb _innerThumb;
		private InputCursor? _resizeCursor;
		private InputCursor? _defaultCursor;

		// Set by LayoutGridControl.CreateSplitters() after instantiation.
		internal bool IsHorizontalResizer { get; set; }

		static LayoutGridResizerControl()
		{
			// DefaultStyleKey set via XAML generic.xaml template lookup.
		}

		public LayoutGridResizerControl()
		{
			DefaultStyleKey = typeof(LayoutGridResizerControl);
			ResizeModeChanged += OnResizeModeChanged;
		}

		// Expose Thumb-like drag events by forwarding from the inner Thumb.
		public event DragStartedEventHandler DragStarted;
		public event DragDeltaEventHandler DragDelta;
		public event DragCompletedEventHandler DragCompleted;

		protected override void OnApplyTemplate()
		{
			base.OnApplyTemplate();
			_innerThumb = GetTemplateChild("PART_Thumb") as Thumb;
			if (_innerThumb != null)
			{
				_innerThumb.DragStarted += (s, e) => DragStarted?.Invoke(this, e);
				_innerThumb.DragDelta += (s, e) => DragDelta?.Invoke(this, e);
				_innerThumb.DragCompleted += (s, e) => DragCompleted?.Invoke(this, e);
			}

			ApplyCursor();
		}

		public static readonly DependencyProperty BackgroundWhileDraggingProperty =
			DependencyProperty.Register(nameof(BackgroundWhileDragging), typeof(Brush),
				typeof(LayoutGridResizerControl), new PropertyMetadata(null));

		[Bindable(true)]
		public Brush BackgroundWhileDragging
		{
			get => (Brush)GetValue(BackgroundWhileDraggingProperty);
			set => SetValue(BackgroundWhileDraggingProperty, value);
		}

		public static readonly DependencyProperty OpacityWhileDraggingProperty =
			DependencyProperty.Register(nameof(OpacityWhileDragging), typeof(double),
				typeof(LayoutGridResizerControl), new PropertyMetadata(0.5));

		[Bindable(true)]
		public double OpacityWhileDragging
		{
			get => (double)GetValue(OpacityWhileDraggingProperty);
			set => SetValue(OpacityWhileDraggingProperty, value);
		}

		internal event EventHandler? ResizeModeChanged;

		internal void RefreshResizeCursor()
		{
			ResizeModeChanged?.Invoke(this, EventArgs.Empty);
		}

		private void OnResizeModeChanged(object? sender, EventArgs e)
		{
			_resizeCursor = InputSystemCursor.Create(
				IsHorizontalResizer
					? InputSystemCursorShape.SizeWestEast
					: InputSystemCursorShape.SizeNorthSouth);
			_defaultCursor ??= InputSystemCursor.Create(InputSystemCursorShape.Arrow);

			ApplyCursor();
		}

		private void ApplyCursor()
		{
			ProtectedCursor = _resizeCursor ?? _defaultCursor;
		}
	}
}
