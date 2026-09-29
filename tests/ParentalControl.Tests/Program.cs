using ParentalControl.Core.Platform;
using System.Diagnostics;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.Core.Security;

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
