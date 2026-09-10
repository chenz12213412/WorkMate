using WorkMate.Models;

namespace WorkMate.Services;

public sealed class WorkDurationCalculator
{
    private readonly ScheduleEngine _scheduleEngine;

    public WorkDurationCalculator(ScheduleEngine scheduleEngine)
    {
        _scheduleEngine = scheduleEngine;
    }

    public bool CanAccumulate(DateTime timestamp)
    {
        return _scheduleEngine.GetSnapshot(timestamp).State == WorkScheduleState.Working;
    }

    public TimeSpan GetCountableDuration(DateTime activeIntervalStart, DateTime activeIntervalEnd)
    {
        return ScheduleEvaluator.GetCountableDuration(
            _scheduleEngine.Settings,
            activeIntervalStart,
            activeIntervalEnd);
    }
}
