using WorkMate.Models;

namespace WorkMate.Services;

public sealed class ScheduleEngine : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly ScheduleSettingsStore _settingsStore;
    private readonly System.Threading.Timer _transitionTimer;
    private ScheduleSettings _settings = ScheduleDefaults.Create();
    private ScheduleSnapshot _lastSnapshot;
    private bool _disposed;

    public ScheduleEngine(ScheduleSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        _lastSnapshot = ScheduleEvaluator.Evaluate(_settings, DateTime.Now);
        _transitionTimer = new System.Threading.Timer(
            OnTransition,
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    public event EventHandler<ScheduleChangedEventArgs>? StateChanged;

    public event EventHandler? SettingsReloaded;

    public ScheduleSettings Settings
    {
        get
        {
            lock (_syncRoot)
            {
                return _settings;
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        Apply(settings);
    }

    public async Task UpdateSettingsAsync(ScheduleSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.TryValidate(out var error))
        {
            throw new ArgumentException(error, nameof(settings));
        }

        await _settingsStore.SaveAsync(settings, cancellationToken);
        Apply(settings);
    }

    public ScheduleSnapshot GetSnapshot(DateTime timestamp)
    {
        return ScheduleEvaluator.Evaluate(Settings, timestamp);
    }

    private void Apply(ScheduleSettings settings)
    {
        ScheduleSnapshot previous;
        ScheduleSnapshot current;
        lock (_syncRoot)
        {
            previous = _lastSnapshot;
            _settings = settings;
            current = ScheduleEvaluator.Evaluate(settings, DateTime.Now);
            _lastSnapshot = current;
            ScheduleNextTransition(current.Timestamp);
        }

        SettingsReloaded?.Invoke(this, EventArgs.Empty);
        if (previous.State != current.State || previous.ActiveProfile != current.ActiveProfile)
        {
            StateChanged?.Invoke(this, new ScheduleChangedEventArgs(previous, current));
        }
    }

    private void OnTransition(object? state)
    {
        ScheduleSnapshot previous;
        ScheduleSnapshot current;
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            previous = _lastSnapshot;
            current = ScheduleEvaluator.Evaluate(_settings, DateTime.Now);
            _lastSnapshot = current;
            ScheduleNextTransition(current.Timestamp);
        }

        if (previous.State != current.State || previous.ActiveProfile != current.ActiveProfile)
        {
            StateChanged?.Invoke(this, new ScheduleChangedEventArgs(previous, current));
        }
    }

    private void ScheduleNextTransition(DateTime now)
    {
        var candidates = new List<DateTime> { now.Date.AddDays(1) };
        for (var offset = 0; offset <= 7; offset++)
        {
            var date = DateOnly.FromDateTime(now.Date.AddDays(offset));
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var profileType = ScheduleEvaluator.ResolveProfile(_settings, date);
            var profile = profileType == ScheduleProfileType.Summer
                ? _settings.SummerProfile
                : _settings.WinterProfile;
            candidates.Add(date.ToDateTime(profile.MorningStart));
            candidates.Add(date.ToDateTime(profile.MorningEnd));
            candidates.Add(date.ToDateTime(profile.AfternoonStart));
            candidates.Add(date.ToDateTime(profile.AfternoonEnd));
        }

        var next = candidates.Where(candidate => candidate > now).Min();
        var dueTime = next - now;
        _transitionTimer.Change(
            dueTime < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : dueTime,
            Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _transitionTimer.Dispose();
        }
    }
}
