using WorkMate.Models;

namespace WorkMate.Services;

public static class IconStateResolver
{
    public static IconAssetKey Resolve(IconVisualState state)
    {
        if (state.ActiveReminder is { } reminder)
        {
            return reminder switch
            {
                ReminderIconType.Drink => IconAssetKey.DrinkReminder,
                ReminderIconType.Stand => IconAssetKey.StandReminder,
                ReminderIconType.Cleaning => IconAssetKey.Cleaning,
                _ => IconAssetKey.Default
            };
        }

        if (state.ScheduleState == WorkScheduleState.Overtime)
        {
            return IconAssetKey.Overtime;
        }

        if (state.ActivityMode != ActivityMode.Computer)
        {
            return state.ActivityMode switch
            {
                ActivityMode.Lab => IconAssetKey.LabMode,
                ActivityMode.Meeting => IconAssetKey.MeetingMode,
                _ => IconAssetKey.Default
            };
        }

        return state.ScheduleState switch
        {
            WorkScheduleState.BeforeWork => IconAssetKey.BeforeWork,
            WorkScheduleState.Working => IconAssetKey.Working,
            WorkScheduleState.Lunch => IconAssetKey.Lunch,
            WorkScheduleState.OffWork => IconAssetKey.OffWork,
            WorkScheduleState.Overtime => IconAssetKey.Overtime,
            _ => IconAssetKey.Default
        };
    }
}
