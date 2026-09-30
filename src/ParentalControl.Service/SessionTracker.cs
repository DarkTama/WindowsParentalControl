using System.Collections.Concurrent;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.Core.Platform;
using ParentalControl.Core.Services;
namespace ParentalControl.Service;

public sealed record ActiveSession(int SessionId, string UserSid, string Username, DateTime LastTick, bool IsLocked = false);

public sealed class SessionTracker
{
    private readonly ConcurrentDictionary<int, ActiveSession> _activeSessions = new();
    private readonly ConcurrentDictionary<int, DateTime> _lastUnlockRejectNotification = new();
    private volatile bool _isAwake = true;
    private readonly Serilog.ILogger _logger;
    private readonly TelegramBotService? _telegramBotService;

    public SessionTracker(Serilog.ILogger logger) : this(null, logger) { }

    public SessionTracker(TelegramBotService? telegramBotService, Serilog.ILogger logger)
    {
        _telegramBotService = telegramBotService;
        _logger = logger;
    }

    public bool IsAwake => _isAwake;

    public IReadOnlyDictionary<int, ActiveSession> ActiveSessions => _activeSessions;
    public ActiveSession? GetConsoleSession()
    {
        var consoleId = SessionManager.GetActiveConsoleSessionId();
        if (consoleId >= 0 && _activeSessions.TryGetValue(consoleId, out var session))
        {
            return session;
        }
        return null;
    }


    public void Initialize()
    {
        var sessions = SessionManager.GetLoggedOnSessions();
        foreach (var (sessionId, username, sid, isLocked) in sessions)
        {
            if (sid is null) continue;

            var user = UserRepository.GetBySid(sid);
            if (user is null)
            {
                user = UserRepository.Upsert(sid, username, false);
            }

            _activeSessions.TryAdd(sessionId, new ActiveSession(sessionId, sid, username, DateTime.Now, isLocked));
            _logger.Information("Recovered existing session: {Username} (SID: {Sid}) on session {SessionId} (Locked: {IsLocked})",
                username, sid, sessionId, isLocked);

            if (user.IsRestricted && !isLocked)
            {
                EnsureAgentRunning(sessionId);
            }
        }
    }

    public void OnUserLogon(int sessionId)
    {
        var username = SessionManager.GetSessionUsername(sessionId);
        var sid = SessionManager.GetSessionUserSid(sessionId);

        if (username is null || sid is null)
        {
            _logger.Warning("Could not resolve user for session {SessionId}", sessionId);
            return;
        }

        var user = UserRepository.GetBySid(sid);
        if (user is null)
        {
            user = UserRepository.Upsert(sid, username, false);
        }

        if (!user.IsRestricted)
        {
            _activeSessions.TryAdd(sessionId, new ActiveSession(sessionId, sid, username, DateTime.Now));
            EventRepository.LogEvent(sid, EventType.LOGIN);
            _logger.Information("User logged in (unrestricted): {Username}", username);

            if (SettingsRepository.IsTelegramNotifyAdminLogonEnabled() && _telegramBotService != null)
            {
                _ = Task.Run(() => _telegramBotService.SendAdminSignInAlertAsync(sessionId, username, Environment.MachineName, DateTime.Now));
            }
            return;
        }

        var nowTime = TimeOnly.FromDateTime(DateTime.Now);
        var dayOfWeek = DateTime.Now.DayOfWeek;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var limit = ScheduleRepository.GetEffectiveLimit(user.Id, dayOfWeek, today);
        UsageRecord? usage = null;
        if (limit is not null)
        {
            if (nowTime < limit.ScheduleStart || nowTime >= limit.ScheduleEnd)
            {
                _logger.Information("Login denied (outside schedule): {Username}", username);
                EventRepository.LogEvent(sid, EventType.LOGIN_DENIED, "Outside allowed schedule");
                Thread.Sleep(300);
                NotificationManager.SendMessage(sessionId, "Parental Control", SettingsRepository.GetMessage(SettingsRepository.KeyMsgLoginDeniedCurfew), isWarning: true, timeoutSeconds: 5, wait: true);
                SessionManager.ForceLogoff(sessionId);
                _activeSessions.TryRemove(sessionId, out _);
                return;
            }

            // today already resolved
            usage = UsageRepository.GetUsage(user.Id, today);
            var totalAllowed = limit.DailyMinutes + (usage?.BonusMinutes ?? 0);
            if (usage is not null && usage.MinutesUsed >= totalAllowed)
            {
                _logger.Information("Login denied (limit reached): {Username}", username);
                EventRepository.LogEvent(sid, EventType.LOGIN_DENIED, "Daily limit already reached");
                Thread.Sleep(300);
                NotificationManager.SendMessage(sessionId, "Parental Control", SettingsRepository.GetMessage(SettingsRepository.KeyMsgLoginDeniedLimit), isWarning: true, timeoutSeconds: 5, wait: true);
                SessionManager.ForceLogoff(sessionId);
                _activeSessions.TryRemove(sessionId, out _);
                return;
            }
        }

        _activeSessions.TryAdd(sessionId, new ActiveSession(sessionId, sid, username, DateTime.Now));

        var loginDetail = "";
        if (limit is not null)
        {
            var totalAllowed = limit.DailyMinutes + (usage?.BonusMinutes ?? 0);
            var remaining = Math.Max(0, totalAllowed - (usage?.MinutesUsed ?? 0));
            loginDetail = $"Remaining: {remaining / 60}h {remaining % 60}m";
            NotificationManager.SendLogonBanner(sessionId, username, remaining, limit.ScheduleStart, limit.ScheduleEnd);
        }
        EventRepository.LogEvent(sid, EventType.LOGIN, loginDetail);
        _logger.Information("User logged in (restricted): {Username}", username);
        EnsureAgentRunning(sessionId);

        if (SettingsRepository.IsTelegramNotifySignInEnabled() && _telegramBotService != null)
        {
            var remaining = limit != null ? Math.Max(0, (limit.DailyMinutes + (usage?.BonusMinutes ?? 0)) - (usage?.MinutesUsed ?? 0)) : 0;
            var start = limit?.ScheduleStart;
            var end = limit?.ScheduleEnd;
            _ = Task.Run(() => _telegramBotService.SendUserSignInNotificationAsync(username, Environment.MachineName, DateTime.Now, remaining, start, end));
        }
    }

