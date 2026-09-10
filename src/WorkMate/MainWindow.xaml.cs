using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WorkMate.Infrastructure;
using WorkMate.ViewModels;

namespace WorkMate;

public partial class MainWindow : Window
{
    private readonly DashboardViewModel _viewModel;
    private readonly Action _showSettings;
    private readonly DispatcherTimer _refreshTimer;
    private bool _allowClose;
    private int _refreshCount;

    public MainWindow(DashboardViewModel viewModel, Action showSettings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _showSettings = showSettings;
        DataContext = viewModel;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += RefreshTimer_OnTick;
        IsVisibleChanged += MainWindow_OnIsVisibleChanged;
        _viewModel.RefreshRequested += ViewModel_OnRefreshRequested;
        _viewModel.Refresh(includeActivitySeries: true);
    }

    public void AllowClose() => _allowClose = true;

    public void ShowTransientStatus(string message)
    {
        _viewModel.ShowStatus(message);
    }

    public void FocusDashboard()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Focus());
    }

    public void ClearSettingsButtonFocus()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (SettingsButton.IsKeyboardFocused || SettingsButton.IsKeyboardFocusWithin)
            {
                Keyboard.ClearFocus();
            }

            FocusManager.SetFocusedElement(this, null);
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        _viewModel.RefreshRequested -= ViewModel_OnRefreshRequested;
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void MainWindow_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _viewModel.Refresh(includeActivitySeries: true);
            _refreshTimer.Start();
        }
        else
        {
            _refreshTimer.Stop();
        }
    }

    private void RefreshTimer_OnTick(object? sender, EventArgs e)
    {
        _refreshCount++;
        _viewModel.Refresh(includeActivitySeries: _refreshCount % 4 == 0);
    }

    private void ViewModel_OnRefreshRequested(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => _viewModel.Refresh(includeActivitySeries: false));
    }

    private void DrinkButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.RecordDrink();
    }

    private void StandButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.RecordStand();
    }

    private async void CleaningButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.MarkCleaningCompletedAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
            _viewModel.ShowStatus("打扫卫生状态保存失败，请稍后重试。");
        }
    }

    private void LunchButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ExplainLunchControl();
    }

    private void OvertimeButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleOvertime();
    }

    private void HideButton_OnClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(this, null);
        _showSettings();
    }
}
