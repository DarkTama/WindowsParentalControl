using MediaColor = System.Windows.Media.Color;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ParentalControl.Agent;

public partial class WidgetWindow : Window
{
    private readonly DispatcherTimer _secondTimer;
    private int _remainingSeconds;
    private bool _isLocked;
    private bool _isCollapsed;
    public bool IsCollapsed => _isCollapsed;

    public WidgetWindow()
    {
        InitializeComponent();

        UserTitleText.Text = Environment.UserName;

        // Position at top-right of work area
        Left = Math.Max(20, SystemParameters.WorkArea.Right - 260);
        Top = 40;

        _secondTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _secondTimer.Tick += (s, e) => TickSecond();
        _secondTimer.Start();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var hwndSource = HwndSource.FromHwnd(hwnd);
            if (hwndSource?.CompositionTarget != null)
            {
                hwndSource.CompositionTarget.BackgroundColor = Colors.Transparent;
            }
        }
        catch { }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    public void UpdateData(int remainingSec, string curfew, bool isLocked, string username)
    {
        Dispatcher.Invoke(() =>
        {
            _remainingSeconds = remainingSec;
            _isLocked = isLocked;
            UserTitleText.Text = !string.IsNullOrWhiteSpace(username) ? username : Environment.UserName;

            if (_isLocked)
            {
                CurfewText.Text = "⏸ Sesi Terkunci (Dijeda)";
            }
            else
            {
                CurfewText.Text = !string.IsNullOrWhiteSpace(curfew) ? curfew : "Jam malam: --:--";
            }

            UpdateDisplay();
        });
    }

    private void TickSecond()
    {
        if (!_isLocked && _remainingSeconds > 0)
        {
            _remainingSeconds--;
        }
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        var formatted = FormatTime(_remainingSeconds);
        CountdownText.Text = formatted;
        MiniCountdownText.Text = formatted;

        if (_isLocked)
        {
            var pauseBrush = new SolidColorBrush(MediaColor.FromRgb(0xf5, 0x9e, 0x0b)); // Amber
            CountdownText.Foreground = pauseBrush;
            MiniCountdownText.Foreground = pauseBrush;
            return;
        }

        // Dynamic color changes based on urgency
        var brush = _remainingSeconds switch
        {
            <= 300 => new SolidColorBrush(MediaColor.FromRgb(0xef, 0x44, 0x44)), // Red (<= 5 min)
            <= 900 => new SolidColorBrush(MediaColor.FromRgb(0xf5, 0x9e, 0x0b)), // Amber (<= 15 min)
            _ => new SolidColorBrush(MediaColor.FromRgb(0x38, 0xbd, 0xf8))       // Cyan (> 15 min)
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
        Width = 145;
        Height = 36;
    }

    private void ExpandBtn_Click(object sender, RoutedEventArgs e)
    {
        _isCollapsed = false;
        CollapsedPanel.Visibility = Visibility.Collapsed;
        ExpandedPanel.Visibility = Visibility.Visible;
        Width = 240;
        Height = 192;
    }

    private void RequestBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var username = Uri.EscapeDataString(Environment.UserName);
            Process.Start(new ProcessStartInfo($"http://localhost:5050/request?user={username}") { UseShellExecute = true });
        }
        catch { }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Restricted child cannot close the widget; collapse it instead
        e.Cancel = true;
        CollapseBtn_Click(this, new RoutedEventArgs());
    }
}
