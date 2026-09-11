using Microsoft.Win32;
using WorkMate.Infrastructure;

namespace WorkMate.Services;

public sealed class SystemAvailabilityState
{
    private readonly object _syncRoot = new();
    private bool _sessionLocked;
    private bool _suspended;

    public bool SetSessionLocked(bool locked)
    {
        lock (_syncRoot)
        {
            _sessionLocked = locked;
            return !_sessionLocked && !_suspended;
        }
    }

    public bool SetSuspended(bool suspended)
    {
        lock (_syncRoot)
        {
            _suspended = suspended;
            return !_sessionLocked && !_suspended;
        }
    }
}

public sealed class SystemActivityStateMonitor : IDisposable
{
    private readonly IActivitySnapshotService _activitySnapshotService;
    private readonly Action<bool>? _availabilityChanged;
    private readonly SystemAvailabilityState _state = new();
    private readonly object _transitionSync = new();
    private Task _pendingTransition = Task.CompletedTask;
    private bool _disposed;

    public SystemActivityStateMonitor(
        IActivitySnapshotService activitySnapshotService,
        Action<bool>? availabilityChanged = null)
    {
        _activitySnapshotService = activitySnapshotService;
        _availabilityChanged = availabilityChanged;
        SystemEvents.SessionSwitch += SystemEvents_OnSessionSwitch;
        SystemEvents.PowerModeChanged += SystemEvents_OnPowerModeChanged;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.SessionSwitch -= SystemEvents_OnSessionSwitch;
        SystemEvents.PowerModeChanged -= SystemEvents_OnPowerModeChanged;
        Task pending;
        lock (_transitionSync)
        {
            pending = _pendingTransition;
        }

        try
        {
            pending.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
        }
    }

    private void SystemEvents_OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        try
        {
            if (e.Reason == SessionSwitchReason.SessionLock)
            {
                QueueAvailability(_state.SetSessionLocked(true));
            }
            else if (e.Reason == SessionSwitchReason.SessionUnlock)
            {
                QueueAvailability(_state.SetSessionLocked(false));
            }
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
        }
    }

    private void SystemEvents_OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        try
        {
            if (e.Mode == PowerModes.Suspend)
            {
                QueueAvailability(_state.SetSuspended(true));
            }
            else if (e.Mode == PowerModes.Resume)
            {
                QueueAvailability(_state.SetSuspended(false));
            }
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
        }
    }

    private void QueueAvailability(bool available)
    {
        lock (_transitionSync)
        {
            _pendingTransition = _pendingTransition
                .ContinueWith(
                    _ => ApplyAvailabilityAsync(available),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default)
                .Unwrap();
        }
    }

    private async Task ApplyAvailabilityAsync(bool available)
    {
        try
        {
            await _activitySnapshotService.SetSystemAvailableAsync(available);
            _availabilityChanged?.Invoke(available);
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
        }
    }
}
