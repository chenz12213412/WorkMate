using System.IO;
using System.Text;

namespace WorkMate.Infrastructure;

internal static class FileLogger
{
    private static readonly object SyncRoot = new();

    public static void Write(Exception exception)
    {
        try
        {
            AppPaths.EnsureRootDirectory();
            var line = $"{DateTimeOffset.Now:O} {exception}\r\n";

            lock (SyncRoot)
            {
                File.AppendAllText(AppPaths.LogPath, line, new UTF8Encoding(false));
            }
        }
        catch
        {
            // 日志写入失败不应导致常驻程序再次崩溃。
        }
    }
}
