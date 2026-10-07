using Microsoft.Data.Sqlite;

namespace ParentalControl.Core.Data;

public static class SettingsRepository
{
    public const string KeyTelegramBotToken = "telegram_bot_token";
    public const string KeyTelegramChatId = "telegram_chat_id";
    public const string KeyTelegramNotifySignIn = "telegram_notify_sign_in";
    public const string KeyTelegramNotifySignOut = "telegram_notify_sign_out";
    public const string KeyTelegramNotifyAdminLogon = "telegram_notify_admin_logon";
    public const string KeyTotpSecret = "totp_secret";
    public const string KeyTotpEnabled = "totp_enabled";
    public const string KeyAlertIntervals = "alert_intervals"; // e.g. "15,5,1"
    public const string KeyMaxDailyRequests = "max_daily_requests"; // default: 1
    public const string KeyLanguagePreset = "language_preset"; // "id" or "en"
    public const string KeyPromptResponsePresets = "prompt_response_presets";
    public const string DefaultPromptResponsePresets = "Sebentar lagi selesai game\nSedang tugas sekolah\nOke, segera logout\nTanggung, minta waktu 5 menit lagi";

    // Customizable message keys
    public const string KeyMsgLimitWarn = "msg_limit_warn";
    public const string KeyMsgCurfewWarn = "msg_curfew_warn";
    public const string KeyMsgLimitReached = "msg_limit_reached";
    public const string KeyMsgCurfewReached = "msg_curfew_reached";
    public const string KeyMsgLoginDeniedLimit = "msg_login_denied_limit";
    public const string KeyMsgLoginDeniedCurfew = "msg_login_denied_curfew";
    public const string KeyMsgRemoteLock = "msg_remote_lock";
    public const string KeyMsgBonusGranted = "msg_bonus_granted";
    public static string Get(string key, string defaultValue = "")
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_settings WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);

        var result = cmd.ExecuteScalar();
        return result?.ToString() ?? defaultValue;
    }

    public static int GetMaxDailyRequests()
    {
        var val = Get(KeyMaxDailyRequests, "1");
        return int.TryParse(val, out var max) && max >= 0 ? max : 1;
    }

    public static bool IsTelegramNotifySignInEnabled()
    {
        return Get(KeyTelegramNotifySignIn, "true") == "true";
    }

    public static bool IsTelegramNotifySignOutEnabled()
    {
        return Get(KeyTelegramNotifySignOut, "true") == "true";
    }

    public static bool IsTelegramNotifyAdminLogonEnabled()
    {
        return Get(KeyTelegramNotifyAdminLogon, "true") == "true";
    }

    public static List<string> GetPromptResponsePresets()
    {
        var raw = Get(KeyPromptResponsePresets, DefaultPromptResponsePresets);
        return raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                  .Select(s => s.Trim())
                  .Where(s => !string.IsNullOrEmpty(s))
                  .ToList();
    }

    public static void Set(string key, string value)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO app_settings (key, value)
            VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = @value
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value ?? string.Empty);
        cmd.ExecuteNonQuery();
    }

    public static Dictionary<string, string> GetAll()
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM app_settings";

        var dict = new Dictionary<string, string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            dict[reader.GetString(0)] = reader.GetString(1);
        }
        return dict;
    }

    public static void ApplyLanguagePreset(string lang)
    {
        var isEnglish = lang.Equals("en", StringComparison.OrdinalIgnoreCase);
        Set(KeyLanguagePreset, isEnglish ? "en" : "id");

        if (isEnglish)
        {
            Set(KeyMsgLimitWarn, "You have {minutes} minutes of daily screen time remaining. Please save your work.");
            Set(KeyMsgCurfewWarn, "Allowed usage time ends at {curfew_end} ({minutes} minutes remaining). Please save your work.");
            Set(KeyMsgLimitReached, "Your daily screen time limit has been reached. Your session will close now.");
            Set(KeyMsgCurfewReached, "Allowed usage time has ended (curfew). Your session will close now.");
            Set(KeyMsgLoginDeniedLimit, "Sign-in denied: Daily screen time limit has already been reached.");
            Set(KeyMsgLoginDeniedCurfew, "Sign-in denied: Outside allowed schedule hours.");
            Set(KeyMsgRemoteLock, "Time for a break. Computer will be locked by administrator shortly.");
            Set(KeyMsgBonusGranted, "Administrator has granted you +{minutes} minutes of screen time!");
        }
        else
        {
            Set(KeyMsgLimitWarn, "Sisa waktu layar harian Anda tinggal {minutes} menit lagi. Harap simpan semua pekerjaan Anda.");
            Set(KeyMsgCurfewWarn, "Waktu penggunaan yang diizinkan akan berakhir pada pukul {curfew_end} ({minutes} menit lagi). Harap simpan pekerjaan Anda.");
            Set(KeyMsgLimitReached, "Batas waktu layar harian Anda telah habis. Sesi Anda akan ditutup sekarang.");
            Set(KeyMsgCurfewReached, "Waktu penggunaan yang diizinkan telah berakhir (jam malam). Sesi Anda akan ditutup sekarang.");
            Set(KeyMsgLoginDeniedLimit, "Login ditolak: Batas waktu layar harian telah tercapai.");
            Set(KeyMsgLoginDeniedCurfew, "Login ditolak: Di luar jadwal jam yang diizinkan.");
            Set(KeyMsgRemoteLock, "Waktunya istirahat. Komputer akan segera dikunci oleh administrator.");
            Set(KeyMsgBonusGranted, "Administrator telah menambahkan +{minutes} menit waktu layar untuk Anda!");
        }
    }

    public static string GetMessage(string key, Dictionary<string, string>? placeholders = null)
    {
        var msg = Get(key);
        if (string.IsNullOrWhiteSpace(msg))
        {
            var currentPreset = Get(KeyLanguagePreset, "id");
            ApplyLanguagePreset(currentPreset);
            msg = Get(key);
        }

        if (placeholders != null && !string.IsNullOrWhiteSpace(msg))
        {
            foreach (var (k, v) in placeholders)
            {
                msg = msg.Replace($"{{{k}}}", v);
            }
        }

        return msg ?? string.Empty;
}
    }
