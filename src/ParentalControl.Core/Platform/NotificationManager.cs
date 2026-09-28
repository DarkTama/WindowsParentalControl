namespace ParentalControl.Core.Platform;

public static class NotificationManager
{
    public static bool SendMessage(int sessionId, string title, string message, bool isWarning = false, int timeoutSeconds = 20)
    {
        try
        {
            var style = isWarning
                ? NativeMethods.MB_ICONWARNING | NativeMethods.MB_OK
                : NativeMethods.MB_ICONINFORMATION | NativeMethods.MB_OK;

            var titleBytes = (title ?? string.Empty).Length * 2;
            var messageBytes = (message ?? string.Empty).Length * 2;

            return NativeMethods.WTSSendMessageW(
                NativeMethods.WTS_CURRENT_SERVER_HANDLE,
                sessionId,
                title ?? string.Empty,
                titleBytes,
                message ?? string.Empty,
                messageBytes,
                style,
                timeoutSeconds,
                out _,
                false);
        }
        catch
        {
            return false;
        }
    }

    public static void SendLogonBanner(int sessionId, string username, int allowedMinutes, TimeOnly start, TimeOnly end)
    {
        var hours = allowedMinutes / 60;
        var mins = allowedMinutes % 60;
        var timeStr = hours > 0 ? $"{hours}h {mins}m" : $"{mins}m";

        var title = "Parental Control — Session Active";
        var msg = $"Welcome, {username}!\n\n"
                + $"• Daily Allowance: {timeStr}\n"
                + $"• Permitted Hours: {start:HH:mm} – {end:HH:mm}\n\n"
                + "You will receive warning alerts before your session time expires.";

        SendMessage(sessionId, title, msg, isWarning: false, timeoutSeconds: 20);
    }

    public static void SendWarning(int sessionId, int minutesRemaining)
    {
        var title = "Parental Control — Time Alert";
        var msg = minutesRemaining <= 1
            ? "⚠️ FINAL WARNING: You have 1 MINUTE left!\nSave all work and games immediately. This session will be logged off."
            : $"⚠️ WARNING: You have {minutesRemaining} minutes of screen time remaining today.";

        SendMessage(sessionId, title, msg, isWarning: true, timeoutSeconds: 30);
    }

    public static void SendCurfewWarning(int sessionId, int minutesRemaining, TimeOnly curfewTime)
    {
        var title = "Parental Control — Schedule Curfew Alert";
        var msg = minutesRemaining <= 1
            ? $"⚠️ FINAL WARNING: Curfew at {curfewTime:HH:mm} is in 1 MINUTE!\nSave all work now. Your session will close."
            : $"⚠️ WARNING: Your allowed schedule ends at {curfewTime:HH:mm} ({minutesRemaining} minutes remaining).";

        SendMessage(sessionId, title, msg, isWarning: true, timeoutSeconds: 30);
    }
}
