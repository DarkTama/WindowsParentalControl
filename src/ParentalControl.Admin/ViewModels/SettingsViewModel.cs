using System.IO;
using System.Windows.Media.Imaging;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ParentalControl.Core.Data;
using ParentalControl.Core.Security;
using ParentalControl.Core.Services;
using ParentalControl.Core;

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
    private int _maxDailyRequests = 1;

    [ObservableProperty]
    private bool _isTotpEnabled;

    [ObservableProperty]
    private string _totpSecret = string.Empty;

    [ObservableProperty]
    private string _totpUri = string.Empty;

    [ObservableProperty]
    private BitmapSource? _totpQrImage;

    [ObservableProperty]
    private bool _hasQrCode;

    [ObservableProperty]
    private string _testStatus = string.Empty;

    // Application Version & Built-in Updater
    [ObservableProperty]
    private string _currentVersion = AppVersion.DisplayName;

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private string _latestVersion = string.Empty;

    [ObservableProperty]
    private string _releaseTitle = string.Empty;

    [ObservableProperty]
    private string _releaseNotes = string.Empty;

    [ObservableProperty]
    private string? _downloadUrl;

    [ObservableProperty]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    private bool _isInstallingUpdate;

    // Customizable User Notification Messages
    [ObservableProperty]
    private string _languagePreset = "id";

    [ObservableProperty]
    private string _msgLimitWarn = string.Empty;

    [ObservableProperty]
    private string _msgCurfewWarn = string.Empty;

    [ObservableProperty]
    private string _msgLimitReached = string.Empty;

    [ObservableProperty]
    private string _msgCurfewReached = string.Empty;

    [ObservableProperty]
    private string _msgLoginDeniedLimit = string.Empty;

    [ObservableProperty]
    private string _msgLoginDeniedCurfew = string.Empty;

    [ObservableProperty]
    private string _msgRemoteLock = string.Empty;

    [ObservableProperty]
    private string _msgBonusGranted = string.Empty;

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
        MaxDailyRequests = SettingsRepository.GetMaxDailyRequests();
        IsTotpEnabled = SettingsRepository.Get(SettingsRepository.KeyTotpEnabled, "false") == "true";
        TotpSecret = SettingsRepository.Get(SettingsRepository.KeyTotpSecret);

        if (!string.IsNullOrWhiteSpace(TotpSecret))
        {
            TotpUri = TotpService.GenerateOtpauthUri("ParentalControl", Environment.MachineName, TotpSecret);
            RefreshQrCode();
        }

        // Load message templates
        LanguagePreset = SettingsRepository.Get(SettingsRepository.KeyLanguagePreset, "id");
        MsgLimitWarn = SettingsRepository.GetMessage(SettingsRepository.KeyMsgLimitWarn);
        MsgCurfewWarn = SettingsRepository.GetMessage(SettingsRepository.KeyMsgCurfewWarn);
        MsgLimitReached = SettingsRepository.GetMessage(SettingsRepository.KeyMsgLimitReached);
        MsgCurfewReached = SettingsRepository.GetMessage(SettingsRepository.KeyMsgCurfewReached);
        MsgLoginDeniedLimit = SettingsRepository.GetMessage(SettingsRepository.KeyMsgLoginDeniedLimit);
        MsgLoginDeniedCurfew = SettingsRepository.GetMessage(SettingsRepository.KeyMsgLoginDeniedCurfew);
        MsgRemoteLock = SettingsRepository.GetMessage(SettingsRepository.KeyMsgRemoteLock);
        MsgBonusGranted = SettingsRepository.GetMessage(SettingsRepository.KeyMsgBonusGranted);
    }

    [RelayCommand]
    private void ApplyPresetId()
    {
        SettingsRepository.ApplyLanguagePreset("id");
        LoadSettings();
        TestStatus = "Applied Indonesian message preset.";
    }

    [RelayCommand]
    private void ApplyPresetEn()
    {
        SettingsRepository.ApplyLanguagePreset("en");
        LoadSettings();
        TestStatus = "Applied English message preset.";
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigateBack();
    }

    partial void OnTotpSecretChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            TotpUri = TotpService.GenerateOtpauthUri("ParentalControl", Environment.MachineName, value.Trim());
        }
        else
        {
            TotpUri = string.Empty;
        }
        RefreshQrCode();
    }

    [RelayCommand]
    private void GenerateTotpSecret()
    {
        TotpSecret = TotpService.GenerateSecret();
    }

    private void RefreshQrCode()
    {
        if (!string.IsNullOrWhiteSpace(TotpUri))
        {
            try
            {
                var pngBytes = TotpService.GenerateQrCodePng(TotpUri, 6);
                var image = new BitmapImage();
                using var ms = new MemoryStream(pngBytes);
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();
                TotpQrImage = image;
                HasQrCode = true;
                return;
            }
            catch
            {
            }
        }

        TotpQrImage = null;
        HasQrCode = false;
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
        if (TelegramChatId.Trim().StartsWith("@"))
        {
            TestStatus = "⚠️ Telegram requires numeric Chat ID (e.g. 123456789), not @username.";
            return;
        }

        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var url = $"https://api.telegram.org/bot{TelegramBotToken.Trim()}/sendMessage";
            var payload = new
            {
                chat_id = TelegramChatId.Trim(),
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
                var errorBody = await res.Content.ReadAsStringAsync();
                try
                {
                    var errDoc = System.Text.Json.Nodes.JsonNode.Parse(errorBody);
                    var desc = errDoc?["description"]?.ToString();
                    TestStatus = $"❌ Telegram error: {desc ?? res.StatusCode.ToString()}";
                }
                catch
                {
                    TestStatus = $"❌ Telegram returned error: {res.StatusCode}";
                }
            }
        }
        catch (Exception ex)
        {
            TestStatus = $"❌ Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DetectChatId()
    {
        if (string.IsNullOrWhiteSpace(TelegramBotToken))
        {
            TestStatus = "⚠️ Enter Bot Token first.";
            return;
        }

        TestStatus = "Checking for messages to bot...";
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var url = $"https://api.telegram.org/bot{TelegramBotToken.Trim()}/getUpdates";
            var res = await client.GetStringAsync(url);
            var doc = System.Text.Json.Nodes.JsonNode.Parse(res);
            var results = doc?["result"]?.AsArray();
            if (results != null && results.Count > 0)
            {
                var lastMsg = results.LastOrDefault(u => u?["message"] != null)?["message"];
                var from = lastMsg?["from"];
                var detectedId = from?["id"]?.ToString();
                var detectedUser = from?["username"]?.ToString() ?? from?["first_name"]?.ToString();
                if (!string.IsNullOrEmpty(detectedId))
                {
                    TelegramChatId = detectedId;
                    TestStatus = $"✅ Detected ID {detectedId} for {detectedUser}!";
                    return;
                }
            }
            TestStatus = "⚠️ No messages found. Open bot in Telegram & send /start first!";
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
        SettingsRepository.Set(SettingsRepository.KeyMaxDailyRequests, MaxDailyRequests.ToString());
        SettingsRepository.Set(SettingsRepository.KeyTotpEnabled, IsTotpEnabled ? "true" : "false");
        SettingsRepository.Set(SettingsRepository.KeyTotpSecret, TotpSecret.Trim());

        // Save customizable messages
        SettingsRepository.Set(SettingsRepository.KeyMsgLimitWarn, MsgLimitWarn.Trim());
        SettingsRepository.Set(SettingsRepository.KeyMsgCurfewWarn, MsgCurfewWarn.Trim());
        SettingsRepository.Set(SettingsRepository.KeyMsgLimitReached, MsgLimitReached.Trim());
        SettingsRepository.Set(SettingsRepository.KeyMsgCurfewReached, MsgCurfewReached.Trim());
        SettingsRepository.Set(SettingsRepository.KeyMsgLoginDeniedLimit, MsgLoginDeniedLimit.Trim());
        SettingsRepository.Set(SettingsRepository.KeyMsgLoginDeniedCurfew, MsgLoginDeniedCurfew.Trim());
        SettingsRepository.Set(SettingsRepository.KeyMsgRemoteLock, MsgRemoteLock.Trim());
        SettingsRepository.Set(SettingsRepository.KeyMsgBonusGranted, MsgBonusGranted.Trim());

        MessageBox.Show("Settings saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingUpdate = true;
        UpdateStatus = "Memeriksa pembaruan di GitHub Releases...";
        try
        {
            var res = await UpdateService.CheckForUpdatesAsync();
            if (res.Error != null)
            {
                UpdateStatus = $"Gagal memeriksa: {res.Error}";
                HasUpdate = false;
            }
            else if (res.HasUpdate)
            {
                HasUpdate = true;
                LatestVersion = res.LatestVersion;
                ReleaseTitle = res.ReleaseTitle;
                ReleaseNotes = res.ReleaseNotes;
                DownloadUrl = res.DownloadUrl;
                UpdateStatus = $"Pembaruan baru v{res.LatestVersion} tersedia!";
            }
            else
            {
                HasUpdate = false;
                UpdateStatus = $"Aplikasi sudah menggunakan versi terbaru ({AppVersion.DisplayName}).";
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Kesalahan: {ex.Message}";
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        if (string.IsNullOrWhiteSpace(DownloadUrl))
        {
            MessageBox.Show("URL installer tidak ditemukan pada rilis ini.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Unduh dan pasang pembaruan v{LatestVersion} sekarang?\nLayanan dan agent akan dimuat ulang secara otomatis.",
            "Konfirmasi Pembaruan",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        IsInstallingUpdate = true;
        UpdateStatus = "Mengunduh file installer ParentalControlSetup.exe...";
        var (success, msg) = await UpdateService.DownloadAndApplyUpdateAsync(DownloadUrl);
        UpdateStatus = msg;
        IsInstallingUpdate = false;

        if (success)
        {
            MessageBox.Show(msg, "Pembaruan Dimulai", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(msg, "Gagal Memperbarui", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
