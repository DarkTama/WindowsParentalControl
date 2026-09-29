using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MediaColor = System.Windows.Media.Color;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace ParentalControl.Agent;

public partial class WidgetWindow : Window
{
    private readonly DispatcherTimer _secondTimer;
    private readonly DispatcherTimer _fullscreenCheckTimer;
    private readonly DispatcherTimer _toastTimer;
    private DispatcherTimer? _previewTimer;

    private int _remainingSeconds;
    private bool _isLocked;
    private bool _isCurfewRestricted;
    private bool _isCollapsed;
    public bool IsCollapsed => _isCollapsed;

    private WidgetSettings _settings;
    private bool _isFullscreenActive;
    private bool _isPreviewActive;
    private double _savedUserLeft;
    private double _savedUserTop;
    private bool _savedUserCollapsed;
    private bool _isPulsing;

    public WidgetWindow()
    {
        InitializeComponent();

        _settings = WidgetSettings.Load();
        UserTitleText.Text = Environment.UserName;

        // Restore last dragged coordinates or default to top-right of primary work area
        if (_settings.LastUserX >= 0 && _settings.LastUserY >= 0)
        {
            Left = _settings.LastUserX;
            Top = _settings.LastUserY;
        }
        else
        {
            Left = Math.Max(20, SystemParameters.WorkArea.Right - 270);
            Top = 40;
        }
        _savedUserLeft = Left;
        _savedUserTop = Top;

        // 1-second countdown display timer
        _secondTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _secondTimer.Tick += (s, e) => TickSecond();
        _secondTimer.Start();

        // 2-second fullscreen check timer
        _fullscreenCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _fullscreenCheckTimer.Tick += (s, e) => CheckFullscreen();
        _fullscreenCheckTimer.Start();

        // Toast auto-dismiss timer (8 seconds)
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _toastTimer.Tick += (s, e) =>
        {
            _toastTimer.Stop();
            DeclineToastPanel.Visibility = Visibility.Collapsed;
        };
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
        if (e.ButtonState == MouseButtonState.Pressed && !_isFullscreenActive && !_isPreviewActive)
        {
            DragMove();
            _savedUserLeft = Left;
            _savedUserTop = Top;
            _settings.LastUserX = Left;
            _settings.LastUserY = Top;
            _settings.Save();
        }
    }

    public void UpdateData(int remainingSec, string curfew, bool isLocked, string username, bool isCurfewClamped = false)
    {
        Dispatcher.Invoke(() =>
        {
            _remainingSeconds = remainingSec;
            _isLocked = isLocked;
            _isCurfewRestricted = isCurfewClamped;
            UserTitleText.Text = !string.IsNullOrWhiteSpace(username) ? username : Environment.UserName;

            if (_isLocked)
            {
                CurfewText.Text = "⏸ Sesi Terkunci (Dijeda)";
                CurfewIcon.Visibility = Visibility.Collapsed;
            }
            else
            {
                CurfewText.Text = !string.IsNullOrWhiteSpace(curfew) ? curfew : "Jam malam: --:--";
                CurfewIcon.Visibility = _isCurfewRestricted ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdateDisplay();
        });
    }

    public void ShowDeclineToast(string reason, DateTime time)
    {
        Dispatcher.Invoke(() =>
        {
            DeclineReasonText.Text = !string.IsNullOrWhiteSpace(reason) ? reason : "Permintaan ditolak oleh orang tua.";
            DeclineTimeText.Text = $"{time:HH:mm:ss}";
            DeclineToastPanel.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        });
    }

    private void CloseToast_Click(object sender, RoutedEventArgs e)
    {
        _toastTimer.Stop();
        DeclineToastPanel.Visibility = Visibility.Collapsed;
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
            var pauseBrush = new SolidColorBrush(MediaColor.FromRgb(0xf5, 0x9e, 0x0b));
            CountdownText.Foreground = pauseBrush;
            MiniCountdownText.Foreground = pauseBrush;
            StopPulsing();
            return;
        }

        // Visual Urgency States & Colors
        SolidColorBrush borderBrush;
        SolidColorBrush textBrush;

        if (_remainingSeconds <= 60)
        {
            // <= 1m: Critical Pulsing Red
            borderBrush = new SolidColorBrush(MediaColor.FromRgb(0xef, 0x44, 0x44));
            textBrush = borderBrush;
            StartPulsing();
        }
        else if (_remainingSeconds <= 300)
        {
            // <= 5m: Red Border
            borderBrush = new SolidColorBrush(MediaColor.FromRgb(0xef, 0x44, 0x44));
            textBrush = borderBrush;
            StopPulsing();
        }
        else if (_remainingSeconds <= 900)
        {
            // <= 15m: Amber Border
            borderBrush = new SolidColorBrush(MediaColor.FromRgb(0xf5, 0x9e, 0x0b));
            textBrush = borderBrush;
            StopPulsing();
        }
        else
        {
            // Normal: Calm Blue Border
            borderBrush = new SolidColorBrush(MediaColor.FromRgb(0x25, 0x63, 0xeb));
            textBrush = new SolidColorBrush(MediaColor.FromRgb(0x38, 0xbd, 0xf8));
            StopPulsing();
        }

        ExpandedPanel.BorderBrush = borderBrush;
        CollapsedPanel.BorderBrush = borderBrush;
        CountdownText.Foreground = textBrush;
        MiniCountdownText.Foreground = textBrush;
    }

