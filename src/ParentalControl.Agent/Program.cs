namespace ParentalControl.Agent;

internal static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    private static void Main()
    {
        var mutexName = $"Global\\ParentalControlAgent_{Environment.UserName}";
        _mutex = new Mutex(true, mutexName, out var createdNew);

        if (!createdNew)
        {
            // Already running for this user
            return;
        }
        // Initialize WPF application context for WidgetWindow
        _ = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
        };

        ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.Run(new TrayApplicationContext());
    }
}
