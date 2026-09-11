using WorkMate.Models;

namespace WorkMate.Services;

public static class ApplicationProcessPolicy
{
    public static bool IsWorkMateProcess(string? processName) =>
        string.Equals(processName?.Trim(), "WorkMate", StringComparison.OrdinalIgnoreCase);

    public static bool ShouldPersistApplication(string? processName) =>
        !string.IsNullOrWhiteSpace(processName) && !IsWorkMateProcess(processName);

    public static string Normalize(string processName) => processName.Trim().ToUpperInvariant();
}

public sealed class ExternalAppSwitchTracker
{
    private string? _lastExternalProcess;

    public bool Observe(string? processName, bool countSwitch = true)
    {
        if (!ApplicationProcessPolicy.ShouldPersistApplication(processName))
        {
            return false;
        }

        var normalizedProcess = ApplicationProcessPolicy.Normalize(processName!);
        var changed = countSwitch &&
                      _lastExternalProcess is not null &&
                      !string.Equals(_lastExternalProcess, normalizedProcess, StringComparison.Ordinal);
        _lastExternalProcess = normalizedProcess;
        return changed;
    }
}

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
        if (!ApplicationProcessPolicy.ShouldPersistApplication(processName) ||
            foregroundDuration <= TimeSpan.Zero)
        {
            return;
        }

        var normalizedName = processName!.Trim();
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
                .Where(pair => pair.Key.Date == date &&
                               ApplicationProcessPolicy.ShouldPersistApplication(pair.Key.Process))
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
                if (!ApplicationProcessPolicy.ShouldPersistApplication(entry.ProcessName))
                {
                    continue;
                }

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
