using Microsoft.Data.Sqlite;
using ParentalControl.Core.Models;

namespace ParentalControl.Core.Data;

public static class AppUsageRepository
{
    public static void AddMinutes(int userId, DateOnly date, string processName, string windowTitle, int minutes)
    {
        if (string.IsNullOrWhiteSpace(processName) || minutes <= 0) return;

        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO app_usage (user_id, date, process_name, window_title, minutes)
            VALUES (@userId, @date, @process, @title, @minutes)
            ON CONFLICT(user_id, date, process_name) DO UPDATE SET
                minutes = minutes + @minutes,
                window_title = @title
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@process", processName);
        cmd.Parameters.AddWithValue("@title", windowTitle ?? string.Empty);
        cmd.Parameters.AddWithValue("@minutes", minutes);
        cmd.ExecuteNonQuery();
    }

    public static List<AppUsageRecord> GetForUserAndDate(int userId, DateOnly date)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_id, date, process_name, window_title, minutes
            FROM app_usage
            WHERE user_id = @userId AND date = @date
            ORDER BY minutes DESC
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));

        var list = new List<AppUsageRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new AppUsageRecord
            {
                Id = reader.GetInt32(0),
                UserId = reader.GetInt32(1),
                Date = DateOnly.Parse(reader.GetString(2)),
                ProcessName = reader.GetString(3),
                WindowTitle = reader.GetString(4),
                Minutes = reader.GetInt32(5)
            });
        }
        return list;
    }

    public static List<AppUsageRecord> GetTopApps(int userId, DateOnly from, DateOnly to, int limit = 10)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT user_id, process_name, MAX(window_title), SUM(minutes) as total_minutes
            FROM app_usage
            WHERE user_id = @userId AND date >= @from AND date <= @to
            GROUP BY user_id, process_name
            ORDER BY total_minutes DESC
            LIMIT {limit}
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

        var list = new List<AppUsageRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new AppUsageRecord
            {
                UserId = reader.GetInt32(0),
                ProcessName = reader.GetString(1),
                WindowTitle = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Minutes = Convert.ToInt32(reader.GetInt64(3)),
                Date = to
            });
        }
        return list;
    }

    public static int DeleteOlderThan(DateOnly cutoff)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM app_usage WHERE date < @cutoff";
        cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("yyyy-MM-dd"));
        return cmd.ExecuteNonQuery();
    }
}
