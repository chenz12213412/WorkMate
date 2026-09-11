using Microsoft.Data.Sqlite;
using WorkMate.Models;
using WorkMate.Services;

var defaults = ScheduleDefaults.Create();
AssertEqual(
    SpeechTextNormalizer.Normalize("17:00 50min 2h 49m"),
    "下午五点 五十分钟 两小时四十九分钟。",
    "TTS time and duration values should be converted to natural Chinese speech.");
AssertEqual(
    SpeechTextNormalizer.Normalize(" 上午辛苦啦。。  午休时间到了 "),
    "上午辛苦啦。午休时间到了。",
    "TTS text should collapse spaces and repeated punctuation.");
AssertEqual(
    SpeechTextNormalizer.ToChineseNumber(52),
    "五十二",
    "Chinese duration numbers should be spoken naturally.");
var speechTemplateProvider = new SpeechTemplateProvider();
var speechTemplateRequest = new ReminderPresentationRequest(
    "Speech.Template.Test",
    ReminderPresentationKind.Drink,
    ReminderPriority.Drink,
    DateTimeOffset.Now,
    DateTimeOffset.Now.AddMinutes(1),
    "喝口水吧",
    "已经工作一阵子了，补充一点水分。",
    "喝水提醒。",
    false,
    true,
    []);
var firstSpeechTemplate = speechTemplateProvider.Resolve(speechTemplateRequest);
var secondSpeechTemplate = speechTemplateProvider.Resolve(speechTemplateRequest);
if (string.Equals(firstSpeechTemplate, secondSpeechTemplate, StringComparison.Ordinal))
{
    throw new InvalidOperationException("Speech templates must not repeat immediately.");
}
AssertEqual(ReminderPresentationSettings.Default.SpeechVolume, 80, "New speech settings should default to 80% volume.");
AssertEqual(ReminderPresentationSettings.Default.SpeechRate, 0, "New speech settings should default to natural rate.");
AssertEqual(ReminderPresentationSettings.Default.SpeechVoiceName, null, "Voice selection should default to automatic.");
AssertState(defaults, new DateTime(2026, 9, 8, 8, 14, 0), WorkScheduleState.BeforeWork);
AssertState(defaults, new DateTime(2026, 9, 8, 8, 15, 0), WorkScheduleState.Working);
AssertState(defaults, new DateTime(2026, 9, 8, 11, 15, 0), WorkScheduleState.Lunch);
AssertState(defaults, new DateTime(2026, 9, 8, 12, 30, 0), WorkScheduleState.Working);
AssertState(defaults, new DateTime(2026, 9, 8, 17, 0, 0), WorkScheduleState.OffWork);
AssertState(defaults, new DateTime(2026, 9, 12, 10, 0, 0), WorkScheduleState.OffWork);

AssertEqual(
    ScheduleEvaluator.ResolveProfile(defaults, new DateOnly(2026, 9, 8)),
    ScheduleProfileType.Summer,
    "September should use the summer profile.");
AssertEqual(
    ScheduleEvaluator.ResolveProfile(defaults, new DateOnly(2026, 12, 8)),
    ScheduleProfileType.Winter,
    "December should use the winter profile.");
AssertEqual(defaults.SwitchMode, ScheduleSwitchMode.Automatic, "Automatic profile switching must be the default.");
AssertEqual(defaults.ManualProfile, ScheduleProfileType.Summer, "The default manual profile must be summer.");
AssertEqual(defaults.SummerProfile.MorningStart, new TimeOnly(8, 15), "Summer morning start default is incorrect.");
AssertEqual(defaults.SummerProfile.MorningEnd, new TimeOnly(11, 15), "Summer morning end default is incorrect.");
AssertEqual(defaults.SummerProfile.AfternoonStart, new TimeOnly(12, 30), "Summer afternoon start default is incorrect.");
AssertEqual(defaults.SummerProfile.AfternoonEnd, new TimeOnly(17, 0), "Summer afternoon end default is incorrect.");
AssertEqual(defaults.WinterProfile.MorningStart, new TimeOnly(8, 15), "Winter morning start default is incorrect.");
AssertEqual(defaults.WinterProfile.MorningEnd, new TimeOnly(11, 15), "Winter morning end default is incorrect.");
AssertEqual(defaults.WinterProfile.AfternoonStart, new TimeOnly(12, 0), "Winter afternoon start default is incorrect.");
AssertEqual(defaults.WinterProfile.AfternoonEnd, new TimeOnly(16, 45), "Winter afternoon end default is incorrect.");
AssertEqual(
    ScheduleEvaluator.ResolveProfile(defaults, new DateOnly(2026, 4, 30)),
    ScheduleProfileType.Winter,
    "April 30 should use the winter profile.");
AssertEqual(
    ScheduleEvaluator.ResolveProfile(defaults, new DateOnly(2026, 5, 1)),
    ScheduleProfileType.Summer,
    "May 1 should start the summer profile.");
AssertEqual(
    ScheduleEvaluator.ResolveProfile(defaults, new DateOnly(2026, 9, 30)),
    ScheduleProfileType.Summer,
    "September 30 should remain in the summer profile.");
AssertEqual(
    ScheduleEvaluator.ResolveProfile(defaults, new DateOnly(2026, 10, 1)),
    ScheduleProfileType.Winter,
    "October 1 should start the cross-year winter period.");

var countable = ScheduleEvaluator.GetCountableDuration(
    defaults,
    new DateTime(2026, 9, 8, 7, 0, 0),
    new DateTime(2026, 9, 8, 19, 0, 0));
AssertEqual(countable, TimeSpan.FromHours(7.5), "Lunch and off-work time must be excluded.");

AssertEqual(
    IconStateResolver.Resolve(
        new IconVisualState(WorkScheduleState.Working, ActivityMode.Computer)),
    IconAssetKey.Working,
    "The normal working state should use the working icon.");
AssertEqual(
    IconStateResolver.Resolve(
        new IconVisualState(WorkScheduleState.Lunch, ActivityMode.Lab)),
    IconAssetKey.LabMode,
    "A manual lab mode should override the normal schedule icon.");
AssertEqual(
    IconStateResolver.Resolve(
        new IconVisualState(WorkScheduleState.Overtime, ActivityMode.Lab)),
    IconAssetKey.Overtime,
    "Overtime should override a manual activity mode.");
AssertEqual(
    IconStateResolver.Resolve(
        new IconVisualState(
            WorkScheduleState.Overtime,
            ActivityMode.Lab,
            ReminderIconType.Cleaning)),
    IconAssetKey.Cleaning,
    "An active reminder should have the highest icon priority.");
AssertEqual(
    IconStateResolver.Resolve(
        new IconVisualState(WorkScheduleState.BeforeWork, ActivityMode.Computer)),
    IconAssetKey.BeforeWork,
    "Before-work should use its schedule icon.");
AssertEqual(
    IconStateResolver.Resolve(
        new IconVisualState(WorkScheduleState.Lunch, ActivityMode.Computer)),
    IconAssetKey.Lunch,
    "Lunch should use its schedule icon.");
