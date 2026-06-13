using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace AvalonDock.Controls
{
	internal sealed class LinuxFloatingWindowDragTracker
	{
		private readonly LayoutFloatingWindowControl _fwc;
		private readonly DockingManager _manager;
		private readonly bool _skipInitialDelay;
		private DispatcherTimer _timer;
		private double _offsetX;
		private double _offsetY;
		private bool _offsetCaptured;
		private bool _started;
		private int _tickCount;

		// Drag state is ENTIRELY event-driven. On the Uno X11 backend both CoreWindow
		// facilities are unreliable here: PointerPosition returns garbage, and
		// GetKeyState(LeftButton) resets when the new floating window activates
		// (observed: reports 'up' on the first tick of a held-button drag).
		//
		// X11's implicit pointer grab routes ALL pointer events to the window where
		// the button went down, until release:
		//   - tear-off drag: press was on the MAIN window's tab strip → moves and the
		//     final release arrive on the manager's root; the float window sees nothing;
		//   - re-drag: press was on the float title bar → events arrive on the float root.
		// Hook both roots; whichever delivers events owns the gesture. A release (or a
		// move reporting button-up) is only honored from a root that has already
		// delivered moves this drag — window creation under a held button can synthesize
		// a spurious release on the brand-new window.
		private (double X, double Y)? _pointerScreen;
		private bool _buttonDown = true;
		private UIElement _floatRoot;
		private UIElement _managerRoot;
		private PointerEventHandler _floatMoved;
		private PointerEventHandler _floatReleased;
		private PointerEventHandler _managerMoved;
		private PointerEventHandler _managerReleased;
		private int _floatMoves;
		private int _managerMoves;

		public LinuxFloatingWindowDragTracker(
			LayoutFloatingWindowControl fwc,
			DockingManager manager,
			bool skipInitialDelay = false)
		{
			_fwc = fwc;
			_manager = manager;
			_skipInitialDelay = skipInitialDelay;
		}

		public Action<double, double> OnCursorInManagerCoords;
		public Action OnDragEnded;
		public Func<(double X, double Y)> ManagerScreenOriginProvider { get; set; }

		private int MinTicksBeforeDrop => _skipInitialDelay ? 0 : 3;

		public void Start()
		{
			if (_started)
				return;

			_started = true;
			AttachPointerHooks();

			_timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
			_timer.Tick += OnTick;
			_timer.Start();
		}

		public void Stop()
		{
			DetachPointerHooks();
			if (_timer == null)
				return;

			_timer.Stop();
			_timer.Tick -= OnTick;
			_timer = null;
		}

		private void AttachPointerHooks()
		{
			_floatRoot = _fwc.GetContentRoot();
			if (_floatRoot != null)
			{
				_floatMoved = OnFloatPointerMoved;
				_floatReleased = OnFloatPointerReleased;
				_floatRoot.AddHandler(UIElement.PointerMovedEvent, _floatMoved, handledEventsToo: true);
				_floatRoot.AddHandler(UIElement.PointerReleasedEvent, _floatReleased, handledEventsToo: true);
			}

			_managerRoot = _manager?.XamlRoot?.Content as UIElement;
			if (_managerRoot != null)
			{
				_managerMoved = OnManagerPointerMoved;
				_managerReleased = OnManagerPointerReleased;
				_managerRoot.AddHandler(UIElement.PointerMovedEvent, _managerMoved, handledEventsToo: true);
				_managerRoot.AddHandler(UIElement.PointerReleasedEvent, _managerReleased, handledEventsToo: true);
			}
		}

		private void DetachPointerHooks()
		{
			if (_floatRoot != null)
			{
				_floatRoot.RemoveHandler(UIElement.PointerMovedEvent, _floatMoved);
				_floatRoot.RemoveHandler(UIElement.PointerReleasedEvent, _floatReleased);
				_floatRoot = null;
				_floatMoved = null;
				_floatReleased = null;
			}

			if (_managerRoot != null)
			{
				_managerRoot.RemoveHandler(UIElement.PointerMovedEvent, _managerMoved);
				_managerRoot.RemoveHandler(UIElement.PointerReleasedEvent, _managerReleased);
				_managerRoot = null;
				_managerMoved = null;
				_managerReleased = null;
			}
		}

		private void OnFloatPointerMoved(object sender, PointerRoutedEventArgs e)
		{
			try
			{
				var cp = e.GetCurrentPoint(null);
				var (wx, wy) = _fwc.GetWindowPosition();
				var scale = _fwc.GetRasterizationScale();
				_pointerScreen = (wx / scale + cp.Position.X, wy / scale + cp.Position.Y);
				_floatMoves++;
				if (_floatMoves > 1 && !cp.Properties.IsLeftButtonPressed)
					_buttonDown = false;
			}
			catch { }
		}

		private void OnManagerPointerMoved(object sender, PointerRoutedEventArgs e)
		{
			try
			{
				// cp is relative to the MAIN window's root; convert with the main
				// window origin (same convention as ComputeScreenOriginLinux).
				var cp = e.GetCurrentPoint(null);
				var aw = Microsoft.UI.Xaml.Window.Current?.AppWindow;
				if (aw == null)
					return;
				var scale = _managerRoot?.XamlRoot?.RasterizationScale ?? 1.0;
				if (scale <= 0) scale = 1.0;
				_pointerScreen = (aw.Position.X / scale + cp.Position.X, aw.Position.Y / scale + cp.Position.Y);
				_managerMoves++;
				if (_managerMoves > 1 && !cp.Properties.IsLeftButtonPressed)
					_buttonDown = false;
			}
			catch { }
		}

		private void OnFloatPointerReleased(object sender, PointerRoutedEventArgs e)
		{
			if (_floatMoves > 0)
				_buttonDown = false;
		}

		private void OnManagerPointerReleased(object sender, PointerRoutedEventArgs e)
		{
			if (_managerMoves > 0)
				_buttonDown = false;
		}

		private void OnTick(object sender, object e)
		{
			try { OnTickCore(); }
			catch { }
		}

		private void OnTickCore()
		{
			var buttonDown = _buttonDown;
			if (_tickCount < MinTicksBeforeDrop)
			{
				_tickCount++;
				if (!buttonDown)
					return;
			}
			else
			{
				_tickCount++;
			}

			var cur = _pointerScreen;
			var scale = _fwc.GetRasterizationScale();

			if (cur.HasValue)
			{
				if (!_offsetCaptured)
				{
					// Cursor offset within the window, in logical pixels — captured from
					// the first real cursor sample.
					var (wx, wy) = _fwc.GetWindowPosition();
					_offsetX = Math.Max(0, cur.Value.X - wx / scale);
					_offsetY = Math.Max(0, cur.Value.Y - wy / scale);
					_offsetCaptured = true;
				}

				_fwc.MoveWindow((cur.Value.X - _offsetX) * scale, (cur.Value.Y - _offsetY) * scale);
			}

			if (buttonDown)
			{
				if (cur.HasValue)
				{
					var origin = ManagerScreenOriginProvider?.Invoke() ?? (0, 0);
					OnCursorInManagerCoords?.Invoke(cur.Value.X - origin.X, cur.Value.Y - origin.Y);
				}
			}
			else
			{
				DockingManager.DragLogW(
					$"LinuxTracker end: ticks={_tickCount} floatMoves={_floatMoves} " +
					$"managerMoves={_managerMoves} hadPointer={_pointerScreen.HasValue}");
				Stop();
				OnDragEnded?.Invoke();
			}
		}

		/// <summary>Kept for API compatibility (DockingManager status reporting). The
		/// CoreWindow key state is unreliable on the Uno X11 backend — do not use it to
		/// drive drag logic.</summary>
		public static bool IsLeftButtonHeld()
		{
			try
			{
				var coreWindow = CoreWindow.GetForCurrentThread();
				if (coreWindow == null)
					return false;
				var state = coreWindow.GetKeyState(VirtualKey.LeftButton);
				return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
			}
			catch { return false; }
		}

		/// <summary>Legacy entry point used by DockingManager.NativeCursorScreen routing.
		/// CoreWindow.PointerPosition is unreliable on the Uno X11 backend; callers should
		/// prefer event-driven coordinates.</summary>
		public static (double X, double Y) GetCursorScreen()
		{
			try
			{
				var coreWindow = CoreWindow.GetForCurrentThread();
				if (coreWindow != null)
				{
					var p = coreWindow.PointerPosition;
					return (p.X, p.Y);
				}
			}
			catch { }

			return (0, 0);
		}
	}
}
