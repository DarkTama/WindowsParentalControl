using Microsoft.Data.Sqlite;
using ParentalControl.Core.Models;

namespace ParentalControl.Core.Data;

public static class GraceRequestRepository
{
    public static GraceRequest? GetTodayRequest(int userId, DateOnly date)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_id, date, requested_minutes, reason, status, created_at, resolved_at
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

    public static GraceRequest Create(int userId, DateOnly date, int requestedMinutes, string reason)
    {
        var now = DateTime.Now;
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO grace_requests (user_id, date, requested_minutes, reason, status, created_at)
            VALUES (@userId, @date, @minutes, @reason, 'PENDING', @createdAt);
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
            RequestedMinutes = requestedMinutes,
            Reason = reason,
            Status = "PENDING",
            CreatedAt = now
        };
    }

    public static void Resolve(int requestId, string status)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE grace_requests
            SET status = @status, resolved_at = @resolvedAt
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@status", status.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@resolvedAt", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("@id", requestId);
        cmd.ExecuteNonQuery();
    }

    public static GraceRequest? GetById(int id)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_id, date, requested_minutes, reason, status, created_at, resolved_at
            FROM grace_requests
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return ReadRecord(reader);
    }

    public static List<GraceRequest> GetRecentRequests(int limit = 50)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, user_id, date, requested_minutes, reason, status, created_at, resolved_at
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
            RequestedMinutes = reader.GetInt32(3),
            Reason = reader.GetString(4),
            Status = reader.GetString(5),
            CreatedAt = DateTime.Parse(reader.GetString(6)),
            ResolvedAt = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7))
        };
    }
}
