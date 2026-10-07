namespace ParentalControl.Core.Models;

public sealed class SignInNotificationPayload
{
    public string Username { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public int RemainingMinutes { get; set; }
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
}

public sealed class SignOutNotificationPayload
{
    public string Username { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public int MinutesUsedToday { get; set; }
}

public sealed class AdminSignInPayload
{
    public int SessionId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
}

public sealed class PromptResponsePayload
{
    public int PromptId { get; set; }
    public int? SessionId { get; set; }
}
