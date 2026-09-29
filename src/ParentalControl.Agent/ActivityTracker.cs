using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;

namespace ParentalControl.Agent;

public static class ActivityTracker
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(3) };

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public static (string ProcessName, string Title) SampleForegroundWindow()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return ("Unknown", "");

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return ("Unknown", "");

            string processName = "Unknown";
            var hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    var sbExe = new StringBuilder(1024);
                    int size = sbExe.Capacity;
                    if (QueryFullProcessImageNameW(hProcess, 0, sbExe, ref size))
                    {
                        processName = Path.GetFileNameWithoutExtension(sbExe.ToString());
                    }
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }

            if (processName == "Unknown")
            {
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    processName = proc.ProcessName;
                }
                catch { }
            }

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

    public static async Task ReportActivityAsync(string username)
    {
        try
        {
            var (process, title) = SampleForegroundWindow();
            if (process == "Unknown" || string.IsNullOrWhiteSpace(process)) return;

            // Skip bare explorer / shell desktop clicks
            if (process.Equals("explorer", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(title))
                return;

            var payload = new
            {
                username,
                processName = process,
                windowTitle = title
            };

            await _httpClient.PostAsJsonAsync("http://127.0.0.1:5050/api/agent/activity", payload);
        }
        catch
        {
            // Service not running or network busy; drop tick silently
        }
    }
}