AssertEqual(
    IconStateResolver.Resolve(
        new IconVisualState(WorkScheduleState.OffWork, ActivityMode.Computer)),
    IconAssetKey.OffWork,
    "Off-work should use its schedule icon.");
AssertEqual(
    IconStateResolver.Resolve(
        new IconVisualState(WorkScheduleState.Working, ActivityMode.Meeting)),
    IconAssetKey.MeetingMode,
    "Meeting mode should override a normal schedule icon.");
AssertEqual(
    IconStateResolver.Resolve(
        new IconVisualState(
            WorkScheduleState.Working,
            ActivityMode.Meeting,
            ReminderIconType.Drink)),
    IconAssetKey.DrinkReminder,
    "Drink reminders should override other icon states.");
AssertEqual(
    IconStateResolver.Resolve(
        new IconVisualState(
            WorkScheduleState.Working,
            ActivityMode.Lab,
            ReminderIconType.Stand)),
    IconAssetKey.StandReminder,
    "Stand reminders should override other icon states.");
AssertEqual(IconService.ChooseTraySize(96), 16, "100% DPI should use a 16px tray icon.");
AssertEqual(IconService.ChooseTraySize(120), 20, "125% DPI should use a 20px tray icon.");
AssertEqual(IconService.ChooseTraySize(144), 24, "150% DPI should use a 24px tray icon.");
AssertEqual(IconService.ChooseTraySize(192), 32, "200% DPI should use a 32px tray icon.");

var workMode = new WorkModeService();
AssertEqual(
    workMode.ResolveScheduleState(WorkScheduleState.Working),
    WorkScheduleState.Working,
    "Normal mode should preserve the schedule state.");
workMode.SetActivityMode(ActivityMode.Lab);
AssertEqual(workMode.ActivityMode, ActivityMode.Lab, "Lab mode should be shared by dashboard and tray.");
workMode.ToggleOvertime();
AssertEqual(
    workMode.ResolveScheduleState(WorkScheduleState.OffWork),
    WorkScheduleState.Overtime,
    "Manual overtime should override the scheduled state.");
var workModeSnapshot = workMode.Snapshot;
AssertEqual(workModeSnapshot.ActivityMode, ActivityMode.Lab, "Work mode snapshot must expose the current activity mode atomically.");
AssertEqual(workModeSnapshot.IsOvertime, true, "Work mode snapshot must expose overtime atomically.");

var emptyActivity = new EmptyActivitySeriesProvider().GetSnapshot(new DateTime(2026, 9, 8, 10, 0, 0));
AssertEqual(emptyActivity.ActivityScore, null, "Unavailable activity score must not be fabricated.");
AssertEqual(emptyActivity.Series.Count, 0, "Unavailable activity series must render an empty state.");

var activityThresholds = ActivityThresholds.Default;
AssertEqual(
    activityThresholds.Classify(TimeSpan.FromMinutes(2)),
    UserActivityState.Active,
    "Short input gaps should remain active.");
AssertEqual(
    activityThresholds.Classify(TimeSpan.FromMinutes(4)),
    UserActivityState.Idle,
    "Three to five minutes without input should be idle.");
AssertEqual(
    activityThresholds.Classify(TimeSpan.FromMinutes(5)),
    UserActivityState.Afk,
    "Five minutes without input should be AFK.");

var appUsage = new AppUsageService();
appUsage.Add(
    new DateOnly(2026, 9, 8),
    "devenv",
    TimeSpan.FromMinutes(10),
    TimeSpan.FromMinutes(8));
appUsage.Add(
    new DateOnly(2026, 9, 8),
    "devenv",
    TimeSpan.FromMinutes(5),
    TimeSpan.FromMinutes(4));
var visualStudioUsage = appUsage.GetForDate(new DateOnly(2026, 9, 8)).Single();
AssertEqual(
    visualStudioUsage.ForegroundDuration,
    TimeSpan.FromMinutes(15),
    "Foreground usage should aggregate by process and day.");
AssertEqual(
    visualStudioUsage.ActiveForegroundDuration,
    TimeSpan.FromMinutes(12),
    "Active foreground usage must exclude inactive time.");
appUsage.Add(
    new DateOnly(2026, 9, 8),
    "WorkMate",
    TimeSpan.FromMinutes(5),
    TimeSpan.FromMinutes(5));
AssertEqual(
    appUsage.GetForDate(new DateOnly(2026, 9, 8)).Count,
    1,
    "WorkMate must not be persisted as a user application.");
var externalSwitches = new ExternalAppSwitchTracker();
AssertEqual(externalSwitches.Observe("chrome", false), false, "The initial external app is a baseline.");
AssertEqual(externalSwitches.Observe("WorkMate"), false, "Opening WorkMate must not count as an app switch.");
AssertEqual(externalSwitches.Observe("devenv"), true, "Chrome to WorkMate to VS should count as one external app switch.");
AssertEqual(externalSwitches.Observe("DEVENV"), false, "The same normalized process name is not a new app switch.");

var aggregator = new ActivityAggregator();
aggregator.Add(new ActivityAggregationSample(
    new DateTime(2026, 9, 8, 23, 59, 55),
    new DateTime(2026, 9, 9, 0, 0, 5),
    new ActivityInputDelta(10, 4, 2, 0, 2, 100),
    UserActivityState.Active,
    2,
    "devenv",
    WorkScheduleState.Overtime,
    ActivityMode.Computer,
    WorkTimeCategory.Overtime,
    60,
    70));
var previousDayBucket = aggregator.GetBuckets(new DateOnly(2026, 9, 8)).Single();
var nextDayBucket = aggregator.GetBuckets(new DateOnly(2026, 9, 9)).Single();
AssertEqual(previousDayBucket.KeyboardCount, 5L, "A midnight-spanning input slice must split counts.");
AssertEqual(nextDayBucket.KeyboardCount, 5L, "The new day must receive only its half of the slice.");
AssertEqual(previousDayBucket.OvertimeSeconds, 5d, "Work duration must split at midnight.");
AssertEqual(nextDayBucket.OvertimeSeconds, 5d, "Work duration after midnight belongs to the new day.");
AssertDiscreteAllocation(1, 0, 0, 0, false, "One keyboard event across a bucket boundary must be conserved.");
AssertDiscreteAllocation(3, 0, 0, 0, false, "Three keyboard events across a bucket boundary must be conserved.");
AssertDiscreteAllocation(0, 1, 0, 0, false, "One mouse click across a bucket boundary must be conserved.");
AssertDiscreteAllocation(0, 0, 0, 1, false, "One app switch across a bucket boundary must be conserved.");
AssertDiscreteAllocation(1, 1, 1, 1, true, "Discrete events across midnight must be conserved.");

var nonWorkAggregator = new ActivityAggregator();
nonWorkAggregator.Add(new ActivityAggregationSample(
    new DateTime(2026, 9, 8, 11, 30, 0),
    new DateTime(2026, 9, 8, 11, 35, 0),
    new ActivityInputDelta(100, 20, 0, 0, 5, 1_000),
    UserActivityState.Active,
    2,
    "chrome",
    WorkScheduleState.Lunch,
    ActivityMode.Computer,
    WorkTimeCategory.None,
    80,
    90));
