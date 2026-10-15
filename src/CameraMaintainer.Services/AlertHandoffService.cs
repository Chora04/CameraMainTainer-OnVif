using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CameraMaintainer.Services;

public sealed class AlertHandoffService : IAlertHandoffService
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly ILogger<AlertHandoffService> _logger;

    public AlertHandoffService(AppDbContext db, ILogger<AlertHandoffService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<AlertHandoff>> GetForAlertAsync(int alertId, int actorUserId)
    {
        var actor = await GetActiveUserAsync(actorUserId);
        var alert = await _db.Alerts
            .AsNoTracking()
            .Include(item => item.Handoffs)
            .SingleOrDefaultAsync(item => item.Id == alertId)
            ?? throw new KeyNotFoundException("Ошибка не найдена");

        EnsureMayView(alert, actor);
        return alert.Handoffs
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToList();
    }

    public async Task<List<AlertHandoffReason>> GetReasonsAsync(bool includeInactive = false)
    {
        var query = _db.AlertHandoffReasons.AsNoTracking();
        if (!includeInactive)
            query = query.Where(item => item.IsActive);

        return await query
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name)
            .ToListAsync();
    }

    public async Task<AlertHandoff> CreateAsync(
        int alertId,
        int toUserId,
        int reasonId,
        string comment,
        int dueHours,
        int actorUserId)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var actor = await GetActiveUserAsync(actorUserId);
        var recipient = await GetActiveUserAsync(toUserId);
        var alert = await GetActiveAlertAsync(alertId);
        EnsureMayHandle(actor);
        EnsureRecipientCanHandle(recipient);
        EnsureMayStartHandoff(alert, actor);
        EnsureDifferentUsers(actor, recipient);
        await EnsureNoOpenHandoffAsync(alertId);
        var reason = await GetActiveReasonAsync(reasonId);

        var handoff = CreateHandoff(alert, null, actor, recipient, reason, comment, dueHours);
        _db.AlertHandoffs.Add(handoff);
        AddAudit(alert, actor, "handoff_created",
            $"Работа передана сотруднику {recipient.DisplayName}: {reason.Name}", handoff);
        AddNotification(alert, handoff, recipient, "handoff_received",
            "Вам передана работа по ошибке",
            $"{CameraLabel(alert)}: {reason.Name}",
            "assignment");
        alert.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        _logger.LogInformation("Alert {AlertId} handed off from {FromUser} to {ToUser}", alertId, actor.Username, recipient.Username);
        return handoff;
    }

    public async Task<AlertHandoff> ViewAsync(long handoffId, int actorUserId)
    {
        var actor = await GetActiveUserAsync(actorUserId);
        var handoff = await GetHandoffAsync(handoffId);
        EnsureRecipient(handoff, actor);

        if (handoff.ViewedAt.HasValue)
            return handoff;

        var now = DateTime.UtcNow;
        handoff.ViewedAt = now;
        await MarkNotificationReadAsync(handoff.Id, actor.Id, now);
        AddAudit(handoff.Alert!, actor, "handoff_viewed", $"Передача просмотрена сотрудником {actor.DisplayName}", handoff);
        handoff.Alert!.UpdatedAt = now;
        await _db.SaveChangesAsync();
        return handoff;
    }

    public async Task<AlertHandoff> AcceptAsync(long handoffId, string? comment, int actorUserId)
    {
        var actor = await GetActiveUserAsync(actorUserId);
        var handoff = await GetHandoffAsync(handoffId);
        EnsureRecipient(handoff, actor);
        EnsureState(handoff, AlertHandoffStates.Pending, "Передача уже обработана");

        var now = DateTime.UtcNow;
        handoff.ViewedAt ??= now;
        handoff.AcceptedAt = now;
        handoff.AcceptanceComment = NormalizeOptionalComment(comment);
        handoff.State = AlertHandoffStates.Accepted;
        await MarkNotificationReadAsync(handoff.Id, actor.Id, now);
        AddAudit(handoff.Alert!, actor, "handoff_accepted", $"{actor.DisplayName} принял переданную работу", handoff);
        handoff.Alert!.UpdatedAt = now;
        await _db.SaveChangesAsync();
        return handoff;
    }

    public async Task<AlertHandoff> ForwardAsync(
        long handoffId,
        int toUserId,
        int reasonId,
        string comment,
        int dueHours,
        int actorUserId)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var actor = await GetActiveUserAsync(actorUserId);
        var recipient = await GetActiveUserAsync(toUserId);
        var current = await GetHandoffAsync(handoffId);
        EnsureRecipient(current, actor);
        EnsureState(current, AlertHandoffStates.Accepted, "Сначала примите переданную работу");
        EnsureDifferentUsers(actor, recipient);
        EnsureRecipientCanHandle(recipient);
        var reason = await GetActiveReasonAsync(reasonId);
        var normalizedComment = NormalizeRequiredComment(comment);
        var now = DateTime.UtcNow;

        current.State = AlertHandoffStates.Forwarded;
        current.CompletedAt = now;
        current.CompletionComment = normalizedComment;
        await ResolveNotificationsAsync(current.Id, now);

        var next = CreateHandoff(current.Alert!, current.Id, actor, recipient, reason, normalizedComment, dueHours);
        _db.AlertHandoffs.Add(next);
        AddAudit(current.Alert!, actor, "handoff_forwarded",
            $"Работа передана дальше сотруднику {recipient.DisplayName}: {reason.Name}", next);
        AddNotification(current.Alert!, next, recipient, "handoff_received",
            "Вам передана работа по ошибке",
            $"{CameraLabel(current.Alert!)}: {reason.Name}",
            "assignment");
        current.Alert!.UpdatedAt = now;

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return next;
    }

    public async Task<AlertHandoff> CompleteAsync(long handoffId, string comment, int actorUserId)
    {
        var actor = await GetActiveUserAsync(actorUserId);
        var handoff = await GetHandoffAsync(handoffId);
        EnsureRecipient(handoff, actor);
        EnsureState(handoff, AlertHandoffStates.Accepted, "Сначала примите переданную работу");
        var normalizedComment = NormalizeRequiredComment(comment);
        var now = DateTime.UtcNow;

        handoff.State = AlertHandoffStates.Completed;
        handoff.CompletedAt = now;
        handoff.CompletionComment = normalizedComment;
        await ResolveNotificationsAsync(handoff.Id, now);
        AddAudit(handoff.Alert!, actor, "handoff_completed", $"{actor.DisplayName} завершил переданную работу", handoff);
        AddNotification(handoff.Alert!, handoff, new AppUser
        {
            Id = handoff.FromUserId,
            Username = handoff.FromUsername,
            DisplayName = handoff.FromDisplayName
        }, "handoff_completed", "Переданная работа завершена",
            $"{CameraLabel(handoff.Alert!)}: {handoff.ReasonName}", "review");
        handoff.Alert!.UpdatedAt = now;
        await _db.SaveChangesAsync();
        return handoff;
    }

    public async Task<AlertHandoff> RejectAsync(long handoffId, string comment, int actorUserId)
    {
        var actor = await GetActiveUserAsync(actorUserId);
        var handoff = await GetHandoffAsync(handoffId);
        EnsureRecipient(handoff, actor);
        if (!AlertHandoffStates.IsOpen(handoff.State))
            throw new InvalidOperationException("Передача уже обработана");
        var normalizedComment = NormalizeRequiredComment(comment);
        var now = DateTime.UtcNow;

        handoff.ViewedAt ??= now;
        handoff.State = AlertHandoffStates.Rejected;
        handoff.CompletedAt = now;
        handoff.CompletionComment = normalizedComment;
        await ResolveNotificationsAsync(handoff.Id, now);
        AddAudit(handoff.Alert!, actor, "handoff_rejected", $"{actor.DisplayName} вернул переданную работу", handoff);
        AddNotification(handoff.Alert!, handoff, new AppUser
        {
            Id = handoff.FromUserId,
            Username = handoff.FromUsername,
            DisplayName = handoff.FromDisplayName
        }, "handoff_rejected", "Переданная работа возвращена",
            $"{CameraLabel(handoff.Alert!)}: {normalizedComment}", "warning");
        handoff.Alert!.UpdatedAt = now;
        await _db.SaveChangesAsync();
        return handoff;
    }

    public async Task<AlertHandoffReason> SaveReasonAsync(int? reasonId, string name, bool isActive, int actorUserId)
    {
        var actor = await GetActiveUserAsync(actorUserId);
        if (!IsAdministrator(actor))
            throw new UnauthorizedAccessException("Настраивать внутренние статусы может только администратор");

        var normalizedName = NormalizeReasonName(name);
        AlertHandoffReason reason;
        if (reasonId.HasValue)
        {
            reason = await _db.AlertHandoffReasons.SingleOrDefaultAsync(item => item.Id == reasonId.Value)
                ?? throw new KeyNotFoundException("Внутренний статус не найден");
            if (!isActive && reason.IsActive)
            {
                var isUsedByOpenHandoff = await _db.AlertHandoffs.AnyAsync(item =>
                    item.ReasonId == reason.Id &&
                    (item.State == AlertHandoffStates.Pending || item.State == AlertHandoffStates.Accepted));
                if (isUsedByOpenHandoff)
                    throw new InvalidOperationException("Статус используется в незавершённой передаче и не может быть отключён");
            }

            reason.Name = normalizedName;
            reason.IsActive = isActive;
            reason.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var maxOrder = await _db.AlertHandoffReasons.MaxAsync(item => (int?)item.SortOrder) ?? 0;
            reason = new AlertHandoffReason
            {
                Key = $"custom-{Guid.NewGuid():N}",
                Name = normalizedName,
                IsActive = isActive,
                SortOrder = maxOrder + 10
            };
            _db.AlertHandoffReasons.Add(reason);
        }

        await _db.SaveChangesAsync();
        return reason;
    }

    private async Task<AppUser> GetActiveUserAsync(int userId) =>
        await _db.AppUsers.SingleOrDefaultAsync(item => item.Id == userId && item.IsActive)
        ?? throw new KeyNotFoundException("Аккаунт не найден или отключён");

    private async Task<Alert> GetActiveAlertAsync(int alertId)
    {
        var alert = await _db.Alerts
            .Include(item => item.Camera)
            .SingleOrDefaultAsync(item => item.Id == alertId)
            ?? throw new KeyNotFoundException("Ошибка не найдена");
        if (alert.AutoResolved)
            throw new InvalidOperationException("Ошибка уже закрыта");
        return alert;
    }

    private async Task<AlertHandoff> GetHandoffAsync(long handoffId) =>
        await _db.AlertHandoffs
            .Include(item => item.Alert)
                .ThenInclude(alert => alert!.Camera)
            .SingleOrDefaultAsync(item => item.Id == handoffId)
        ?? throw new KeyNotFoundException("Передача не найдена");

    private async Task<AlertHandoffReason> GetActiveReasonAsync(int reasonId) =>
        await _db.AlertHandoffReasons.SingleOrDefaultAsync(item => item.Id == reasonId && item.IsActive)
        ?? throw new KeyNotFoundException("Внутренний статус не найден или отключён");

    private async Task EnsureNoOpenHandoffAsync(int alertId)
    {
        var hasOpen = await _db.AlertHandoffs.AnyAsync(item => item.AlertId == alertId &&
            (item.State == AlertHandoffStates.Pending || item.State == AlertHandoffStates.Accepted));
        if (hasOpen)
            throw new InvalidOperationException("Сначала завершите текущую передачу работы");
    }

    private static AlertHandoff CreateHandoff(
        Alert alert,
        long? parentId,
        AppUser actor,
        AppUser recipient,
        AlertHandoffReason reason,
        string comment,
        int dueHours)
    {
        var now = DateTime.UtcNow;
        return new AlertHandoff
        {
            Alert = alert,
            AlertId = alert.Id,
            ParentHandoffId = parentId,
            Reason = reason,
            ReasonId = reason.Id,
            ReasonName = reason.Name,
            FromUserId = actor.Id,
            FromUsername = actor.Username,
            FromDisplayName = actor.DisplayName,
            ToUserId = recipient.Id,
            ToUsername = recipient.Username,
            ToDisplayName = recipient.DisplayName,
            Comment = NormalizeRequiredComment(comment),
            State = AlertHandoffStates.Pending,
            CreatedAt = now,
            DueAt = now.AddHours(Math.Clamp(dueHours, 1, 720))
        };
    }

    private void AddAudit(Alert alert, AppUser actor, string eventType, string summary, AlertHandoff handoff)
    {
        var state = AlertAuditEvent.CaptureState(alert);
        var detail = JsonNode.Parse(state)!.AsObject();
        detail["handoff"] = JsonSerializer.SerializeToNode(handoff, AuditJsonOptions);
        _db.AlertAuditEvents.Add(new AlertAuditEvent
        {
            Alert = alert,
            AlertId = alert.Id,
            EventType = eventType,
            ActorUserId = actor.Id,
            ActorUsername = actor.Username,
            ActorDisplayName = actor.DisplayName,
            Summary = summary,
            FromStage = alert.WorkflowStage,
            ToStage = alert.WorkflowStage,
            PreviousValuesJson = state,
            NewValuesJson = detail.ToJsonString(),
            CreatedAt = DateTime.UtcNow
        });
    }

    private void AddNotification(
        Alert alert,
        AlertHandoff handoff,
        AppUser recipient,
        string type,
        string title,
        string message,
        string tone)
    {
        _db.UserNotifications.Add(new UserNotification
        {
            UserId = recipient.Id,
            Alert = alert,
            AlertId = alert.Id,
            Handoff = handoff,
            HandoffId = handoff.Id,
            Type = type,
            Title = title,
            Message = message,
            Tone = tone,
            Url = $"/Alerts?view=active&alert={alert.Id}",
            CreatedAt = DateTime.UtcNow,
            ReminderAt = handoff.DueAt
        });
    }

    private async Task MarkNotificationReadAsync(long handoffId, int userId, DateTime now)
    {
        var notifications = await _db.UserNotifications
            .Where(item => item.HandoffId == handoffId && item.UserId == userId && !item.ResolvedAt.HasValue)
            .ToListAsync();
        foreach (var notification in notifications)
            notification.ReadAt ??= now;
    }

    private async Task ResolveNotificationsAsync(long handoffId, DateTime now)
    {
        var notifications = await _db.UserNotifications
            .Where(item => item.HandoffId == handoffId && !item.ResolvedAt.HasValue)
            .ToListAsync();
        foreach (var notification in notifications)
        {
            notification.ResolvedAt = now;
            notification.ReadAt ??= now;
        }
    }

    private static string CameraLabel(Alert alert) =>
        alert.Camera?.Name ?? (alert.CameraId.HasValue ? $"Камера {alert.CameraId}" : "Системная ошибка");

    private static void EnsureMayStartHandoff(Alert alert, AppUser actor)
    {
        var assignedToActor = alert.AssignedToUserId == actor.Id ||
            (!alert.AssignedToUserId.HasValue && string.Equals(alert.AssignedToUsername, actor.Username, StringComparison.OrdinalIgnoreCase));
        if (!assignedToActor && !IsAdministrator(actor))
            throw new UnauthorizedAccessException("Передавать работу может ответственный или администратор");
    }

    private static void EnsureMayView(Alert alert, AppUser actor)
    {
        var assignedToActor = alert.AssignedToUserId == actor.Id ||
            (!alert.AssignedToUserId.HasValue && string.Equals(alert.AssignedToUsername, actor.Username, StringComparison.OrdinalIgnoreCase));
        var participant = alert.Handoffs.Any(item => item.FromUserId == actor.Id || item.ToUserId == actor.Id);
        var isNew = !alert.AutoResolved && !alert.AssignedToUserId.HasValue;
        if (!assignedToActor && !participant && !isNew && !IsAdministrator(actor))
            throw new UnauthorizedAccessException("У вас нет доступа к цепочке передачи этой ошибки");
    }

    private static void EnsureMayHandle(AppUser actor)
    {
        if (!CanHandle(actor))
            throw new UnauthorizedAccessException("У аккаунта нет доступа к работе с ошибками");
    }

    private static void EnsureRecipientCanHandle(AppUser recipient)
    {
        if (!CanHandle(recipient))
            throw new InvalidOperationException("Получатель не может работать с ошибками");
    }

    private static void EnsureRecipient(AlertHandoff handoff, AppUser actor)
    {
        EnsureMayHandle(actor);
        if (handoff.Alert?.AutoResolved == true)
            throw new InvalidOperationException("Ошибка уже закрыта");
        if (handoff.ToUserId != actor.Id)
            throw new UnauthorizedAccessException("Это действие доступно только получателю передачи");
    }

    private static void EnsureDifferentUsers(AppUser actor, AppUser recipient)
    {
        if (actor.Id == recipient.Id)
            throw new InvalidOperationException("Нельзя передать работу самому себе");
    }

    private static void EnsureState(AlertHandoff handoff, string expected, string message)
    {
        if (!string.Equals(handoff.State, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(message);
    }

    private static bool CanHandle(AppUser user) =>
        user.IsActive && user.CanViewAlerts && user.CanRunChecks;

    private static bool IsAdministrator(AppUser user) =>
        user.CanManageUsers && string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRequiredComment(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Добавьте комментарий к передаче");
        if (normalized.Length > 2000)
            throw new InvalidOperationException("Комментарий не может быть длиннее 2000 символов");
        return normalized;
    }

    private static string? NormalizeOptionalComment(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        if (normalized.Length > 2000)
            throw new InvalidOperationException("Комментарий не может быть длиннее 2000 символов");
        return normalized;
    }

    private static string NormalizeReasonName(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Введите название внутреннего статуса");
        if (normalized.Length > 128)
            throw new InvalidOperationException("Название не может быть длиннее 128 символов");
        return normalized;
    }
}
