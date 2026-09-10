using System.Globalization;

namespace WorkMate.Models;

public enum WorkScheduleState
{
    BeforeWork,
    Working,
    Lunch,
    OffWork,
    Overtime
}

public enum ScheduleProfileType
{
    Summer,
    Winter
}

public enum ScheduleSwitchMode
{
    Automatic,
    Manual
}

public readonly record struct MonthDay(int Month, int Day)
{
    public int Ordinal => (Month * 100) + Day;

    public static bool TryParse(string text, out MonthDay value)
    {
        if (DateOnly.TryParseExact(
                $"2000-{text}",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            value = new MonthDay(date.Month, date.Day);
            return true;
        }

        value = default;
        return false;
    }

    public override string ToString() => $"{Month:00}-{Day:00}";
}

public sealed record ScheduleProfile(
    TimeOnly MorningStart,
    TimeOnly MorningEnd,
    TimeOnly AfternoonStart,
    TimeOnly AfternoonEnd)
{
    public bool TryValidate(out string error)
    {
        if (MorningStart >= MorningEnd)
        {
            error = "上午上班时间必须早于上午下班时间。";
            return false;
        }

        if (MorningEnd > AfternoonStart)
        {
            error = "上午下班时间不能晚于下午上班时间。";
            return false;
        }

        if (AfternoonStart >= AfternoonEnd)
        {
            error = "下午上班时间必须早于下午下班时间。";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed record ScheduleSettings(
    ScheduleProfile SummerProfile,
    ScheduleProfile WinterProfile,
    ScheduleSwitchMode SwitchMode,
    ScheduleProfileType ManualProfile,
    MonthDay AutomaticSummerStart,
    MonthDay AutomaticSummerEnd)
{
    public bool TryValidate(out string error)
    {
        if (!SummerProfile.TryValidate(out error))
        {
            error = $"夏令时 Profile：{error}";
            return false;
        }

        if (!WinterProfile.TryValidate(out error))
        {
            error = $"冬令时 Profile：{error}";
            return false;
        }

        if (AutomaticSummerStart.Month == 0 || AutomaticSummerEnd.Month == 0)
        {
            error = "自动切换日期必须使用 MM-dd 格式。";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed record ScheduleSnapshot(
    DateTime Timestamp,
    WorkScheduleState State,
    ScheduleProfileType ActiveProfile,
    ScheduleProfile Schedule);

public sealed class ScheduleChangedEventArgs : EventArgs
{
    public ScheduleChangedEventArgs(ScheduleSnapshot previous, ScheduleSnapshot current)
    {
        Previous = previous;
        Current = current;
    }

    public ScheduleSnapshot Previous { get; }

    public ScheduleSnapshot Current { get; }
}
