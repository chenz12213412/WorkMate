using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WorkMate.Collectors;

public sealed class KeyboardActivityCollector : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;

    private readonly HookProc _hookCallback;
    private IntPtr _hookHandle;
    private long _keyPressCount;
    private bool _disposed;

    public KeyboardActivityCollector()
    {
        _hookCallback = OnKeyboardEvent;
    }

    public bool IsRunning => _hookHandle != IntPtr.Zero;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning)
        {
            return;
        }

        _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookCallback, GetModuleHandle(null), 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "键盘活动 Hook 初始化失败。");
        }
    }

    public long DrainCount() => Interlocked.Exchange(ref _keyPressCount, 0);

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

    private IntPtr OnKeyboardEvent(int code, IntPtr message, IntPtr data)
    {
        _ = data;
        if (code >= 0 && (message == (IntPtr)WmKeyDown || message == (IntPtr)WmSysKeyDown))
        {
            Interlocked.Increment(ref _keyPressCount);
        }

        return CallNextHookEx(_hookHandle, code, message, data);
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
