namespace ParentalControl.Core.Models;

public sealed class GraceRequest
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public string RequestType { get; set; } = "extension"; // "extension" or "schedule_change"
    public DateOnly? TargetDate { get; set; }
    public int RequestedMinutes { get; set; }
    public TimeOnly? RequestedStart { get; set; }
    public TimeOnly? RequestedEnd { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING"; // PENDING, APPROVED, DECLINED
    public string? DeclineReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
