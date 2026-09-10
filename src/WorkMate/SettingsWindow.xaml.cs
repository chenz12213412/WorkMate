using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WorkMate.Models;
using WorkMate.ViewModels;

namespace WorkMate;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private bool _isInitializing = true;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        AutoStartCheckBox.IsChecked = viewModel.AutoStartEnabled;
        LoadScheduleSettings(viewModel.ScheduleSettings);
        LoadReminderSettings(viewModel.CleaningSettings);
        LoadSpeechVoices();
        LoadPresentationSettings(viewModel.ReminderSettings);
        AutoOvertimePromptEnabledCheckBox.IsChecked = viewModel.AutoOvertimePromptEnabled;
        _isInitializing = false;
        ShowPage(SettingsPage.General);
    }

    protected override void OnActivated(EventArgs e)
    {
        _viewModel.RefreshReadOnlyData();
        base.OnActivated(e);
    }

    private void Navigation_OnChecked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || sender is not System.Windows.Controls.RadioButton { Tag: string pageName } ||
            !Enum.TryParse<SettingsPage>(pageName, out var page))
        {
            return;
        }

        ShowPage(page);
    }

    private async void AutoStartCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        AutoStartCheckBox.IsEnabled = false;
        try
        {
            await _viewModel.SetAutoStartAsync(
                AutoStartCheckBox.IsChecked == true,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _viewModel.ReportError($"开机启动设置保存失败：{exception.Message}");
            _isInitializing = true;
            AutoStartCheckBox.IsChecked = _viewModel.AutoStartEnabled;
            _isInitializing = false;
        }
        finally
        {
            AutoStartCheckBox.IsEnabled = true;
        }
    }

    private void ScheduleMode_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        UpdateScheduleModeControls();
    }

    private async void SaveScheduleButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadProfile(
                SummerMorningStartTextBox.Text,
                SummerMorningEndTextBox.Text,
                SummerAfternoonStartTextBox.Text,
                SummerAfternoonEndTextBox.Text,
                out var summerProfile) ||
            !TryReadProfile(
                WinterMorningStartTextBox.Text,
                WinterMorningEndTextBox.Text,
                WinterAfternoonStartTextBox.Text,
                WinterAfternoonEndTextBox.Text,
                out var winterProfile))
        {
            _viewModel.ReportError("时间格式无效，请使用 HH:mm，例如 08:30。");
            return;
        }

        if (!MonthDay.TryParse(AutomaticSummerStartTextBox.Text.Trim(), out var summerStart) ||
            !MonthDay.TryParse(AutomaticSummerEndTextBox.Text.Trim(), out var summerEnd))
        {
            _viewModel.ReportError("自动切换日期格式无效，请使用 MM-dd，例如 05-01。");
            return;
        }

        var settings = new ScheduleSettings(
            summerProfile,
            winterProfile,
            AutomaticModeRadioButton.IsChecked == true
                ? ScheduleSwitchMode.Automatic
                : ScheduleSwitchMode.Manual,
            ManualProfileComboBox.SelectedIndex == 0
                ? ScheduleProfileType.Summer
                : ScheduleProfileType.Winter,
            summerStart,
            summerEnd);

        if (!settings.TryValidate(out var validationError))
        {
            _viewModel.ReportError(validationError);
            return;
        }

        try
        {
            await _viewModel.SaveScheduleAsync(settings, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _viewModel.ReportError($"作息时间保存失败：{exception.Message}");
        }
    }

    private async void SaveReminderButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(
                CleaningReminderAdvanceComboBox.Text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var advanceMinutes))
        {
            _viewModel.ReportError("提前提醒分钟数必须是整数。");
            return;
        }

        var settings = new CleaningReminderSettings(
            CleaningReminderEnabledCheckBox.IsChecked == true,
            advanceMinutes);
        if (!settings.TryValidate(out var validationError))
        {
            _viewModel.ReportError(validationError);
            return;
        }

        if (!TryReadPresentationSettings(out var presentationSettings, out validationError))
        {
            _viewModel.ReportError(validationError);
            return;
        }

        try
        {
            await _viewModel.SaveReminderAsync(settings, CancellationToken.None);
            await _viewModel.SavePresentationSettingsAsync(
                presentationSettings,
                CancellationToken.None);
            await _viewModel.SaveAutoOvertimePromptAsync(
                AutoOvertimePromptEnabledCheckBox.IsChecked == true,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _viewModel.ReportError($"提醒设置保存失败：{exception.Message}");
        }
    }

    private async void SaveSpeechButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadPresentationSettings(out var settings, out var validationError))
        {
            _viewModel.ReportError(validationError);
            return;
        }

        try
        {
            await _viewModel.SavePresentationSettingsAsync(settings, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _viewModel.ReportError($"语音设置保存失败：{exception.Message}");
        }
    }

    private async void TestSpeechButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TryReadPresentationSettings(out var settings, out var validationError))
        {
            _viewModel.ReportError(validationError);
            return;
        }

        try
        {
            await _viewModel.SavePresentationSettingsAsync(settings, CancellationToken.None);
            await _viewModel.TestSpeechAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _viewModel.ReportError($"测试语音失败：{exception.Message}");
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LoadScheduleSettings(ScheduleSettings settings)
    {
        SummerMorningStartTextBox.Text = FormatTime(settings.SummerProfile.MorningStart);
        SummerMorningEndTextBox.Text = FormatTime(settings.SummerProfile.MorningEnd);
        SummerAfternoonStartTextBox.Text = FormatTime(settings.SummerProfile.AfternoonStart);
        SummerAfternoonEndTextBox.Text = FormatTime(settings.SummerProfile.AfternoonEnd);
        WinterMorningStartTextBox.Text = FormatTime(settings.WinterProfile.MorningStart);
        WinterMorningEndTextBox.Text = FormatTime(settings.WinterProfile.MorningEnd);
        WinterAfternoonStartTextBox.Text = FormatTime(settings.WinterProfile.AfternoonStart);
        WinterAfternoonEndTextBox.Text = FormatTime(settings.WinterProfile.AfternoonEnd);
        AutomaticSummerStartTextBox.Text = settings.AutomaticSummerStart.ToString();
        AutomaticSummerEndTextBox.Text = settings.AutomaticSummerEnd.ToString();
        AutomaticModeRadioButton.IsChecked = settings.SwitchMode == ScheduleSwitchMode.Automatic;
        ManualModeRadioButton.IsChecked = settings.SwitchMode == ScheduleSwitchMode.Manual;
        ManualProfileComboBox.SelectedIndex = settings.ManualProfile == ScheduleProfileType.Summer ? 0 : 1;
        UpdateScheduleModeControls();
    }

    private void LoadReminderSettings(CleaningReminderSettings settings)
    {
        CleaningReminderEnabledCheckBox.IsChecked = settings.Enabled;
        CleaningReminderAdvanceComboBox.Text = settings.AdvanceMinutes.ToString(CultureInfo.InvariantCulture);
    }

    private void LoadPresentationSettings(ReminderPresentationSettings settings)
    {
        GlobalSpeechEnabledCheckBox.IsChecked = settings.GlobalSpeechEnabled;
        SpeechVolumeSlider.Value = settings.SpeechVolume;
        SpeechRateSlider.Value = settings.SpeechRate;
        SpeechVoiceComboBox.SelectedItem = SpeechVoiceComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(
                item.Tag as string,
                settings.SpeechVoiceName,
                StringComparison.OrdinalIgnoreCase)) ?? SpeechVoiceComboBox.Items[0];
        ScheduleSpeechCheckBox.IsChecked = settings.ScheduleSpeechEnabled;
        CleaningSpeechCheckBox.IsChecked = settings.CleaningSpeechEnabled;
        DrinkReminderEnabledCheckBox.IsChecked = settings.DrinkEnabled;
        DrinkIntervalComboBox.Text = settings.DrinkIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        DrinkSpeechCheckBox.IsChecked = settings.DrinkSpeechEnabled;
        DrinkPopupCheckBox.IsChecked = settings.DrinkPopupEnabled;
        StandReminderEnabledCheckBox.IsChecked = settings.StandEnabled;
        StandContinuousComboBox.Text = settings.StandContinuousMinutes.ToString(CultureInfo.InvariantCulture);
        StandSpeechCheckBox.IsChecked = settings.StandSpeechEnabled;
        StandPopupCheckBox.IsChecked = settings.StandPopupEnabled;
        LunchSoonEnabledCheckBox.IsChecked = settings.LunchSoonEnabled;
        LunchSoonAdvanceComboBox.Text = settings.LunchSoonAdvanceMinutes.ToString(CultureInfo.InvariantCulture);
        LunchStartEnabledCheckBox.IsChecked = settings.LunchStartEnabled;
        AfternoonStartEnabledCheckBox.IsChecked = settings.AfternoonStartEnabled;
        OffWorkSpeechCheckBox.IsChecked = settings.OffWorkSpeechEnabled;
        OffWorkPopupCheckBox.IsChecked = settings.OffWorkPopupEnabled;
    }

    private bool TryReadPresentationSettings(
        out ReminderPresentationSettings settings,
        out string validationError)
    {
        if (!TryReadMinutes(DrinkIntervalComboBox.Text, "喝水提醒间隔", out var drinkMinutes) ||
            !TryReadMinutes(StandContinuousComboBox.Text, "站立提醒时长", out var standMinutes) ||
            !TryReadMinutes(LunchSoonAdvanceComboBox.Text, "午休提前提醒", out var lunchAdvance))
        {
            settings = ReminderPresentationSettings.Default;
            validationError = "提醒分钟数必须是整数。";
            return false;
        }

        settings = new ReminderPresentationSettings(
            GlobalSpeechEnabledCheckBox.IsChecked == true,
            (int)Math.Round(SpeechVolumeSlider.Value),
            (int)Math.Round(SpeechRateSlider.Value),
            DrinkReminderEnabledCheckBox.IsChecked == true,
            drinkMinutes,
            DrinkSpeechCheckBox.IsChecked == true,
            DrinkPopupCheckBox.IsChecked == true,
            StandReminderEnabledCheckBox.IsChecked == true,
            standMinutes,
            StandSpeechCheckBox.IsChecked == true,
            StandPopupCheckBox.IsChecked == true,
            LunchSoonEnabledCheckBox.IsChecked == true,
            lunchAdvance,
            LunchStartEnabledCheckBox.IsChecked == true,
            AfternoonStartEnabledCheckBox.IsChecked == true,
            OffWorkSpeechCheckBox.IsChecked == true,
            OffWorkPopupCheckBox.IsChecked == true)
        {
            SpeechVoiceName = (SpeechVoiceComboBox.SelectedItem as ComboBoxItem)?.Tag as string,
            ScheduleSpeechEnabled = ScheduleSpeechCheckBox.IsChecked == true,
            CleaningSpeechEnabled = CleaningSpeechCheckBox.IsChecked == true
        };
        return settings.TryValidate(out validationError);
    }

    private void LoadSpeechVoices()
    {
        SpeechVoiceComboBox.Items.Clear();
        SpeechVoiceComboBox.Items.Add(new ComboBoxItem
        {
            Content = "自动选择",
            Tag = null
        });
        foreach (var voice in _viewModel.AvailableVoices)
        {
            SpeechVoiceComboBox.Items.Add(new ComboBoxItem
            {
                Content = voice.DisplayName,
                Tag = voice.Name,
                ToolTip = $"{voice.Description} · {voice.Gender}"
            });
        }

        SpeechVoiceComboBox.SelectedIndex = 0;
    }

    private static bool TryReadMinutes(string text, string fieldName, out int value)
    {
        _ = fieldName;
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private void UpdateScheduleModeControls()
    {
        var automatic = AutomaticModeRadioButton.IsChecked == true;
        AutomaticSummerStartTextBox.IsEnabled = automatic;
        AutomaticSummerEndTextBox.IsEnabled = automatic;
        ManualProfileComboBox.IsEnabled = !automatic;
    }

    private void ShowPage(SettingsPage page)
    {
        GeneralPage.Visibility = page == SettingsPage.General ? Visibility.Visible : Visibility.Collapsed;
        SchedulePage.Visibility = page == SettingsPage.Schedule ? Visibility.Visible : Visibility.Collapsed;
        RemindersPage.Visibility = page == SettingsPage.Reminders ? Visibility.Visible : Visibility.Collapsed;
        SpeechPage.Visibility = page == SettingsPage.Speech ? Visibility.Visible : Visibility.Collapsed;
        AppearancePage.Visibility = page == SettingsPage.Appearance ? Visibility.Visible : Visibility.Collapsed;
        DataPage.Visibility = page == SettingsPage.Data ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == SettingsPage.About ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool TryReadProfile(
        string morningStart,
        string morningEnd,
        string afternoonStart,
        string afternoonEnd,
        out ScheduleProfile profile)
    {
        if (TryParseTime(morningStart, out var parsedMorningStart) &&
            TryParseTime(morningEnd, out var parsedMorningEnd) &&
            TryParseTime(afternoonStart, out var parsedAfternoonStart) &&
            TryParseTime(afternoonEnd, out var parsedAfternoonEnd))
        {
            profile = new ScheduleProfile(
                parsedMorningStart,
                parsedMorningEnd,
                parsedAfternoonStart,
                parsedAfternoonEnd);
            return true;
        }

        profile = null!;
        return false;
    }

    private static bool TryParseTime(string text, out TimeOnly value)
    {
        return TimeOnly.TryParseExact(
            text.Trim(),
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out value);
    }

    private static string FormatTime(TimeOnly value) => value.ToString("HH:mm", CultureInfo.InvariantCulture);

    private enum SettingsPage
    {
        General,
        Schedule,
        Reminders,
        Speech,
        Appearance,
        Data,
        About
    }
}