var lunchActivityBucket = nonWorkAggregator.GetBuckets(new DateOnly(2026, 9, 8)).Single();
if (lunchActivityBucket.ActivityScore <= 0)
{
    throw new InvalidOperationException("Lunch computer activity should still produce ActivityScore.");
}

AssertEqual(lunchActivityBucket.WorkIntensity, 0d, "Lunch activity must not contribute WorkIntensity.");
nonWorkAggregator.Add(new ActivityAggregationSample(
    new DateTime(2026, 9, 8, 17, 30, 0),
    new DateTime(2026, 9, 8, 17, 35, 0),
    new ActivityInputDelta(100, 20, 0, 0, 5, 1_000),
    UserActivityState.Active,
    2,
    "chrome",
    WorkScheduleState.OffWork,
    ActivityMode.Computer,
    WorkTimeCategory.None,
    80,
    90));
var offWorkActivityBucket = nonWorkAggregator.GetBuckets(new DateOnly(2026, 9, 8))
    .Single(static bucket => bucket.BucketStart.Hour == 17);
AssertEqual(offWorkActivityBucket.WorkIntensity, 0d, "Off-work activity must not contribute WorkIntensity.");
nonWorkAggregator.Add(new ActivityAggregationSample(
    new DateTime(2026, 9, 8, 18, 0, 0),
    new DateTime(2026, 9, 8, 18, 5, 0),
    new ActivityInputDelta(100, 20, 0, 0, 5, 1_000),
    UserActivityState.Active,
    2,
    "devenv",
    WorkScheduleState.Overtime,
    ActivityMode.Computer,
    WorkTimeCategory.Overtime,
    80,
    70));
var overtimeIntensityBucket = nonWorkAggregator.GetBuckets(new DateOnly(2026, 9, 8))
    .Single(static bucket => bucket.BucketStart.Hour == 18);
AssertEqual(overtimeIntensityBucket.WorkIntensity, 70d, "Overtime work must restore WorkIntensity aggregation.");

var twoHourAggregator = new ActivityAggregator();
var twoHourStart = new DateTime(2026, 9, 8, 8, 0, 0);
for (var index = 0; index < 1_440; index++)
{
    var sliceStart = twoHourStart.AddSeconds(index * 5);
    twoHourAggregator.Add(new ActivityAggregationSample(
        sliceStart,
        sliceStart.AddSeconds(5),
        new ActivityInputDelta(1, 0, 0, 0, 0, 10),
        UserActivityState.Active,
        0,
        "devenv",
        WorkScheduleState.Working,
        ActivityMode.Computer,
        WorkTimeCategory.Normal,
        40,
        50,
        (index + 1) * 5));
}

var twoHourBuckets = twoHourAggregator.GetBuckets(new DateOnly(2026, 9, 8));
AssertEqual(twoHourBuckets.Count, 24, "Two hours of five-second samples should remain 24 five-minute buckets.");
AssertEqual(twoHourBuckets.Sum(static bucket => bucket.KeyboardCount), 1_440L, "Two-hour aggregation must preserve counts.");
AssertEqual(twoHourBuckets.Sum(static bucket => bucket.NormalWorkSeconds), 7_200d, "Two-hour aggregation must preserve work duration.");
AssertEqual(twoHourBuckets.Max(static bucket => bucket.LongestContinuousWorkSeconds), 7_200d, "The longest continuous session must survive bucket boundaries.");

AssertEqual(
    WorkSliceClassifier.Classify(new WorkClassificationContext(
        WorkScheduleState.Working,
        ActivityMode.Lab,
        false,
        UserActivityState.Afk,
        true)),
    WorkTimeCategory.Lab,
    "Lab mode must count manual work even without keyboard or mouse activity.");
AssertEqual(
    WorkSliceClassifier.Classify(new WorkClassificationContext(
        WorkScheduleState.OffWork,
        ActivityMode.Computer,
        false,
        UserActivityState.Active,
        true)),
    WorkTimeCategory.None,
    "Normal work must not grow after work hours.");
AssertEqual(
    WorkSliceClassifier.Classify(new WorkClassificationContext(
        WorkScheduleState.OffWork,
        ActivityMode.Computer,
        true,
        UserActivityState.Active,
        true)),
    WorkTimeCategory.Overtime,
    "Active overtime must be recorded separately.");
AssertEqual(
    WorkSliceClassifier.Classify(new WorkClassificationContext(
        WorkScheduleState.Working,
        ActivityMode.Meeting,
        true,
        UserActivityState.Active,
        true)),
    WorkTimeCategory.Meeting,
    "Manual meeting time must take precedence over overtime to avoid double counting.");
AssertEqual(
    WorkSliceClassifier.Classify(new WorkClassificationContext(
        WorkScheduleState.Lunch,
        ActivityMode.Computer,
        false,
        UserActivityState.Active,
        true)),
    WorkTimeCategory.None,
    "Computer activity during lunch must not increase normal work.");

var manualModeAggregator = new ActivityAggregator();
manualModeAggregator.Add(new ActivityAggregationSample(
    new DateTime(2026, 9, 8, 18, 0, 0),
    new DateTime(2026, 9, 8, 18, 30, 0),
    ActivityInputDelta.Empty,
    UserActivityState.Afk,
    0,
    null,
    WorkScheduleState.OffWork,
    ActivityMode.Lab,
    WorkTimeCategory.Lab,
    0,
    20,
    1_800));
var labBuckets = manualModeAggregator.GetBuckets(new DateOnly(2026, 9, 8));
AssertEqual(labBuckets.Sum(static bucket => bucket.LabSeconds), 1_800d, "Thirty AFK minutes in LAB must count as lab time.");
AssertEqual(labBuckets.Sum(static bucket => bucket.ManualWorkSeconds), 1_800d, "LAB must add to manual work exactly once.");
AssertEqual(labBuckets.Sum(static bucket => bucket.NormalWorkSeconds), 0d, "LAB must not also add normal work.");
AssertEqual(labBuckets.All(static bucket => bucket.WorkIntensity == 20d), true, "LAB work must retain WorkIntensity.");

var sessions = new WorkSessionTracker();
var activeContext = new WorkClassificationContext(
    WorkScheduleState.Working,
    ActivityMode.Computer,
    false,
    UserActivityState.Active,
    true);
sessions.Advance(TimeSpan.FromMinutes(20), activeContext);
sessions.Advance(
    TimeSpan.FromMinutes(2),
    activeContext with { UserState = UserActivityState.Idle });
AssertEqual(
    sessions.GetSnapshot().ContinuousDuration,
    TimeSpan.FromMinutes(22),
    "A short idle period may keep the continuous session alive.");
sessions.Advance(
    TimeSpan.FromMinutes(5),
    activeContext with { UserState = UserActivityState.Afk });
AssertEqual(
    sessions.GetSnapshot().ContinuousDuration,
    TimeSpan.Zero,
    "AFK must reset continuous work.");
