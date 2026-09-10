namespace WorkMate.Models;

public enum ActivityMode
{
    Computer,
    Lab,
    Meeting
}

public enum ReminderIconType
{
    Drink,
    Stand,
    Cleaning
}

public enum IconAssetKey
{
    Default,
    BeforeWork,
    Working,
    Lunch,
    OffWork,
    Overtime,
    DrinkReminder,
    StandReminder,
    Cleaning,
    LabMode,
    MeetingMode
}

public readonly record struct IconVisualState(
    WorkScheduleState ScheduleState,
    ActivityMode ActivityMode,
    ReminderIconType? ActiveReminder = null);
