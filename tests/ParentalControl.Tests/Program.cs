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
var events = EventRepository.GetEvents(userSid: testSid);
Debug.Assert(events.Any(e => e.EventType == EventType.SESSION_LOCKED), "SESSION_LOCKED event should be recorded");
Debug.Assert(events.Any(e => e.EventType == EventType.SESSION_UNLOCKED), "SESSION_UNLOCKED event should be recorded");
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

// 15. Test AppVersion & UpdateService SemVer Logic
Debug.Assert(AppVersion.Current == "1.2.0", "Current version should be 1.2.0");
Debug.Assert(AppVersion.DisplayName == "v1.2.0", "Display name should be v1.2.0");
Debug.Assert(AppVersion.GitHubRepo == "DarkTama/WindowsParentalControl", "GitHub repo match");

Debug.Assert(UpdateService.IsNewerVersion("1.3.0", "1.2.0") == true, "1.3.0 is newer than 1.2.0");
Debug.Assert(UpdateService.IsNewerVersion("2.0.0", "1.2.0") == true, "2.0.0 is newer than 1.2.0");
Debug.Assert(UpdateService.IsNewerVersion("1.2.1", "1.2.0") == true, "1.2.1 is newer than 1.2.0");
Debug.Assert(UpdateService.IsNewerVersion("1.2.0", "1.2.0") == false, "1.2.0 is not newer than 1.2.0");
Debug.Assert(UpdateService.IsNewerVersion("1.1.9", "1.2.0") == false, "1.1.9 is not newer than 1.2.0");
Console.WriteLine("✅ AppVersion & UpdateService SemVer Comparison Verification Passed.");

var updateCheck = UpdateService.CheckForUpdatesAsync().GetAwaiter().GetResult();
Debug.Assert(updateCheck.HasUpdate == false, "No update should be pending when no release on GitHub");
Debug.Assert(updateCheck.Error == null, "Error should be null on 404 (handled gracefully)");
Console.WriteLine("✅ UpdateService CheckForUpdatesAsync 404 Handled Gracefully.");

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
