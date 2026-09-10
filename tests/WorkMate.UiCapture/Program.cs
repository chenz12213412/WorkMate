using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using WorkMate;
using WorkMate.Models;
using WorkMate.Services;
using WorkMate.ViewModels;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        RunAsync(args).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(string[] args)
    {
var outputDirectory = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "ui-captures"));
Directory.CreateDirectory(outputDirectory);

var application = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
application.InitializeComponent();

var temporaryDirectory = Path.Combine(
    Path.GetTempPath(),
    "WorkMate.UiCapture",
    Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporaryDirectory);

try
{
    var database = new DatabaseStore(Path.Combine(temporaryDirectory, "capture.db"));
    await database.InitializeAsync(CancellationToken.None);
    using var scheduleEngine = new ScheduleEngine(new ScheduleSettingsStore(database));
    await scheduleEngine.InitializeAsync(CancellationToken.None);
    using var speechService = new SpeechService();
    using var iconService = new IconService();
    using var presentationService = new ReminderPresentationService(
        application.Dispatcher,
        speechService,
        iconService);
    var workModeService = new WorkModeService();
    using var reminderEngine = new ReminderEngine(
        scheduleEngine,
        new CleaningReminderSettingsStore(database),
        new ReminderPresentationSettingsStore(database),
        database,
        presentationService,
        workModeService);
    await reminderEngine.InitializeAsync(CancellationToken.None);
    VerifyDynamicIcons(iconService);
    var dashboardViewModel = new DashboardViewModel(
        scheduleEngine,
        reminderEngine,
        workModeService,
        new EmptyActivitySeriesProvider(),
        iconService);

    var settingsOpenCount = 0;
    var dashboard = new MainWindow(dashboardViewModel, () => settingsOpenCount++);
    CaptureWindow(dashboard, Path.Combine(outputDirectory, "WorkMate-Dashboard-100.png"), 1.0);
    CaptureWindow(dashboard, Path.Combine(outputDirectory, "WorkMate-Dashboard-125.png"), 1.25);
    CaptureWindow(dashboard, Path.Combine(outputDirectory, "WorkMate-Dashboard-150.png"), 1.5);
    VerifySettingsGearInteraction(dashboard, () => settingsOpenCount);
    CaptureWindow(dashboard, Path.Combine(outputDirectory, "WorkMate-Dashboard-Gear-Normal.png"), 1.0);
    dashboard.AllowClose();
    dashboard.Close();

    using var captureActivity = new CaptureActivitySnapshotService();
    var activitySettingsStore = new ActivityTrackingSettingsStore(database);
    using var overtimeSuggestions = new AutoOvertimeSuggestionService(
        captureActivity,
        scheduleEngine,
        workModeService,
        database,
        activitySettingsStore);
    await overtimeSuggestions.InitializeAsync(CancellationToken.None);
    var settingsViewModel = new SettingsViewModel(
        new StartupManager(database),
        scheduleEngine,
        reminderEngine,
        overtimeSuggestions,
        speechService);
    var settings = new SettingsWindow(settingsViewModel);
    VerifyScheduleModeControls(settings);
    CaptureWindow(settings, Path.Combine(outputDirectory, "WorkMate-Settings-100.png"), 1.0);
    CaptureWindow(settings, Path.Combine(outputDirectory, "WorkMate-Settings-125.png"), 1.25);
    CaptureWindow(settings, Path.Combine(outputDirectory, "WorkMate-Settings-150.png"), 1.5);
    SelectSettingsPage(settings, "Schedule");
    CaptureWindow(settings, Path.Combine(outputDirectory, "WorkMate-Settings-Schedule.png"), 1.0);
    SelectSettingsPage(settings, "Reminders");
    CaptureWindow(settings, Path.Combine(outputDirectory, "WorkMate-Settings-Reminders.png"), 1.0);
    SelectSettingsPage(settings, "Speech");
    CaptureWindow(settings, Path.Combine(outputDirectory, "WorkMate-Settings-Speech.png"), 1.0);
    settings.Close();

    CaptureReminderPopup(
        iconService,
        ReminderPresentationKind.Drink,
        "喝口水吧",
        "已经工作一阵子了，补充一点水分。",
        [new("我喝了", ReminderAction.Completed, true), new("10 分钟后提醒", ReminderAction.Snoozed)],
        Path.Combine(outputDirectory, "WorkMate-Drink-Popup.png"));
    CaptureReminderPopup(
        iconService,
        ReminderPresentationKind.Stand,
        "起来活动一下",
        "已经连续工作 52 分钟了。",
        [new("我起来了", ReminderAction.Completed, true), new("10 分钟后提醒", ReminderAction.Snoozed)],
        Path.Combine(outputDirectory, "WorkMate-Stand-Popup.png"));
    CaptureReminderPopup(
        iconService,
        ReminderPresentationKind.LunchStart,
        "午休时间到",
        "休息一下，下午再继续。",
        [new("开始午休", ReminderAction.Acknowledge, true)],
        Path.Combine(outputDirectory, "WorkMate-Lunch-Popup.png"));
}
finally
{
    SqliteConnection.ClearAllPools();
    if (Directory.Exists(temporaryDirectory))
    {
        Directory.Delete(temporaryDirectory, recursive: true);
    }
}

    }

