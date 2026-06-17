using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;
using Microsoft.UI.Xaml;

namespace AvalonDockTest
{
	[TestFixture]
	public class OverlayWindowBridgeTests
	{
		[Test]
		public void Host_ShowOverlayWindow_ReusesUntilHidden()
		{
			var manager = new DockingManager();
			var host = (IOverlayWindowHost)manager;

			var first = host.ShowOverlayWindow(null);
			var second = host.ShowOverlayWindow(null);

			Assert.That(second, Is.SameAs(first));

			host.HideOverlayWindow();

			var third = host.ShowOverlayWindow(null);
			Assert.That(third, Is.Not.SameAs(first));
		}

		[Test]
		public void Host_HitTestScreen_WithoutTemplateRoot_ReturnsFalse()
		{
			var manager = new DockingManager();
			var host = (IOverlayWindowHost)manager;

			var hit = host.HitTestScreen(new Windows.Foundation.Point(10, 10));

			Assert.That(hit, Is.False);
		}

		[Test]
		public void OverlayWindow_DragDrop_ForwardsFloatingModelToDropTarget()
		{
			var overlayWindow = new OverlayWindow(new TestHost());
			var overlay = (IOverlayWindow)overlayWindow;

			var floatingModel = CreateDocumentFloatingWindowModel();
			var floatingWindowControl = new LayoutDocumentFloatingWindowControl(floatingModel);
			var dropTarget = new RecordingDropTarget();

			overlay.DragEnter(floatingWindowControl);
			overlay.DragDrop(dropTarget);

			Assert.That(dropTarget.DropCallCount, Is.EqualTo(1));
			Assert.That(dropTarget.LastDroppedWindow, Is.SameAs(floatingModel));
		}

		[Test]
		public void OverlayWindow_DragDrop_WithoutFloatingWindow_DoesNotDrop()
		{
			var overlay = (IOverlayWindow)new OverlayWindow(new TestHost());
			var dropTarget = new RecordingDropTarget();

			overlay.DragDrop(dropTarget);

			Assert.That(dropTarget.DropCallCount, Is.Zero);
		}

		[Test]
		public void OverlayWindow_GetTargets_ForDockingManagerArea_ReturnsFourEdgeTargets()
		{
			var overlay = (IOverlayWindow)new OverlayWindow(new TestHost());
			overlay.DragEnter(new LayoutDocumentFloatingWindowControl(CreateDocumentFloatingWindowModel()));
			overlay.DragEnter(new TestDropArea(DropAreaType.DockingManager, new Windows.Foundation.Rect(0, 0, 300, 200)));

			var targetTypes = overlay.GetTargets().Select(t => t.Type).ToList();

			Assert.That(targetTypes.Count, Is.EqualTo(4));
			Assert.That(targetTypes, Does.Contain(DropTargetType.DockingManagerDockLeft));
			Assert.That(targetTypes, Does.Contain(DropTargetType.DockingManagerDockTop));
			Assert.That(targetTypes, Does.Contain(DropTargetType.DockingManagerDockRight));
			Assert.That(targetTypes, Does.Contain(DropTargetType.DockingManagerDockBottom));
		}

		[Test]
		public void OverlayWindow_DockingManagerOuterDrop_MutatesLayout()
		{
			var manager = CreateManagerWithSingleDocument();
			var overlay = (IOverlayWindow)new OverlayWindow(new TestHost(manager));

			var floatingModel = CreateDocumentFloatingWindowModel();
			overlay.DragEnter(new LayoutDocumentFloatingWindowControl(floatingModel));
			overlay.DragEnter(new TestDropArea(DropAreaType.DockingManager, new Windows.Foundation.Rect(0, 0, 300, 200)));

			var leftTarget = overlay.GetTargets().First(t => t.Type == DropTargetType.DockingManagerDockLeft);
			overlay.DragDrop(leftTarget);

			var allDocuments = manager.Layout.Descendents().OfType<LayoutDocument>().Select(d => d.Title).ToList();
			Assert.That(allDocuments, Does.Contain("Floating"));
			Assert.That(allDocuments, Does.Contain("Doc1"));
			Assert.That(allDocuments.Count, Is.EqualTo(2));
			Assert.That(manager.Layout.RootPanel.ChildrenCount, Is.EqualTo(2));
		}

