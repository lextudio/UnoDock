using LeXtudio.DevFlow.Agent.Uno;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.UI.Xaml;

namespace UnoDock.Sample;

public partial class App : Application
{
    private UnoAgentService? _agent;

    public Window? MainWindow { get; private set; }
    public AvalonDock.DockingManager? DockManager
        => (MainWindow as MainWindow)?.GetDockManager();

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Uncomment to enable UnoDock debug logging:
        // AvalonDock.DockLog.Enable();
        var window = new MainWindow();
        MainWindow = window;
        // Closing the main window should terminate the whole app — close all
        // floating child windows first, then exit.
        window.Closed += (_, _) =>
        {
            DockManager?.CloseAllFloatingWindows();
            Environment.Exit(0);
        };
        window.Activate();

        _agent = new UnoAgentService(new AgentOptions
        {
            Port = GetAgentPort()
        });
        _agent.Start();
    }

    private static int GetAgentPort()
    {
        var portValue = Environment.GetEnvironmentVariable("DEVFLOW_AGENT_PORT");
        if (int.TryParse(portValue, out var parsedPort) && parsedPort > 0)
        {
            return parsedPort;
        }

        return DevFlowAgentPortResolver.GetPortFromAssemblyMetadata() ?? AgentOptions.DefaultPort;
    }
}
