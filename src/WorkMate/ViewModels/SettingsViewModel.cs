using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using WorkMate.Infrastructure;
using WorkMate.Models;
using WorkMate.Services;

namespace WorkMate.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly StartupManager _startupManager;
    private readonly ScheduleEngine _scheduleEngine;
    private readonly ReminderEngine _reminderEngine;
    private readonly AutoOvertimeSuggestionService _autoOvertimeSuggestionService;
    private readonly SpeechService _speechService;
    private string _statusMessage = string.Empty;

    public SettingsViewModel(
        StartupManager startupManager,
        ScheduleEngine scheduleEngine,
        ReminderEngine reminderEngine,
        AutoOvertimeSuggestionService autoOvertimeSuggestionService,
        SpeechService speechService)
    {
        _startupManager = startupManager;
        _scheduleEngine = scheduleEngine;
        _reminderEngine = reminderEngine;
        _autoOvertimeSuggestionService = autoOvertimeSuggestionService;
        _speechService = speechService;
        RefreshReadOnlyData();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ScheduleSettings ScheduleSettings => _scheduleEngine.Settings;

    public CleaningReminderSettings CleaningSettings => _reminderEngine.CleaningSettings;

    public ReminderPresentationSettings ReminderSettings => _reminderEngine.PresentationSettings;

    public IReadOnlyList<SpeechVoiceInfo> AvailableVoices => _speechService.GetAvailableVoices();

    public string SpeechBackendName => _speechService.BackendName;

    public bool AutoStartEnabled => _startupManager.IsEnabled();

    public bool AutoOvertimePromptEnabled =>
        _autoOvertimeSuggestionService.Settings.AutoOvertimePromptEnabled;

    public string DatabasePath => AppPaths.DatabasePath;

    public string DatabaseSize { get; private set; } = "0 KB";

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public async Task SetAutoStartAsync(bool enabled, CancellationToken cancellationToken)
    {
        await _startupManager.SetEnabledAsync(enabled, cancellationToken);
        StatusMessage = enabled ? "已开启开机自动启动。" : "已关闭开机自动启动。";
        OnPropertyChanged(nameof(AutoStartEnabled));
    }

    public async Task SaveScheduleAsync(ScheduleSettings settings, CancellationToken cancellationToken)
    {
        await _scheduleEngine.UpdateSettingsAsync(settings, cancellationToken);
        StatusMessage = "作息时间已保存并立即生效。";
        OnPropertyChanged(nameof(ScheduleSettings));
    }

    public async Task SaveReminderAsync(
        CleaningReminderSettings settings,
        CancellationToken cancellationToken)
    {
        await _reminderEngine.UpdateCleaningSettingsAsync(settings, cancellationToken);
        StatusMessage = "提醒设置已保存并立即重新安排。";
        OnPropertyChanged(nameof(CleaningSettings));
    }

    public async Task SavePresentationSettingsAsync(
        ReminderPresentationSettings settings,
        CancellationToken cancellationToken)
    {
        await _reminderEngine.UpdatePresentationSettingsAsync(settings, cancellationToken);
        StatusMessage = "提醒和语音设置已保存并立即生效。";
        OnPropertyChanged(nameof(ReminderSettings));
    }

    public async Task SaveAutoOvertimePromptAsync(bool enabled, CancellationToken cancellationToken)
    {
        await _autoOvertimeSuggestionService.UpdateEnabledAsync(enabled, cancellationToken);
        OnPropertyChanged(nameof(AutoOvertimePromptEnabled));
    }

    public Task TestSpeechAsync(CancellationToken cancellationToken)
    {
        return _speechService.TestAsync(cancellationToken);
    }

    public void RefreshReadOnlyData()
    {
        var databaseFile = new FileInfo(AppPaths.DatabasePath);
        DatabaseSize = databaseFile.Exists
            ? FormatFileSize(databaseFile.Length)
            : "0 KB";
        OnPropertyChanged(nameof(DatabaseSize));
    }

    public void ReportError(string message)
    {
        StatusMessage = message;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024 * 1024)
        {
            return $"{Math.Max(1, bytes / 1024d):0.#} KB";
        }

        return $"{bytes / 1024d / 1024d:0.##} MB";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