sessions.Advance(
    TimeSpan.FromMinutes(30),
    activeContext with { ActivityMode = ActivityMode.Lab, UserState = UserActivityState.Afk });
AssertEqual(
    sessions.GetSnapshot().ContinuousDuration,
    TimeSpan.FromMinutes(30),
    "Manual lab mode must form a new session even without keyboard or mouse activity.");

var sleepSessions = new WorkSessionTracker();
sleepSessions.Advance(TimeSpan.FromMinutes(20), activeContext);
sleepSessions.Advance(
    TimeSpan.FromHours(1),
    activeContext with { SystemAvailable = false });
AssertEqual(
    sleepSessions.GetSnapshot().ContinuousDuration,
    TimeSpan.Zero,
    "One hour of sleep or lock must reset continuous work.");
sleepSessions.Advance(TimeSpan.FromMinutes(5), activeContext);
AssertEqual(
    sleepSessions.GetSnapshot().ContinuousDuration,
    TimeSpan.FromMinutes(5),
    "Work after resume must start a new session instead of including sleep time.");
AssertEqual(
    WorkSliceClassifier.Classify(activeContext with
    {
        ActivityMode = ActivityMode.Lab,
        SystemAvailable = false
    }),
    WorkTimeCategory.None,
    "Sleep or lock must never be classified as work, including manual modes.");

var availabilityState = new SystemAvailabilityState();
AssertEqual(availabilityState.SetSessionLocked(true), false, "Lock must make activity unavailable.");
AssertEqual(availabilityState.SetSuspended(true), false, "Suspend while locked must remain unavailable.");
AssertEqual(availabilityState.SetSessionLocked(false), false, "Unlock while suspended must remain unavailable.");
AssertEqual(availabilityState.SetSuspended(false), true, "Activity resumes only after unlock and resume.");

var standCycle = new StandReminderCycle();
AssertEqual(
    standCycle.Advance(TimeSpan.FromMinutes(50), true, TimeSpan.FromMinutes(50)),
    true,
    "Stand reminder should become due after one complete active cycle.");
standCycle.Complete();
AssertEqual(
    standCycle.Advance(TimeSpan.FromMinutes(50), true, TimeSpan.FromMinutes(50)),
    true,
    "Completing a stand reminder must start a new full reminder cycle.");
standCycle.Expire();
AssertEqual(
    standCycle.Advance(TimeSpan.FromMinutes(50), true, TimeSpan.FromMinutes(50)),
    true,
    "An expired queued stand reminder must release pending state for a new cycle.");
standCycle.Reset();
standCycle.Advance(TimeSpan.FromMinutes(40), true, TimeSpan.FromMinutes(50));
standCycle.Reset();
AssertEqual(
    standCycle.Advance(TimeSpan.FromMinutes(10), true, TimeSpan.FromMinutes(50)),
    false,
    "AFK, lunch, off-work, sleep, and lock resets must discard the previous stand interval.");
var speechOnlyLifecycle = new ReminderPresentationLifecycle();
speechOnlyLifecycle.MarkPreempted();
AssertEqual(
    speechOnlyLifecycle.Resolve(
        ReminderAction.Dismissed,
        DateTimeOffset.Now.AddMinutes(1),
        DateTimeOffset.Now),
    ReminderAction.Preempted,
    "Speech-only preemption must not fall back to Dismissed.");
var popupLifecycle = new ReminderPresentationLifecycle();
popupLifecycle.MarkPreempted();
popupLifecycle.MarkActionSelected(ReminderAction.Preempted);
AssertEqual(
    popupLifecycle.Resolve(
        ReminderAction.Preempted,
        DateTimeOffset.Now.AddMinutes(1),
        DateTimeOffset.Now),
    ReminderAction.Preempted,
    "Popup preemption must remain Preempted and eligible for requeue.");
var expiredLifecycle = new ReminderPresentationLifecycle();
expiredLifecycle.MarkPreempted();
AssertEqual(
    expiredLifecycle.Resolve(
        ReminderAction.Dismissed,
        DateTimeOffset.Now.AddMinutes(-1),
        DateTimeOffset.Now),
    ReminderAction.Expired,
    "Preempted reminders past ExpiresAt must finalize as Expired.");
var completedLifecycle = new ReminderPresentationLifecycle();
completedLifecycle.MarkActionSelected(ReminderAction.Completed);
completedLifecycle.MarkPreempted();
AssertEqual(
    completedLifecycle.Resolve(
        ReminderAction.Completed,
        DateTimeOffset.Now.AddMinutes(1),
        DateTimeOffset.Now),
    ReminderAction.Completed,
    "A completed user action must not be overwritten by a later preemption.");

var intensityCalculator = new WorkIntensityCalculator();
var quietIntensity = intensityCalculator.Calculate(new ActivityIntensityInput(
    ActivityInputDelta.Empty,
    TimeSpan.FromSeconds(5),
    UserActivityState.Active,
    TimeSpan.Zero,
    0,
    0));
var busyIntensity = intensityCalculator.Calculate(new ActivityIntensityInput(
    new ActivityInputDelta(20, 5, 2, 0, 2, 4000),
    TimeSpan.FromSeconds(5),
    UserActivityState.Active,
    TimeSpan.FromMinutes(50),
    1,
    3));
AssertEqual(quietIntensity.ActivityScore, 0, "No input should produce a zero raw activity baseline.");
if (busyIntensity.ActivityScore <= 0 || busyIntensity.ActivityScore >= 100)
{
    throw new InvalidOperationException("EMA must increase activity smoothly instead of jumping directly to 100.");
}

if (busyIntensity.WorkIntensity <= quietIntensity.WorkIntensity || busyIntensity.WorkIntensity >= 100)
{
    throw new InvalidOperationException("Work intensity must be responsive, bounded, and EMA-smoothed.");
}

AssertEqual(
    ReminderEngine.CalculateCleaningReminderTime(defaults, new DateOnly(2026, 9, 8), 30),
    new DateTime(2026, 9, 8, 16, 30, 0),
    "Summer cleaning reminder should be 30 minutes before afternoon work ends.");
AssertEqual(
    ReminderEngine.CalculateCleaningReminderTime(defaults, new DateOnly(2026, 12, 8), 30),
    new DateTime(2026, 12, 8, 16, 15, 0),
    "Winter cleaning reminder should use the active winter profile.");
AssertEqual(
    ReminderEngine.CalculateCleaningReminderTime(defaults, new DateOnly(2026, 9, 12), 30),
    null,
    "Weekend cleaning reminders must be disabled.");
AssertEqual(
    ReminderEngine.CalculateScheduleReminderTime(
        defaults,
        new DateOnly(2026, 9, 8),
        ScheduleReminderNode.LunchSoon,
        5),
    new DateTime(2026, 9, 8, 11, 10, 0),
    "Lunch pre-reminder must be calculated from the active profile morning end.");
AssertEqual(
    ReminderEngine.CalculateScheduleReminderTime(
        defaults,
        new DateOnly(2026, 12, 8),
        ScheduleReminderNode.AfternoonStart),
    new DateTime(2026, 12, 8, 12, 0, 0),
    "Afternoon start reminder must follow the winter profile.");
