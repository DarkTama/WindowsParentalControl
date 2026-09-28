using System.Diagnostics;
using System.Security.Principal;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;

namespace ParentalControl.Agent;

public sealed class TrayApplicationContext : System.Windows.Forms.ApplicationContext
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private User? _currentUser;
    private bool _isAdmin;
    private WidgetWindow? _widgetWindow;

    public TrayApplicationContext()
    {
        ResolveCurrentUser();

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Visible = true,
            Text = "Parental Control Agent"
        };

        BuildContextMenu();

        // Show WPF floating countdown widget on startup
        try
        {
            _widgetWindow = new WidgetWindow(_currentUser, _isAdmin);
            _widgetWindow.Show();
        }
        catch { }

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 30000 // 30 seconds
        };
        _timer.Tick += (s, e) => UpdateStatus();
        _timer.Start();

        UpdateStatus();
    }

    private void ResolveCurrentUser()
    {
        try
        {
            var windowsIdentity = WindowsIdentity.GetCurrent();
            var sid = windowsIdentity.User?.Value;
            var principal = new WindowsPrincipal(windowsIdentity);
            _isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);

            if (!string.IsNullOrEmpty(sid))
            {
                _currentUser = UserRepository.GetBySid(sid);
            }
        }
        catch { }
    }

    private void BuildContextMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();

        var titleItem = new System.Windows.Forms.ToolStripMenuItem(_currentUser != null ? $"Pengguna: {_currentUser.Username}" : "Parental Control")
        {
            Enabled = false
        };
        menu.Items.Add(titleItem);

        var toggleWidgetItem = new System.Windows.Forms.ToolStripMenuItem("⏱ Tampilkan / Sembunyikan Widget", null, (s, e) =>
        {
            if (_widgetWindow != null)
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
            }
        });
        menu.Items.Add(toggleWidgetItem);

        var requestItem = new System.Windows.Forms.ToolStripMenuItem("🎮 Minta Tambahan Waktu Layar...", null, (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("http://localhost:5050/request") { UseShellExecute = true });
            }
            catch { }
        });
        menu.Items.Add(requestItem);

        if (_isAdmin)
        {
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            var exitItem = new System.Windows.Forms.ToolStripMenuItem("Keluar dari Agent", null, (s, e) =>
            {
                _notifyIcon.Visible = false;
                _widgetWindow?.Close();
                System.Windows.Forms.Application.Exit();
            });
            menu.Items.Add(exitItem);
        }

        _notifyIcon.ContextMenuStrip = menu;
    }

    private void UpdateStatus()
    {
        if (_currentUser == null)
        {
            ResolveCurrentUser();
            if (_currentUser == null) return;
        }

        if (!_currentUser.IsRestricted)
        {
            _notifyIcon.Text = "Parental Control: Akun Tidak Dibatasi";
            return;
        }

        // Track foreground app tick asynchronously
        ActivityTracker.RecordTick(_currentUser.Id);

        var userId = _currentUser.Id;
        Task.Run(() =>
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                var dayOfWeek = DateTime.Now.DayOfWeek;
                var limit = ScheduleRepository.GetEffectiveLimit(userId, dayOfWeek);
                var usage = UsageRepository.GetUsage(userId, today);

                if (limit != null)
                {
                    var totalAllowed = limit.DailyMinutes + (usage?.BonusMinutes ?? 0);
                    var remaining = Math.Max(0, totalAllowed - (usage?.MinutesUsed ?? 0));
                    var hours = remaining / 60;
                    var mins = remaining % 60;
                    var text = hours > 0 ? $"Sisa {hours} jam {mins} mnt" : $"Sisa {mins} mnt";

                    // NotifyIcon Text limit is 63 chars
                    var tooltip = $"Parental Control: {text}".Trim();
                    if (tooltip.Length > 63) tooltip = tooltip.Substring(0, 63);

                    // Update tooltip on UI thread
                    _notifyIcon.Text = tooltip;
                }
            }
            catch { }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Stop();
            _timer?.Dispose();
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            if (_widgetWindow != null)
            {
                try
                {
                    _widgetWindow.Close();
                }
                catch { }
            }
        }
        base.Dispose(disposing);
    }
}
