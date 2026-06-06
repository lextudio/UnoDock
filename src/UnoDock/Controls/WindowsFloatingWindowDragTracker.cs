using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace AvalonDock.Controls
{
	internal sealed class WindowsFloatingWindowDragTracker
	{
		public static bool IsLeftButtonHeld() => (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

		private const int VK_LBUTTON = 0x01;

		[StructLayout(LayoutKind.Sequential)]
		private struct POINT
		{
			public int X;
			public int Y;
		}

		[DllImport("user32.dll")]
		private static extern bool GetCursorPos(out POINT lpPoint);

		[DllImport("user32.dll")]
		private static extern short GetAsyncKeyState(int vKey);

		private readonly LayoutFloatingWindowControl _fwc;
		private readonly DockingManager _manager;
		private readonly bool _skipInitialDelay;
		private DispatcherTimer _timer;
		private double _offsetX;
		private double _offsetY;
		private bool _started;
		private int _tickCount;

		public WindowsFloatingWindowDragTracker(
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
			var (cx, cy) = GetCursorScreen();
			var (wx, wy) = _fwc.GetWindowPosition();
			_offsetX = Math.Max(0, cx - wx);
			_offsetY = Math.Max(0, cy - wy);

			_timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
			_timer.Tick += OnTick;
			_timer.Start();
		}

		public void Stop()
		{
			if (_timer == null)
				return;

			_timer.Stop();
			_timer.Tick -= OnTick;
			_timer = null;
		}

		private void OnTick(object sender, object e)
		{
			try { OnTickCore(); }
			catch { }
		}

		private void OnTickCore()
		{
			var buttonDown = IsLeftButtonHeld();
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

			var (cx, cy) = GetCursorScreen();
			_fwc.MoveWindow(cx - _offsetX, cy - _offsetY);

			if (buttonDown)
			{
				var origin = ManagerScreenOriginProvider?.Invoke() ?? (0, 0);
				OnCursorInManagerCoords?.Invoke(cx - origin.X, cy - origin.Y);
			}
			else
			{
				Stop();
				OnDragEnded?.Invoke();
			}
		}

		public static (double X, double Y) GetCursorScreen()
		{
			return GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0);
		}
	}
}
