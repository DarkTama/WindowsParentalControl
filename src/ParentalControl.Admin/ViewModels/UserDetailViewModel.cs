using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;

namespace ParentalControl.Admin.ViewModels;

public partial class DayScheduleRow : ObservableObject
{
    public DayOfWeek DayOfWeek { get; set; }
    public string DayName => DayOfWeek switch
    {
        DayOfWeek.Monday => "Senin (Monday)",
        DayOfWeek.Tuesday => "Selasa (Tuesday)",
        DayOfWeek.Wednesday => "Rabu (Wednesday)",
        DayOfWeek.Thursday => "Kamis (Thursday)",
        DayOfWeek.Friday => "Jumat (Friday)",
        DayOfWeek.Saturday => "Sabtu (Saturday)",
        DayOfWeek.Sunday => "Minggu (Sunday)",
        _ => DayOfWeek.ToString()
    };

    [ObservableProperty]
    private bool _isCustom;

    [ObservableProperty]
    private int _dailyMinutes = 120;

    [ObservableProperty]
    private string _scheduleStart = "08:00";

    [ObservableProperty]
    private string _scheduleEnd = "22:00";
}

public partial class UserDetailViewModel : ObservableObject
{
    private readonly Dictionary<string, string> _sidToUsername;
    private readonly Action _navigateBack;

    public UserRow User { get; }

    [ObservableProperty]
    private int _dailyMinutes = 120;

    [ObservableProperty]
    private string _scheduleStart = "08:00";

    [ObservableProperty]
    private string _scheduleEnd = "22:00";

    [ObservableProperty]
    private int _editableUsageMinutes;

    [ObservableProperty]
    private int _todayBonusMinutes;

    [ObservableProperty]
    private ObservableCollection<DayScheduleRow> _weeklySchedules = [];

    [ObservableProperty]
    private ObservableCollection<AppUsageRecord> _appUsageRecords = [];

    [ObservableProperty]
    private ObservableCollection<EventRecord> _eventRecords = [];

    public UserDetailViewModel(UserRow user, Dictionary<string, string> sidToUsername, Action navigateBack)
    {
        User = user;
        _sidToUsername = sidToUsername;
        _navigateBack = navigateBack;
    }

    public void LoadAll()
    {
        RefreshUser();
        LoadLimitFields();
        LoadWeeklySchedule();
        LoadAppUsage();
        RefreshEvents();
        EditableUsageMinutes = User.TodayMinutesUsed;
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigateBack();
    }

    [RelayCommand]
    private void RefreshUser()
    {
        User.RefreshUsage();
        User.RefreshLimits();
        LoadLimitFields();
        EditableUsageMinutes = User.TodayMinutesUsed;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var usage = UsageRepository.GetUsage(User.Id, today);
        TodayBonusMinutes = usage?.BonusMinutes ?? 0;
    }

    [RelayCommand]
    private void AddBonus15() => GrantBonus(15);

    [RelayCommand]
    private void AddBonus30() => GrantBonus(30);

    [RelayCommand]
    private void AddBonus60() => GrantBonus(60);

    private void GrantBonus(int minutes)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        UsageRepository.AddBonusMinutes(User.Id, today, minutes);
        EventRepository.LogEvent(User.Sid, EventType.LOGIN, $"Admin granted +{minutes}m bonus time");
        RefreshUser();
        MessageBox.Show($"Granted +{minutes} minutes bonus screen time for today.", "Bonus Added", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void LoadWeeklySchedule()
    {
        var existing = ScheduleRepository.GetWeeklySchedule(User.Id).ToDictionary(s => s.DayOfWeek, s => s);
        var list = new ObservableCollection<DayScheduleRow>();

        // Order: Monday (1) to Friday (5), Saturday (6), Sunday (0)
        var dayOrder = new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
        };

        foreach (var day in dayOrder)
        {
            if (existing.TryGetValue(day, out var s))
            {
                list.Add(new DayScheduleRow
                {
                    DayOfWeek = day,
                    IsCustom = true,
                    DailyMinutes = s.DailyMinutes,
                    ScheduleStart = s.ScheduleStart.ToString("HH:mm"),
                    ScheduleEnd = s.ScheduleEnd.ToString("HH:mm")
                });
            }
            else
            {
                list.Add(new DayScheduleRow
                {
                    DayOfWeek = day,
                    IsCustom = false,
                    DailyMinutes = DailyMinutes,
                    ScheduleStart = ScheduleStart,
                    ScheduleEnd = ScheduleEnd
                });
            }
        }
        WeeklySchedules = list;
    }

