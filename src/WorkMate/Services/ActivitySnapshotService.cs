using WorkMate.Collectors;
using WorkMate.Infrastructure;
using WorkMate.Models;

namespace WorkMate.Services;

public interface IActivitySnapshotService : IActivitySeriesProvider, IDisposable
{
    event EventHandler<ActivityDashboardSnapshot>? SnapshotUpdated;

    ActivityDashboardSnapshot Current { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task FlushAsync(CancellationToken cancellationToken);

    void SetSystemAvailable(bool available);
}

public sealed class ActivitySnapshotService : IActivitySnapshotService
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumContinuousSample = TimeSpan.FromSeconds(30);
    private const int SamplesPerPersistenceFlush = 60;

    private readonly DatabaseStore _database;
    private readonly ScheduleEngine _scheduleEngine;
    private readonly WorkModeService _workModeService;
    private readonly KeyboardActivityCollector _keyboardCollector;
    private readonly MouseActivityCollector _mouseCollector;
    private readonly IdleActivityCollector _idleCollector;
    private readonly ForegroundWindowCollector _foregroundCollector;
    private readonly ActivityAggregator _aggregator = new();
    private readonly AppUsageService _appUsage = new();
    private readonly WorkSessionTracker _workSession = new();
    private readonly WorkIntensityCalculator _intensityCalculator = new();
    private readonly SemaphoreSlim _tickLock = new(1, 1);
    private readonly Dictionary<DateTime, ActivityBucket> _dirtyBuckets = new();
    private readonly HashSet<DateOnly> _dirtyUsageDates = [];
    private readonly System.Threading.Timer _timer;
    private ActivityDashboardSnapshot _current = CreateEmptySnapshot();
    private DateTime _lastSampleAt;
    private bool _keyboardAvailable;
    private bool _mouseAvailable;
    private bool _foregroundAvailable;
    private volatile bool _systemAvailable = true;
    private int _ticksSinceFlush;
    private bool _disposed;

    public ActivitySnapshotService(
        DatabaseStore database,
        ScheduleEngine scheduleEngine,
        WorkModeService workModeService)
    {
        _database = database;
        _scheduleEngine = scheduleEngine;
        _workModeService = workModeService;
        _keyboardCollector = new KeyboardActivityCollector();
        _mouseCollector = new MouseActivityCollector();
        _idleCollector = new IdleActivityCollector();
        _foregroundCollector = new ForegroundWindowCollector();
        _timer = new System.Threading.Timer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler<ActivityDashboardSnapshot>? SnapshotUpdated;

    public ActivityDashboardSnapshot Current => Volatile.Read(ref _current);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var today = DateOnly.FromDateTime(DateTime.Now);
        _aggregator.Load(await _database.GetActivityBucketsAsync(today, cancellationToken));
        _appUsage.Load(await _database.GetAppUsageAsync(today, cancellationToken));

        _keyboardAvailable = TryStart(_keyboardCollector.Start);
        _mouseAvailable = TryStart(_mouseCollector.Start);
        _foregroundAvailable = TryStart(_foregroundCollector.Start);
        _lastSampleAt = DateTime.Now;
        PublishSnapshot(_lastSampleAt, null, null, UserActivityState.Afk, 0);
        _timer.Change(SampleInterval, SampleInterval);
    }

    public ActivityDashboardSnapshot GetSnapshot(DateTime now)
    {
        _ = now;
        return Current;
    }

    public void SetSystemAvailable(bool available)
    {
        if (_disposed || _systemAvailable == available)
        {
            return;
        }

        _systemAvailable = available;
        if (!available)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            DrainPendingCollectorData();
            _workSession.Reset();
            _intensityCalculator.Reset();
            PublishSnapshot(DateTime.Now, null, null, UserActivityState.Afk, 0);
            _ = FlushSafelyAsync();
        }
        else
        {
            DrainPendingCollectorData();
            _lastSampleAt = DateTime.Now;
            _intensityCalculator.Reset();
            _timer.Change(SampleInterval, SampleInterval);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_systemAvailable && !_disposed)
        {
            await ProcessTickAsync(DateTime.Now, cancellationToken);
        }

