using ParentalControl.Core.Data;
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
        var timeStr = hours > 0 ? $"{hours} jam {mins} menit" : $"{mins} menit";

        var title = "Parental Control — Sesi Aktif";
        var msg = $"Halo, {username}!\n\n"
                + $"• Jatah Waktu Layar Hari Ini: {timeStr}\n"
                + $"• Jam Yang Diizinkan: {start:HH:mm} – {end:HH:mm}\n\n"
                + "Anda akan menerima peringatan otomatis sebelum waktu sesi habis.";

        SendMessage(sessionId, title, msg, isWarning: false, timeoutSeconds: 20);
    }

    public static void SendWarning(int sessionId, int minutesRemaining)
    {
        var title = "Parental Control — Screen Time Warning";
        var msg = SettingsRepository.GetMessage(SettingsRepository.KeyMsgLimitWarn, new Dictionary<string, string>
        {
            ["minutes"] = minutesRemaining.ToString()
        });

        SendMessage(sessionId, title, msg, isWarning: true, timeoutSeconds: 30);
    }

    public static void SendCurfewWarning(int sessionId, int minutesRemaining, TimeOnly curfewTime)
    {
        var title = "Parental Control — Curfew Warning";
        var msg = SettingsRepository.GetMessage(SettingsRepository.KeyMsgCurfewWarn, new Dictionary<string, string>
        {
            ["minutes"] = minutesRemaining.ToString(),
            ["curfew_end"] = curfewTime.ToString("HH:mm")
        });

        SendMessage(sessionId, title, msg, isWarning: true, timeoutSeconds: 30);
    }
}
