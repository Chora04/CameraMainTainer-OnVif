using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace CameraMaintainer.Infrastructure.Data;

public sealed record LegacyBootstrapResult(bool BaselineCreated, string? BackupPath);

public static class LegacyDatabaseBootstrapper
{
    private static readonly string[] RequiredTables =
    {
        "alerts",
        "app_users",
        "cameras",
        "config_backups",
        "credentials",
        "firmware_records",
        "health_checks",
        "maintenance_log",
        "ptz_presets",
        "scan_records",
        "settings"
    };

    private static readonly (string Table, string Column, string Definition)[] LegacyColumns =
    {
        ("cameras", "DirectionStatus", "TEXT NOT NULL DEFAULT 'Unknown'"),
        ("cameras", "DirectionLastCheckedAt", "TEXT NULL"),
        ("cameras", "LastEdgeDensity", "REAL NULL"),
        ("cameras", "LastTextureScore", "REAL NULL"),
        ("cameras", "BaselineHistogramJson", "TEXT NOT NULL DEFAULT '{}'"),
        ("health_checks", "JitterMs", "REAL NULL"),
        ("health_checks", "PacketLossPercent", "REAL NULL"),
        ("health_checks", "SnapshotSizeBytes", "INTEGER NULL"),
        ("health_checks", "DirectionStatus", "TEXT NOT NULL DEFAULT 'Unknown'")
    };

    public static async Task<LegacyBootstrapResult> PrepareAsync(
        AppDbContext db,
        CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            if (!await TableExistsAsync(connection, null, "cameras", cancellationToken))
                return new LegacyBootstrapResult(false, null);

            if (await HasMigrationHistoryAsync(connection, cancellationToken))
                return new LegacyBootstrapResult(false, null);

            var backupPath = await BackupAsync(connection, cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await ExecuteAsync(connection, transaction, LegacyTablesSql, cancellationToken);

            foreach (var (table, column, definition) in LegacyColumns)
            {
                if (!await ColumnExistsAsync(connection, transaction, table, column, cancellationToken))
                {
                    await ExecuteAsync(
                        connection,
                        transaction,
                        $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}",
                        cancellationToken);
                }
            }

            var missingTables = new List<string>();
            foreach (var table in RequiredTables)
            {
                if (!await TableExistsAsync(connection, transaction, table, cancellationToken))
                    missingTables.Add(table);
            }

            if (missingTables.Count > 0)
            {
                throw new InvalidOperationException(
                    "Legacy database is incomplete. Missing tables: " +
                    string.Join(", ", missingTables) +
                    $". A backup was created at {backupPath}.");
            }

            await ExecuteAsync(connection, transaction, LegacyIndexesSql, cancellationToken);
            await ExecuteAsync(connection, transaction, HistoryTableSql, cancellationToken);

            var migrationId = db.Database.GetMigrations().FirstOrDefault()
                ?? throw new InvalidOperationException("No EF Core migrations were found.");
            await InsertHistoryAsync(connection, transaction, migrationId, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new LegacyBootstrapResult(true, backupPath);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static async Task<bool> HasMigrationHistoryAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, null, "__EFMigrationsHistory", cancellationToken))
            return false;

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM \"__EFMigrationsHistory\" LIMIT 1)";
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<bool> TableExistsAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name)";
        AddParameter(command, "$name", table);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<bool> ColumnExistsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT EXISTS(SELECT 1 FROM pragma_table_info($table) WHERE name = $column COLLATE NOCASE)";
        AddParameter(command, "$table", table);
        AddParameter(command, "$column", column);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<string> BackupAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var sourcePath = Path.GetFullPath(connection.DataSource);
        var directory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException("The database directory could not be determined.");
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath);
        var backupPath = Path.Combine(
            directory,
            $"{fileName}.pre-migrations-{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}");

        await using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $backupPath";
        AddParameter(command, "$backupPath", backupPath);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return backupPath;
    }

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Only private schema constants and the fixed LegacyColumns allowlist reach this method.")]
    private static async Task ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertHistoryAsync(
        DbConnection connection,
        DbTransaction transaction,
        string migrationId,
        CancellationToken cancellationToken)
    {
        var informationalVersion = typeof(DbContext).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var productVersion = informationalVersion?.Split('+')[0]
            ?? typeof(DbContext).Assembly.GetName().Version?.ToString(3)
            ?? throw new InvalidOperationException("The Entity Framework product version could not be determined.");

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ($migrationId, $productVersion)
            """;
        AddParameter(command, "$migrationId", migrationId);
        AddParameter(command, "$productVersion", productVersion);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private const string HistoryTableSql = """
        CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
            "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
            "ProductVersion" TEXT NOT NULL
        );
        """;

    private const string LegacyTablesSql = """
        CREATE TABLE IF NOT EXISTS firmware_records (
            Id INTEGER NOT NULL CONSTRAINT PK_firmware_records PRIMARY KEY AUTOINCREMENT,
            CameraId INTEGER NOT NULL,
            FirmwareVersion TEXT NOT NULL,
            HardwareId TEXT NOT NULL,
            Manufacturer TEXT NOT NULL,
            Model TEXT NOT NULL,
            IsCurrent INTEGER NOT NULL DEFAULT 0,
            DetectedAt TEXT NOT NULL,
            FOREIGN KEY (CameraId) REFERENCES cameras(Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS app_users (
            Id INTEGER NOT NULL CONSTRAINT PK_app_users PRIMARY KEY AUTOINCREMENT,
            Username TEXT NOT NULL,
            DisplayName TEXT NOT NULL,
            PasswordHash TEXT NOT NULL,
            PasswordSalt TEXT NOT NULL,
            Role TEXT NOT NULL,
            CanViewCameras INTEGER NOT NULL,
            CanRunChecks INTEGER NOT NULL,
            CanManageCameras INTEGER NOT NULL,
            CanManageUsers INTEGER NOT NULL,
            IsActive INTEGER NOT NULL,
            LastLoginAt TEXT NULL,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        );
        """;

    private const string LegacyIndexesSql = """
        CREATE UNIQUE INDEX IF NOT EXISTS IX_app_users_Username ON app_users (Username);
        CREATE INDEX IF NOT EXISTS IX_firmware_records_CameraId_DetectedAt
            ON firmware_records (CameraId, DetectedAt DESC);
        CREATE INDEX IF NOT EXISTS IX_firmware_records_CameraId
            ON firmware_records (CameraId);
        """;
}
