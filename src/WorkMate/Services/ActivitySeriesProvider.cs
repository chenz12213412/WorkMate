using WorkMate.Models;

namespace WorkMate.Services;

public interface IActivitySeriesProvider
{
    ActivityDashboardSnapshot GetSnapshot(DateTime now);
}

public sealed class EmptyActivitySeriesProvider : IActivitySeriesProvider
{
    private static readonly ActivityDashboardSnapshot EmptySnapshot = new(
        null,
        null,
        null,
        null,
        null,
        [],
        TimeSpan.Zero,
        TimeSpan.Zero,
        0,
        null,
        0,
        UserActivityState.Afk);

    public ActivityDashboardSnapshot GetSnapshot(DateTime now)
    {
        _ = now;
        return EmptySnapshot;
    }
}
