using Microsoft.Data.Sqlite;
using ParentalControl.Core.Models;

namespace ParentalControl.Core.Data;

public static class AppUsageRepository
{
    private sealed class ActivityAccumulator
    {
        public int Seconds;
        public DateOnly LastDate;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int UserId, string ProcessName), ActivityAccumulator> _accumulators = new();

    public static void ResetAccumulators()
    {
        _accumulators.Clear();
    }

    public static void AddSeconds(int userId, DateOnly date, string processName, string windowTitle, int seconds, int hour = -1)
    {
        if (string.IsNullOrWhiteSpace(processName) || seconds <= 0) return;

        var key = (userId, processName.ToLowerInvariant());
        var acc = _accumulators.GetOrAdd(key, _ => new ActivityAccumulator());
        int minutesToAdd = 0;
        lock (acc)
        {
            if (acc.LastDate != date)
            {
                acc.LastDate = date;
                acc.Seconds = 0;
            }

            acc.Seconds += seconds;
            if (acc.Seconds >= 60)
            {
                minutesToAdd = acc.Seconds / 60;
                acc.Seconds %= 60;
            }
        }

        if (minutesToAdd > 0)
        {
            AddMinutes(userId, date, processName, windowTitle, minutesToAdd, hour);
        }
    }

    public static void AddMinutes(int userId, DateOnly date, string processName, string windowTitle, int minutes, int hour = -1)
    {
        if (string.IsNullOrWhiteSpace(processName) || minutes <= 0) return;
        if (hour < 0 || hour > 23) hour = DateTime.Now.Hour;

        using var connection = DatabaseManager.CreateConnection();
        using var transaction = connection.BeginTransaction();

        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
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

        using (var hourlyCmd = connection.CreateCommand())
        {
            hourlyCmd.Transaction = transaction;
            hourlyCmd.CommandText = """
                INSERT INTO app_activity_hourly (user_id, date, hour, process_name, minutes)
                VALUES (@userId, @date, @hour, @process, @minutes)
                ON CONFLICT(user_id, date, hour, process_name) DO UPDATE SET
                    minutes = minutes + @minutes
                """;
            hourlyCmd.Parameters.AddWithValue("@userId", userId);
            hourlyCmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
            hourlyCmd.Parameters.AddWithValue("@hour", hour);
            hourlyCmd.Parameters.AddWithValue("@process", processName);
            hourlyCmd.Parameters.AddWithValue("@minutes", minutes);
            hourlyCmd.ExecuteNonQuery();
        }

        transaction.Commit();
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

    public static List<AppUsageRecord> GetUsageForRange(int? userId, DateOnly from, DateOnly to)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();

        if (userId.HasValue)
        {
            cmd.CommandText = """
                SELECT user_id, process_name, MAX(window_title), SUM(minutes) as total_minutes
                FROM app_usage
                WHERE user_id = @userId AND date >= @from AND date <= @to
                GROUP BY user_id, process_name
                ORDER BY total_minutes DESC
                """;
            cmd.Parameters.AddWithValue("@userId", userId.Value);
        }
        else
        {
            cmd.CommandText = """
                SELECT user_id, process_name, MAX(window_title), SUM(minutes) as total_minutes
                FROM app_usage
                WHERE date >= @from AND date <= @to
                GROUP BY user_id, process_name
                ORDER BY total_minutes DESC
                """;
        }

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

    public static int[] GetHourlyDistribution(int? userId, DateOnly from, DateOnly to)
    {
        var distribution = new int[24];
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();

        if (userId.HasValue)
        {
            cmd.CommandText = """
                SELECT hour, SUM(minutes)
                FROM app_activity_hourly
                WHERE user_id = @userId AND date >= @from AND date <= @to
                GROUP BY hour
                """;
            cmd.Parameters.AddWithValue("@userId", userId.Value);
        }
        else
        {
            cmd.CommandText = """
                SELECT hour, SUM(minutes)
                FROM app_activity_hourly
                WHERE date >= @from AND date <= @to
                GROUP BY hour
                """;
        }

        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var h = reader.GetInt32(0);
            var m = Convert.ToInt32(reader.GetInt64(1));
            if (h >= 0 && h < 24)
            {
                var maxForSlot = Math.Max(60, (to.DayNumber - from.DayNumber + 1) * 60);
                distribution[h] = Math.Clamp(m, 0, maxForSlot);
            }
        }
        return distribution;
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
        using var transaction = connection.BeginTransaction();

        using var cmd1 = connection.CreateCommand();
        cmd1.Transaction = transaction;
        cmd1.CommandText = "DELETE FROM app_usage WHERE date < @cutoff";
        cmd1.Parameters.AddWithValue("@cutoff", cutoff.ToString("yyyy-MM-dd"));
        var count = cmd1.ExecuteNonQuery();

        using var cmd2 = connection.CreateCommand();
        cmd2.Transaction = transaction;
        cmd2.CommandText = "DELETE FROM app_activity_hourly WHERE date < @cutoff";
        cmd2.Parameters.AddWithValue("@cutoff", cutoff.ToString("yyyy-MM-dd"));
        cmd2.ExecuteNonQuery();

        transaction.Commit();
        return count;
    }
}
