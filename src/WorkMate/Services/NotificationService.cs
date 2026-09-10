namespace WorkMate.Services;

using WorkMate.Models;

public sealed class NotificationService
{
    private Action<ReminderIconType, string, string>? _showNotification;
    private Action<string, string>? _showInformation;

    public void Attach(
        Action<ReminderIconType, string, string> showNotification,
        Action<string, string>? showInformation = null)
    {
        _showNotification = showNotification;
        _showInformation = showInformation;
    }

    public void Show(ReminderIconType reminderType, string title, string message)
    {
        _showNotification?.Invoke(reminderType, title, message);
    }

    public void ShowInformation(string title, string message)
    {
        _showInformation?.Invoke(title, message);
    }
}
