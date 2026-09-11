namespace WorkMate.Models;

public enum ReminderPresentationKind
{
    Drink,
    Stand,
    LunchSoon,
    LunchStart,
    AfternoonStart,
    OffWork,
    OvertimeSuggestion,
    Cleaning
}

public enum ReminderPriority
{
    Drink = 10,
    Stand = 20,
    Cleaning = 30,
    ScheduleCritical = 40
}

public enum ReminderAction
{
    Dismissed,
    Acknowledge,
    Completed,
    Snoozed,
    Skipped,
    StartOvertime,
    Preempted
}

public sealed record ReminderActionOption(
    string Label,
    ReminderAction Action,
    bool IsPrimary = false);

public sealed record ReminderPresentationRequest(
    string HistoryType,
    ReminderPresentationKind Kind,
    ReminderPriority Priority,
    DateTimeOffset TriggeredAt,
    DateTimeOffset ExpiresAt,
    string Title,
    string Message,
    string? SpeechText,
    bool ShowPopup,
    bool PlaySpeech,
    IReadOnlyList<ReminderActionOption> Actions,
    Func<ReminderAction, CancellationToken, Task>? HandleActionAsync = null,
    Func<DateTimeOffset, CancellationToken, Task>? MarkShownAsync = null);

public sealed record ReminderPresentationSettings(
    bool GlobalSpeechEnabled,
    int SpeechVolume,
    int SpeechRate,
    bool DrinkEnabled,
    int DrinkIntervalMinutes,
    bool DrinkSpeechEnabled,
    bool DrinkPopupEnabled,
    bool StandEnabled,
    int StandContinuousMinutes,
    bool StandSpeechEnabled,
    bool StandPopupEnabled,
    bool LunchSoonEnabled,
    int LunchSoonAdvanceMinutes,
    bool LunchStartEnabled,
    bool AfternoonStartEnabled,
    bool OffWorkSpeechEnabled,
    bool OffWorkPopupEnabled)
{
    public string? SpeechVoiceName { get; init; }

    public bool ScheduleSpeechEnabled { get; init; } = true;

    public bool CleaningSpeechEnabled { get; init; } = true;

    public static ReminderPresentationSettings Default { get; } = new(
        GlobalSpeechEnabled: true,
        SpeechVolume: 80,
        SpeechRate: 0,
        DrinkEnabled: true,
        DrinkIntervalMinutes: 45,
        DrinkSpeechEnabled: true,
        DrinkPopupEnabled: true,
        StandEnabled: true,
        StandContinuousMinutes: 50,
        StandSpeechEnabled: true,
        StandPopupEnabled: true,
        LunchSoonEnabled: true,
        LunchSoonAdvanceMinutes: 5,
        LunchStartEnabled: true,
        AfternoonStartEnabled: true,
        OffWorkSpeechEnabled: true,
        OffWorkPopupEnabled: true);

    public bool TryValidate(out string error)
    {
        if (SpeechVolume is < 0 or > 100)
        {
            error = "语音音量必须在 0～100 之间。";
            return false;
        }

        if (SpeechRate is < -1 or > 1)
        {
            error = "语音语速设置无效。";
            return false;
        }

        if (SpeechVoiceName?.Length > 200)
        {
            error = "语音名称过长。";
            return false;
        }

        if (DrinkIntervalMinutes is < 5 or > 240)
        {
            error = "喝水提醒间隔必须在 5～240 分钟之间。";
            return false;
        }

        if (StandContinuousMinutes is < 5 or > 240)
        {
            error = "站立提醒时长必须在 5～240 分钟之间。";
            return false;
        }

        if (LunchSoonAdvanceMinutes is not (5 or 10 or 15))
        {
            error = "午休前提醒仅支持提前 5、10 或 15 分钟。";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
