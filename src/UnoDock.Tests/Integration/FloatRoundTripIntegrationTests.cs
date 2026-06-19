using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace AvalonDockTest.Integration
{
	// Live counterpart of the headless FloatContentTests / DropToLayoutMutationTests:
	// drives the running sample over DevFlow and asserts on the structured
	// dock-query-layout result (not screenshots). This is the integration tier that
	// verifies the actuation the headless tests structurally cannot — real controls,
	// real floating windows (Challenge 3 via /ui/tree-style structured query).
	//
	// Opt-in: set DEVFLOW_TEST_PORT to a running sample's agent port. Without it (the
	// default CI/headless run) every test is Ignored, so the suite stays green.
	[TestFixture]
	[Category("Integration")]
	public class FloatRoundTripIntegrationTests
	{
		private DevFlowClient _client;

		[SetUp]
		public async Task SetUp()
		{
			var port = DevFlowClient.ResolvePortOrNull();
			if (port == null)
				Assert.Ignore("Set DEVFLOW_TEST_PORT to a running UnoDock.Sample agent port to run integration tests.");

			_client = new DevFlowClient(port.Value);
			if (!await _client.IsReachableAsync())
				Assert.Ignore($"No DevFlow agent reachable on port {port.Value}.");

			// The fixture shares one live app, so re-dock any windows a prior test left
			// floating. This makes every test order-independent (the cumulative-state trap
			// that makes integration suites flaky).
			await ReDockAllFloatingAsync();
		}

		// Drop floating windows back into the manager center until none remain.
		// dock-simulate-drop always targets the first floating window, so repeating
		// drains them all.
		private async Task ReDockAllFloatingAsync()
		{
			for (var i = 0; i < 10; i++)
			{
				var snap = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
				if (snap.FloatingWindows.Count == 0)
					return;
				await _client.InvokeAsync("dock-simulate-drop", "Center");
			}
		}

		[TearDown]
		public void TearDown() => _client?.Dispose();

		[Test]
		public async Task QueryLayout_ReturnsParseableSnapshot()
		{
			var json = await _client.InvokeAsync("dock-query-layout");
			var snap = DockLayoutSnapshot.Parse(json);

			// The sample always starts with at least one document pane holding tabs.
			Assert.That(snap.DocumentPanes, Is.Not.Empty);
			Assert.That(snap.DocumentPanes.SelectMany(p => p.Tabs), Is.Not.Empty);
		}

		[Test]
		public async Task FloatAnchorable_MovesItIntoAFloatingWindow()
		{
			var before = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
			var toolId = before.AnchorablePanes
				.SelectMany(p => p.Tabs)
				.FirstOrDefault();
			if (toolId == null)
				Assert.Ignore("Sample has no anchorable to float.");

			Assert.That(before.FloatingWindows.SelectMany(f => f.Contents), Does.Not.Contain(toolId));

			await _client.InvokeAsync("dock-float-anchorable", toolId);

			var after = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
			Assert.That(after.FloatingWindows.SelectMany(f => f.Contents), Does.Contain(toolId),
				"floated anchorable should appear in a floating window's contents");
			Assert.That(after.AnchorablePanes.SelectMany(p => p.Tabs), Does.Not.Contain(toolId),
				"floated anchorable should no longer be docked in an anchorable pane");
		}

		[Test]
		public async Task FloatDocument_ThenDropCenter_DocksBackIntoADocumentPane()
		{
			var before = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
			var docId = before.DocumentPanes.SelectMany(p => p.Tabs).FirstOrDefault();
			if (docId == null)
				Assert.Ignore("Sample has no document to float.");

			// Float the active document, confirm it tore out into a floating window.
			await _client.InvokeAsync("dock-float-active");
			var floated = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
			Assert.That(floated.FloatingWindows.SelectMany(f => f.Contents), Is.Not.Empty,
				"floating the active document should create a floating window");
			var floatedId = floated.FloatingWindows.SelectMany(f => f.Contents).First();

			// Drop it back into the center of the manager → re-docks into a document pane.
			await _client.InvokeAsync("dock-simulate-drop", "Center");

			var after = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
			Assert.That(after.FloatingWindows.SelectMany(f => f.Contents), Does.Not.Contain(floatedId),
				"dropped document should no longer be in a floating window");
			Assert.That(after.DocumentPanes.SelectMany(p => p.Tabs), Does.Contain(floatedId),
				"dropped document should be docked back into a document pane");
		}

		[Test]
		public async Task FloatDocument_ThenDropLeft_SplitsIntoTwoDocumentPanes()
		{
			var before = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
			if (before.DocumentPanes.SelectMany(p => p.Tabs).Count() < 2)
				Assert.Ignore("Need at least two documents to demonstrate a split.");
			var paneCountBefore = before.DocumentPanes.Count;

			await _client.InvokeAsync("dock-float-active");
			await _client.InvokeAsync("dock-simulate-drop", "Left");

			var after = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
			Assert.That(after.FloatingWindows, Is.Empty, "the floated document should have re-docked");
			Assert.That(after.DocumentPanes.Count, Is.GreaterThan(paneCountBefore),
				"a left drop should split the documents into separate panes");
		}

		[Test]
		public async Task ToggleAutoHide_MovesToolOutOfDockedPane_AndBack()
		{
			var before = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
			var toolId = before.AnchorablePanes.SelectMany(p => p.Tabs).FirstOrDefault();
			if (toolId == null)
				Assert.Ignore("Sample has no docked anchorable to auto-hide.");

			// Auto-hide → tool moves to a side panel, leaving the docked anchorable panes.
			await _client.InvokeAsync("dock-toggle-autohide", toolId);
			var hidden = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
			Assert.That(hidden.AnchorablePanes.SelectMany(p => p.Tabs), Does.Not.Contain(toolId),
				"auto-hidden tool should leave the docked anchorable panes");

			// Toggle back → returns to a docked anchorable pane.
			await _client.InvokeAsync("dock-toggle-autohide", toolId);
			var restored = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
			Assert.That(restored.AnchorablePanes.SelectMany(p => p.Tabs), Does.Contain(toolId),
				"toggling auto-hide off should re-dock the tool");
		}
	}
}
