using WorkMate.Models;

namespace WorkMate.Services;

public static class ScheduleEvaluator
{
    public static ScheduleSnapshot Evaluate(ScheduleSettings settings, DateTime timestamp)
    {
        var activeProfile = ResolveProfile(settings, DateOnly.FromDateTime(timestamp));
        var schedule = activeProfile == ScheduleProfileType.Summer
            ? settings.SummerProfile
            : settings.WinterProfile;
        var state = ResolveState(schedule, timestamp);
        return new ScheduleSnapshot(timestamp, state, activeProfile, schedule);
    }

    public static ScheduleProfileType ResolveProfile(ScheduleSettings settings, DateOnly date)
    {
        if (settings.SwitchMode == ScheduleSwitchMode.Manual)
        {
            return settings.ManualProfile;
        }

        var current = new MonthDay(date.Month, date.Day).Ordinal;
        var start = settings.AutomaticSummerStart.Ordinal;
        var end = settings.AutomaticSummerEnd.Ordinal;
        var isSummer = start <= end
            ? current >= start && current <= end
            : current >= start || current <= end;
        return isSummer ? ScheduleProfileType.Summer : ScheduleProfileType.Winter;
    }

    public static WorkScheduleState ResolveState(ScheduleProfile schedule, DateTime timestamp)
    {
        if (timestamp.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return WorkScheduleState.OffWork;
        }

        var time = TimeOnly.FromDateTime(timestamp);
        if (time < schedule.MorningStart)
        {
            return WorkScheduleState.BeforeWork;
        }

        if (time < schedule.MorningEnd)
        {
            return WorkScheduleState.Working;
        }

        if (time < schedule.AfternoonStart)
        {
            return WorkScheduleState.Lunch;
        }

        return time < schedule.AfternoonEnd
            ? WorkScheduleState.Working
            : WorkScheduleState.OffWork;
    }

    public static TimeSpan GetCountableDuration(
        ScheduleSettings settings,
        DateTime intervalStart,
        DateTime intervalEnd)
    {
        if (intervalEnd <= intervalStart)
        {
            return TimeSpan.Zero;
        }

        var total = TimeSpan.Zero;
        var date = DateOnly.FromDateTime(intervalStart);
        var lastDate = DateOnly.FromDateTime(intervalEnd);

        while (date <= lastDate)
        {
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                var profileType = ResolveProfile(settings, date);
                var profile = profileType == ScheduleProfileType.Summer
                    ? settings.SummerProfile
                    : settings.WinterProfile;
                total += Intersect(intervalStart, intervalEnd, date, profile.MorningStart, profile.MorningEnd);
                total += Intersect(intervalStart, intervalEnd, date, profile.AfternoonStart, profile.AfternoonEnd);
            }

            date = date.AddDays(1);
        }

        return total;
    }

    private static TimeSpan Intersect(
        DateTime intervalStart,
        DateTime intervalEnd,
        DateOnly date,
        TimeOnly periodStart,
        TimeOnly periodEnd)
    {
        var start = date.ToDateTime(periodStart);
        var end = date.ToDateTime(periodEnd);
        var effectiveStart = intervalStart > start ? intervalStart : start;
        var effectiveEnd = intervalEnd < end ? intervalEnd : end;
        return effectiveEnd > effectiveStart ? effectiveEnd - effectiveStart : TimeSpan.Zero;
    }
}
