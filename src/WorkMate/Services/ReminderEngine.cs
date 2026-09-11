using WorkMate.Infrastructure;
using WorkMate.Models;

namespace WorkMate.Services;

public enum ReminderKind
{
    DrinkWater,
    Stand,
    RegularWork
}

public enum ScheduleReminderNode
{
    LunchSoon,
    LunchStart,
    AfternoonStart,
    OffWork
}

public sealed class ReminderEngine : IDisposable
{
    public const string CleaningReminderType = "Cleaning";
    public const string LunchSoonReminderType = "Schedule.LunchSoon";
    public const string LunchStartReminderType = "Schedule.LunchStart";
    public const string AfternoonStartReminderType = "Schedule.AfternoonStart";
    public const string OffWorkReminderType = "Schedule.OffWork";
    public const int MaximumCleaningSnoozes = 2;
    private const int MaximumOrdinarySnoozes = 1;
    private static readonly TimeSpan MissedReminderGrace = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly ScheduleEngine _scheduleEngine;
    private readonly CleaningReminderSettingsStore _cleaningSettingsStore;
    private readonly ReminderPresentationSettingsStore _presentationSettingsStore;
    private readonly DatabaseStore _database;
    private readonly IReminderPresentationService _presentationService;
    private readonly IActivitySnapshotService? _activitySnapshotService;
    private readonly WorkModeService _workModeService;
    private readonly System.Threading.Timer _timer;
    private readonly CancellationTokenSource _disposeTokenSource = new();
    private CleaningReminderSettings _cleaningSettings = CleaningReminderSettings.Default;
    private ReminderPresentationSettings _presentationSettings = ReminderPresentationSettings.Default;
    private bool _allRemindersPaused;
    private DateTime _lastActivitySnapshotAt;
    private double _activeDrinkSeconds;
    private readonly StandReminderCycle _standReminderCycle = new();
    private int _timerBusy;
    private int _ordinaryReminderBusy;
    private int _disposeState;

    public ReminderEngine(
        ScheduleEngine scheduleEngine,
        CleaningReminderSettingsStore cleaningSettingsStore,
        ReminderPresentationSettingsStore presentationSettingsStore,
        DatabaseStore database,
        IReminderPresentationService presentationService,
        WorkModeService workModeService,
        IActivitySnapshotService? activitySnapshotService = null)
    {
        _scheduleEngine = scheduleEngine;
        _cleaningSettingsStore = cleaningSettingsStore;
        _presentationSettingsStore = presentationSettingsStore;
        _database = database;
        _presentationService = presentationService;
        _workModeService = workModeService;
        _activitySnapshotService = activitySnapshotService;
        _timer = new System.Threading.Timer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _scheduleEngine.StateChanged += ScheduleEngine_OnStateChanged;
        _scheduleEngine.SettingsReloaded += ScheduleEngine_OnSettingsReloaded;
        _workModeService.Changed += WorkModeService_OnChanged;
        if (_activitySnapshotService is not null)
        {
            _activitySnapshotService.SnapshotUpdated += ActivitySnapshotService_OnSnapshotUpdated;
        }
    }

    public event EventHandler? AvailabilityChanged;

    public event EventHandler? CleaningStatusChanged;

    public CleaningReminderSettings CleaningSettings => _cleaningSettings;

    public ReminderPresentationSettings PresentationSettings => _presentationSettings;

