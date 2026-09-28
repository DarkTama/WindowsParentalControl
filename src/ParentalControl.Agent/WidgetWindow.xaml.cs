using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;

namespace ParentalControl.Agent;

public partial class WidgetWindow : Window
{
    private readonly User? _user;
    private readonly bool _isAdmin;
    private readonly DispatcherTimer _secondTimer;
    private readonly DispatcherTimer _syncTimer;
    private int _remainingSeconds;
    public bool IsCollapsed => _isCollapsed;
    private bool _isCollapsed;

    public WidgetWindow(User? user, bool isAdmin)
    {
        InitializeComponent();
        _user = user;
        _isAdmin = isAdmin;

        UserTitleText.Text = _user != null ? _user.Username : Environment.UserName;
        CloseBtn.Visibility = _isAdmin ? Visibility.Visible : Visibility.Collapsed;

        // Position at top-right of work area
        Left = SystemParameters.WorkArea.Right - 270;
        Top = 40;

        _secondTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _secondTimer.Tick += (s, e) => TickSecond();

        _syncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        _syncTimer.Tick += async (s, e) => await SyncRemainingTimeAsync();

        Loaded += async (s, e) =>
        {
            await SyncRemainingTimeAsync();
            _secondTimer.Start();
            _syncTimer.Start();
        };
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private async Task SyncRemainingTimeAsync()
    {
        if (_user == null || !_user.IsRestricted)
        {
            CountdownText.Text = "Unlimited";
            MiniCountdownText.Text = "Unlimited";
            CurfewText.Text = "Akun tidak dibatasi";
            return;
        }

        var (remSec, curfewStr) = await Task.Run(() =>
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                var dayOfWeek = DateTime.Now.DayOfWeek;
                var limit = ScheduleRepository.GetEffectiveLimit(_user.Id, dayOfWeek);
                var usage = UsageRepository.GetUsage(_user.Id, today);

                if (limit == null) return (0, "Tanpa batas");

                var totalAllowedMinutes = limit.DailyMinutes + (usage?.BonusMinutes ?? 0);
                var usedMinutes = usage?.MinutesUsed ?? 0;
                var remMin = Math.Max(0, totalAllowedMinutes - usedMinutes);
                var curfew = $"Jam malam: {limit.ScheduleStart:HH:mm} – {limit.ScheduleEnd:HH:mm}";

                return (remMin * 60, curfew);
            }
            catch
            {
                return (0, "");
            }
        });

        _remainingSeconds = remSec;
        CurfewText.Text = curfewStr;
        UpdateDisplay();
    }

    private void TickSecond()
    {
        if (_remainingSeconds > 0)
        {
            _remainingSeconds--;
        }
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (_user != null && !_user.IsRestricted) return;

        var formatted = FormatTime(_remainingSeconds);
        CountdownText.Text = formatted;
        MiniCountdownText.Text = formatted;

        // Dynamic color changes based on urgency
        var brush = _remainingSeconds switch
        {
            <= 300 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xef, 0x44, 0x44)), // Red (<= 5 min)
            <= 900 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xf5, 0x9e, 0x0b)), // Amber (<= 15 min)
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0xbd, 0xf8))       // Cyan (> 15 min)
        };

        CountdownText.Foreground = brush;
        MiniCountdownText.Foreground = brush;
    }

    private static string FormatTime(int totalSec)
    {
        if (totalSec <= 0) return "00:00";
        var hours = totalSec / 3600;
        var minutes = (totalSec % 3600) / 60;
        var seconds = totalSec % 60;

        if (hours > 0)
        {
            return $"{hours}:{minutes:D2}:{seconds:D2}";
        }
        return $"{minutes:D2}:{seconds:D2}";
    }

    private void CollapseBtn_Click(object sender, RoutedEventArgs e)
    {
        _isCollapsed = true;
        ExpandedPanel.Visibility = Visibility.Collapsed;
        CollapsedPanel.Visibility = Visibility.Visible;
    }

    private void ExpandBtn_Click(object sender, RoutedEventArgs e)
    {
        _isCollapsed = false;
        CollapsedPanel.Visibility = Visibility.Collapsed;
        ExpandedPanel.Visibility = Visibility.Visible;
    }

    private void RequestBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("http://localhost:5050/request") { UseShellExecute = true });
        }
        catch { }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isAdmin)
        {
            Close();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isAdmin)
        {
            // Restricted user cannot close the widget HUD
            e.Cancel = true;
            // Simply minimize/collapse instead
            CollapseBtn_Click(this, new RoutedEventArgs());
            return;
        }
        base.OnClosing(e);
    }
}
