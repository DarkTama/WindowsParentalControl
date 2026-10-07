using System.Globalization;
using Microsoft.Data.Sqlite;
using ParentalControl.Core.Models;

namespace ParentalControl.Core.Data;

public static class PendingNotificationRepository
{
    public static void Enqueue(string notificationType, string payloadJson, DateTime eventTime)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO pending_telegram_queue (notification_type, payload_json, event_time, retry_count, created_at)
            VALUES (@type, @payload, @eventTime, 0, @createdAt)
            """;
        cmd.Parameters.AddWithValue("@type", notificationType);
        cmd.Parameters.AddWithValue("@payload", payloadJson);
        cmd.Parameters.AddWithValue("@eventTime", eventTime.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    public static List<PendingNotification> GetPending(int limit = 10)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, notification_type, payload_json, event_time, retry_count, created_at FROM pending_telegram_queue ORDER BY id ASC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        var list = new List<PendingNotification>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new PendingNotification
            {
                Id = reader.GetInt32(0),
                NotificationType = reader.GetString(1),
                PayloadJson = reader.GetString(2),
                EventTime = DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                RetryCount = reader.GetInt32(4),
                CreatedAt = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture)
            });
        }
        return list;
    }

    public static void IncrementRetry(int id)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE pending_telegram_queue SET retry_count = retry_count + 1 WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public static void Delete(int id)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM pending_telegram_queue WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public static int GetCount()
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pending_telegram_queue";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
