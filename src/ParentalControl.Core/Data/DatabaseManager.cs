using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Data.Sqlite;

namespace ParentalControl.Core.Data;

public static class DatabaseManager
{
    private static string DataDirectory => Environment.GetEnvironmentVariable("PARENTAL_CONTROL_DATA_DIR") ?? @"C:\ProgramData\ParentalControl";
    private const string DatabaseFileName = "data.db";
    public const int RetentionDays = 30;
    public const int CapturesRetentionDays = 7;
    public const long MaxCapturesStorageBytes = 500L * 1024 * 1024; // 500 MB

    public static string DatabasePath => Path.Combine(DataDirectory, DatabaseFileName);
    public static string CapturesDirectory => Path.Combine(DataDirectory, "captures");

    public static string ConnectionString => $"Data Source={DatabasePath}";

    public static void Initialize()
    {
        var dirInfo = new DirectoryInfo(DataDirectory);
        if (!dirInfo.Exists)
        {
            Directory.CreateDirectory(DataDirectory);
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PARENTAL_CONTROL_DATA_DIR")))
            {
                try
                {
                    var security = dirInfo.GetAccessControl();
                    security.SetAccessRuleProtection(true, false);
                    security.AddAccessRule(new FileSystemAccessRule(
                        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));
                    security.AddAccessRule(new FileSystemAccessRule(
                        new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));
                    dirInfo.SetAccessControl(security);
                }
                catch
                {
                    // Best-effort ACL for environments without elevation
                }
            }
        }

        if (!Directory.Exists(CapturesDirectory))
        {
            Directory.CreateDirectory(CapturesDirectory);
        }

        using var connection = CreateConnection();

        using var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA journal_mode=WAL;";
        pragmaCmd.ExecuteNonQuery();

        using var schemaCmd = connection.CreateCommand();
        schemaCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                sid TEXT NOT NULL UNIQUE,
                username TEXT NOT NULL,
                is_restricted INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS limits (
                user_id INTEGER PRIMARY KEY,
                daily_minutes INTEGER NOT NULL DEFAULT 120,
                schedule_start TEXT NOT NULL DEFAULT '08:00',
                schedule_end TEXT NOT NULL DEFAULT '22:00',
                FOREIGN KEY (user_id) REFERENCES users(id)
            );

            CREATE TABLE IF NOT EXISTS usage (
                user_id INTEGER NOT NULL,
                date TEXT NOT NULL,
                minutes_used INTEGER NOT NULL DEFAULT 0,
                bonus_minutes INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (user_id, date),
                FOREIGN KEY (user_id) REFERENCES users(id)
            );

            CREATE TABLE IF NOT EXISTS schedule_days (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                day_of_week INTEGER NOT NULL,
                daily_minutes INTEGER NOT NULL DEFAULT 120,
                schedule_start TEXT NOT NULL DEFAULT '08:00',
                schedule_end TEXT NOT NULL DEFAULT '22:00',
                UNIQUE(user_id, day_of_week),
                FOREIGN KEY (user_id) REFERENCES users(id)
            );

            CREATE TABLE IF NOT EXISTS grace_requests (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                date TEXT NOT NULL,
                requested_minutes INTEGER NOT NULL,
                reason TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL DEFAULT 'PENDING',
                created_at TEXT NOT NULL,
                resolved_at TEXT,
                FOREIGN KEY (user_id) REFERENCES users(id)
            );

            CREATE TABLE IF NOT EXISTS app_usage (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                date TEXT NOT NULL,
                process_name TEXT NOT NULL,
                window_title TEXT NOT NULL DEFAULT '',
                minutes INTEGER NOT NULL DEFAULT 0,
                UNIQUE(user_id, date, process_name),
                FOREIGN KEY (user_id) REFERENCES users(id)
            );

            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS app_activity_hourly (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                date TEXT NOT NULL,
                hour INTEGER NOT NULL,
                process_name TEXT NOT NULL,
                minutes INTEGER NOT NULL DEFAULT 0,
                UNIQUE(user_id, date, hour, process_name),
                FOREIGN KEY (user_id) REFERENCES users(id)
            );

            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                user_sid TEXT NOT NULL,
                event_type TEXT NOT NULL,
                details TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS screen_captures (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL,
                timestamp TEXT NOT NULL,
                file_path TEXT NOT NULL,
                width INTEGER NOT NULL,
                height INTEGER NOT NULL,
                file_size_bytes INTEGER NOT NULL,
                trigger_type TEXT NOT NULL DEFAULT 'MANUAL',
                admin_ip TEXT,
                FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_events_timestamp ON events (timestamp);
            CREATE INDEX IF NOT EXISTS idx_events_user_timestamp ON events (user_sid, timestamp);
            CREATE INDEX IF NOT EXISTS idx_grace_user_date ON grace_requests (user_id, date);
            CREATE INDEX IF NOT EXISTS idx_app_usage_user_date ON app_usage (user_id, date);
            CREATE INDEX IF NOT EXISTS idx_app_activity_hourly_date ON app_activity_hourly (user_id, date);
            CREATE INDEX IF NOT EXISTS idx_screen_captures_user_time ON screen_captures (user_id, timestamp);
            """;
        schemaCmd.ExecuteNonQuery();

        // Migration: check if bonus_minutes exists in existing usage table
        using var checkColCmd = connection.CreateCommand();
        checkColCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('usage') WHERE name = 'bonus_minutes';";
        var count = Convert.ToInt32(checkColCmd.ExecuteScalar());
        if (count == 0)
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE usage ADD COLUMN bonus_minutes INTEGER NOT NULL DEFAULT 0;";
            alterCmd.ExecuteNonQuery();
        }
    }

    public static SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA busy_timeout=5000;";
        pragmaCmd.ExecuteNonQuery();

        return connection;
    }
}
