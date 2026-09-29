namespace ParentalControl.Core.Models;

public sealed class ScreenCaptureRecord
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime Timestamp { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSizeBytes { get; set; }
    public string TriggerType { get; set; } = "MANUAL";
    public string? AdminIp { get; set; }
}
