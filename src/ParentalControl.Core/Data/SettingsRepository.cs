using Microsoft.Data.Sqlite;

namespace ParentalControl.Core.Data;

public static class SettingsRepository
{
    public const string KeyTelegramBotToken = "telegram_bot_token";
    public const string KeyTelegramChatId = "telegram_chat_id";
    public const string KeyTotpSecret = "totp_secret";
    public const string KeyTotpEnabled = "totp_enabled";
    public const string KeyAlertIntervals = "alert_intervals"; // e.g. "15,5,1"

    public static string Get(string key, string defaultValue = "")
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_settings WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);

        var result = cmd.ExecuteScalar();
        return result?.ToString() ?? defaultValue;
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
}
