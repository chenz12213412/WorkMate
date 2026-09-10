using System.ComponentModel;
using System.Runtime.InteropServices;
using WorkMate.Models;

namespace WorkMate.Collectors;

public sealed class MouseActivityCollector : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmRightButtonDown = 0x0204;
    private const int WmMiddleButtonDown = 0x0207;
    private const int WmMouseWheel = 0x020A;
    private const int WmMouseHorizontalWheel = 0x020E;

    private readonly HookProc _hookCallback;
    private readonly object _positionLock = new();
    private IntPtr _hookHandle;
    private Point _lastPosition;
    private bool _hasLastPosition;
    private long _leftClickCount;
    private long _rightClickCount;
    private long _middleClickCount;
    private long _wheelCount;
    private long _distanceMilliPixels;
    private bool _disposed;

    public MouseActivityCollector()
    {
        _hookCallback = OnMouseEvent;
    }

    public bool IsRunning => _hookHandle != IntPtr.Zero;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning)
        {
            return;
        }

        _hookHandle = SetWindowsHookEx(WhMouseLl, _hookCallback, GetModuleHandle(null), 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "鼠标活动 Hook 初始化失败。");
        }
    }

    public ActivityInputDelta Drain()
    {
        return new ActivityInputDelta(
            0,
            Interlocked.Exchange(ref _leftClickCount, 0),
            Interlocked.Exchange(ref _rightClickCount, 0),
            Interlocked.Exchange(ref _middleClickCount, 0),
            Interlocked.Exchange(ref _wheelCount, 0),
            Interlocked.Exchange(ref _distanceMilliPixels, 0) / 1000d);
    }

    public void ResetPosition()
    {
        lock (_positionLock)
        {
            _hasLastPosition = false;
            _lastPosition = default;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr OnMouseEvent(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var messageId = unchecked((int)message.ToInt64());
            switch (messageId)
            {
                case WmLeftButtonDown:
                    Interlocked.Increment(ref _leftClickCount);
                    break;
                case WmRightButtonDown:
                    Interlocked.Increment(ref _rightClickCount);
                    break;
                case WmMiddleButtonDown:
                    Interlocked.Increment(ref _middleClickCount);
                    break;
                case WmMouseWheel:
                case WmMouseHorizontalWheel:
                    Interlocked.Increment(ref _wheelCount);
                    break;
                case WmMouseMove:
                    RecordMouseMove(Marshal.PtrToStructure<LowLevelMouseData>(data).Position);
                    break;
            }
        }

        return CallNextHookEx(_hookHandle, code, message, data);
    }

    private void RecordMouseMove(Point current)
    {
        lock (_positionLock)
        {
            if (_hasLastPosition)
            {
                var deltaX = (double)current.X - _lastPosition.X;
                var deltaY = (double)current.Y - _lastPosition.Y;
                var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
                Interlocked.Add(ref _distanceMilliPixels, (long)Math.Round(distance * 1000d));
            }

            _lastPosition = current;
            _hasLastPosition = true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Point
    {
        public readonly int X;
        public readonly int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct LowLevelMouseData
    {
        public readonly Point Position;
        public readonly uint MouseData;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nuint ExtraInfo;
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
