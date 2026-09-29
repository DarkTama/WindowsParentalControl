using System.Security.Principal;

namespace ParentalControl.Agent;

internal static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    private static void Main()
    {
        // 1. Exit immediately if running under Administrator account.
        // Admins are not monitored, have no daily limits, and use ParentalControl.Admin instead.
        try
        {
            var windowsIdentity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(windowsIdentity);
            if (principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                return;
            }
        }
        catch { }

        // 2. Prevent duplicate agent instances for the same user session.
        var mutexName = $"Global\\ParentalControlAgent_{Environment.UserName}";
        _mutex = new Mutex(true, mutexName, out var createdNew);
        if (!createdNew)
        {
            return;
        }

        // 3. Run native WPF Application message loop for smooth rendering, drag, and layered windows
        var app = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
        };

        using var controller = new AgentController(app);
        app.Run();
    }
}
