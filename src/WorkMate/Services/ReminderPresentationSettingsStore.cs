using System.Text.Json;
using WorkMate.Infrastructure;
using WorkMate.Models;

namespace WorkMate.Services;

public sealed class ReminderPresentationSettingsStore
{
    private const string SettingsKey = "ReminderPresentationSettings";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);
    private readonly DatabaseStore _database;

    public ReminderPresentationSettingsStore(DatabaseStore database)
    {
        _database = database;
    }

    public async Task<ReminderPresentationSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var json = await _database.GetSettingAsync(SettingsKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var settings = JsonSerializer.Deserialize<ReminderPresentationSettings>(json, JsonOptions);
                if (settings is not null)
                {
                    var migrated = settings with
                    {
                        SpeechRate = Math.Clamp(settings.SpeechRate, -1, 1),
                        SpeechVoiceName = string.IsNullOrWhiteSpace(settings.SpeechVoiceName)
                            ? null
                            : settings.SpeechVoiceName.Trim()
                    };
                    if (migrated.TryValidate(out _))
                    {
                        return migrated;
                    }
                }
            }
            catch (Exception exception)
            {
                FileLogger.Write(exception);
            }
        }

        await SaveAsync(ReminderPresentationSettings.Default, cancellationToken);
        return ReminderPresentationSettings.Default;
    }

    public Task SaveAsync(ReminderPresentationSettings settings, CancellationToken cancellationToken)
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