    public void OnUserLogoff(int sessionId)
    {
        _lastUnlockRejectNotification.TryRemove(sessionId, out _);
        if (_activeSessions.TryRemove(sessionId, out var session))
        {
            FlushSessionTime(session);
            EventRepository.LogEvent(session.UserSid, EventType.LOGOUT);
            _logger.Information("User logged off: {Username}", session.Username);
            var user = UserRepository.GetBySid(session.UserSid);
            if (user != null && user.IsRestricted && SettingsRepository.IsTelegramNotifySignOutEnabled() && _telegramBotService != null)
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                var usage = UsageRepository.GetUsage(user.Id, today);
                var used = usage?.MinutesUsed ?? 0;
                _ = Task.Run(() => _telegramBotService.SendUserSignOutNotificationAsync(session.Username, Environment.MachineName, DateTime.Now, used));
            }
        }
    }

    public void OnSleep()
    {
        _isAwake = false;
        _logger.Information("System entering sleep");

        var now = DateTime.Now;
        foreach (var (sessionId, session) in _activeSessions)
        {
            FlushSessionTime(session);
            _activeSessions[sessionId] = session with { LastTick = now };
            EventRepository.LogEvent(session.UserSid, EventType.SLEEP);
        }
    }

    public void OnWake()
    {
        _isAwake = true;
        _logger.Information("System resuming from sleep");

        var now = DateTime.Now;
        foreach (var (sessionId, session) in _activeSessions)
        {
            var sleepMinutes = (int)(now - session.LastTick).TotalMinutes;
            var detail = sleepMinutes > 1 ? $"After ~{sleepMinutes} min sleep" : "";
            _activeSessions[sessionId] = session with { LastTick = now };
            EventRepository.LogEvent(session.UserSid, EventType.WAKE, detail);
        }
    }

    public void RemoveSession(int sessionId)
    {
        _activeSessions.TryRemove(sessionId, out _);
    }

    public void OnSessionLock(int sessionId, string reason = "")
    {
        if (_activeSessions.TryGetValue(sessionId, out var session))
        {
            FlushSessionTime(session);
            _activeSessions[sessionId] = session with { IsLocked = true, LastTick = DateTime.Now };
            EventRepository.LogEvent(session.UserSid, EventType.SESSION_LOCKED, reason);
            _logger.Information("Session {SessionId} ({Username}) locked: {Reason}", sessionId, session.Username, reason);
        }
        else
        {
            var username = SessionManager.GetSessionUsername(sessionId);
            var sid = SessionManager.GetSessionUserSid(sessionId);
            if (username != null && sid != null)
            {
                UserRepository.GetBySid(sid);
                _activeSessions.TryAdd(sessionId, new ActiveSession(sessionId, sid, username, DateTime.Now, true));
                EventRepository.LogEvent(sid, EventType.SESSION_LOCKED, reason);
                _logger.Information("Session {SessionId} ({Username}) tracked as locked: {Reason}", sessionId, username, reason);
            }
        }
    }

    public void OnSessionUnlock(int sessionId)
    {
        if (!_activeSessions.TryGetValue(sessionId, out var session))
        {
            var username = SessionManager.GetSessionUsername(sessionId);
            var sid = SessionManager.GetSessionUserSid(sessionId);
            if (username != null && sid != null)
            {
                var recoveredUser = UserRepository.GetBySid(sid) ?? UserRepository.Upsert(sid, username, false);
                session = new ActiveSession(sessionId, sid, username, DateTime.Now, false);
                _activeSessions.TryAdd(sessionId, session);
                _logger.Information("Session {SessionId} ({Username}) restored on unlock", sessionId, username);
            }
            else
            {
                _logger.Warning("Session {SessionId} unlocked but user could not be determined", sessionId);
                return;
            }
        }

        var user = UserRepository.GetBySid(session.UserSid);
        if (user is not null && user.IsRestricted)
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var nowTime = TimeOnly.FromDateTime(DateTime.Now);
            var dayOfWeek = DateTime.Now.DayOfWeek;
            var limit = ScheduleRepository.GetEffectiveLimit(user.Id, dayOfWeek, today);

            if (limit is not null)
            {
                var usage = UsageRepository.GetUsage(user.Id, today);
                var totalAllowed = limit.DailyMinutes + (usage?.BonusMinutes ?? 0);
                var isCurfewExpired = nowTime < limit.ScheduleStart || nowTime >= limit.ScheduleEnd;
                var isLimitExpired = usage is not null && usage.MinutesUsed >= totalAllowed;

                if (isCurfewExpired || isLimitExpired)
                {
                    var reason = isCurfewExpired ? "Outside allowed schedule on unlock" : "Daily limit reached on unlock";
                    var message = isCurfewExpired
                        ? SettingsRepository.GetMessage(SettingsRepository.KeyMsgLoginDeniedCurfew)
                        : SettingsRepository.GetMessage(SettingsRepository.KeyMsgLoginDeniedLimit);

                    _logger.Information("Unlock rejected ({Reason}): {Username} on session {SessionId}", reason, session.Username, sessionId);
                    EventRepository.LogEvent(session.UserSid, EventType.LOGIN_DENIED, reason);

                    _activeSessions[sessionId] = session with { IsLocked = true, LastTick = DateTime.Now };

                    var now = DateTime.Now;
                    var shouldNotify = !_lastUnlockRejectNotification.TryGetValue(sessionId, out var lastTime) || (now - lastTime) > TimeSpan.FromSeconds(10);

                    if (shouldNotify)
                    {
                        _lastUnlockRejectNotification[sessionId] = now;
                        Thread.Sleep(300);
                        NotificationManager.SendMessage(sessionId, "Parental Control", message, isWarning: true, timeoutSeconds: 5, wait: true);
                    }

                    SessionManager.LockSession(sessionId);
                    return;
                }
            }

            EnsureAgentRunning(sessionId);
        }

        _activeSessions[sessionId] = session with { IsLocked = false, LastTick = DateTime.Now };
        EventRepository.LogEvent(session.UserSid, EventType.SESSION_UNLOCKED, "Session unlocked");
        _logger.Information("Session {SessionId} ({Username}) unlocked", sessionId, session.Username);
    }

    public void EnsureAgentRunning(int sessionId)
    {
        try
        {
            if (SessionManager.IsProcessRunningInSession("ParentalControl.Agent", sessionId))
            {
                return;
            }

            var agentPath = GetAgentExecutablePath();
            if (agentPath == null)
            {
                _logger.Warning("Agent executable not found; cannot launch in session {SessionId}", sessionId);
                return;
            }

            var launched = SessionManager.LaunchProcessInSession(sessionId, agentPath);
            if (launched)
            {
                _logger.Information("Successfully launched Agent in session {SessionId} ({Path})", sessionId, agentPath);
            }
            else
            {
                _logger.Warning("Failed to launch Agent in session {SessionId}", sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error launching Agent in session {SessionId}", sessionId);
        }
    }

    private static string? GetAgentExecutablePath()
    {
        var baseDir = AppContext.BaseDirectory;
        // 1. Production layout: service is in {app}\service, agent is in {app}\agent
        var path1 = Path.GetFullPath(Path.Combine(baseDir, "..", "agent", "ParentalControl.Agent.exe"));
        if (File.Exists(path1)) return path1;

        // 2. Same directory (tests or flat build)
        var path2 = Path.Combine(baseDir, "ParentalControl.Agent.exe");
        if (File.Exists(path2)) return path2;

        // 3. Standard Program Files installation path
        var path3 = @"C:\Program Files\ParentalControl\agent\ParentalControl.Agent.exe";
        if (File.Exists(path3)) return path3;

        return null;
    }

    public void TickAllSessions()
    {
        var now = DateTime.Now;
        foreach (var (sessionId, session) in _activeSessions)
        {
            if (session.IsLocked)
            {
                // Screen is locked — do not deduct or consume daily limits
                _activeSessions[sessionId] = session with { LastTick = now };
                continue;
            }
            if (now < session.LastTick)
            {
                _logger.Warning("Clock manipulation detected for {Username}: current time {Now} is before last tick {LastTick}",
                    session.Username, now, session.LastTick);

                var clockUser = UserRepository.GetBySid(session.UserSid);
                if (clockUser is not null && clockUser.IsRestricted)
                {
                    var limit = LimitRepository.GetByUserId(clockUser.Id);
                    if (limit is not null)
                    {
                        var lockDate = DateOnly.FromDateTime(now);
                        UsageRepository.AddMinutes(clockUser.Id, lockDate, limit.DailyMinutes);
                        EventRepository.LogEvent(session.UserSid, EventType.CLOCK_TAMPER,
                            $"Clock went backwards: {session.LastTick:HH:mm:ss} → {now:HH:mm:ss}. Locked for today.");
                        SessionManager.LockSession(sessionId);
                        _activeSessions.TryRemove(sessionId, out _);
                        continue;
                    }
                }

                EventRepository.LogEvent(session.UserSid, EventType.CLOCK_TAMPER,
                    $"Clock went backwards: {session.LastTick:HH:mm:ss} → {now:HH:mm:ss}");
                _activeSessions[sessionId] = session with { LastTick = now };
                continue;
            }

            var elapsed = (int)(now - session.LastTick).TotalMinutes;
            if (elapsed < 1) continue;

            var user = UserRepository.GetBySid(session.UserSid);
            if (user is null) continue;

            var today = DateOnly.FromDateTime(now);

            if (elapsed > 2)
            {
                _logger.Warning("Time gap detected ({Elapsed} min) for {Username} — possible undetected sleep/hibernate",
                    elapsed, session.Username);
                EventRepository.LogEvent(session.UserSid, EventType.SLEEP, $"Detected retroactively (~{elapsed} min gap)");
                EventRepository.LogEvent(session.UserSid, EventType.WAKE, $"Detected retroactively (~{elapsed} min gap)");
                UsageRepository.AddMinutes(user.Id, today, 1);
                _activeSessions[sessionId] = session with { LastTick = now };
                continue;
            }

            UsageRepository.AddMinutes(user.Id, today, elapsed);
            _activeSessions[sessionId] = session with { LastTick = now };
        }
    }

    private void FlushSessionTime(ActiveSession session)
    {
        if (session.IsLocked) return;
        var now = DateTime.Now;
        if (now < session.LastTick) return;
        var elapsed = (int)(now - session.LastTick).TotalMinutes;
        if (elapsed < 1) return;
        if (elapsed > 2) elapsed = 1;

        var user = UserRepository.GetBySid(session.UserSid);
        if (user is null) return;

        var today = DateOnly.FromDateTime(now);
        UsageRepository.AddMinutes(user.Id, today, elapsed);
    }
}
