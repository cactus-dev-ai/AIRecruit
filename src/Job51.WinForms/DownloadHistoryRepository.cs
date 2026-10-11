using Microsoft.Data.Sqlite;

namespace Job51.WinForms;

internal sealed class DownloadHistoryRepository
{
    private static readonly string LegacyDatabasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "51job",
        "51job.db");

    public string DatabasePath { get; } = Path.Combine(AppContext.BaseDirectory, "51job.db");

    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppContext.BaseDirectory);
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS downloaded_talents (
                talent_id TEXT NOT NULL PRIMARY KEY,
                source_url TEXT NOT NULL,
                candidate_name TEXT NULL,
                json_path TEXT NOT NULL,
                batch_type TEXT NOT NULL,
                downloaded_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_downloaded_talents_downloaded_at
                ON downloaded_talents(downloaded_at);
            CREATE TABLE IF NOT EXISTS downloaded_talent_jobs (
                talent_id TEXT NOT NULL,
                source_job_id TEXT NOT NULL,
                source_job_name TEXT NOT NULL,
                json_path TEXT NOT NULL,
                downloaded_at TEXT NOT NULL,
                PRIMARY KEY (talent_id, source_job_id)
            );
            CREATE TABLE IF NOT EXISTS discarded_talents (
                talent_id TEXT NOT NULL PRIMARY KEY,
                reason TEXT NOT NULL,
                position_name TEXT NULL,
                discarded_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS talent_search_progress (
                singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
                resume_job_id TEXT NOT NULL,
                resume_job_name TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            DELETE FROM downloaded_talents
                WHERE lower(talent_id) IN ('resume','detail','talent','candidate','user','search','management');
            DELETE FROM discarded_talents
                WHERE lower(talent_id) IN ('resume','detail','talent','candidate','user','search','management');
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        if (!Path.GetFullPath(LegacyDatabasePath).Equals(
                Path.GetFullPath(DatabasePath), StringComparison.OrdinalIgnoreCase) &&
            File.Exists(LegacyDatabasePath))
        {
            await using var migrate = connection.CreateCommand();
            migrate.CommandText = """
                ATTACH DATABASE $legacyPath AS legacy_db;
                INSERT OR IGNORE INTO downloaded_talents
                    (talent_id, source_url, candidate_name, json_path, batch_type, downloaded_at)
                SELECT talent_id, source_url, candidate_name, json_path, batch_type, downloaded_at
                FROM legacy_db.downloaded_talents;
                INSERT OR IGNORE INTO discarded_talents
                    (talent_id, reason, position_name, discarded_at)
                SELECT talent_id, reason, position_name, discarded_at
                FROM legacy_db.discarded_talents;
                DETACH DATABASE legacy_db;
                """;
            migrate.Parameters.AddWithValue("$legacyPath", LegacyDatabasePath);
            await migrate.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<TalentSearchProgress?> GetTalentSearchProgressAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT resume_job_id, resume_job_name, updated_at
            FROM talent_search_progress
            WHERE singleton_id = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var updatedAt = DateTimeOffset.TryParse(reader.GetString(2), out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
        return new TalentSearchProgress(reader.GetString(0), reader.GetString(1), updatedAt);
    }

    public async Task SaveTalentSearchProgressAsync(
        string resumeJobId,
        string resumeJobName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO talent_search_progress
                (singleton_id, resume_job_id, resume_job_name, updated_at)
            VALUES (1, $jobId, $jobName, $updatedAt)
            ON CONFLICT(singleton_id) DO UPDATE SET
                resume_job_id = excluded.resume_job_id,
                resume_job_name = excluded.resume_job_name,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$jobId", resumeJobId);
        command.Parameters.AddWithValue("$jobName", resumeJobName);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.Now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> IsHandledAsync(string talentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(talentId)) return false;
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM downloaded_talents WHERE talent_id = $talentId
            UNION ALL
            SELECT 1 FROM discarded_talents WHERE talent_id = $talentId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$talentId", talentId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task<bool> IsHandledAsync(string talentId, string sourceJobId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(talentId) || string.IsNullOrWhiteSpace(sourceJobId)) return false;
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM downloaded_talent_jobs WHERE talent_id=$talentId AND source_job_id=$jobId LIMIT 1;";
        command.Parameters.AddWithValue("$talentId", talentId);
        command.Parameters.AddWithValue("$jobId", sourceJobId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task RecordAsync(DownloadRecord record, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO downloaded_talents
                (talent_id, source_url, candidate_name, json_path, batch_type, downloaded_at)
            VALUES
                ($talentId, $sourceUrl, $candidateName, $jsonPath, $batchType, $downloadedAt);
            """;
        command.Parameters.AddWithValue("$talentId", record.TalentId);
        command.Parameters.AddWithValue("$sourceUrl", record.SourceRoute);
        command.Parameters.AddWithValue("$candidateName", (object?)record.CandidateName ?? DBNull.Value);
        command.Parameters.AddWithValue("$jsonPath", record.JsonPath);
        command.Parameters.AddWithValue("$batchType", record.BatchType);
        command.Parameters.AddWithValue("$downloadedAt", record.DownloadedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(record.SourceJobId))
        {
            await using var jobCommand = connection.CreateCommand();
            jobCommand.CommandText = "INSERT OR IGNORE INTO downloaded_talent_jobs(talent_id,source_job_id,source_job_name,json_path,downloaded_at) VALUES($talentId,$jobId,$jobName,$jsonPath,$downloadedAt);";
            jobCommand.Parameters.AddWithValue("$talentId", record.TalentId);
            jobCommand.Parameters.AddWithValue("$jobId", record.SourceJobId);
            jobCommand.Parameters.AddWithValue("$jobName", record.SourceJobName ?? string.Empty);
            jobCommand.Parameters.AddWithValue("$jsonPath", record.JsonPath);
            jobCommand.Parameters.AddWithValue("$downloadedAt", record.DownloadedAt.ToString("O"));
            await jobCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task RecordDiscardedAsync(DiscardRecord record, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO discarded_talents
                (talent_id, reason, position_name, discarded_at)
            VALUES
                ($talentId, $reason, $positionName, $discardedAt);
            """;
        command.Parameters.AddWithValue("$talentId", record.TalentId);
        command.Parameters.AddWithValue("$reason", record.Reason);
        command.Parameters.AddWithValue("$positionName", (object?)record.PositionName ?? DBNull.Value);
        command.Parameters.AddWithValue("$discardedAt", record.DiscardedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
