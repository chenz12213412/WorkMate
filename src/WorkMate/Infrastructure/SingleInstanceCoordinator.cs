using System.Security.Principal;
using System.Windows.Threading;

namespace WorkMate.Infrastructure;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly EventWaitHandle _dashboardEvent;
    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private RegisteredWaitHandle? _registeredWait;
    private CancellationTokenRegistration _cancellationRegistration;
    private bool _disposed;

    private SingleInstanceCoordinator(EventWaitHandle dashboardEvent, Mutex mutex, bool ownsMutex)
    {
        _dashboardEvent = dashboardEvent;
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    public bool IsPrimaryInstance => _ownsMutex;

    public static SingleInstanceCoordinator Create()
    {
        var userSid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var nameSuffix = userSid.Replace('\\', '_');
        var dashboardEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            $"Local\\WorkMate.ShowDashboard.{nameSuffix}");

        var mutex = new Mutex(
            true,
            $"Local\\WorkMate.SingleInstance.{nameSuffix}",
            out var createdNew);

        return new SingleInstanceCoordinator(dashboardEvent, mutex, createdNew);
    }

    public void RequestDashboard()
    {
        _dashboardEvent.Set();
    }

    public void StartDashboardListener(
        Dispatcher dispatcher,
        Action showDashboard,
        CancellationToken cancellationToken)
    {
        if (!_ownsMutex)
        {
            return;
        }

        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _dashboardEvent,
            (_, _) =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    dispatcher.BeginInvoke(showDashboard);
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        _cancellationRegistration = cancellationToken.Register(RequestDashboard);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellationRegistration.Dispose();
        _registeredWait?.Unregister(null);
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
        _dashboardEvent.Dispose();
    }
}
