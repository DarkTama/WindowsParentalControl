using System.Windows;
using System.Windows.Controls;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace ParentalControl.Agent;

public partial class FullscreenSettingsWindow : Window
{
    private readonly WidgetSettings _settings;
    private readonly Action<WidgetSettings>? _onSave;
    private readonly Action<WidgetSettings>? _onPreview;

    public FullscreenSettingsWindow(WidgetSettings settings, Action<WidgetSettings>? onSave = null, Action<WidgetSettings>? onPreview = null)
    {
        InitializeComponent();
        _settings = settings;
        _onSave = onSave;
        _onPreview = onPreview;

        LoadScreens();
        ApplyToUI();
    }

    private void LoadScreens()
    {
        MonitorCombo.Items.Clear();
        var screens = WinFormsScreen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var isPrimary = screens[i].Primary ? " (Utama)" : "";
            var text = $"Monitor {i + 1}{isPrimary} [{screens[i].Bounds.Width}x{screens[i].Bounds.Height}]";
            var item = new ComboBoxItem { Content = text, Tag = i };
            MonitorCombo.Items.Add(item);
        }

        var targetIdx = Math.Min(_settings.TargetMonitorIndex, Math.Max(0, screens.Length - 1));
        if (MonitorCombo.Items.Count > targetIdx)
        {
            MonitorCombo.SelectedIndex = targetIdx;
        }
    }

    private void ApplyToUI()
    {
        // Corner
        foreach (ComboBoxItem item in CornerCombo.Items)
        {
            if (item.Tag?.ToString() == _settings.CornerDock.ToString())
            {
                CornerCombo.SelectedItem = item;
                break;
            }
        }

        // Opacity
        OpacitySlider.Value = (int)(_settings.FullscreenOpacity * 100);
        OpacityLabel.Text = $"{OpacitySlider.Value}%";

        // Auto collapse
        AutoCollapseCheck.IsChecked = _settings.AutoCollapseSingleScreen;
    }

    private WidgetSettings ReadFromUI()
    {
        var settings = new WidgetSettings
        {
            CornerDock = CornerCombo.SelectedItem is ComboBoxItem cItem && Enum.TryParse<CornerPreset>(cItem.Tag?.ToString(), out var preset)
                ? preset
                : CornerPreset.TopRight,
            TargetMonitorIndex = MonitorCombo.SelectedItem is ComboBoxItem mItem && mItem.Tag is int idx ? idx : 0,
            FullscreenOpacity = OpacitySlider.Value / 100.0,
            AutoCollapseSingleScreen = AutoCollapseCheck.IsChecked ?? true,
            LastUserX = _settings.LastUserX,
            LastUserY = _settings.LastUserY
        };
        return settings;
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityLabel != null)
        {
            OpacityLabel.Text = $"{(int)e.NewValue}%";
        }
    }

    private void PreviewBtn_Click(object sender, RoutedEventArgs e)
    {
        var tempSettings = ReadFromUI();
        _onPreview?.Invoke(tempSettings);
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var newSettings = ReadFromUI();
        newSettings.Save();
        _onSave?.Invoke(newSettings);
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
