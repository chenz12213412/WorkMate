using System.Text.Json;
using WorkMate.Infrastructure;
using WorkMate.Models;

namespace WorkMate.Services;

public sealed class ScheduleSettingsStore
{
    private const string SettingsKey = "ScheduleSettings";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);
    private readonly DatabaseStore _database;

    public ScheduleSettingsStore(DatabaseStore database)
    {
        _database = database;
    }

    public async Task<ScheduleSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var json = await _database.GetSettingAsync(SettingsKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var settings = JsonSerializer.Deserialize<ScheduleSettings>(json, JsonOptions);
                if (settings is not null && settings.TryValidate(out _))
                {
                    return settings;
                }
            }
            catch (Exception exception)
            {
                FileLogger.Write(exception);
            }
        }

        var defaults = ScheduleDefaults.Create();
        await SaveAsync(defaults, cancellationToken);
        return defaults;
    }

    public Task SaveAsync(ScheduleSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.TryValidate(out var error))
        {
            throw new ArgumentException(error, nameof(settings));
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        return _database.SetSettingAsync(SettingsKey, json, cancellationToken);
    }
}
