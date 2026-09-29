using System.Security.Cryptography;
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

namespace ParentalControl.Service.Web;

public sealed class WebServerHost : BackgroundService
{
    private readonly SessionTracker _sessionTracker;
    private readonly TelegramBotService _telegramBotService;
    private readonly Serilog.ILogger _logger;
    private readonly HashSet<string> _validAdminTokens = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string ProcessName, string WindowTitle, DateTime LastSeen)> _liveActivities = new(StringComparer.OrdinalIgnoreCase);

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

            var limit = ScheduleRepository.GetEffectiveLimit(targetUser!.Id, dayOfWeek);
            var usage = UsageRepository.GetUsage(targetUser.Id, today);
            var totalAllowed = (limit?.DailyMinutes ?? 120) + (usage?.BonusMinutes ?? 0);
            var used = usage?.MinutesUsed ?? 0;
            var remaining = Math.Max(0, totalAllowed - used);
            var curfew = limit != null ? $"{limit.ScheduleStart:HH:mm} – {limit.ScheduleEnd:HH:mm}" : "08:00 – 22:00";

            var sessions = _sessionTracker.ActiveSessions.Values.ToList();
            var activeTargetSession = sessions.FirstOrDefault(s => s.UserSid == targetUser.Sid);
            bool isSessionActive = activeTargetSession != null;
            bool isSessionLocked = activeTargetSession?.IsLocked ?? false;

            var elapsedSec = activeTargetSession != null && !isSessionLocked ? (int)(DateTime.Now - activeTargetSession.LastTick).TotalSeconds : 0;
            var remainingSeconds = Math.Max(0, (remaining * 60) - Math.Min(59, Math.Max(0, elapsedSec)));

            var isOffline = !await TelegramBotService.CheckConnectivityAsync();
            var maxRequests = SettingsRepository.GetMaxDailyRequests();
            var countToday = GraceRequestRepository.GetTodayRequestCount(targetUser.Id, today);
            var hasPending = GraceRequestRepository.HasPendingRequest(targetUser.Id, today);
            var latestReq = GraceRequestRepository.GetTodayRequest(targetUser.Id, today);

            var availableUsernames = restrictedUsers.Select(u => u.Username).ToList();

            var html = RequestPage.Render(
                targetUser.Username,
                remaining,
                remainingSeconds,
                curfew,
                isOffline,
                countToday,
                maxRequests,
                hasPending,
                latestReq?.Status,
                isAdminTesting,
                availableUsernames,
                isSessionActive,
                isSessionLocked);
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
                var minutes = doc.RootElement.GetProperty("minutes").GetInt32();
                var reason = doc.RootElement.GetProperty("reason").GetString() ?? "";
                var requestedUser = doc.RootElement.TryGetProperty("username", out var uProp) ? uProp.GetString() : null;

                if (minutes <= 0 || minutes > 120 || string.IsNullOrWhiteSpace(reason))
                {
                    return Results.BadRequest(new { error = "Jumlah menit tidak valid atau alasan harus diisi." });
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

                var req = GraceRequestRepository.Create(user.Id, today, minutes, reason);

                // Dispatch Telegram alert
                _ = _telegramBotService.SendGraceRequestAlertAsync(req, user.Username);

                return Results.Ok(new { success = true, message = $"Permintaan tambahan waktu +{minutes} menit berhasil dikirim ke administrator." });
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
            if (user == null || !user.IsRestricted)
            {
                return Results.Ok(new
                {
                    username,
                    isRestricted = false,
                    remainingSeconds = 0,
                    curfew = "Akun tidak dibatasi",
                    isLocked = false
                });
            }

            var today = DateOnly.FromDateTime(DateTime.Now);
            var dayOfWeek = DateTime.Now.DayOfWeek;
            var limit = ScheduleRepository.GetEffectiveLimit(user.Id, dayOfWeek);
            var usage = UsageRepository.GetUsage(user.Id, today);
            var totalAllowed = (limit?.DailyMinutes ?? 120) + (usage?.BonusMinutes ?? 0);
            var used = usage?.MinutesUsed ?? 0;
            var remMinutes = Math.Max(0, totalAllowed - used);

            var sessions = _sessionTracker.ActiveSessions.Values.ToList();
            var session = sessions.FirstOrDefault(s => s.UserSid == user.Sid);
            var isLocked = session?.IsLocked ?? false;
            var elapsedSec = session != null && !isLocked ? (int)(DateTime.Now - session.LastTick).TotalSeconds : 0;
            var remainingSeconds = Math.Max(0, (remMinutes * 60) - Math.Min(59, Math.Max(0, elapsedSec)));

            var curfew = limit != null ? $"{limit.ScheduleStart:HH:mm} – {limit.ScheduleEnd:HH:mm}" : "08:00 – 22:00";

            return Results.Ok(new
            {
                username = user.Username,
                isRestricted = true,
                remainingSeconds,
                curfew,
                isLocked
            });
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

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(processName))
                {
                    return Results.BadRequest();
                }

                var user = UserRepository.GetByUsername(username);
                if (user != null && user.IsRestricted)
                if (user != null && user.IsRestricted)
                {
                    _liveActivities[user.Username] = (processName, windowTitle, DateTime.Now);
                    var today = DateOnly.FromDateTime(DateTime.Now);
                    AppUsageRepository.AddMinutes(user.Id, today, processName, windowTitle, 1);
                }

                return Results.Ok(new { success = true });
            }
            catch
            {
                return Results.BadRequest();
            }
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
                var limit = isRestricted ? ScheduleRepository.GetEffectiveLimit(user.Id, dayOfWeek) : null;
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

            return Results.Ok(new
            {
                sessions = sessionList,
                requests = recentRequests,
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
}