        await _tickLock.WaitAsync(cancellationToken);
        try
        {
            await PersistDirtyAsync(cancellationToken);
        }
        finally
        {
            _tickLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        try
        {
            FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
        }

        _disposed = true;

        _timer.Dispose();
        _foregroundCollector.Dispose();
        _mouseCollector.Dispose();
        _keyboardCollector.Dispose();
        _tickLock.Dispose();
    }

    private async void OnTimer(object? state)
    {
        _ = state;
        try
        {
            await ProcessTickAsync(DateTime.Now, CancellationToken.None);
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
        }
    }

    private async Task ProcessTickAsync(DateTime now, CancellationToken cancellationToken)
    {
        if (_disposed || !_systemAvailable || !await _tickLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var start = _lastSampleAt;
            _lastSampleAt = now;
            var elapsed = now - start;
            if (elapsed <= TimeSpan.Zero)
            {
                return;
            }

            var keyboardCount = _keyboardAvailable ? _keyboardCollector.DrainCount() : 0;
            var mouse = _mouseAvailable ? _mouseCollector.Drain() : ActivityInputDelta.Empty;
            var switches = _foregroundAvailable ? _foregroundCollector.DrainSwitchCount() : 0;
            if (elapsed > MaximumContinuousSample)
            {
                _workSession.Reset();
                _intensityCalculator.Reset();
                PublishSnapshot(now, null, _foregroundCollector.GetCurrent(), UserActivityState.Afk, 0);
                return;
            }

            IdleActivitySnapshot idle;
            try
            {
                idle = _idleCollector.GetSnapshot();
            }
            catch (Exception exception)
            {
                FileLogger.Write(exception);
                idle = new IdleActivitySnapshot(elapsed, UserActivityState.Afk);
            }

            var input = mouse with { KeyboardCount = keyboardCount };
            if (start.Date != now.Date)
            {
                _workSession.ResetDay();
                _intensityCalculator.Reset();
            }

            var scheduleState = _scheduleEngine.GetSnapshot(start + TimeSpan.FromTicks(elapsed.Ticks / 2)).State;
            var context = new WorkClassificationContext(
                scheduleState,
                _workModeService.ActivityMode,
                _workModeService.IsOvertime,
                idle.State,
                _systemAvailable);
            var category = WorkSliceClassifier.Classify(context);
            var session = _workSession.Advance(elapsed, context);
            var intensity = _intensityCalculator.Calculate(new ActivityIntensityInput(
                input,
                elapsed,
                idle.State,
                session.ContinuousDuration,
                idle.State == UserActivityState.Active ? 1 : 0,
                switches));
            var foreground = _foregroundAvailable ? _foregroundCollector.GetCurrent() : null;
            var foregroundUsage = _foregroundAvailable
                ? _foregroundCollector.DrainUsage(new DateTimeOffset(now))
                : [];
            var changed = _aggregator.Add(new ActivityAggregationSample(
                start,
                now,
                input,
                idle.State,
                switches,
                foreground?.ProcessName,
                _workModeService.ResolveScheduleState(scheduleState),
                _workModeService.ActivityMode,
                category,
                intensity.ActivityScore,
                intensity.WorkIntensity,
                session.LongestContinuousDuration.TotalSeconds));
            foreach (var bucket in changed)
            {
                _dirtyBuckets[bucket.BucketStart] = bucket;
            }

            foreach (var usage in foregroundUsage)
            {
                AddAppUsageAcrossDates(
                    usage.Start.LocalDateTime,
                    usage.End.LocalDateTime,
                    usage.ProcessName,
                    idle.State == UserActivityState.Active);
            }
            PublishSnapshot(now, intensity, foreground, idle.State, idle.IdleDuration.TotalSeconds);

            _ticksSinceFlush++;
            if (_ticksSinceFlush >= SamplesPerPersistenceFlush)
            {
                await PersistDirtyAsync(cancellationToken);
            }
        }
        finally
        {
            _tickLock.Release();
        }
    }

