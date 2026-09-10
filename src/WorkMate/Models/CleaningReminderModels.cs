namespace WorkMate.Models;

public sealed record CleaningReminderSettings(bool Enabled, int AdvanceMinutes)
{
    public static CleaningReminderSettings Default { get; } = new(true, 30);

    public bool TryValidate(out string error)
    {
        if (AdvanceMinutes is < 1 or > 240)
        {
            error = "打扫卫生提醒的提前分钟数必须在 1～240 之间。";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public enum ReminderHistoryStatus
{
    Triggered,
    Snoozed,
    Completed,
    Skipped,
    Dismissed,
    Expired
}

public sealed record ReminderHistoryEntry(
    string ReminderType,
    DateOnly ReminderDate,
    ReminderHistoryStatus Status,
    int SnoozeCount,
    DateTimeOffset? NextDueAt,
    string? Message,
    DateTimeOffset? ShownAt = null,
    string? Action = null);