private static void VerifyDynamicIcons(IIconService iconService)
{
    IconVisualState[] states =
    [
        new(WorkScheduleState.BeforeWork, ActivityMode.Computer),
        new(WorkScheduleState.Working, ActivityMode.Computer),
        new(WorkScheduleState.Lunch, ActivityMode.Computer),
        new(WorkScheduleState.OffWork, ActivityMode.Computer),
        new(WorkScheduleState.Overtime, ActivityMode.Computer),
        new(WorkScheduleState.Working, ActivityMode.Lab),
        new(WorkScheduleState.Working, ActivityMode.Meeting)
    ];

    foreach (var state in states)
    {
        if (iconService.GetStatusIcon(state, 96) is not BitmapSource { PixelWidth: 96 })
        {
            throw new InvalidOperationException($"Status icon failed to decode: {state}");
        }

        var trayIcon = iconService.GetTrayIcon(state);
        if (trayIcon.Width is not (16 or 20 or 24 or 32))
        {
            throw new InvalidOperationException($"Tray icon has an invalid size: {trayIcon.Width}");
        }
    }

    foreach (var reminderType in Enum.GetValues<ReminderIconType>())
    {
        if (iconService.GetReminderIcon(reminderType, 64) is not BitmapSource { PixelWidth: 64 })
        {
            throw new InvalidOperationException($"Reminder icon failed to decode: {reminderType}");
        }
    }
}

private static void CaptureReminderPopup(
    IIconService iconService,
    ReminderPresentationKind kind,
    string title,
    string message,
    IReadOnlyList<ReminderActionOption> actions,
    string outputPath)
{
    var now = DateTimeOffset.Now;
    var request = new ReminderPresentationRequest(
        $"Capture.{kind}",
        kind,
        ReminderPriority.ScheduleCritical,
        now,
        now.AddMinutes(1),
        title,
        message,
        null,
        true,
        false,
        actions);
    var popup = new ReminderPopupWindow(request, iconService);
    CaptureWindow(popup, outputPath, 1.0);
    popup.CloseForPreemption();
}

private static void SelectSettingsPage(DependencyObject root, string tag)
{
    for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
    {
        var child = VisualTreeHelper.GetChild(root, index);
        if (child is System.Windows.Controls.RadioButton { Tag: string pageTag } button &&
            string.Equals(pageTag, tag, StringComparison.Ordinal))
        {
            button.IsChecked = true;
            return;
        }

        SelectSettingsPage(child, tag);
    }
}

private static void VerifyScheduleModeControls(SettingsWindow settings)
{
    var automatic = (System.Windows.Controls.RadioButton)settings.FindName("AutomaticModeRadioButton");
    var manual = (System.Windows.Controls.RadioButton)settings.FindName("ManualModeRadioButton");
    var profile = (System.Windows.Controls.ComboBox)settings.FindName("ManualProfileComboBox");
    if (automatic.IsChecked != true || profile.IsEnabled)
    {
        throw new InvalidOperationException("Automatic mode must be enabled and manual profile disabled by default.");
    }

    manual.IsChecked = true;
    if (!profile.IsEnabled)
    {
        throw new InvalidOperationException("Manual profile must become selectable when automatic switching is disabled.");
    }

    automatic.IsChecked = true;
    if (profile.IsEnabled)
    {
        throw new InvalidOperationException("Manual profile must be disabled again when automatic switching is enabled.");
    }
}

private static void VerifySettingsGearInteraction(MainWindow dashboard, Func<int> getOpenCount)
{
    dashboard.Show();
    dashboard.Activate();
    dashboard.UpdateLayout();
    var button = (System.Windows.Controls.Button)dashboard.FindName("SettingsButton");
    if (button.FocusVisualStyle is not null)
    {
        throw new InvalidOperationException("The settings gear must not use the default focus rectangle.");
    }

    _ = Keyboard.Focus(button);
    button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    dashboard.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    if (button.IsKeyboardFocused || button.IsKeyboardFocusWithin)
    {
        throw new InvalidOperationException("The settings gear retained keyboard focus after cleanup.");
    }

    _ = Keyboard.Focus(button);
    button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    dashboard.ClearSettingsButtonFocus();
    dashboard.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    if (getOpenCount() != 2)
    {
        throw new InvalidOperationException("The settings gear did not remain clickable after focus cleanup.");
    }

    dashboard.Hide();
}

private static void CaptureWindow(Window window, string outputPath, double scale)
{
    window.Show();
    window.UpdateLayout();

    var visual = (FrameworkElement)window.Content;
    var pixelWidth = (int)Math.Ceiling(visual.ActualWidth * scale);
    var pixelHeight = (int)Math.Ceiling(visual.ActualHeight * scale);
    var bitmap = new RenderTargetBitmap(
        pixelWidth,
        pixelHeight,
        96,
        96,
        PixelFormats.Pbgra32);
    var drawing = new DrawingVisual();
    using (var context = drawing.RenderOpen())
    {
        context.DrawRectangle(window.Background, null, new Rect(0, 0, pixelWidth, pixelHeight));
        context.PushTransform(new ScaleTransform(scale, scale));
        context.DrawRectangle(
            new VisualBrush(visual),
            null,
            new Rect(0, 0, visual.ActualWidth, visual.ActualHeight));
        context.Pop();
    }

    bitmap.Render(drawing);

    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = File.Create(outputPath);
    encoder.Save(stream);
    window.Hide();
}

private sealed class CaptureActivitySnapshotService : IActivitySnapshotService
{
    private readonly EmptyActivitySeriesProvider _empty = new();

    public event EventHandler<ActivityDashboardSnapshot>? SnapshotUpdated
    {
        add { }
        remove { }
    }

    public ActivityDashboardSnapshot Current => _empty.GetSnapshot(DateTime.Now);

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void SetSystemAvailable(bool available)
    {
        _ = available;
    }

    public ActivityDashboardSnapshot GetSnapshot(DateTime now) => _empty.GetSnapshot(now);

    public void Dispose()
    {
    }
}
}
