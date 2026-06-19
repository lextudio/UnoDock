// Floating window host for UnoDock.
//
// Design decision (from design.md §6.1): AvalonDock's WPF LayoutFloatingWindowControl
// derives from Window (which is ContentControl in WPF). In Uno Platform, Window is
// NOT a FrameworkElement/Control — you cannot subclass it as a visual tree node.
//
// Instead, UnoDock hosts each floating window as a *real Uno Window* that contains a
// plain FrameworkElement content root. This class manages the lifetime of that Window
// and exposes the AvalonDock ILayoutControl surface.
//
// On macOS the new Window would be merged into the main window's tab bar by the OS
// unless we call [NSWindow setTabbingMode: NSWindowTabbingModeDisallowed] immediately
// after Window.Activate(). MacOSWindowTabbing.DisableLastWindowTabbing() does this.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AvalonDock.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
#if !WINDOWS
using AvalonDock.Hosting;
#endif

namespace AvalonDock.Controls
{
	/// <summary>
	/// Hosts a <see cref="LayoutFloatingWindow"/> model in a dedicated Uno Platform
	/// <see cref="Window"/>. Replaces the WPF-only <c>LayoutFloatingWindowControl : Window</c>
	/// pattern which cannot be ported to Uno (Uno Window is not a FrameworkElement).
	/// </summary>
	public abstract class LayoutFloatingWindowControl : ILayoutControl
	{
		private Window _window;
		private ContentControl _host;
		private Border _titleBar;
		private TextBlock _titleText;
		private Path _titleGrip;
		private Path _windowStateGlyph;
		private nint _nsWindow;
		private System.IntPtr _willMoveObserver; // NSNotificationCenter observer token
		private IntPtr _windowsHwnd;

		/// <summary>Fired when the user starts dragging this floating window's title bar.</summary>
		public Action OnTitleBarDragStarted;

		/// <summary>Fired when child chrome requests closing this floating window.</summary>
		public Action OnChildWindowCloseRequested;

		/// <summary>The NSWindow handle set by DisableLastWindowTabbing().
		/// Valid only on macOS after Show(); 0 on Windows or before Show().</summary>
		public nint NsWindowHandle => _nsWindow;

		// Width/Height/Left/Top are written by DockingManager before Show().
		public double Width  { get; set; } = 400;
		public double Height { get; set; } = 300;
		public double Left   { get; set; }
		public double Top    { get; set; }
		public bool ShowHiddenUntilPositioned { get; set; }

		public bool KeepContentVisibleOnClose { get; set; }

		public abstract ILayoutElement Model { get; }

		public bool IsVisible => _window != null;

