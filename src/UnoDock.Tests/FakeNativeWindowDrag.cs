using System;
using System.Collections.Generic;
using AvalonDock.Controls;
using Windows.Foundation;

namespace AvalonDockTest
{
	// Headless test double for the native-drag Strategy (docs/refactoring.md, Challenge 1).
	//
	// Subclasses the real NativeWindowDragBase so it exercises the *actual* invariant
	// algorithm (pre-position → observe → hand off → raise events → tear down) while
	// replacing every OS primitive with a recording no-op. Tests can drive the drag
	// lifecycle on command via Move()/End() with no real window or cursor.
	internal sealed class FakeNativeWindowDrag : NativeWindowDragBase
	{
		public readonly List<(double X, double Y)> Moves = new();
		public int InstallObserversCount;
		public int RemoveObserversCount;
		public int HandOffCount;
		public bool ObserversInstalledBeforeHandOff { get; private set; } = true;

		// Drive the lifecycle from a test.
		public void Move(Point cursor) => RaiseMoving(cursor);

		public void End(Point cursor) => RaiseEnded(cursor);

		protected override void MoveWindowNative(double x, double y) => Moves.Add((x, y));

		protected override void HandOffToNativeMoveLoop(Point cursor, Point grabOffset)
		{
			HandOffCount++;
			if (InstallObserversCount == 0)
				ObserversInstalledBeforeHandOff = false;
		}

		protected override void InstallObservers() => InstallObserversCount++;

		protected override void RemoveObservers() => RemoveObserversCount++;

		protected override Point GetCursorScreen() => default;
	}
}
