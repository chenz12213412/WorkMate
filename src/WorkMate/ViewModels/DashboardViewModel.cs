using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using WorkMate.Models;
using WorkMate.Services;

namespace WorkMate.ViewModels;

public sealed class DashboardViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ScheduleEngine _scheduleEngine;
    private readonly ReminderEngine _reminderEngine;
    private readonly WorkModeService _workModeService;
    private readonly IActivitySeriesProvider _activitySeriesProvider;
    private readonly IIconService _iconService;
    private string _statusMessage = "今天也在安静陪你工作。";

    public DashboardViewModel(
        ScheduleEngine scheduleEngine,
        ReminderEngine reminderEngine,
        WorkModeService workModeService,
        IActivitySeriesProvider activitySeriesProvider,
        IIconService iconService)
    {
        _scheduleEngine = scheduleEngine;
        _reminderEngine = reminderEngine;
        _workModeService = workModeService;
        _activitySeriesProvider = activitySeriesProvider;
        _iconService = iconService;
        _scheduleEngine.StateChanged += ScheduleEngine_OnChanged;
        _scheduleEngine.SettingsReloaded += ScheduleEngine_OnSettingsReloaded;
        _workModeService.Changed += WorkModeService_OnChanged;
        Refresh(includeActivitySeries: true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? RefreshRequested;

    public string CurrentTime { get; private set; } = string.Empty;

    public string CurrentDate { get; private set; } = string.Empty;

    public string CurrentProfile { get; private set; } = string.Empty;

    public string StateName { get; private set; } = string.Empty;

    public string StateDescription { get; private set; } = string.Empty;

    public ImageSource? StateIcon { get; private set; }

    public string TodayWorkDuration { get; private set; } = "0m";

    public string ContinuousWorkDuration { get; private set; } = "0m";

    public string WorkIntensity { get; private set; } = "—";

    public string KeyboardCount { get; private set; } = "—";

    public string MouseClickCount { get; private set; } = "—";

    public string AppSwitchCount { get; private set; } = "—";

    public IReadOnlyList<ActivitySeriesPoint> ActivitySeries { get; private set; } = [];

    public string ActivitySeriesStatus { get; private set; } = "等待活动采集数据";

    public string OvertimeActionText => _workModeService.IsOvertime ? "结束加班" : "开始加班";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public void Refresh(bool includeActivitySeries)
    {
        var now = DateTime.Now;
        var snapshot = _scheduleEngine.GetSnapshot(now);
        var effectiveState = _workModeService.ResolveScheduleState(snapshot.State);
        CurrentTime = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        CurrentDate = $"{now:yyyy-MM-dd} {ToWeekday(now.DayOfWeek)}";
        CurrentProfile = snapshot.ActiveProfile == ScheduleProfileType.Summer ? "夏令时" : "冬令时";
        StateName = ToStateName(effectiveState);
        StateDescription = ToStateDescription(effectiveState);
        StateIcon = _iconService.GetStatusIcon(
            new IconVisualState(effectiveState, _workModeService.ActivityMode),
            96);
        var activity = _activitySeriesProvider.GetSnapshot(now);
        TodayWorkDuration = FormatDuration(activity.TodayWorkDuration);
        ContinuousWorkDuration = FormatDuration(activity.ContinuousWorkDuration);
        WorkIntensity = activity.WorkIntensity?.ToString(CultureInfo.InvariantCulture) ?? "—";
        KeyboardCount = FormatCount(activity.KeyboardCount);
        MouseClickCount = FormatCount(activity.MouseClickCount);
        AppSwitchCount = FormatCount(activity.AppSwitchCount);

        if (includeActivitySeries)
        {
            ActivitySeries = activity.Series;
            ActivitySeriesStatus = activity.Series.Count == 0 ? "等待活动采集数据" : string.Empty;
        }

        RaiseAll();
    }

    public async Task MarkCleaningCompletedAsync(CancellationToken cancellationToken)
    {
        await _reminderEngine.MarkCleaningCompletedAsync(cancellationToken);
        StatusMessage = "已记录：今天的工位整理完成。";
    }

    public void RecordDrink()
    {
        _reminderEngine.MarkDrinkCompleted();
        StatusMessage = "已记录：刚刚喝过水。";
    }

    public void RecordStand()
    {
        _reminderEngine.MarkStandCompleted();
        StatusMessage = "很好，起来活动一下吧。";
    }

    public void ExplainLunchControl()
    {
        StatusMessage = "午休状态由当前作息时间自动判断。";
    }

    public void ToggleOvertime()
    {
        _workModeService.ToggleOvertime();
        StatusMessage = _workModeService.IsOvertime ? "已开始加班。" : "已结束加班。";
    }

    public void ShowStatus(string message)
    {
        StatusMessage = message;
    }

    public void Dispose()
    {
        _scheduleEngine.StateChanged -= ScheduleEngine_OnChanged;
        _scheduleEngine.SettingsReloaded -= ScheduleEngine_OnSettingsReloaded;
        _workModeService.Changed -= WorkModeService_OnChanged;
    }

    private static string ToStateName(WorkScheduleState state)
    {
        return state switch
        {
            WorkScheduleState.BeforeWork => "上班前",
            WorkScheduleState.Working => "工作中",
            WorkScheduleState.Lunch => "午休",
            WorkScheduleState.Overtime => "加班中",
            _ => "已下班"
        };
    }

    private static string ToStateDescription(WorkScheduleState state)
    {
        return state switch
        {
            WorkScheduleState.BeforeWork => "慢慢准备，新的一天就要开始。",
            WorkScheduleState.Working => "专注当下，慢慢变好。",
            WorkScheduleState.Lunch => "先休息一下，下午再继续。",
            WorkScheduleState.Overtime => "还在努力，也别忘了照顾自己。",
            _ => "今天辛苦了，好好休息。"
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var safeDuration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        var totalHours = (int)safeDuration.TotalHours;
        return totalHours > 0
            ? $"{totalHours}h {safeDuration.Minutes}m"
            : $"{safeDuration.Minutes}m";
    }

    private static string FormatCount(long? count)
    {
        return count?.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
    }

    private static string ToWeekday(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            DayOfWeek.Saturday => "周六",
            _ => "周日"
        };
    }

    private void ScheduleEngine_OnChanged(object? sender, ScheduleChangedEventArgs e)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleEngine_OnSettingsReloaded(object? sender, EventArgs e)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void WorkModeService_OnChanged(object? sender, EventArgs e)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(CurrentTime));
        OnPropertyChanged(nameof(CurrentDate));
        OnPropertyChanged(nameof(CurrentProfile));
        OnPropertyChanged(nameof(StateName));
        OnPropertyChanged(nameof(StateDescription));
        OnPropertyChanged(nameof(StateIcon));
        OnPropertyChanged(nameof(TodayWorkDuration));
        OnPropertyChanged(nameof(ContinuousWorkDuration));
        OnPropertyChanged(nameof(WorkIntensity));
        OnPropertyChanged(nameof(KeyboardCount));
        OnPropertyChanged(nameof(MouseClickCount));
        OnPropertyChanged(nameof(AppSwitchCount));
        OnPropertyChanged(nameof(ActivitySeries));
        OnPropertyChanged(nameof(ActivitySeriesStatus));
        OnPropertyChanged(nameof(OvertimeActionText));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
