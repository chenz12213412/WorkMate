using WorkMate.Models;

namespace WorkMate.Services;

public sealed class ActivityAggregator
{
    public static readonly TimeSpan BucketDuration = TimeSpan.FromMinutes(5);

    private readonly object _syncRoot = new();
    private readonly SortedDictionary<DateTime, MutableBucket> _buckets = new();

    public IReadOnlyList<ActivityBucket> Add(ActivityAggregationSample sample)
    {
        if (sample.End <= sample.Start)
        {
            return [];
        }

        var segments = BuildSegments(sample.Start, sample.End);
        var keyboardAllocations = AllocateDiscrete(sample.Input.KeyboardCount, segments);
        var mouseClickAllocations = AllocateDiscrete(sample.Input.MouseClickCount, segments);
        var scrollAllocations = AllocateDiscrete(sample.Input.WheelCount, segments);
        var appSwitchAllocations = AllocateDiscrete(sample.AppSwitchCount, segments);
        var changed = new List<ActivityBucket>();
        lock (_syncRoot)
        {
            var totalSeconds = (sample.End - sample.Start).TotalSeconds;
            for (var index = 0; index < segments.Count; index++)
            {
                var segment = segments[index];
                var segmentSeconds = segment.Seconds;
                var ratio = segmentSeconds / totalSeconds;

                if (!_buckets.TryGetValue(segment.BucketStart, out var bucket))
                {
                    bucket = new MutableBucket(segment.BucketStart, segment.BucketStart.Add(BucketDuration));
                    _buckets.Add(segment.BucketStart, bucket);
                }

                bucket.Add(
                    sample,
                    segmentSeconds,
                    ratio,
                    keyboardAllocations[index],
                    mouseClickAllocations[index],
                    scrollAllocations[index],
                    checked((int)appSwitchAllocations[index]));
                changed.Add(bucket.ToRecord());
            }
        }

        return changed;
    }

    public IReadOnlyList<ActivityBucket> GetBuckets(DateOnly date)
    {
        lock (_syncRoot)
        {
            return _buckets.Values
                .Where(bucket => DateOnly.FromDateTime(bucket.BucketStart) == date)
                .Select(static bucket => bucket.ToRecord())
                .ToArray();
        }
    }

    public void Load(IEnumerable<ActivityBucket> buckets)
    {
        lock (_syncRoot)
        {
            foreach (var bucket in buckets)
            {
                _buckets[bucket.BucketStart] = new MutableBucket(bucket);
            }
        }
    }

    public void RemoveBefore(DateOnly date)
    {
        lock (_syncRoot)
        {
            var oldKeys = _buckets.Keys
                .Where(key => DateOnly.FromDateTime(key) < date)
                .ToArray();
            foreach (var key in oldKeys)
            {
                _buckets.Remove(key);
            }
        }
    }

    public static DateTime FloorToBucket(DateTime timestamp)
    {
        var minute = timestamp.Minute - (timestamp.Minute % 5);
        return new DateTime(
            timestamp.Year,
            timestamp.Month,
            timestamp.Day,
            timestamp.Hour,
            minute,
            0,
            timestamp.Kind);
    }

    private static IReadOnlyList<Segment> BuildSegments(DateTime start, DateTime end)
    {
        var segments = new List<Segment>();
        var segmentStart = start;
        while (segmentStart < end)
        {
            var bucketStart = FloorToBucket(segmentStart);
            var bucketEnd = bucketStart.Add(BucketDuration);
            var midnight = segmentStart.Date.AddDays(1);
            var segmentEnd = new[] { end, bucketEnd, midnight }.Min();
            segments.Add(new Segment(bucketStart, (segmentEnd - segmentStart).TotalSeconds));
            segmentStart = segmentEnd;
        }

        return segments;
    }

    private static long[] AllocateDiscrete(long total, IReadOnlyList<Segment> segments)
    {
        var allocations = new long[segments.Count];
        if (total <= 0 || segments.Count == 0)
        {
            return allocations;
        }

        var totalSeconds = segments.Sum(static segment => segment.Seconds);
        var remainders = new (int Index, double Fraction)[segments.Count];
        long allocated = 0;
        for (var index = 0; index < segments.Count; index++)
        {
            var raw = total * (segments[index].Seconds / totalSeconds);
            var floor = (long)Math.Floor(raw);
            allocations[index] = floor;
            allocated += floor;
            remainders[index] = (index, raw - floor);
        }

        var remaining = total - allocated;
        foreach (var remainder in remainders
                     .OrderByDescending(static item => item.Fraction)
                     .ThenBy(static item => item.Index)
                     .Take(checked((int)remaining)))
        {
            allocations[remainder.Index]++;
        }

        return allocations;
    }

    private sealed record Segment(DateTime BucketStart, double Seconds);

    private sealed class MutableBucket
    {
        private readonly Dictionary<string, double> _processDurations = new(StringComparer.OrdinalIgnoreCase);
        private long _keyboardCount;
        private long _mouseClickCount;
        private long _scrollCount;
        private int _appSwitchCount;
        private double _activityScoreWeightedSeconds;
        private double _activitySampleSeconds;
        private double _workIntensityWeightedSeconds;
        private double _workIntensitySampleSeconds;

        public MutableBucket(DateTime bucketStart, DateTime bucketEnd)
        {
            BucketStart = bucketStart;
            BucketEnd = bucketEnd;
        }

