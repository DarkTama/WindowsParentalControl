using Microsoft.Data.Sqlite;
using ParentalControl.Core.Models;

namespace ParentalControl.Core.Data;

public static class ScheduleExceptionRepository
{
    public static ScheduleException? GetForDate(int userId, DateOnly date)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_id, exception_date, daily_minutes, schedule_start, schedule_end, created_at
            FROM schedule_exceptions
            WHERE user_id = @userId AND exception_date = @date
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return ReadRecord(reader);
    }

    public static List<ScheduleException> GetUpcomingForUser(int userId, DateOnly fromDate)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_id, exception_date, daily_minutes, schedule_start, schedule_end, created_at
            FROM schedule_exceptions
            WHERE user_id = @userId AND exception_date >= @fromDate
            ORDER BY exception_date ASC
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@fromDate", fromDate.ToString("yyyy-MM-dd"));

        var list = new List<ScheduleException>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(ReadRecord(reader));
        }
        return list;
    }

    public static void Upsert(ScheduleException exc)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO schedule_exceptions (user_id, exception_date, daily_minutes, schedule_start, schedule_end, created_at)
            VALUES (@userId, @date, @minutes, @start, @end, @createdAt)
            ON CONFLICT(user_id, exception_date) DO UPDATE SET
                daily_minutes = @minutes,
                schedule_start = @start,
                schedule_end = @end
            """;
        cmd.Parameters.AddWithValue("@userId", exc.UserId);
        cmd.Parameters.AddWithValue("@date", exc.ExceptionDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@minutes", exc.DailyMinutes);
        cmd.Parameters.AddWithValue("@start", exc.ScheduleStart.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("@end", exc.ScheduleEnd.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("@createdAt", (exc.CreatedAt == default ? DateTime.Now : exc.CreatedAt).ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public static bool Delete(int id)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM schedule_exceptions WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public static bool DeleteForUserAndDate(int userId, DateOnly date)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM schedule_exceptions WHERE user_id = @userId AND exception_date = @date";
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
        return cmd.ExecuteNonQuery() > 0;
    }

    public static int DeleteOlderThan(DateOnly cutoffDate)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM schedule_exceptions WHERE exception_date < @cutoff";
        cmd.Parameters.AddWithValue("@cutoff", cutoffDate.ToString("yyyy-MM-dd"));
        return cmd.ExecuteNonQuery();
    }

    private static ScheduleException ReadRecord(SqliteDataReader reader)
    {
        return new ScheduleException
        {
            Id = reader.GetInt32(0),
            UserId = reader.GetInt32(1),
            ExceptionDate = DateOnly.Parse(reader.GetString(2)),
            DailyMinutes = reader.GetInt32(3),
            ScheduleStart = TimeOnly.Parse(reader.GetString(4)),
            ScheduleEnd = TimeOnly.Parse(reader.GetString(5)),
            CreatedAt = DateTime.Parse(reader.GetString(6))
        };
    }
}