    [RelayCommand]
    private void SaveWeeklySchedule()
    {
        int savedCustomCount = 0;
        foreach (var row in WeeklySchedules)
        {
            if (row.IsCustom)
            {
                if (!TimeOnly.TryParse(row.ScheduleStart, out var start) || !TimeOnly.TryParse(row.ScheduleEnd, out var end))
                {
                    MessageBox.Show($"Format jam salah untuk {row.DayName}. Gunakan format HH:mm.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (start >= end)
                {
                    MessageBox.Show($"Jam mulai harus lebih awal dari jam selesai untuk {row.DayName}.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (row.DailyMinutes < 1 || row.DailyMinutes > 1440)
                {
                    MessageBox.Show($"Menit penggunaan harus antara 1 dan 1440 untuk {row.DayName}.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ScheduleRepository.SaveDaySchedule(new DaySchedule
                {
                    UserId = User.Id,
                    DayOfWeek = row.DayOfWeek,
                    DailyMinutes = row.DailyMinutes,
                    ScheduleStart = start,
                    ScheduleEnd = end
                });
                savedCustomCount++;
            }
            else
            {
                // Untoggled: delete day override to fall back to baseline default
                ScheduleRepository.DeleteDaySchedule(User.Id, row.DayOfWeek);
            }
        }

        MessageBox.Show($"Jadwal 7 hari berhasil disimpan ({savedCustomCount} hari khusus, {7 - savedCustomCount} hari mengikuti aturan standar).", "Jadwal Disimpan", MessageBoxButton.OK, MessageBoxImage.Information);
        User.RefreshLimits();
    }

    [RelayCommand]
    private void ApplySchoolDaysPreset()
    {
        foreach (var row in WeeklySchedules)
        {
            if (row.DayOfWeek >= DayOfWeek.Monday && row.DayOfWeek <= DayOfWeek.Friday)
            {
                row.IsCustom = true;
                row.DailyMinutes = 60;
                row.ScheduleStart = "08:00";
                row.ScheduleEnd = "20:00";
            }
        }
    }

    [RelayCommand]
    private void ApplyWeekendPreset()
    {
        foreach (var row in WeeklySchedules)
        {
            if (row.DayOfWeek == DayOfWeek.Saturday || row.DayOfWeek == DayOfWeek.Sunday)
            {
                row.IsCustom = true;
                row.DailyMinutes = 180;
                row.ScheduleStart = "09:00";
                row.ScheduleEnd = "23:00";
            }
        }
    }

    [RelayCommand]
    private void ResetAllToDefault()
    {
        foreach (var row in WeeklySchedules)
        {
            row.IsCustom = false;
            row.DailyMinutes = DailyMinutes;
            row.ScheduleStart = ScheduleStart;
            row.ScheduleEnd = ScheduleEnd;
        }
    }

    public void LoadAppUsage()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var apps = AppUsageRepository.GetForUserAndDate(User.Id, today);
        AppUsageRecords = new ObservableCollection<AppUsageRecord>(apps);
    }

    [RelayCommand]
    private void RefreshEvents()
    {
        var events = EventRepository.GetEvents(userSid: User.Sid);
        foreach (var evt in events)
        {
            evt.Username = _sidToUsername.TryGetValue(evt.UserSid, out var name) ? name : evt.UserSid;
        }
        EventRecords = new ObservableCollection<EventRecord>(events);
        LoadAppUsage();
    }

    [RelayCommand]
    private void SetUsage()
    {
        if (EditableUsageMinutes < 0)
        {
            MessageBox.Show("Usage minutes cannot be negative.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        UsageRepository.SetUsage(User.Id, today, EditableUsageMinutes);
        User.RefreshUsage();
        EditableUsageMinutes = User.TodayMinutesUsed;
    }

    [RelayCommand]
    private void SetLimits()
    {
        if (!TimeOnly.TryParse(ScheduleStart, out var start) ||
            !TimeOnly.TryParse(ScheduleEnd, out var end))
        {
            MessageBox.Show("Invalid time format. Use HH:mm (e.g., 08:00).", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (start >= end)
        {
            MessageBox.Show("Schedule start must be before schedule end.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (DailyMinutes < 1 || DailyMinutes > 1440)
        {
            MessageBox.Show("Daily minutes must be between 1 and 1440 (24 hours).", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LimitRepository.Upsert(new LimitConfig
        {
            UserId = User.Id,
            DailyMinutes = DailyMinutes,
            ScheduleStart = start,
            ScheduleEnd = end
        });
        UserRepository.SetRestricted(User.Id, true);
        User.IsRestricted = true;
        User.RefreshLimits();

        MessageBox.Show("Default limits saved successfully.", "Success",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void RemoveLimits()
    {
        var result = MessageBox.Show(
            $"Remove all limits and schedules for {User.Username}? This will allow unrestricted access.",
            "Confirm Remove Limits", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        LimitRepository.Delete(User.Id);
        ScheduleRepository.DeleteForUser(User.Id);
        UserRepository.SetRestricted(User.Id, false);
        User.IsRestricted = false;
        User.RefreshLimits();

        DailyMinutes = 120;
        ScheduleStart = "08:00";
        ScheduleEnd = "22:00";
        LoadWeeklySchedule();
    }

    private void LoadLimitFields()
    {
        if (User.DailyMinutes.HasValue)
        {
            DailyMinutes = User.DailyMinutes.Value;
            ScheduleStart = User.ScheduleStart ?? "08:00";
            ScheduleEnd = User.ScheduleEnd ?? "22:00";
        }
        else
        {
            DailyMinutes = 120;
            ScheduleStart = "08:00";
            ScheduleEnd = "22:00";
        }
    }
}
