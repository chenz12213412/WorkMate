using WorkMate.Models;

namespace WorkMate.Services;

public static class WorkSliceClassifier
{
    public static WorkTimeCategory Classify(WorkClassificationContext context)
    {
        if (!context.SystemAvailable)
        {
            return WorkTimeCategory.None;
        }

        if (context.ActivityMode == ActivityMode.Lab)
        {
            return WorkTimeCategory.Lab;
        }

        if (context.ActivityMode == ActivityMode.Meeting)
        {
            return WorkTimeCategory.Meeting;
        }

        if (context.UserState != UserActivityState.Active)
        {
            return WorkTimeCategory.None;
        }

        if (context.IsOvertime)
        {
            return WorkTimeCategory.Overtime;
        }

        return context.ScheduleState == WorkScheduleState.Working
            ? WorkTimeCategory.Normal
            : WorkTimeCategory.None;
    }
}

public sealed class WorkSessionTracker
{
    private WorkTimeCategory _sessionCategory;
    private TimeSpan _continuousDuration;
    private TimeSpan _longestContinuousDuration;

    public WorkSessionSnapshot Advance(
        TimeSpan elapsed,
        WorkClassificationContext context)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return GetSnapshot();
        }

        var workCategory = WorkSliceClassifier.Classify(context);
        var sessionCategory = ResolveSessionCategory(context, workCategory);
        if (sessionCategory == WorkTimeCategory.None)
        {
            Reset();
            return GetSnapshot();
        }

        if (_sessionCategory != WorkTimeCategory.None && _sessionCategory != sessionCategory)
        {
            _continuousDuration = TimeSpan.Zero;
        }

        _sessionCategory = sessionCategory;
        _continuousDuration += elapsed;
        if (_continuousDuration > _longestContinuousDuration)
        {
            _longestContinuousDuration = _continuousDuration;
        }

        return GetSnapshot();
    }

    public void Reset()
    {
        _sessionCategory = WorkTimeCategory.None;
        _continuousDuration = TimeSpan.Zero;
    }

    public void ResetDay()
    {
        Reset();
        _longestContinuousDuration = TimeSpan.Zero;
    }

    public WorkSessionSnapshot GetSnapshot() => new(
        _sessionCategory,
        _continuousDuration,
        _longestContinuousDuration);

    private static WorkTimeCategory ResolveSessionCategory(
        WorkClassificationContext context,
        WorkTimeCategory workCategory)
    {
        if (!context.SystemAvailable)
        {
            return WorkTimeCategory.None;
        }

        if (workCategory is WorkTimeCategory.Lab or WorkTimeCategory.Meeting)
        {
            return workCategory;
        }

        if (context.UserState == UserActivityState.Afk)
        {
            return WorkTimeCategory.None;
        }

        if (context.IsOvertime)
        {
            return WorkTimeCategory.Overtime;
        }

        return context.ScheduleState == WorkScheduleState.Working
            ? WorkTimeCategory.Normal
            : WorkTimeCategory.None;
    }
}
