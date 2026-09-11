using WorkMate.Models;

namespace WorkMate.Services;

public sealed record WorkModeSnapshot(ActivityMode ActivityMode, bool IsOvertime);

public sealed class WorkModeService
{
    private readonly object _syncRoot = new();
    private ActivityMode _activityMode = ActivityMode.Computer;
    private bool _isOvertime;

    public event EventHandler? Changed;

    public WorkModeSnapshot Snapshot
    {
        get
        {
            lock (_syncRoot)
            {
                return new WorkModeSnapshot(_activityMode, _isOvertime);
            }
        }
    }

    public ActivityMode ActivityMode => Snapshot.ActivityMode;

    public bool IsOvertime => Snapshot.IsOvertime;

    public WorkScheduleState ResolveScheduleState(WorkScheduleState scheduledState)
    {
        return Snapshot.IsOvertime ? WorkScheduleState.Overtime : scheduledState;
    }

    public void SetActivityMode(ActivityMode mode)
    {
        lock (_syncRoot)
        {
            if (_activityMode == mode)
            {
                return;
            }

            _activityMode = mode;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleOvertime()
    {
        lock (_syncRoot)
        {
            _isOvertime = !_isOvertime;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
