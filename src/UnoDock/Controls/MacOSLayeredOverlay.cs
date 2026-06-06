#if !WINDOWS
using System;
using System.Runtime.InteropServices;

namespace AvalonDock.Controls
{
	internal sealed class MacOSLayeredOverlay : IDisposable
	{
		private const string ObjC = "/usr/lib/libobjc.dylib";
		private const nuint NSWindowStyleMaskBorderless = 0;
		private const nuint NSBackingStoreBuffered = 2;
		private const nint NSFloatingWindowLevel = 24;
		private const nuint NSBitmapFormatPremultipliedAlphaLast = 0;
		private static readonly IntPtr NSDeviceRGBColorSpace = CreateNSString("NSDeviceRGBColorSpace");

		private readonly IntPtr _window;
		private readonly IntPtr _imageView;
		private bool _disposed;

		public MacOSLayeredOverlay()
		{
			var frame = new NSRect { Size = new NSSize { Width = 1, Height = 1 } };
			_window = MsgSend_initWindow(
				MsgSend(GetClass("NSWindow"), Sel("alloc")),
				Sel("initWithContentRect:styleMask:backing:defer:"),
				frame,
				NSWindowStyleMaskBorderless,
				NSBackingStoreBuffered,
				0);

			if (_window == IntPtr.Zero)
				return;

			ConfigureWindow(_window);

			_imageView = MsgSend_initWithFrame(
				MsgSend(GetClass("NSImageView"), Sel("alloc")),
				Sel("initWithFrame:"),
				frame);
			if (_imageView != IntPtr.Zero)
			{
				MsgSend_nuint(_imageView, Sel("setImageScaling:"), 1); // NSImageScaleAxesIndependently
				MsgSend_IntPtr(_window, Sel("setContentView:"), _imageView);
			}
		}

		public void Update(
			byte[] rgbaPremultiplied,
			int pixelWidth,
			int pixelHeight,
			double pointWidth,
			double pointHeight,
			double screenX,
			double screenY)
		{
			if (_disposed || _window == IntPtr.Zero || _imageView == IntPtr.Zero || pixelWidth <= 0 || pixelHeight <= 0
				|| pointWidth <= 0 || pointHeight <= 0)
				return;
			if (rgbaPremultiplied == null || rgbaPremultiplied.Length < pixelWidth * pixelHeight * 4)
				return;

			var image = CreateImage(rgbaPremultiplied, pixelWidth, pixelHeight, pointWidth, pointHeight);
			if (image == IntPtr.Zero)
				return;

			var frame = new NSRect { Size = new NSSize { Width = pointWidth, Height = pointHeight } };
			MsgSend_NSRect(_imageView, Sel("setFrame:"), frame);
			MsgSend_IntPtr(_imageView, Sel("setImage:"), image);

			MoveWindowTopLeft(_window, screenX, screenY, pointWidth, pointHeight);
			MsgSend_nint(_window, Sel("setLevel:"), NSFloatingWindowLevel);
			MsgSend(_window, Sel("orderFront:"), IntPtr.Zero);

			MsgSend(image, Sel("release"), IntPtr.Zero);
		}

		public void Hide()
		{
			if (_disposed || _window == IntPtr.Zero) return;
			MsgSend(_window, Sel("orderOut:"), IntPtr.Zero);
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			if (_window != IntPtr.Zero)
			{
				MsgSend(_window, Sel("orderOut:"), IntPtr.Zero);
				MsgSend(_window, Sel("close"), IntPtr.Zero);
				MsgSend(_window, Sel("release"), IntPtr.Zero);
			}
		}

		private static IntPtr CreateImage(
			byte[] rgbaPremultiplied,
			int pixelWidth,
			int pixelHeight,
			double pointWidth,
			double pointHeight)
		{
			var rep = MsgSend_initBitmap(
				MsgSend(GetClass("NSBitmapImageRep"), Sel("alloc")),
				Sel("initWithBitmapDataPlanes:pixelsWide:pixelsHigh:bitsPerSample:samplesPerPixel:hasAlpha:isPlanar:colorSpaceName:bitmapFormat:bytesPerRow:bitsPerPixel:"),
				IntPtr.Zero,
				pixelWidth,
				pixelHeight,
				8,
				4,
				1,
				0,
				NSDeviceRGBColorSpace,
				NSBitmapFormatPremultipliedAlphaLast,
				pixelWidth * 4,
				32);
			if (rep == IntPtr.Zero)
				return IntPtr.Zero;

			var data = MsgSend(rep, Sel("bitmapData"));
			if (data == IntPtr.Zero)
			{
				MsgSend(rep, Sel("release"), IntPtr.Zero);
				return IntPtr.Zero;
			}

			Marshal.Copy(rgbaPremultiplied, 0, data, pixelWidth * pixelHeight * 4);

			var image = MsgSend_initWithSize(
				MsgSend(GetClass("NSImage"), Sel("alloc")),
				Sel("initWithSize:"),
				new NSSize { Width = pointWidth, Height = pointHeight });
			if (image != IntPtr.Zero)
				MsgSend_IntPtr(image, Sel("addRepresentation:"), rep);
			MsgSend(rep, Sel("release"), IntPtr.Zero);
			return image;
		}

