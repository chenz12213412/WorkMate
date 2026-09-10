using WorkMate.Models;

namespace WorkMate.Services;

public sealed class WorkModeService
{
    private ActivityMode _activityMode = ActivityMode.Computer;
    private bool _isOvertime;

    public event EventHandler? Changed;

    public ActivityMode ActivityMode => _activityMode;

    public bool IsOvertime => _isOvertime;

    public WorkScheduleState ResolveScheduleState(WorkScheduleState scheduledState)
    {
        return _isOvertime ? WorkScheduleState.Overtime : scheduledState;
    }

    public void SetActivityMode(ActivityMode mode)
    {
        if (_activityMode == mode)
        {
            return;
        }

        _activityMode = mode;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleOvertime()
    {
        _isOvertime = !_isOvertime;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
