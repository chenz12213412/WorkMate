using Microsoft.Win32;
using WorkMate.Infrastructure;

namespace WorkMate.Services;

public sealed class SystemActivityStateMonitor : IDisposable
{
    private readonly IActivitySnapshotService _activitySnapshotService;
    private readonly Action<bool>? _availabilityChanged;
    private bool _sessionLocked;
    private bool _suspended;
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
    }

    private void SystemEvents_OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        try
        {
            if (e.Reason == SessionSwitchReason.SessionLock)
            {
                _sessionLocked = true;
            }
            else if (e.Reason == SessionSwitchReason.SessionUnlock)
            {
                _sessionLocked = false;
            }

            UpdateAvailability();
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
                _suspended = true;
            }
            else if (e.Mode == PowerModes.Resume)
            {
                _suspended = false;
            }

            UpdateAvailability();
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
        }
    }

    private void UpdateAvailability()
    {
        var available = !_sessionLocked && !_suspended;
        _activitySnapshotService.SetSystemAvailable(available);
        _availabilityChanged?.Invoke(available);
    }
}
