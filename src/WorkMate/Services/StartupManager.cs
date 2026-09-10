using Microsoft.Win32;

namespace WorkMate.Services;

public sealed class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WorkMate";
    private const string InitializedSetting = "AutoStartInitialized";
    private const string EnabledSetting = "AutoStartEnabled";

    private readonly DatabaseStore _database;

    public StartupManager(DatabaseStore database)
    {
        _database = database;
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public async Task ApplyFirstRunDefaultAsync(CancellationToken cancellationToken)
    {
        var initialized = await _database.GetSettingAsync(InitializedSetting, cancellationToken);
        if (!string.Equals(initialized, "true", StringComparison.OrdinalIgnoreCase))
        {
            await SetEnabledAsync(true, cancellationToken);
            await _database.SetSettingAsync(InitializedSetting, "true", cancellationToken);
            return;
        }

        var enabled = await _database.GetSettingAsync(EnabledSetting, cancellationToken);
        if (string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            WriteRunEntry();
        }
        else
        {
            DeleteRunEntry();
        }
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (enabled)
        {
            WriteRunEntry();
        }
        else
        {
            DeleteRunEntry();
        }

        await _database.SetSettingAsync(EnabledSetting, enabled ? "true" : "false", cancellationToken);
    }

    private static void WriteRunEntry()
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法获取 WorkMate.exe 的路径。 ");
        var command = $"\"{executablePath}\" --startup";

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户的开机启动设置。 ");
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    private static void DeleteRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
