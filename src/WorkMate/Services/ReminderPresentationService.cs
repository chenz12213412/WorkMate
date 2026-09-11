using System.Windows;
using System.Windows.Threading;
using WorkMate.Infrastructure;
using WorkMate.Models;

namespace WorkMate.Services;

public interface IReminderPresentationService : IDisposable
{
    void ApplySettings(ReminderPresentationSettings settings);

    Task EnqueueAsync(ReminderPresentationRequest request, CancellationToken cancellationToken);

    void SetSystemAvailable(bool available);
}

public sealed class ReminderPresentationService : IReminderPresentationService
{
    public static readonly TimeSpan MinimumReminderGap = TimeSpan.FromMinutes(3);

    private readonly object _syncRoot = new();
    private readonly List<QueuedReminder> _queue = [];
    private readonly Dispatcher _dispatcher;
    private readonly SpeechService _speechService;
    private readonly IIconService _iconService;
    private readonly SpeechTemplateProvider _speechTemplateProvider = new();
    private readonly CancellationTokenSource _disposeTokenSource = new();
    private CancellationTokenSource? _queueDelayTokenSource;
    private ReminderPopupWindow? _currentPopup;
    private ReminderPriority? _currentPriority;
    private DateTimeOffset? _lastShownAt;
    private long _sequence;
    private int _processing;
    private bool _systemAvailable = true;
    private int _disposeState;
    private ReminderPresentationLifecycle? _currentLifecycle;
    private ReminderPresentationSettings _settings = ReminderPresentationSettings.Default;

    public ReminderPresentationService(
        Dispatcher dispatcher,
        SpeechService speechService,
        IIconService iconService)
    {
        _dispatcher = dispatcher;
        _speechService = speechService;
        _iconService = iconService;
    }

    public event Action<ReminderIconType>? ReminderStarted;

    public event Action? ReminderEnded;

