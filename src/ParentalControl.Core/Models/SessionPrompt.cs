namespace ParentalControl.Core.Models;

public sealed class SessionPrompt
{
    public int Id { get; set; }
    public string UserSid { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Urgency { get; set; } = "NORMAL";
    public int TargetDisplay { get; set; } = -1; // -1 = Auto, 0 = Monitor 1, 1 = Monitor 2
    public string? CustomOptions { get; set; }
    public string Status { get; set; } = "PENDING"; // PENDING, ANSWERED, TIMEOUT, DISMISSED
    public string? Response { get; set; } // YES, NO, TIMEOUT
    public string? ResponseReason { get; set; }
    public int TurnaroundSeconds { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AnsweredAt { get; set; }
}
