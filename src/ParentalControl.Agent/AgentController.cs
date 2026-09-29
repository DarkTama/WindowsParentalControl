using System.Net.Http;
using System.Diagnostics;
using System.Drawing;
using System.Net.Http.Json;
using System.Windows.Threading;

namespace ParentalControl.Agent;

public sealed record AgentStatusResponse(
    string? username,
    bool isRestricted,
    int remainingSeconds,
    string? curfew,
    bool isLocked,
    bool captureRequested = false,
    int watchIntervalSeconds = 0,
    bool isCurfewClamped = false,
    int? lastDeclinedId = null,
    string? lastDeclinedReason = null,
    DateTime? lastDeclinedTime = null);

public sealed class AgentController : IDisposable
{
    private readonly System.Windows.Application _app;
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _activityTimer;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly WidgetWindow _widgetWindow;
    private DispatcherTimer? _watchTimer;
    private int _currentWatchInterval;
    private bool _disposed;
    private int? _lastShownDeclinedId;

    public AgentController(System.Windows.Application app)
    {
        _app = app;
        _widgetWindow = new WidgetWindow();

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Visible = true,
            Text = "Parental Control Agent"
        };

        BuildContextMenu();

        // 1. Status Polling Timer (every 5 seconds)
        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _statusTimer.Tick += async (s, e) => await PollStatusAsync();
        _statusTimer.Start();

        // 2. Foreground Activity Reporting Timer (every 20 seconds)
        _activityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        _activityTimer.Tick += async (s, e) => await ActivityTracker.ReportActivityAsync(Environment.UserName);
        _activityTimer.Start();

        // Initial immediate poll
        _ = PollStatusAsync();
    }

    private void BuildContextMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();

        var titleItem = new System.Windows.Forms.ToolStripMenuItem($"Pengguna: {Environment.UserName}")
        {
            Enabled = false
        };
        menu.Items.Add(titleItem);

        var toggleWidgetItem = new System.Windows.Forms.ToolStripMenuItem("⏱ Tampilkan / Sembunyikan Widget", null, (s, e) =>
        {
            if (_widgetWindow.Visibility == System.Windows.Visibility.Visible)
            {
                _widgetWindow.Hide();
            }
            else
            {
                _widgetWindow.Show();
                _widgetWindow.Activate();
            }
        });
        menu.Items.Add(toggleWidgetItem);
        var settingsItem = new System.Windows.Forms.ToolStripMenuItem("⚙️ Pengaturan Layar Penuh", null, (s, e) =>
        {
            _widgetWindow.Dispatcher.Invoke(() => _widgetWindow.OpenSettings());
        });
        menu.Items.Add(settingsItem);

        var requestItem = new System.Windows.Forms.ToolStripMenuItem("🎮 Minta Tambahan Waktu Layar...", null, (s, e) =>
        {
            try
            {
                var username = Uri.EscapeDataString(Environment.UserName);
                Process.Start(new ProcessStartInfo($"http://localhost:5050/request?user={username}") { UseShellExecute = true });
            }
            catch { }
        });
        menu.Items.Add(requestItem);

        _notifyIcon.ContextMenuStrip = menu;
    }

    private async Task PollStatusAsync()
    {
        try
        {
            var username = Uri.EscapeDataString(Environment.UserName);
            var status = await _httpClient.GetFromJsonAsync<AgentStatusResponse>(
                $"http://127.0.0.1:5050/api/agent/status?user={username}");

            if (status == null) return;

            if (!status.isRestricted)
            {
                if (_widgetWindow.Visibility == System.Windows.Visibility.Visible)
                {
                    _widgetWindow.Hide();
                }
                _notifyIcon.Text = "Parental Control: Akun Tidak Dibatasi";
                return;
            }

            // User is restricted: show widget if not shown
            if (_widgetWindow.Visibility != System.Windows.Visibility.Visible)
            {
                _widgetWindow.Show();
            }

            _widgetWindow.UpdateData(
                status.remainingSeconds,
                status.curfew ?? "Jam malam: --:--",
                status.isLocked,
                status.username ?? Environment.UserName,
                status.isCurfewClamped);

            if (status.lastDeclinedId != null && status.lastDeclinedId != _lastShownDeclinedId)
            {
                _lastShownDeclinedId = status.lastDeclinedId;
                _widgetWindow.ShowDeclineToast(
                    status.lastDeclinedReason ?? "Permintaan ditolak oleh orang tua.",
                    status.lastDeclinedTime ?? DateTime.Now);
            }

            // Update NotifyIcon tooltip
            string tooltip;
            if (status.isLocked)
            {
                tooltip = "Parental Control: Sesi Dikunci (Dijeda)";
            }
            else
            {
                var hours = status.remainingSeconds / 3600;
                var mins = (status.remainingSeconds % 3600) / 60;
                var timeText = hours > 0 ? $"Sisa {hours}j {mins}m" : $"Sisa {mins}m";
                tooltip = $"Parental Control: {timeText}";
            }

            if (tooltip.Length > 63) tooltip = tooltip.Substring(0, 63);
            _notifyIcon.Text = tooltip;
            // Check for capture command from service
            if (status.captureRequested)
            {
                _ = CaptureAndUploadAsync("MANUAL");
            }

            // Manage Watch Mode timer
            if (status.watchIntervalSeconds > 0)
            {
                if (_watchTimer == null || _currentWatchInterval != status.watchIntervalSeconds)
                {
                    _watchTimer?.Stop();
                    _currentWatchInterval = status.watchIntervalSeconds;
                    _watchTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(_currentWatchInterval)
                    };
                    _watchTimer.Tick += async (s, e) => await CaptureAndUploadAsync("WATCH");
                    _watchTimer.Start();
                }
            }
            else if (_watchTimer != null)
            {
                _watchTimer.Stop();
                _watchTimer = null;
                _currentWatchInterval = 0;
            }
        }
        catch
        {
            // If service temporarily down or restarting, do not crash or block
        }
    }

    private async Task CaptureAndUploadAsync(string trigger)
    {
        try
        {
            var bytes = ScreenCaptureHelper.CaptureScreenJpeg(1920, 70L, out var width, out var height);
            if (bytes == null || bytes.Length == 0) return;

            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Headers.Add("X-User", Environment.UserName);
            content.Headers.Add("X-Trigger", trigger);
            content.Headers.Add("X-Width", width.ToString());
            content.Headers.Add("X-Height", height.ToString());

            await _httpClient.PostAsync("http://127.0.0.1:5050/api/agent/capture", content);
        }
        catch
        {
            // Silent capture failure; do not alert or disturb restricted user
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _statusTimer?.Stop();
        _activityTimer?.Stop();
        _watchTimer?.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _httpClient.Dispose();
        try
        {
            _widgetWindow?.Close();
        }
        catch { }
    }
}
