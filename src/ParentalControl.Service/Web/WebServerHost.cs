using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.Core.Platform;
using ParentalControl.Core.Security;
using ParentalControl.Core.Services;
using ParentalControl.Service.Web.Pages;
using ParentalControl.Core;

namespace ParentalControl.Service.Web;

public sealed class WebServerHost : BackgroundService
{
    private readonly SessionTracker _sessionTracker;
    private readonly TelegramBotService _telegramBotService;
    private readonly Serilog.ILogger _logger;
    private readonly HashSet<string> _validAdminTokens = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string ProcessName, string WindowTitle, DateTime LastSeen)> _liveActivities = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CaptureCommandState> _captureStates = new(StringComparer.OrdinalIgnoreCase);
    public static event Action<ScreenCaptureRecord>? OnCaptureReceived;
    public WebServerHost(SessionTracker sessionTracker, TelegramBotService telegramBotService, Serilog.ILogger logger)
    {
        _sessionTracker = sessionTracker;
        _telegramBotService = telegramBotService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(5050);
        });

        builder.Services.AddRouting();

        var app = builder.Build();

        // Setup Telegram Bot command hooks
        TelegramBotService.OnCaptureRequested = async (userArg) =>
        {
            string targetUsername;
            if (!string.IsNullOrWhiteSpace(userArg))
            {
                targetUsername = userArg;
            }
            else
            {
                var activeSessions = _sessionTracker.ActiveSessions.Values.Where(s => !s.IsLocked).ToList();
                var firstRestricted = activeSessions.Select(s => UserRepository.GetBySid(s.UserSid)).FirstOrDefault(u => u != null && u.IsRestricted);
                if (firstRestricted == null) return null;
                targetUsername = firstRestricted.Username;
            }
            return await TriggerCaptureAndWaitAsync(targetUsername, "TELEGRAM", 15);
        };

        TelegramBotService.OnStatusRequested = () =>
        {
            var restrictedUsers = UserRepository.GetAll().Where(u => u.IsRestricted).ToList();
            if (restrictedUsers.Count == 0) return Task.FromResult("ℹ️ Belum ada akun pengguna terbatas yang terdaftar.");

            var sb = new StringBuilder();
            sb.AppendLine("📊 *Status Screen Time Hari Ini:*\n");
            var today = DateOnly.FromDateTime(DateTime.Now);
            var dayOfWeek = DateTime.Now.DayOfWeek;

            foreach (var u in restrictedUsers)
            {
                var limit = ScheduleRepository.GetEffectiveLimit(u.Id, dayOfWeek, today);
                var usage = UsageRepository.GetUsage(u.Id, today);
                var totalAllowed = (limit?.DailyMinutes ?? 120) + (usage?.BonusMinutes ?? 0);
                var used = usage?.MinutesUsed ?? 0;
                var remaining = Math.Max(0, totalAllowed - used);

                var session = _sessionTracker.ActiveSessions.Values.FirstOrDefault(s => s.UserSid == u.Sid);
                var isOnline = session != null;
                var isLocked = session?.IsLocked ?? false;

                string stateText;
                if (!isOnline) stateText = "⚫ Offline";
                else if (isLocked) stateText = "🔒 Terkunci (Dijeda)";
                else stateText = "🟢 Aktif di desktop";

                sb.AppendLine($"👤 *{u.Username}* ({stateText})");
                sb.AppendLine($"   ⏳ Sisa: *{remaining}m* / {totalAllowed}m (Terpakai: {used}m)");
                if (limit != null)
                {
                    sb.AppendLine($"   🌙 Jam Malam: `{limit.ScheduleStart:HH:mm} – {limit.ScheduleEnd:HH:mm}`");
                }

                if (_liveActivities.TryGetValue(u.Username, out var act) && (DateTime.Now - act.LastSeen).TotalMinutes < 2)
                {
                    sb.AppendLine($"   🎮 Sedang membuka: `{act.ProcessName}`");
                }
                sb.AppendLine();
            }
            return Task.FromResult(sb.ToString());
        };

        TelegramBotService.OnPromptDispatchRequested = (targetUsername, message, urgency, targetDisplay, customOptions) =>
        {
            var user = UserRepository.GetByUsername(targetUsername)
                ?? UserRepository.GetAll().FirstOrDefault(u => u.Username.Equals(targetUsername, StringComparison.OrdinalIgnoreCase));
            if (user == null)
            {
                return Task.FromResult((false, $"Pengguna '{targetUsername}' tidak ditemukan."));
            }

            var promptId = SessionPromptRepository.Create(user.Sid, user.Username, message, urgency, targetDisplay, customOptions);
            EventRepository.LogEvent(user.Sid, EventType.PROMPT_SENT, $"Pesan interaktif dikirim ke {user.Username}: \"{message}\" (Prioritas: {urgency})");

            var activeSession = _sessionTracker.ActiveSessions.Values.FirstOrDefault(s => s.UserSid == user.Sid && !s.IsLocked);
            if (activeSession != null)
            {
                _sessionTracker.EnsureAgentRunning(activeSession.SessionId);
            }

            return Task.FromResult((true, string.Empty));
        };

        TelegramBotService.OnLockSessionRequested = (sessionId) =>
        {
            if (sessionId > 0)
            {
                SessionManager.LockSession(sessionId);
            }
            var consoleId = SessionManager.GetActiveConsoleSessionId();
            if (consoleId >= 0 && consoleId != sessionId)
            {
                SessionManager.LockSession(consoleId);
            }
            return Task.FromResult(true);
        };

        TelegramBotService.OnGrantBonusMinutesRequested = (targetUsername, minutes) =>
        {
            var user = UserRepository.GetByUsername(targetUsername)
                ?? UserRepository.GetAll().FirstOrDefault(u => u.Username.Equals(targetUsername, StringComparison.OrdinalIgnoreCase));
            if (user != null)
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                UsageRepository.AddBonusMinutes(user.Id, today, minutes);
                EventRepository.LogEvent(user.Sid, EventType.LOGIN, $"Admin menambahkan +{minutes}m via Telegram");
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        };

        TelegramBotService.OnGetActiveSessionsRequested = () =>
        {
            var list = _sessionTracker.ActiveSessions.Values
                .Where(s => !s.IsLocked)
                .Select(s => (s.SessionId, s.Username, s.UserSid))
                .ToList();
            return Task.FromResult(list);
        };

        app.MapGet("/", () => Results.Redirect("/request"));

        // User Grace Request Portal
        app.MapGet("/request", async (HttpContext ctx) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var dayOfWeek = DateTime.Now.DayOfWeek;

            var allUsers = UserRepository.GetAll();
            var restrictedUsers = allUsers.Where(u => u.IsRestricted).ToList();

            // Check if specific restricted user was requested via query parameter ?user=...
            var queryUser = ctx.Request.Query["user"].ToString();
            User? targetUser = null;
            if (!string.IsNullOrWhiteSpace(queryUser))
            {
                targetUser = restrictedUsers.FirstOrDefault(u => string.Equals(u.Username, queryUser, StringComparison.OrdinalIgnoreCase));
            }

            var consoleSession = _sessionTracker.GetConsoleSession();
            User? consoleUser = consoleSession != null ? UserRepository.GetBySid(consoleSession.UserSid) : null;

            // If no explicit ?user= query:
            if (targetUser == null)
            {
                // If console user is Administrator or unrestricted, redirect directly to /admin
                if (consoleUser != null && !consoleUser.IsRestricted)
                {
                    return Results.Redirect("/admin");
                }

                // If console user is restricted, target them
                if (consoleUser != null && consoleUser.IsRestricted)
                {
                    targetUser = consoleUser;
                }
            }

            bool isAdminTesting = false;
            if (targetUser == null)
            {
                // Fallback for query testing or preview
                if (restrictedUsers.Count > 0)
                {
                    targetUser = restrictedUsers.FirstOrDefault();
                    isAdminTesting = true;
                }
                else
                {
                    return Results.Redirect("/admin");
                }
            }

            var limit = ScheduleRepository.GetEffectiveLimit(targetUser!.Id, dayOfWeek, today);
            var usage = UsageRepository.GetUsage(targetUser.Id, today);
            var totalAllowed = (limit?.DailyMinutes ?? 120) + (usage?.BonusMinutes ?? 0);
            var used = usage?.MinutesUsed ?? 0;
            var dailyRemaining = Math.Max(0, totalAllowed - used);

            var sessions = _sessionTracker.ActiveSessions.Values.ToList();
            var activeTargetSession = sessions.FirstOrDefault(s => s.UserSid == targetUser.Sid);
            bool isSessionActive = activeTargetSession != null;
            bool isSessionLocked = activeTargetSession?.IsLocked ?? false;

            var elapsedSec = activeTargetSession != null && !isSessionLocked ? (int)(DateTime.Now - activeTargetSession.LastTick).TotalSeconds : 0;
            var dailySec = Math.Max(0, (dailyRemaining * 60) - Math.Min(59, Math.Max(0, elapsedSec)));

            var nowTime = DateTime.Now.TimeOfDay;
            var curfewSec = int.MaxValue;
            if (limit != null)
            {
                var startSpan = limit.ScheduleStart.ToTimeSpan();
                var endSpan = limit.ScheduleEnd.ToTimeSpan();
                if (nowTime < startSpan || nowTime >= endSpan)
                {
                    curfewSec = 0;
                }
                else
                {
                    curfewSec = Math.Max(0, (int)(endSpan - nowTime).TotalSeconds);
                }
            }

            var remainingSeconds = Math.Min(dailySec, curfewSec);
            var remaining = (remainingSeconds + 59) / 60;
            var curfew = limit != null ? $"{limit.ScheduleStart:HH:mm} – {limit.ScheduleEnd:HH:mm}" : "08:00 – 22:00";

            var isOffline = !await TelegramBotService.CheckConnectivityAsync();
            var maxRequests = SettingsRepository.GetMaxDailyRequests();
            var countToday = GraceRequestRepository.GetTodayRequestCount(targetUser.Id, today);
            var hasPending = GraceRequestRepository.HasPendingRequest(targetUser.Id, today);
            var latestReq = GraceRequestRepository.GetTodayRequest(targetUser.Id, today);
            var lastOverallReq = GraceRequestRepository.GetLatestRequest(targetUser.Id);
            string? latestDeclineReason = null;
            DateTime? latestResolvedAt = null;
            if (lastOverallReq != null && string.Equals(lastOverallReq.Status, "DECLINED", StringComparison.OrdinalIgnoreCase))
            {
                latestDeclineReason = lastOverallReq.DeclineReason;
                latestResolvedAt = lastOverallReq.ResolvedAt;
            }

            var availableUsernames = restrictedUsers.Select(u => u.Username).ToList();

            var baseLimit = LimitRepository.GetByUserId(targetUser.Id);
            var customSchedules = ScheduleRepository.GetWeeklySchedule(targetUser.Id);
            var upcomingExceptions = ScheduleExceptionRepository.GetUpcomingForUser(targetUser.Id, today);
            var defaultSummary = baseLimit != null
                ? $"{baseLimit.DailyMinutes}m ({baseLimit.ScheduleStart:HH:mm}–{baseLimit.ScheduleEnd:HH:mm})"
                : "120m (08:00–22:00)";

            var dayNames = new (DayOfWeek Day, string Code, string Name)[]
            {
                (DayOfWeek.Monday, "Sen", "Senin"),
                (DayOfWeek.Tuesday, "Sel", "Selasa"),
                (DayOfWeek.Wednesday, "Rab", "Rabu"),
                (DayOfWeek.Thursday, "Kam", "Kamis"),
                (DayOfWeek.Friday, "Jum", "Jumat"),
                (DayOfWeek.Saturday, "Sab", "Sabtu"),
                (DayOfWeek.Sunday, "Min", "Minggu")
            };

            var weeklySchedule = dayNames.Select(dn =>
            {
                var custom = customSchedules.FirstOrDefault(s => s.DayOfWeek == dn.Day);
                int mins = custom != null ? custom.DailyMinutes : (baseLimit?.DailyMinutes ?? 120);
                var start = custom != null ? custom.ScheduleStart : (baseLimit?.ScheduleStart ?? new TimeOnly(8, 0));
                var end = custom != null ? custom.ScheduleEnd : (baseLimit?.ScheduleEnd ?? new TimeOnly(22, 0));

                bool hasExc = false;
                string? excNote = null;
                if (dn.Day == dayOfWeek)
                {
                    var todayExc = upcomingExceptions.FirstOrDefault(e => e.ExceptionDate == today);
                    if (todayExc != null)
                    {
                        hasExc = true;
                        mins = todayExc.DailyMinutes;
                        start = todayExc.ScheduleStart;
                        end = todayExc.ScheduleEnd;
                        excNote = "Pengecualian Hari Ini";
                    }
                }

                return new RequestPage.ScheduleDayView(
                    dn.Day,
                    dn.Code,
                    dn.Name,
                    mins,
                    $"{start:HH:mm} – {end:HH:mm}",
                    custom != null,
                    dn.Day == dayOfWeek,
                    hasExc,
                    excNote
                );
            }).ToList();

            var html = RequestPage.Render(
                targetUser.Username,
                remaining,
                remainingSeconds,
                curfew,
                isOffline,
                countToday,
                maxRequests,
                hasPending,
                latestReq?.Status ?? lastOverallReq?.Status,
                latestDeclineReason,
                latestResolvedAt,
                isAdminTesting,
                availableUsernames,
                isSessionActive,
                isSessionLocked,
                weeklySchedule,
                defaultSummary);
            return Results.Content(html, "text/html");
        });

        // User Grace Request Submission API
        app.MapPost("/api/request", async (HttpContext ctx) =>
        {
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();
                var doc = JsonDocument.Parse(body);
                var requestType = doc.RootElement.TryGetProperty("requestType", out var typeProp) ? typeProp.GetString() : "extension";
                var minutes = doc.RootElement.GetProperty("minutes").GetInt32();
                var reason = doc.RootElement.GetProperty("reason").GetString() ?? "";
                var requestedUser = doc.RootElement.TryGetProperty("username", out var uProp) ? uProp.GetString() : null;

                if (string.Equals(requestType, "schedule_change", StringComparison.OrdinalIgnoreCase))
                {
                    if (minutes <= 0 || minutes > 480 || string.IsNullOrWhiteSpace(reason))
                    {
                        return Results.BadRequest(new { error = "Jumlah menit tidak valid (1–480 menit) atau alasan harus diisi." });
                    }
                }
                else
                {
                    if (minutes <= 0 || minutes > 120 || string.IsNullOrWhiteSpace(reason))
                    {
                        return Results.BadRequest(new { error = "Jumlah menit tidak valid (1–120 menit) atau alasan harus diisi." });
                    }
                }

                var today = DateOnly.FromDateTime(DateTime.Now);

                var restrictedUsers = UserRepository.GetAll().Where(u => u.IsRestricted).ToList();
                User? user = null;

                if (!string.IsNullOrWhiteSpace(requestedUser))
                {
                    user = restrictedUsers.FirstOrDefault(u => string.Equals(u.Username, requestedUser, StringComparison.OrdinalIgnoreCase));
                }

                if (user == null)
                {
                    var sessions = _sessionTracker.ActiveSessions.Values.ToList();
                    foreach (var s in sessions)
                    {
                        var u = UserRepository.GetBySid(s.UserSid);
                        if (u != null && u.IsRestricted) { user = u; break; }
                    }
                }

                user ??= restrictedUsers.FirstOrDefault();

                if (user == null)
                {
                    return Results.BadRequest(new { error = "Pengguna terbatas tidak ditemukan." });
                }

                // Check daily request limits and pending requests
                var maxRequests = SettingsRepository.GetMaxDailyRequests();
                if (maxRequests <= 0)
                {
                    return Results.BadRequest(new { error = "Permintaan tambahan waktu dinonaktifkan oleh administrator." });
                }

                if (GraceRequestRepository.HasPendingRequest(user.Id, today))
                {
                    return Results.BadRequest(new { error = "Anda masih memiliki permintaan yang sedang menunggu keputusan administrator." });
                }

                var countToday = GraceRequestRepository.GetTodayRequestCount(user.Id, today);
                if (countToday >= maxRequests)
                {
                    return Results.BadRequest(new { error = $"Anda telah mencapai batas maksimal ({maxRequests}) permintaan untuk hari ini." });
                }

                // Check internet
                var isOnline = await TelegramBotService.CheckConnectivityAsync();
                if (!isOnline)
                {
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

                if (string.Equals(requestType, "schedule_change", StringComparison.OrdinalIgnoreCase))
                {
                    var targetDateStr = doc.RootElement.GetProperty("targetDate").GetString();
                    if (!DateOnly.TryParse(targetDateStr, out var targetDate))
                    {
                        return Results.BadRequest(new { error = "Tanggal sasaran tidak valid." });
                    }

                    var startStr = doc.RootElement.TryGetProperty("requestedStart", out var sProp) ? sProp.GetString() : null;
                    var endStr = doc.RootElement.TryGetProperty("requestedEnd", out var eProp) ? eProp.GetString() : null;
                    TimeOnly? start = !string.IsNullOrEmpty(startStr) && TimeOnly.TryParse(startStr, out var sVal) ? sVal : null;
                    TimeOnly? end = !string.IsNullOrEmpty(endStr) && TimeOnly.TryParse(endStr, out var eVal) ? eVal : null;

                    var req = GraceRequestRepository.CreateScheduleChange(user.Id, today, targetDate, minutes, start, end, reason);
                    _ = _telegramBotService.SendGraceRequestAlertAsync(req, user.Username);

                    return Results.Ok(new { success = true, message = $"Pengajuan jadwal baru untuk {targetDate:dd/MM/yyyy} ({minutes}m) berhasil dikirim ke orang tua." });
                }
                else
                {
                    var req = GraceRequestRepository.Create(user.Id, today, minutes, reason);
                    _ = _telegramBotService.SendGraceRequestAlertAsync(req, user.Username);

                    return Results.Ok(new { success = true, message = $"Permintaan tambahan waktu +{minutes} menit berhasil dikirim ke orang tua." });
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to process grace request submission");
                return Results.BadRequest(new { error = "Permintaan tidak valid." });
            }
        });

        // Agent Status API (polled by ParentalControl.Agent running in standard user session)
        app.MapGet("/api/agent/status", (HttpContext ctx) =>
        {
            var username = ctx.Request.Query["user"].ToString();
            if (string.IsNullOrWhiteSpace(username))
            {
                return Results.BadRequest(new { error = "Parameter 'user' harus diisi." });
            }

            var user = UserRepository.GetByUsername(username);

            object? activePromptObj = null;
            var activePrompt = SessionPromptRepository.GetActivePrompt(username);
            if (activePrompt != null)
            {
                List<string> optionsList;
                if (!string.IsNullOrWhiteSpace(activePrompt.CustomOptions))
                {
                    optionsList = activePrompt.CustomOptions.Split('|', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                }
                else
                {
                    optionsList = SettingsRepository.GetPromptResponsePresets();
                }

                activePromptObj = new
                {
                    id = activePrompt.Id,
                    message = activePrompt.Message,
                    urgency = activePrompt.Urgency,
                    targetDisplay = activePrompt.TargetDisplay,
                    timeoutSeconds = 120,
                    options = optionsList
                };
            }

            if (user == null || !user.IsRestricted)
            {
                return Results.Ok(new
                {
                    username,
                    isRestricted = false,
                    remainingSeconds = 0,
                    curfew = "Akun tidak dibatasi",
                    isLocked = false,
                    activePrompt = activePromptObj
                });
            }

            var today = DateOnly.FromDateTime(DateTime.Now);
            var dayOfWeek = DateTime.Now.DayOfWeek;
            var limit = ScheduleRepository.GetEffectiveLimit(user.Id, dayOfWeek, today);
            var usage = UsageRepository.GetUsage(user.Id, today);
            var totalAllowed = (limit?.DailyMinutes ?? 120) + (usage?.BonusMinutes ?? 0);
            var used = usage?.MinutesUsed ?? 0;
            var remMinutes = Math.Max(0, totalAllowed - used);

            var sessions = _sessionTracker.ActiveSessions.Values.ToList();
            var session = sessions.FirstOrDefault(s => s.UserSid == user.Sid);
            var isLocked = session?.IsLocked ?? false;
            var elapsedSec = session != null && !isLocked ? (int)(DateTime.Now - session.LastTick).TotalSeconds : 0;
            var dailySec = Math.Max(0, (remMinutes * 60) - Math.Min(59, Math.Max(0, elapsedSec)));

            var nowTime = DateTime.Now.TimeOfDay;
            var curfewSec = int.MaxValue;
            if (limit != null)
            {
                var startSpan = limit.ScheduleStart.ToTimeSpan();
                var endSpan = limit.ScheduleEnd.ToTimeSpan();
                if (nowTime < startSpan || nowTime >= endSpan)
                {
                    curfewSec = 0;
                }
                else
                {
                    curfewSec = Math.Max(0, (int)(endSpan - nowTime).TotalSeconds);
                }
            }

            var remainingSeconds = Math.Min(dailySec, curfewSec);
            var isCurfewClamped = curfewSec < dailySec;

            var latestReq = GraceRequestRepository.GetLatestRequest(user.Id);
            int? lastDeclinedId = null;
            string? lastDeclinedReason = null;
            DateTime? lastDeclinedTime = null;
            if (latestReq != null && string.Equals(latestReq.Status, "DECLINED", StringComparison.OrdinalIgnoreCase))
            {
                lastDeclinedId = latestReq.Id;
                lastDeclinedReason = latestReq.DeclineReason;
                lastDeclinedTime = latestReq.ResolvedAt;
            }

            var curfew = limit != null ? $"{limit.ScheduleStart:HH:mm} – {limit.ScheduleEnd:HH:mm}" : "08:00 – 22:00";

            var captureRequested = false;
            var watchInterval = 0;
            if (_captureStates.TryGetValue(user.Username, out var capState))
            {
                if (capState.WatchMode && DateTime.Now < capState.WatchExpiresAt)
                {
                    watchInterval = 10;
                }
                if (capState.CaptureRequested)
                {
                    captureRequested = true;
                    capState.CaptureRequested = false;
                }
            }

            return Results.Ok(new
            {
                username = user.Username,
                isRestricted = true,
                remainingSeconds,
                curfew,
                isLocked,
                isCurfewClamped,
                lastDeclinedId,
                lastDeclinedReason,
                lastDeclinedTime,
                captureRequested,
                watchIntervalSeconds = watchInterval,
                activePrompt = activePromptObj
            });
        });

        // Agent Interactive Prompt Response API
        app.MapPost("/api/agent/prompt/respond", async (HttpContext ctx) =>
        {
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();
                var doc = JsonDocument.Parse(body);
                var promptId = doc.RootElement.GetProperty("promptId").GetInt32();
                var response = doc.RootElement.GetProperty("response").GetString() ?? "YES";
                var reason = doc.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : null;
                var turnaroundSeconds = doc.RootElement.TryGetProperty("turnaroundSeconds", out var ts) ? ts.GetInt32() : 0;

                var prompt = SessionPromptRepository.GetById(promptId);
                if (prompt == null)
                {
                    return Results.NotFound(new { error = "Pesan prompt tidak ditemukan." });
                }

                var resolved = SessionPromptRepository.ResolvePrompt(promptId, response, reason, turnaroundSeconds);
                if (!resolved)
                {
                    return Results.BadRequest(new { error = "Pesan prompt sudah pernah dijawab." });
                }

                if (response.Equals("YES", StringComparison.OrdinalIgnoreCase))
                {
                    EventRepository.LogEvent(prompt.UserSid, EventType.PROMPT_ANSWERED, $"Prompt #{promptId} dijawab (YA) dalam {turnaroundSeconds}s");
                }
                else if (response.Equals("TIMEOUT", StringComparison.OrdinalIgnoreCase))
                {
                    EventRepository.LogEvent(prompt.UserSid, EventType.PROMPT_TIMEOUT, $"Prompt #{promptId} tidak dijawab (Timeout {turnaroundSeconds}s)");
                }
                else
                {
                    var reasonDesc = !string.IsNullOrWhiteSpace(reason) ? $": \"{reason}\"" : "";
                    EventRepository.LogEvent(prompt.UserSid, EventType.PROMPT_ANSWERED, $"Prompt #{promptId} dijawab (TIDAK){reasonDesc} dalam {turnaroundSeconds}s");
                }

                prompt.Response = response;
                prompt.ResponseReason = reason;
                prompt.TurnaroundSeconds = turnaroundSeconds;
                prompt.Status = response.Equals("TIMEOUT", StringComparison.OrdinalIgnoreCase) ? "TIMEOUT" : "ANSWERED";

                var session = _sessionTracker.ActiveSessions.Values.FirstOrDefault(s => s.UserSid == prompt.UserSid);
                var sessionId = session?.SessionId;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _telegramBotService.SendPromptResponseAlertAsync(prompt, sessionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to send prompt response alert to Telegram");
                    }
                });

                return Results.Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing prompt response");
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Agent Activity Reporting API (posted by ParentalControl.Agent to record foreground window)
        app.MapPost("/api/agent/activity", async (HttpContext ctx) =>
        {
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();
                var doc = JsonDocument.Parse(body);
                var username = doc.RootElement.GetProperty("username").GetString();
                var processName = doc.RootElement.GetProperty("processName").GetString();
                var windowTitle = doc.RootElement.TryGetProperty("windowTitle", out var wt) ? wt.GetString() ?? "" : "";
                var durationSeconds = 20;
                if (doc.RootElement.TryGetProperty("durationSeconds", out var dsProp) && dsProp.TryGetInt32(out var dsVal) && dsVal > 0)
                {
                    durationSeconds = Math.Clamp(dsVal, 1, 300);
                }

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(processName))
                {
                    return Results.BadRequest();
                }

                var user = UserRepository.GetByUsername(username);
                if (user != null && user.IsRestricted)
                {
                    var isSessionLocked = _sessionTracker.ActiveSessions.Values
                        .Any(s => string.Equals(s.Username, username, StringComparison.OrdinalIgnoreCase) && s.IsLocked);

                    if (isSessionLocked)
                    {
                        return Results.Ok(new { success = true, skipped = "session_locked" });
                    }

                    _liveActivities[user.Username] = (processName, windowTitle, DateTime.Now);
                    var today = DateOnly.FromDateTime(DateTime.Now);
                    AppUsageRepository.AddSeconds(user.Id, today, processName, windowTitle, durationSeconds);
                }

                return Results.Ok(new { success = true });
            }
            catch
            {
                return Results.BadRequest();
            }
        });

        // Agent Silent Screen Capture Upload API
        app.MapPost("/api/agent/capture", async (HttpContext ctx) =>
        {
            var username = ctx.Request.Headers["X-User"].ToString();
            var trigger = ctx.Request.Headers["X-Trigger"].ToString();
            if (string.IsNullOrWhiteSpace(trigger)) trigger = "MANUAL";
            int.TryParse(ctx.Request.Headers["X-Width"], out var width);
            int.TryParse(ctx.Request.Headers["X-Height"], out var height);

            if (string.IsNullOrWhiteSpace(username))
            {
                return Results.BadRequest(new { error = "Header X-User is required." });
            }

            var user = UserRepository.GetByUsername(username);
            if (user == null)
            {
                return Results.NotFound(new { error = "User not found." });
            }

            var userCapturesDir = Path.Combine(DatabaseManager.CapturesDirectory, user.Id.ToString());
            if (!Directory.Exists(userCapturesDir))
            {
                Directory.CreateDirectory(userCapturesDir);
            }

            var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.jpg";
            var filePath = Path.Combine(userCapturesDir, fileName);

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await ctx.Request.Body.CopyToAsync(fs);
            }

            var fi = new FileInfo(filePath);
            var record = new ScreenCaptureRecord
            {
                UserId = user.Id,
                Timestamp = DateTime.Now,
                FilePath = filePath,
                Width = width > 0 ? width : 1920,
                Height = height > 0 ? height : 1080,
                FileSizeBytes = fi.Length,
                TriggerType = trigger,
                AdminIp = ctx.Connection.RemoteIpAddress?.ToString()
            };

            record = ScreenCaptureRepository.Add(record);
            _logger.Information("Screen capture recorded for {Username} ({Width}x{Height}, {Bytes} bytes, Trigger: {Trigger})",
                user.Username, record.Width, record.Height, record.FileSizeBytes, record.TriggerType);

            OnCaptureReceived?.Invoke(record);

            return Results.Ok(new { id = record.Id, path = record.FilePath });
        });

        // Web Admin Page
        app.MapGet("/admin", (HttpContext ctx) =>
        {
            var isTotpEnabled = SettingsRepository.Get(SettingsRepository.KeyTotpEnabled, "false") == "true";

            if (isTotpEnabled && !IsAdminAuthenticated(ctx))
            {
                return Results.Content(AdminPage.RenderLogin(), "text/html");
            }

            return Results.Content(AdminPage.RenderDashboard(), "text/html");
        });

        // Web Admin Login API
        app.MapPost("/api/admin/login", async (HttpContext ctx) =>
        {
            string? code = null;
            if (ctx.Request.HasFormContentType)
            {
                var form = await ctx.Request.ReadFormAsync();
                code = form["code"].ToString();
            }
            else
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var doc = JsonDocument.Parse(await reader.ReadToEndAsync());
                code = doc.RootElement.GetProperty("code").GetString();
            }

            var secret = SettingsRepository.Get(SettingsRepository.KeyTotpSecret);

            if (string.IsNullOrWhiteSpace(secret) || TotpService.VerifyCode(secret, code ?? ""))
            {
                var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                lock (_validAdminTokens)
                {
                    _validAdminTokens.Add(token);
                }

                ctx.Response.Cookies.Append("pc_admin_session", token, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Expires = DateTimeOffset.UtcNow.AddHours(12)
                });

                return Results.Redirect("/admin");
            }

            return Results.Content(AdminPage.RenderLogin("Invalid code. Please try again."), "text/html");
        });

        app.MapGet("/api/admin/logout", (HttpContext ctx) =>
        {
            if (ctx.Request.Cookies.TryGetValue("pc_admin_session", out var token))
            {
                lock (_validAdminTokens)
                {
                    _validAdminTokens.Remove(token);
                }
            }
            ctx.Response.Cookies.Delete("pc_admin_session");
            return Results.Redirect("/admin");
        });

        // Web Admin Dashboard JSON API
        app.MapGet("/api/admin/dashboard", (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();

            var today = DateOnly.FromDateTime(DateTime.Now);
            var dayOfWeek = DateTime.Now.DayOfWeek;
            var users = UserRepository.GetAll().ToDictionary(u => u.Id, u => u);
            var restrictedUsers = users.Values.Where(u => u.IsRestricted).ToList();

            // Query parameters for ActivityWatch timeline
            var filterUser = ctx.Request.Query["user"].ToString();
            var filterDateStr = ctx.Request.Query["date"].ToString();
            var filterRange = ctx.Request.Query["range"].ToString(); // "day" or "week"

            var targetDate = today;
            if (!string.IsNullOrWhiteSpace(filterDateStr) && DateOnly.TryParse(filterDateStr, out var parsedDate))
            {
                targetDate = parsedDate;
            }

            var fromDate = string.Equals(filterRange, "week", StringComparison.OrdinalIgnoreCase)
                ? targetDate.AddDays(-6)
                : targetDate;

            int? targetUserId = null;
            if (!string.IsNullOrWhiteSpace(filterUser) && !string.Equals(filterUser, "all", StringComparison.OrdinalIgnoreCase))
            {
                var matched = restrictedUsers.FirstOrDefault(u => string.Equals(u.Username, filterUser, StringComparison.OrdinalIgnoreCase));
                if (matched != null) targetUserId = matched.Id;
            }

            var sessionList = new List<object>();
            foreach (var (sessionId, activeSession) in _sessionTracker.ActiveSessions)
            {
                var user = UserRepository.GetBySid(activeSession.UserSid);
                if (user == null) continue;
                var isRestricted = user.IsRestricted;
                var limit = isRestricted ? ScheduleRepository.GetEffectiveLimit(user.Id, dayOfWeek, today) : null;
                var usage = isRestricted ? UsageRepository.GetUsage(user.Id, today) : null;
                var totalAllowed = limit != null ? (limit.DailyMinutes + (usage?.BonusMinutes ?? 0)) : 0;
                var used = usage?.MinutesUsed ?? 0;
                var remaining = limit != null ? Math.Max(0, totalAllowed - used) : 0;

                string currentApp = "Inactive / Idle";
                bool isAppLive = false;
                if (_liveActivities.TryGetValue(activeSession.Username, out var live))
                {
                    if (DateTime.Now - live.LastSeen < TimeSpan.FromMinutes(2))
                    {
                        currentApp = string.IsNullOrWhiteSpace(live.WindowTitle)
                            ? live.ProcessName
                            : $"{live.ProcessName} — {live.WindowTitle}";
                        isAppLive = true;
                    }
                }

                sessionList.Add(new
                {
                    sessionId,
                    username = activeSession.Username,
                    userId = user.Id,
                    isRestricted,
                    isLocked = activeSession.IsLocked,
                    remainingMinutes = remaining,
                    totalAllowed,
                    curfew = limit != null ? $"{limit.ScheduleStart:HH:mm}–{limit.ScheduleEnd:HH:mm}" : "None",
                    currentApp,
                    isAppLive
                });
            }

            var recentRequests = GraceRequestRepository.GetRecentRequests(10)
                .Select(r => new
                {
                    r.Id,
                    username = users.TryGetValue(r.UserId, out var u) ? u.Username : $"User #{r.UserId}",
                    r.RequestedMinutes,
                    r.Reason,
                    r.Status,
                    createdAt = r.CreatedAt.ToString("HH:mm:ss")
                });

            // Hourly distribution for 24-hour timeline bar
            var hourlyDistribution = AppUsageRepository.GetHourlyDistribution(targetUserId, fromDate, targetDate);

            // Detailed app usage for selected user & date range
            var usageRecords = AppUsageRepository.GetUsageForRange(targetUserId, fromDate, targetDate);
            var totalMinutes = usageRecords.Sum(r => r.Minutes);

            var appUsageList = usageRecords.Select(r => new
            {
                userId = r.UserId,
                username = users.TryGetValue(r.UserId, out var u) ? u.Username : $"User #{r.UserId}",
                processName = r.ProcessName,
                windowTitle = r.WindowTitle,
                minutes = r.Minutes,
                percentage = totalMinutes > 0 ? (int)Math.Round((double)r.Minutes / totalMinutes * 100) : 0
            }).ToList();

            var availableUsers = restrictedUsers.Select(u => u.Username).ToList();
            var managedUsers = restrictedUsers.Select(u =>
            {
                var baseLim = LimitRepository.GetByUserId(u.Id);
                var customList = ScheduleRepository.GetWeeklySchedule(u.Id);
                return new
                {
                    id = u.Id,
                    username = u.Username,
                    baseMinutes = baseLim?.DailyMinutes ?? 120,
                    baseStart = baseLim != null ? baseLim.ScheduleStart.ToString("HH:mm") : "08:00",
                    baseEnd = baseLim != null ? baseLim.ScheduleEnd.ToString("HH:mm") : "22:00",
                    customCount = customList.Count
                };
            }).ToList();

            return Results.Ok(new
            {
                sessions = sessionList,
                requests = recentRequests,
                managedUsers,
                appUsage = appUsageList,
                hourlyTimeline = hourlyDistribution,
                totalMinutes,
                availableUsers,
                selectedUser = string.IsNullOrWhiteSpace(filterUser) ? "all" : filterUser,
                selectedDate = targetDate.ToString("yyyy-MM-dd"),
                selectedRange = string.Equals(filterRange, "week", StringComparison.OrdinalIgnoreCase) ? "week" : "day"
            });
        });

        // Web Admin Grant Time API
        app.MapPost("/api/admin/grant", async (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();

            using var reader = new StreamReader(ctx.Request.Body);
            var doc = JsonDocument.Parse(await reader.ReadToEndAsync());
            var userId = doc.RootElement.GetProperty("userId").GetInt32();
            var minutes = doc.RootElement.GetProperty("minutes").GetInt32();

            var today = DateOnly.FromDateTime(DateTime.Now);
            UsageRepository.AddBonusMinutes(userId, today, minutes);
            var user = UserRepository.GetAll().FirstOrDefault(u => u.Id == userId);
            if (user != null)
            {
                EventRepository.LogEvent(user.Sid, EventType.LOGIN, $"Web Admin granted +{minutes}m bonus");
                var active = SessionManager.GetActiveSessions().FirstOrDefault(s => s.Sid == user.Sid);
                if (active.Sid != null)
                {
                    var msg = SettingsRepository.GetMessage(SettingsRepository.KeyMsgBonusGranted, new Dictionary<string, string>
                    {
                        ["minutes"] = minutes.ToString()
                    });
                    NotificationManager.SendMessage(active.SessionId, "Parental Control", msg, false, 15);
                }
            }

            return Results.Ok(new { success = true });
        });

        // Web Admin Force Logoff API
        app.MapPost("/api/admin/force-logoff", async (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();

            using var reader = new StreamReader(ctx.Request.Body);
            var doc = JsonDocument.Parse(await reader.ReadToEndAsync());
            var sessionId = doc.RootElement.GetProperty("sessionId").GetInt32();

            NotificationManager.SendMessage(sessionId, "Parental Control", "Administrator has terminated this session.", true, 5);
            SessionManager.ForceLogoff(sessionId);
            _sessionTracker.RemoveSession(sessionId);

            return Results.Ok(new { success = true });
        });

        // Web Admin Lock Session with Pre-Warning API
        app.MapPost("/api/admin/lock-session", async (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();

            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var doc = JsonDocument.Parse(await reader.ReadToEndAsync());
                var sessionId = doc.RootElement.GetProperty("sessionId").GetInt32();
                var message = doc.RootElement.TryGetProperty("message", out var mProp) ? mProp.GetString() : "";
                var seconds = doc.RootElement.TryGetProperty("seconds", out var sProp) ? sProp.GetInt32() : 15;

                if (seconds < 5) seconds = 5;
                if (seconds > 60) seconds = 60;

                if (string.IsNullOrWhiteSpace(message))
                {
                    message = SettingsRepository.GetMessage(SettingsRepository.KeyMsgRemoteLock);
                }

                var sessionUser = _sessionTracker.ActiveSessions.TryGetValue(sessionId, out var s) ? s.Username : $"Session {sessionId}";

                _logger.Information("Lock requested for session {SessionId} ({Username}) with {Seconds}s countdown: {Message}",
                    sessionId, sessionUser, seconds, message);

                // 1. Send unskippable native Win32 popup into session with countdown duration
                var popupText = $"{message}\n\n(Layar akan otomatis dikunci dalam {seconds} detik)";
                NotificationManager.SendMessage(sessionId, "Parental Control — Layar Dikunci", popupText, isWarning: true, timeoutSeconds: seconds);

                // 2. Schedule lock after countdown so user has 10-15s to view/save
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(seconds));
                    SessionManager.LockSession(sessionId);
                    _sessionTracker.OnSessionLock(sessionId, $"Remote lock from Web Admin ({seconds}s delay): {message}");
                });

                return Results.Ok(new { success = true, message = $"Peringatan dikirim ke {sessionUser}. Layar akan dikunci dalam {seconds} detik." });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to execute lock session request");
                return Results.BadRequest(new { error = "Invalid lock request payload." });
            }
        });

        // Web Admin Dispatch Interactive Prompt API
        app.MapPost("/api/admin/prompt/send", async (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();

            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var doc = JsonDocument.Parse(await reader.ReadToEndAsync());
                var username = doc.RootElement.GetProperty("username").GetString() ?? "";
                var message = doc.RootElement.GetProperty("message").GetString() ?? "";
                var urgency = doc.RootElement.TryGetProperty("urgency", out var uProp) ? uProp.GetString() ?? "NORMAL" : "NORMAL";
                var targetDisplay = doc.RootElement.TryGetProperty("targetDisplay", out var dProp) ? dProp.GetInt32() : -1;
                var customOptions = doc.RootElement.TryGetProperty("customOptions", out var cProp) ? cProp.GetString() : null;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(message))
                {
                    return Results.BadRequest(new { error = "Pengguna dan pesan harus diisi." });
                }

                var user = UserRepository.GetByUsername(username)
                    ?? UserRepository.GetAll().FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
                if (user == null)
                {
                    return Results.NotFound(new { error = $"Pengguna '{username}' tidak ditemukan." });
                }

                var promptId = SessionPromptRepository.Create(user.Sid, user.Username, message, urgency, targetDisplay, customOptions);
                EventRepository.LogEvent(user.Sid, EventType.PROMPT_SENT, $"Pesan interaktif dikirim ke {user.Username}: \"{message}\" (Prioritas: {urgency})");

                var activeSession = _sessionTracker.ActiveSessions.Values.FirstOrDefault(s => s.UserSid == user.Sid && !s.IsLocked);
                if (activeSession != null)
                {
                    _sessionTracker.EnsureAgentRunning(activeSession.SessionId);

                    if (!SessionManager.IsProcessRunningInSession("ParentalControl.Agent", activeSession.SessionId))
                    {
                        var popupTitle = urgency == "URGENT" ? "🚨 Parental Control — Mendesak" : "Parental Control — Pesan";
                        NotificationManager.SendMessage(activeSession.SessionId, popupTitle, message, urgency == "URGENT", timeoutSeconds: 30);
                    }
                }

                return Results.Ok(new { success = true, promptId });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to send prompt to user");
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Web Admin Prompt History API
        app.MapGet("/api/admin/prompts", (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();
            return Results.Ok(SessionPromptRepository.GetHistory(20));
        });
        // Web Admin Resolve Request API
        app.MapPost("/api/admin/resolve-request", async (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();

            using var reader = new StreamReader(ctx.Request.Body);
            var doc = JsonDocument.Parse(await reader.ReadToEndAsync());
            var requestId = doc.RootElement.GetProperty("requestId").GetInt32();
            var action = doc.RootElement.GetProperty("action").GetString();
            var minutes = doc.RootElement.TryGetProperty("minutes", out var m) ? m.GetInt32() : 0;

            var req = GraceRequestRepository.GetById(requestId);
            if (req == null) return Results.NotFound();

            if (action == "approve")
            {
                GraceRequestRepository.Resolve(requestId, "APPROVED");
                UsageRepository.AddBonusMinutes(req.UserId, req.Date, minutes);
                var user = UserRepository.GetAll().FirstOrDefault(u => u.Id == req.UserId);
                if (user != null)
                {
                    EventRepository.LogEvent(user.Sid, EventType.LOGIN, $"Web Admin approved Request #{requestId} (+{minutes}m)");
                    var active = SessionManager.GetActiveSessions().FirstOrDefault(s => s.Sid == user.Sid);
                    if (active.Sid != null)
                    {
                        var msg = SettingsRepository.GetMessage(SettingsRepository.KeyMsgBonusGranted, new Dictionary<string, string>
                        {
                            ["minutes"] = minutes.ToString()
                        });
                        NotificationManager.SendMessage(active.SessionId, "Parental Control", msg, false, 15);
                    }
                }
            }
            else
            {
                GraceRequestRepository.Resolve(requestId, "DECLINED");
                var user = UserRepository.GetAll().FirstOrDefault(u => u.Id == req.UserId);
                if (user != null)
                {
                    EventRepository.LogEvent(user.Sid, EventType.LOGIN_DENIED, $"Web Admin declined Request #{requestId}");
                }
            }

            return Results.Ok(new { success = true });
        });

        // Admin Trigger Capture API (Single-shot or Watch mode)
        app.MapPost("/api/admin/capture/request", async (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();

            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            var doc = JsonDocument.Parse(body);
            var username = doc.RootElement.GetProperty("username").GetString();
            var mode = doc.RootElement.TryGetProperty("mode", out var m) ? m.GetString() ?? "single" : "single";
            var enableWatch = doc.RootElement.TryGetProperty("enableWatch", out var ew) && ew.GetBoolean();

            if (string.IsNullOrWhiteSpace(username)) return Results.BadRequest(new { error = "Parameter 'username' harus diisi." });

            var user = UserRepository.GetByUsername(username);
            if (user == null) return Results.NotFound(new { error = "User tidak ditemukan." });

            var session = _sessionTracker.ActiveSessions.Values.FirstOrDefault(s => s.UserSid == user.Sid);
            if (session == null || session.IsLocked)
            {
                return Results.BadRequest(new { error = $"Sesi {username} sedang terkunci atau tidak aktif di desktop." });
            }

            if (mode == "watch")
            {
                var state = _captureStates.GetOrAdd(user.Username, _ => new CaptureCommandState());
                state.WatchMode = enableWatch;
                state.CaptureRequested = enableWatch;
                state.WatchExpiresAt = enableWatch ? DateTime.Now.AddMinutes(10) : DateTime.MinValue;
                state.Trigger = "WATCH";
                return Results.Ok(new { success = true, watchMode = enableWatch });
            }
            else
            {
                var state = _captureStates.GetOrAdd(user.Username, _ => new CaptureCommandState());
                state.CaptureRequested = true;
                state.Trigger = "MANUAL";
                return Results.Ok(new { success = true, captureRequested = true });
            }
        });

        // Admin Heartbeat for Watch Mode (extends timeout)
        app.MapPost("/api/admin/capture/heartbeat", (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();
            var username = ctx.Request.Query["user"].ToString();
            if (!string.IsNullOrWhiteSpace(username) && _captureStates.TryGetValue(username, out var cs) && cs.WatchMode)
            {
                cs.WatchExpiresAt = DateTime.Now.AddMinutes(10);
            }
            return Results.Ok(new { success = true });
        });

        // Admin Get Latest Capture API
        app.MapGet("/api/admin/captures/latest", (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();
            var username = ctx.Request.Query["user"].ToString();
            if (string.IsNullOrWhiteSpace(username) || username == "all")
            {
                var firstRestricted = UserRepository.GetAll().FirstOrDefault(u => u.IsRestricted);
                if (firstRestricted == null) return Results.Ok(new { capture = (object?)null });
                username = firstRestricted.Username;
            }

            var user = UserRepository.GetByUsername(username);
            if (user == null) return Results.NotFound();

            var latest = ScreenCaptureRepository.GetLatestByUser(user.Id);
            if (latest == null) return Results.Ok(new { capture = (object?)null });

            return Results.Ok(new
            {
                capture = new
                {
                    id = latest.Id,
                    userId = latest.UserId,
                    username = user.Username,
                    timestamp = latest.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                    width = latest.Width,
                    height = latest.Height,
                    fileSizeBytes = latest.FileSizeBytes,
                    triggerType = latest.TriggerType,
                    url = $"/api/admin/captures/{latest.Id}"
                }
            });
        });

        // Admin Get Recent Captures API
        app.MapGet("/api/admin/captures/recent", (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();
            var username = ctx.Request.Query["user"].ToString();
            int.TryParse(ctx.Request.Query["limit"], out var limit);
            if (limit <= 0) limit = 10;

            if (string.IsNullOrWhiteSpace(username) || username == "all")
            {
                var firstRestricted = UserRepository.GetAll().FirstOrDefault(u => u.IsRestricted);
                if (firstRestricted == null) return Results.Ok(new { captures = Array.Empty<object>() });
                username = firstRestricted.Username;
            }

            var user = UserRepository.GetByUsername(username);
            if (user == null) return Results.NotFound();

            var recent = ScreenCaptureRepository.GetRecentByUser(user.Id, limit);
            var list = recent.Select(r => new
            {
                id = r.Id,
                userId = r.UserId,
                username = user.Username,
                timestamp = r.Timestamp.ToString("HH:mm:ss"),
                date = r.Timestamp.ToString("yyyy-MM-dd"),
                width = r.Width,
                height = r.Height,
                fileSizeBytes = r.FileSizeBytes,
                triggerType = r.TriggerType,
                url = $"/api/admin/captures/{r.Id}"
            }).ToList();

            return Results.Ok(new { captures = list });
        });

        // Admin Serve Capture JPEG Image
        app.MapGet("/api/admin/captures/{id:int}", (HttpContext ctx, int id) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();

            var record = ScreenCaptureRepository.GetById(id);
            if (record == null || !File.Exists(record.FilePath))
            {
                return Results.NotFound();
            }

            return Results.File(record.FilePath, "image/jpeg");
        });

        // Admin Push Capture to Telegram
        app.MapPost("/api/admin/capture/telegram", async (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();

            int.TryParse(ctx.Request.Query["id"], out var id);
            var record = ScreenCaptureRepository.GetById(id);
            if (record == null || !File.Exists(record.FilePath))
            {
                return Results.NotFound(new { error = "Tangkapan layar tidak ditemukan." });
            }

            var chatId = SettingsRepository.Get(SettingsRepository.KeyTelegramChatId);
            if (string.IsNullOrWhiteSpace(chatId))
            {
                return Results.BadRequest(new { error = "Telegram Chat ID belum dikonfigurasi di Settings." });
            }

            var user = UserRepository.GetAll().FirstOrDefault(u => u.Id == record.UserId);
            var photoBytes = await File.ReadAllBytesAsync(record.FilePath);
            var caption = $"📸 *Tangkapan Layar Desktop* ({user?.Username ?? "User"})\n⏰ Waktu: `{record.Timestamp:yyyy-MM-dd HH:mm:ss}`\n📏 Resolusi: {record.Width}x{record.Height}\n🏷️ Trigger: {record.TriggerType}";

            var success = await _telegramBotService.SendPhotoAsync(chatId, photoBytes, caption);
            return Results.Ok(new { success });
        });

        // Admin Schedule Management GET API
        app.MapGet("/api/admin/schedule", (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();
            var userIdStr = ctx.Request.Query["userId"].ToString();
            if (!int.TryParse(userIdStr, out var userId))
            {
                return Results.BadRequest(new { error = "Invalid userId" });
            }

            var user = UserRepository.GetById(userId);
            if (user == null)
            {
                return Results.NotFound(new { error = "User not found" });
            }

            var baseLimit = LimitRepository.GetByUserId(userId);
            var customSchedules = ScheduleRepository.GetWeeklySchedule(userId);

            var dayNames = new (DayOfWeek Day, string Name)[]
            {
                (DayOfWeek.Monday, "Senin"),
                (DayOfWeek.Tuesday, "Selasa"),
                (DayOfWeek.Wednesday, "Rabu"),
                (DayOfWeek.Thursday, "Kamis"),
                (DayOfWeek.Friday, "Jumat"),
                (DayOfWeek.Saturday, "Sabtu"),
                (DayOfWeek.Sunday, "Minggu")
            };

            var baseMinutes = baseLimit?.DailyMinutes ?? 120;
            var baseStart = baseLimit != null ? baseLimit.ScheduleStart.ToString("HH:mm") : "08:00";
            var baseEnd = baseLimit != null ? baseLimit.ScheduleEnd.ToString("HH:mm") : "22:00";

            var days = dayNames.Select(dn =>
            {
                var custom = customSchedules.FirstOrDefault(s => s.DayOfWeek == dn.Day);
                return new
                {
                    dayOfWeek = (int)dn.Day,
                    dayName = dn.Name,
                    isCustom = custom != null,
                    dailyMinutes = custom?.DailyMinutes ?? baseMinutes,
                    scheduleStart = custom != null ? custom.ScheduleStart.ToString("HH:mm") : baseStart,
                    scheduleEnd = custom != null ? custom.ScheduleEnd.ToString("HH:mm") : baseEnd
                };
            }).ToList();

            var today = DateOnly.FromDateTime(DateTime.Now);
            var upcomingExceptions = ScheduleExceptionRepository.GetUpcomingForUser(userId, today)
                .Select(e => new
                {
                    id = e.Id,
                    date = e.ExceptionDate.ToString("yyyy-MM-dd"),
                    dailyMinutes = e.DailyMinutes,
                    scheduleStart = e.ScheduleStart.ToString("HH:mm"),
                    scheduleEnd = e.ScheduleEnd.ToString("HH:mm")
                }).ToList();

            return Results.Ok(new
            {
                userId = user.Id,
                username = user.Username,
                baseDailyMinutes = baseMinutes,
                baseScheduleStart = baseStart,
                baseScheduleEnd = baseEnd,
                days,
                upcomingExceptions
            });
        });
        // Admin Schedule Exception Add/Upsert POST API
        app.MapPost("/api/admin/schedule-exception", async (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();
                var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var userId = root.GetProperty("userId").GetInt32();
                var dateStr = root.GetProperty("date").GetString();
                if (!DateOnly.TryParse(dateStr, out var date))
                    return Results.BadRequest(new { error = "Tanggal tidak valid." });

                var minutes = root.GetProperty("dailyMinutes").GetInt32();
                var startStr = root.GetProperty("scheduleStart").GetString() ?? "08:00";
                var endStr = root.GetProperty("scheduleEnd").GetString() ?? "22:00";
                if (!TimeOnly.TryParse(startStr, out var start) || !TimeOnly.TryParse(endStr, out var end) || start >= end)
                    return Results.BadRequest(new { error = "Jam batas tidak valid." });
                if (minutes < 1 || minutes > 1440)
                    return Results.BadRequest(new { error = "Menit harian harus antara 1 dan 1440." });

                ScheduleExceptionRepository.Upsert(new ScheduleException
                {
                    UserId = userId,
                    ExceptionDate = date,
                    DailyMinutes = minutes,
                    ScheduleStart = start,
                    ScheduleEnd = end,
                    CreatedAt = DateTime.Now
                });

                return Results.Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Admin Schedule Exception Delete API
        app.MapDelete("/api/admin/schedule-exception", (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();
            var idStr = ctx.Request.Query["id"].ToString();
            if (!int.TryParse(idStr, out var id))
                return Results.BadRequest(new { error = "ID tidak valid." });

            ScheduleExceptionRepository.Delete(id);
            return Results.Ok(new { success = true });
        });


        // Admin Schedule Management POST API
        app.MapPost("/api/admin/schedule", async (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();
                var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                var userId = root.GetProperty("userId").GetInt32();
                var baseMinutes = root.GetProperty("baseDailyMinutes").GetInt32();
                var baseStartStr = root.GetProperty("baseScheduleStart").GetString() ?? "08:00";
                var baseEndStr = root.GetProperty("baseScheduleEnd").GetString() ?? "22:00";

                if (!TimeOnly.TryParse(baseStartStr, out var baseStart) || !TimeOnly.TryParse(baseEndStr, out var baseEnd) || baseStart >= baseEnd)
                {
                    return Results.BadRequest(new { error = "Jam batas standar tidak valid (Format HH:mm, jam mulai harus lebih awal dari jam selesai)." });
                }
                if (baseMinutes < 1 || baseMinutes > 1440)
                {
                    return Results.BadRequest(new { error = "Menit harian harus antara 1 dan 1440." });
                }

                // Save base limits
                LimitRepository.Upsert(new LimitConfig
                {
                    UserId = userId,
                    DailyMinutes = baseMinutes,
                    ScheduleStart = baseStart,
                    ScheduleEnd = baseEnd
                });

                // Process 7 days
                if (root.TryGetProperty("days", out var daysElement) && daysElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in daysElement.EnumerateArray())
                    {
                        var dayOfWeek = (DayOfWeek)d.GetProperty("dayOfWeek").GetInt32();
                        var isCustom = d.GetProperty("isCustom").GetBoolean();
                        if (isCustom)
                        {
                            var dMinutes = d.GetProperty("dailyMinutes").GetInt32();
                            var dStartStr = d.GetProperty("scheduleStart").GetString() ?? "08:00";
                            var dEndStr = d.GetProperty("scheduleEnd").GetString() ?? "22:00";
                            if (TimeOnly.TryParse(dStartStr, out var dStart) && TimeOnly.TryParse(dEndStr, out var dEnd) && dStart < dEnd)
                            {
                                ScheduleRepository.SaveDaySchedule(new DaySchedule
                                {
                                    UserId = userId,
                                    DayOfWeek = dayOfWeek,
                                    DailyMinutes = Math.Clamp(dMinutes, 1, 1440),
                                    ScheduleStart = dStart,
                                    ScheduleEnd = dEnd
                                });
                            }
                        }
                        else
                        {
                            ScheduleRepository.DeleteDaySchedule(userId, dayOfWeek);
                        }
                    }
                }

                return Results.Ok(new { success = true, message = "Jadwal berhasil disimpan." });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Admin Check Updates API
        app.MapGet("/api/admin/update/check", async (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();
            var res = await UpdateService.CheckForUpdatesAsync();
            return Results.Ok(new
            {
                currentVersion = AppVersion.DisplayName,
                latestVersion = res.LatestVersion,
                hasUpdate = res.HasUpdate,
                releaseTitle = res.ReleaseTitle,
                releaseNotes = res.ReleaseNotes,
                downloadUrl = res.DownloadUrl,
                error = res.Error
            });
        });

        // Admin Apply Update API
        app.MapPost("/api/admin/update/apply", async (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            var doc = JsonDocument.Parse(body);
            var downloadUrl = doc.RootElement.GetProperty("downloadUrl").GetString();
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return Results.BadRequest(new { error = "Download URL required." });
            }

            var (success, msg) = await UpdateService.DownloadAndApplyUpdateAsync(downloadUrl);
            return Results.Ok(new { success, message = msg });
        });

        // Admin Settings GET API
        app.MapGet("/api/admin/settings", (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();
            return Results.Ok(new
            {
                telegramConfigured = !string.IsNullOrWhiteSpace(SettingsRepository.Get(SettingsRepository.KeyTelegramBotToken)) &&
                                     !string.IsNullOrWhiteSpace(SettingsRepository.Get(SettingsRepository.KeyTelegramChatId)),
                telegramNotifySignIn = SettingsRepository.IsTelegramNotifySignInEnabled(),
                telegramNotifySignOut = SettingsRepository.IsTelegramNotifySignOutEnabled(),
                telegramNotifyAdminLogon = SettingsRepository.IsTelegramNotifyAdminLogonEnabled(),
                promptResponsePresets = SettingsRepository.Get(SettingsRepository.KeyPromptResponsePresets, SettingsRepository.DefaultPromptResponsePresets)
            });
        });

        // Admin Settings POST API
        app.MapPost("/api/admin/settings", async (HttpContext ctx) =>
        {
            if (!CheckAuth(ctx)) return Results.Unauthorized();
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();
                var doc = JsonDocument.Parse(body);

                if (doc.RootElement.TryGetProperty("telegramNotifySignIn", out var si))
                {
                    SettingsRepository.Set(SettingsRepository.KeyTelegramNotifySignIn, si.GetBoolean() ? "true" : "false");
                }
                if (doc.RootElement.TryGetProperty("telegramNotifySignOut", out var so))
                {
                    SettingsRepository.Set(SettingsRepository.KeyTelegramNotifySignOut, so.GetBoolean() ? "true" : "false");
                }
                if (doc.RootElement.TryGetProperty("telegramNotifyAdminLogon", out var al))
                {
                    SettingsRepository.Set(SettingsRepository.KeyTelegramNotifyAdminLogon, al.GetBoolean() ? "true" : "false");
                }
                if (doc.RootElement.TryGetProperty("promptResponsePresets", out var pr))
                {
                    SettingsRepository.Set(SettingsRepository.KeyPromptResponsePresets, pr.GetString() ?? SettingsRepository.DefaultPromptResponsePresets);
                }

                return Results.Ok(new { success = true });
            }
            catch
            {
                return Results.BadRequest(new { error = "Invalid payload" });
            }
        });

        _logger.Information("Starting embedded WebServer on http://0.0.0.0:5050");
        await app.RunAsync(stoppingToken);
    }

    private bool CheckAuth(HttpContext ctx)
    {
        var isTotpEnabled = SettingsRepository.Get(SettingsRepository.KeyTotpEnabled, "false") == "true";
        if (!isTotpEnabled) return true;
        return IsAdminAuthenticated(ctx);
    }

    private bool IsAdminAuthenticated(HttpContext ctx)
    {
        if (ctx.Request.Cookies.TryGetValue("pc_admin_session", out var token) && !string.IsNullOrEmpty(token))
        {
            lock (_validAdminTokens)
            {
                return _validAdminTokens.Contains(token);
            }
        }
        return false;
    }

    private async Task<ScreenCaptureRecord?> TriggerCaptureAndWaitAsync(string username, string trigger, int timeoutSeconds = 15)
    {
        var user = UserRepository.GetByUsername(username);
        if (user == null) return null;

        var session = _sessionTracker.ActiveSessions.Values.FirstOrDefault(s => s.UserSid == user.Sid);
        if (session == null || session.IsLocked) return null;

        var tcs = new TaskCompletionSource<ScreenCaptureRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(ScreenCaptureRecord record)
        {
            if (record.UserId == user.Id)
            {
                tcs.TrySetResult(record);
            }
        }

        OnCaptureReceived += Handler;
        try
        {
            var state = _captureStates.GetOrAdd(user.Username, _ => new CaptureCommandState());
            state.CaptureRequested = true;
            state.Trigger = trigger;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            cts.Token.Register(() => tcs.TrySetCanceled());

            return await tcs.Task;
        }
        catch
        {
            return null;
        }
        finally
        {
            OnCaptureReceived -= Handler;
        }
    }

public sealed class CaptureCommandState
{
    public bool CaptureRequested { get; set; }
    public bool WatchMode { get; set; }
    public DateTime WatchExpiresAt { get; set; }
    public string Trigger { get; set; } = "MANUAL";
}
}