		/// <summary>Creates the Uno Window and shows it.</summary>
		public void Show()
		{
			if (_window != null) return;

			_host = new ContentControl
			{
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment   = VerticalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				VerticalContentAlignment   = VerticalAlignment.Stretch,
			};

			SetHostContent(_host);

			_window = new Window();
			_window.Title = GetWindowTitle();
			if (OperatingSystem.IsWindows())
			{
				_window.Content = CreateWindowsWindowContent();
			}
			else
			{
				// A floating window is a separate Window with its own visual-tree root, so the
				// DockingManager's merged theme dictionary does not reach it. Merge the active
				// theme (e.g. VS2013) into the content host so the floated pane/tabs are themed —
				// the macOS counterpart of what CreateWindowsWindowContent does on Windows.
				AddManagerThemeResources(_host);
				_window.Content = _host;
			}
			_window.Closed += OnWindowClosed;
			_window.Activated += OnWindowActivated;

			_window.Activate();

			// Resize — Uno AppWindow API
			try
			{
				var aw = _window.AppWindow;
				if (aw != null)
				{
					aw.Resize(new Windows.Graphics.SizeInt32 { Width = (int)Width, Height = (int)Height });
					if (OperatingSystem.IsWindows() || Left != 0 || Top != 0)
						aw.Move(new Windows.Graphics.PointInt32 { X = (int)Left, Y = (int)Top });
					if (OperatingSystem.IsWindows())
						HideNativeWindowChrome();
				}
			}
			catch { /* best-effort */ }

			if (OperatingSystem.IsWindows())
			{
				// NOTE: do NOT set ExtendsContentIntoTitleBar=true here. The window is made
				// fully borderless via OverlappedPresenter.SetBorderAndTitleBar(false,false) +
				// Win32 WS_CAPTION stripping (HideNativeWindowChrome). Re-enabling the extended
				// title bar makes WinUI paint a default title bar that flashes the system accent
				// (blue) for one frame on every activation (e.g. clicking the title bar to drag).
				_window.DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
					HideNativeWindowChrome);
			}

#if !WINDOWS
			if (OperatingSystem.IsMacOS())
			{
				// On macOS: prevent OS from merging this window into the main window's tab bar.
				_nsWindow = MacOSWindowTabbing.DisableLastWindowTabbing();
				if (_nsWindow != 0 && (Left != 0 || Top != 0))
					MacOSWindowTabbing.MoveWindow(_nsWindow, Left, Top, 0, 0);
				if (_nsWindow != 0 && ShowHiddenUntilPositioned)
					MacOSWindowTabbing.HideWindow(_nsWindow);
				// Register for NSWindowWillMoveNotification so DockingManager can start
				// the drag tracker precisely when the user grabs the title bar.
				if (_nsWindow != 0)
					_willMoveObserver = MacOSWindowTabbing.RegisterWindowWillMove(
						_nsWindow, () => OnTitleBarDragStarted?.Invoke());
			}
#endif
		}

		/// <summary>Hides and destroys the floating window.</summary>
		public void InternalClose()
		{
			if (_window == null) return;
#if !WINDOWS
			if (OperatingSystem.IsMacOS())
			{
				// Unregister NSWindowWillMoveNotification before closing to avoid
				// a callback on a zombie window.
				if (_willMoveObserver != System.IntPtr.Zero)
				{
					MacOSWindowTabbing.UnregisterWindowWillMove(_willMoveObserver);
					_willMoveObserver = System.IntPtr.Zero;
				}
				MacOSWindowTabbing.CloseWindow(_nsWindow);
				_nsWindow = 0;
				return;
			}

			_window.Close();
#else
			_window.Close();
#endif
		}

		/// <summary>Move the window to the given screen position (called during drag).</summary>
		public void MoveWindow(double x, double y)
		{
#if !WINDOWS
			if (OperatingSystem.IsMacOS() && _nsWindow != 0)
			{
				MacOSWindowTabbing.MoveWindow(_nsWindow, x, y, 0, 0);
				return;
			}
#endif
			try { _window?.AppWindow?.Move(new Windows.Graphics.PointInt32 { X = (int)x, Y = (int)y }); }
			catch { }
		}

		public (double X, double Y) GetWindowPosition()
		{
			try
			{
				var pos = _window?.AppWindow?.Position;
				return pos.HasValue ? (pos.Value.X, pos.Value.Y) : (0, 0);
			}
			catch { return (0, 0); }
		}

