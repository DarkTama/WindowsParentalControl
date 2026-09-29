using Microsoft.Data.Sqlite;
using ParentalControl.Core.Models;

namespace ParentalControl.Core.Data;

public static class GraceRequestRepository
{
    private const string SelectColumns = """
        id, user_id, date, request_type, target_date, requested_minutes, requested_start, requested_end, reason, status, decline_reason, created_at, resolved_at
        """;

    public static GraceRequest? GetTodayRequest(int userId, DateOnly date)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectColumns}
            FROM grace_requests
            WHERE user_id = @userId AND date = @date
            ORDER BY id DESC LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return ReadRecord(reader);
    }

    public static int GetTodayRequestCount(int userId, DateOnly date)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM grace_requests
            WHERE user_id = @userId AND date = @date
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static bool HasPendingRequest(int userId, DateOnly date)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM grace_requests
            WHERE user_id = @userId AND (date = @date OR status = 'PENDING') AND status = 'PENDING'
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public static GraceRequest Create(int userId, DateOnly date, int requestedMinutes, string reason)
    {
        var now = DateTime.Now;
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO grace_requests (user_id, date, request_type, requested_minutes, reason, status, created_at)
            VALUES (@userId, @date, 'extension', @minutes, @reason, 'PENDING', @createdAt);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@minutes", requestedMinutes);
        cmd.Parameters.AddWithValue("@reason", reason);
        cmd.Parameters.AddWithValue("@createdAt", now.ToString("o"));

        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return new GraceRequest
        {
            Id = id,
            UserId = userId,
            Date = date,
            RequestType = "extension",
            RequestedMinutes = requestedMinutes,
            Reason = reason,
            Status = "PENDING",
            CreatedAt = now
        };
    }

    public static GraceRequest CreateScheduleChange(
        int userId,
        DateOnly date,
        DateOnly targetDate,
        int requestedMinutes,
        TimeOnly? requestedStart,
        TimeOnly? requestedEnd,
        string reason)
    {
        var now = DateTime.Now;
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO grace_requests (user_id, date, request_type, target_date, requested_minutes, requested_start, requested_end, reason, status, created_at)
            VALUES (@userId, @date, 'schedule_change', @targetDate, @minutes, @start, @end, @reason, 'PENDING', @createdAt);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@targetDate", targetDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@minutes", requestedMinutes);
        cmd.Parameters.AddWithValue("@start", requestedStart?.ToString("HH:mm") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@end", requestedEnd?.ToString("HH:mm") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@reason", reason);
        cmd.Parameters.AddWithValue("@createdAt", now.ToString("o"));

        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return new GraceRequest
        {
            Id = id,
            UserId = userId,
            Date = date,
            RequestType = "schedule_change",
            TargetDate = targetDate,
            RequestedMinutes = requestedMinutes,
            RequestedStart = requestedStart,
            RequestedEnd = requestedEnd,
            Reason = reason,
            Status = "PENDING",
            CreatedAt = now
        };
    }

    public static void Resolve(int requestId, string status, string? declineReason = null)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE grace_requests
            SET status = @status,
                decline_reason = COALESCE(@declineReason, decline_reason),
                resolved_at = @resolvedAt
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@status", status.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@declineReason", declineReason ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@resolvedAt", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("@id", requestId);
        cmd.ExecuteNonQuery();
    }

    public static void SetDeclineReason(int requestId, string declineReason)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE grace_requests
            SET decline_reason = @declineReason
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@declineReason", declineReason);
        cmd.Parameters.AddWithValue("@id", requestId);
        cmd.ExecuteNonQuery();
    }

    public static GraceRequest? GetById(int id)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectColumns}
            FROM grace_requests
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return ReadRecord(reader);
    }

    public static GraceRequest? GetLatestRequest(int userId)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectColumns}
            FROM grace_requests
            WHERE user_id = @userId
            ORDER BY id DESC LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@userId", userId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return ReadRecord(reader);
    }

    public static List<GraceRequest> GetRecentRequests(int limit = 50)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectColumns}
            FROM grace_requests
            ORDER BY id DESC LIMIT {limit}
            """;

        var list = new List<GraceRequest>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(ReadRecord(reader));
        }
        return list;
    }

    private static GraceRequest ReadRecord(SqliteDataReader reader)
    {
        return new GraceRequest
        {
            Id = reader.GetInt32(0),
            UserId = reader.GetInt32(1),
            Date = DateOnly.Parse(reader.GetString(2)),
            RequestType = reader.IsDBNull(3) ? "extension" : reader.GetString(3),
            TargetDate = reader.IsDBNull(4) ? null : DateOnly.Parse(reader.GetString(4)),
            RequestedMinutes = reader.GetInt32(5),
            RequestedStart = reader.IsDBNull(6) ? null : TimeOnly.Parse(reader.GetString(6)),
            RequestedEnd = reader.IsDBNull(7) ? null : TimeOnly.Parse(reader.GetString(7)),
            Reason = reader.GetString(8),
            Status = reader.GetString(9),
            DeclineReason = reader.IsDBNull(10) ? null : reader.GetString(10),
            CreatedAt = DateTime.Parse(reader.GetString(11)),
            ResolvedAt = reader.IsDBNull(12) ? null : DateTime.Parse(reader.GetString(12))
        };
    }
}