        public MutableBucket(ActivityBucket bucket)
        {
            BucketStart = bucket.BucketStart;
            BucketEnd = bucket.BucketEnd;
            _keyboardCount = bucket.KeyboardCount;
            _mouseClickCount = bucket.MouseClickCount;
            MouseDistance = bucket.MouseDistance;
            _scrollCount = bucket.ScrollCount;
            ActiveSeconds = bucket.ActiveSeconds;
            IdleSeconds = bucket.IdleSeconds;
            AfkSeconds = bucket.AfkSeconds;
            _appSwitchCount = bucket.AppSwitchCount;
            ScheduleState = bucket.ScheduleState;
            WorkMode = bucket.WorkMode;
            NormalWorkSeconds = bucket.NormalWorkSeconds;
            OvertimeSeconds = bucket.OvertimeSeconds;
            ManualWorkSeconds = bucket.ManualWorkSeconds;
            LabSeconds = bucket.LabSeconds;
            MeetingSeconds = bucket.MeetingSeconds;
            LongestContinuousWorkSeconds = bucket.LongestContinuousWorkSeconds;
            _activitySampleSeconds = Math.Max(1, ActiveSeconds + IdleSeconds + AfkSeconds);
            _activityScoreWeightedSeconds = bucket.ActivityScore * _activitySampleSeconds;
            _workIntensitySampleSeconds = NormalWorkSeconds + OvertimeSeconds + ManualWorkSeconds;
            _workIntensityWeightedSeconds = bucket.WorkIntensity * _workIntensitySampleSeconds;
            if (ApplicationProcessPolicy.ShouldPersistApplication(bucket.DominantProcess))
            {
                _processDurations[bucket.DominantProcess!] = _activitySampleSeconds;
            }
        }

        public DateTime BucketStart { get; }

        public DateTime BucketEnd { get; }

        public double MouseDistance { get; private set; }

        public double ActiveSeconds { get; private set; }

        public double IdleSeconds { get; private set; }

        public double AfkSeconds { get; private set; }

        public WorkScheduleState ScheduleState { get; private set; }

        public ActivityMode WorkMode { get; private set; }

        public double NormalWorkSeconds { get; private set; }

        public double OvertimeSeconds { get; private set; }

        public double ManualWorkSeconds { get; private set; }

        public double LabSeconds { get; private set; }

        public double MeetingSeconds { get; private set; }

        public double LongestContinuousWorkSeconds { get; private set; }

        public void Add(
            ActivityAggregationSample sample,
            double seconds,
            double inputRatio,
            long keyboardCount,
            long mouseClickCount,
            long scrollCount,
            int appSwitchCount)
        {
            _keyboardCount += keyboardCount;
            _mouseClickCount += mouseClickCount;
            _scrollCount += scrollCount;
            MouseDistance += sample.Input.MouseMoveDistance * inputRatio;
            _appSwitchCount += appSwitchCount;
            LongestContinuousWorkSeconds = Math.Max(
                LongestContinuousWorkSeconds,
                sample.LongestContinuousWorkSeconds);

            switch (sample.UserState)
            {
                case UserActivityState.Active:
                    ActiveSeconds += seconds;
                    break;
                case UserActivityState.Idle:
                    IdleSeconds += seconds;
                    break;
                default:
                    AfkSeconds += seconds;
                    break;
            }

            switch (sample.WorkCategory)
            {
                case WorkTimeCategory.Normal:
                    NormalWorkSeconds += seconds;
                    break;
                case WorkTimeCategory.Overtime:
                    OvertimeSeconds += seconds;
                    break;
                case WorkTimeCategory.Lab:
                    ManualWorkSeconds += seconds;
                    LabSeconds += seconds;
                    break;
                case WorkTimeCategory.Meeting:
                    ManualWorkSeconds += seconds;
                    MeetingSeconds += seconds;
                    break;
            }

            if (ApplicationProcessPolicy.ShouldPersistApplication(sample.ForegroundProcess))
            {
                var processName = sample.ForegroundProcess!;
                _processDurations.TryGetValue(processName, out var processSeconds);
                _processDurations[processName] = processSeconds + seconds;
            }

            ScheduleState = sample.ScheduleState;
            WorkMode = sample.WorkMode;
            _activityScoreWeightedSeconds += sample.ActivityScore * seconds;
            _activitySampleSeconds += seconds;
            if (sample.WorkCategory != WorkTimeCategory.None)
            {
                _workIntensityWeightedSeconds += sample.WorkIntensity * seconds;
                _workIntensitySampleSeconds += seconds;
            }
        }

        public ActivityBucket ToRecord()
        {
            var dominantProcess = _processDurations.Count == 0
                ? null
                : _processDurations.MaxBy(static pair => pair.Value).Key;
            return new ActivityBucket(
                BucketStart,
                BucketEnd,
                _keyboardCount,
                _mouseClickCount,
                MouseDistance,
                _scrollCount,
                ActiveSeconds,
                IdleSeconds,
                AfkSeconds,
                _appSwitchCount,
                dominantProcess,
                ScheduleState,
                WorkMode,
                NormalWorkSeconds,
                OvertimeSeconds,
                ManualWorkSeconds,
                LabSeconds,
                MeetingSeconds,
                _activitySampleSeconds <= 0 ? 0 : _activityScoreWeightedSeconds / _activitySampleSeconds,
                _workIntensitySampleSeconds <= 0 ? 0 : _workIntensityWeightedSeconds / _workIntensitySampleSeconds,
                LongestContinuousWorkSeconds);
        }
    }
}
