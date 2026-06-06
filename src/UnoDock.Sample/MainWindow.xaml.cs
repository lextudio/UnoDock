using AvalonDock.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UnoDock.Themes.VS2013;

namespace UnoDock.Sample;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        // Load the VS2013 Light theme palette into the application-level
        // resources so all elements (including the custom menu bar outside
        // DockingManager) can resolve UnoDock_VS2013_* brushes via StaticResource.
        Application.Current.Resources.MergedDictionaries.Add(
            new Vs2013LightTheme().ThemeResourceDictionary);

        this.InitializeComponent();
        Title = "UnoDock Sample — AvalonDock on Uno Platform";
        AppWindow?.Resize(new Windows.Graphics.SizeInt32 { Width = 1600, Height = 900 });
    }

    public AvalonDock.DockingManager GetDockManager() => DockManager;

    private void OnSaveLayoutClick(object sender, RoutedEventArgs e)
    {
        // Route through DockDiagnostics which accesses DockManager via App.DockManager.
        _ = DockDiagnostics.CacheContent();
        _ = DockDiagnostics.SaveLayout();
    }

    private void OnLoadLayoutClick(object sender, RoutedEventArgs e)
    {
        _ = DockDiagnostics.LoadLayout();
    }

    private void OnFloatActiveClick(object sender, RoutedEventArgs e)
    {
        // Float the currently selected document in the root document pane.
        var layout = DockManager.Layout;
        var docPane = layout?.Descendents().OfType<LayoutDocumentPane>().FirstOrDefault();
        if (docPane?.SelectedContent != null)
            DockManager.StartDraggingFloatingWindowForContent(docPane.SelectedContent);
    }

    private void OnShowCompassClick(object sender, RoutedEventArgs e)
        => DockManager.ShowOverlayForDiagnostics();
}