    private void StartPulsing()
    {
        if (_isPulsing) return;
        _isPulsing = true;
        if (Resources["PulsingStoryboard"] is Storyboard sb)
        {
            sb.Begin(this, true);
        }
    }

    private void StopPulsing()
    {
        if (!_isPulsing) return;
        _isPulsing = false;
        if (Resources["PulsingStoryboard"] is Storyboard sb)
        {
            sb.Stop(this);
        }
        ExpandedPanel.Opacity = _isFullscreenActive || _isPreviewActive ? _settings.FullscreenOpacity : 1.0;
        CollapsedPanel.Opacity = _isFullscreenActive || _isPreviewActive ? _settings.FullscreenOpacity : 1.0;
    }

    private void CheckFullscreen()
    {
        if (_isPreviewActive) return;

        bool isFs = FullscreenDetector.IsForegroundFullscreen(out var mRect, out _);

        if (isFs && !_isFullscreenActive)
        {
            // Entered Fullscreen
            _isFullscreenActive = true;
            _savedUserLeft = Left;
            _savedUserTop = Top;
            _savedUserCollapsed = _isCollapsed;
            ApplyFullscreenLayout(_settings);
        }
        else if (!isFs && _isFullscreenActive)
        {
            // Exited Fullscreen
            _isFullscreenActive = false;
            RestoreUserLayout();
        }
    }

    private void ApplyFullscreenLayout(WidgetSettings settings)
    {
        var screens = WinFormsScreen.AllScreens;

        if (screens.Length > 1)
        {
            // Multi-Monitor Setup: Relocate to secondary display
            var targetIdx = Math.Min(settings.TargetMonitorIndex, screens.Length - 1);
            if (targetIdx == 0 && screens.Length > 1) targetIdx = 1; // Default to secondary if monitor 0 selected

            var targetScreen = screens[targetIdx];
            PositionAtCorner(targetScreen.WorkingArea, settings.CornerDock);
            Opacity = settings.FullscreenOpacity;
        }
        else
        {
            // Single-Monitor Setup
            if (settings.AutoCollapseSingleScreen && !_isCollapsed)
            {
                SetCollapsedState(true);
            }
            var primary = WinFormsScreen.PrimaryScreen ?? screens[0];
            PositionAtCorner(primary.WorkingArea, settings.CornerDock);
            Opacity = settings.FullscreenOpacity;
        }
    }

    private void PositionAtCorner(System.Drawing.Rectangle workArea, CornerPreset corner)
    {
        const int margin = 16;
        var w = _isCollapsed ? 145 : 240;
        var h = _isCollapsed ? 36 : 192;

        switch (corner)
        {
            case CornerPreset.TopRight:
                Left = workArea.Right - w - margin;
                Top = workArea.Top + margin;
                break;
            case CornerPreset.TopLeft:
                Left = workArea.Left + margin;
                Top = workArea.Top + margin;
                break;
            case CornerPreset.BottomRight:
                Left = workArea.Right - w - margin;
                Top = workArea.Bottom - h - margin;
                break;
            case CornerPreset.BottomLeft:
                Left = workArea.Left + margin;
                Top = workArea.Bottom - h - margin;
                break;
        }
    }

    private void RestoreUserLayout()
    {
        Opacity = 1.0;
        Left = _savedUserLeft;
        Top = _savedUserTop;
        if (_isCollapsed != _savedUserCollapsed)
        {
            SetCollapsedState(_savedUserCollapsed);
        }
    }

    public void TriggerPreview(WidgetSettings previewSettings)
    {
        _isPreviewActive = true;
        _previewTimer?.Stop();

        _savedUserLeft = Left;
        _savedUserTop = Top;
        _savedUserCollapsed = _isCollapsed;

        ApplyFullscreenLayout(previewSettings);

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _previewTimer.Tick += (s, e) =>
        {
            _previewTimer.Stop();
            _previewTimer = null;
            _isPreviewActive = false;
            RestoreUserLayout();
        };
        _previewTimer.Start();
    }

    private void SetCollapsedState(bool collapse)
    {
        _isCollapsed = collapse;
        if (collapse)
        {
            ExpandedPanel.Visibility = Visibility.Collapsed;
            CollapsedPanel.Visibility = Visibility.Visible;
            Width = 145;
            Height = 36;
        }
        else
        {
            CollapsedPanel.Visibility = Visibility.Collapsed;
            ExpandedPanel.Visibility = Visibility.Visible;
            Width = 240;
            Height = 192;
        }
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
        SetCollapsedState(true);
    }

    private void ExpandBtn_Click(object sender, RoutedEventArgs e)
    {
        SetCollapsedState(false);
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new FullscreenSettingsWindow(
            _settings,
            onSave: newSettings =>
            {
                _settings = newSettings;
                if (_isFullscreenActive) ApplyFullscreenLayout(_settings);
            },
            onPreview: previewSettings =>
            {
                TriggerPreview(previewSettings);
            });
        dlg.Owner = this;
        dlg.ShowDialog();
    }

    public void OpenSettings()
    {
        SettingsBtn_Click(this, new RoutedEventArgs());
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
        e.Cancel = true;
        CollapseBtn_Click(this, new RoutedEventArgs());
    }
}
