using Microsoft.Data.Sqlite;
using ParentalControl.Core.Models;
using Serilog;

namespace ParentalControl.Core.Data;

public static class ScreenCaptureRepository
{
    private static readonly ILogger _logger = Log.ForContext(typeof(ScreenCaptureRepository));

    public static ScreenCaptureRecord Add(ScreenCaptureRecord record)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO screen_captures (user_id, timestamp, file_path, width, height, file_size_bytes, trigger_type, admin_ip)
            VALUES (@userId, @timestamp, @filePath, @width, @height, @fileSizeBytes, @triggerType, @adminIp);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@userId", record.UserId);
        cmd.Parameters.AddWithValue("@timestamp", record.Timestamp.ToString("o"));
        cmd.Parameters.AddWithValue("@filePath", record.FilePath);
        cmd.Parameters.AddWithValue("@width", record.Width);
        cmd.Parameters.AddWithValue("@height", record.Height);
        cmd.Parameters.AddWithValue("@fileSizeBytes", record.FileSizeBytes);
        cmd.Parameters.AddWithValue("@triggerType", record.TriggerType);
        cmd.Parameters.AddWithValue("@adminIp", (object?)record.AdminIp ?? DBNull.Value);

        record.Id = Convert.ToInt32(cmd.ExecuteScalar());
        return record;
    }

    public static ScreenCaptureRecord? GetById(int id)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, timestamp, file_path, width, height, file_size_bytes, trigger_type, admin_ip FROM screen_captures WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return ReadRecord(reader);
        }
        return null;
    }

    public static ScreenCaptureRecord? GetLatestByUser(int userId)
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, timestamp, file_path, width, height, file_size_bytes, trigger_type, admin_ip FROM screen_captures WHERE user_id = @userId ORDER BY timestamp DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("@userId", userId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return ReadRecord(reader);
        }
        return null;
    }

    public static List<ScreenCaptureRecord> GetRecentByUser(int userId, int limit = 12)
    {
        var list = new List<ScreenCaptureRecord>();
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, user_id, timestamp, file_path, width, height, file_size_bytes, trigger_type, admin_ip FROM screen_captures WHERE user_id = @userId ORDER BY timestamp DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(ReadRecord(reader));
        }
        return list;
    }

    public static bool Delete(int id)
    {
        var record = GetById(id);
        if (record == null) return false;

        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM screen_captures WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();

        try
        {
            if (File.Exists(record.FilePath))
            {
                File.Delete(record.FilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to delete capture file {Path}", record.FilePath);
        }
        return true;
    }

    public static int PruneOldCaptures(int retentionDays = 7, long maxTotalBytes = 500L * 1024 * 1024)
    {
        int deletedCount = 0;
        var cutoff = DateTime.Now.AddDays(-retentionDays);

        // 1. Prune by retention days
        using (var connection = DatabaseManager.CreateConnection())
        {
            var expiredFiles = new List<(int Id, string Path)>();
            using (var selectCmd = connection.CreateCommand())
            {
                selectCmd.CommandText = "SELECT id, file_path FROM screen_captures WHERE timestamp < @cutoff;";
                selectCmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("o"));
                using var reader = selectCmd.ExecuteReader();
                while (reader.Read())
                {
                    expiredFiles.Add((reader.GetInt32(0), reader.GetString(1)));
                }
            }

            foreach (var item in expiredFiles)
            {
                using var delCmd = connection.CreateCommand();
                delCmd.CommandText = "DELETE FROM screen_captures WHERE id = @id;";
                delCmd.Parameters.AddWithValue("@id", item.Id);
                delCmd.ExecuteNonQuery();

                try
                {
                    if (File.Exists(item.Path))
                    {
                        File.Delete(item.Path);
                    }
                }
                catch { }
                deletedCount++;
            }
        }

        // 2. Prune by size cap if total bytes exceed limit
        try
        {
            long totalBytes = GetTotalStorageBytes();
            if (totalBytes > maxTotalBytes)
            {
                using var connection = DatabaseManager.CreateConnection();
                var oldestFiles = new List<(int Id, string Path, long Size)>();
                using (var selectOldestCmd = connection.CreateCommand())
                {
                    selectOldestCmd.CommandText = "SELECT id, file_path, file_size_bytes FROM screen_captures ORDER BY timestamp ASC;";
                    using var reader = selectOldestCmd.ExecuteReader();
                    while (reader.Read())
                    {
                        oldestFiles.Add((reader.GetInt32(0), reader.GetString(1), reader.GetInt64(2)));
                    }
                }

                long targetCap = (long)(maxTotalBytes * 0.8); // prune down to 80% of max
                foreach (var item in oldestFiles)
                {
                    if (totalBytes <= targetCap) break;

                    using var delCmd = connection.CreateCommand();
                    delCmd.CommandText = "DELETE FROM screen_captures WHERE id = @id;";
                    delCmd.Parameters.AddWithValue("@id", item.Id);
                    delCmd.ExecuteNonQuery();

                    try
                    {
                        if (File.Exists(item.Path))
                        {
                            File.Delete(item.Path);
                        }
                    }
                    catch { }

                    totalBytes -= item.Size;
                    deletedCount++;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error while checking capture storage size limits");
        }

        return deletedCount;
    }

    public static long GetTotalStorageBytes()
    {
        using var connection = DatabaseManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(file_size_bytes), 0) FROM screen_captures;";
        var val = cmd.ExecuteScalar();
        return Convert.ToInt64(val);
    }

    private static ScreenCaptureRecord ReadRecord(SqliteDataReader reader)
    {
        return new ScreenCaptureRecord
        {
            Id = reader.GetInt32(0),
            UserId = reader.GetInt32(1),
            Timestamp = DateTime.Parse(reader.GetString(2)),
            FilePath = reader.GetString(3),
            Width = reader.GetInt32(4),
            Height = reader.GetInt32(5),
            FileSizeBytes = reader.GetInt64(6),
            TriggerType = reader.GetString(7),
            AdminIp = reader.IsDBNull(8) ? null : reader.GetString(8)
        };
    }
}
