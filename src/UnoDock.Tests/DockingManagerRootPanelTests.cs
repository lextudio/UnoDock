using System.Reflection;
using NUnit.Framework;
using AvalonDock;
using AvalonDock.Layout;

namespace AvalonDockTest
{
	[TestFixture]
	public class DockingManagerRootPanelTests
	{
		private static readonly MethodInfo RebuildLayoutControlsMethod =
			typeof(DockingManager).GetMethod("RebuildLayoutControls", BindingFlags.Instance | BindingFlags.NonPublic);
		private static readonly FieldInfo LayoutPanelControlModelField =
			typeof(AvalonDock.Controls.LayoutPanelControl).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic);

		[Test]
		public void RootPanelChange_RebuildsLayoutRootPanelVisual()
		{
			var manager = new DockingManager();
			var layout = manager.Layout;
			var originalRootPanel = layout.RootPanel;

			RebuildLayoutControlsMethod.Invoke(manager, new object[] { layout });

			Assert.That(manager.LayoutRootPanel, Is.Not.Null);
			Assert.That(LayoutPanelControlModelField.GetValue(manager.LayoutRootPanel), Is.SameAs(originalRootPanel));

			var replacementRootPanel = new LayoutPanel
			{
				Orientation = System.Windows.Controls.Orientation.Vertical
			};
			replacementRootPanel.Children.Add(new LayoutDocumentPaneGroup(new LayoutDocumentPane()));
			replacementRootPanel.Children.Add(new LayoutDocumentPaneGroup(new LayoutDocumentPane()));

			layout.RootPanel = replacementRootPanel;

			Assert.That(manager.LayoutRootPanel, Is.Not.Null);
			Assert.That(LayoutPanelControlModelField.GetValue(manager.LayoutRootPanel), Is.SameAs(replacementRootPanel));
		}
	}
}