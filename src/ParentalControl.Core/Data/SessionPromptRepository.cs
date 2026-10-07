using System.Globalization;
using Microsoft.Data.Sqlite;
using ParentalControl.Core.Models;

namespace ParentalControl.Core.Data;

public static class SessionPromptRepository
{
    public static int Create(string userSid, string username, string message, string urgency = "NORMAL", int targetDisplay = -1, string? customOptions = null)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO session_prompts (user_sid, username, message, urgency, target_display, custom_options, status, turnaround_seconds, created_at)
            VALUES (@userSid, @username, @message, @urgency, @targetDisplay, @customOptions, 'PENDING', 0, @createdAt);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@userSid", userSid);
        cmd.Parameters.AddWithValue("@username", username);
        cmd.Parameters.AddWithValue("@message", message);
        cmd.Parameters.AddWithValue("@urgency", urgency);
        cmd.Parameters.AddWithValue("@targetDisplay", targetDisplay);
        cmd.Parameters.AddWithValue("@customOptions", (object?)customOptions ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture));

        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static SessionPrompt? GetById(int id)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_sid, username, message, urgency, target_display, custom_options, status, response, response_reason, turnaround_seconds, created_at, answered_at
            FROM session_prompts WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return ReadPrompt(reader);
        }
        return null;
    }

    public static SessionPrompt? GetActivePrompt(string username)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_sid, username, message, urgency, target_display, custom_options, status, response, response_reason, turnaround_seconds, created_at, answered_at
            FROM session_prompts
            WHERE username = @username AND status = 'PENDING'
            ORDER BY id DESC LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@username", username);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var prompt = ReadPrompt(reader);
            // If prompt was created more than 5 minutes ago, auto-expire it
            if ((DateTime.Now - prompt.CreatedAt).TotalMinutes > 5)
            {
                ResolvePrompt(prompt.Id, "TIMEOUT", "Prompt expired after 5 minutes", 300);
                return null;
            }
            return prompt;
        }
        return null;
    }

    public static bool ResolvePrompt(int id, string response, string? responseReason, int turnaroundSeconds)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE session_prompts
            SET status = @status, response = @response, response_reason = @reason, turnaround_seconds = @turnaround, answered_at = @answeredAt
            WHERE id = @id AND status = 'PENDING'
            """;
        var status = response.Equals("TIMEOUT", StringComparison.OrdinalIgnoreCase) ? "TIMEOUT" : "ANSWERED";
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@response", response);
        cmd.Parameters.AddWithValue("@reason", (object?)responseReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@turnaround", turnaroundSeconds);
        cmd.Parameters.AddWithValue("@answeredAt", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@id", id);

        return cmd.ExecuteNonQuery() > 0;
    }

    public static bool DismissPrompt(int id)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE session_prompts SET status = 'DISMISSED' WHERE id = @id AND status = 'PENDING'";
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    public static List<SessionPrompt> GetHistory(int limit = 50)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, user_sid, username, message, urgency, target_display, custom_options, status, response, response_reason, turnaround_seconds, created_at, answered_at
            FROM session_prompts
            ORDER BY id DESC LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@limit", limit);

        var list = new List<SessionPrompt>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(ReadPrompt(reader));
        }
        return list;
    }

    private static SessionPrompt ReadPrompt(SqliteDataReader reader)
    {
        return new SessionPrompt
        {
            Id = reader.GetInt32(0),
            UserSid = reader.GetString(1),
            Username = reader.GetString(2),
            Message = reader.GetString(3),
            Urgency = reader.GetString(4),
            TargetDisplay = reader.GetInt32(5),
            CustomOptions = reader.IsDBNull(6) ? null : reader.GetString(6),
            Status = reader.GetString(7),
            Response = reader.IsDBNull(8) ? null : reader.GetString(8),
            ResponseReason = reader.IsDBNull(9) ? null : reader.GetString(9),
            TurnaroundSeconds = reader.GetInt32(10),
            CreatedAt = DateTime.Parse(reader.GetString(11), CultureInfo.InvariantCulture),
            AnsweredAt = reader.IsDBNull(12) ? null : DateTime.Parse(reader.GetString(12), CultureInfo.InvariantCulture)
        };
    }
}