		/// <summary>
		/// Bring the floating window to the top of the non-topmost Z-order tier on Windows,
		/// placing it above the main window but below the HWND_TOPMOST overlay compass window.
		/// Mirrors macOS MacOSWindowTabbing.OrderWindowFront() used in StartDragTracking.
		/// No-op on non-Windows platforms.
		/// </summary>
		public void BringToFrontWindows()
		{
			if (!OperatingSystem.IsWindows()) return;
			EnsureWindowsHwnd();
			if (_windowsHwnd == IntPtr.Zero) return;
			SetWindowPos(_windowsHwnd, HWND_TOP, 0, 0, 0, 0,
				SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
		}

		private void EnsureWindowsHwnd()
		{
			if (_windowsHwnd == IntPtr.Zero)
				_windowsHwnd = FindWindowForCurrentProcess(GetWindowTitle());
		}

		/// <summary>
		/// Factory Method (session 26): create the platform-appropriate native drag
		/// strategy for this floating window. Returns null if no native handle is
		/// available yet (caller should fall back to the timer tracker).
		/// See docs/drag-to-float.md Part 4.
		/// </summary>
		internal INativeWindowDrag CreateNativeDrag()
		{
			// macOS: NOT supported. AppKit's performWindowDragWithEvent: only starts a
			// window drag when invoked from the live mouseDown: NSEvent currently being
			// processed. Our tear-off originates from an Uno PointerMoved (no real
			// NSEvent), and the button-down is owned by the main window's tab — so a
			// synthesized event never attaches a drag (window stays put, no NSWindowDidMove,
			// no overlay). Returning null makes the caller fall back to the timer tracker,
			// which is the proven macOS path. See docs/session26.md.
			if (OperatingSystem.IsWindows())
			{
				EnsureWindowsHwnd();
				return _windowsHwnd != IntPtr.Zero ? new WindowsNativeWindowDrag(_windowsHwnd) : null;
			}

			return null;
		}

		public void SetWindowsAlpha(double alpha)
		{
			if (!OperatingSystem.IsWindows())
				return;

			EnsureWindowsHwnd();
			var hwnd = _windowsHwnd;
			if (hwnd == IntPtr.Zero)
				return;
			alpha = Math.Clamp(alpha, 0.0, 1.0);
			var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
			SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle | WS_EX_LAYERED));
			SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(alpha * 255.0), LWA_ALPHA);
		}

		private UIElement CreateWindowsWindowContent()
		{
			if (!ShowWindowsTitleBar)
			{
				AddManagerThemeResources(_host);
				return new Border
				{
					Background = Brush(0xFF, 0xEE, 0xEE, 0xF2),
					BorderBrush = Brush(0xFF, 0xCC, 0xCE, 0xDB),
					BorderThickness = new Thickness(1),
					Child = _host,
				};
			}

			var titleText = new TextBlock
			{
				Text = GetWindowTitle(),
				FontSize = 12,
				FontWeight = UseToolWindowTitleChrome ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(4, 0, 6, 0),
			};
			if (!UseToolWindowTitleChrome)
				titleText.Foreground = Brush(0xFF, 0x44, 0x44, 0x44);
			_titleText = titleText;

			var buttonPanel = new StackPanel
			{
				Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Top,
			};
			_windowStateGlyph = CreateCaptionGlyph(MaximizeGlyph, 8, 8);
			if (UseToolWindowTitleChrome)
				buttonPanel.Children.Add(CreateCaptionButton(CreateCaptionGlyph(MenuGlyph, 7, 4), OnFloatingWindowMenuClick));
			buttonPanel.Children.Add(CreateCaptionButton(_windowStateGlyph, ToggleMaximizeRestore));
			buttonPanel.Children.Add(CreateCaptionButton(CreateCaptionGlyph(CloseGlyph, 8, 8), RequestCloseFromChrome));

			var titleGrid = new Grid();
			titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			Grid.SetColumn(titleText, 0);
			var dragHandle = new Path
			{
				Height = 5,
				Margin = new Thickness(4, 0, 4, 0),
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Center,
			};
			_titleGrip = dragHandle;
			if (UseToolWindowTitleChrome)
				dragHandle.SizeChanged += OnTitleGripSizeChanged;
			else
			{
				dragHandle.Data = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 2, 1, 1) };
				dragHandle.Fill = Brush(0xFF, 0xA8, 0xA8, 0xA8);
				dragHandle.Stretch = Stretch.Fill;
				dragHandle.Opacity = 0.75;
			}
			Grid.SetColumn(dragHandle, 1);
			Grid.SetColumn(buttonPanel, 2);
			titleGrid.Children.Add(titleText);
			titleGrid.Children.Add(dragHandle);
			titleGrid.Children.Add(buttonPanel);

			_titleBar = new Border
			{
				Height = 21,
				Child = titleGrid,
			};
			_titleBar.PointerPressed += OnTitleBarPointerPressed;

			var root = new Grid
			{
				Background = Brush(0xFF, 0xEE, 0xEE, 0xF2),
			};
			AddManagerThemeResources(root);
			root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(21) });
			root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
			Grid.SetRow(_titleBar, 0);
			var contentFrame = new Border
			{
				Child = _host,
			};
			Grid.SetRow(contentFrame, 1);
			root.Children.Add(_titleBar);
			root.Children.Add(contentFrame);
			if (UseToolWindowTitleChrome)
				UpdateWindowsTitleBarChrome();
			else
				_titleBar.Background = Brush(0xFF, 0xEE, 0xEE, 0xF2);

			return new Border
			{
				Background = Brush(0xFF, 0xEE, 0xEE, 0xF2),
				BorderBrush = Brush(0xFF, 0xCC, 0xCE, 0xDB),
				BorderThickness = new Thickness(1),
				Child = root,
			};
		}

		private Button CreateCaptionButton(string glyph, Action action)
			=> CreateCaptionButton(new TextBlock
			{
				Text = glyph,
				FontSize = glyph == "_" ? 14 : 12,
				Foreground = Brush(0xFF, 0x44, 0x44, 0x44),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
			}, action);

		private Button CreateCaptionButton(UIElement content, Action action)
		{
			var button = new Button
			{
				Width = 20,
				Height = 20,
				Padding = new Thickness(0),
				Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
				BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
				BorderThickness = new Thickness(0),
				Content = content,
			};
			button.Click += (_, _) => action?.Invoke();
			return button;
		}

		private void RequestCloseFromChrome()
		{
			if (OnChildWindowCloseRequested != null)
				OnChildWindowCloseRequested.Invoke();
			else
				_window?.Close();
		}

		private void OnFloatingWindowMenuClick()
		{
			// Placeholder for AvalonDock's floating-window context menu. The button is part
			// of the chrome contract even when no app-supplied menu is available yet.
		}

		private void ToggleMaximizeRestore()
		{
			try
			{
				if (_window?.AppWindow?.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter)
					return;

				if (presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized)
				{
					presenter.Restore();
					if (_windowStateGlyph != null)
					{
						_windowStateGlyph.Width = 9;
						_windowStateGlyph.Height = 9;
						_windowStateGlyph.Data = ParseGeometry(MaximizeGlyph);
					}
				}
				else
				{
					presenter.Maximize();
					if (_windowStateGlyph != null)
					{
						_windowStateGlyph.Width = 10;
						_windowStateGlyph.Height = 10;
						_windowStateGlyph.Data = ParseGeometry(RestoreGlyph);
					}
				}
			}
			catch { }
		}

		private static SolidColorBrush Brush(byte a, byte r, byte g, byte b)
			=> new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));

		private static Path CreateCaptionGlyph(string data, double width, double height)
			=> new Path
			{
				Width = width,
				Height = height,
				Data = ParseGeometry(data),
				Fill = Brush(0xFF, 0x44, 0x44, 0x44),
				Stretch = Stretch.Uniform,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
			};

		private static Geometry ParseGeometry(string data)
		{
			try
			{
				var escaped = data.Replace("&", "&amp;").Replace("\"", "&quot;");
				var path = (Path)Microsoft.UI.Xaml.Markup.XamlReader.Load(
					$"<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"{escaped}\" />");
				return path?.Data;
			}
			catch { return null; }
		}

		private const string MaximizeGlyph =
			"M0,0L0,9 9,9 9,0 0,0 0,3 8,3 8,8 1,8 1,3 0,3z";

		private const string RestoreGlyph =
			"M0,10L0,3 3,3 3,0 10,0 10,2 4,2 4,3 7,3 7,6 6,6 6,5 1,5 1,10z M1,10L7,10 7,7 10,7 10,2 9,2 9,6 6,6 6,9 1,9z";

		private const string CloseGlyph =
			"M 0,2.0345e-005L 7.62109,2.0345e-005L 19.2627,12.0551L 30.9043,2.0345e-005L 38.5241,2.0345e-005L 23.0726,16.0003L 38.5234,32L 30.9023,32L 19.2621,19.9462L 7.62177,32L 0.00195313,32L 15.4521,16.001L 0,2.0345e-005 Z";

		private const string MenuGlyph =
			"M 0,0 L 7,0 L 3.5,4 Z";

		private void AddManagerThemeResources(FrameworkElement element)
		{
			try
			{
				if (Model is not LayoutFloatingWindow floatingWindow)
					return;
				if (floatingWindow.Root is not LayoutRoot root)
					return;
				var theme = root.Manager?.Theme;
				if (theme == null)
					return;

				element.Resources.MergedDictionaries.Add(
					theme is AvalonDock.Themes.DictionaryTheme dt
						? dt.ThemeResourceDictionary
						: new ResourceDictionary { Source = theme.GetResourceUri() });
			}
			catch { }
		}

		private void UpdateWindowsTitleBarChrome()
		{
			var isActive = GetSelectedContent()?.IsActive == true;
			var captionBg = ResolveBrush(
				isActive ? "UnoDock_VS2013_ToolWindowCaptionActiveBackground" : "UnoDock_VS2013_ToolWindowCaptionInactiveBackground",
				Brush(0xFF, 0xEE, 0xEE, 0xF2));
			var captionText = ResolveBrush(
				isActive ? "UnoDock_VS2013_ToolWindowCaptionActiveText" : "UnoDock_VS2013_ToolWindowCaptionInactiveText",
				Brush(0xFF, 0x44, 0x44, 0x44));
			var grip = ResolveBrush(
				isActive ? "UnoDock_VS2013_ToolWindowCaptionActiveGrip" : "UnoDock_VS2013_ToolWindowCaptionInactiveGrip",
				Brush(0xFF, 0xA8, 0xA8, 0xA8));
			var glyph = ResolveBrush(
				isActive ? "UnoDock_VS2013_ToolWindowCaptionButtonActiveGlyph" : "UnoDock_VS2013_ToolWindowCaptionButtonInactiveGlyph",
				captionText);

			if (_titleBar != null)
				_titleBar.Background = captionBg;
			if (_titleText != null)
				_titleText.Foreground = captionText;
			if (_titleGrip != null)
				_titleGrip.Fill = grip;
			SetCaptionButtonGlyphBrush(_titleBar, glyph);
		}

		private static void SetCaptionButtonGlyphBrush(DependencyObject node, Brush brush)
		{
			if (node == null)
				return;
			if (node is Path path)
				path.Fill = brush;
			if (node is TextBlock text)
				text.Foreground = brush;

			var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
			for (var index = 0; index < count; index++)
				SetCaptionButtonGlyphBrush(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, index), brush);
		}

		private Brush ResolveBrush(string key, Brush fallback)
		{
			DependencyObject current = _titleBar != null ? _titleBar : _host;
			while (current != null)
			{
				if (current is FrameworkElement fe
					&& fe.Resources != null
					&& fe.Resources.TryGetValue(key, out var scoped)
					&& scoped is Brush scopedBrush)
					return scopedBrush;

				current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
			}

			var appResources = Application.Current?.Resources;
			if (appResources != null && appResources.TryGetValue(key, out var app) && app is Brush appBrush)
				return appBrush;

			return fallback;
		}

		private void OnTitleGripSizeChanged(object sender, SizeChangedEventArgs e)
		{
			if (_titleGrip == null)
				return;

			var width = e.NewSize.Width;
			var dots = new GeometryGroup();
			for (var x = 0.0; x + 1 <= width; x += 4)
				dots.Children.Add(new RectangleGeometry { Rect = new Windows.Foundation.Rect(x, 0, 1, 1) });
			for (var x = 2.0; x + 1 <= width; x += 4)
				dots.Children.Add(new RectangleGeometry { Rect = new Windows.Foundation.Rect(x, 2, 1, 1) });
			_titleGrip.Data = dots;
		}

		private void HideNativeWindowChrome()
		{
			if (!OperatingSystem.IsWindows())
				return;

			try
			{
				if (_window?.AppWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
					presenter.SetBorderAndTitleBar(false, false);
			}
			catch { }

			var hwnd = _windowsHwnd != IntPtr.Zero ? _windowsHwnd : FindWindowForCurrentProcess(GetWindowTitle());
			if (hwnd == IntPtr.Zero)
				return;

			_windowsHwnd = hwnd;
			var style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
			style &= ~(WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
			style |= WS_THICKFRAME;
			SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));
			SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
				SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
		}

		private void OnTitleBarPointerPressed(object sender, PointerRoutedEventArgs e)
		{
			if (!OperatingSystem.IsWindows())
				return;

			if (e.GetCurrentPoint(_titleBar).Properties.IsLeftButtonPressed)
			{
				DockingManager.DragLogW(
					$"TitleBarPressed: OnTitleBarDragStarted={(OnTitleBarDragStarted != null ? "set" : "null")}");
				e.Handled = true;
				// Defer to next dispatcher tick so WM_LBUTTONDOWN finishes processing on
				// the Uno WndProc before we send WM_NCLBUTTONDOWN to the same HWND.
				// Calling SendMessage re-entrantly within WM_LBUTTONDOWN (same HWND) prevents
				// DefWindowProc from entering the modal move loop — same fix as WPF AvalonDock's
				// Dispatcher.BeginInvoke pattern.
				_window?.DispatcherQueue?.TryEnqueue(
					Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal,
					() =>
					{
						DockingManager.DragLogW("TitleBarPressed deferred: invoking OnTitleBarDragStarted");
						OnTitleBarDragStarted?.Invoke();
					});
			}
		}

		protected abstract void SetHostContent(ContentControl host);
		protected abstract string GetWindowTitle();

		/// <summary>The content that should become active when this floating window is focused.</summary>
		protected virtual LayoutContent GetSelectedContent() => null;

		protected virtual bool ShowWindowsTitleBar => true;
		protected virtual bool UseToolWindowTitleChrome => false;

		// When the floating window gains focus, mark its content active so its tab renders
		// with the active (focused) highlight. Clicking another window deactivates it via the
		// normal ActiveContent flow (the newly-activated content clears this one's IsActive).
		private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
		{
			var content = GetSelectedContent();
			DockingManager.DragLogW(
				$"FloatWin.Activated state={e.WindowActivationState} title='{content?.Title}' " +
				$"contentIsActive={content?.IsActive}");
			// Only a genuine user click on the floating window (PointerActivated) claims active
			// content. CodeActivated fires as a spurious programmatic re-activation ~80ms AFTER
			// the user clicks AWAY to the main window; honoring it would steal active back and
			// leave the just-clicked main tab selected-but-unfocused. The initial floated-content
			// activation is handled by the floating pane's SyncSelection, not here.
			if ((int)e.WindowActivationState != (int)WindowActivationState.PointerActivated)
				return;
			if (content != null && !content.IsActive)
				content.IsActive = true;
			if (UseToolWindowTitleChrome)
				UpdateWindowsTitleBarChrome();
		}

		private void OnWindowClosed(object sender, WindowEventArgs e)
		{
			if (_window != null)
				_window.Activated -= OnWindowActivated;
			_window = null;
			_nsWindow = 0;
			_windowsHwnd = IntPtr.Zero;
		}

		private const int GWL_STYLE = -16;
		private const int GWL_EXSTYLE = -20;
		private const long WS_CAPTION = 0x00C00000L;
		private const long WS_SYSMENU = 0x00080000L;
		private const long WS_THICKFRAME = 0x00040000L;
		private const long WS_MINIMIZEBOX = 0x00020000L;
		private const long WS_MAXIMIZEBOX = 0x00010000L;
		private const long WS_EX_LAYERED = 0x00080000L;
		private const uint LWA_ALPHA = 0x00000002;
		private const uint SWP_NOSIZE = 0x0001;
		private const uint SWP_NOMOVE = 0x0002;
		private const uint SWP_NOZORDER = 0x0004;
		private static readonly IntPtr HWND_TOP = new IntPtr(0);
		private const uint SWP_NOACTIVATE = 0x0010;
		private const uint SWP_FRAMECHANGED = 0x0020;

		private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

		[DllImport("user32.dll")]
		private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

		[DllImport("user32.dll")]
		private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

		[DllImport("user32.dll")]
		private static extern bool IsWindowVisible(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

		[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
		private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
		private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

		private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
			=> IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

		[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
		private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

		[DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
		private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

		private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
			=> IntPtr.Size == 8
				? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
				: new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

		[DllImport("user32.dll")]
		private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

		[DllImport("user32.dll")]
		private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

		private static IntPtr FindWindowForCurrentProcess(string title)
		{
			var currentPid = (uint)Process.GetCurrentProcess().Id;
			var result = IntPtr.Zero;
			EnumWindows((hwnd, _) =>
			{
				if (!IsWindowVisible(hwnd))
					return true;

				GetWindowThreadProcessId(hwnd, out var pid);
				if (pid != currentPid)
					return true;

				var sb = new StringBuilder(256);
				GetWindowText(hwnd, sb, sb.Capacity);
				if (sb.ToString() == title)
				{
					result = hwnd;
					return false;
				}

				return true;
			}, IntPtr.Zero);
			return result;
		}

		// ILayoutControl
		ILayoutElement ILayoutControl.Model => Model;
	}

	/// <summary>Floating window for <see cref="LayoutAnchorableFloatingWindow"/>.</summary>
	public sealed class LayoutAnchorableFloatingWindowControl : LayoutFloatingWindowControl, ILayoutControl
	{
		private readonly LayoutAnchorableFloatingWindow _model;

		public LayoutAnchorableFloatingWindowControl(
			LayoutAnchorableFloatingWindow model,
			bool isContentImmutable = false)
		{
			_model = model ?? throw new ArgumentNullException(nameof(model));
		}

		public override ILayoutElement Model => _model;

		protected override bool UseToolWindowTitleChrome => true;

		protected override string GetWindowTitle()
		{
			var pane = _model.RootPanel?.Descendents().OfType<LayoutAnchorablePane>()
				.FirstOrDefault();
			return pane?.SelectedContent?.Title ?? "Tool Window";
		}

		protected override LayoutContent GetSelectedContent()
			=> _model.RootPanel?.Descendents().OfType<LayoutAnchorablePane>()
				.FirstOrDefault()?.SelectedContent;

		protected override void SetHostContent(ContentControl host)
		{
			// Phase 5: create a LayoutAnchorablePaneControl for the root pane.
			var pane = _model.RootPanel?.Descendents().OfType<LayoutAnchorablePane>()
				.FirstOrDefault();
			if (pane != null)
				host.Content = new LayoutAnchorablePaneControl(pane, true)
				{
					FloatingWindowDragStarted = () => OnTitleBarDragStarted?.Invoke(),
					FloatingWindowCloseRequested = () => OnChildWindowCloseRequested?.Invoke(),
					HideHeaderWhenHostedInFloatingWindow = true
				};
		}
	}

	/// <summary>Floating window for <see cref="LayoutDocumentFloatingWindow"/>.</summary>
	public sealed class LayoutDocumentFloatingWindowControl : LayoutFloatingWindowControl, ILayoutControl
	{
		private readonly LayoutDocumentFloatingWindow _model;

		public LayoutDocumentFloatingWindowControl(
			LayoutDocumentFloatingWindow model,
			bool isContentImmutable = false)
		{
			_model = model ?? throw new ArgumentNullException(nameof(model));
		}

		public override ILayoutElement Model => _model;

		protected override string GetWindowTitle()
		{
			var pane = _model.RootPanel?.Descendents().OfType<LayoutDocumentPane>()
				.FirstOrDefault();
			return pane?.SelectedContent?.Title ?? "Document";
		}

		protected override LayoutContent GetSelectedContent()
			=> _model.RootPanel?.Descendents().OfType<LayoutDocumentPane>()
				.FirstOrDefault()?.SelectedContent;

		protected override void SetHostContent(ContentControl host)
		{
			var pane = _model.RootPanel?.Descendents().OfType<LayoutDocumentPane>()
				.FirstOrDefault();
			if (pane != null)
				host.Content = new LayoutDocumentPaneControl(pane, true);
		}
	}
}