AssertEqual(
    ReminderEngine.CalculateScheduleReminderTime(
        defaults,
        new DateOnly(2026, 9, 12),
        ScheduleReminderNode.OffWork),
    null,
    "Schedule reminders must not run on weekends.");
AssertEqual(
    ReminderPresentationService.MinimumReminderGap,
    TimeSpan.FromMinutes(3),
    "Ordinary reminders must keep a three-minute presentation gap.");
if (!ReminderPresentationSettings.Default.TryValidate(out _))
{
    throw new InvalidOperationException("Default reminder presentation settings must be valid.");
}

if ((ReminderPresentationSettings.Default with { LunchSoonAdvanceMinutes = 7 }).TryValidate(out _))
{
    throw new InvalidOperationException("Lunch pre-reminder must accept only 5, 10, or 15 minutes.");
}
AssertEqual(
    ReminderEngine.CalculateNextCleaningReminderTime(
        defaults,
        new DateTime(2026, 9, 8, 16, 31, 0),
        30),
    new DateTime(2026, 9, 9, 16, 30, 0),
    "Starting after today's reminder must not replay the missed reminder.");

var changedEnd = defaults with
{
    SummerProfile = defaults.SummerProfile with { AfternoonEnd = new TimeOnly(18, 0) }
};
AssertEqual(
    ReminderEngine.CalculateCleaningReminderTime(changedEnd, new DateOnly(2026, 9, 8), 20),
    new DateTime(2026, 9, 8, 17, 40, 0),
    "Changing work end should immediately change the calculated reminder time.");
AssertEqual(
    ReminderEngine.CreateCleaningMessage(20),
    "还有二十分钟下班，记得打扫一下卫生。",
    "Non-default reminder text should include the configured lead time.");

