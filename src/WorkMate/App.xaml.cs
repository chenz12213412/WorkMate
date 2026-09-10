using System.Windows;
using System.Windows.Threading;
using WorkMate.Infrastructure;
using WorkMate.Services;
using WorkMate.ViewModels;

namespace WorkMate;

public partial class App : System.Windows.Application
{
    private readonly CancellationTokenSource _shutdownTokenSource = new();
    private SingleInstanceCoordinator? _singleInstance;
    private TrayIconService? _trayIcon;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private StartupManager? _startupManager;
    private ScheduleEngine? _scheduleEngine;
    private ReminderEngine? _reminderEngine;
    private SpeechService? _speechService;
    private IReminderPresentationService? _reminderPresentationService;
    private IIconService? _iconService;
    private WorkModeService? _workModeService;
    private IActivitySnapshotService? _activitySnapshotService;
    private SystemActivityStateMonitor? _systemActivityStateMonitor;
    private AutoOvertimeSuggestionService? _autoOvertimeSuggestionService;
    private DailySummaryService? _dailySummaryService;
    private NotificationService? _notificationService;
    private string? _pendingStatusMessage;
    private bool _isExiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var isStartupLaunch = e.Args.Any(static argument =>
            string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase));

        _singleInstance = SingleInstanceCoordinator.Create();
        if (!_singleInstance.IsPrimaryInstance)
        {
            if (!isStartupLaunch)
            {
                _singleInstance.RequestDashboard();
            }

            _singleInstance.Dispose();
            Shutdown();
            return;
        }

        try
        {
            var database = new DatabaseStore(AppPaths.DatabasePath);
            await database.InitializeAsync(_shutdownTokenSource.Token);

            _startupManager = new StartupManager(database);
            await _startupManager.ApplyFirstRunDefaultAsync(_shutdownTokenSource.Token);

            _scheduleEngine = new ScheduleEngine(new ScheduleSettingsStore(database));
            await _scheduleEngine.InitializeAsync(_shutdownTokenSource.Token);
            _workModeService = new WorkModeService();
            _activitySnapshotService = new ActivitySnapshotService(
                database,
                _scheduleEngine,
                _workModeService);
            await _activitySnapshotService.InitializeAsync(_shutdownTokenSource.Token);
            _speechService = new SpeechService();
            _iconService = new IconService();
            _notificationService = new NotificationService();
            _reminderPresentationService = new ReminderPresentationService(
                Dispatcher,
                _speechService,
                _iconService);
            _systemActivityStateMonitor = new SystemActivityStateMonitor(
                _activitySnapshotService,
                _reminderPresentationService.SetSystemAvailable);
            _reminderEngine = new ReminderEngine(
                _scheduleEngine,
                new CleaningReminderSettingsStore(database),
                new ReminderPresentationSettingsStore(database),
                database,
                _reminderPresentationService,
                _workModeService,
                _activitySnapshotService);
            await _reminderEngine.InitializeAsync(_shutdownTokenSource.Token);
            var activityTrackingSettingsStore = new ActivityTrackingSettingsStore(database);
            _autoOvertimeSuggestionService = new AutoOvertimeSuggestionService(
                _activitySnapshotService,
                _scheduleEngine,
                _workModeService,
                database,
                activityTrackingSettingsStore);
            await _autoOvertimeSuggestionService.InitializeAsync(_shutdownTokenSource.Token);
            _autoOvertimeSuggestionService.SuggestionDue += AutoOvertimeSuggestionService_OnSuggestionDue;
            _dailySummaryService = new DailySummaryService(
                database,
                _activitySnapshotService,
                _scheduleEngine,
                _workModeService);
            await _dailySummaryService.InitializeAsync(_shutdownTokenSource.Token);
            _dailySummaryService.SummaryGenerated += DailySummaryService_OnSummaryGenerated;
            _trayIcon = new TrayIconService(
                _iconService,
                _scheduleEngine,
                _workModeService,
                ShowDashboard,
                ShowSettings,
                ShowTransientStatus,
                () => _reminderEngine.MarkDrinkCompleted(),
                () => _reminderEngine.MarkStandCompleted(),
                () => _ = MarkCleaningCompletedFromTrayAsync(),
                paused => _reminderEngine.SetAllRemindersPaused(paused),
                ExitApplication);
            if (_reminderPresentationService is ReminderPresentationService reminderPresentation)
            {
                reminderPresentation.ReminderStarted += _trayIcon.BeginReminder;
                reminderPresentation.ReminderEnded += _trayIcon.EndReminder;
            }
            _notificationService.Attach(
                (reminderType, title, message) => Dispatcher.BeginInvoke(() =>
                    _trayIcon?.ShowNotification(reminderType, title, message)),
                (title, message) => Dispatcher.BeginInvoke(() =>
                    _trayIcon?.ShowInformation(title, message)));
            _singleInstance.StartDashboardListener(Dispatcher, ShowDashboard, _shutdownTokenSource.Token);

            if (isStartupLaunch)
            {
                var greetingService = new GreetingService(database, _scheduleEngine, _speechService);
                _ = greetingService.RunStartupGreetingAsync(_shutdownTokenSource.Token);
            }
            else
            {
                ShowDashboard();
            }
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
            System.Windows.MessageBox.Show(
                "WorkMate 启动失败，错误详情已写入本地日志。",
                "WorkMate",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ExitApplication();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _activitySnapshotService?.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
            _dailySummaryService?.GenerateAsync(
                DateOnly.FromDateTime(DateTime.Now),
                CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
        }

        _shutdownTokenSource.Cancel();
        _trayIcon?.Dispose();
        _reminderEngine?.Dispose();
        _reminderPresentationService?.Dispose();
        if (_autoOvertimeSuggestionService is not null)
        {
            _autoOvertimeSuggestionService.SuggestionDue -= AutoOvertimeSuggestionService_OnSuggestionDue;
            _autoOvertimeSuggestionService.Dispose();
        }

        if (_dailySummaryService is not null)
        {
            _dailySummaryService.SummaryGenerated -= DailySummaryService_OnSummaryGenerated;
            _dailySummaryService.Dispose();
        }

        _systemActivityStateMonitor?.Dispose();
        _activitySnapshotService?.Dispose();
        _speechService?.Dispose();
        _scheduleEngine?.Dispose();
        _iconService?.Dispose();
        _singleInstance?.Dispose();
        _shutdownTokenSource.Dispose();
        base.OnExit(e);
    }

    private void ShowDashboard()
    {
        ShowMainWindow();
    }

    private void ShowSettings()
    {
        if (_startupManager is null || _scheduleEngine is null || _reminderEngine is null ||
            _autoOvertimeSuggestionService is null || _speechService is null)
        {
            return;
        }

        if (_settingsWindow is null)
        {
            var settingsViewModel = new SettingsViewModel(
                _startupManager,
                _scheduleEngine,
                _reminderEngine,
                _autoOvertimeSuggestionService,
                _speechService);
            _settingsWindow = new SettingsWindow(settingsViewModel);
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                _mainWindow?.ClearSettingsButtonFocus();
            };
        }

        _settingsWindow.Show();
        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Activate();
        _settingsWindow.Topmost = true;
        _settingsWindow.Topmost = false;
        _settingsWindow.Focus();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null &&
            _scheduleEngine is not null &&
            _reminderEngine is not null &&
            _activitySnapshotService is not null &&
            _workModeService is not null &&
            _iconService is not null)
        {
            var dashboardViewModel = new DashboardViewModel(
                _scheduleEngine,
                _reminderEngine,
                _workModeService,
                _activitySnapshotService,
                _iconService);
            _mainWindow = new MainWindow(dashboardViewModel, ShowSettings);
            if (!string.IsNullOrWhiteSpace(_pendingStatusMessage))
            {
                _mainWindow.ShowTransientStatus(_pendingStatusMessage);
                _pendingStatusMessage = null;
            }
        }

        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Focus();
        _mainWindow.FocusDashboard();
    }

    private void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _shutdownTokenSource.Cancel();
        _trayIcon?.Dispose();
        _settingsWindow?.Close();
        _mainWindow?.AllowClose();
        _mainWindow?.Close();
        Shutdown();
    }

    private void ShowTransientStatus(string message)
    {
        if (_mainWindow is null)
        {
            _pendingStatusMessage = message;
            return;
        }

        _mainWindow.ShowTransientStatus(message);
    }

    private async Task MarkCleaningCompletedFromTrayAsync()
    {
        if (_reminderEngine is null)
        {
            return;
        }

        try
        {
            await _reminderEngine.MarkCleaningCompletedAsync(_shutdownTokenSource.Token);
            ShowTransientStatus("已记录：今天的工位整理完成。");
        }
        catch (OperationCanceledException) when (_shutdownTokenSource.IsCancellationRequested)
        {
            // 正常退出。
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
        }
    }

    private void AutoOvertimeSuggestionService_OnSuggestionDue(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_reminderPresentationService is null || _autoOvertimeSuggestionService is null)
            {
                return;
            }

            var now = DateTimeOffset.Now;
            var request = new Models.ReminderPresentationRequest(
                AutoOvertimeSuggestionService.ReminderType,
                Models.ReminderPresentationKind.OvertimeSuggestion,
                Models.ReminderPriority.ScheduleCritical,
                now,
                now.AddMinutes(10),
                "还在工作吗？",
                "检测到下班后仍在持续工作，要进入加班模式吗？",
                null,
                true,
                false,
                [
                    new("进入加班", Models.ReminderAction.StartOvertime, true),
                    new("今天忽略", Models.ReminderAction.Skipped)
                ],
                _autoOvertimeSuggestionService.HandleActionAsync,
                _autoOvertimeSuggestionService.RecordShownAsync);
            _ = _reminderPresentationService.EnqueueAsync(request, _shutdownTokenSource.Token);
        });
    }

    private void DailySummaryService_OnSummaryGenerated(object? sender, Models.DailySummary summary)
    {
        var message = $"今日工作 {FormatSummaryDuration(summary.TotalWorkDuration)} · " +
                      $"加班 {FormatSummaryDuration(summary.OvertimeDuration)} · " +
                      $"强度 {summary.AverageWorkIntensity}";
        _notificationService?.ShowInformation("WorkMate · 今日摘要", message);
    }

    private static string FormatSummaryDuration(TimeSpan duration)
    {
        var totalHours = (int)Math.Max(0, duration.TotalHours);
        return totalHours > 0 ? $"{totalHours}h{duration.Minutes}m" : $"{Math.Max(0, duration.Minutes)}m";
    }
}
