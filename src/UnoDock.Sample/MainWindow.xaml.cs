using AvalonDock.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LayoutPanel = AvalonDock.Layout.LayoutPanel;
using Microsoft.UI.Xaml.Media;
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
        BuildInitialLayout();
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

    private void BuildInitialLayout()
    {
        var solutionExplorer = new LayoutAnchorable
        {
            Title = "Solution Explorer",
            ContentId = "solution-explorer",
            Content = BuildPaneText(
                "Search (Ctrl+;)",
                "UnoDock.Sample",
                "src",
                "App.xaml",
                "App.xaml.cs",
                "MainWindow.xaml",
                "MainWindow.xaml.cs")
        };

        var gitChanges = new LayoutAnchorable
        {
            Title = "Git Changes",
            ContentId = "git-changes",
            Content = BuildPaneText(
                "main up0 down0",
                "Changes (3)",
                "M Themes/Generic.xaml",
                "M Controls/LayoutDocumentPaneControl.cs",
                "M Controls/DockingManager.cs")
        };

        var properties = new LayoutAnchorable
        {
            Title = "Properties",
            ContentId = "properties",
            Content = BuildPaneText(
                "Selection",
                "MainWindow.xaml",
                "Type: Window",
                "Theme: Vs2013LightTheme",
                "Status: Loaded")
        };

        var output = new LayoutAnchorable
        {
            Title = "Output",
            ContentId = "output",
            Content = BuildPaneText(
                "Build started...",
                "Build succeeded.",
                "0 Error(s), WinUI sample enabled")
        };

        var mainWindowDoc = new LayoutDocument
        {
            Title = "MainWindow.xaml",
            ContentId = "main-window-xaml",
            Content = BuildDocumentText(
                "<Window",
                "    x:Class=\"UnoDock.Sample.MainWindow\"",
                "    xmlns:dock=\"using:AvalonDock\"",
                "    xmlns:dockLayout=\"using:AvalonDock.Layout\">",
                "    <dock:DockingManager />",
                "</Window>")
        };

        var readmeDoc = new LayoutDocument
        {
            Title = "README.md",
            ContentId = "readme",
            Content = BuildPaneText(
                "UnoDock",
                "A faithful source port of AvalonDock.",
                "WinUI 3 build path is now enabled.",
                "This sample layout is created in code-behind for WinUI.")
        };

        var leftPane = new LayoutAnchorablePane();
        leftPane.Children.Add(solutionExplorer);
        leftPane.Children.Add(gitChanges);

        var documentPane = new LayoutDocumentPane();
        documentPane.Children.Add(mainWindowDoc);
        documentPane.Children.Add(readmeDoc);

        var rightPane = new LayoutAnchorablePane();
        rightPane.Children.Add(properties);
        rightPane.Children.Add(output);

        DockManager.Layout = new LayoutRoot
        {
            RootPanel = new LayoutPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Children =
                {
                    leftPane,
                    documentPane,
                    rightPane
                }
            }
        };
    }

    private static UIElement BuildPaneText(params string[] lines)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(8),
            Spacing = 4
        };

        foreach (var line in lines)
        {
            panel.Children.Add(new TextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F)),
                FontSize = 12
            });
        }

        return new ScrollViewer
        {
            Content = panel
        };
    }

    private static UIElement BuildDocumentText(params string[] lines)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 2
        };

        foreach (var line in lines)
        {
            panel.Children.Add(new TextBlock
            {
                Text = line,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F))
            });
        }

        return new ScrollViewer
        {
            Content = panel
        };
    }
}
