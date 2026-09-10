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

        var changed = new List<ActivityBucket>();
        lock (_syncRoot)
        {
            var totalSeconds = (sample.End - sample.Start).TotalSeconds;
            var segmentStart = sample.Start;
            while (segmentStart < sample.End)
            {
                var bucketStart = FloorToBucket(segmentStart);
                var bucketEnd = bucketStart.Add(BucketDuration);
                var midnight = segmentStart.Date.AddDays(1);
                var segmentEnd = new[] { sample.End, bucketEnd, midnight }.Min();
                var segmentSeconds = (segmentEnd - segmentStart).TotalSeconds;
                var ratio = segmentSeconds / totalSeconds;

                if (!_buckets.TryGetValue(bucketStart, out var bucket))
                {
                    bucket = new MutableBucket(bucketStart, bucketEnd);
                    _buckets.Add(bucketStart, bucket);
                }

                bucket.Add(sample, segmentSeconds, ratio);
                changed.Add(bucket.ToRecord());
                segmentStart = segmentEnd;
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

    private sealed class MutableBucket
    {
        private readonly Dictionary<string, double> _processDurations = new(StringComparer.OrdinalIgnoreCase);
        private double _keyboardCount;
        private double _mouseClickCount;
        private double _scrollCount;
        private double _appSwitchCount;
        private double _activityScoreSeconds;
        private double _intensitySeconds;
        private double _sampleSeconds;

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
            _sampleSeconds = Math.Max(1, ActiveSeconds + IdleSeconds + AfkSeconds);
            _activityScoreSeconds = bucket.ActivityScore * _sampleSeconds;
            _intensitySeconds = bucket.WorkIntensity * _sampleSeconds;
            if (!string.IsNullOrWhiteSpace(bucket.DominantProcess))
            {
                _processDurations[bucket.DominantProcess] = _sampleSeconds;
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

        public void Add(ActivityAggregationSample sample, double seconds, double inputRatio)
        {
            _keyboardCount += sample.Input.KeyboardCount * inputRatio;
            _mouseClickCount += sample.Input.MouseClickCount * inputRatio;
            _scrollCount += sample.Input.WheelCount * inputRatio;
            MouseDistance += sample.Input.MouseMoveDistance * inputRatio;
            _appSwitchCount += sample.AppSwitchCount * inputRatio;
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

            if (!string.IsNullOrWhiteSpace(sample.ForegroundProcess))
            {
                _processDurations.TryGetValue(sample.ForegroundProcess, out var processSeconds);
                _processDurations[sample.ForegroundProcess] = processSeconds + seconds;
            }

            ScheduleState = sample.ScheduleState;
            WorkMode = sample.WorkMode;
            _activityScoreSeconds += sample.ActivityScore * seconds;
            _intensitySeconds += sample.WorkIntensity * seconds;
            _sampleSeconds += seconds;
        }

        public ActivityBucket ToRecord()
        {
            var dominantProcess = _processDurations.Count == 0
                ? null
                : _processDurations.MaxBy(static pair => pair.Value).Key;
            return new ActivityBucket(
                BucketStart,
                BucketEnd,
                (long)Math.Round(_keyboardCount),
                (long)Math.Round(_mouseClickCount),
                MouseDistance,
                (long)Math.Round(_scrollCount),
                ActiveSeconds,
                IdleSeconds,
                AfkSeconds,
                (int)Math.Round(_appSwitchCount),
                dominantProcess,
                ScheduleState,
                WorkMode,
                NormalWorkSeconds,
                OvertimeSeconds,
                ManualWorkSeconds,
                LabSeconds,
                MeetingSeconds,
                _sampleSeconds <= 0 ? 0 : _activityScoreSeconds / _sampleSeconds,
                _sampleSeconds <= 0 ? 0 : _intensitySeconds / _sampleSeconds,
                LongestContinuousWorkSeconds);
        }
    }
}
