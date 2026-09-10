using WorkMate.Models;

namespace WorkMate.Services;

public static class ScheduleDefaults
{
    public static ScheduleSettings Create()
    {
        return new ScheduleSettings(
            SummerProfile: new ScheduleProfile(
                MorningStart: new TimeOnly(8, 15),
                MorningEnd: new TimeOnly(11, 15),
                AfternoonStart: new TimeOnly(12, 30),
                AfternoonEnd: new TimeOnly(17, 0)),
            WinterProfile: new ScheduleProfile(
                MorningStart: new TimeOnly(8, 15),
                MorningEnd: new TimeOnly(11, 15),
                AfternoonStart: new TimeOnly(12, 0),
                AfternoonEnd: new TimeOnly(16, 45)),
            SwitchMode: ScheduleSwitchMode.Automatic,
            ManualProfile: ScheduleProfileType.Summer,
            AutomaticSummerStart: new MonthDay(5, 1),
            AutomaticSummerEnd: new MonthDay(9, 30));
    }
}
