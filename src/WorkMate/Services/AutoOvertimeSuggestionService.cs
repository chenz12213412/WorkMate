using WorkMate.Infrastructure;
using WorkMate.Models;

namespace WorkMate.Services;

public sealed class AutoOvertimeSuggestionService : IDisposable
{
    public const string ReminderType = "AutoOvertimePrompt";
    public static readonly TimeSpan RequiredActiveDuration = TimeSpan.FromMinutes(15);

    private readonly IActivitySnapshotService _activity;
    private readonly ScheduleEngine _scheduleEngine;
    private readonly WorkModeService _workModeService;
    private readonly DatabaseStore _database;
    private readonly ActivityTrackingSettingsStore _settingsStore;
    private DateOnly _trackedDate;
    private DateTimeOffset? _activeSince;
    private bool _claiming;
    private bool _disposed;

    public AutoOvertimeSuggestionService(
        IActivitySnapshotService activity,
        ScheduleEngine scheduleEngine,
        WorkModeService workModeService,
        DatabaseStore database,
        ActivityTrackingSettingsStore settingsStore)
    {
        _activity = activity;
        _scheduleEngine = scheduleEngine;
        _workModeService = workModeService;
        _database = database;
        _settingsStore = settingsStore;
        _activity.SnapshotUpdated += Activity_OnSnapshotUpdated;
        _workModeService.Changed += WorkModeService_OnChanged;
    }

    public event EventHandler? SuggestionDue;

    public ActivityTrackingSettings Settings { get; private set; } = ActivityTrackingSettings.Default;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Settings = await _settingsStore.LoadAsync(cancellationToken);
    }

    public async Task UpdateEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        var settings = new ActivityTrackingSettings(enabled);
        await _settingsStore.SaveAsync(settings, cancellationToken);
        Settings = settings;
        if (!enabled)
        {
            _activeSince = null;
        }
    }

    public Task IgnoreTodayAsync(CancellationToken cancellationToken)
    {
        return _database.SetReminderStatusAsync(
            ReminderType,
            DateOnly.FromDateTime(DateTime.Now),
            ReminderHistoryStatus.Skipped,
            DateTimeOffset.Now,
            cancellationToken);
    }

    public Task RecordShownAsync(DateTimeOffset shownAt, CancellationToken cancellationToken)
    {
        return _database.RecordReminderShownAsync(
            ReminderType,
            DateOnly.FromDateTime(shownAt.LocalDateTime),
            shownAt,
            cancellationToken);
    }

    public async Task HandleActionAsync(ReminderAction action, CancellationToken cancellationToken)
    {
        if (action == ReminderAction.StartOvertime)
        {
            if (!_workModeService.IsOvertime)
            {
                _workModeService.ToggleOvertime();
            }

            await _database.RecordReminderActionAsync(
                ReminderType,
                DateOnly.FromDateTime(DateTime.Now),
                action.ToString(),
                ReminderHistoryStatus.Completed,
                DateTimeOffset.Now,
                cancellationToken);
            return;
        }

        var status = MapActionToStatus(action);
        await _database.RecordReminderActionAsync(
            ReminderType,
            DateOnly.FromDateTime(DateTime.Now),
            action.ToString(),
            status,
            DateTimeOffset.Now,
            cancellationToken);
    }

    public static ReminderHistoryStatus MapActionToStatus(ReminderAction action) => action switch
    {
        ReminderAction.StartOvertime => ReminderHistoryStatus.Completed,
        ReminderAction.Skipped => ReminderHistoryStatus.Skipped,
        ReminderAction.Dismissed => ReminderHistoryStatus.Dismissed,
        ReminderAction.Expired => ReminderHistoryStatus.Expired,
        ReminderAction.Preempted => ReminderHistoryStatus.Preempted,
        _ => ReminderHistoryStatus.Dismissed
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activity.SnapshotUpdated -= Activity_OnSnapshotUpdated;
        _workModeService.Changed -= WorkModeService_OnChanged;
    }

    private void Activity_OnSnapshotUpdated(object? sender, ActivityDashboardSnapshot snapshot)
    {
        if (_disposed || !Settings.AutoOvertimePromptEnabled || _workModeService.IsOvertime ||
            _workModeService.ActivityMode != ActivityMode.Computer)
        {
            _activeSince = null;
            return;
        }

        var now = DateTimeOffset.Now;
        var date = DateOnly.FromDateTime(now.LocalDateTime);
        var scheduledState = _scheduleEngine.GetSnapshot(now.LocalDateTime).State;
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
            scheduledState != WorkScheduleState.OffWork ||
            snapshot.UserState != UserActivityState.Active)
        {
            _activeSince = null;
            return;
        }

        if (_trackedDate != date)
        {
            _trackedDate = date;
            _activeSince = now;
        }

        _activeSince ??= now;
        if (!_claiming && now - _activeSince >= RequiredActiveDuration)
        {
            _claiming = true;
            _ = ClaimAndNotifyAsync(date, now);
        }
    }

    private async Task ClaimAndNotifyAsync(DateOnly date, DateTimeOffset now)
    {
        try
        {
            if (await _database.TryClaimReminderAsync(
                    ReminderType,
                    date,
                    now,
                    "检测到下班后仍在持续工作。",
                    CancellationToken.None))
            {
                SuggestionDue?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
        }
        finally
        {
            _claiming = false;
            _activeSince = null;
        }
    }

    private void WorkModeService_OnChanged(object? sender, EventArgs e)
    {
        _activeSince = null;
    }
}
