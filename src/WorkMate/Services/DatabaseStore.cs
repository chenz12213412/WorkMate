using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using WorkMate.Models;

namespace WorkMate.Services;

public sealed class DatabaseStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public DatabaseStore(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath)
            ?? throw new ArgumentException("数据库路径无效。", nameof(databasePath));
        Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            DefaultTimeout = 5
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000;";
            await pragma.ExecuteNonQueryAsync(cancellationToken);

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS app_settings (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS daily_greetings (
                    greeting_date TEXT PRIMARY KEY,
                    greeted_at TEXT NOT NULL,
                    template TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS reminder_history (
                    reminder_type TEXT NOT NULL,
                    reminder_date TEXT NOT NULL,
                    first_triggered_at TEXT NULL,
                    last_triggered_at TEXT NULL,
                    status TEXT NOT NULL,
                    snooze_count INTEGER NOT NULL DEFAULT 0,
                    next_due_at TEXT NULL,
                    message TEXT NULL,
                    shown_at TEXT NULL,
                    action TEXT NULL,
                    PRIMARY KEY (reminder_type, reminder_date)
                );

                CREATE TABLE IF NOT EXISTS activity_buckets (
                    bucket_start TEXT PRIMARY KEY,
                    bucket_end TEXT NOT NULL,
                    keyboard_count INTEGER NOT NULL,
                    mouse_click_count INTEGER NOT NULL,
                    mouse_distance REAL NOT NULL,
                    scroll_count INTEGER NOT NULL,
                    active_seconds REAL NOT NULL,
                    idle_seconds REAL NOT NULL,
                    afk_seconds REAL NOT NULL,
                    app_switch_count INTEGER NOT NULL,
                    dominant_process TEXT NULL,
                    schedule_state TEXT NOT NULL,
                    work_mode TEXT NOT NULL,
                    normal_work_seconds REAL NOT NULL,
                    overtime_seconds REAL NOT NULL,
                    manual_work_seconds REAL NOT NULL,
                    lab_seconds REAL NOT NULL,
                    meeting_seconds REAL NOT NULL,
                    activity_score REAL NOT NULL,
                    work_intensity REAL NOT NULL,
                    longest_continuous_seconds REAL NOT NULL DEFAULT 0,
                    updated_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_activity_buckets_end
                    ON activity_buckets(bucket_end);

                CREATE TABLE IF NOT EXISTS app_usage_daily (
                    usage_date TEXT NOT NULL,
                    process_name TEXT NOT NULL,
                    foreground_seconds REAL NOT NULL,
                    active_foreground_seconds REAL NOT NULL,
                    updated_at TEXT NOT NULL,
                    PRIMARY KEY (usage_date, process_name)
                );

                CREATE TABLE IF NOT EXISTS daily_summaries (
                    summary_date TEXT PRIMARY KEY,
                    normal_work_seconds REAL NOT NULL,
                    overtime_seconds REAL NOT NULL,
                    lab_seconds REAL NOT NULL,
                    meeting_seconds REAL NOT NULL,
                    total_work_seconds REAL NOT NULL,
                    active_computer_seconds REAL NOT NULL,
                    afk_seconds REAL NOT NULL,
                    keyboard_count INTEGER NOT NULL,
                    mouse_click_count INTEGER NOT NULL,
                    mouse_distance REAL NOT NULL,
                    scroll_count INTEGER NOT NULL,
                    app_switch_count INTEGER NOT NULL,
                    average_work_intensity INTEGER NOT NULL,
                    longest_continuous_seconds REAL NOT NULL,
                    top_applications_json TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            var schemaVersion = await GetSchemaVersionAsync(connection, transaction, cancellationToken);
            if (schemaVersion < 1)
            {
                await SetSchemaVersionAsync(connection, transaction, 1, cancellationToken);
                schemaVersion = 1;
            }

            if (schemaVersion < 2)
            {
                await EnsureColumnAsync(
                    connection,
                    transaction,
                    "activity_buckets",
                    "longest_continuous_seconds",
                    "REAL NOT NULL DEFAULT 0",
                    cancellationToken);
                await SetSchemaVersionAsync(connection, transaction, 2, cancellationToken);
                schemaVersion = 2;
            }

            if (schemaVersion < 3)
            {
                await EnsureColumnAsync(
                    connection,
                    transaction,
                    "reminder_history",
                    "shown_at",
                    "TEXT NULL",
                    cancellationToken);
                await EnsureColumnAsync(
                    connection,
                    transaction,
                    "reminder_history",
                    "action",
                    "TEXT NULL",
                    cancellationToken);
                await SetSchemaVersionAsync(connection, transaction, 3, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private static async Task<int> GetSchemaVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task SetSchemaVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA user_version = {version};";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        string declaration,
        CancellationToken cancellationToken)
    {
        var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await check.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await reader.DisposeAsync();
        var alter = connection.CreateCommand();
        alter.Transaction = transaction;
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {declaration};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }


    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_settings WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_settings (key, value, updated_at)
            VALUES ($key, $value, $updatedAt)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryClaimDailyGreetingAsync(
        DateOnly date,
        string greeting,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO daily_greetings (greeting_date, greeted_at, template)
            VALUES ($date, $greetedAt, $template);
            SELECT changes();
            """;
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$greetedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$template", greeting);

        var changes = Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        return changes == 1;
    }

    public async Task<ReminderHistoryEntry?> GetReminderHistoryAsync(
        string reminderType,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, snooze_count, next_due_at, message, shown_at, action
            FROM reminder_history
            WHERE reminder_type = $type AND reminder_date = $date
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$type", reminderType);
        command.Parameters.AddWithValue("$date", FormatDate(date));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var status = Enum.TryParse<ReminderHistoryStatus>(reader.GetString(0), out var parsedStatus)
            ? parsedStatus
            : ReminderHistoryStatus.Expired;
        DateTimeOffset? nextDueAt = reader.IsDBNull(2)
            ? null
            : DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
        return new ReminderHistoryEntry(
            reminderType,
            date,
            status,
            reader.GetInt32(1),
            nextDueAt,
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4)
                ? null
                : DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    public async Task<IReadOnlyList<ReminderHistoryEntry>> GetSnoozedOrdinaryRemindersAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT reminder_type, status, snooze_count, next_due_at, message, shown_at, action
            FROM reminder_history
            WHERE reminder_date = $date
              AND status = 'Snoozed'
              AND (reminder_type LIKE 'Drink:%' OR reminder_type LIKE 'Stand:%');
            """;
        command.Parameters.AddWithValue("$date", FormatDate(date));
        var result = new List<ReminderHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ReminderHistoryEntry(
                reader.GetString(0),
                date,
                ReminderHistoryStatus.Snoozed,
                reader.GetInt32(2),
                reader.IsDBNull(3)
                    ? null
                    : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5)
                    ? null
                    : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return result;
    }

    public async Task RecordReminderShownAsync(
        string reminderType,
        DateOnly date,
        DateTimeOffset shownAt,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE reminder_history
            SET shown_at = $shownAt, last_triggered_at = $shownAt
            WHERE reminder_type = $type AND reminder_date = $date;
            """;
        command.Parameters.AddWithValue("$type", reminderType);
        command.Parameters.AddWithValue("$date", FormatDate(date));
        command.Parameters.AddWithValue("$shownAt", FormatTimestamp(shownAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordReminderActionAsync(
        string reminderType,
        DateOnly date,
        string action,
        ReminderHistoryStatus status,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE reminder_history
            SET action = $action,
                status = $status,
                last_triggered_at = $updatedAt,
                next_due_at = CASE WHEN $status = 'Snoozed' THEN next_due_at ELSE NULL END
            WHERE reminder_type = $type AND reminder_date = $date;
            """;
        command.Parameters.AddWithValue("$type", reminderType);
        command.Parameters.AddWithValue("$date", FormatDate(date));
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(updatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryClaimReminderAsync(
        string reminderType,
        DateOnly date,
        DateTimeOffset triggeredAt,
        string message,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO reminder_history (
                reminder_type, reminder_date, first_triggered_at, last_triggered_at,
                status, snooze_count, next_due_at, message)
            VALUES ($type, $date, $at, $at, 'Triggered', 0, NULL, $message);
            SELECT changes();
            """;
        command.Parameters.AddWithValue("$type", reminderType);
        command.Parameters.AddWithValue("$date", FormatDate(date));
        command.Parameters.AddWithValue("$at", FormatTimestamp(triggeredAt));
        command.Parameters.AddWithValue("$message", message);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }

    public async Task<bool> TrySnoozeReminderAsync(
        string reminderType,
        DateOnly date,
        DateTimeOffset nextDueAt,
        int maximumSnoozes,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE reminder_history
            SET status = 'Snoozed',
                snooze_count = snooze_count + 1,
                next_due_at = $nextDueAt
            WHERE reminder_type = $type
              AND reminder_date = $date
              AND status IN ('Triggered', 'Snoozed')
              AND snooze_count < $maximumSnoozes;
            SELECT changes();
            """;
        command.Parameters.AddWithValue("$type", reminderType);
        command.Parameters.AddWithValue("$date", FormatDate(date));
        command.Parameters.AddWithValue("$nextDueAt", FormatTimestamp(nextDueAt));
        command.Parameters.AddWithValue("$maximumSnoozes", maximumSnoozes);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }

    public async Task<bool> TryConsumeSnoozedReminderAsync(
        string reminderType,
        DateOnly date,
        DateTimeOffset triggeredAt,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE reminder_history
            SET status = 'Triggered', last_triggered_at = $at, next_due_at = NULL
            WHERE reminder_type = $type
              AND reminder_date = $date
              AND status = 'Snoozed'
              AND next_due_at <= $at;
            SELECT changes();
            """;
        command.Parameters.AddWithValue("$type", reminderType);
        command.Parameters.AddWithValue("$date", FormatDate(date));
        command.Parameters.AddWithValue("$at", FormatTimestamp(triggeredAt));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }

    public async Task SetReminderStatusAsync(
        string reminderType,
        DateOnly date,
        ReminderHistoryStatus status,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO reminder_history (
                reminder_type, reminder_date, first_triggered_at, last_triggered_at,
                status, snooze_count, next_due_at, message)
            VALUES ($type, $date, NULL, $at, $status, 0, NULL, NULL)
            ON CONFLICT(reminder_type, reminder_date) DO UPDATE SET
                status = excluded.status,
                last_triggered_at = excluded.last_triggered_at,
                next_due_at = NULL;
            """;
        command.Parameters.AddWithValue("$type", reminderType);
        command.Parameters.AddWithValue("$date", FormatDate(date));
        command.Parameters.AddWithValue("$at", FormatTimestamp(updatedAt));
        command.Parameters.AddWithValue("$status", status.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertActivityBucketsAsync(
        IEnumerable<ActivityBucket> buckets,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        var items = buckets.GroupBy(static bucket => bucket.BucketStart).Select(static group => group.Last()).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        foreach (var bucket in items)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO activity_buckets (
                    bucket_start, bucket_end, keyboard_count, mouse_click_count,
                    mouse_distance, scroll_count, active_seconds, idle_seconds, afk_seconds,
                    app_switch_count, dominant_process, schedule_state, work_mode,
                    normal_work_seconds, overtime_seconds, manual_work_seconds,
                    lab_seconds, meeting_seconds, activity_score, work_intensity,
                    longest_continuous_seconds, updated_at)
                VALUES (
                    $start, $end, $keyboard, $mouseClicks, $mouseDistance, $scroll,
                    $active, $idle, $afk, $appSwitches, $process, $schedule, $mode,
                    $normal, $overtime, $manual, $lab, $meeting, $activity, $intensity,
                    $longestContinuous, $updatedAt)
                ON CONFLICT(bucket_start) DO UPDATE SET
                    bucket_end = excluded.bucket_end,
                    keyboard_count = excluded.keyboard_count,
                    mouse_click_count = excluded.mouse_click_count,
                    mouse_distance = excluded.mouse_distance,
                    scroll_count = excluded.scroll_count,
                    active_seconds = excluded.active_seconds,
                    idle_seconds = excluded.idle_seconds,
                    afk_seconds = excluded.afk_seconds,
                    app_switch_count = excluded.app_switch_count,
                    dominant_process = excluded.dominant_process,
                    schedule_state = excluded.schedule_state,
                    work_mode = excluded.work_mode,
                    normal_work_seconds = excluded.normal_work_seconds,
                    overtime_seconds = excluded.overtime_seconds,
                    manual_work_seconds = excluded.manual_work_seconds,
                    lab_seconds = excluded.lab_seconds,
                    meeting_seconds = excluded.meeting_seconds,
                    activity_score = excluded.activity_score,
                    work_intensity = excluded.work_intensity,
                    longest_continuous_seconds = excluded.longest_continuous_seconds,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$start", FormatLocalTimestamp(bucket.BucketStart));
            command.Parameters.AddWithValue("$end", FormatLocalTimestamp(bucket.BucketEnd));
            command.Parameters.AddWithValue("$keyboard", bucket.KeyboardCount);
            command.Parameters.AddWithValue("$mouseClicks", bucket.MouseClickCount);
            command.Parameters.AddWithValue("$mouseDistance", bucket.MouseDistance);
            command.Parameters.AddWithValue("$scroll", bucket.ScrollCount);
            command.Parameters.AddWithValue("$active", bucket.ActiveSeconds);
            command.Parameters.AddWithValue("$idle", bucket.IdleSeconds);
            command.Parameters.AddWithValue("$afk", bucket.AfkSeconds);
            command.Parameters.AddWithValue("$appSwitches", bucket.AppSwitchCount);
            command.Parameters.AddWithValue("$process", (object?)bucket.DominantProcess ?? DBNull.Value);
            command.Parameters.AddWithValue("$schedule", bucket.ScheduleState.ToString());
            command.Parameters.AddWithValue("$mode", bucket.WorkMode.ToString());
            command.Parameters.AddWithValue("$normal", bucket.NormalWorkSeconds);
            command.Parameters.AddWithValue("$overtime", bucket.OvertimeSeconds);
            command.Parameters.AddWithValue("$manual", bucket.ManualWorkSeconds);
            command.Parameters.AddWithValue("$lab", bucket.LabSeconds);
            command.Parameters.AddWithValue("$meeting", bucket.MeetingSeconds);
            command.Parameters.AddWithValue("$activity", bucket.ActivityScore);
            command.Parameters.AddWithValue("$intensity", bucket.WorkIntensity);
            command.Parameters.AddWithValue("$longestContinuous", bucket.LongestContinuousWorkSeconds);
            command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(DateTimeOffset.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    public async Task<IReadOnlyList<ActivityBucket>> GetActivityBucketsAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bucket_start, bucket_end, keyboard_count, mouse_click_count,
                   mouse_distance, scroll_count, active_seconds, idle_seconds, afk_seconds,
                   app_switch_count, dominant_process, schedule_state, work_mode,
                   normal_work_seconds, overtime_seconds, manual_work_seconds,
                   lab_seconds, meeting_seconds, activity_score, work_intensity,
                   longest_continuous_seconds
            FROM activity_buckets
            WHERE bucket_start >= $start AND bucket_start < $end
            ORDER BY bucket_start;
            """;
        command.Parameters.AddWithValue("$start", FormatLocalTimestamp(date.ToDateTime(TimeOnly.MinValue)));
        command.Parameters.AddWithValue("$end", FormatLocalTimestamp(date.AddDays(1).ToDateTime(TimeOnly.MinValue)));

        var result = new List<ActivityBucket>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            _ = Enum.TryParse<WorkScheduleState>(reader.GetString(11), out var scheduleState);
            _ = Enum.TryParse<ActivityMode>(reader.GetString(12), out var workMode);
            result.Add(new ActivityBucket(
                DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetDouble(4),
                reader.GetInt64(5),
                reader.GetDouble(6),
                reader.GetDouble(7),
                reader.GetDouble(8),
                reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                scheduleState,
                workMode,
                reader.GetDouble(13),
                reader.GetDouble(14),
                reader.GetDouble(15),
                reader.GetDouble(16),
                reader.GetDouble(17),
                reader.GetDouble(18),
                reader.GetDouble(19),
                reader.GetDouble(20)));
        }

        return result;
    }

    public async Task UpsertAppUsageAsync(
        IEnumerable<AppUsageEntry> entries,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        var items = entries
            .Where(static entry => ApplicationProcessPolicy.ShouldPersistApplication(entry.ProcessName))
            .ToArray();
        if (items.Length == 0)
        {
            return;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        foreach (var entry in items)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO app_usage_daily (
                    usage_date, process_name, foreground_seconds,
                    active_foreground_seconds, updated_at)
                VALUES ($date, $process, $foreground, $active, $updatedAt)
                ON CONFLICT(usage_date, process_name) DO UPDATE SET
                    foreground_seconds = excluded.foreground_seconds,
                    active_foreground_seconds = excluded.active_foreground_seconds,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$date", FormatDate(entry.Date));
            command.Parameters.AddWithValue("$process", entry.ProcessName);
            command.Parameters.AddWithValue("$foreground", entry.ForegroundDuration.TotalSeconds);
            command.Parameters.AddWithValue("$active", entry.ActiveForegroundDuration.TotalSeconds);
            command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(DateTimeOffset.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    public async Task<IReadOnlyList<AppUsageEntry>> GetAppUsageAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT process_name, foreground_seconds, active_foreground_seconds
            FROM app_usage_daily
            WHERE usage_date = $date
            ORDER BY active_foreground_seconds DESC;
            """;
        command.Parameters.AddWithValue("$date", FormatDate(date));

        var result = new List<AppUsageEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var processName = reader.GetString(0);
            if (!ApplicationProcessPolicy.ShouldPersistApplication(processName))
            {
                continue;
            }

            result.Add(new AppUsageEntry(
                date,
                processName,
                TimeSpan.FromSeconds(reader.GetDouble(1)),
                TimeSpan.FromSeconds(reader.GetDouble(2))));
        }

        return result;
    }

    public async Task UpsertDailySummaryAsync(
        DailySummary summary,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO daily_summaries (
                summary_date, normal_work_seconds, overtime_seconds, lab_seconds,
                meeting_seconds, total_work_seconds, active_computer_seconds, afk_seconds,
                keyboard_count, mouse_click_count, mouse_distance, scroll_count,
                app_switch_count, average_work_intensity, longest_continuous_seconds,
                top_applications_json, updated_at)
            VALUES (
                $date, $normal, $overtime, $lab, $meeting, $total, $active, $afk,
                $keyboard, $mouseClicks, $mouseDistance, $scroll, $appSwitches,
                $intensity, $longest, $topApps, $updatedAt)
            ON CONFLICT(summary_date) DO UPDATE SET
                normal_work_seconds = excluded.normal_work_seconds,
                overtime_seconds = excluded.overtime_seconds,
                lab_seconds = excluded.lab_seconds,
                meeting_seconds = excluded.meeting_seconds,
                total_work_seconds = excluded.total_work_seconds,
                active_computer_seconds = excluded.active_computer_seconds,
                afk_seconds = excluded.afk_seconds,
                keyboard_count = excluded.keyboard_count,
                mouse_click_count = excluded.mouse_click_count,
                mouse_distance = excluded.mouse_distance,
                scroll_count = excluded.scroll_count,
                app_switch_count = excluded.app_switch_count,
                average_work_intensity = excluded.average_work_intensity,
                longest_continuous_seconds = excluded.longest_continuous_seconds,
                top_applications_json = excluded.top_applications_json,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$date", FormatDate(summary.Date));
        command.Parameters.AddWithValue("$normal", summary.NormalWorkDuration.TotalSeconds);
        command.Parameters.AddWithValue("$overtime", summary.OvertimeDuration.TotalSeconds);
        command.Parameters.AddWithValue("$lab", summary.LabDuration.TotalSeconds);
        command.Parameters.AddWithValue("$meeting", summary.MeetingDuration.TotalSeconds);
        command.Parameters.AddWithValue("$total", summary.TotalWorkDuration.TotalSeconds);
        command.Parameters.AddWithValue("$active", summary.ActiveComputerDuration.TotalSeconds);
        command.Parameters.AddWithValue("$afk", summary.AfkDuration.TotalSeconds);
        command.Parameters.AddWithValue("$keyboard", summary.KeyboardCount);
        command.Parameters.AddWithValue("$mouseClicks", summary.MouseClickCount);
        command.Parameters.AddWithValue("$mouseDistance", summary.MouseDistance);
        command.Parameters.AddWithValue("$scroll", summary.ScrollCount);
        command.Parameters.AddWithValue("$appSwitches", summary.AppSwitchCount);
        command.Parameters.AddWithValue("$intensity", summary.AverageWorkIntensity);
        command.Parameters.AddWithValue("$longest", summary.LongestContinuousWork.TotalSeconds);
        command.Parameters.AddWithValue(
            "$topApps",
            System.Text.Json.JsonSerializer.Serialize(summary.TopApplications.Select(static app => new
            {
                app.ProcessName,
                ActiveSeconds = app.ActiveDuration.TotalSeconds
            })));
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string FormatLocalTimestamp(DateTime timestamp) =>
        timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    private Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        return _initialized ? Task.CompletedTask : InitializeAsync(cancellationToken);
    }

}
