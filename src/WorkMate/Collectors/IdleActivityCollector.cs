using System.ComponentModel;
using System.Runtime.InteropServices;
using WorkMate.Models;

namespace WorkMate.Collectors;

public sealed class IdleActivityCollector
{
    private readonly ActivityThresholds _thresholds;

    public IdleActivityCollector(ActivityThresholds? thresholds = null)
    {
        _thresholds = thresholds ?? ActivityThresholds.Default;
    }

    public IdleActivitySnapshot GetSnapshot()
    {
        var input = new LastInputInfo
        {
            Size = (uint)Marshal.SizeOf<LastInputInfo>()
        };
        if (!GetLastInputInfo(ref input))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 Windows 最后输入时间。");
        }

        var elapsedMilliseconds = unchecked(GetTickCount() - input.TickCount);
        var idleDuration = TimeSpan.FromMilliseconds(elapsedMilliseconds);
        return new IdleActivitySnapshot(idleDuration, _thresholds.Classify(idleDuration));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint TickCount;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    [DllImport("kernel32.dll")]
    private static extern uint GetTickCount();
}
