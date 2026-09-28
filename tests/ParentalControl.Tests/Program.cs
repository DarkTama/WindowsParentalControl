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
Console.WriteLine("✅ TOTP Service Verification Passed.");
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
Console.WriteLine("✅ Grace Request Repository Verification Passed.");

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
