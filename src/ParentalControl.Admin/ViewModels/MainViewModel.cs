using ParentalControl.Core.Data;
using System.ServiceProcess;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ParentalControl.Admin.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string ServiceName = "ParentalControl.Service";

    private readonly DashboardViewModel _dashboardVm;
    private readonly DispatcherTimer _serviceTimer;
    private SettingsViewModel? _settingsVm;

    [ObservableProperty]
    private ObservableObject _currentView = null!;

    [ObservableProperty]
    private string _serviceStatusText = "Service: ...";

    [ObservableProperty]
    private SolidColorBrush _serviceStatusColor = Brushes.Gray;

    [ObservableProperty]
    private bool _isServiceRunning;

    public MainViewModel()
    {
        _dashboardVm = new DashboardViewModel(NavigateToUserDetail);
        CurrentView = _dashboardVm;
        _dashboardVm.LoadAll();
        _dashboardVm.LoadAll();
        CheckFirstLaunchLanguagePreset();
        UpdateServiceStatus();
        _serviceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _serviceTimer.Tick += (_, _) => UpdateServiceStatus();
        _serviceTimer.Start();
    }

    public void StopServicePolling() => _serviceTimer.Stop();

    private void UpdateServiceStatus()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status == ServiceControllerStatus.Running)
            {
                ServiceStatusText = "Service: Up";
                ServiceStatusColor = Brushes.Green;
                IsServiceRunning = true;
                return;
            }
        }
        catch
        {
            // Service not registered in Windows Service Manager (e.g. running in console / dev mode)
        }

        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName("ParentalControl.Service");
            if (processes.Length > 0)
            {
                ServiceStatusText = "Service: Up (Console)";
                ServiceStatusColor = Brushes.Green;
                IsServiceRunning = true;
                return;
            }
        }
        catch
        {
        }

        ServiceStatusText = "Service: Down";
        ServiceStatusColor = Brushes.Red;
        IsServiceRunning = false;
    }

    private void NavigateToUserDetail(UserRow user)
    {
        var detailVm = new UserDetailViewModel(user, _dashboardVm.SidToUsername, NavigateBack);
        CurrentView = detailVm;
        detailVm.LoadAll();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ShowDashboard()
    {
        NavigateBack();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ShowSettings()
    {
        _settingsVm = new SettingsViewModel(NavigateBack);
        CurrentView = _settingsVm;
    }

    private void NavigateBack()
    {
        _dashboardVm.LoadAll();
        CurrentView = _dashboardVm;
    }

    private void CheckFirstLaunchLanguagePreset()
    {
        var existing = SettingsRepository.Get(SettingsRepository.KeyLanguagePreset);
        if (string.IsNullOrWhiteSpace(existing))
        {
            var result = System.Windows.MessageBox.Show(
                "Welcome to Parental Control!\n\n" +
                "Please choose your default language preset for child notifications, screen warnings, and the time request portal:\n\n" +
                "• Click [YES] for Bahasa Indonesia (Default)\n" +
                "• Click [NO] for English\n\n" +
                "(Note: You can customize every alert message or change presets anytime later under Settings.)",
                "Parental Control — Initial Setup",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                SettingsRepository.ApplyLanguagePreset("id");
            }
            else
            {
                SettingsRepository.ApplyLanguagePreset("en");
            }
        }
    }
}