    public void ApplySettings(ReminderPresentationSettings settings)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        _settings = settings;
        _speechService.UpdateSettings(
            settings.GlobalSpeechEnabled,
            settings.SpeechVolume,
            settings.SpeechRate,
            settings.SpeechVoiceName);
    }

    public Task EnqueueAsync(ReminderPresentationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposeState) != 0 || request.ExpiresAt <= DateTimeOffset.Now)
        {
            return Task.CompletedTask;
        }

        lock (_syncRoot)
        {
            if (_queue.Any(item => string.Equals(
                    item.Request.HistoryType,
                    request.HistoryType,
                    StringComparison.Ordinal)))
            {
                return Task.CompletedTask;
            }

            _queue.Add(new QueuedReminder(request, Interlocked.Increment(ref _sequence)));
            if (request.Priority == ReminderPriority.ScheduleCritical)
            {
                _queueDelayTokenSource?.Cancel();
                if (_currentPriority is { } priority && priority < ReminderPriority.ScheduleCritical)
                {
                    _currentLifecycle?.MarkPreempted();
                    _dispatcher.BeginInvoke(() =>
                    {
                        _speechService.Stop();
                        _currentPopup?.CloseForPreemption();
                    });
                }
            }
        }

        StartProcessor();
        return Task.CompletedTask;
    }

    public void SetSystemAvailable(bool available)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            _systemAvailable = available;
            if (!available)
            {
                if (_currentPriority is { } priority && priority < ReminderPriority.ScheduleCritical)
                {
                    _currentLifecycle?.MarkPreempted();
                }

                _speechService.Stop();
                _dispatcher.BeginInvoke(() => _currentPopup?.CloseForPreemption());
            }
        }

        if (available)
        {
            StartProcessor();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _disposeTokenSource.Cancel();
        lock (_syncRoot)
        {
            _queue.Clear();
            _queueDelayTokenSource?.Cancel();
        }

        _speechService.Stop();
        if (_dispatcher.CheckAccess())
        {
            _currentPopup?.CloseForPreemption();
        }
        else
        {
            _dispatcher.BeginInvoke(() => _currentPopup?.CloseForPreemption());
        }

        // 取消即可；后台处理器可能仍在使用这些 token，避免 Dispose 竞态。
    }

    private void StartProcessor()
    {
        if (Volatile.Read(ref _disposeState) != 0 || Interlocked.CompareExchange(ref _processing, 1, 0) != 0)
        {
            return;
        }

        _dispatcher.BeginInvoke(() => _ = ProcessQueueAsync());
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            while (Volatile.Read(ref _disposeState) == 0)
            {
                foreach (var expired in TakeExpiredRequests())
                {
                    await FinalizeExpiredAsync(expired, _disposeTokenSource.Token);
                }

                var request = TakeNextRequest();
                if (request is null)
                {
                    return;
                }

                if (!_systemAvailable)
                {
                    Requeue(request);
                    await DelayQueueAsync(TimeSpan.FromSeconds(5));
                    continue;
                }

                var now = DateTimeOffset.Now;
                if (request.ExpiresAt <= now)
                {
                    await FinalizeExpiredAsync(request, _disposeTokenSource.Token);
                    continue;
                }

                if (request.Priority != ReminderPriority.ScheduleCritical &&
                    _lastShownAt is { } lastShown &&
                    now - lastShown < MinimumReminderGap)
                {
                    Requeue(request);
                    await DelayQueueAsync(MinimumReminderGap - (now - lastShown));
                    continue;
                }

                await PresentAsync(request, _disposeTokenSource.Token);
            }
        }
        catch (OperationCanceledException) when (_disposeTokenSource.IsCancellationRequested)
        {
            // 正常退出。
        }
        catch (Exception exception)
        {
            FileLogger.Write(exception);
        }
        finally
        {
            Volatile.Write(ref _processing, 0);
            lock (_syncRoot)
            {
                if (Volatile.Read(ref _disposeState) == 0 && _queue.Count > 0)
                {
                    StartProcessor();
                }
            }
        }
    }

    private async Task PresentAsync(ReminderPresentationRequest request, CancellationToken cancellationToken)
    {
        var lifecycle = new ReminderPresentationLifecycle();
        _currentLifecycle = lifecycle;
        _currentPriority = request.Priority;
        _lastShownAt = DateTimeOffset.Now;
        var reminderIcon = request.Kind switch
        {
            ReminderPresentationKind.Drink => ReminderIconType.Drink,
            ReminderPresentationKind.Stand => ReminderIconType.Stand,
            ReminderPresentationKind.Cleaning => ReminderIconType.Cleaning,
            _ => (ReminderIconType?)null
        };
        if (reminderIcon is { } icon)
        {
            ReminderStarted?.Invoke(icon);
        }

        try
        {
            if (request.MarkShownAsync is not null)
            {
                await request.MarkShownAsync(_lastShownAt.Value, cancellationToken);
            }

            Task speechTask = Task.CompletedTask;
            if (request.PlaySpeech && ShouldPlaySpeech(request.Kind))
            {
                var speechText = _speechTemplateProvider.Resolve(request);
                if (!string.IsNullOrWhiteSpace(speechText))
                {
                    speechTask = _speechService.SpeakAsync(speechText, cancellationToken);
                }
            }

            var action = ReminderAction.Dismissed;
            if (request.ShowPopup)
            {
                var completion = new TaskCompletionSource<ReminderAction>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var popup = new ReminderPopupWindow(request, _iconService);
                _currentPopup = popup;
                popup.ActionSelected += (_, selectedAction) =>
                {
                    lifecycle.MarkActionSelected(selectedAction);
                    completion.TrySetResult(selectedAction);
                };
                popup.Show();
                action = await completion.Task.WaitAsync(cancellationToken);
                _currentPopup = null;
            }

            try
            {
                await speechTask;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                FileLogger.Write(exception);
            }

            action = lifecycle.Resolve(action, request.ExpiresAt, DateTimeOffset.Now);

            if (request.HandleActionAsync is not null)
            {
                await request.HandleActionAsync(action, cancellationToken);
            }

            if (action == ReminderAction.Preempted && request.ExpiresAt > DateTimeOffset.Now)
            {
                Requeue(request);
            }
        }
        finally
        {
            if (reminderIcon is not null)
            {
                ReminderEnded?.Invoke();
            }

            _currentLifecycle = null;
            _currentPriority = null;
        }
    }

    private IReadOnlyList<ReminderPresentationRequest> TakeExpiredRequests()
    {
        lock (_syncRoot)
        {
            var now = DateTimeOffset.Now;
            var expired = _queue
                .Where(item => item.Request.ExpiresAt <= now)
                .Select(static item => item.Request)
                .ToArray();
            if (expired.Length > 0)
            {
                _queue.RemoveAll(item => item.Request.ExpiresAt <= now);
            }

            return expired;
        }
    }

    private static async Task FinalizeExpiredAsync(
        ReminderPresentationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.HandleActionAsync is not null)
        {
            await request.HandleActionAsync(ReminderAction.Expired, cancellationToken);
        }
    }

    private ReminderPresentationRequest? TakeNextRequest()
    {
        lock (_syncRoot)
        {
            var next = _queue
                .OrderByDescending(static item => item.Request.Priority)
                .ThenBy(static item => item.Sequence)
                .FirstOrDefault();
            if (next is null)
            {
                return null;
            }

            _queue.Remove(next);
            return next.Request;
        }
    }

    private bool ShouldPlaySpeech(ReminderPresentationKind kind)
    {
        return kind switch
        {
            ReminderPresentationKind.Cleaning => _settings.CleaningSpeechEnabled,
            ReminderPresentationKind.LunchSoon or
                ReminderPresentationKind.LunchStart or
                ReminderPresentationKind.AfternoonStart or
                ReminderPresentationKind.OffWork => _settings.ScheduleSpeechEnabled,
            _ => true
        };
    }

    private void Requeue(ReminderPresentationRequest request)
    {
        if (Volatile.Read(ref _disposeState) != 0 || request.ExpiresAt <= DateTimeOffset.Now)
        {
            return;
        }

        lock (_syncRoot)
        {
            _queue.Add(new QueuedReminder(request, Interlocked.Increment(ref _sequence)));
        }
    }

    private async Task DelayQueueAsync(TimeSpan delay)
    {
        if (delay < TimeSpan.FromMilliseconds(100))
        {
            return;
        }

        using var delayTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            _disposeTokenSource.Token);
        lock (_syncRoot)
        {
            _queueDelayTokenSource?.Cancel();
            _queueDelayTokenSource = delayTokenSource;
        }

        try
        {
            await Task.Delay(delay, delayTokenSource.Token);
        }
        catch (OperationCanceledException) when (!_disposeTokenSource.IsCancellationRequested)
        {
            // 更高优先级提醒到达，立即重新选择队列。
        }
        finally
        {
            lock (_syncRoot)
            {
                if (ReferenceEquals(_queueDelayTokenSource, delayTokenSource))
                {
                    _queueDelayTokenSource = null;
                }
            }
        }
    }

    private sealed record QueuedReminder(ReminderPresentationRequest Request, long Sequence);
}

public sealed class ReminderPresentationLifecycle
{
    private int _preempted;
    private int _userActionSelected;

    public void MarkPreempted() => Interlocked.Exchange(ref _preempted, 1);

    public void MarkActionSelected(ReminderAction action)
    {
        if (action != ReminderAction.Preempted)
        {
            Interlocked.Exchange(ref _userActionSelected, 1);
        }
    }

    public ReminderAction Resolve(
        ReminderAction action,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        if (Volatile.Read(ref _preempted) != 0 &&
            Volatile.Read(ref _userActionSelected) == 0)
        {
            action = ReminderAction.Preempted;
        }

        return action == ReminderAction.Preempted && expiresAt <= now
            ? ReminderAction.Expired
            : action;
    }
}
