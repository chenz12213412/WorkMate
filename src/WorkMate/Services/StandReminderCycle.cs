namespace WorkMate.Services;

public sealed class StandReminderCycle
{
    private double _activeSeconds;
    private bool _reminderPending;

    public double ActiveSeconds => _activeSeconds;

    public bool Advance(TimeSpan elapsed, bool shouldAccumulate, TimeSpan interval)
    {
        if (!shouldAccumulate || elapsed <= TimeSpan.Zero || _reminderPending)
        {
            return false;
        }

        _activeSeconds += elapsed.TotalSeconds;
        if (_activeSeconds < interval.TotalSeconds)
        {
            return false;
        }

        _reminderPending = true;
        return true;
    }

    public void Complete() => Reset();

    public void Expire() => Reset();

    public void Reset()
    {
        _activeSeconds = 0;
        _reminderPending = false;
    }
}
