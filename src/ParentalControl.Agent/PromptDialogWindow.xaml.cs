using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace ParentalControl.Agent;

public partial class PromptDialogWindow : Window
{
    private readonly int _promptId;
    private readonly string _urgency;
    private readonly int _targetDisplay;
    private readonly int _timeoutSeconds;
    private readonly List<string> _options;
    private readonly Action<string, string?, int> _onCompleted;

    private readonly DispatcherTimer _countdownTimer;
    private readonly DateTime _startTime;
    private int _remainingSeconds;
    private bool _submitted;

    public PromptDialogWindow(
        int promptId,
        string message,
        string urgency,
        int targetDisplay,
        int timeoutSeconds,
        List<string>? options,
        Action<string, string?, int> onCompleted)
    {
        InitializeComponent();

        _promptId = promptId;
        _urgency = urgency?.ToUpperInvariant() ?? "NORMAL";
        _targetDisplay = targetDisplay;
        _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 120;
        _options = options ?? new List<string>();
        _onCompleted = onCompleted;

        _remainingSeconds = _timeoutSeconds;
        _startTime = DateTime.UtcNow;

        MessageText.Text = message;
        ConfigureUrgencyStyle();
        PopulatePresetChips();

        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += CountdownTimer_Tick;
        _countdownTimer.Start();
    }

    private void ConfigureUrgencyStyle()
    {
        if (_urgency == "URGENT")
        {
            OuterBorder.BorderBrush = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#ef4444"));
            TitleText.Text = "🚨 PESAN MENDESAK DARI ORANG TUA";
            TitleText.Foreground = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#ef4444"));
            IconText.Text = "🚨";
        }
    }

    private void PopulatePresetChips()
    {
        PresetChipsPanel.Children.Clear();
        foreach (var opt in _options)
        {
            var btn = new WpfButton
            {
                Content = opt,
                Style = (Style)FindResource("PresetChipButton")
            };
            btn.Click += (s, e) =>
            {
                ReasonTextBox.Text = opt;
                ReasonTextBox.Focus();
                ReasonTextBox.CaretIndex = ReasonTextBox.Text.Length;
            };
            PresetChipsPanel.Children.Add(btn);
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionOnTargetMonitor();

        if (_urgency == "URGENT")
        {
            try
            {
                SystemSounds.Exclamation.Play();
            }
            catch { }
        }

        Activate();
        Focus();
    }

    private void PositionOnTargetMonitor()
    {
        try
        {
            var screens = WinFormsScreen.AllScreens;
            WinFormsScreen targetScreen;

            if (_targetDisplay < 0)
            {
                // Auto: monitor under current cursor / foreground
                var cursorPoint = System.Windows.Forms.Cursor.Position;
                targetScreen = WinFormsScreen.FromPoint(cursorPoint) ?? WinFormsScreen.PrimaryScreen ?? screens[0];
            }
            else
            {
                var clampedIdx = Math.Clamp(_targetDisplay, 0, screens.Length - 1);
                targetScreen = screens[clampedIdx];
            }

            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            var wa = targetScreen.WorkingArea;
            var targetWidth = ActualWidth > 0 ? ActualWidth : Width;
            var targetHeight = ActualHeight > 0 ? ActualHeight : 260;

            Left = (wa.Left / dpiX) + Math.Max(0, ((wa.Width / dpiX) - targetWidth) / 2);
            Top = (wa.Top / dpiY) + Math.Max(0, ((wa.Height / dpiY) - targetHeight) / 2);
        }
        catch
        {
            // Default center if screen math fails
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void CountdownTimer_Tick(object? sender, EventArgs e)
    {
        _remainingSeconds--;
        if (_remainingSeconds <= 0)
        {
            _countdownTimer.Stop();
            SubmitResponse("TIMEOUT", null);
            return;
        }

        CountdownText.Text = $"⏱️ {_remainingSeconds}s";
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        SubmitResponse("YES", null);
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        InitialActionGrid.Visibility = Visibility.Collapsed;
        ReasonPanel.Visibility = Visibility.Visible;
        ReasonTextBox.Focus();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        ReasonPanel.Visibility = Visibility.Collapsed;
        InitialActionGrid.Visibility = Visibility.Visible;
    }

    private void SendReasonButton_Click(object sender, RoutedEventArgs e)
    {
        var reason = ReasonTextBox.Text.Trim();
        SubmitResponse("NO", string.IsNullOrWhiteSpace(reason) ? "Ditolak tanpa alasan khusus" : reason);
    }

    private void SubmitResponse(string response, string? reason)
    {
        if (_submitted) return;
        _submitted = true;

        _countdownTimer.Stop();
        var elapsed = Math.Max(1, (int)(DateTime.UtcNow - _startTime).TotalSeconds);

        try
        {
            _onCompleted?.Invoke(response, reason, elapsed);
        }
        finally
        {
            Close();
        }
    }
}
