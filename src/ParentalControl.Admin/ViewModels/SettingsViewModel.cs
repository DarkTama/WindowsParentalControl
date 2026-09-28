using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ParentalControl.Core.Data;
using ParentalControl.Core.Security;
using ParentalControl.Core.Services;

namespace ParentalControl.Admin.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly Action _navigateBack;

    [ObservableProperty]
    private string _telegramBotToken = string.Empty;

    [ObservableProperty]
    private string _telegramChatId = string.Empty;

    [ObservableProperty]
    private string _alertIntervals = "15,5,1";

    [ObservableProperty]
    private bool _isTotpEnabled;

    [ObservableProperty]
    private string _totpSecret = string.Empty;

    [ObservableProperty]
    private string _totpUri = string.Empty;

    [ObservableProperty]
    private string _testStatus = string.Empty;

    public SettingsViewModel(Action navigateBack)
    {
        _navigateBack = navigateBack;
        LoadSettings();
    }

    public void LoadSettings()
    {
        TelegramBotToken = SettingsRepository.Get(SettingsRepository.KeyTelegramBotToken);
        TelegramChatId = SettingsRepository.Get(SettingsRepository.KeyTelegramChatId);
        AlertIntervals = SettingsRepository.Get(SettingsRepository.KeyAlertIntervals, "15,5,1");
        IsTotpEnabled = SettingsRepository.Get(SettingsRepository.KeyTotpEnabled, "false") == "true";
        TotpSecret = SettingsRepository.Get(SettingsRepository.KeyTotpSecret);

        if (!string.IsNullOrEmpty(TotpSecret))
        {
            TotpUri = TotpService.GenerateOtpauthUri("ParentalControl", Environment.MachineName, TotpSecret);
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigateBack();
    }

    [RelayCommand]
    private void GenerateTotpSecret()
    {
        TotpSecret = TotpService.GenerateSecret();
        TotpUri = TotpService.GenerateOtpauthUri("ParentalControl", Environment.MachineName, TotpSecret);
    }

    [RelayCommand]
    private async Task TestTelegram()
    {
        TestStatus = "Checking connectivity...";
        var isOnline = await TelegramBotService.CheckConnectivityAsync();
        if (!isOnline)
        {
            TestStatus = "❌ No internet or Telegram unreachable.";
            return;
        }

        if (string.IsNullOrWhiteSpace(TelegramBotToken) || string.IsNullOrWhiteSpace(TelegramChatId))
        {
            TestStatus = "⚠️ Please enter Bot Token and Chat ID first.";
            return;
        }

        TestStatus = "Sending test message...";
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var url = $"https://api.telegram.org/bot{TelegramBotToken}/sendMessage";
            var payload = new
            {
                chat_id = TelegramChatId,
                text = "🔔 *Parental Control Test Message*\nTelegram alerts are working properly on " + Environment.MachineName,
                parse_mode = "Markdown"
            };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            using var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var res = await client.PostAsync(url, content);
            if (res.IsSuccessStatusCode)
            {
                TestStatus = "✅ Test message delivered to Telegram!";
            }
            else
            {
                TestStatus = $"❌ Telegram returned error: {res.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            TestStatus = $"❌ Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        SettingsRepository.Set(SettingsRepository.KeyTelegramBotToken, TelegramBotToken.Trim());
        SettingsRepository.Set(SettingsRepository.KeyTelegramChatId, TelegramChatId.Trim());
        SettingsRepository.Set(SettingsRepository.KeyAlertIntervals, AlertIntervals.Trim());
        SettingsRepository.Set(SettingsRepository.KeyTotpEnabled, IsTotpEnabled ? "true" : "false");
        SettingsRepository.Set(SettingsRepository.KeyTotpSecret, TotpSecret.Trim());

        MessageBox.Show("Settings saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
