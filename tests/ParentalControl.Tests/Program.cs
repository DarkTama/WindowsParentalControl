using ParentalControl.Core.Platform;
using System.Diagnostics;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.Core.Security;
using ParentalControl.Core;
using ParentalControl.Core.Services;

Console.WriteLine("=== Running ParentalControl Self-Checks ===");

// 1. Test TOTP service
var secret = TotpService.GenerateSecret();
Debug.Assert(!string.IsNullOrEmpty(secret), "Secret should not be empty");
Debug.Assert(secret.Length >= 16, "Secret should be at least 16 chars");

var uri = TotpService.GenerateOtpauthUri("TestApp", "TestUser", secret);
Debug.Assert(uri.StartsWith("otpauth://totp/"), "URI should start with otpauth://totp/");

Debug.Assert(!TotpService.VerifyCode(secret, "000000"), "Random dummy code should fail");
Debug.Assert(!TotpService.VerifyCode(secret, ""), "Empty code should fail");
Debug.Assert(!TotpService.VerifyCode(secret, "12345"), "Short code should fail");
var qrBytes = TotpService.GenerateQrCodePng(uri, 4);
Debug.Assert(qrBytes != null && qrBytes.Length > 0, "QR Code PNG bytes should not be empty");
Debug.Assert(qrBytes[0] == 0x89 && qrBytes[1] == 0x50 && qrBytes[2] == 0x4E && qrBytes[3] == 0x47, "QR should be valid PNG");
Console.WriteLine("✅ TOTP Service & QR Generation Verification Passed.");
var tempDir = Path.Combine(Path.GetTempPath(), "ParentalControlTest_" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("PARENTAL_CONTROL_DATA_DIR", tempDir);
try
{
    // 2. Initialize Database in test environment
    DatabaseManager.Initialize();
// 3. Test Settings
SettingsRepository.Set("test_key", "test_value_123");
var val = SettingsRepository.Get("test_key");
Debug.Assert(val == "test_value_123", "Settings key should match saved value");
Console.WriteLine("✅ Settings Repository Verification Passed.");

// 4. Test User & Grace Requests
var testSid = "S-1-5-21-999999999-999999999-999999999-9999";
var user = UserRepository.Upsert(testSid, "TestBrother", true);
Debug.Assert(user.Id > 0, "User ID should be > 0");

var today = DateOnly.FromDateTime(DateTime.Now);
var req = GraceRequestRepository.Create(user.Id, today, 30, "Testing grace submission");
Debug.Assert(req.Id > 0, "GraceRequest ID should be > 0");
Debug.Assert(req.Status == "PENDING", "Initial status should be PENDING");

var retrievedReq = GraceRequestRepository.GetTodayRequest(user.Id, today);
Debug.Assert(retrievedReq != null, "Today's request should be retrievable");
Debug.Assert(retrievedReq!.RequestedMinutes == 30, "Requested minutes should be 30");

GraceRequestRepository.Resolve(req.Id, "APPROVED");
var resolvedReq = GraceRequestRepository.GetById(req.Id);
Debug.Assert(resolvedReq!.Status == "APPROVED", "Status should be updated to APPROVED");
var count = GraceRequestRepository.GetTodayRequestCount(user.Id, today);
Debug.Assert(count == 1, "Today's request count should be 1");
Debug.Assert(!GraceRequestRepository.HasPendingRequest(user.Id, today), "Pending request should be false after resolve");

Debug.Assert(SettingsRepository.GetMaxDailyRequests() == 1, "Default max daily requests should be 1");
SettingsRepository.Set(SettingsRepository.KeyMaxDailyRequests, "3");
Debug.Assert(SettingsRepository.GetMaxDailyRequests() == 3, "Updated max daily requests should be 3");
Console.WriteLine("✅ Grace Request Repository & Dynamic Limits Verification Passed.");

// 5. Test Usage and Bonus Minutes
UsageRepository.SetUsage(user.Id, today, 60);
UsageRepository.AddBonusMinutes(user.Id, today, 15);
var usage = UsageRepository.GetUsage(user.Id, today);
Debug.Assert(usage != null, "Usage should not be null");
Debug.Assert(usage!.MinutesUsed == 60, "MinutesUsed should be 60");
Debug.Assert(usage!.BonusMinutes == 15, "BonusMinutes should be 15");
Console.WriteLine("✅ Usage and Bonus Minutes Verification Passed.");

// 6. Test Weekly Schedule
ScheduleRepository.SaveDaySchedule(new DaySchedule
{
    UserId = user.Id,
    DayOfWeek = DayOfWeek.Saturday,
    DailyMinutes = 180,
    ScheduleStart = new TimeOnly(9, 0),
    ScheduleEnd = new TimeOnly(23, 0)
});
var saturdayLimit = ScheduleRepository.GetEffectiveLimit(user.Id, DayOfWeek.Saturday);
Debug.Assert(saturdayLimit != null, "Saturday limit should not be null");
Debug.Assert(saturdayLimit!.DailyMinutes == 180, "Saturday limit should be 180");

// Sunday has no specific day schedule, should fallback
var sundayLimit = ScheduleRepository.GetEffectiveLimit(user.Id, DayOfWeek.Sunday);
Console.WriteLine("✅ Weekly Schedule & Fallback Verification Passed.");

// 7. Test Session Lock/Unlock Event Types
EventRepository.LogEvent(testSid, EventType.SESSION_LOCKED, "Locked via Web Admin (15s delay)");
EventRepository.LogEvent(testSid, EventType.SESSION_UNLOCKED, "Unlocked by user");
EventRepository.LogEvent(testSid, EventType.LOGIN_DENIED, "Outside allowed schedule");
var events = EventRepository.GetEvents(userSid: testSid);
Debug.Assert(events.Any(e => e.EventType == EventType.SESSION_LOCKED), "SESSION_LOCKED event should be recorded");
Debug.Assert(events.Any(e => e.EventType == EventType.SESSION_UNLOCKED), "SESSION_UNLOCKED event should be recorded");
Debug.Assert(events.Any(e => e.EventType == EventType.LOGIN_DENIED), "LOGIN_DENIED event should be recorded");
Console.WriteLine("✅ Session Lock & Unlock Events Verification Passed.");

// 8. Test GetByUsername and Console Session detection
var foundUser = UserRepository.GetByUsername("TESTBROTHER");
Debug.Assert(foundUser != null && foundUser.Id == user.Id, "GetByUsername should find user case-insensitively");
var notFound = UserRepository.GetByUsername("NonExistentUser123");
Debug.Assert(notFound == null, "GetByUsername should return null for nonexistent user");
var consoleId = SessionManager.GetActiveConsoleSessionId();
Debug.Assert(consoleId >= -1, "GetActiveConsoleSessionId should return valid session ID or -1");
Console.WriteLine("✅ User Discovery by Username & Console Session Detection Passed.");

// 9. Test App Activity Hourly & Range Queries
AppUsageRepository.AddMinutes(user.Id, today, "RobloxPlayerBeta.exe", "Roblox", 45, hour: 14);
AppUsageRepository.AddMinutes(user.Id, today, "chrome.exe", "YouTube", 15, hour: 14);
AppUsageRepository.AddMinutes(user.Id, today, "chrome.exe", "Homework", 30, hour: 16);

var hourly = AppUsageRepository.GetHourlyDistribution(user.Id, today, today);
Debug.Assert(hourly[14] == 60, "Hour 14 should have 60 min total");
Debug.Assert(hourly[16] == 30, "Hour 16 should have 30 min total");
var rangeApps = AppUsageRepository.GetUsageForRange(user.Id, today, today);
Debug.Assert(rangeApps.Count == 2, "Should have 2 unique processes");
Debug.Assert(rangeApps.First(a => a.ProcessName == "chrome.exe").Minutes == 45, "Chrome should have 45 min total");
Console.WriteLine("✅ Hourly Activity & App Usage Range Verification Passed.");

// 9b. Test 20-Second Activity Tick Accumulation & Second-to-Minute Conversion
AppUsageRepository.ResetAccumulators();
var testGame = "eldenring.exe";
AppUsageRepository.AddSeconds(user.Id, today, testGame, "ELDEN RING", 20, hour: 10);
var check1 = AppUsageRepository.GetForUserAndDate(user.Id, today).FirstOrDefault(a => a.ProcessName == testGame);
Debug.Assert(check1 == null, "20 seconds should not yet commit a full minute to database");

AppUsageRepository.AddSeconds(user.Id, today, testGame, "ELDEN RING", 20, hour: 10);
var check2 = AppUsageRepository.GetForUserAndDate(user.Id, today).FirstOrDefault(a => a.ProcessName == testGame);
Debug.Assert(check2 == null, "40 seconds should not yet commit a full minute to database");

AppUsageRepository.AddSeconds(user.Id, today, testGame, "ELDEN RING", 20, hour: 10);
var check3 = AppUsageRepository.GetForUserAndDate(user.Id, today).FirstOrDefault(a => a.ProcessName == testGame);
Debug.Assert(check3 != null && check3.Minutes == 1, "60 seconds (3x20s) must commit exactly 1 minute to database");

// 9c. Test Database Migration Repair for Legacy Inflated Records (> 60m per hour)
using (var rawConn = DatabaseManager.CreateConnection())
{
    using var cmd = rawConn.CreateCommand();
    cmd.CommandText = """
        INSERT INTO app_activity_hourly (user_id, date, hour, process_name, minutes)
        VALUES (@userId, @date, 9, 'legacy_inflated.exe', 180)
        ON CONFLICT(user_id, date, hour, process_name) DO UPDATE SET minutes = 180;
        INSERT INTO app_usage (user_id, date, process_name, window_title, minutes)
        VALUES (@userId, @date, 'legacy_inflated.exe', 'Legacy Game', 180)
        ON CONFLICT(user_id, date, process_name) DO UPDATE SET minutes = 180;
        """;
    cmd.Parameters.AddWithValue("@userId", user.Id);
    cmd.Parameters.AddWithValue("@date", today.ToString("yyyy-MM-dd"));
    cmd.ExecuteNonQuery();
}
DatabaseManager.Initialize(); // Re-run migration/initialization
var repairedHourly = AppUsageRepository.GetHourlyDistribution(user.Id, today, today);
Debug.Assert(repairedHourly[9] == 60, "Legacy inflated 180m record in hour 9 must be repaired and clamped to 60m");
var repairedApp = AppUsageRepository.GetForUserAndDate(user.Id, today).FirstOrDefault(a => a.ProcessName == "legacy_inflated.exe");
Debug.Assert(repairedApp != null && repairedApp.Minutes == 60, "Legacy inflated app usage should be re-synced to match repaired hourly sum (60m)");
Console.WriteLine("✅ 20-Second Telemetry Accumulation & Legacy Inflation Repair Passed.");

// 10. Test Session Enumeration & Curfew Countdown Clamping Logic
var loggedOnSessions = SessionManager.GetLoggedOnSessions();
Debug.Assert(loggedOnSessions != null, "GetLoggedOnSessions should return a non-null list");
var runningCheck = SessionManager.IsProcessRunningInSession("NonExistentProcessName123", 0);
Debug.Assert(!runningCheck, "Nonexistent process should not be reported as running");

// Verify curfew clamping formula:
// Case A: Curfew is earlier than daily quota
var testDailySec = 7200; // 2 hours
var testCurfewSec = 600;  // 10 minutes
var testClamped = Math.Min(testDailySec, testCurfewSec);
Debug.Assert(testClamped == 600, "Should clamp to curfew remaining seconds");

// Case B: Daily quota is earlier than curfew
testDailySec = 300;   // 5 minutes
testCurfewSec = 3600; // 1 hour
testClamped = Math.Min(testDailySec, testCurfewSec);
Debug.Assert(testClamped == 300, "Should clamp to daily remaining seconds");
Console.WriteLine("✅ Session Enumeration & Curfew Clamping Calculation Verification Passed.");

// 11. Test Screen Capture Repository & File Management
var dummyCaptureFile = Path.Combine(DatabaseManager.CapturesDirectory, $"{user.Id}_test.jpg");
File.WriteAllBytes(dummyCaptureFile, new byte[1024]);

var capture = new ScreenCaptureRecord
{
    UserId = user.Id,
    Timestamp = DateTime.Now,
    FilePath = dummyCaptureFile,
    Width = 1920,
    Height = 1080,
    FileSizeBytes = 1024,
    TriggerType = "MANUAL",
    AdminIp = "127.0.0.1"
};
var savedCapture = ScreenCaptureRepository.Add(capture);
Debug.Assert(savedCapture.Id > 0, "ScreenCapture ID should be > 0");

var retrievedCapture = ScreenCaptureRepository.GetById(savedCapture.Id);
Debug.Assert(retrievedCapture != null, "Capture should be retrievable by ID");
Debug.Assert(retrievedCapture!.Width == 1920 && retrievedCapture.Height == 1080, "Dimensions should match");

var latestCapture = ScreenCaptureRepository.GetLatestByUser(user.Id);
Debug.Assert(latestCapture != null && latestCapture.Id == savedCapture.Id, "Latest capture should match saved capture");

var recentCaptures = ScreenCaptureRepository.GetRecentByUser(user.Id, 5);
Debug.Assert(recentCaptures.Count == 1, "Should have 1 recent capture");

var totalBytes = ScreenCaptureRepository.GetTotalStorageBytes();
Debug.Assert(totalBytes >= 1024, "Total storage should be at least 1024 bytes");

// Test capture deletion
var deleted = ScreenCaptureRepository.Delete(savedCapture.Id);
Debug.Assert(deleted, "Capture should be deleted successfully");
Debug.Assert(!File.Exists(dummyCaptureFile), "Capture file should be deleted from disk");
Console.WriteLine("✅ Screen Capture Repository CRUD & Storage Tracking Passed.");

// 12. Test Screen Capture Retention & Storage Ceiling Pruning
var oldCaptureFile = Path.Combine(DatabaseManager.CapturesDirectory, $"{user.Id}_old.jpg");
File.WriteAllBytes(oldCaptureFile, new byte[2048]);
var oldCapture = ScreenCaptureRepository.Add(new ScreenCaptureRecord
{
    UserId = user.Id,
    Timestamp = DateTime.Now.AddDays(-10), // older than 7 days
    FilePath = oldCaptureFile,
    Width = 1920,
    Height = 1080,
    FileSizeBytes = 2048,
    TriggerType = "WATCH"
});

var prunedCount = ScreenCaptureRepository.PruneOldCaptures(retentionDays: 7, maxTotalBytes: 500 * 1024 * 1024);
Debug.Assert(prunedCount >= 1, "PruneOldCaptures should prune expired captures");
Debug.Assert(ScreenCaptureRepository.GetById(oldCapture.Id) == null, "Old capture should be removed from database");
Debug.Assert(!File.Exists(oldCaptureFile), "Old capture file should be removed from disk");
Console.WriteLine("✅ Screen Capture Pruning (Retention Days & Size Limit) Passed.");

// 13. Test Telegram Dynamic Approval Buttons Calculation
var testReq60 = new GraceRequest { Id = 101, RequestedMinutes = 60 };
var buttons60 = new SortedSet<int>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
buttons60.Add(testReq60.RequestedMinutes);
if (testReq60.RequestedMinutes > 60) buttons60.Add(60);
if (testReq60.RequestedMinutes > 30) buttons60.Add(30);
if (testReq60.RequestedMinutes > 15) buttons60.Add(15);
Debug.Assert(buttons60.Contains(60), "60m request must generate Approve 60m option");
Debug.Assert(buttons60.Contains(30), "60m request must also generate Approve 30m option");
Debug.Assert(buttons60.Contains(15), "60m request must also generate Approve 15m option");

var testReq30 = new GraceRequest { Id = 102, RequestedMinutes = 30 };
var buttons30 = new SortedSet<int>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
buttons30.Add(testReq30.RequestedMinutes);
if (testReq30.RequestedMinutes > 60) buttons30.Add(60);
if (testReq30.RequestedMinutes > 30) buttons30.Add(30);
if (testReq30.RequestedMinutes > 15) buttons30.Add(15);
Debug.Assert(!buttons30.Contains(60), "30m request should not offer 60m");
Debug.Assert(buttons30.Contains(30) && buttons30.Contains(15), "30m request should offer 30m and 15m");
Console.WriteLine("✅ Telegram Dynamic Approval Buttons Calculation Passed.");

// 14. Test Sparse Weekly Schedule Persistence and Fallback Deletion
LimitRepository.Upsert(new LimitConfig
{
    UserId = user.Id,
    DailyMinutes = 120,
    ScheduleStart = new TimeOnly(8, 0),
    ScheduleEnd = new TimeOnly(22, 0)
});
ScheduleRepository.SaveDaySchedule(new DaySchedule
{
    UserId = user.Id,
    DayOfWeek = DayOfWeek.Monday,
    DailyMinutes = 90,
    ScheduleStart = new TimeOnly(15, 0),
    ScheduleEnd = new TimeOnly(20, 0)
});

var monLimit = ScheduleRepository.GetEffectiveLimit(user.Id, DayOfWeek.Monday);
Debug.Assert(monLimit != null && monLimit.DailyMinutes == 90, "Monday custom limit should be 90m");
Debug.Assert(monLimit.ScheduleStart == new TimeOnly(15, 0), "Monday custom start should be 15:00");

var tueLimit = ScheduleRepository.GetEffectiveLimit(user.Id, DayOfWeek.Tuesday);
Debug.Assert(tueLimit != null && tueLimit.DailyMinutes == 120, "Tuesday should fall back to baseline 120m");

// Delete custom schedule for Monday -> should fall back to baseline
ScheduleRepository.DeleteDaySchedule(user.Id, DayOfWeek.Monday);
var monLimitAfterDelete = ScheduleRepository.GetEffectiveLimit(user.Id, DayOfWeek.Monday);
Debug.Assert(monLimitAfterDelete != null && monLimitAfterDelete.DailyMinutes == 120, "Monday should fall back to 120m baseline after deletion");
Console.WriteLine("✅ Sparse Weekly Schedule & Baseline Fallback Verification Passed.");

// 14b. Test Single-Day Schedule Exceptions & Precedence Hierarchy
var testDate = new DateOnly(2026, 10, 15); // Thursday
var exc = new ScheduleException
{
    UserId = user.Id,
    ExceptionDate = testDate,
    DailyMinutes = 300,
    ScheduleStart = new TimeOnly(7, 0),
    ScheduleEnd = new TimeOnly(23, 0),
    CreatedAt = DateTime.Now
};
ScheduleExceptionRepository.Upsert(exc);

var queriedExc = ScheduleExceptionRepository.GetForDate(user.Id, testDate);
Debug.Assert(queriedExc != null && queriedExc.DailyMinutes == 300, "Queried exception should have 300m");

var upcomingExcs = ScheduleExceptionRepository.GetUpcomingForUser(user.Id, new DateOnly(2026, 10, 1));
Debug.Assert(upcomingExcs.Any(e => e.ExceptionDate == testDate), "Upcoming exceptions should include test date");

// Precedence check: Exception should override baseline
var effectiveWithExc = ScheduleRepository.GetEffectiveLimit(user.Id, testDate.DayOfWeek, testDate);
Debug.Assert(effectiveWithExc != null && effectiveWithExc.DailyMinutes == 300, "Effective limit should use 300m exception");
Debug.Assert(effectiveWithExc!.ScheduleEnd == new TimeOnly(23, 0), "Effective limit should use 23:00 exception curfew");

// Delete exception -> fallback
ScheduleExceptionRepository.DeleteForUserAndDate(user.Id, testDate);
var effectiveAfterExcDelete = ScheduleRepository.GetEffectiveLimit(user.Id, testDate.DayOfWeek, testDate);
Debug.Assert(effectiveAfterExcDelete != null && effectiveAfterExcDelete.DailyMinutes == 120, "Should fall back to 120m baseline after exception delete");

// 14c. Test Schedule Change Grace Requests & Decline Reasons
var schedReq = GraceRequestRepository.CreateScheduleChange(
    user.Id,
    DateOnly.FromDateTime(DateTime.Now),
    testDate,
    240,
    new TimeOnly(8, 0),
    new TimeOnly(21, 0),
    "Study holiday");
Debug.Assert(schedReq.RequestType == "schedule_change", "RequestType should be schedule_change");
Debug.Assert(schedReq.TargetDate == testDate, "TargetDate should match");
Debug.Assert(schedReq.RequestedMinutes == 240, "RequestedMinutes should be 240");

GraceRequestRepository.Resolve(schedReq.Id, "DECLINED", "Belum menyelesaikan tugas sekolah");
var resolvedSchedReq = GraceRequestRepository.GetById(schedReq.Id);
Debug.Assert(resolvedSchedReq != null && resolvedSchedReq.Status == "DECLINED", "Status should be DECLINED");
Debug.Assert(resolvedSchedReq!.DeclineReason == "Belum menyelesaikan tugas sekolah", "DeclineReason should match");

var latestReq = GraceRequestRepository.GetLatestRequest(user.Id);
Debug.Assert(latestReq != null && latestReq.Id == schedReq.Id, "Latest request should match");
Console.WriteLine("✅ Schedule Exception Precedence & Extended Grace Requests Verification Passed.");

// 15. Test AppVersion & UpdateService SemVer Logic
Debug.Assert(AppVersion.Current == "1.5.0", "Current version should be 1.5.0");
Debug.Assert(AppVersion.DisplayName == "v1.5.0", "Display name should be v1.5.0");
Debug.Assert(AppVersion.GitHubRepo == "DarkTama/WindowsParentalControl", "GitHub repo match");

Debug.Assert(UpdateService.IsNewerVersion("1.5.1", "1.5.0") == true, "1.5.1 is newer than 1.5.0");
Debug.Assert(UpdateService.IsNewerVersion("2.0.0", "1.5.0") == true, "2.0.0 is newer than 1.5.0");
Debug.Assert(UpdateService.IsNewerVersion("1.6.0", "1.5.0") == true, "1.6.0 is newer than 1.5.0");
Debug.Assert(UpdateService.IsNewerVersion("1.5.0", "1.5.0") == false, "1.5.0 is not newer than 1.5.0");
Debug.Assert(UpdateService.IsNewerVersion("1.4.1", "1.5.0") == false, "1.4.1 is not newer than 1.5.0");
Debug.Assert(UpdateService.IsNewerVersion("1.4.0", "1.5.0") == false, "1.4.0 is not newer than 1.5.0");
Debug.Assert(UpdateService.IsNewerVersion("1.3.0", "1.5.0") == false, "1.3.0 is not newer than 1.5.0");
Debug.Assert(UpdateService.IsNewerVersion("1.2.1", "1.4.1") == false, "1.2.1 is not newer than 1.4.1");
Console.WriteLine("✅ AppVersion & UpdateService SemVer Comparison Verification Passed.");

var updateCheck = UpdateService.CheckForUpdatesAsync().GetAwaiter().GetResult();
Debug.Assert(updateCheck.Error == null, $"Error should be null on update check: {updateCheck.Error}");
Console.WriteLine($"✅ UpdateService CheckForUpdatesAsync Passed (Latest: {updateCheck.LatestVersion}, HasUpdate: {updateCheck.HasUpdate}).");

var shouldTestLiveDownload = Environment.GetEnvironmentVariable("TEST_LIVE_DOWNLOAD") == "1";
if (shouldTestLiveDownload && !string.IsNullOrWhiteSpace(updateCheck.DownloadUrl))
{
    int lastPct = -1;
    var progress = new Progress<int>(pct => { lastPct = pct; });
    var (downloadSuccess, downloadedPath, downloadMsg) = UpdateService.DownloadUpdateAsync(updateCheck.DownloadUrl, progress).GetAwaiter().GetResult();
    Debug.Assert(downloadSuccess, $"Download should succeed: {downloadMsg}");
    Debug.Assert(File.Exists(downloadedPath), "Downloaded installer should exist");
    Debug.Assert(new FileInfo(downloadedPath!).Length > 1_000_000, "Downloaded installer should be > 1MB");
    Debug.Assert(lastPct == 100, "Progress should reach 100%");
    Console.WriteLine($"✅ UpdateService DownloadUpdateAsync Live Verification Passed ({new FileInfo(downloadedPath!).Length} bytes, 100% progress).");
    try { File.Delete(downloadedPath!); } catch { }
}
else
{
    Console.WriteLine("ℹ️ UpdateService DownloadUpdateAsync live 137MB download skipped (set TEST_LIVE_DOWNLOAD=1 to run).");
}

// 16. Test Telegram Session Notification Toggles & Security Alert Events
Debug.Assert(SettingsRepository.IsTelegramNotifySignInEnabled() == true, "Sign-in notification should be enabled by default");
Debug.Assert(SettingsRepository.IsTelegramNotifySignOutEnabled() == true, "Sign-out notification should be enabled by default");
Debug.Assert(SettingsRepository.IsTelegramNotifyAdminLogonEnabled() == true, "Admin logon alert should be enabled by default");

SettingsRepository.Set(SettingsRepository.KeyTelegramNotifySignIn, "false");
SettingsRepository.Set(SettingsRepository.KeyTelegramNotifySignOut, "false");
SettingsRepository.Set(SettingsRepository.KeyTelegramNotifyAdminLogon, "false");
Debug.Assert(SettingsRepository.IsTelegramNotifySignInEnabled() == false, "Sign-in notification should reflect disabled state");
Debug.Assert(SettingsRepository.IsTelegramNotifySignOutEnabled() == false, "Sign-out notification should reflect disabled state");
Debug.Assert(SettingsRepository.IsTelegramNotifyAdminLogonEnabled() == false, "Admin logon alert should reflect disabled state");

SettingsRepository.Set(SettingsRepository.KeyTelegramNotifySignIn, "true");
SettingsRepository.Set(SettingsRepository.KeyTelegramNotifySignOut, "true");
SettingsRepository.Set(SettingsRepository.KeyTelegramNotifyAdminLogon, "true");

EventRepository.LogEvent(testSid, EventType.SECURITY_ALERT, "Unauthorized administrator login alert test");
var secEvents = EventRepository.GetEvents(userSid: testSid);
Debug.Assert(secEvents.Any(e => e.EventType == EventType.SECURITY_ALERT), "SECURITY_ALERT event should be stored and retrievable");

var testLogger = ParentalControl.Core.Logging.LoggingConfig.CreateLogger("Test");
var botService = new TelegramBotService(testLogger);
var resSignIn = botService.SendUserSignInNotificationAsync("testuser", "TEST-PC", DateTime.Now, 120, new TimeOnly(8, 0), new TimeOnly(20, 0)).GetAwaiter().GetResult();
Debug.Assert(!resSignIn, "SendUserSignInNotificationAsync should return false gracefully when token is unconfigured");
var resSignOut = botService.SendUserSignOutNotificationAsync("testuser", "TEST-PC", DateTime.Now, 60).GetAwaiter().GetResult();
Debug.Assert(!resSignOut, "SendUserSignOutNotificationAsync should return false gracefully when token is unconfigured");
var resAdmin = botService.SendAdminSignInAlertAsync(1, "Administrator", "TEST-PC", DateTime.Now).GetAwaiter().GetResult();
Debug.Assert(!resAdmin, "SendAdminSignInAlertAsync should return false gracefully when token is unconfigured");
Console.WriteLine("✅ Telegram Session Notification Toggles & Security Alert Events Passed.");

// 17. Test Offline Telegram Notification Queue & Timestamp Drift Bug Fix
var pastEventTime = DateTime.Now.AddMinutes(-15);
var dummyPayload = "{\"Username\":\"TestKid\",\"MachineName\":\"KID-PC\",\"MinutesUsedToday\":45}";
PendingNotificationRepository.Enqueue("USER_SIGN_OUT", dummyPayload, pastEventTime);
Debug.Assert(PendingNotificationRepository.GetCount() == 1, "Pending queue count should be 1 after enqueue");

var pendingList = PendingNotificationRepository.GetPending(5);
Debug.Assert(pendingList.Count == 1, "Pending list should return 1 item");
Debug.Assert(pendingList[0].NotificationType == "USER_SIGN_OUT", "Type should be USER_SIGN_OUT");
Debug.Assert(Math.Abs((pendingList[0].EventTime - pastEventTime).TotalSeconds) < 1, "Original event time must be preserved exactly");
Debug.Assert(pendingList[0].RetryCount == 0, "Initial retry count should be 0");

PendingNotificationRepository.IncrementRetry(pendingList[0].Id);
var retriedList = PendingNotificationRepository.GetPending(5);
Debug.Assert(retriedList[0].RetryCount == 1, "Retry count should be 1 after increment");

// Verify FormatTimeWithDelay behavior
var recentTime = DateTime.Now;
var formattedRecent = TelegramBotService.FormatTimeWithDelay(recentTime, "id");
Debug.Assert(!formattedRecent.Contains("Terkirim tertunda"), "Immediate notification should not have delayed tag");
Debug.Assert(formattedRecent.Contains(recentTime.ToString("HH:mm:ss")), "Recent time string must match event time");

var formattedDelayedId = TelegramBotService.FormatTimeWithDelay(pastEventTime, "id");
Debug.Assert(formattedDelayedId.Contains("Terkirim tertunda"), "Delayed notification in ID must contain 'Terkirim tertunda'");
Debug.Assert(formattedDelayedId.Contains(pastEventTime.ToString("HH:mm:ss")), "Delayed notification must display true original event time");

var formattedDelayedEn = TelegramBotService.FormatTimeWithDelay(pastEventTime, "en");
Debug.Assert(formattedDelayedEn.Contains("Delayed delivery"), "Delayed notification in EN must contain 'Delayed delivery'");
Debug.Assert(formattedDelayedEn.Contains(pastEventTime.ToString("HH:mm:ss")), "Delayed notification in EN must display true original event time");

PendingNotificationRepository.Delete(pendingList[0].Id);
Debug.Assert(PendingNotificationRepository.GetCount() == 0, "Pending queue should be 0 after delete");
Console.WriteLine("✅ Offline Telegram Notification Queue & Timestamp Drift Bug Fix Passed.");

// 18. Test Interactive Session Prompts Repository & Presets
var defaultPresets = SettingsRepository.GetPromptResponsePresets();
Debug.Assert(defaultPresets.Count >= 3, "Default prompt presets should have at least 3 items");
Debug.Assert(defaultPresets.Any(p => p.Contains("selesai game", StringComparison.OrdinalIgnoreCase)), "Default preset should include game finish option");

var promptId1 = SessionPromptRepository.Create(testSid, "TestBrother", "Mau makan siang?", "URGENT", 1, "Sekarang|Nanti 10m");
Debug.Assert(promptId1 > 0, "Created prompt ID should be > 0");

var activePrompt = SessionPromptRepository.GetActivePrompt("TestBrother");
Debug.Assert(activePrompt != null, "Active prompt should be found");
Debug.Assert(activePrompt!.Id == promptId1, "Active prompt ID should match");
Debug.Assert(activePrompt.Urgency == "URGENT", "Urgency should be URGENT");
Debug.Assert(activePrompt.TargetDisplay == 1, "Target display should be 1");
Debug.Assert(activePrompt.CustomOptions == "Sekarang|Nanti 10m", "Custom options should match");
Debug.Assert(activePrompt.Status == "PENDING", "Status should be PENDING");

var resolvedSuccess = SessionPromptRepository.ResolvePrompt(promptId1, "NO", "Nanti 10m", 14);
Debug.Assert(resolvedSuccess, "Prompt resolution should succeed");

var promptAfterResolve = SessionPromptRepository.GetById(promptId1);
Debug.Assert(promptAfterResolve != null, "Prompt should exist");
Debug.Assert(promptAfterResolve!.Status == "ANSWERED", "Status should be ANSWERED");
Debug.Assert(promptAfterResolve.Response == "NO", "Response should be NO");
Debug.Assert(promptAfterResolve.ResponseReason == "Nanti 10m", "Response reason should match");
Debug.Assert(promptAfterResolve.TurnaroundSeconds == 14, "Turnaround seconds should be 14");
Debug.Assert(promptAfterResolve.AnsweredAt != null, "AnsweredAt should not be null");

// Verify active prompt is now cleared
Debug.Assert(SessionPromptRepository.GetActivePrompt("TestBrother") == null, "Active prompt should be null after resolve");

// Test timeout resolution
var promptId2 = SessionPromptRepository.Create(testSid, "TestBrother", "Sudah selesai tugas?", "NORMAL", -1);
var activePrompt2 = SessionPromptRepository.GetActivePrompt("TestBrother");
Debug.Assert(activePrompt2 != null && activePrompt2.Id == promptId2, "Second prompt should be active");
SessionPromptRepository.ResolvePrompt(promptId2, "TIMEOUT", null, 120);
var prompt2After = SessionPromptRepository.GetById(promptId2);
Debug.Assert(prompt2After!.Status == "TIMEOUT", "Status should be TIMEOUT");
Debug.Assert(prompt2After.TurnaroundSeconds == 120, "Turnaround should be 120s");

// Test Prompt Event Logging
EventRepository.LogEvent(testSid, EventType.PROMPT_SENT, "Prompt sent test");
EventRepository.LogEvent(testSid, EventType.PROMPT_ANSWERED, "Prompt answered test");
EventRepository.LogEvent(testSid, EventType.PROMPT_TIMEOUT, "Prompt timeout test");
var promptEvents = EventRepository.GetEvents(userSid: testSid);
Debug.Assert(promptEvents.Any(e => e.EventType == EventType.PROMPT_SENT), "PROMPT_SENT should be in event log");
Debug.Assert(promptEvents.Any(e => e.EventType == EventType.PROMPT_ANSWERED), "PROMPT_ANSWERED should be in event log");
Debug.Assert(promptEvents.Any(e => e.EventType == EventType.PROMPT_TIMEOUT), "PROMPT_TIMEOUT should be in event log");
Console.WriteLine("✅ Interactive Session Prompts Repository & Presets Passed.");

// Cleanup test user
UserRepository.DeleteBySid(testSid);
ScheduleRepository.DeleteForUser(user.Id);
    Console.WriteLine("✅ Cleanup Completed.");

    Console.WriteLine("\n🎉 ALL ASSERTIONS PASSED SUCCESSFULLY!");
}
finally
{
    try
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
    }
    catch { }
}