		[Test]
		public void OverlayWindow_DocumentPaneDockAsAnchorableTop_MatchesManagerOuterTopAction()
		{
			var managerFromPaneArea = CreateManagerWithDocumentHostAndLeftPane();
			var managerFromOuterTop = CreateManagerWithDocumentHostAndLeftPane();

			var overlayWindow = new OverlayWindow(new TestHost(managerFromPaneArea));
			var applyDrop = typeof(OverlayWindow).GetMethod("ApplyDrop", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(applyDrop, Is.Not.Null);

			var floatingModelForPaneArea = CreateAnchorableFloatingWindowModel("FloatingTool");

			var paneAreaTarget = managerFromPaneArea.Layout.Descendents().OfType<LayoutDocumentPane>().First();
			var areaControl = new TestLayoutControl(paneAreaTarget);
			var dropArea = new OverlayDropArea(areaControl, DropAreaType.DocumentPane);

			var appliedFromPaneArea = (bool)applyDrop.Invoke(
				overlayWindow,
				new object[] { dropArea, DropTargetType.DocumentPaneDockAsAnchorableTop, null, floatingModelForPaneArea });
			Assert.That(appliedFromPaneArea, Is.True);

			var overlayOuter = new OverlayWindow(new TestHost(managerFromOuterTop));
			var floatingModelForOuterTop = CreateAnchorableFloatingWindowModel("FloatingTool");
			var appliedFromOuterTop = (bool)applyDrop.Invoke(
				overlayOuter,
				new object[] { new TestDropArea(DropAreaType.DockingManager, new Windows.Foundation.Rect(0, 0, 100, 100)), DropTargetType.DockingManagerDockTop, null, floatingModelForOuterTop });
			Assert.That(appliedFromOuterTop, Is.True);

			var paneAreaSignature = DescribeLayout(managerFromPaneArea.Layout.RootPanel);
			var outerTopSignature = DescribeLayout(managerFromOuterTop.Layout.RootPanel);
			Assert.That(paneAreaSignature, Is.EqualTo(outerTopSignature));
			Assert.That(paneAreaSignature, Does.Contain("FloatingTool"));
		}

		[Test]
		public void OverlayTabTargetRules_ComputesTrailingAppendArea_WhenSpaceAvailable()
		{
			var ok = OverlayTabTargetRules.TryComputeTrailingTabDropArea(
				lastTabLeft: 120,
				lastTabTop: 20,
				lastTabRight: 180,
				lastTabBottom: 40,
				paneRight: 320,
				out var areaLeft,
				out var areaTop,
				out var areaRight,
				out var areaBottom);

			Assert.That(ok, Is.True);
			Assert.That(areaLeft, Is.EqualTo(180));
			Assert.That(areaRight, Is.EqualTo(240));
			Assert.That(areaTop, Is.EqualTo(20));
			Assert.That(areaBottom, Is.EqualTo(40));
		}

		[Test]
		public void OverlayTabTargetRules_RejectsTrailingAppendArea_WhenPaneWouldOverflow()
		{
			var ok = OverlayTabTargetRules.TryComputeTrailingTabDropArea(
				lastTabLeft: 120,
				lastTabTop: 20,
				lastTabRight: 180,
				lastTabBottom: 40,
				paneRight: 230,
				out _,
				out _,
				out _,
				out _);

			Assert.That(ok, Is.False);
		}

		private static DockingManager CreateManagerWithSingleDocument()
		{
			var pane = new LayoutDocumentPane();
			pane.Children.Add(new LayoutDocument { Title = "Doc1", ContentId = "doc1" });

			var manager = new DockingManager
			{
				Layout = new LayoutRoot
				{
					RootPanel = new LayoutPanel()
				}
			};

			manager.Layout.RootPanel.Children.Add(pane);
			return manager;
		}

		private static DockingManager CreateManagerWithDocumentHostAndLeftPane()
		{
			var leftPane = new LayoutAnchorablePane();
			leftPane.Children.Add(new LayoutAnchorable { Title = "LeftTool", ContentId = "left-tool" });

			var docPane = new LayoutDocumentPane();
			docPane.Children.Add(new LayoutDocument { Title = "Doc1", ContentId = "doc1" });

			var docGroup = new LayoutDocumentPaneGroup();
			docGroup.Children.Add(docPane);

			var rootPanel = new LayoutPanel();
			rootPanel.Children.Add(leftPane);
			rootPanel.Children.Add(docGroup);

			return new DockingManager
			{
				Layout = new LayoutRoot { RootPanel = rootPanel }
			};
		}

		private static string DescribeLayout(ILayoutElement root)
		{
			if (root is LayoutDocumentPaneGroup docGroup)
				return "DocGroup(" + string.Join(",", docGroup.Children.OfType<ILayoutElement>().Select(DescribeLayout)) + ")";

			if (root is LayoutPanel panel)
				return "Panel[" + panel.Orientation + "](" + string.Join(",", panel.Children.OfType<ILayoutElement>().Select(DescribeLayout)) + ")";

			if (root is LayoutDocumentPane docPane)
				return "DocPane(" + string.Join(",", docPane.Children.Select(d => d.Title)) + ")";

			if (root is LayoutAnchorablePane anchorPane)
				return "AnchPane(" + string.Join(",", anchorPane.Children.Select(a => a.Title)) + ")";

			return root?.GetType().Name ?? "null";
		}

		private static LayoutDocumentFloatingWindow CreateDocumentFloatingWindowModel()
		{
			var pane = new LayoutDocumentPane();
			pane.Children.Add(new LayoutDocument { Title = "Floating", ContentId = "floating-doc" });

			var rootPanel = new LayoutDocumentPaneGroup();
			rootPanel.Children.Add(pane);

			return new LayoutDocumentFloatingWindow { RootPanel = rootPanel };
		}

		private static LayoutAnchorableFloatingWindow CreateAnchorableFloatingWindowModel(string title)
		{
			var pane = new LayoutAnchorablePane();
			pane.Children.Add(new LayoutAnchorable { Title = title, ContentId = title.ToLowerInvariant() });

			var rootPanel = new LayoutAnchorablePaneGroup();
			rootPanel.Children.Add(pane);

			return new LayoutAnchorableFloatingWindow { RootPanel = rootPanel };
		}

		private sealed class TestHost : IOverlayWindowHost
		{
			public TestHost(DockingManager manager = null)
			{
				Manager = manager ?? new DockingManager();
			}

			public DockingManager Manager { get; }

			public bool HitTestScreen(Windows.Foundation.Point dragPoint) => false;

			public IOverlayWindow ShowOverlayWindow(LayoutFloatingWindowControl draggingWindow) => null;

			public void HideOverlayWindow()
			{
			}

			public IEnumerable<IDropArea> GetDropAreas(LayoutFloatingWindowControl draggingWindow)
			{
				yield break;
			}
		}

		private sealed class TestDropArea : IDropArea
		{
			public TestDropArea(DropAreaType type, Windows.Foundation.Rect detectionRect)
			{
				Type = type;
				DetectionRect = detectionRect;
			}

			public Windows.Foundation.Rect DetectionRect { get; }

			public DropAreaType Type { get; }

			public Windows.Foundation.Point TransformToDeviceDPI(Windows.Foundation.Point dragPosition) => dragPosition;
		}

		private sealed class TestLayoutControl : FrameworkElement, ILayoutControl
		{
			public TestLayoutControl(ILayoutElement model)
			{
				Model = model;
			}

			public ILayoutElement Model { get; }
		}

		private sealed class RecordingDropTarget : IDropTarget
		{
			public DropTargetType Type => DropTargetType.DocumentPaneDockInside;

			public int DropCallCount { get; private set; }

			public LayoutFloatingWindow LastDroppedWindow { get; private set; }

			public System.Windows.Media.Geometry GetPreviewPath(OverlayWindow overlayWindow, LayoutFloatingWindow floatingWindowModel)
				=> null;

			public bool HitTestScreen(Windows.Foundation.Point dragPoint) => true;

			public void Drop(LayoutFloatingWindow floatingWindow)
			{
				DropCallCount++;
				LastDroppedWindow = floatingWindow;
			}

			public void DragEnter()
			{
			}

			public void DragLeave()
			{
			}
		}
	}
}
