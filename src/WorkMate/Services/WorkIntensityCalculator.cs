using WorkMate.Models;

namespace WorkMate.Services;

public sealed class WorkIntensityCalculator
{
    public const double EmaAlpha = 0.25;

    private double? _smoothedActivity;
    private double? _smoothedIntensity;

    public ActivityIntensityResult Calculate(ActivityIntensityInput input)
    {
        var activity = CalculateRawActivity(input);
        var intensity = CalculateRawIntensity(input, activity);
        _smoothedActivity = ApplyEma(_smoothedActivity, activity);
        _smoothedIntensity = ApplyEma(_smoothedIntensity, intensity);
        return new ActivityIntensityResult(
            ClampAndRound(_smoothedActivity.Value),
            ClampAndRound(_smoothedIntensity.Value));
    }

    public void Reset()
    {
        _smoothedActivity = null;
        _smoothedIntensity = null;
    }

    public static double CalculateRawActivity(ActivityIntensityInput input)
    {
        if (input.Interval <= TimeSpan.Zero || input.UserState == UserActivityState.Afk)
        {
            return 0;
        }

        var minutes = Math.Max(input.Interval.TotalMinutes, 1d / 60d);
        var keyboard = Normalize(input.Input.KeyboardCount / minutes, 120);
        var clicks = Normalize(input.Input.MouseClickCount / minutes, 50);
        var movement = Normalize(input.Input.MouseMoveDistance / minutes, 15000);
        var scroll = Normalize(input.Input.WheelCount / minutes, 20);
        var raw = 100 * ((0.45 * keyboard) + (0.25 * clicks) + (0.20 * movement) + (0.10 * scroll));
        return input.UserState == UserActivityState.Idle ? raw * 0.35 : raw;
    }

    public static double CalculateRawIntensity(ActivityIntensityInput input, double activityScore)
    {
        var minutes = Math.Max(input.Interval.TotalMinutes, 1d / 60d);
        var continuous = Math.Clamp(input.ContinuousWorkDuration.TotalMinutes / 50d, 0, 1);
        var activeRatio = Math.Clamp(input.RecentActiveRatio, 0, 1);
        var contextSwitching = Normalize(input.AppSwitchCount / minutes, 1.2);
        return (0.40 * activityScore) +
               (25 * continuous) +
               (20 * activeRatio) +
               (15 * contextSwitching);
    }

    private static double Normalize(double value, double fullScale) =>
        Math.Clamp(value / fullScale, 0, 1);

    private static double ApplyEma(double? previous, double current) =>
        previous is null ? current : (EmaAlpha * current) + ((1 - EmaAlpha) * previous.Value);

    private static int ClampAndRound(double value) =>
        (int)Math.Round(Math.Clamp(value, 0, 100));
}
