// Forked from AvalonDock Controls/LayoutGridResizerControl.cs.
// WinUI's Thumb is sealed so we cannot subclass it.
// Instead, subclass Control and replicate the two DPs + expose Thumb-compatible
// drag events by hosting an inner Thumb. The DragStarted/Delta/Completed events
// are forwarded so LayoutGridControl wires up identically.

using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Core;

namespace AvalonDock.Controls
{
	public class LayoutGridResizerControl : Control
	{
		private Thumb _innerThumb;

		// Set by LayoutGridControl.CreateSplitters() after instantiation.
		internal bool IsHorizontalResizer { get; set; }

		static LayoutGridResizerControl()
		{
			// DefaultStyleKey set via XAML generic.xaml template lookup.
		}

		public LayoutGridResizerControl()
		{
			DefaultStyleKey = typeof(LayoutGridResizerControl);
			PointerEntered += OnPointerEntered;
			PointerExited  += OnPointerExited;
		}

		private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
		{
			var cursor = IsHorizontalResizer
				? new CoreCursor(CoreCursorType.SizeWestEast, 1)
				: new CoreCursor(CoreCursorType.SizeNorthSouth, 1);
			Window.Current?.CoreWindow?.PointerCursor = cursor;
		}

		private void OnPointerExited(object sender, PointerRoutedEventArgs e)
		{
			Window.Current?.CoreWindow?.PointerCursor =
				new CoreCursor(CoreCursorType.Arrow, 1);
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
	}
}
