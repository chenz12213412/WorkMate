using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WorkMate.Models;

namespace WorkMate.Collectors;

public sealed class ForegroundWindowCollector : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;

    private readonly object _syncRoot = new();
    private readonly WinEventProc _eventCallback;
    private IntPtr _eventHook;
    private ForegroundAppSnapshot? _current;
    private readonly List<ForegroundUsageDelta> _pendingUsage = [];
    private int _switchCount;
    private bool _disposed;

    public ForegroundWindowCollector()
    {
        _eventCallback = OnForegroundChanged;
    }

    public bool IsRunning => _eventHook != IntPtr.Zero;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning)
        {
            return;
        }

        _eventHook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _eventCallback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);
        if (_eventHook == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "前台程序事件 Hook 初始化失败。");
        }

        UpdateForeground(GetForegroundWindow(), countSwitch: false);
    }

    public ForegroundAppSnapshot? GetCurrent()
    {
        lock (_syncRoot)
        {
            return _current;
        }
    }

    public int DrainSwitchCount() => Interlocked.Exchange(ref _switchCount, 0);

    public IReadOnlyList<ForegroundUsageDelta> DrainUsage(DateTimeOffset now)
    {
        lock (_syncRoot)
        {
            AccumulateCurrentUsage(now);
            if (_current is not null)
            {
                _current = _current with { ForegroundStart = now };
            }

            var result = _pendingUsage.ToArray();
            _pendingUsage.Clear();
            return result;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_eventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_eventHook);
            _eventHook = IntPtr.Zero;
        }
    }

    private void OnForegroundChanged(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        _ = hook;
        _ = eventType;
        _ = objectId;
        _ = childId;
        _ = eventThread;
        _ = eventTime;
        UpdateForeground(window, countSwitch: true);
    }

    private void UpdateForeground(IntPtr window, bool countSwitch)
    {
        if (window == IntPtr.Zero)
        {
            return;
        }

        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return;
        }

        string processName;
        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            processName = process.ProcessName;
        }
        catch
        {
            processName = $"pid-{processId}";
        }

        var titleBuffer = new StringBuilder(512);
        _ = GetWindowText(window, titleBuffer, titleBuffer.Capacity);
        var timestamp = DateTimeOffset.Now;
        var next = new ForegroundAppSnapshot(
            unchecked((int)processId),
            processName,
            titleBuffer.ToString(),
            timestamp);

        lock (_syncRoot)
        {
            AccumulateCurrentUsage(timestamp);
            if (countSwitch && _current is not null && _current.ProcessId != next.ProcessId)
            {
                Interlocked.Increment(ref _switchCount);
            }

            _current = next;
        }
    }

    private void AccumulateCurrentUsage(DateTimeOffset end)
    {
        if (_current is null || end <= _current.ForegroundStart)
        {
            return;
        }

        _pendingUsage.Add(new ForegroundUsageDelta(
            _current.ProcessName,
            _current.ForegroundStart,
            end));
    }

    private delegate void WinEventProc(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr eventHookModule,
        WinEventProc eventProc,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr eventHook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);
}
