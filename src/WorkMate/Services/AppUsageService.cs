using WorkMate.Models;

namespace WorkMate.Services;

public sealed class AppUsageService
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<(DateOnly Date, string Process), MutableUsage> _usage = new();

    public void Add(
        DateOnly date,
        string? processName,
        TimeSpan foregroundDuration,
        TimeSpan activeForegroundDuration)
    {
        if (string.IsNullOrWhiteSpace(processName) || foregroundDuration <= TimeSpan.Zero)
        {
            return;
        }

        var normalizedName = processName.Trim();
        lock (_syncRoot)
        {
            var key = (date, normalizedName);
            if (!_usage.TryGetValue(key, out var usage))
            {
                usage = new MutableUsage();
                _usage.Add(key, usage);
            }

            usage.ForegroundSeconds += foregroundDuration.TotalSeconds;
            usage.ActiveForegroundSeconds += Math.Min(
                foregroundDuration.TotalSeconds,
                Math.Max(0, activeForegroundDuration.TotalSeconds));
        }
    }

    public IReadOnlyList<AppUsageEntry> GetForDate(DateOnly date)
    {
        lock (_syncRoot)
        {
            return _usage
                .Where(pair => pair.Key.Date == date)
                .Select(pair => new AppUsageEntry(
                    pair.Key.Date,
                    pair.Key.Process,
                    TimeSpan.FromSeconds(pair.Value.ForegroundSeconds),
                    TimeSpan.FromSeconds(pair.Value.ActiveForegroundSeconds)))
                .OrderByDescending(static entry => entry.ActiveForegroundDuration)
                .ToArray();
        }
    }

    public void Load(IEnumerable<AppUsageEntry> entries)
    {
        lock (_syncRoot)
        {
            foreach (var entry in entries)
            {
                _usage[(entry.Date, entry.ProcessName)] = new MutableUsage
                {
                    ForegroundSeconds = entry.ForegroundDuration.TotalSeconds,
                    ActiveForegroundSeconds = entry.ActiveForegroundDuration.TotalSeconds
                };
            }
        }
    }

    public void RemoveBefore(DateOnly date)
    {
        lock (_syncRoot)
        {
            var oldKeys = _usage.Keys.Where(key => key.Date < date).ToArray();
            foreach (var key in oldKeys)
            {
                _usage.Remove(key);
            }
        }
    }

    private sealed class MutableUsage
    {
        public double ForegroundSeconds { get; set; }

        public double ActiveForegroundSeconds { get; set; }
    }
}
