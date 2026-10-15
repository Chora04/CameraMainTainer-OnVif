using System.Text.Json;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CameraMaintainer.Infrastructure.Data.Repositories;

public sealed class AlertBoardRepository
{
    private const string SettingKey = "alerts.board.columns";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _db;
    private readonly IMonitoringEvents? _events;

    public AlertBoardRepository(AppDbContext db, IMonitoringEvents? events = null)
    {
        _db = db;
        _events = events;
    }

    public async Task<List<AlertBoardColumn>> GetColumnsAsync()
    {
        var setting = await _db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Key == SettingKey);
        if (setting == null || string.IsNullOrWhiteSpace(setting.Value))
            return AlertBoardColumnDefaults.Create();

        try
        {
            var columns = JsonSerializer.Deserialize<List<AlertBoardColumn>>(setting.Value, JsonOptions);
            if (columns is { Count: > 0 } && columns.All(IsValid))
            {
                foreach (var column in columns)
                    column.VisibleToRoles = AlertBoardRoleVisibility.Normalize(column.VisibleToRoles);
                return columns.OrderBy(x => x.SortOrder).ToList();
            }
        }
        catch (JsonException)
        {
            // Fall back to the safe default board when a local setting is damaged.
        }

        return AlertBoardColumnDefaults.Create();
    }

    public async Task SaveColumnsAsync(IEnumerable<AlertBoardColumn> columns)
    {
        var normalized = columns
            .Where(IsValid)
            .Select((column, index) => new AlertBoardColumn
            {
                Key = column.Key.Trim(),
                Title = column.Title.Trim(),
                Subtitle = column.Subtitle.Trim(),
                Color = column.Color.Trim().ToUpperInvariant(),
                VisibleToRoles = AlertBoardRoleVisibility.Normalize(column.VisibleToRoles),
                SortOrder = index
            })
            .ToList();

        if (normalized.Count == 0)
            throw new InvalidOperationException("Доска должна содержать хотя бы одну колонку");

        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        var setting = await _db.Settings.SingleOrDefaultAsync(x => x.Key == SettingKey);
        if (setting == null)
        {
            _db.Settings.Add(new Setting { Key = SettingKey, Value = json });
        }
        else
        {
            setting.Value = json;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        if (_events != null) await _events.AlertsChangedAsync();
    }

    public async Task<AlertBoardColumn> AddColumnAsync(AlertBoardColumn column)
    {
        var columns = await GetColumnsAsync();
        if (columns.Any(x => x.Key.Equals(column.Key, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Колонка с таким ключом уже существует");

        column.SortOrder = columns.Count;
        columns.Add(column);
        await SaveColumnsAsync(columns);
        return column;
    }

    public async Task UpdateColumnAsync(string key, AlertBoardColumn changes)
    {
        var columns = await GetColumnsAsync();
        var column = columns.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("Колонка не найдена");

        column.Title = changes.Title;
        column.Subtitle = changes.Subtitle;
        column.Color = changes.Color;
        column.VisibleToRoles = AlertBoardRoleVisibility.Normalize(changes.VisibleToRoles);
        await SaveColumnsAsync(columns);
    }

    public async Task ReorderColumnsAsync(IReadOnlyList<string> keys)
    {
        var columns = await GetColumnsAsync();
        if (keys.Count != columns.Count || keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Count ||
            keys.Any(key => columns.All(column => !column.Key.Equals(key, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("Нельзя сохранить порядок колонок");
        }

        var ordered = keys.Select(key => columns.First(column =>
            column.Key.Equals(key, StringComparison.OrdinalIgnoreCase))).ToList();
        await SaveColumnsAsync(ordered);
    }

    public async Task<List<AlertBoardColumn>> ApplyRecommendedWorkflowAsync(int? actorUserId = null)
    {
        var existingColumns = await GetColumnsAsync();
        var recommendedColumns = AlertBoardColumnDefaults.Create();
        var knownKeys = recommendedColumns
            .Select(column => column.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var legacyStageMap = existingColumns
            .GroupBy(column => column.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => MapLegacyStage(group.First(), knownKeys),
                StringComparer.OrdinalIgnoreCase);

        var activeAlerts = await _db.Alerts.Where(alert => !alert.AutoResolved).ToListAsync();
        var actor = actorUserId.HasValue ? await _db.AppUsers.FindAsync(actorUserId.Value) : null;
        foreach (var alert in activeAlerts)
        {
            var previous = AlertAuditEvent.CaptureState(alert);
            var fromStage = alert.WorkflowStage;
            var currentStage = string.IsNullOrWhiteSpace(alert.WorkflowStage) ? "new" : alert.WorkflowStage;
            if (legacyStageMap.TryGetValue(currentStage, out var mappedStage))
                alert.WorkflowStage = mappedStage;
            else if (!knownKeys.Contains(currentStage))
                alert.WorkflowStage = alert.AssignedToUserId.HasValue ? "waiting" : "new";
            if (fromStage != alert.WorkflowStage) RecordStageMigration(alert, fromStage, previous, actor);
        }

        await SaveColumnsAsync(recommendedColumns);
        return recommendedColumns;
    }

    public async Task DeleteColumnAsync(string key, string moveTo, int? actorUserId = null)
    {
        if (IsRequiredStage(key))
            throw new InvalidOperationException("Этот системный этап нужен для обработки ошибок. Его можно переименовать или настроить, но нельзя удалить");
        var columns = await GetColumnsAsync();
        if (columns.Count <= 1)
            throw new InvalidOperationException("Нельзя удалить последнюю колонку");

        var source = columns.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("Колонка не найдена");
        var target = columns.FirstOrDefault(x => x.Key.Equals(moveTo, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Выберите колонку для переноса карточек");
        if (source.Key.Equals(target.Key, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Карточки нельзя переносить в удаляемую колонку");

        var alerts = await _db.Alerts.Where(x => !x.AutoResolved && x.WorkflowStage == source.Key).ToListAsync();
        var actor = actorUserId.HasValue ? await _db.AppUsers.FindAsync(actorUserId.Value) : null;
        foreach (var alert in alerts)
        {
            var previous = AlertAuditEvent.CaptureState(alert);
            var fromStage = alert.WorkflowStage;
            alert.WorkflowStage = target.Key;
            RecordStageMigration(alert, fromStage, previous, actor);
        }

        columns.Remove(source);
        await SaveColumnsAsync(columns);
    }

    private static bool IsRequiredStage(string key) =>
        new[] { "new", "assigned", "working", "review", "ready" }.Contains(key, StringComparer.OrdinalIgnoreCase);

    private void RecordStageMigration(Alert alert, string? fromStage, string previous, AppUser? actor)
    {
        alert.UpdatedAt = DateTime.UtcNow;
        _db.AlertAuditEvents.Add(new AlertAuditEvent
        {
            Alert = alert,
            EventType = "stage_changed",
            FromStage = fromStage,
            ToStage = alert.WorkflowStage,
            ActorUserId = actor?.Id,
            ActorUsername = actor?.Username ?? "system",
            ActorDisplayName = actor?.DisplayName ?? "Система",
            CreatedAt = alert.UpdatedAt,
            Summary = "Этап изменён при настройке колонок",
            PreviousValuesJson = previous,
            NewValuesJson = AlertAuditEvent.CaptureState(alert)
        });
    }

    private static bool IsValid(AlertBoardColumn column) =>
        !string.IsNullOrWhiteSpace(column.Key) &&
        !string.IsNullOrWhiteSpace(column.Title) &&
        column.Key.Length <= 64 &&
        column.Title.Length <= 64 &&
        column.Subtitle.Length <= 128 &&
        System.Text.RegularExpressions.Regex.IsMatch(column.Color ?? string.Empty, "^#[0-9a-fA-F]{6}$");

    private static string MapLegacyStage(AlertBoardColumn column, HashSet<string> knownKeys)
    {
        if (knownKeys.Contains(column.Key))
            return column.Key.ToLowerInvariant();

        var text = $"{column.Title} {column.Subtitle}".ToLowerInvariant();
        if (text.Contains("назнач", StringComparison.Ordinal) &&
            (text.Contains("техник", StringComparison.Ordinal) || text.Contains("исполн", StringComparison.Ordinal)))
            return "assigned";
        if (text.Contains("ожида", StringComparison.Ordinal) ||
            text.Contains("уточн", StringComparison.Ordinal) ||
            text.Contains("ответ", StringComparison.Ordinal))
            return "waiting";
        if (text.Contains("проверк", StringComparison.Ordinal) ||
            text.Contains("сдал", StringComparison.Ordinal) ||
            text.Contains("заверш", StringComparison.Ordinal))
            return "review";

        return "waiting";
    }
}
