using System.Text.Json;
using WorkMate.Models;

namespace WorkMate.Services;

public sealed class ActivityTrackingSettingsStore
{
    private const string SettingsKey = "activity-tracking-settings-v1";
    private readonly DatabaseStore _database;

    public ActivityTrackingSettingsStore(DatabaseStore database)
    {
        _database = database;
    }

    public async Task<ActivityTrackingSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var json = await _database.GetSettingAsync(SettingsKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return ActivityTrackingSettings.Default;
        }

        try
        {
            return JsonSerializer.Deserialize<ActivityTrackingSettings>(json)
                   ?? ActivityTrackingSettings.Default;
        }
        catch (JsonException)
        {
            return ActivityTrackingSettings.Default;
        }
    }

    public Task SaveAsync(ActivityTrackingSettings settings, CancellationToken cancellationToken) =>
        _database.SetSettingAsync(SettingsKey, JsonSerializer.Serialize(settings), cancellationToken);
}