var temporaryRoot = Path.Combine(Path.GetTempPath(), "WorkMate.ScheduleChecks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporaryRoot);
try
{
    var defaultsDatabasePath = Path.Combine(temporaryRoot, "schedule-defaults.db");
    var defaultsDatabase = new DatabaseStore(defaultsDatabasePath);
    await defaultsDatabase.InitializeAsync(CancellationToken.None);
    var defaultsStore = new ScheduleSettingsStore(defaultsDatabase);
    var firstRunSettings = await defaultsStore.LoadAsync(CancellationToken.None);
    AssertEqual(firstRunSettings, defaults, "A new database must receive the centralized schedule defaults.");
    var userSettings = defaults with
    {
        SummerProfile = defaults.SummerProfile with
        {
            MorningStart = new TimeOnly(8, 30),
            MorningEnd = new TimeOnly(11, 30),
            AfternoonStart = new TimeOnly(13, 0),
            AfternoonEnd = new TimeOnly(17, 30)
        }
    };
    await defaultsStore.SaveAsync(userSettings, CancellationToken.None);
    var reopenedDatabase = new DatabaseStore(defaultsDatabasePath);
    await reopenedDatabase.InitializeAsync(CancellationToken.None);
    var reopenedSettings = await new ScheduleSettingsStore(reopenedDatabase)
        .LoadAsync(CancellationToken.None);
    AssertEqual(
        reopenedSettings,
        userSettings,
        "Restarting or replacing the executable must not overwrite valid user schedule settings.");

    var databasePath = Path.Combine(temporaryRoot, "checks.db");
    await using (var legacyConnection = new SqliteConnection($"Data Source={databasePath}"))
    {
        await legacyConnection.OpenAsync();
        var legacySchema = legacyConnection.CreateCommand();
        legacySchema.CommandText = """
            CREATE TABLE activity_buckets (
                bucket_start TEXT PRIMARY KEY,
                bucket_end TEXT NOT NULL,
                keyboard_count INTEGER NOT NULL,
                mouse_click_count INTEGER NOT NULL,
                mouse_distance REAL NOT NULL,
                scroll_count INTEGER NOT NULL,
                active_seconds REAL NOT NULL,
                idle_seconds REAL NOT NULL,
                afk_seconds REAL NOT NULL,
                app_switch_count INTEGER NOT NULL,
                dominant_process TEXT NULL,
                schedule_state TEXT NOT NULL,
                work_mode TEXT NOT NULL,
                normal_work_seconds REAL NOT NULL,
                overtime_seconds REAL NOT NULL,
                manual_work_seconds REAL NOT NULL,
                lab_seconds REAL NOT NULL,
                meeting_seconds REAL NOT NULL,
                activity_score REAL NOT NULL,
                work_intensity REAL NOT NULL,
                updated_at TEXT NOT NULL
            );
            """;
        await legacySchema.ExecuteNonQueryAsync();
    }

    var database = new DatabaseStore(databasePath);
    await database.InitializeAsync(CancellationToken.None);
    await using (var schemaConnection = new SqliteConnection($"Data Source={databasePath}"))
    {
        await schemaConnection.OpenAsync();
        var schemaCommand = schemaConnection.CreateCommand();
        schemaCommand.CommandText = "PRAGMA user_version;";
        AssertEqual(Convert.ToInt32(await schemaCommand.ExecuteScalarAsync()), 3, "Database migrations must finish at schema version 3.");
    }
    await database.UpsertActivityBucketsAsync(
        new[] { previousDayBucket, nextDayBucket },
        CancellationToken.None);
    var restoredBuckets = await database.GetActivityBucketsAsync(
        new DateOnly(2026, 9, 8),
        CancellationToken.None);
    AssertEqual(restoredBuckets.Count, 1, "Activity buckets should round-trip through SQLite.");
    AssertEqual(restoredBuckets[0].KeyboardCount, 5L, "Persisted buckets must retain aggregate counts.");
    await database.UpsertAppUsageAsync(
        appUsage.GetForDate(new DateOnly(2026, 9, 8)),
        CancellationToken.None);
    var restoredUsage = await database.GetAppUsageAsync(
        new DateOnly(2026, 9, 8),
        CancellationToken.None);
    AssertEqual(restoredUsage.Single().ProcessName, "devenv", "App usage should round-trip by process name.");
    await database.UpsertAppUsageAsync(
        [new AppUsageEntry(
            new DateOnly(2026, 9, 8),
            "WorkMate",
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5))],
        CancellationToken.None);
    AssertEqual(
        (await database.GetAppUsageAsync(new DateOnly(2026, 9, 8), CancellationToken.None)).Count,
        1,
        "WorkMate self usage must not be written to or returned from app_usage_daily.");
    using var engine = new ScheduleEngine(new ScheduleSettingsStore(database));
    await engine.InitializeAsync(CancellationToken.None);
    using (var concurrentActivity = new ActivitySnapshotService(
               database,
               engine,
               new WorkModeService()))
    {
        await concurrentActivity.InitializeAsync(CancellationToken.None);
        await Task.WhenAll(
            concurrentActivity.FlushAsync(CancellationToken.None),
            concurrentActivity.SetSystemAvailableAsync(false));
        AssertEqual(
            concurrentActivity.Current.UserState,
            UserActivityState.Afk,
            "A concurrent Tick/Lock transition must publish an unavailable AFK snapshot without racing.");
        await concurrentActivity.SetSystemAvailableAsync(true);
        await concurrentActivity.FlushAsync(CancellationToken.None);
    }

    using var presentation = new TestReminderPresentationService();
    var reminderWorkMode = new WorkModeService();
    using var reminders = new ReminderEngine(
        engine,
        new CleaningReminderSettingsStore(database),
        new ReminderPresentationSettingsStore(database),
        database,
        presentation,
        reminderWorkMode);
    await reminders.InitializeAsync(CancellationToken.None);
    var calculator = new WorkDurationCalculator(engine);
    using var summaryService = new DailySummaryService(
        database,
        new TestActivitySnapshotService(),
        engine,
        new WorkModeService());
    var dailySummary = await summaryService.GenerateAsync(
        new DateOnly(2026, 9, 8),
        CancellationToken.None);
    AssertEqual(
        dailySummary.OvertimeDuration,
        TimeSpan.FromSeconds(5),
        "Daily summary must use the persisted mutually exclusive overtime duration.");
    AssertEqual(
        dailySummary.TotalWorkDuration,
        TimeSpan.FromSeconds(5),
        "Daily total must not double-count work categories.");
    AssertEqual(
        dailySummary.TopApplications.Single().ProcessName,
        "devenv",
        "Daily summary should include top applications from aggregate usage.");

    AssertEqual(
        reminders.CanDeliver(ReminderKind.DrinkWater, new DateTime(2026, 9, 8, 10, 0, 0)),
        true,
        "Work reminders should be enabled while working.");
    AssertEqual(
        reminders.CanDeliver(ReminderKind.Stand, new DateTime(2026, 9, 8, 11, 30, 0)),
        false,
        "Work reminders should be disabled during lunch.");
    var reminderActivity = new TestActivitySnapshotService();
    using var activityAwareReminders = new ReminderEngine(
        engine,
        new CleaningReminderSettingsStore(database),
        new ReminderPresentationSettingsStore(database),
        database,
        presentation,
        reminderWorkMode,
        reminderActivity);
    reminderActivity.CurrentSnapshot = reminderActivity.CurrentSnapshot with
    {
        UserState = UserActivityState.Afk
    };
    AssertEqual(
        activityAwareReminders.CanDeliver(
            ReminderKind.Stand,
            new DateTime(2026, 9, 8, 10, 0, 0)),
        false,
        "AFK should suppress stand reminders even during work hours.");
    reminderActivity.CurrentSnapshot = reminderActivity.CurrentSnapshot with
    {
        UserState = UserActivityState.Active
    };
    AssertEqual(
        activityAwareReminders.CanDeliver(
            ReminderKind.Stand,
            new DateTime(2026, 9, 8, 10, 0, 0)),
        true,
        "Active input should re-enable stand reminders during work hours.");
    reminderActivity.CurrentSnapshot = reminderActivity.CurrentSnapshot with
    {
        UserState = UserActivityState.Afk
    };
    reminderWorkMode.SetActivityMode(ActivityMode.Lab);
    AssertEqual(
        activityAwareReminders.CanDeliver(
            ReminderKind.Stand,
            new DateTime(2026, 9, 8, 10, 0, 0)),
        false,
        "LAB mode should suppress stand reminders by default.");
    AssertEqual(
        activityAwareReminders.CanDeliver(
            ReminderKind.DrinkWater,
            new DateTime(2026, 9, 8, 10, 0, 0)),
        true,
        "LAB mode should keep drink reminders even without keyboard input.");
    reminderWorkMode.SetActivityMode(ActivityMode.Meeting);
    AssertEqual(
        activityAwareReminders.CanDeliver(
            ReminderKind.Stand,
            new DateTime(2026, 9, 8, 10, 0, 0)),
        true,
        "Meeting mode should keep lightweight popup reminders available.");
    reminderWorkMode.SetActivityMode(ActivityMode.Computer);
    reminderWorkMode.ToggleOvertime();
    AssertEqual(
        activityAwareReminders.CanDeliver(
            ReminderKind.DrinkWater,
            new DateTime(2026, 9, 8, 18, 0, 0)),
        false,
        "AFK should still suppress ordinary overtime reminders.");
    reminderActivity.CurrentSnapshot = reminderActivity.CurrentSnapshot with
    {
        UserState = UserActivityState.Active
    };
    AssertEqual(
        activityAwareReminders.CanDeliver(
            ReminderKind.DrinkWater,
            new DateTime(2026, 9, 8, 18, 0, 0)),
        true,
        "Active overtime should re-enable ordinary reminders.");
    reminderWorkMode.ToggleOvertime();
    reminders.SetAllRemindersPaused(true);
    AssertEqual(
        reminders.CanDeliver(ReminderKind.DrinkWater, new DateTime(2026, 9, 8, 10, 0, 0)),
        false,
        "Pause all should gate ordinary reminders.");
    reminders.SetAllRemindersPaused(false);
    AssertEqual(
        calculator.CanAccumulate(new DateTime(2026, 9, 8, 18, 0, 0)),
        false,
        "Work duration should not accumulate after work.");

    var reloadCount = 0;
    engine.SettingsReloaded += (_, _) => reloadCount++;
    var manualWinter = defaults with
    {
        SwitchMode = ScheduleSwitchMode.Manual,
        ManualProfile = ScheduleProfileType.Winter
    };
    await engine.UpdateSettingsAsync(manualWinter, CancellationToken.None);
    AssertEqual(reloadCount, 1, "Updating settings should reload the engine immediately.");
    AssertEqual(
        engine.GetSnapshot(new DateTime(2026, 9, 8, 8, 10, 0)).State,
        WorkScheduleState.BeforeWork,
        "The reloaded winter profile should take effect without restart.");
    await engine.UpdateSettingsAsync(defaults, CancellationToken.None);
    AssertEqual(
        engine.GetSnapshot(new DateTime(2026, 9, 8, 12, 30, 0)).ActiveProfile,
        ScheduleProfileType.Summer,
        "Re-enabling automatic mode must immediately resolve the profile from the current date.");

    await reminders.UpdateCleaningSettingsAsync(
        new CleaningReminderSettings(true, 20),
        CancellationToken.None);
    AssertEqual(
        reminders.CleaningSettings.AdvanceMinutes,
        20,
        "Cleaning settings should reload without restart.");
    var updatedPresentationSettings = ReminderPresentationSettings.Default with
    {
        DrinkIntervalMinutes = 60,
        StandContinuousMinutes = 45,
        LunchSoonAdvanceMinutes = 10,
        SpeechVolume = 80,
        SpeechRate = 1,
        SpeechVoiceName = "Test Voice",
        ScheduleSpeechEnabled = false,
        CleaningSpeechEnabled = false
    };
    await reminders.UpdatePresentationSettingsAsync(
        updatedPresentationSettings,
        CancellationToken.None);
    AssertEqual(
        reminders.PresentationSettings.DrinkIntervalMinutes,
        60,
        "Drink reminder interval should hot reload without restart.");
    var restoredPresentationSettings = await new ReminderPresentationSettingsStore(database)
        .LoadAsync(CancellationToken.None);
    AssertEqual(
        restoredPresentationSettings.LunchSoonAdvanceMinutes,
        10,
        "Reminder presentation settings should round-trip through SQLite.");
    AssertEqual(
        restoredPresentationSettings.SpeechVoiceName,
        "Test Voice",
        "Voice selection should round-trip through SQLite.");
    AssertEqual(
        restoredPresentationSettings.ScheduleSpeechEnabled,
        false,
        "Schedule speech preference should round-trip through SQLite.");

    var historyDate = new DateOnly(2026, 9, 8);
    var claimed = await database.TryClaimReminderAsync(
        ReminderEngine.CleaningReminderType,
        historyDate,
        DateTimeOffset.Now,
        "test",
        CancellationToken.None);
    var duplicate = await database.TryClaimReminderAsync(
        ReminderEngine.CleaningReminderType,
        historyDate,
        DateTimeOffset.Now,
        "test",
        CancellationToken.None);
    AssertEqual(claimed, true, "The first daily reminder claim should succeed.");
    AssertEqual(duplicate, false, "A restart must not claim the same daily reminder again.");
    var shownAt = DateTimeOffset.Now;
    await database.RecordReminderShownAsync(
        ReminderEngine.CleaningReminderType,
        historyDate,
        shownAt,
        CancellationToken.None);
    AssertEqual(
        await database.TrySnoozeReminderAsync(
            ReminderEngine.CleaningReminderType,
            historyDate,
            DateTimeOffset.Now.AddMinutes(10),
            ReminderEngine.MaximumCleaningSnoozes,
            CancellationToken.None),
        true,
        "The first snooze should succeed.");
    AssertEqual(
        await database.TrySnoozeReminderAsync(
            ReminderEngine.CleaningReminderType,
            historyDate,
            DateTimeOffset.Now.AddMinutes(20),
            ReminderEngine.MaximumCleaningSnoozes,
            CancellationToken.None),
        true,
        "The second snooze should succeed.");
    AssertEqual(
        await database.TrySnoozeReminderAsync(
            ReminderEngine.CleaningReminderType,
            historyDate,
            DateTimeOffset.Now.AddMinutes(30),
            ReminderEngine.MaximumCleaningSnoozes,
            CancellationToken.None),
        false,
        "A third snooze must be rejected.");
    await database.RecordReminderActionAsync(
        ReminderEngine.CleaningReminderType,
        historyDate,
        ReminderAction.Completed.ToString(),
        ReminderHistoryStatus.Completed,
        shownAt,
        CancellationToken.None);
    var actionHistory = await database.GetReminderHistoryAsync(
        ReminderEngine.CleaningReminderType,
        historyDate,
        CancellationToken.None);
    AssertEqual(actionHistory?.Status, ReminderHistoryStatus.Completed, "Reminder completion must be persisted.");
    AssertEqual(actionHistory?.Action, ReminderAction.Completed.ToString(), "Reminder action must be persisted.");
    if (actionHistory?.ShownAt is null)
    {
        throw new InvalidOperationException("Reminder shown time must be persisted.");
    }

    var ordinaryType = "Drink:100000";
    AssertEqual(
        await database.TryClaimReminderAsync(ordinaryType, historyDate, shownAt, "喝水", CancellationToken.None),
        true,
        "An ordinary reminder should be claimable before snoozing.");
    var ordinaryDueAt = DateTimeOffset.Now.AddMinutes(10);
    AssertEqual(
        await database.TrySnoozeReminderAsync(ordinaryType, historyDate, ordinaryDueAt, 1, CancellationToken.None),
        true,
        "An ordinary reminder snooze must be persisted.");
    var reopenedSnoozeDatabase = new DatabaseStore(databasePath);
    var restoredSnoozes = await reopenedSnoozeDatabase.GetSnoozedOrdinaryRemindersAsync(historyDate, CancellationToken.None);
    AssertEqual(restoredSnoozes.Count, 1, "Ordinary snoozes must be restored after restarting the database store.");
    AssertEqual(restoredSnoozes[0].NextDueAt is not null, true, "Restored ordinary snooze must retain next_due_at.");

    var preemptedOrdinaryType = "Stand:preempted-requeue";
    await database.TryClaimReminderAsync(preemptedOrdinaryType, historyDate, shownAt, "站立", CancellationToken.None);
    var preemptedOutcome = new ReminderPresentationLifecycle();
    preemptedOutcome.MarkPreempted();
    AssertEqual(
        preemptedOutcome.Resolve(
            ReminderAction.Dismissed,
            DateTimeOffset.Now.AddMinutes(1),
            DateTimeOffset.Now),
        ReminderAction.Preempted,
        "A valid preemption should be requeued as an internal outcome.");
    var beforeSnooze = await database.GetReminderHistoryAsync(preemptedOrdinaryType, historyDate, CancellationToken.None);
    AssertEqual(beforeSnooze?.Status, ReminderHistoryStatus.Triggered, "A requeued preemption must keep Triggered persistence.");
    var requeueDueAt = DateTimeOffset.Now.AddMinutes(10);
    AssertEqual(
        await database.TrySnoozeReminderAsync(preemptedOrdinaryType, historyDate, requeueDueAt, 1, CancellationToken.None),
        true,
        "A requeued reminder must still accept Snooze.");
    var requeueSnooze = await database.GetReminderHistoryAsync(preemptedOrdinaryType, historyDate, CancellationToken.None);
    AssertEqual(requeueSnooze?.Status, ReminderHistoryStatus.Snoozed, "Requeued reminder should persist Snoozed status.");
    AssertEqual(requeueSnooze?.NextDueAt is not null, true, "Requeued Snooze must persist next_due_at.");

    var legacyPreemptedType = "Drink:legacy-preempted";
    await database.TryClaimReminderAsync(legacyPreemptedType, historyDate, shownAt, "喝水", CancellationToken.None);
    await database.RecordReminderActionAsync(
        legacyPreemptedType,
        historyDate,
        ReminderAction.Preempted.ToString(),
        ReminderHistoryStatus.Preempted,
        shownAt,
        CancellationToken.None);
    await database.RestorePreemptedOrdinaryRemindersAsync(historyDate, CancellationToken.None);
    var restoredLegacyPreempted = await database.GetReminderHistoryAsync(legacyPreemptedType, historyDate, CancellationToken.None);
    AssertEqual(restoredLegacyPreempted?.Status, ReminderHistoryStatus.Triggered, "A legacy persisted Preempted ordinary reminder must recover as Triggered.");

    AssertEqual(
        AutoOvertimeSuggestionService.MapActionToStatus(ReminderAction.Expired),
        ReminderHistoryStatus.Expired,
        "Auto overtime Expired must not be persisted as Dismissed.");
    AssertEqual(
        AutoOvertimeSuggestionService.MapActionToStatus(ReminderAction.Dismissed),
        ReminderHistoryStatus.Dismissed,
        "Auto overtime Dismissed must remain Dismissed.");
    AssertEqual(
        AutoOvertimeSuggestionService.MapActionToStatus(ReminderAction.StartOvertime),
        ReminderHistoryStatus.Completed,
        "Auto overtime StartOvertime must be persisted as Completed.");

    var preemptedType = "Schedule.Preempted.Test";
    AssertEqual(
        await database.TryClaimReminderAsync(preemptedType, historyDate, shownAt, "测试", CancellationToken.None),
        true,
        "A reminder should be claimable before preemption.");
    await database.RecordReminderActionAsync(
        preemptedType,
        historyDate,
        ReminderAction.Preempted.ToString(),
        ReminderHistoryStatus.Preempted,
        shownAt,
        CancellationToken.None);
    var preemptedHistory = await database.GetReminderHistoryAsync(preemptedType, historyDate, CancellationToken.None);
    AssertEqual(preemptedHistory?.Status, ReminderHistoryStatus.Preempted, "Preempted reminders must not be recorded as dismissed.");
    AssertEqual(
        Enum.Parse<ReminderHistoryStatus>(ReminderAction.Expired.ToString()),
        ReminderHistoryStatus.Expired,
        "Expired must remain a distinct reminder final outcome.");

    var expiredDate = DateOnly.FromDateTime(DateTime.Now);
    var expiredType = "Stand:expired-test";
    var expiredAt = DateTimeOffset.Now.AddMinutes(-5);
    await database.TryClaimReminderAsync(expiredType, expiredDate, expiredAt, "站立", CancellationToken.None);
    await database.TrySnoozeReminderAsync(expiredType, expiredDate, expiredAt, 1, CancellationToken.None);
    using (var expiryPresentation = new TestReminderPresentationService())
    using (var expiryEngine = new ReminderEngine(
               engine,
               new CleaningReminderSettingsStore(database),
               new ReminderPresentationSettingsStore(database),
               database,
               expiryPresentation,
               new WorkModeService()))
    {
        await expiryEngine.InitializeAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1.5));
    }

    var expiredHistory = await database.GetReminderHistoryAsync(expiredType, expiredDate, CancellationToken.None);
    AssertEqual(expiredHistory?.Status, ReminderHistoryStatus.Expired, "Expired ordinary snoozes must be marked Expired by the polling engine.");

    reminders.Dispose();
    reminders.Dispose();
}
finally
{
    SqliteConnection.ClearAllPools();
    if (Directory.Exists(temporaryRoot))
    {
        Directory.Delete(temporaryRoot, recursive: true);
    }
}

