using Microsoft.Data.Sqlite;
using ParentalControl.Core.Models;

namespace ParentalControl.Core.Data;

public static class ScheduleRepository
{
    public static LimitConfig? GetEffectiveLimit(int userId, DayOfWeek day, DateOnly? date = null)
    {
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.Now);

        // 1. Highest priority: single-day schedule exception
        var exc = ScheduleExceptionRepository.GetForDate(userId, targetDate);
        if (exc != null)
        {
            return new LimitConfig
            {
                UserId = userId,
                DailyMinutes = exc.DailyMinutes,
                ScheduleStart = exc.ScheduleStart,
                ScheduleEnd = exc.ScheduleEnd
            };
        }

        // 2. Weekly schedule override
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT daily_minutes, schedule_start, schedule_end
            FROM schedule_days
            WHERE user_id = @userId AND day_of_week = @day
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@day", (int)day);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new LimitConfig
            {
                UserId = userId,
                DailyMinutes = reader.GetInt32(0),
                ScheduleStart = TimeOnly.Parse(reader.GetString(1)),
                ScheduleEnd = TimeOnly.Parse(reader.GetString(2))
            };
        }

        // 3. Fallback to general baseline limit
        return LimitRepository.GetByUserId(userId);
    }

    public static List<DaySchedule> GetWeeklySchedule(int userId)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_id, day_of_week, daily_minutes, schedule_start, schedule_end
            FROM schedule_days
            WHERE user_id = @userId
            ORDER BY day_of_week
            """;
        cmd.Parameters.AddWithValue("@userId", userId);

        var list = new List<DaySchedule>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new DaySchedule
            {
                Id = reader.GetInt32(0),
                UserId = reader.GetInt32(1),
                DayOfWeek = (DayOfWeek)reader.GetInt32(2),
                DailyMinutes = reader.GetInt32(3),
                ScheduleStart = TimeOnly.Parse(reader.GetString(4)),
                ScheduleEnd = TimeOnly.Parse(reader.GetString(5))
            });
        }
        return list;
    }

    public static void SaveDaySchedule(DaySchedule schedule)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO schedule_days (user_id, day_of_week, daily_minutes, schedule_start, schedule_end)
            VALUES (@userId, @day, @minutes, @start, @end)
            ON CONFLICT(user_id, day_of_week) DO UPDATE SET
                daily_minutes = @minutes,
                schedule_start = @start,
                schedule_end = @end
            """;
        cmd.Parameters.AddWithValue("@userId", schedule.UserId);
        cmd.Parameters.AddWithValue("@day", (int)schedule.DayOfWeek);
        cmd.Parameters.AddWithValue("@minutes", schedule.DailyMinutes);
        cmd.Parameters.AddWithValue("@start", schedule.ScheduleStart.ToString("HH:mm"));
        cmd.Parameters.AddWithValue("@end", schedule.ScheduleEnd.ToString("HH:mm"));
        cmd.ExecuteNonQuery();
    }

    public static void DeleteDaySchedule(int userId, DayOfWeek day)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM schedule_days WHERE user_id = @userId AND day_of_week = @day";
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@day", (int)day);
        cmd.ExecuteNonQuery();
    }

    public static void DeleteForUser(int userId)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM schedule_days WHERE user_id = @userId";
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.ExecuteNonQuery();
    }
}
