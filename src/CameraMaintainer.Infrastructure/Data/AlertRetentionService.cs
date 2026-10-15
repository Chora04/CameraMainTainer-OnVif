using System.Text.Json;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CameraMaintainer.Infrastructure.Data;

public sealed record AlertRetentionPolicy(int RetentionMonths = 3);

public sealed record AlertRetentionRun(
    DateTime RunAtUtc,
    DateTime? CutoffUtc,
    int DeletedAlerts,
    int DeletedAuditEvents);

public sealed record AlertRetentionOverview(
    AlertRetentionPolicy Policy,
    DateTime? CutoffUtc,
    DateTime? OldestResolvedAtUtc,
    int ResolvedAlertCount,
    int EligibleAlertCount,
    AlertRetentionRun? LastRun);

public sealed class AlertRetentionService
{
    public const string PolicySettingKey = "alerts.retention.policy";
    public const string StatusSettingKey = "alerts.retention.last-run";

    private static readonly int[] AllowedRetentionMonths = [0, 1, 3, 6, 12, 24];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim CleanupLock = new(1, 1);

    private readonly AppDbContext _db;
    private readonly IMonitoringEvents? _events;

    public AlertRetentionService(AppDbContext db, IMonitoringEvents? events = null)
    {
        _db = db;
        _events = events;
    }

    public async Task<AlertRetentionPolicy> GetPolicyAsync(CancellationToken cancellationToken = default)
    {
        var value = await _db.Settings
            .AsNoTracking()
            .Where(item => item.Key == PolicySettingKey)
            .Select(item => item.Value)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(value))
            return new AlertRetentionPolicy();

        try
        {
            return Normalize(JsonSerializer.Deserialize<AlertRetentionPolicy>(value, JsonOptions));
        }
        catch (JsonException)
        {
            return new AlertRetentionPolicy();
        }
    }

    public async Task<AlertRetentionPolicy> SavePolicyAsync(
        int retentionMonths,
        CancellationToken cancellationToken = default)
    {
        var policy = Normalize(new AlertRetentionPolicy(retentionMonths));
        var setting = await _db.Settings.SingleOrDefaultAsync(
            item => item.Key == PolicySettingKey,
            cancellationToken);
        var value = JsonSerializer.Serialize(policy, JsonOptions);

        if (setting == null)
        {
            _db.Settings.Add(new Setting
            {
                Key = PolicySettingKey,
                Value = value,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return policy;
    }

    public async Task<AlertRetentionOverview> GetOverviewAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var policy = await GetPolicyAsync(cancellationToken);
        var resolved = _db.Alerts.AsNoTracking()
            .Where(alert => alert.AutoResolved && alert.ResolvedAt != null);
        var cutoff = GetCutoffUtc(policy, utcNow);
        var oldest = await resolved.MinAsync(
            alert => (DateTime?)alert.ResolvedAt,
            cancellationToken);
        var resolvedCount = await resolved.CountAsync(cancellationToken);
        var eligibleCount = cutoff.HasValue
            ? await resolved.CountAsync(alert => alert.ResolvedAt < cutoff.Value, cancellationToken)
            : 0;

        return new AlertRetentionOverview(
            policy,
            cutoff,
            oldest,
            resolvedCount,
            eligibleCount,
            await GetLastRunAsync(cancellationToken));
    }

    public async Task<AlertRetentionRun> CleanupAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        await CleanupLock.WaitAsync(cancellationToken);
        try
        {
            var policy = await GetPolicyAsync(cancellationToken);
            var cutoff = GetCutoffUtc(policy, utcNow);
            var deletedAlerts = 0;
            var deletedAuditEvents = 0;

            if (cutoff.HasValue)
            {
                await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
                var expiredAlerts = _db.Alerts.Where(alert =>
                    alert.AutoResolved &&
                    alert.ResolvedAt != null &&
                    alert.ResolvedAt < cutoff.Value);

                deletedAuditEvents = await _db.AlertAuditEvents
                    .Where(audit => expiredAlerts.Select(alert => alert.Id).Contains(audit.AlertId))
                    .ExecuteDeleteAsync(cancellationToken);
                deletedAlerts = await expiredAlerts.ExecuteDeleteAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                // SQLite keeps freed pages in its free list and reuses them for new records.
                await _db.Database.ExecuteSqlRawAsync("PRAGMA optimize;", cancellationToken);
            }

            var result = new AlertRetentionRun(
                utcNow,
                cutoff,
                deletedAlerts,
                deletedAuditEvents);
            await SaveLastRunAsync(result, cancellationToken);
            if (deletedAlerts > 0 && _events != null) await _events.AlertsChangedAsync();
            return result;
        }
        finally
        {
            CleanupLock.Release();
        }
    }

    public static DateTime? GetCutoffUtc(AlertRetentionPolicy policy, DateTime utcNow)
    {
        var normalized = Normalize(policy);
        return normalized.RetentionMonths == 0
            ? null
            : DateTime.SpecifyKind(utcNow.AddHours(5).Date.AddMonths(-normalized.RetentionMonths).AddHours(-5), DateTimeKind.Utc);
    }

    private static AlertRetentionPolicy Normalize(AlertRetentionPolicy? policy)
    {
        var months = policy?.RetentionMonths ?? 3;
        return new AlertRetentionPolicy(
            AllowedRetentionMonths.Contains(months) ? months : 3);
    }

    private async Task<AlertRetentionRun?> GetLastRunAsync(CancellationToken cancellationToken)
    {
        var value = await _db.Settings
            .AsNoTracking()
            .Where(item => item.Key == StatusSettingKey)
            .Select(item => item.Value)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return JsonSerializer.Deserialize<AlertRetentionRun>(value, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task SaveLastRunAsync(
        AlertRetentionRun result,
        CancellationToken cancellationToken)
    {
        var setting = await _db.Settings.SingleOrDefaultAsync(
            item => item.Key == StatusSettingKey,
            cancellationToken);
        var value = JsonSerializer.Serialize(result, JsonOptions);

        if (setting == null)
        {
            _db.Settings.Add(new Setting
            {
                Key = StatusSettingKey,
                Value = value,
                UpdatedAt = result.RunAtUtc
            });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = result.RunAtUtc;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
