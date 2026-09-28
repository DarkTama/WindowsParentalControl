using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ParentalControl.Core.Data;

namespace ParentalControl.Agent;

public static class ActivityTracker
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    public static (string ProcessName, string Title) SampleForegroundWindow()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return ("Unknown", "");

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return ("Unknown", "");

            string processName = "Unknown";
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
            }
            catch { }

            var sb = new StringBuilder(512);
            GetWindowTextW(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            return (processName, title);
        }
        catch
        {
            return ("Unknown", "");
        }
    }

    public static void RecordTick(int userId)
    {
        var (process, title) = SampleForegroundWindow();
        if (process == "Unknown" || string.IsNullOrWhiteSpace(process)) return;

        // Skip explorer / shell components from clogging top activity
        if (process.Equals("explorer", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(title))
            return;

        var today = DateOnly.FromDateTime(DateTime.Now);
        Task.Run(() =>
        {
            try
            {
                AppUsageRepository.AddMinutes(userId, today, process, title, 1);
            }
            catch { }
        });
    }
}
