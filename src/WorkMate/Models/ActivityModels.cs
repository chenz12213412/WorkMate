namespace WorkMate.Models;

public sealed record ActivitySeriesPoint(DateTime Time, double Value);

public enum UserActivityState
{
    Active,
    Idle,
    Afk
}

public sealed record ActivityThresholds(TimeSpan IdleThreshold, TimeSpan AfkThreshold)
{
    public static ActivityThresholds Default { get; } = new(
        TimeSpan.FromMinutes(3),
        TimeSpan.FromMinutes(5));

    public UserActivityState Classify(TimeSpan idleDuration)
    {
        if (idleDuration >= AfkThreshold)
        {
            return UserActivityState.Afk;
        }

        return idleDuration >= IdleThreshold
            ? UserActivityState.Idle
            : UserActivityState.Active;
    }
}

public sealed record IdleActivitySnapshot(TimeSpan IdleDuration, UserActivityState State);

public sealed record ActivityInputDelta(
    long KeyboardCount,
    long LeftClickCount,
    long RightClickCount,
    long MiddleClickCount,
    long WheelCount,
    double MouseMoveDistance)
{
    public long MouseClickCount => LeftClickCount + RightClickCount + MiddleClickCount;

    public static ActivityInputDelta Empty { get; } = new(0, 0, 0, 0, 0, 0);
}

public sealed record ForegroundAppSnapshot(
    int ProcessId,
    string ProcessName,
    string WindowTitle,
    DateTimeOffset ForegroundStart);

public sealed record ForegroundUsageDelta(
    string ProcessName,
    DateTimeOffset Start,
    DateTimeOffset End);

public sealed record AppUsageEntry(
    DateOnly Date,
    string ProcessName,
    TimeSpan ForegroundDuration,
    TimeSpan ActiveForegroundDuration);

public enum WorkTimeCategory
{
    None,
    Normal,
    Overtime,
    Lab,
    Meeting
}

public sealed record WorkClassificationContext(
    WorkScheduleState ScheduleState,
    ActivityMode ActivityMode,
    bool IsOvertime,
    UserActivityState UserState,
    bool SystemAvailable);

public sealed record WorkSessionSnapshot(
    WorkTimeCategory CurrentCategory,
    TimeSpan ContinuousDuration,
    TimeSpan LongestContinuousDuration);

public sealed record ActivityIntensityInput(
    ActivityInputDelta Input,
    TimeSpan Interval,
    UserActivityState UserState,
    TimeSpan ContinuousWorkDuration,
    double RecentActiveRatio,
    int AppSwitchCount);

public sealed record ActivityIntensityResult(int ActivityScore, int WorkIntensity);

public sealed record DailyApplicationSummary(string ProcessName, TimeSpan ActiveDuration);

public sealed record DailySummary(
    DateOnly Date,
    TimeSpan NormalWorkDuration,
    TimeSpan OvertimeDuration,
    TimeSpan LabDuration,
    TimeSpan MeetingDuration,
    TimeSpan TotalWorkDuration,
    TimeSpan ActiveComputerDuration,
    TimeSpan AfkDuration,
    long KeyboardCount,
    long MouseClickCount,
    double MouseDistance,
    long ScrollCount,
    int AppSwitchCount,
    int AverageWorkIntensity,
    TimeSpan LongestContinuousWork,
    IReadOnlyList<DailyApplicationSummary> TopApplications);

public sealed record ActivityTrackingSettings(bool AutoOvertimePromptEnabled)
{
    public static ActivityTrackingSettings Default { get; } = new(true);
}

public sealed record ActivityAggregationSample(
    DateTime Start,
    DateTime End,
    ActivityInputDelta Input,
    UserActivityState UserState,
    int AppSwitchCount,
    string? ForegroundProcess,
    WorkScheduleState ScheduleState,
    ActivityMode WorkMode,
    WorkTimeCategory WorkCategory,
    double ActivityScore,
    double WorkIntensity,
    double LongestContinuousWorkSeconds = 0);

public sealed record ActivityBucket(
    DateTime BucketStart,
    DateTime BucketEnd,
    long KeyboardCount,
    long MouseClickCount,
    double MouseDistance,
    long ScrollCount,
    double ActiveSeconds,
    double IdleSeconds,
    double AfkSeconds,
    int AppSwitchCount,
    string? DominantProcess,
    WorkScheduleState ScheduleState,
    ActivityMode WorkMode,
    double NormalWorkSeconds,
    double OvertimeSeconds,
    double ManualWorkSeconds,
    double LabSeconds,
    double MeetingSeconds,
    double ActivityScore,
    double WorkIntensity,
    double LongestContinuousWorkSeconds);

public sealed record ActivityDashboardSnapshot(
    int? ActivityScore,
    int? WorkIntensity,
    long? KeyboardCount,
    long? MouseClickCount,
    int? AppSwitchCount,
    IReadOnlyList<ActivitySeriesPoint> Series,
    TimeSpan TodayWorkDuration,
    TimeSpan ContinuousWorkDuration,
    double MouseDistance,
    string? CurrentProcess,
    double IdleSeconds,
    UserActivityState UserState);
