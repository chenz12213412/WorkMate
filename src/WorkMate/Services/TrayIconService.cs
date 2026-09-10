using System.Windows.Forms;
using System.Windows.Threading;
using WorkMate.Models;

namespace WorkMate.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly IIconService _iconService;
    private readonly ScheduleEngine _scheduleEngine;
    private readonly WorkModeService _workModeService;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _reminderIconTimer;
    private readonly DispatcherTimer _pauseTimer;
    private readonly Action<bool> _setAllRemindersPaused;
    private readonly Action<string> _updateStatus;
    private readonly ToolStripMenuItem _overtimeMenuItem;
    private readonly ToolStripMenuItem _labModeMenuItem;
    private readonly ToolStripMenuItem _meetingModeMenuItem;
    private WorkScheduleState _scheduleState;
    private ReminderIconType? _activeReminder;

    public TrayIconService(
        IIconService iconService,
        ScheduleEngine scheduleEngine,
        WorkModeService workModeService,
        Action showDashboard,
        Action showSettings,
        Action<string> updateStatus,
        Action recordDrink,
        Action recordStand,
        Action markCleaningCompleted,
        Action<bool> setAllRemindersPaused,
        Action exit)
    {
        _iconService = iconService;
        _scheduleEngine = scheduleEngine;
        _workModeService = workModeService;
        _setAllRemindersPaused = setAllRemindersPaused;
        _updateStatus = updateStatus;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _scheduleState = scheduleEngine.GetSnapshot(DateTime.Now).State;
        _reminderIconTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _reminderIconTimer.Tick += ReminderIconTimer_OnTick;
        _pauseTimer = new DispatcherTimer();
        _pauseTimer.Tick += PauseTimer_OnTick;

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("打开今日状态", null, (_, _) => showDashboard());
        contextMenu.Items.Add("设置...", null, (_, _) => showSettings());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("喝水 ✓", null, (_, _) =>
        {
            recordDrink();
            updateStatus("已记录：刚刚喝过水。");
            ShowReminderIconTemporarily(ReminderIconType.Drink);
        });
        contextMenu.Items.Add("站立一下", null, (_, _) =>
        {
            recordStand();
            updateStatus("很好，起来活动一下吧。");
            ShowReminderIconTemporarily(ReminderIconType.Stand);
        });
        contextMenu.Items.Add("打扫卫生 ✓", null, (_, _) => markCleaningCompleted());
        contextMenu.Items.Add(new ToolStripSeparator());

        _overtimeMenuItem = new ToolStripMenuItem();
        _overtimeMenuItem.Click += (_, _) =>
        {
            _workModeService.ToggleOvertime();
            _updateStatus(_workModeService.IsOvertime ? "已开始加班。" : "已结束加班。");
        };
        _labModeMenuItem = new ToolStripMenuItem("实验台模式");
        _meetingModeMenuItem = new ToolStripMenuItem("会议模式");
        _labModeMenuItem.Click += (_, _) => ToggleActivityMode(ActivityMode.Lab, "实验台模式");
        _meetingModeMenuItem.Click += (_, _) => ToggleActivityMode(ActivityMode.Meeting, "会议模式");
        contextMenu.Items.Add(_overtimeMenuItem);
        contextMenu.Items.Add(_labModeMenuItem);
        contextMenu.Items.Add(_meetingModeMenuItem);

        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("暂停提醒 30 分钟", null, (_, _) => PauseReminders(TimeSpan.FromMinutes(30)));
        contextMenu.Items.Add("暂停提醒 1 小时", null, (_, _) => PauseReminders(TimeSpan.FromHours(1)));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("退出 WorkMate", null, (_, _) => exit());

        _notifyIcon = new NotifyIcon
        {
            Text = "WorkMate",
            Icon = _iconService.GetAppIcon(),
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                showDashboard();
            }
        };

        _scheduleEngine.StateChanged += ScheduleEngine_OnStateChanged;
        _scheduleEngine.SettingsReloaded += ScheduleEngine_OnSettingsReloaded;
        _workModeService.Changed += WorkModeService_OnChanged;
        RefreshModeMenu();
        UpdateIcon();
    }

    public void ShowNotification(ReminderIconType reminderType, string title, string message)
    {
        ShowReminderIconTemporarily(reminderType);
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.None;
        _notifyIcon.ShowBalloonTip(10000);
    }

    public void ShowInformation(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(10000);
    }

    public void BeginReminder(ReminderIconType reminderType)
    {
        _reminderIconTimer.Stop();
        _activeReminder = reminderType;
        UpdateIcon();
    }

    public void EndReminder()
    {
        _reminderIconTimer.Stop();
        _activeReminder = null;
        UpdateIcon();
    }

    public void Dispose()
    {
        _scheduleEngine.StateChanged -= ScheduleEngine_OnStateChanged;
        _scheduleEngine.SettingsReloaded -= ScheduleEngine_OnSettingsReloaded;
        _workModeService.Changed -= WorkModeService_OnChanged;
        _reminderIconTimer.Stop();
        _pauseTimer.Stop();
        _setAllRemindersPaused(false);
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }

    private void ShowReminderIconTemporarily(ReminderIconType reminderType)
    {
        BeginReminder(reminderType);
        _reminderIconTimer.Start();
    }

    private void ReminderIconTimer_OnTick(object? sender, EventArgs e)
    {
        EndReminder();
    }

    private void ScheduleEngine_OnStateChanged(object? sender, ScheduleChangedEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            _scheduleState = e.Current.State;
            UpdateIcon();
        });
    }

    private void ScheduleEngine_OnSettingsReloaded(object? sender, EventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            _scheduleState = _scheduleEngine.GetSnapshot(DateTime.Now).State;
            UpdateIcon();
        });
    }

    private void WorkModeService_OnChanged(object? sender, EventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            RefreshModeMenu();
            UpdateIcon();
        });
    }

    private void ToggleActivityMode(ActivityMode requestedMode, string displayName)
    {
        var nextMode = _workModeService.ActivityMode == requestedMode
            ? ActivityMode.Computer
            : requestedMode;
        _workModeService.SetActivityMode(nextMode);
        _updateStatus(nextMode == ActivityMode.Computer
            ? "已返回电脑工作模式。"
            : $"已切换为：{displayName}。");
    }

    private void PauseReminders(TimeSpan duration)
    {
        _pauseTimer.Stop();
        _pauseTimer.Interval = duration;
        _setAllRemindersPaused(true);
        _pauseTimer.Start();
        _updateStatus(duration.TotalMinutes == 30
            ? "提醒已暂停 30 分钟。"
            : "提醒已暂停 1 小时。");
    }

    private void PauseTimer_OnTick(object? sender, EventArgs e)
    {
        _pauseTimer.Stop();
        _setAllRemindersPaused(false);
        _updateStatus("提醒已恢复。");
    }

    private void RefreshModeMenu()
    {
        _overtimeMenuItem.Text = _workModeService.IsOvertime ? "结束加班" : "开始加班";
        _overtimeMenuItem.Checked = _workModeService.IsOvertime;
        _labModeMenuItem.Checked = _workModeService.ActivityMode == ActivityMode.Lab;
        _meetingModeMenuItem.Checked = _workModeService.ActivityMode == ActivityMode.Meeting;
    }

    private void UpdateIcon()
    {
        var effectiveScheduleState = _workModeService.ResolveScheduleState(_scheduleState);
        var state = new IconVisualState(
            effectiveScheduleState,
            _workModeService.ActivityMode,
            _activeReminder);
        _notifyIcon.Icon = _iconService.GetTrayIcon(state);
    }
}