Console.WriteLine("All schedule checks passed.");
if (args.Contains("--speech-audit", StringComparer.OrdinalIgnoreCase))
{
    await RunSpeechAuditAsync();
}

static async Task RunSpeechAuditAsync()
{
    using var speechService = new SpeechService();
    speechService.UpdateSettings(enabled: true, volume: 80, rate: 0);
    Console.WriteLine($"Speech backend: {speechService.BackendName}");
    foreach (var voice in speechService.GetAvailableVoices())
    {
        Console.WriteLine($"Voice: {voice.Name} | {voice.Culture} | {voice.Gender} | {voice.Description}");
    }

    string[] samples =
    [
        "工作一会儿啦，喝口水吧。",
        "坐得有点久啦。已经连续工作五十二分钟了，起来活动一下吧。",
        "还有五分钟午休。手头的事情，可以慢慢收个尾啦。",
        "上午辛苦啦。午休时间到了，好好休息一下吧。",
        "下午好。休息结束啦，慢慢进入状态吧。",
        "下班时间到啦。今天也辛苦了，好好放松一下吧。",
        "还在加班呀。记得也照顾好自己，喝口水吧。",
        "还有半小时下班，可以顺手收拾一下工位啦。"
    ];
    foreach (var sample in samples)
    {
        Console.WriteLine($"Speaking: {sample}");
        await speechService.SpeakAsync(sample, CancellationToken.None);
    }

    Console.WriteLine("Speech audit playback completed.");
}

