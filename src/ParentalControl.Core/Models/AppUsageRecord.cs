namespace ParentalControl.Core.Models;

public sealed class AppUsageRecord
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public int Minutes { get; set; }
}
