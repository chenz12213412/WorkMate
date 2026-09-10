using System.Text.Json;
using WorkMate.Infrastructure;
using WorkMate.Models;

namespace WorkMate.Services;

public sealed class CleaningReminderSettingsStore
{
    private const string SettingsKey = "CleaningReminderSettings";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);
    private readonly DatabaseStore _database;

    public CleaningReminderSettingsStore(DatabaseStore database)
    {
        _database = database;
    }

    public async Task<CleaningReminderSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var json = await _database.GetSettingAsync(SettingsKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var settings = JsonSerializer.Deserialize<CleaningReminderSettings>(json, JsonOptions);
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

        await SaveAsync(CleaningReminderSettings.Default, cancellationToken);
        return CleaningReminderSettings.Default;
    }

    public Task SaveAsync(CleaningReminderSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.TryValidate(out var error))
        {
            throw new ArgumentException(error, nameof(settings));
        }

        return _database.SetSettingAsync(
            SettingsKey,
            JsonSerializer.Serialize(settings, JsonOptions),
            cancellationToken);
    }
}