static void AssertState(ScheduleSettings settings, DateTime timestamp, WorkScheduleState expected)
{
    AssertEqual(ScheduleEvaluator.Evaluate(settings, timestamp).State, expected, $"Unexpected state at {timestamp:O}.");
}

static void AssertDiscreteAllocation(
    long keyboardCount,
    long mouseClickCount,
    long scrollCount,
    int appSwitchCount,
    bool crossMidnight,
    string message)
{
    var aggregator = new ActivityAggregator();
    var start = crossMidnight
        ? new DateTime(2026, 9, 8, 23, 59, 59)
        : new DateTime(2026, 9, 8, 10, 4, 59);
    var end = start.AddSeconds(2);
    aggregator.Add(new ActivityAggregationSample(
        start,
        end,
        new ActivityInputDelta(keyboardCount, mouseClickCount, 0, 0, scrollCount, 10),
        UserActivityState.Active,
        appSwitchCount,
        "devenv",
        WorkScheduleState.Working,
        ActivityMode.Computer,
        WorkTimeCategory.Normal,
        50,
        50));
    var dates = crossMidnight
        ? new[] { new DateOnly(2026, 9, 8), new DateOnly(2026, 9, 9) }
        : new[] { new DateOnly(2026, 9, 8) };
    var buckets = dates.SelectMany(aggregator.GetBuckets).ToArray();
    AssertEqual(buckets.Sum(static bucket => bucket.KeyboardCount), keyboardCount, message);
    AssertEqual(buckets.Sum(static bucket => bucket.MouseClickCount), mouseClickCount, message);
    AssertEqual(buckets.Sum(static bucket => bucket.ScrollCount), scrollCount, message);
    AssertEqual(buckets.Sum(static bucket => bucket.AppSwitchCount), appSwitchCount, message);
}

static void AssertEqual<T>(T actual, T expected, string message)
{
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
    {
        throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
    }
}

sealed class TestActivitySnapshotService : IActivitySnapshotService
{
    private readonly EmptyActivitySeriesProvider _empty = new();

    public TestActivitySnapshotService()
    {
        CurrentSnapshot = _empty.GetSnapshot(DateTime.Now);
    }

    public event EventHandler<ActivityDashboardSnapshot>? SnapshotUpdated
    {
        add { }
        remove { }
    }

    public ActivityDashboardSnapshot Current => CurrentSnapshot;

    public ActivityDashboardSnapshot CurrentSnapshot { get; set; }

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetSystemAvailableAsync(bool available, CancellationToken cancellationToken = default)
    {
        _ = available;
        return Task.CompletedTask;
    }

    public ActivityDashboardSnapshot GetSnapshot(DateTime now)
    {
        _ = now;
        return CurrentSnapshot;
    }

    public void Dispose()
    {
    }
}

sealed class TestReminderPresentationService : IReminderPresentationService
{
    public List<ReminderPresentationRequest> Requests { get; } = [];

    public void ApplySettings(ReminderPresentationSettings settings)
    {
        _ = settings;
    }

    public Task EnqueueAsync(ReminderPresentationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        return Task.CompletedTask;
    }

    public void SetSystemAvailable(bool available)
    {
        _ = available;
    }

    public void Dispose()
    {
    }
}
