using System.Diagnostics;
using System.Security.Principal;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;

namespace ParentalControl.Agent;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private User? _currentUser;
    private bool _isAdmin;

    public TrayApplicationContext()
    {
        ResolveCurrentUser();

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Visible = true,
            Text = "Parental Control Agent"
        };

        BuildContextMenu();

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
        var menu = new ContextMenuStrip();

        var titleItem = new ToolStripMenuItem(_currentUser != null ? $"Pengguna: {_currentUser.Username}" : "Parental Control")
        {
            Enabled = false
        };
        menu.Items.Add(titleItem);

        var requestItem = new ToolStripMenuItem("🎮 Minta Tambahan Waktu Layar...", null, (s, e) =>
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
            menu.Items.Add(new ToolStripSeparator());
            var exitItem = new ToolStripMenuItem("Keluar dari Agent", null, (s, e) =>
            {
                _notifyIcon.Visible = false;
                Application.Exit();
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

        // Track foreground app tick
        ActivityTracker.RecordTick(_currentUser.Id);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var dayOfWeek = DateTime.Now.DayOfWeek;
        var limit = ScheduleRepository.GetEffectiveLimit(_currentUser.Id, dayOfWeek);
        var usage = UsageRepository.GetUsage(_currentUser.Id, today);

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
            _notifyIcon.Text = tooltip;
        }
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
        }
        base.Dispose(disposing);
    }
}
