// Forked from AvalonDock Controls/LayoutGridResizerControl.cs.
// WinUI's Thumb is sealed so we cannot subclass it.
// Instead, subclass Control and replicate the two DPs + expose Thumb-compatible
// drag events by hosting an inner Thumb. The DragStarted/Delta/Completed events
// are forwarded so LayoutGridControl wires up identically.

using System.ComponentModel;
using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace AvalonDock.Controls
{
	public class LayoutGridResizerControl : Control
	{
		private Thumb _innerThumb;
		private InputCursor? _resizeCursor;
		private bool _linuxDragging;
		private Windows.Foundation.Point _linuxLastPoint;
		// Root element hooked for the duration of a drag. Pointer capture on the Uno X11
		// backend does not reliably route PointerMoved once the cursor leaves the 4px
		// splitter strip (Released still arrives, Moved stops). The XamlRoot content
		// root sees moves wherever the cursor is in the window, so drive the drag there.
		private UIElement _linuxDragRoot;
		private PointerEventHandler _linuxRootMoved;
		private PointerEventHandler _linuxRootReleased;

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
				if (OperatingSystem.IsLinux())
				{
					// Linux fallback: Thumb Drag* events and Thumb-local pointer streams are
					// unreliable on some Uno/Linux backends. Use control-level pointer events
					// so the outer template surface drives resize consistently. The Thumb must
					// not be hit-testable, or it steals pointer capture on press and the outer
					// control's CapturePointer fails (moves stop once the cursor leaves the strip).
					_innerThumb.IsHitTestVisible = false;
				}
				else
				{
					_innerThumb.DragStarted += (s, e) => DragStarted?.Invoke(this, e);
					_innerThumb.DragDelta += (s, e) => DragDelta?.Invoke(this, e);
					_innerThumb.DragCompleted += (s, e) => DragCompleted?.Invoke(this, e);
				}
			}

			ApplyCursor();

			if (OperatingSystem.IsLinux())
			{
				AddHandler(PointerPressedEvent, new PointerEventHandler(OnLinuxPointerPressed), handledEventsToo: true);
				AddHandler(PointerMovedEvent, new PointerEventHandler(OnLinuxPointerMoved), handledEventsToo: true);
				AddHandler(PointerReleasedEvent, new PointerEventHandler(OnLinuxPointerReleased), handledEventsToo: true);
				AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnLinuxPointerCaptureLost), handledEventsToo: true);
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

		private void OnLinuxPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			if (!OperatingSystem.IsLinux())
				return;

			// Measure in window coordinates (GetCurrentPoint(null)), NOT relative to this
			// control: live resize moves the splitter under the cursor, so splitter-relative
			// positions snap back each frame and deltas degenerate to ~0.
			var pt = e.GetCurrentPoint(null);
			if (!pt.Properties.IsLeftButtonPressed)
				return;

			_linuxDragging = true;
			_linuxLastPoint = pt.Position;
			var captured = CapturePointer(e.Pointer);
			AttachLinuxRootHandlers();
			DragStarted?.Invoke(this, new DragStartedEventArgs(0, 0));
			AvalonDock.DockingManager.DragLogW($"SplitterPressed horizontal={IsHorizontalResizer} pos=({_linuxLastPoint.X:F1},{_linuxLastPoint.Y:F1}) captured={captured} root={_linuxDragRoot != null}");
			e.Handled = true;
		}

		private void AttachLinuxRootHandlers()
		{
			if (_linuxDragRoot != null)
				return;

			_linuxDragRoot = XamlRoot?.Content as UIElement;
			if (_linuxDragRoot == null)
				return;

			_linuxRootMoved = OnLinuxPointerMoved;
			_linuxRootReleased = OnLinuxPointerReleased;
			_linuxDragRoot.AddHandler(PointerMovedEvent, _linuxRootMoved, handledEventsToo: true);
			_linuxDragRoot.AddHandler(PointerReleasedEvent, _linuxRootReleased, handledEventsToo: true);
		}

		private void DetachLinuxRootHandlers()
		{
			if (_linuxDragRoot == null)
				return;

			_linuxDragRoot.RemoveHandler(PointerMovedEvent, _linuxRootMoved);
			_linuxDragRoot.RemoveHandler(PointerReleasedEvent, _linuxRootReleased);
			_linuxDragRoot = null;
			_linuxRootMoved = null;
			_linuxRootReleased = null;
		}

		private void OnLinuxPointerMoved(object sender, PointerRoutedEventArgs e)
		{
			if (!OperatingSystem.IsLinux() || !_linuxDragging)
				return;

			// The same event can bubble through both this control and the root handler;
			// the shared _linuxLastPoint makes the second sighting compute a zero delta.
			var pt = e.GetCurrentPoint(null).Position;
			var dx = pt.X - _linuxLastPoint.X;
			var dy = pt.Y - _linuxLastPoint.Y;
			_linuxLastPoint = pt;
			if (dx == 0 && dy == 0)
				return;

			AvalonDock.DockingManager.DragLogWVerbose($"SplitterDelta horizontal={IsHorizontalResizer} dx={dx:F1} dy={dy:F1}");
			DragDelta?.Invoke(this, new DragDeltaEventArgs(dx, dy));
			e.Handled = true;
		}

		private void OnLinuxPointerReleased(object sender, PointerRoutedEventArgs e)
		{
			if (!OperatingSystem.IsLinux() || !_linuxDragging)
				return;

			_linuxDragging = false;
			DetachLinuxRootHandlers();
			ReleasePointerCapture(e.Pointer);
			AvalonDock.DockingManager.DragLogW("SplitterReleased");
			DragCompleted?.Invoke(this, new DragCompletedEventArgs(0, 0, false));
			e.Handled = true;
		}

		private void OnLinuxPointerCaptureLost(object sender, PointerRoutedEventArgs e)
		{
			// Capture loss is expected on this backend mid-drag; the root handlers keep
			// the drag alive, so do NOT end the drag here. Release/detach ends it.
			if (!OperatingSystem.IsLinux() || !_linuxDragging)
				return;

			AvalonDock.DockingManager.DragLogW("SplitterCaptureLost (ignored, root handlers active)");
		}
	}
}
