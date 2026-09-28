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
    public string DayName => DayOfWeek.ToString();

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

        // Sunday (0) through Saturday (6)
        for (int i = 0; i <= 6; i++)
        {
            var day = (DayOfWeek)i;
            if (existing.TryGetValue(day, out var s))
            {
                list.Add(new DayScheduleRow
                {
                    DayOfWeek = day,
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
        foreach (var row in WeeklySchedules)
        {
            if (!TimeOnly.TryParse(row.ScheduleStart, out var start) || !TimeOnly.TryParse(row.ScheduleEnd, out var end))
            {
                MessageBox.Show($"Invalid time format for {row.DayName}. Use HH:mm.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (start >= end)
            {
                MessageBox.Show($"Schedule start must be before end for {row.DayName}.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (row.DailyMinutes < 1 || row.DailyMinutes > 1440)
            {
                MessageBox.Show($"Minutes must be between 1 and 1440 for {row.DayName}.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        }

        MessageBox.Show("7-Day schedule saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
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
