using NUnit.Framework;
using AvalonDock.Controls;

namespace AvalonDockTest
{
	// Headless test double for the pointer seam (docs/refactoring.md, Challenge 4),
	// so future drag-orchestration tests can supply a deterministic cursor/button.
	internal sealed class FakePointerProbe : IPointerProbe
	{
		public (double X, double Y) Cursor;
		public bool LeftDown;

		public (double X, double Y) GetCursorScreen() => Cursor;
		public bool IsLeftButtonDown() => LeftDown;
	}

	[TestFixture]
	public class PointerProbeTests
	{
		[Test]
		public void Create_ReturnsAWorkingProbe_ForThisPlatform()
		{
			var probe = PointerProbe.Create();

			Assert.That(probe, Is.Not.Null);
			// Reading the cursor must not throw on the host platform.
			Assert.DoesNotThrow(() => probe.GetCursorScreen());
			Assert.DoesNotThrow(() => probe.IsLeftButtonDown());
		}

		[Test]
		public void Shared_IsSingleton()
		{
			Assert.That(PointerProbe.Shared, Is.SameAs(PointerProbe.Shared));
		}

		[Test]
		public void Fake_ReportsConfiguredCursorAndButton()
		{
			var fake = new FakePointerProbe { Cursor = (123, 456), LeftDown = true };

			Assert.That(((IPointerProbe)fake).GetCursorScreen(), Is.EqualTo((123.0, 456.0)));
			Assert.That(((IPointerProbe)fake).IsLeftButtonDown(), Is.True);
		}
	}
}