    private void AddAppUsageAcrossDates(DateTime start, DateTime end, string? processName, bool isActive)
    {
        var segmentStart = start;
        while (segmentStart < end)
        {
            var segmentEnd = end < segmentStart.Date.AddDays(1) ? end : segmentStart.Date.AddDays(1);
            var duration = segmentEnd - segmentStart;
            var date = DateOnly.FromDateTime(segmentStart);
            _appUsage.Add(date, processName, duration, isActive ? duration : TimeSpan.Zero);
            _dirtyUsageDates.Add(date);
            segmentStart = segmentEnd;
        }
    }

    private async Task PersistDirtyAsync(CancellationToken cancellationToken)
    {
        if (_dirtyBuckets.Count > 0)
        {
            await _database.UpsertActivityBucketsAsync(_dirtyBuckets.Values, cancellationToken);
            _dirtyBuckets.Clear();
        }

        foreach (var date in _dirtyUsageDates.ToArray())
        {
            await _database.UpsertAppUsageAsync(_appUsage.GetForDate(date), cancellationToken);
            _dirtyUsageDates.Remove(date);
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        _aggregator.RemoveBefore(today);
        _appUsage.RemoveBefore(today);

        _ticksSinceFlush = 0;
    }

    private void PublishSnapshot(
        DateTime now,
        ActivityIntensityResult? intensity,
        ForegroundAppSnapshot? foreground,
        UserActivityState userState,
        double idleSeconds)
    {
        var buckets = _aggregator.GetBuckets(DateOnly.FromDateTime(now));
        var session = _workSession.GetSnapshot();
        var totalWorkSeconds = buckets.Sum(static bucket =>
            bucket.NormalWorkSeconds + bucket.OvertimeSeconds + bucket.ManualWorkSeconds);
        var snapshot = new ActivityDashboardSnapshot(
            intensity?.ActivityScore ?? (buckets.Count == 0 ? null : (int?)Math.Round(buckets[^1].ActivityScore)),
            intensity?.WorkIntensity ?? (buckets.Count == 0 ? null : (int?)Math.Round(buckets[^1].WorkIntensity)),
            _keyboardAvailable ? buckets.Sum(static bucket => bucket.KeyboardCount) : null,
            _mouseAvailable ? buckets.Sum(static bucket => bucket.MouseClickCount) : null,
            _foregroundAvailable ? buckets.Sum(static bucket => bucket.AppSwitchCount) : null,
            buckets.Select(static bucket => new ActivitySeriesPoint(bucket.BucketStart, bucket.WorkIntensity)).ToArray(),
            TimeSpan.FromSeconds(totalWorkSeconds),
            session.ContinuousDuration,
            buckets.Sum(static bucket => bucket.MouseDistance),
            foreground?.ProcessName,
            idleSeconds,
            userState);
        Volatile.Write(ref _current, snapshot);
        SnapshotUpdated?.Invoke(this, snapshot);
    }

    private static bool TryStart(Action start)
    {
        try
        {
            start();
            return true;
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
            return false;
        }
    }

    private void DrainPendingCollectorData()
    {
        if (_keyboardAvailable)
        {
            _keyboardCollector.DrainCount();
        }

        if (_mouseAvailable)
        {
            _mouseCollector.Drain();
            _mouseCollector.ResetPosition();
        }

        if (_foregroundAvailable)
        {
            _foregroundCollector.DrainSwitchCount();
            _foregroundCollector.DrainUsage(DateTimeOffset.Now);
        }
    }

    private async Task FlushSafelyAsync()
    {
        try
        {
            await FlushAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
        }
    }

    private static ActivityDashboardSnapshot CreateEmptySnapshot() => new(
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
}
