namespace ParentalControl.Core.Models;

public sealed class PendingNotification
{
    public int Id { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime EventTime { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
