// Forked from AvalonDock Controls/LayoutGridResizerControl.cs.
// WinUI's Thumb is sealed so we cannot subclass it.
// Instead, subclass Control and replicate the two DPs + expose Thumb-compatible
// drag events by hosting an inner Thumb. The DragStarted/Delta/Completed events
// are forwarded so LayoutGridControl wires up identically.

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

		// Set by LayoutGridControl.CreateSplitters() after instantiation.
		internal bool IsHorizontalResizer { get; set; }

		static LayoutGridResizerControl()
		{
			// DefaultStyleKey set via XAML generic.xaml template lookup.
		}

		public LayoutGridResizerControl()
		{
			DefaultStyleKey = typeof(LayoutGridResizerControl);
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

		// Called by LayoutGridControl when orientation/resize-mode changes after the
		// template is applied, so the cursor stays in sync with IsHorizontalResizer.
		internal void RefreshResizeCursor() => ApplyCursor();

		private void ApplyCursor()
		{
			// IsHorizontalResizer is set by CreateSplitters() before the control enters the
			// tree, so it is reliable here. Build the cursor eagerly — the inner Thumb (the
			// actual hit-test target) inherits CalculatedFinalCursor from this control.
			_resizeCursor = InputSystemCursor.Create(
				IsHorizontalResizer
					? InputSystemCursorShape.SizeWestEast
					: InputSystemCursorShape.SizeNorthSouth);

			ProtectedCursor = _resizeCursor;
		}
	}
}
