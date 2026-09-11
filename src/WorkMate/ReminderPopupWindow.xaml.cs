using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WorkMate.Models;
using WorkMate.Services;
using Forms = System.Windows.Forms;

namespace WorkMate;

public partial class ReminderPopupWindow : Window
{
    private readonly ReminderPresentationRequest _request;
    private readonly System.Windows.Threading.DispatcherTimer _dismissTimer;
    private bool _completed;

    public ReminderPopupWindow(ReminderPresentationRequest request, IIconService iconService)
    {
        InitializeComponent();
        _request = request;
        TitleText.Text = request.Title;
        MessageText.Text = request.Message;
        ReminderImage.Source = ResolveIcon(iconService, request.Kind);
        BuildActions(request.Actions);
        _dismissTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = request.Actions.Count == 0
                ? TimeSpan.FromSeconds(8)
                : TimeSpan.FromSeconds(30)
        };
        _dismissTimer.Tick += (_, _) => Complete(ReminderAction.Dismissed);
        SourceInitialized += ReminderPopupWindow_OnSourceInitialized;
        Loaded += ReminderPopupWindow_OnLoaded;
        Closing += ReminderPopupWindow_OnClosing;
    }

    public event EventHandler<ReminderAction>? ActionSelected;

    public void CloseForPreemption()
    {
        Complete(ReminderAction.Preempted);
    }

    private static System.Windows.Media.ImageSource ResolveIcon(
        IIconService iconService,
        ReminderPresentationKind kind)
    {
        return kind switch
        {
            ReminderPresentationKind.Drink => iconService.GetReminderIcon(ReminderIconType.Drink, 64),
            ReminderPresentationKind.Stand => iconService.GetReminderIcon(ReminderIconType.Stand, 64),
            ReminderPresentationKind.Cleaning => iconService.GetReminderIcon(ReminderIconType.Cleaning, 64),
            ReminderPresentationKind.LunchSoon or ReminderPresentationKind.LunchStart =>
                iconService.GetStatusIcon(new IconVisualState(WorkScheduleState.Lunch, ActivityMode.Computer), 64),
            ReminderPresentationKind.AfternoonStart =>
                iconService.GetStatusIcon(new IconVisualState(WorkScheduleState.Working, ActivityMode.Computer), 64),
            ReminderPresentationKind.OffWork or ReminderPresentationKind.OvertimeSuggestion =>
                iconService.GetStatusIcon(new IconVisualState(WorkScheduleState.OffWork, ActivityMode.Computer), 64),
            _ => iconService.GetStatusIcon(new IconVisualState(WorkScheduleState.Working, ActivityMode.Computer), 64)
        };
    }

    private void BuildActions(IReadOnlyList<ReminderActionOption> actions)
    {
        foreach (var option in actions)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = option.Label,
                MinWidth = 74,
                Margin = new Thickness(ActionPanel.Children.Count == 0 ? 0 : 8, 0, 0, 0),
                Style = (Style)FindResource("ActionButtonStyle"),
                IsDefault = option.IsPrimary,
                Tag = option.Action
            };
            button.Click += ActionButton_OnClick;
            AutomationProperties.SetName(button, option.Label);
            ActionPanel.Children.Add(button);
        }
    }

    private void ReminderPopupWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(extendedStyle | WsExToolWindow | WsExNoActivate));
        PositionInCurrentWorkArea();
    }

    private void ReminderPopupWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        _dismissTimer.Start();
    }

    private void ReminderPopupWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        _dismissTimer.Stop();
        if (!_completed)
        {
            _completed = true;
            ActionSelected?.Invoke(this, ReminderAction.Dismissed);
        }
    }

    private void ActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: ReminderAction action })
        {
            Complete(action);
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Complete(ReminderAction.Dismissed);
    }

    private void Complete(ReminderAction action)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _dismissTimer.Stop();
        ActionSelected?.Invoke(this, action);
        Close();
    }

    private void PositionInCurrentWorkArea()
    {
        var screen = Forms.Screen.FromPoint(Forms.Control.MousePosition);
        var dpi = VisualTreeHelper.GetDpi(this);
        var workArea = screen.WorkingArea;
        Left = (workArea.Right / dpi.DpiScaleX) - Width - 12;
        Top = (workArea.Bottom / dpi.DpiScaleY) - Height - 12;
    }

    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, index) : new IntPtr(GetWindowLong32(hWnd, index));

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newStyle) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, index, newStyle)
            : new IntPtr(SetWindowLong32(hWnd, index, newStyle.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int index, int newStyle);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr newStyle);
}
