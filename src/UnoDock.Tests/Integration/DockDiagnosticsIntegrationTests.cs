using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace AvalonDockTest.Integration
{
	// Extends FloatRoundTripIntegrationTests' coverage to match the AvalonDock
	// DevFlowIntegrationTests suite: agent/action discovery, error-path handling for
	// invalid ContentIds, hide/show round-trips, and layout serialization round-trips.
	// Uses the same auto-launching DevFlowAppFixture and re-dock-before-each-test pattern.
	[TestFixture]
	[Category("Integration")]
	public class DockDiagnosticsIntegrationTests
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

			await ReDockAllFloatingAsync();
			await ShowAllHiddenAsync();
		}

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

		private async Task ShowAllHiddenAsync()
		{
			for (var i = 0; i < 10; i++)
			{
				var snap = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
				if (snap.Hidden.Count == 0)
					return;
				await _client.InvokeAsync("dock-show-hidden");
			}
		}

		[TearDown]
		public void TearDown() => _client?.Dispose();

		// --- Agent/action discovery (mirrors DevFlowAgentIntegrationTests) ---

		[Test]
		public async Task AgentStatus_ReportsUnoFramework()
		{
			var status = await _client.GetStatusAsync();
			Assert.That(status.TryGetProperty("framework", out var framework), Is.True,
				"agent status should report a 'framework' field");
			Assert.That(framework.GetString(), Is.EqualTo("uno"));
		}

		[Test]
		public async Task InvokeActions_ExposeDockingManagerActions()
		{
			var actions = await _client.ListActionsAsync();
			Assert.That(actions, Is.Not.Empty, "action list should be non-empty");
			Assert.That(actions, Does.Contain("dock-query-layout"));
			Assert.That(actions, Does.Contain("dock-simulate-drop"));
			Assert.That(actions, Does.Contain("dock-toggle-autohide"));
		}

		[Test]
		public async Task UITree_ReturnsNonEmpty()
		{
			var tree = await _client.GetTreeAsync();
			Assert.That(tree, Is.Not.Null.And.Not.Empty);
		}

		// --- Error paths for invalid ContentIds (mirrors DragDropFeatureIntegrationTests) ---

		[Test]
		public async Task FloatAnchorable_UnknownContentId_ReportsNotFound()
		{
			var result = await _client.InvokeAsync("dock-float-anchorable", "does-not-exist");
			Assert.That(result, Does.Contain("not found"));
		}

		[Test]
		public async Task HideAnchorable_UnknownContentId_ReportsNotFound()
		{
			var result = await _client.InvokeAsync("dock-hide-anchorable", "does-not-exist");
			Assert.That(result, Does.Contain("not found"));
		}

		[Test]
		public async Task OpenFlyout_UnknownContentId_ReportsNotFoundOrNotAutoHidden()
		{
			var result = await _client.InvokeAsync("dock-open-flyout", "does-not-exist");
			Assert.That(result, Does.Contain("not found"));
		}

		// --- Hide/show round trip (mirrors AvalonDockLayoutIntegrationTests.HideAndShowAnchorable) ---

		[Test]
		public async Task HideAndShowAnchorable_RoundTripsThroughLayoutModel()
		{
			var before = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
			var toolId = before.AnchorablePanes.SelectMany(p => p.Tabs).FirstOrDefault();
			if (toolId == null)
				Assert.Ignore("Sample has no anchorable to hide.");

			await _client.InvokeAsync("dock-hide-anchorable", toolId);
			var hiddenList = await _client.InvokeAsync("dock-list-hidden");
			Assert.That(hiddenList, Does.Contain(toolId), "dock-list-hidden should report the hidden ContentId");

			var afterHide = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
			Assert.That(afterHide.Hidden, Does.Contain(toolId));
			Assert.That(afterHide.AnchorablePanes.SelectMany(p => p.Tabs), Does.Not.Contain(toolId));

			await _client.InvokeAsync("dock-show-hidden");
			var afterShow = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
			Assert.That(afterShow.Hidden, Does.Not.Contain(toolId));
			Assert.That(afterShow.AnchorablePanes.SelectMany(p => p.Tabs), Does.Contain(toolId),
				"showing the hidden anchorable should restore it to a docked anchorable pane");
		}

		// --- Layout serialization round trip (mirrors AvalonDockLayoutIntegrationTests.AddDocuments_CanBeRestoredFromSerializedLayout) ---

		[Test]
		public async Task SaveAndLoadLayout_RoundTripsDocumentAndAnchorableCounts()
		{
			var before = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));

			await _client.InvokeAsync("dock-cache-content");
			var saveResult = await _client.InvokeAsync("dock-save-layout");
			Assert.That(saveResult, Does.Contain("saved"), "dock-save-layout should report bytes written");

			var loadResult = await _client.InvokeAsync("dock-load-layout");
			Assert.That(loadResult, Does.Not.Contain("file not found"));

			var after = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
			Assert.That(after.DocumentPanes.SelectMany(p => p.Tabs).Count(),
				Is.EqualTo(before.DocumentPanes.SelectMany(p => p.Tabs).Count()),
				"reloading the just-saved layout should preserve the document count");
			Assert.That(after.AnchorablePanes.SelectMany(p => p.Tabs).Count(),
				Is.EqualTo(before.AnchorablePanes.SelectMany(p => p.Tabs).Count()),
				"reloading the just-saved layout should preserve the anchorable count");
		}

		// --- Active content / tab selection (no AvalonDock equivalent, but same DevFlow actions) ---

		// dock-select-anchorable looks up by Title, but dock-query-layout reports ContentId
		// (LayoutContent.ContentId ?? Title). The sample's built-in tool panes use distinct
		// ContentId/Title pairs (MainWindow.xaml.cs BuildInitialLayout), so translate here.
		private static readonly System.Collections.Generic.Dictionary<string, string> ContentIdToTitle = new()
		{
			["solution-explorer"] = "Solution Explorer",
			["git-changes"] = "Git Changes",
			["properties"] = "Properties",
			["output"] = "Output",
		};

		[Test]
		public async Task SelectAnchorable_ChangesActiveContent()
		{
			var before = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
			var contentId = before.AnchorablePanes.SelectMany(p => p.Tabs)
				.FirstOrDefault(id => ContentIdToTitle.ContainsKey(id));
			if (contentId == null)
				Assert.Ignore("Sample has no known anchorable to select.");

			var result = await _client.InvokeAsync("dock-select-anchorable", ContentIdToTitle[contentId]);
			Assert.That(result, Does.Not.Contain("not found"));
		}
	}
}
