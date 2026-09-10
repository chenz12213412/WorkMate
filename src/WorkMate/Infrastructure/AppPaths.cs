using System.IO;

namespace WorkMate.Infrastructure;

internal static class AppPaths
{
    private static readonly string RootDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkMate");

    public static string DatabasePath => Path.Combine(RootDirectory, "workmate.db");

    public static string LogPath => Path.Combine(RootDirectory, "workmate.log");

    public static void EnsureRootDirectory()
    {
        Directory.CreateDirectory(RootDirectory);
    }
}
