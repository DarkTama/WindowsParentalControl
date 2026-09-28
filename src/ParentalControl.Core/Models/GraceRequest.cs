namespace ParentalControl.Core.Models;

public sealed class GraceRequest
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public int RequestedMinutes { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING"; // PENDING, APPROVED, DECLINED
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
