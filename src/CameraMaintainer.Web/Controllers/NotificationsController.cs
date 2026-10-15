using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CameraMaintainer.Web.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private const string SettingsPrefix = "alerts.notifications.user.";
    private const string ReadSettingsPrefix = "alerts.notifications.read.user.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex NotificationIdPattern = new("^[0-9]+:[a-z_]+:[0-9]+$", RegexOptions.Compiled);
    private static readonly Regex PersistentNotificationIdPattern = new("^n:[0-9]+$", RegexOptions.Compiled);
    private static readonly Regex NoteNotificationIdPattern = new("^note:[0-9]+:[0-9]+$", RegexOptions.Compiled);
    private readonly AppDbContext _db;
    private readonly AlertBoardRepository _alertBoardRepository;

    public NotificationsController(AppDbContext db, AlertBoardRepository alertBoardRepository)
    {
        _db = db;
        _alertBoardRepository = alertBoardRepository;
    }

    [HttpGet]
    [Authorize(Policy = AccessPolicies.ViewAlerts)]
    public async Task<ActionResult<AlertNotificationFeedDto>> GetNotifications()
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var preferences = await GetPreferencesAsync(userId);
        var username = User.FindFirstValue(ClaimTypes.Name);
        var isAdministrator = User.HasClaim(
            AccessPolicies.PermissionClaim,
            AccessPolicies.ManageUsers) &&
            string.Equals(User.FindFirstValue(ClaimTypes.Role), "Admin", StringComparison.OrdinalIgnoreCase);
        var role = User.FindFirstValue(ClaimTypes.Role);
        var canReview = isAdministrator || string.Equals(role, "Operator", StringComparison.OrdinalIgnoreCase);
        var accountColumnKeys = await _db.AppUsers.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.AlertBoardColumnKeys)
            .SingleOrDefaultAsync();
        var boardColumns = await _alertBoardRepository.GetColumnsAsync();
        var visibleStageKeys = boardColumns
            .Where(column => AlertBoardRoleVisibility.IsVisibleTo(
                column,
                role,
                isAdministrator,
                accountColumnKeys))
            .Select(column => column.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var recentSince = now.AddMinutes(-preferences.RecentWindowMinutes);
        var overdueBefore = now.AddHours(-preferences.OverdueHours);

        var activeAlertsQuery = _db.Alerts
            .AsNoTracking()
            .Include(alert => alert.Camera)
            .Where(alert => !alert.AutoResolved);

        if (!isAdministrator)
        {
            activeAlertsQuery = activeAlertsQuery.Where(alert =>
                alert.AssignedToUserId == userId ||
                (!alert.AssignedToUserId.HasValue && !string.IsNullOrWhiteSpace(username) &&
                 alert.AssignedToUsername == username));

            var visibleStages = visibleStageKeys.ToList();
            var canSeeNew = visibleStageKeys.Contains("new");
            var canSeeAssigned = visibleStageKeys.Contains("assigned");
            activeAlertsQuery = activeAlertsQuery.Where(alert =>
                (alert.WorkflowStage != null && visibleStages.Contains(alert.WorkflowStage)) ||
                (alert.WorkflowStage == null &&
                 ((!alert.AssignedToUserId.HasValue && canSeeNew) ||
                  (alert.AssignedToUserId.HasValue && canSeeAssigned))));
        }

        var query = activeAlertsQuery.Where(alert =>
            (canReview && preferences.ReviewRequestsEnabled &&
             alert.RecoveryDetectedAt.HasValue && alert.WorkflowStage == "ready") ||
            (preferences.OverdueEnabled &&
             alert.UpdatedAt <= overdueBefore &&
             (alert.WorkflowStage == null || alert.WorkflowStage != "ready")) ||
            (canReview &&
             preferences.ReviewRequestsEnabled &&
             alert.WorkflowStage == "review" &&
             alert.UpdatedAt >= recentSince) ||
            (isAdministrator &&
             preferences.AssignmentsEnabled &&
             (alert.WorkflowStage == null || alert.WorkflowStage == "new") &&
             !alert.AssignedToUserId.HasValue &&
             alert.CreatedAt >= recentSince) ||
            (!isAdministrator &&
             preferences.AssignmentsEnabled &&
             alert.AssignedAt.HasValue &&
             alert.AssignedAt >= recentSince));

        var alerts = await query
            .OrderBy(alert => alert.UpdatedAt > overdueBefore)
            .ThenByDescending(alert => alert.UpdatedAt)
            .Take(100)
            .ToListAsync();

        var items = new List<AlertNotificationItemDto>();

        if (preferences.CalendarNotesEnabled)
        {
            var noteWindowEnd = now.AddHours(24);
            var noteWindowStart = now.AddDays(-30);
            var notes = await _db.WorkspaceNotes.AsNoTracking()
                .Where(note =>
                    (note.CreatedByUserId == userId ||
                     note.IsVisibleToAll ||
                     note.RecipientUserId == userId) &&
                    !note.CompletedAt.HasValue &&
                    note.ScheduledFor >= noteWindowStart && note.ScheduledFor <= noteWindowEnd)
                .OrderBy(note => note.ScheduledFor)
                .Take(100)
                .ToListAsync();
            items.AddRange(notes.Select(note =>
            {
                var overdue = note.ScheduledFor < now;
                var notificationId = $"note:{note.Id}:{note.ScheduledFor.Ticks}";
                var message = string.IsNullOrWhiteSpace(note.Body)
                    ? $"Автор: {note.CreatedByDisplayName}"
                    : note.Body;
                return new AlertNotificationItemDto(
                    notificationId,
                    null,
                    overdue ? "calendar_overdue" : "calendar_note",
                    overdue ? $"Просрочено: {note.Title}" : note.Title,
                    message,
                    overdue ? "danger" : note.Tone,
                    note.ScheduledFor,
                    null,
                    null,
                    $"/Alerts?view=active&planner=1&date={note.ScheduledFor:yyyy-MM-dd}",
                    false);
            }));
        }

        if (preferences.HandoffsEnabled)
        {
            var persistent = await _db.UserNotifications
                .Include(item => item.Alert)
                    .ThenInclude(alert => alert!.Handoffs)
                .Where(item => item.UserId == userId && !item.ResolvedAt.HasValue &&
                    item.Alert != null && !item.Alert.AutoResolved)
                .OrderBy(item => item.ReadAt.HasValue)
                .ThenByDescending(item => item.CreatedAt)
                .Take(100)
                .ToListAsync();
            if (!isAdministrator)
            {
                persistent = persistent
                    .Where(item => AlertVisibilityPolicy.IsHandoffParticipant(item.Alert!, userId) ||
                        visibleStageKeys.Contains(AlertVisibilityPolicy.EffectiveStage(item.Alert!)))
                    .ToList();
            }

            var reminderChanged = false;
            foreach (var item in persistent.Where(item => item.ReminderAt.HasValue && item.ReminderAt <= now))
            {
                item.ReadAt = null;
                item.LastRemindedAt = now;
                item.ReminderCount++;
                item.ReminderAt = now.AddHours(preferences.HandoffRepeatHours);
                reminderChanged = true;
            }
            if (reminderChanged)
                await _db.SaveChangesAsync();

            items.AddRange(persistent.Select(item =>
            {
                var overdue = item.ReminderCount > 0;
                return new AlertNotificationItemDto(
                    $"n:{item.Id}",
                    item.AlertId,
                    overdue ? "handoff_overdue" : item.Type,
                    overdue ? "Передача требует внимания" : item.Title,
                    item.Message,
                    overdue ? "warning" : item.Tone,
                    item.CreatedAt,
                    null,
                    null,
                    item.Url,
                    item.ReadAt.HasValue);
            }));
        }

        foreach (var alert in alerts)
        {
            var camera = alert.Camera?.Name ?? (alert.CameraId.HasValue ? $"Камера {alert.CameraId}" : "Камера");
            var address = alert.Camera?.IpAddress;
            var stage = alert.WorkflowStage?.ToLowerInvariant() ?? "new";

            if (canReview && preferences.ReviewRequestsEnabled && stage == "ready" && alert.RecoveryDetectedAt.HasValue)
            {
                items.Add(CreateItem(
                    alert, "recovery", "Камера восстановилась: подтвердите закрытие",
                    $"{camera}: {alert.Title}", "review", alert.RecoveryDetectedAt.Value, address));
                continue;
            }

            if (preferences.OverdueEnabled && alert.UpdatedAt <= overdueBefore && stage != "ready")
            {
                items.Add(CreateItem(
                    alert,
                    "overdue",
                    "Карточка долго без изменений",
                    $"{camera}: {alert.Title}",
                    "warning",
                    alert.UpdatedAt,
                    address));
                continue;
            }

            if (canReview && preferences.ReviewRequestsEnabled &&
                stage == "review" && alert.UpdatedAt >= recentSince)
            {
                items.Add(CreateItem(
                    alert,
                    "review",
                    "Работа техника ожидает проверки",
                    $"{camera}: {alert.Title}",
                    "review",
                    alert.UpdatedAt,
                    address));
                continue;
            }

            if (!preferences.AssignmentsEnabled)
                continue;

            if (isAdministrator && stage == "new" && !alert.AssignedToUserId.HasValue && alert.CreatedAt >= recentSince)
            {
                items.Add(CreateItem(
                    alert,
                    "new",
                    "Новая ошибка без ответственного",
                    $"{camera}: {alert.Title}",
                    "new",
                    alert.CreatedAt,
                    address));
            }
            else if (!isAdministrator && alert.AssignedAt >= recentSince)
            {
                items.Add(CreateItem(
                    alert,
                    "assignment",
                    "Вам назначена карточка",
                    $"{camera}: {alert.Title}",
                    "assignment",
                    alert.AssignedAt.Value,
                    address));
            }
        }

        var ordered = items
            .OrderBy(item => item.Type == "overdue" ? 0 : 1)
            .ThenByDescending(item => item.OccurredAt)
            .Take(100)
            .ToList();
        var readIds = await GetReadNotificationIdsAsync(userId);
        var decorated = ordered
            .Select(item => item.NotificationId.StartsWith("n:", StringComparison.Ordinal)
                ? item
                : item with { IsRead = readIds.Contains(item.NotificationId) })
            .ToList();
        var currentIds = decorated
            .Select(item => item.NotificationId)
            .ToHashSet(StringComparer.Ordinal);
        var retainedReadDescriptors = readIds
            .Where(id => !currentIds.Contains(id))
            .Select(ParseNotificationId)
            .Where(item => item != null)
            .Cast<ReadNotificationDescriptor>()
            .Take(100)
            .ToList();
        var retainedReadItems = new List<AlertNotificationItemDto>();

        if (retainedReadDescriptors.Count > 0)
        {
            var retainedAlertIds = retainedReadDescriptors
                .Select(item => item.AlertId)
                .Distinct()
                .ToList();
            var retainedAlerts = await activeAlertsQuery
                .Where(alert => retainedAlertIds.Contains(alert.Id))
                .ToListAsync();
            var alertsById = retainedAlerts.ToDictionary(alert => alert.Id);

            foreach (var descriptor in retainedReadDescriptors)
            {
                if ((descriptor.Type == "overdue" && !preferences.OverdueEnabled) ||
                    (descriptor.Type is "review" or "recovery" && !preferences.ReviewRequestsEnabled) ||
                    (descriptor.Type is "assignment" or "new" && !preferences.AssignmentsEnabled))
                    continue;
                if (!alertsById.TryGetValue(descriptor.AlertId, out var alert))
                    continue;

                var retained = CreateRetainedReadItem(alert, descriptor);
                if (retained != null)
                    retainedReadItems.Add(retained);
            }
        }

        var feedItems = decorated
            .Concat(retainedReadItems)
            .OrderBy(item => item.IsRead)
            .ThenByDescending(item => item.OccurredAt)
            .ToList();

        return Ok(new AlertNotificationFeedDto(feedItems.Count(item => !item.IsRead), feedItems,
            preferences.SoundEnabled, preferences.PopupEnabled));
    }

    [HttpPost("read")]
    [Authorize(Policy = AccessPolicies.ViewAlerts)]
    public async Task<ActionResult> MarkAsRead([FromBody] MarkNotificationReadDto request)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.NotificationId) ||
            request.NotificationId.Length > 96 ||
            (!NotificationIdPattern.IsMatch(request.NotificationId) &&
             !PersistentNotificationIdPattern.IsMatch(request.NotificationId) &&
             !NoteNotificationIdPattern.IsMatch(request.NotificationId)))
        {
            return BadRequest(new { message = "Некорректный идентификатор уведомления" });
        }

        if (PersistentNotificationIdPattern.IsMatch(request.NotificationId))
        {
            if (!long.TryParse(request.NotificationId.AsSpan(2), out var notificationId))
                return BadRequest(new { message = "Некорректный идентификатор уведомления" });

            var notification = await _db.UserNotifications.SingleOrDefaultAsync(item =>
                item.Id == notificationId && item.UserId == userId && !item.ResolvedAt.HasValue);
            if (notification == null)
                return NoContent();

            var now = DateTime.UtcNow;
            notification.ReadAt ??= now;
            if (notification.Type is "handoff_completed" or "handoff_rejected")
                notification.ReminderAt = null;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        var key = ReadSettingsPrefix + userId;
        var setting = await _db.Settings.SingleOrDefaultAsync(item => item.Key == key);
        var readIds = DeserializeReadNotificationIds(setting?.Value);
        readIds.Remove(request.NotificationId);
        readIds.Insert(0, request.NotificationId);
        if (readIds.Count > 200)
            readIds.RemoveRange(200, readIds.Count - 200);

        var value = JsonSerializer.Serialize(readIds, JsonOptions);
        if (setting == null)
            _db.Settings.Add(new Setting { Key = key, Value = value });
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("preferences")]
    [Authorize(Policy = AccessPolicies.ViewAlerts)]
    public async Task<ActionResult<AlertNotificationPreferencesDto>> GetPreferences()
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        return Ok(await GetPreferencesAsync(userId));
    }

    [HttpPut("preferences")]
    [Authorize(Policy = AccessPolicies.ViewAlerts)]
    public async Task<ActionResult<AlertNotificationPreferencesDto>> UpdatePreferences(
        [FromBody] AlertNotificationPreferencesDto request)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var preferences = Normalize(request);
        var key = SettingsPrefix + userId;
        var setting = await _db.Settings.SingleOrDefaultAsync(item => item.Key == key);
        var value = JsonSerializer.Serialize(preferences, JsonOptions);
        if (setting == null)
        {
            _db.Settings.Add(new Setting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok(preferences);
    }

    private async Task<AlertNotificationPreferencesDto> GetPreferencesAsync(int userId)
    {
        var setting = await _db.Settings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Key == SettingsPrefix + userId);
        if (setting == null || string.IsNullOrWhiteSpace(setting.Value))
            return new AlertNotificationPreferencesDto();

        try
        {
            return Normalize(JsonSerializer.Deserialize<AlertNotificationPreferencesDto>(setting.Value, JsonOptions));
        }
        catch (JsonException)
        {
            return new AlertNotificationPreferencesDto();
        }
    }

    private async Task<HashSet<string>> GetReadNotificationIdsAsync(int userId)
    {
        var value = await _db.Settings
            .AsNoTracking()
            .Where(item => item.Key == ReadSettingsPrefix + userId)
            .Select(item => item.Value)
            .SingleOrDefaultAsync();
        return DeserializeReadNotificationIds(value).ToHashSet(StringComparer.Ordinal);
    }

    private static List<string> DeserializeReadNotificationIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(value, JsonOptions)?
                .Where(IsValidReadNotificationId)
                .Distinct(StringComparer.Ordinal)
                .Take(200)
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private static AlertNotificationPreferencesDto Normalize(AlertNotificationPreferencesDto? value)
    {
        value ??= new AlertNotificationPreferencesDto();
        value.RecentWindowMinutes = Math.Clamp(value.RecentWindowMinutes, 5, 1440);
        value.OverdueHours = Math.Clamp(value.OverdueHours, 1, 168);
        value.HandoffDueHours = Math.Clamp(value.HandoffDueHours, 1, 720);
        value.HandoffRepeatHours = Math.Clamp(value.HandoffRepeatHours, 1, 168);
        return value;
    }

    private static AlertNotificationItemDto CreateItem(
        Alert alert,
        string type,
        string title,
        string message,
        string tone,
        DateTime occurredAt,
        string? address)
    {
        var utcOccurredAt = occurredAt.Kind == DateTimeKind.Utc
            ? occurredAt
            : DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc);
        var notificationId = $"{alert.Id}:{type}:{utcOccurredAt.Ticks}";
        return new AlertNotificationItemDto(
            notificationId,
            alert.Id,
            type,
            title,
            message,
            tone,
            occurredAt,
            alert.CameraId,
            address,
            $"/Alerts?view=active&alert={alert.Id}",
            false);
    }

    private static AlertNotificationItemDto? CreateRetainedReadItem(
        Alert alert,
        ReadNotificationDescriptor descriptor)
    {
        var camera = alert.Camera?.Name ?? (alert.CameraId.HasValue ? $"Камера {alert.CameraId}" : "Камера");
        var message = $"{camera}: {alert.Title}";
        var (title, tone) = descriptor.Type switch
        {
            "overdue" => ("Карточка долго без изменений", "warning"),
            "review" => ("Работа техника ожидает проверки", "review"),
            "assignment" => ("Вам назначена карточка", "assignment"),
            "new" => ("Новая ошибка без ответственного", "new"),
            _ => (string.Empty, string.Empty)
        };
        if (string.IsNullOrEmpty(title))
            return null;

        return new AlertNotificationItemDto(
            descriptor.NotificationId,
            alert.Id,
            descriptor.Type,
            title,
            message,
            tone,
            descriptor.OccurredAt,
            alert.CameraId,
            alert.Camera?.IpAddress,
            $"/Alerts?view=active&alert={alert.Id}",
            true);
    }

    private static ReadNotificationDescriptor? ParseNotificationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 96 ||
            !NotificationIdPattern.IsMatch(value))
        {
            return null;
        }

        var parts = value.Split(':', 3);
        if (!int.TryParse(parts[0], out var alertId) || alertId <= 0 ||
            !long.TryParse(parts[2], out var ticks) ||
            ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            return null;
        }

        return new ReadNotificationDescriptor(
            value,
            alertId,
            parts[1],
            new DateTime(ticks, DateTimeKind.Utc));
    }

    private static bool IsValidReadNotificationId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 96 &&
        (NotificationIdPattern.IsMatch(value) || NoteNotificationIdPattern.IsMatch(value));

    private sealed record ReadNotificationDescriptor(
        string NotificationId,
        int AlertId,
        string Type,
        DateTime OccurredAt);
}

public sealed class AlertNotificationPreferencesDto
{
    public bool SoundEnabled { get; set; } = true;
    public bool PopupEnabled { get; set; } = true;
    public bool AssignmentsEnabled { get; set; } = true;
    public bool ReviewRequestsEnabled { get; set; } = true;
    public bool OverdueEnabled { get; set; } = true;
    public bool HandoffsEnabled { get; set; } = true;
    public bool CalendarNotesEnabled { get; set; } = true;
    public int RecentWindowMinutes { get; set; } = 60;
    public int OverdueHours { get; set; } = 4;
    public int HandoffDueHours { get; set; } = 24;
    public int HandoffRepeatHours { get; set; } = 4;
}

public sealed record AlertNotificationItemDto(
    string NotificationId,
    int? AlertId,
    string Type,
    string Title,
    string Message,
    string Tone,
    DateTime OccurredAt,
    int? CameraId,
    string? Address,
    string Url,
    bool IsRead);

public sealed record AlertNotificationFeedDto(int Count, IReadOnlyList<AlertNotificationItemDto> Items,
    bool SoundEnabled = true, bool PopupEnabled = true);

public sealed class MarkNotificationReadDto
{
    public string NotificationId { get; set; } = string.Empty;
}