    public bool IsAllRemindersPaused => _allRemindersPaused;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _cleaningSettings = await _cleaningSettingsStore.LoadAsync(cancellationToken);
        _presentationSettings = await _presentationSettingsStore.LoadAsync(cancellationToken);
        _presentationService.ApplySettings(_presentationSettings);
        _lastActivitySnapshotAt = DateTime.Now;
        _timer.Change(TimeSpan.FromSeconds(1), PollInterval);
    }

    public bool CanDeliver(ReminderKind kind, DateTime timestamp)
    {
        if (_allRemindersPaused)
        {
            return false;
        }

        var scheduledState = _scheduleEngine.GetSnapshot(timestamp).State;
        var workMode = _workModeService.Snapshot;
        var effectiveState = workMode.IsOvertime
            ? WorkScheduleState.Overtime
            : scheduledState;
        if (effectiveState is not (WorkScheduleState.Working or WorkScheduleState.Overtime))
        {
            return false;
        }

        if (workMode.ActivityMode == ActivityMode.Lab && kind == ReminderKind.Stand)
        {
            return false;
        }

        if (workMode.ActivityMode is ActivityMode.Lab or ActivityMode.Meeting)
        {
            return true;
        }

        var activity = _activitySnapshotService?.Current;
        return activity is null || kind switch
        {
            ReminderKind.Stand => activity.UserState == UserActivityState.Active,
            _ => activity.UserState != UserActivityState.Afk
        };
    }

    public void MarkDrinkCompleted()
    {
        _activeDrinkSeconds = 0;
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MarkStandCompleted()
    {
        _standReminderCycle.Complete();
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpdateCleaningSettingsAsync(
        CleaningReminderSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.TryValidate(out var error))
        {
            throw new ArgumentException(error, nameof(settings));
        }

        await _cleaningSettingsStore.SaveAsync(settings, cancellationToken);
        _cleaningSettings = settings;
        _ = ProcessSoonAsync();
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpdatePresentationSettingsAsync(
        ReminderPresentationSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.TryValidate(out var error))
        {
            throw new ArgumentException(error, nameof(settings));
        }

        await _presentationSettingsStore.SaveAsync(settings, cancellationToken);
        _presentationSettings = settings;
        _presentationService.ApplySettings(settings);
        _ = ProcessSoonAsync();
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAllRemindersPaused(bool paused)
    {
        if (_allRemindersPaused == paused)
        {
            return;
        }

        _allRemindersPaused = paused;
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<ReminderHistoryEntry?> GetTodayCleaningStatusAsync(CancellationToken cancellationToken)
    {
        return _database.GetReminderHistoryAsync(
            CleaningReminderType,
            DateOnly.FromDateTime(DateTime.Now),
            cancellationToken);
    }

    public Task MarkCleaningCompletedAsync(CancellationToken cancellationToken) =>
        SetTodayCleaningStatusAsync(ReminderHistoryStatus.Completed, ReminderAction.Completed, cancellationToken);

    public Task SkipCleaningTodayAsync(CancellationToken cancellationToken) =>
        SetTodayCleaningStatusAsync(ReminderHistoryStatus.Skipped, ReminderAction.Skipped, cancellationToken);

    public async Task<bool> SnoozeCleaningAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        var date = DateOnly.FromDateTime(now.LocalDateTime);
        var snoozed = await _database.TrySnoozeReminderAsync(
            CleaningReminderType,
            date,
            now.AddMinutes(10),
            MaximumCleaningSnoozes,
            cancellationToken);
        if (snoozed)
        {
            await _database.RecordReminderActionAsync(
                CleaningReminderType,
                date,
                ReminderAction.Snoozed.ToString(),
                ReminderHistoryStatus.Snoozed,
                now,
                cancellationToken);
            CleaningStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        return snoozed;
    }

    public static DateTime? CalculateCleaningReminderTime(
        ScheduleSettings scheduleSettings,
        DateOnly date,
        int advanceMinutes)
    {
        var profile = GetProfileForWorkday(scheduleSettings, date);
        return profile is null ? null : date.ToDateTime(profile.AfternoonEnd).AddMinutes(-advanceMinutes);
    }

    public static DateTime? CalculateScheduleReminderTime(
        ScheduleSettings scheduleSettings,
        DateOnly date,
        ScheduleReminderNode node,
        int lunchAdvanceMinutes = 5)
    {
        var profile = GetProfileForWorkday(scheduleSettings, date);
        if (profile is null)
        {
            return null;
        }

        return node switch
        {
            ScheduleReminderNode.LunchSoon => date.ToDateTime(profile.MorningEnd).AddMinutes(-lunchAdvanceMinutes),
            ScheduleReminderNode.LunchStart => date.ToDateTime(profile.MorningEnd),
            ScheduleReminderNode.AfternoonStart => date.ToDateTime(profile.AfternoonStart),
            ScheduleReminderNode.OffWork => date.ToDateTime(profile.AfternoonEnd),
            _ => null
        };
    }

    public static DateTime? CalculateNextCleaningReminderTime(
        ScheduleSettings scheduleSettings,
        DateTime now,
        int advanceMinutes)
    {
        for (var offset = 0; offset <= 14; offset++)
        {
            var date = DateOnly.FromDateTime(now.Date.AddDays(offset));
            var candidate = CalculateCleaningReminderTime(scheduleSettings, date, advanceMinutes);
            if (candidate > now)
            {
                return candidate;
            }
        }

        return null;
    }

    public static string CreateCleaningMessage(int advanceMinutes)
    {
        if (advanceMinutes == 30)
        {
            string[] templates =
            [
                "还有半小时下班，记得打扫一下卫生。",
                "快下班啦，记得提前把工位收拾一下。",
                "还有一会儿下班，可以开始整理桌面和卫生了。",
                "今天也快结束了，记得把工位整理干净。"
            ];
            return templates[Random.Shared.Next(templates.Length)];
        }

        return $"还有{ToChineseNumber(advanceMinutes)}分钟下班，记得打扫一下卫生。";
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _disposeTokenSource.Cancel();
        _scheduleEngine.StateChanged -= ScheduleEngine_OnStateChanged;
        _scheduleEngine.SettingsReloaded -= ScheduleEngine_OnSettingsReloaded;
        _workModeService.Changed -= WorkModeService_OnChanged;
        if (_activitySnapshotService is not null)
        {
            _activitySnapshotService.SnapshotUpdated -= ActivitySnapshotService_OnSnapshotUpdated;
        }

        _timer.Dispose();
        // 保留 CTS 至所有已排队回调观察到取消，避免定时器回调与 Dispose 竞态。
    }

    private static ScheduleProfile? GetProfileForWorkday(ScheduleSettings settings, DateOnly date)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return null;
        }

        var profileType = ScheduleEvaluator.ResolveProfile(settings, date);
        return profileType == ScheduleProfileType.Summer ? settings.SummerProfile : settings.WinterProfile;
    }

    private async void OnTimer(object? state)
    {
        if (Volatile.Read(ref _disposeState) != 0 || Interlocked.Exchange(ref _timerBusy, 1) != 0)
        {
            return;
        }

        try
        {
            var now = DateTimeOffset.Now;
            await ProcessOrdinarySnoozesAsync(now, _disposeTokenSource.Token);
            await ProcessScheduleRemindersAsync(now, _disposeTokenSource.Token);
            await ProcessCleaningReminderAsync(now, _disposeTokenSource.Token);
        }
        catch (OperationCanceledException) when (_disposeTokenSource.IsCancellationRequested)
        {
            // 正常退出。
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
        }
        finally
        {
            Volatile.Write(ref _timerBusy, 0);
        }
    }

    private async Task ProcessScheduleRemindersAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_allRemindersPaused)
        {
            return;
        }

        var date = DateOnly.FromDateTime(now.LocalDateTime);
        var candidates = new[]
        {
            (ScheduleReminderNode.LunchSoon, LunchSoonReminderType, _presentationSettings.LunchSoonEnabled),
            (ScheduleReminderNode.LunchStart, LunchStartReminderType, _presentationSettings.LunchStartEnabled),
            (ScheduleReminderNode.AfternoonStart, AfternoonStartReminderType, _presentationSettings.AfternoonStartEnabled),
            (ScheduleReminderNode.OffWork, OffWorkReminderType,
                _presentationSettings.OffWorkPopupEnabled || _presentationSettings.OffWorkSpeechEnabled)
        };

        foreach (var (node, historyType, enabled) in candidates)
        {
            if (!enabled)
            {
                continue;
            }

            if (node == ScheduleReminderNode.OffWork && _workModeService.IsOvertime)
            {
                continue;
            }

            var due = CalculateScheduleReminderTime(
                _scheduleEngine.Settings,
                date,
                node,
                _presentationSettings.LunchSoonAdvanceMinutes);
            if (!IsWithinDueWindow(now.LocalDateTime, due))
            {
                continue;
            }

            var request = CreateScheduleRequest(node, historyType, now, date);
            if (await _database.TryClaimReminderAsync(
                    request.HistoryType,
                    date,
                    now,
                    string.Empty,
                    cancellationToken))
            {
                await _presentationService.EnqueueAsync(request, cancellationToken);
            }
        }
    }

    private async Task ProcessOrdinarySnoozesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (_allRemindersPaused)
        {
            return;
        }

        var date = DateOnly.FromDateTime(now.LocalDateTime);
        var histories = await _database.GetSnoozedOrdinaryRemindersAsync(date, cancellationToken);
        foreach (var history in histories)
        {
            if (history.NextDueAt is not { } dueAt || dueAt > now)
            {
                continue;
            }

            if (now - dueAt > MissedReminderGrace)
            {
                await _database.SetReminderStatusAsync(
                    history.ReminderType,
                    date,
                    ReminderHistoryStatus.Expired,
                    now,
                    cancellationToken);
                continue;
            }

            var kind = history.ReminderType.StartsWith("Drink:", StringComparison.OrdinalIgnoreCase)
                ? ReminderKind.DrinkWater
                : ReminderKind.Stand;
            if (!CanDeliver(kind, now.LocalDateTime) ||
                !await _database.TryConsumeSnoozedReminderAsync(
                    history.ReminderType,
                    date,
                    now,
                    cancellationToken))
            {
                continue;
            }

            await EnqueueOrdinaryReminderAsync(
                kind,
                _activitySnapshotService?.Current ?? CreateEmptyActivitySnapshot(),
                now,
                cancellationToken,
                history.ReminderType,
                skipClaim: true);
        }
    }

    private async Task ProcessCleaningReminderAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_allRemindersPaused || !_cleaningSettings.Enabled)
        {
            return;
        }

        var date = DateOnly.FromDateTime(now.LocalDateTime);
        var history = await _database.GetReminderHistoryAsync(CleaningReminderType, date, cancellationToken);
        if (history?.Status == ReminderHistoryStatus.Snoozed && history.NextDueAt is { } snoozeDue)
        {
            var lateness = now - snoozeDue;
            if (lateness > MissedReminderGrace)
            {
                await _database.SetReminderStatusAsync(
                    CleaningReminderType, date, ReminderHistoryStatus.Expired, now, cancellationToken);
            }
            else if (lateness >= TimeSpan.Zero &&
                     await _database.TryConsumeSnoozedReminderAsync(
                         CleaningReminderType, date, now, cancellationToken))
            {
                await EnqueueCleaningReminderAsync(
                    history.Message ?? CreateCleaningMessage(_cleaningSettings.AdvanceMinutes),
                    now,
                    date,
                    cancellationToken);
            }

            return;
        }

        var due = CalculateCleaningReminderTime(
            _scheduleEngine.Settings,
            date,
            _cleaningSettings.AdvanceMinutes);
        if (!IsWithinDueWindow(now.LocalDateTime, due) ||
            _scheduleEngine.GetSnapshot(now.LocalDateTime).State != WorkScheduleState.Working)
        {
            return;
        }

        var message = CreateCleaningMessage(_cleaningSettings.AdvanceMinutes);
        if (await _database.TryClaimReminderAsync(
                CleaningReminderType, date, now, message, cancellationToken))
        {
            await EnqueueCleaningReminderAsync(message, now, date, cancellationToken);
            CleaningStatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private ReminderPresentationRequest CreateScheduleRequest(
        ScheduleReminderNode node,
        string historyType,
        DateTimeOffset now,
        DateOnly date)
    {
        var advance = _presentationSettings.LunchSoonAdvanceMinutes;
        var (kind, title, message, speech, actions) = node switch
        {
            ScheduleReminderNode.LunchSoon => (
                ReminderPresentationKind.LunchSoon,
                "快到午休时间啦",
                $"还有 {advance} 分钟，可以先把手头工作收个尾。",
                RandomText($"还有{ToChineseNumber(advance)}分钟午休，可以准备收尾啦。", "快到午休时间了，记得保存一下现在的工作。"),
                (IReadOnlyList<ReminderActionOption>)[new("知道了", ReminderAction.Acknowledge, true)]),
            ScheduleReminderNode.LunchStart => (
                ReminderPresentationKind.LunchStart,
                "午休时间到",
                "休息一下，下午再继续。",
                RandomText("午休时间到了，休息一下吧。", "上午辛苦啦，午休时间到了。"),
                (IReadOnlyList<ReminderActionOption>)[new("开始午休", ReminderAction.Acknowledge, true)]),
            ScheduleReminderNode.AfternoonStart => (
                ReminderPresentationKind.AfternoonStart,
                "下午开始啦",
                "休息结束，慢慢进入工作状态。",
                RandomText("下午工作开始啦，慢慢进入状态吧。", "下午好，休息结束，可以开始工作啦。"),
                (IReadOnlyList<ReminderActionOption>)[new("开始下午工作", ReminderAction.Acknowledge, true)]),
            _ => (
                ReminderPresentationKind.OffWork,
                "下班啦",
                "今天辛苦了，记得收尾和放松。",
                "今天的正常工作时间结束啦，辛苦了。",
                (IReadOnlyList<ReminderActionOption>)[
                    new("知道了", ReminderAction.Acknowledge, true),
                    new("开始加班", ReminderAction.StartOvertime)])
        };

        var showPopup = node != ScheduleReminderNode.OffWork || _presentationSettings.OffWorkPopupEnabled;
        var playSpeech = node != ScheduleReminderNode.OffWork || _presentationSettings.OffWorkSpeechEnabled;
        return new ReminderPresentationRequest(
            historyType,
            kind,
            ReminderPriority.ScheduleCritical,
            now,
            now.Add(MissedReminderGrace),
            title,
            message,
            speech,
            showPopup,
            playSpeech,
            actions,
            async (action, token) =>
            {
                if (action == ReminderAction.StartOvertime && !_workModeService.IsOvertime)
                {
                    _workModeService.ToggleOvertime();
                }

                await RecordActionAsync(historyType, date, action, token);
            },
            (shownAt, token) => _database.RecordReminderShownAsync(historyType, date, shownAt, token));
    }

    private Task EnqueueCleaningReminderAsync(
        string message,
        DateTimeOffset now,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var request = new ReminderPresentationRequest(
            CleaningReminderType,
            ReminderPresentationKind.Cleaning,
            ReminderPriority.Cleaning,
            now,
            now.AddMinutes(10),
            "下班前整理",
            message,
            message,
            true,
            true,
            [
                new("已完成", ReminderAction.Completed, true),
                new("10 分钟后提醒", ReminderAction.Snoozed),
                new("今天跳过", ReminderAction.Skipped)
            ],
            async (action, token) =>
            {
                switch (action)
                {
                    case ReminderAction.Completed:
                        await MarkCleaningCompletedAsync(token);
                        break;
                    case ReminderAction.Snoozed:
                        await SnoozeCleaningAsync(token);
                        break;
                    case ReminderAction.Skipped:
                        await SkipCleaningTodayAsync(token);
                        break;
                    default:
                        await RecordActionAsync(CleaningReminderType, date, action, token);
                        break;
                }
            },
            (shownAt, token) => _database.RecordReminderShownAsync(CleaningReminderType, date, shownAt, token));
        return _presentationService.EnqueueAsync(request, cancellationToken);
    }

    private async void ActivitySnapshotService_OnSnapshotUpdated(
        object? sender,
        ActivityDashboardSnapshot snapshot)
    {
        if (Volatile.Read(ref _disposeState) != 0 || Interlocked.Exchange(ref _ordinaryReminderBusy, 1) != 0)
        {
            return;
        }

        try
        {
            var now = DateTime.Now;
            var elapsed = _lastActivitySnapshotAt == default ? TimeSpan.Zero : now - _lastActivitySnapshotAt;
            _lastActivitySnapshotAt = now;
            if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromSeconds(30))
            {
                elapsed = TimeSpan.Zero;
            }

            if (!CanDeliver(ReminderKind.RegularWork, now))
            {
                _standReminderCycle.Reset();
                return;
            }

            if (snapshot.UserState == UserActivityState.Afk &&
                _workModeService.ActivityMode == ActivityMode.Computer)
            {
                _standReminderCycle.Reset();
                return;
            }

            if (_workModeService.ActivityMode == ActivityMode.Lab)
            {
                _standReminderCycle.Reset();
            }

            if (snapshot.UserState == UserActivityState.Active ||
                _workModeService.ActivityMode is ActivityMode.Lab or ActivityMode.Meeting)
            {
                _activeDrinkSeconds += elapsed.TotalSeconds;
            }

            if (_presentationSettings.DrinkEnabled &&
                _activeDrinkSeconds >= TimeSpan.FromMinutes(_presentationSettings.DrinkIntervalMinutes).TotalSeconds &&
                CanDeliver(ReminderKind.DrinkWater, now))
            {
                _activeDrinkSeconds = 0;
                await EnqueueOrdinaryReminderAsync(
                    ReminderKind.DrinkWater, snapshot, DateTimeOffset.Now, CancellationToken.None);
            }

            var standDue = _presentationSettings.StandEnabled &&
                           _workModeService.ActivityMode != ActivityMode.Lab &&
                           _standReminderCycle.Advance(
                               elapsed,
                               snapshot.UserState == UserActivityState.Active ||
                               _workModeService.ActivityMode == ActivityMode.Meeting,
                               TimeSpan.FromMinutes(_presentationSettings.StandContinuousMinutes));
            if (standDue &&
                CanDeliver(ReminderKind.Stand, now))
            {
                await EnqueueOrdinaryReminderAsync(
                    ReminderKind.Stand, snapshot, DateTimeOffset.Now, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
        }
        finally
        {
            Volatile.Write(ref _ordinaryReminderBusy, 0);
        }
    }

    private async Task EnqueueOrdinaryReminderAsync(
        ReminderKind kind,
        ActivityDashboardSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        string? historyTypeOverride = null,
        bool skipClaim = false)
    {
        var date = DateOnly.FromDateTime(now.LocalDateTime);
        var isDrink = kind == ReminderKind.DrinkWater;
        var historyType = historyTypeOverride ?? $"{(isDrink ? "Drink" : "Stand")}:{now:HHmmss}";
        var isMeeting = _workModeService.ActivityMode == ActivityMode.Meeting;
        var isOvertime = _workModeService.IsOvertime;
        var title = isDrink ? "喝口水吧" : "起来活动一下";
        var message = isDrink
            ? "已经工作一阵子了，补充一点水分。"
            : $"已经连续工作 {Math.Max(1, (int)snapshot.ContinuousWorkDuration.TotalMinutes)} 分钟了。";
        var speech = isOvertime
            ? isDrink ? "加班喝水提醒。" : "加班站立提醒。"
            : isDrink ? "喝水提醒。" : "站立提醒。";
        var playSpeech = !isMeeting && (isDrink
            ? _presentationSettings.DrinkSpeechEnabled
            : _presentationSettings.StandSpeechEnabled);
        var showPopup = isDrink
            ? _presentationSettings.DrinkPopupEnabled
            : _presentationSettings.StandPopupEnabled;
        if (!skipClaim && !await _database.TryClaimReminderAsync(
                historyType, date, now, string.Empty, cancellationToken))
        {
            return;
        }

        ReminderPresentationRequest? request = null;
        request = new ReminderPresentationRequest(
            historyType,
            isDrink ? ReminderPresentationKind.Drink : ReminderPresentationKind.Stand,
            isDrink ? ReminderPriority.Drink : ReminderPriority.Stand,
            now,
            now.AddMinutes(10),
            title,
            message,
            speech,
            showPopup,
            playSpeech,
            [
                new(isDrink ? "我喝了" : "我起来了", ReminderAction.Completed, true),
                new("10 分钟后提醒", ReminderAction.Snoozed)
            ],
            async (action, token) =>
            {
                if (action == ReminderAction.Completed)
                {
                    if (isDrink)
                    {
                        MarkDrinkCompleted();
                    }
                    else
                    {
                        MarkStandCompleted();
                    }
                }
                else if (action == ReminderAction.Snoozed)
                {
                    var snoozed = await _database.TrySnoozeReminderAsync(
                        historyType,
                        date,
                        DateTimeOffset.Now.AddMinutes(10),
                        MaximumOrdinarySnoozes,
                        token);
                    if (snoozed && request is not null)
                    {
                        _ = RequeueOrdinaryAfterSnoozeAsync(request, date);
                    }
                }

                await RecordActionAsync(historyType, date, action, token);
            },
            (shownAt, token) => _database.RecordReminderShownAsync(historyType, date, shownAt, token));
        await _presentationService.EnqueueAsync(request, cancellationToken);
    }

    private async Task RequeueOrdinaryAfterSnoozeAsync(
        ReminderPresentationRequest request,
        DateOnly date)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(10), _disposeTokenSource.Token);
            var kind = request.Kind == ReminderPresentationKind.Drink
                ? ReminderKind.DrinkWater
                : ReminderKind.Stand;
            if (Volatile.Read(ref _disposeState) != 0 || !CanDeliver(kind, DateTime.Now))
            {
                return;
            }

            var now = DateTimeOffset.Now;
            if (await _database.TryConsumeSnoozedReminderAsync(
                    request.HistoryType, date, now, _disposeTokenSource.Token))
            {
                await _presentationService.EnqueueAsync(
                    request with { TriggeredAt = now, ExpiresAt = now.AddMinutes(10) },
                    _disposeTokenSource.Token);
            }
        }
        catch (OperationCanceledException) when (_disposeTokenSource.IsCancellationRequested)
        {
            // 正常退出。
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
        }
    }

    private static ActivityDashboardSnapshot CreateEmptyActivitySnapshot() => new(
        null,
        null,
        null,
        null,
        null,
        [],
        TimeSpan.Zero,
        TimeSpan.Zero,
        0,
        null,
        0,
        UserActivityState.Afk);

    private async Task SetTodayCleaningStatusAsync(
        ReminderHistoryStatus status,
        ReminderAction action,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        var date = DateOnly.FromDateTime(now.LocalDateTime);
        await _database.SetReminderStatusAsync(
            CleaningReminderType, date, status, now, cancellationToken);
        await _database.RecordReminderActionAsync(
            CleaningReminderType, date, action.ToString(), status, now, cancellationToken);
        CleaningStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private Task RecordActionAsync(
        string historyType,
        DateOnly date,
        ReminderAction action,
        CancellationToken cancellationToken)
    {
        var status = action switch
        {
            ReminderAction.Completed or ReminderAction.Acknowledge or ReminderAction.StartOvertime =>
                ReminderHistoryStatus.Completed,
            ReminderAction.Snoozed => ReminderHistoryStatus.Snoozed,
            ReminderAction.Skipped => ReminderHistoryStatus.Skipped,
            ReminderAction.Preempted => ReminderHistoryStatus.Preempted,
            _ => ReminderHistoryStatus.Dismissed
        };
        return _database.RecordReminderActionAsync(
            historyType, date, action.ToString(), status, DateTimeOffset.Now, cancellationToken);
    }

    private static bool IsWithinDueWindow(DateTime now, DateTime? due)
    {
        if (due is null)
        {
            return false;
        }

        var lateness = now - due.Value;
        return lateness >= TimeSpan.Zero && lateness <= MissedReminderGrace;
    }

    private void ScheduleEngine_OnStateChanged(object? sender, ScheduleChangedEventArgs e)
    {
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        _ = ProcessSoonAsync();
    }

    private void ScheduleEngine_OnSettingsReloaded(object? sender, EventArgs e)
    {
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        _ = ProcessSoonAsync();
    }

    private void WorkModeService_OnChanged(object? sender, EventArgs e)
    {
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ProcessSoonAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), _disposeTokenSource.Token);
            OnTimer(null);
        }
        catch (OperationCanceledException) when (_disposeTokenSource.IsCancellationRequested)
        {
            // 正常退出。
        }
    }

    private static string RandomText(params string[] templates) =>
        templates[Random.Shared.Next(templates.Length)];

    private static string ToChineseNumber(int number)
    {
        string[] digits = ["零", "一", "二", "三", "四", "五", "六", "七", "八", "九"];
        if (number < 10)
        {
            return digits[number];
        }

        if (number < 20)
        {
            return $"十{(number == 10 ? string.Empty : digits[number % 10])}";
        }

        if (number < 100)
        {
            return $"{digits[number / 10]}十{(number % 10 == 0 ? string.Empty : digits[number % 10])}";
        }

        return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
