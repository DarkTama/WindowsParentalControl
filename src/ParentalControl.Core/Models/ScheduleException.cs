namespace ParentalControl.Core.Models;

public sealed class ScheduleException
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly ExceptionDate { get; set; }
    public int DailyMinutes { get; set; }
    public TimeOnly ScheduleStart { get; set; }
    public TimeOnly ScheduleEnd { get; set; }
    public DateTime CreatedAt { get; set; }
}
