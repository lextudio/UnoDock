using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace AvalonDockTest.Integration
{
	// Counterpart to AvalonDock's DropTargetZoneIntegrationTests (18 real-drag zone cases):
	// exercises every CompassDropZone (AvalonDock.Layout.LayoutRootMutations) that
	// dock-simulate-drop can reach, for both document and anchorable floating content.
	//
	// UnoDock's zone model is coarser than AvalonDock's DropTargetType (9 zones vs 19):
	// dock-simulate-drop is a model-mutation shortcut (LayoutRootMutations.InsertPane) that
	// doesn't distinguish "document pane" vs "docking-manager edge" vs "as-anchorable"
	// indicators the way the real overlay does — it only knows content kind (document vs
	// anchorable) x zone. See LayoutRootMutations.InsertPane for the exact branching this
	// test matrix is built from. It does NOT exercise the real pointer/hit-test/overlay
	// pipeline (that gap is tracked separately — dock-simulate-drop bypasses it entirely).
	[TestFixture]
	[Category("Integration")]
	public class DropZoneIntegrationTests
	{
		private DevFlowClient _client;

		private const string AnchorableContentId = "solution-explorer";

		public enum ContentKind { Document, Anchorable }

		private static readonly string[] InnerZones = { "Center", "Left", "Right", "Top", "Bottom" };
		private static readonly string[] OuterZones = { "OuterLeft", "OuterRight", "OuterTop", "OuterBottom" };

		private static readonly object[] DocumentCases =
			InnerZones.Concat(OuterZones).Select(z => (object)new object[] { ContentKind.Document, z }).ToArray();

		private static readonly object[] AnchorableCases =
			InnerZones.Concat(OuterZones).Select(z => (object)new object[] { ContentKind.Anchorable, z }).ToArray();

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

		[TestCaseSource(nameof(DocumentCases))]
		public Task DragFloatingDocument_OntoZone_DocksThere(ContentKind kind, string zone)
			=> DragFloatingContent_OntoZone_DocksThere(kind, zone);

		[TestCaseSource(nameof(AnchorableCases))]
		public Task DragFloatingAnchorable_OntoZone_DocksThere(ContentKind kind, string zone)
			=> DragFloatingContent_OntoZone_DocksThere(kind, zone);

		private async Task DragFloatingContent_OntoZone_DocksThere(ContentKind kind, string zone)
		{
			string floatedId;
			if (kind == ContentKind.Document)
			{
				var before = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
				if (before.DocumentPanes.SelectMany(p => p.Tabs).FirstOrDefault() == null)
					Assert.Ignore("Sample has no document to float.");

				await _client.InvokeAsync("dock-float-active");
				var floated = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
				Assert.That(floated.FloatingWindows.SelectMany(f => f.Contents), Is.Not.Empty,
					"floating the active document should create a floating window");
				floatedId = floated.FloatingWindows.SelectMany(f => f.Contents).First();
			}
			else
			{
				await _client.InvokeAsync("dock-float-anchorable", AnchorableContentId);
				var floated = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
				Assert.That(floated.FloatingWindows.SelectMany(f => f.Contents), Does.Contain(AnchorableContentId),
					$"floating anchorable '{AnchorableContentId}' should create a floating window");
				floatedId = AnchorableContentId;
			}

			await _client.InvokeAsync("dock-simulate-drop", zone);

			var after = DockLayoutSnapshot.Parse(await _client.InvokeAsync("dock-query-layout"));
			Assert.That(after.FloatingWindows.SelectMany(f => f.Contents), Does.Not.Contain(floatedId),
				$"dropping onto {zone} should redock — content should leave the floating window");

			var targetPanes = kind == ContentKind.Document ? after.DocumentPanes : after.AnchorablePanes;
			Assert.That(targetPanes.SelectMany(p => p.Tabs), Does.Contain(floatedId),
				$"dropping onto {zone} should place the content into a {kind} pane");
		}
	}
}
