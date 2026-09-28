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
            var nowTime = TimeOnly.FromDateTime(DateTime.Now);
            var dayOfWeek = DateTime.Now.DayOfWeek;

            // Detect user from active session
            var sessions = _sessionTracker.ActiveSessions.Values.ToList();
            var activeSession = sessions.FirstOrDefault();
            User? targetUser = null;

            if (activeSession != null)
            {
                targetUser = UserRepository.GetBySid(activeSession.UserSid);
            }

            if (targetUser == null)
            {
                targetUser = UserRepository.GetAll().FirstOrDefault(u => u.IsRestricted);
            }

            if (targetUser == null)
            {
                return Results.Content("<h3>No restricted users found on this system.</h3>", "text/html");
            }

            var limit = ScheduleRepository.GetEffectiveLimit(targetUser.Id, dayOfWeek);
            var usage = UsageRepository.GetUsage(targetUser.Id, today);
            var totalAllowed = (limit?.DailyMinutes ?? 120) + (usage?.BonusMinutes ?? 0);
            var used = usage?.MinutesUsed ?? 0;
            var remaining = Math.Max(0, totalAllowed - used);
            var curfew = limit != null ? $"{limit.ScheduleStart:HH:mm} – {limit.ScheduleEnd:HH:mm}" : "08:00 – 22:00";

            var isOffline = !await TelegramBotService.CheckConnectivityAsync();
            var maxRequests = SettingsRepository.GetMaxDailyRequests();
            var countToday = GraceRequestRepository.GetTodayRequestCount(targetUser.Id, today);
            var hasPending = GraceRequestRepository.HasPendingRequest(targetUser.Id, today);
            var latestReq = GraceRequestRepository.GetTodayRequest(targetUser.Id, today);

            var html = RequestPage.Render(
                targetUser.Username,
                remaining,
                curfew,
                isOffline,
                countToday,
                maxRequests,
                hasPending,
                latestReq?.Status);

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

                if (minutes <= 0 || minutes > 120 || string.IsNullOrWhiteSpace(reason))
                {
                    return Results.BadRequest(new { error = "Jumlah menit tidak valid atau alasan harus diisi." });
                }

                var today = DateOnly.FromDateTime(DateTime.Now);

                // Detect active user
                var sessions = _sessionTracker.ActiveSessions.Values.ToList();
                var activeSession = sessions.FirstOrDefault();
                User? user = activeSession != null ? UserRepository.GetBySid(activeSession.UserSid) : null;
                user ??= UserRepository.GetAll().FirstOrDefault(u => u.IsRestricted);

                if (user == null)
                {
                    return Results.BadRequest(new { error = "Pengguna tidak ditemukan." });
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

            var sessionList = new List<object>();
            foreach (var (sessionId, activeSession) in _sessionTracker.ActiveSessions)
            {
                var user = UserRepository.GetBySid(activeSession.UserSid);
                if (user == null) continue;

                var limit = ScheduleRepository.GetEffectiveLimit(user.Id, dayOfWeek);
                var usage = UsageRepository.GetUsage(user.Id, today);
                var totalAllowed = (limit?.DailyMinutes ?? 120) + (usage?.BonusMinutes ?? 0);
                var used = usage?.MinutesUsed ?? 0;
                var remaining = Math.Max(0, totalAllowed - used);

                sessionList.Add(new
                {
                    sessionId,
                    username = activeSession.Username,
                    userId = user.Id,
                    remainingMinutes = remaining,
                    totalAllowed,
                    curfew = limit != null ? $"{limit.ScheduleStart:HH:mm}–{limit.ScheduleEnd:HH:mm}" : "08:00–22:00"
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

            var todayAppUsage = users.Values.Where(u => u.IsRestricted)
                .SelectMany(u => AppUsageRepository.GetForUserAndDate(u.Id, today).Select(a => new
                {
                    username = u.Username,
                    a.ProcessName,
                    a.WindowTitle,
                    a.Minutes
                }))
                .OrderByDescending(a => a.Minutes)
                .Take(25);

            return Results.Ok(new
            {
                sessions = sessionList,
                requests = recentRequests,
                appUsage = todayAppUsage
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
                    NotificationManager.SendMessage(active.SessionId, "Parental Control — Extra Time Granted!",
                        $"Administrator granted you +{minutes} minutes of screen time!", false, 15);
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
                        NotificationManager.SendMessage(active.SessionId, "Parental Control — Extra Time Approved!",
                            $"Your request for screen time was approved (+{minutes}m)!", false, 15);
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
