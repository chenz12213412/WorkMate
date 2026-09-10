using WorkMate.Infrastructure;
using WorkMate.Models;

namespace WorkMate.Services;

public sealed class GreetingService
{
    private readonly DatabaseStore _database;
    private readonly ScheduleEngine _scheduleEngine;
    private readonly SpeechService _speechService;

    public GreetingService(
        DatabaseStore database,
        ScheduleEngine scheduleEngine,
        SpeechService speechService)
    {
        _database = database;
        _scheduleEngine = scheduleEngine;
        _speechService = speechService;
    }

    public async Task RunStartupGreetingAsync(CancellationToken cancellationToken)
    {
        try
        {
            var delay = TimeSpan.FromSeconds(Random.Shared.Next(5, 16));
            await Task.Delay(delay, cancellationToken);

            var now = DateTime.Now;
            var greeting = CreateGreeting(now, _scheduleEngine.GetSnapshot(now));

            // 先原子占用今日问候名额，保证崩溃重启时也不会连续播报。
            var shouldGreet = await _database.TryClaimDailyGreetingAsync(
                DateOnly.FromDateTime(now),
                greeting,
                cancellationToken);
            if (!shouldGreet)
            {
                return;
            }

            await _speechService.SpeakAsync(greeting, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常退出。
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
        }
    }

    internal static string CreateGreeting(DateTime now, ScheduleSnapshot snapshot)
    {
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return Pick(GreetingTemplates.WeekendGreeting);
        }

        return snapshot.State switch
        {
            WorkScheduleState.BeforeWork => Pick(GreetingTemplates.BeforeWorkGreeting),
            WorkScheduleState.Working => Pick(GreetingTemplates.WorkingGreeting),
            WorkScheduleState.Lunch => Pick(GreetingTemplates.LunchGreeting),
            _ => Pick(GreetingTemplates.AfterWorkGreeting)
        };
    }

    private static string Pick(IReadOnlyList<string> templates)
    {
        return templates[Random.Shared.Next(templates.Count)];
    }

}
