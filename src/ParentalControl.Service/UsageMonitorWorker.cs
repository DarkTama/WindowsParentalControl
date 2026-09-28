using System.Collections.Concurrent;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.Core.Platform;

namespace ParentalControl.Service;

public sealed class UsageMonitorWorker : BackgroundService
{
    private readonly SessionTracker _sessionTracker;
    private readonly Serilog.ILogger _logger;
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);
    private DateOnly _lastCleanupDate = DateOnly.MinValue;
    private readonly ConcurrentDictionary<int, HashSet<int>> _sentAlerts = new();

    public UsageMonitorWorker(SessionTracker sessionTracker, Serilog.ILogger logger)
    {
        _sessionTracker = sessionTracker;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Information("Usage monitor worker started");

        try
        {
            ProcessTick();
            RunCleanupIfNeeded();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during initial usage monitor tick");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TickInterval, stoppingToken);

            if (!_sessionTracker.IsAwake)
                continue;

            try
            {
                ProcessTick();
                RunCleanupIfNeeded();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during usage monitor tick");
            }
        }
    }

    private void ProcessTick()
    {
        _sessionTracker.TickAllSessions();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var now = TimeOnly.FromDateTime(DateTime.Now);
        var dayOfWeek = DateTime.Now.DayOfWeek;

        var alertSetting = SettingsRepository.Get(SettingsRepository.KeyAlertIntervals, "15,5,1");
        var thresholds = alertSetting.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : 0)
            .Where(v => v > 0)
            .OrderByDescending(v => v)
            .ToList();

        // Clean stale sessions from _sentAlerts
        var activeIds = _sessionTracker.ActiveSessions.Keys.ToHashSet();
        foreach (var key in _sentAlerts.Keys)
        {
            if (!activeIds.Contains(key))
            {
                _sentAlerts.TryRemove(key, out _);
            }
        }

        foreach (var (sessionId, session) in _sessionTracker.ActiveSessions)
        {
            if (session.IsLocked) continue;

            var user = UserRepository.GetBySid(session.UserSid);
            if (user is null || !user.IsRestricted) continue;

            var limit = ScheduleRepository.GetEffectiveLimit(user.Id, dayOfWeek);
            if (limit is null) continue;

            var usage = UsageRepository.GetUsage(user.Id, today);
            var totalAllowed = limit.DailyMinutes + (usage?.BonusMinutes ?? 0);
            var used = usage?.MinutesUsed ?? 0;
            var remainingDaily = Math.Max(0, totalAllowed - used);

            var remainingSchedule = int.MaxValue;
            if (now < limit.ScheduleEnd)
            {
                remainingSchedule = (int)(limit.ScheduleEnd.ToTimeSpan() - now.ToTimeSpan()).TotalMinutes;
            }
            else
            {
                remainingSchedule = 0;
            }

            // Check hard expirations first
            if (used >= totalAllowed)
            {
                _logger.Information("Daily limit reached for {Username} ({MinutesUsed}/{DailyMinutes} min, bonus: {Bonus} min)",
                    session.Username, used, limit.DailyMinutes, usage?.BonusMinutes ?? 0);
                EventRepository.LogEvent(session.UserSid, EventType.LIMIT_REACHED,
                    $"Used {used} of {totalAllowed} minutes (base: {limit.DailyMinutes}, bonus: {usage?.BonusMinutes ?? 0})");
                NotificationManager.SendMessage(sessionId, "Parental Control", SettingsRepository.GetMessage(SettingsRepository.KeyMsgLimitReached), isWarning: true, timeoutSeconds: 5);
                SessionManager.ForceLogoff(sessionId);
                EventRepository.LogEvent(session.UserSid, EventType.FORCED_LOGOUT, "Daily limit reached");
                _sessionTracker.RemoveSession(sessionId);
                _sentAlerts.TryRemove(sessionId, out _);
                continue;
            }

            if (now < limit.ScheduleStart || now >= limit.ScheduleEnd)
            {
                _logger.Information("Outside allowed schedule for {Username} (allowed {Start}-{End})",
                    session.Username, limit.ScheduleStart, limit.ScheduleEnd);
                NotificationManager.SendMessage(sessionId, "Parental Control", SettingsRepository.GetMessage(SettingsRepository.KeyMsgCurfewReached), isWarning: true, timeoutSeconds: 5);
                SessionManager.ForceLogoff(sessionId);
                EventRepository.LogEvent(session.UserSid, EventType.FORCED_LOGOUT, "Outside allowed schedule");
                _sessionTracker.RemoveSession(sessionId);
                _sentAlerts.TryRemove(sessionId, out _);
                continue;
            }

            // Check thresholds for notifications
            var sessionSent = _sentAlerts.GetOrAdd(sessionId, _ => new HashSet<int>());
            var effectiveRemaining = Math.Min(remainingDaily, remainingSchedule);

            foreach (var threshold in thresholds)
            {
                if (effectiveRemaining <= threshold && !sessionSent.Contains(threshold))
                {
                    if (remainingDaily <= remainingSchedule)
                    {
                        NotificationManager.SendWarning(sessionId, remainingDaily);
                    }
                    else
                    {
                        NotificationManager.SendCurfewWarning(sessionId, remainingSchedule, limit.ScheduleEnd);
                    }

                    sessionSent.Add(threshold);
                    _logger.Information("Dispatched {Threshold}m warning to {Username} on session {SessionId}",
                        threshold, session.Username, sessionId);
                    break; // alert once per tick
                }
            }
        }
    }

    private void RunCleanupIfNeeded()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today <= _lastCleanupDate) return;

        _lastCleanupDate = today;

        var eventsCutoff = DateTime.Now.AddDays(-DatabaseManager.RetentionDays);
        var usageCutoff = today.AddDays(-DatabaseManager.RetentionDays);

        var eventsDeleted = EventRepository.DeleteOlderThan(eventsCutoff);
        var usageDeleted = UsageRepository.DeleteOlderThan(usageCutoff);
        var appUsageDeleted = AppUsageRepository.DeleteOlderThan(usageCutoff);

        if (eventsDeleted > 0 || usageDeleted > 0 || appUsageDeleted > 0)
        {
            _logger.Information("Database cleanup: deleted {EventsDeleted} events, {UsageDeleted} usage records, {AppUsageDeleted} app records older than {Days} days",
                eventsDeleted, usageDeleted, appUsageDeleted, DatabaseManager.RetentionDays);
        }
    }
}