		private static void ConfigureWindow(IntPtr window)
		{
			MsgSend_nuint(window, Sel("setStyleMask:"), NSWindowStyleMaskBorderless);
			MsgSend_bool(window, Sel("setOpaque:"), 0);
			var clear = MsgSend(GetClass("NSColor"), Sel("clearColor"));
			if (clear != IntPtr.Zero)
				MsgSend_IntPtr(window, Sel("setBackgroundColor:"), clear);
			MsgSend_bool(window, Sel("setHasShadow:"), 0);
			MsgSend_bool(window, Sel("setIgnoresMouseEvents:"), 1);
			MsgSend_bool(window, Sel("setReleasedWhenClosed:"), 0);
			MsgSend_nint(window, Sel("setLevel:"), NSFloatingWindowLevel);
		}

		private static void MoveWindowTopLeft(IntPtr window, double quartzX, double quartzY, double width, double height)
		{
			var screenH = (double)(long)CGDisplayPixelsHigh(CGMainDisplayID());
			var cocoaTopLeft = new NSPoint
			{
				X = quartzX,
				Y = screenH - quartzY
			};
			MsgSend_NSPoint(window, Sel("setFrameTopLeftPoint:"), cocoaTopLeft);
			MsgSend_NSRect_bool(window, Sel("setFrame:display:"), new NSRect
			{
				Origin = new NSPoint { X = quartzX, Y = screenH - quartzY - height },
				Size = new NSSize { Width = width, Height = height }
			}, 1);
		}

		private static IntPtr CreateNSString(string value)
		{
			var cstr = Marshal.StringToHGlobalAnsi(value);
			try
			{
				return MsgSend_stringWithCString(
					MsgSend(GetClass("NSString"), Sel("alloc")),
					Sel("initWithUTF8String:"),
					cstr);
			}
			finally { Marshal.FreeHGlobal(cstr); }
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NSPoint { public double X; public double Y; }

		[StructLayout(LayoutKind.Sequential)]
		private struct NSSize { public double Width; public double Height; }

		[StructLayout(LayoutKind.Sequential)]
		private struct NSRect { public NSPoint Origin; public NSSize Size; }

		[DllImport(ObjC, EntryPoint = "objc_getClass")]
		private static extern IntPtr GetClass(string name);

		[DllImport(ObjC, EntryPoint = "sel_registerName")]
		private static extern IntPtr Sel(string name);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		private static extern IntPtr MsgSend(IntPtr rcv, IntPtr sel);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		private static extern void MsgSend(IntPtr rcv, IntPtr sel, IntPtr arg);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		private static extern void MsgSend_bool(IntPtr rcv, IntPtr sel, byte value);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		private static extern void MsgSend_nint(IntPtr rcv, IntPtr sel, nint value);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		private static extern void MsgSend_nuint(IntPtr rcv, IntPtr sel, nuint value);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		private static extern void MsgSend_IntPtr(IntPtr rcv, IntPtr sel, IntPtr value);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		private static extern void MsgSend_NSPoint(IntPtr rcv, IntPtr sel, NSPoint value);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		private static extern void MsgSend_NSRect(IntPtr rcv, IntPtr sel, NSRect value);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		private static extern void MsgSend_NSRect_bool(IntPtr rcv, IntPtr sel, NSRect value, byte display);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		private static extern IntPtr MsgSend_stringWithCString(IntPtr rcv, IntPtr sel, IntPtr value);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		private static extern IntPtr MsgSend_initWithFrame(IntPtr rcv, IntPtr sel, NSRect frame);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		private static extern IntPtr MsgSend_initWithSize(IntPtr rcv, IntPtr sel, NSSize size);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		private static extern IntPtr MsgSend_initWindow(
			IntPtr rcv,
			IntPtr sel,
			NSRect contentRect,
			nuint styleMask,
			nuint backing,
			byte defer);

		[DllImport(ObjC, EntryPoint = "objc_msgSend")]
		private static extern IntPtr MsgSend_initBitmap(
			IntPtr rcv,
			IntPtr sel,
			IntPtr planes,
			nint pixelsWide,
			nint pixelsHigh,
			nint bitsPerSample,
			nint samplesPerPixel,
			byte hasAlpha,
			byte isPlanar,
			IntPtr colorSpaceName,
			nuint bitmapFormat,
			nint bytesPerRow,
			nint bitsPerPixel);

		[DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
		private static extern uint CGMainDisplayID();

		[DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
		private static extern nuint CGDisplayPixelsHigh(uint display);
	}
}
#endif
