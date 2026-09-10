using WorkMate.Infrastructure;
using WorkMate.Models;

namespace WorkMate.Services;

public sealed class DailySummaryService : IDisposable
{
    private readonly DatabaseStore _database;
    private readonly IActivitySnapshotService _activity;
    private readonly ScheduleEngine _scheduleEngine;
    private readonly WorkModeService _workModeService;
    private bool _previousOvertime;
    private DateOnly _trackedDate;
    private bool _disposed;

    public DailySummaryService(
        DatabaseStore database,
        IActivitySnapshotService activity,
        ScheduleEngine scheduleEngine,
        WorkModeService workModeService)
    {
        _database = database;
        _activity = activity;
        _scheduleEngine = scheduleEngine;
        _workModeService = workModeService;
        _previousOvertime = workModeService.IsOvertime;
        _trackedDate = DateOnly.FromDateTime(DateTime.Now);
        _scheduleEngine.StateChanged += ScheduleEngine_OnStateChanged;
        _workModeService.Changed += WorkModeService_OnChanged;
        _activity.SnapshotUpdated += Activity_OnSnapshotUpdated;
    }

    public event EventHandler<DailySummary>? SummaryGenerated;

    public Task<DailySummary> InitializeAsync(CancellationToken cancellationToken)
    {
        return GenerateAsync(_trackedDate.AddDays(-1), cancellationToken);
    }

    public async Task<DailySummary> GenerateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        await _activity.FlushAsync(cancellationToken);
        var buckets = await _database.GetActivityBucketsAsync(date, cancellationToken);
        var applications = await _database.GetAppUsageAsync(date, cancellationToken);
        var normalSeconds = buckets.Sum(static bucket => bucket.NormalWorkSeconds);
        var overtimeSeconds = buckets.Sum(static bucket => bucket.OvertimeSeconds);
        var labSeconds = buckets.Sum(static bucket => bucket.LabSeconds);
        var meetingSeconds = buckets.Sum(static bucket => bucket.MeetingSeconds);
        var manualSeconds = buckets.Sum(static bucket => bucket.ManualWorkSeconds);
        var workSeconds = normalSeconds + overtimeSeconds + manualSeconds;
        var weightedIntensitySeconds = buckets.Sum(static bucket =>
            bucket.WorkIntensity *
            (bucket.NormalWorkSeconds + bucket.OvertimeSeconds + bucket.ManualWorkSeconds));
        var summary = new DailySummary(
            date,
            TimeSpan.FromSeconds(normalSeconds),
            TimeSpan.FromSeconds(overtimeSeconds),
            TimeSpan.FromSeconds(labSeconds),
            TimeSpan.FromSeconds(meetingSeconds),
            TimeSpan.FromSeconds(workSeconds),
            TimeSpan.FromSeconds(buckets.Sum(static bucket => bucket.ActiveSeconds)),
            TimeSpan.FromSeconds(buckets.Sum(static bucket => bucket.AfkSeconds)),
            buckets.Sum(static bucket => bucket.KeyboardCount),
            buckets.Sum(static bucket => bucket.MouseClickCount),
            buckets.Sum(static bucket => bucket.MouseDistance),
            buckets.Sum(static bucket => bucket.ScrollCount),
            buckets.Sum(static bucket => bucket.AppSwitchCount),
            workSeconds <= 0 ? 0 : (int)Math.Round(weightedIntensitySeconds / workSeconds),
            CalculateLongestContinuousWork(buckets),
            applications
                .Take(5)
                .Select(static entry => new DailyApplicationSummary(
                    entry.ProcessName,
                    entry.ActiveForegroundDuration))
                .ToArray());
        await _database.UpsertDailySummaryAsync(summary, cancellationToken);
        SummaryGenerated?.Invoke(this, summary);
        return summary;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scheduleEngine.StateChanged -= ScheduleEngine_OnStateChanged;
        _workModeService.Changed -= WorkModeService_OnChanged;
        _activity.SnapshotUpdated -= Activity_OnSnapshotUpdated;
    }

    public static TimeSpan CalculateLongestContinuousWork(IEnumerable<ActivityBucket> source)
    {
        var buckets = source.OrderBy(static item => item.BucketStart).ToArray();
        var recordedLongest = buckets.Length == 0
            ? 0
            : buckets.Max(static bucket => bucket.LongestContinuousWorkSeconds);
        if (recordedLongest > 0)
        {
            return TimeSpan.FromSeconds(recordedLongest);
        }

        var longestSeconds = 0d;
        var currentSeconds = 0d;
        DateTime? previousEnd = null;
        WorkTimeCategory previousCategory = WorkTimeCategory.None;
        foreach (var bucket in buckets)
        {
            var (category, workSeconds) = ResolveDominantWork(bucket);
            var isContiguous = previousEnd is not null &&
                               bucket.BucketStart <= previousEnd.Value.AddSeconds(1) &&
                               category == previousCategory;
            currentSeconds = category == WorkTimeCategory.None
                ? 0
                : (isContiguous ? currentSeconds : 0) + workSeconds;
            longestSeconds = Math.Max(longestSeconds, currentSeconds);
            previousEnd = bucket.BucketEnd;
            previousCategory = category;
        }

        return TimeSpan.FromSeconds(longestSeconds);
    }

    private static (WorkTimeCategory Category, double Seconds) ResolveDominantWork(ActivityBucket bucket)
    {
        var candidates = new[]
        {
            (WorkTimeCategory.Normal, bucket.NormalWorkSeconds),
            (WorkTimeCategory.Overtime, bucket.OvertimeSeconds),
            (WorkTimeCategory.Lab, bucket.LabSeconds),
            (WorkTimeCategory.Meeting, bucket.MeetingSeconds)
        };
        var dominant = candidates.MaxBy(static item => item.Item2);
        return dominant.Item2 <= 0 ? (WorkTimeCategory.None, 0) : dominant;
    }

    private void ScheduleEngine_OnStateChanged(object? sender, ScheduleChangedEventArgs e)
    {
        if (e.Current.State == WorkScheduleState.OffWork && e.Previous.State != WorkScheduleState.OffWork)
        {
            _ = GenerateSafelyAsync(DateOnly.FromDateTime(e.Previous.Timestamp));
        }
    }

    private void WorkModeService_OnChanged(object? sender, EventArgs e)
    {
        var overtime = _workModeService.IsOvertime;
        if (_previousOvertime && !overtime)
        {
            _ = GenerateSafelyAsync(DateOnly.FromDateTime(DateTime.Now));
        }

        _previousOvertime = overtime;
    }

    private void Activity_OnSnapshotUpdated(object? sender, ActivityDashboardSnapshot snapshot)
    {
        _ = snapshot;
        var currentDate = DateOnly.FromDateTime(DateTime.Now);
        if (currentDate == _trackedDate)
        {
            return;
        }

        var completedDate = _trackedDate;
        _trackedDate = currentDate;
        _ = GenerateSafelyAsync(completedDate);
    }

    private async Task GenerateSafelyAsync(DateOnly date)
    {
        try
        {
            await GenerateAsync(date, CancellationToken.None);
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
        }
    }
}
