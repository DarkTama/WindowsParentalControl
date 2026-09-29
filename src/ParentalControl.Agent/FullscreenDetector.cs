using System.Runtime.InteropServices;
using System.Text;

namespace ParentalControl.Agent;

public static class FullscreenDetector
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    public static bool IsForegroundFullscreen(out RECT monitorRect, out RECT windowRect)
    {
        monitorRect = default;
        windowRect = default;

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        // Filter out Windows Shell elements (desktop, taskbar, start menu)
        var sbClass = new StringBuilder(256);
        GetClassNameW(hwnd, sbClass, sbClass.Capacity);
        var className = sbClass.ToString();

        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Windows.UI.Core.CoreWindow")
        {
            return false;
        }

        if (!GetWindowRect(hwnd, out windowRect))
        {
            return false;
        }

        var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero) return false;

        var mi = new MONITORINFO();
        mi.cbSize = Marshal.SizeOf<MONITORINFO>();
        if (!GetMonitorInfoW(hMonitor, ref mi))
        {
            return false;
        }

        monitorRect = mi.rcMonitor;

        // Check if window bounds cover or exceed monitor rectangle
        bool coversMonitor = windowRect.Left <= mi.rcMonitor.Left &&
                             windowRect.Top <= mi.rcMonitor.Top &&
                             windowRect.Right >= mi.rcMonitor.Right &&
                             windowRect.Bottom >= mi.rcMonitor.Bottom;

        return coversMonitor;
    }
}
