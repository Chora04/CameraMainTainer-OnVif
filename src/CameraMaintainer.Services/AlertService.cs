using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Services;

public class AlertService : IAlertService
{
    private readonly AlertRepository _alertRepo;
    private readonly AlertBoardRepository? _alertBoardRepo;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        AlertRepository alertRepo,
        ILogger<AlertService> logger,
        AlertBoardRepository? alertBoardRepo = null)
    {
        _alertRepo = alertRepo;
        _logger = logger;
        _alertBoardRepo = alertBoardRepo;
    }

    public async Task<Alert> CreateAlertAsync(int? cameraId, AlertType alertType, AlertSeverity severity,
        string title, string? description = null, AlertDiagnostic? diagnostic = null)
    {
        var gate = CameraAlertLocks.ForCamera(cameraId);
        await gate.WaitAsync();
        try { return await CreateOrAppendAsync(cameraId, alertType, severity, title, description, diagnostic); }
        finally { gate.Release(); }
    }

    private async Task<Alert> CreateOrAppendAsync(int? cameraId, AlertType alertType, AlertSeverity severity,
        string title, string? description, AlertDiagnostic? diagnostic)
    {
        if (severity == AlertSeverity.Info)
        {
            _logger.LogInformation(
                "Informational event not persisted: {Title} (Camera: {CameraId})",
                title,
                cameraId);
            return new Alert { Id = -1 };
        }

        // One open case per camera; individual faults keep independent recovery times.
        var existing = cameraId.HasValue
            ? await _alertRepo.GetActiveForCameraAsync(cameraId.Value)
            : null;
        if (existing != null)
        {
            var issue = existing.Issues.FirstOrDefault(item => item.AlertType == alertType && item.RecoveredAt == null);
            if (issue == null)
            {
                var previousValues = AlertAuditEvent.CaptureState(existing);
                var fromStage = existing.WorkflowStage;
                var isRecurrence = existing.Issues.Any(item => item.AlertType == alertType);
                var addedIssue = new AlertIssue
                {
                    AlertType = alertType,
                    Category = Classify(alertType),
                    Severity = severity,
                    Title = title,
                    Description = description,
                    StartedAt = DateTime.UtcNow
                };
                ApplyDiagnostic(addedIssue, diagnostic);
                existing.Issues.Add(addedIssue);
                existing.RecoveryDetectedAt = null;
                if (fromStage is "ready" or "review")
                    existing.WorkflowStage = existing.AssignedToUserId.HasValue ? "working" : "new";
                AlertRepository.RefreshSummary(existing);
                await _alertRepo.ResetStabilityAsync(cameraId!.Value);
                await _alertRepo.UpdateAsync(existing, CreateAuditEvent(
                    existing, isRecurrence ? "recovery_lost" : "issue_added", actor: null,
                    (isRecurrence ? "Неисправность обнаружена повторно: " : "В карточку добавлена ошибка: ") + title,
                    previousValues, fromStage, existing.WorkflowStage));
                return existing;
            }
            if (severity > issue.Severity)
            {
                var previousValues = AlertAuditEvent.CaptureState(existing);
                issue.Severity = severity;
                issue.Title = title;
                issue.Description = description;
                ApplyDiagnostic(issue, diagnostic);
                AlertRepository.RefreshSummary(existing);
                await _alertRepo.ResetStabilityAsync(cameraId!.Value);
                await _alertRepo.UpdateAsync(existing, CreateAuditEvent(existing, "issue_updated", null,
                    "Повышена важность ошибки: " + title, previousValues, existing.WorkflowStage, existing.WorkflowStage));
                return existing;
            }
            if (ApplyMissingDiagnostic(issue, diagnostic))
            {
                await _alertRepo.UpdateAsync(existing);
                _logger.LogDebug(
                    "Added camera-check diagnostics to existing alert {AlertId} for camera {CameraId}",
                    existing.Id,
                    cameraId);
                return existing;
            }
            _logger.LogDebug("Skipping duplicate alert {AlertType} for camera {CameraId}", alertType, cameraId);
            return new Alert { Id = -1 }; // Sentinel for "already exists"
        }

        var alert = new Alert
        {
            CameraId = cameraId,
            CameraSequenceNumber = await _alertRepo.ReserveNextCameraSequenceAsync(cameraId),
            AlertType = alertType,
            Category = Classify(alertType),
            Severity = severity,
            Title = title,
            Description = description,
            CreatedAt = DateTime.UtcNow
        };

        AlertRepository.EnsureIssues(alert);
        ApplyDiagnostic(alert.Issues.Single(), diagnostic);
        if (cameraId.HasValue) await _alertRepo.ResetStabilityAsync(cameraId.Value);
        await _alertRepo.AddAsync(alert, CreateAuditEvent(
            alert,
            "created",
            actor: null,
            "Ошибка создана системой",
            previousValues: null,
            fromStage: null,
            toStage: alert.WorkflowStage));
        _logger.LogWarning("Alert created: [{Severity}] {Title} (Camera: {CameraId})", severity, title, cameraId);

        return alert;
    }

    private static void ApplyDiagnostic(AlertIssue issue, AlertDiagnostic? diagnostic)
    {
        if (diagnostic == null) return;

        issue.FailureCode = diagnostic.FailureCode;
        issue.FailureStage = diagnostic.FailureStage;
        issue.FailureDurationMs = diagnostic.DurationMs;
        issue.FailureTimeoutMs = diagnostic.TimeoutMs;
    }

    private static bool ApplyMissingDiagnostic(AlertIssue issue, AlertDiagnostic? diagnostic)
    {
        if (diagnostic == null) return false;

        var changed = false;
        if (string.IsNullOrWhiteSpace(issue.FailureCode) && !string.IsNullOrWhiteSpace(diagnostic.FailureCode))
        {
            issue.FailureCode = diagnostic.FailureCode;
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(issue.FailureStage) && !string.IsNullOrWhiteSpace(diagnostic.FailureStage))
        {
            issue.FailureStage = diagnostic.FailureStage;
            changed = true;
        }
        if (!issue.FailureDurationMs.HasValue && diagnostic.DurationMs.HasValue)
        {
            issue.FailureDurationMs = diagnostic.DurationMs;
            changed = true;
        }
        if (!issue.FailureTimeoutMs.HasValue && diagnostic.TimeoutMs.HasValue)
        {
            issue.FailureTimeoutMs = diagnostic.TimeoutMs;
            changed = true;
        }
        return changed;
    }

    public async Task<List<Alert>> GetCameraAlertHistoryAsync(int cameraId, int actorUserId, int limit = 30)
    {
        var actor = await GetActiveUserAsync(actorUserId);
        var boundedLimit = Math.Clamp(limit, 1, 100);
        var alerts = IsAdministrator(actor)
            ? await _alertRepo.GetForCameraAsync(cameraId, boundedLimit)
            : await _alertRepo.GetForCameraAsync(cameraId, boundedLimit, actor.Id, actor.Username);

        foreach (var alert in alerts)
            alert.AuditEvents = alert.AuditEvents.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id).ToList();

        return alerts;
    }

    public async Task<List<AlertAuditEvent>> GetAlertAuditAsync(int alertId, int actorUserId)
    {
        var actor = await GetActiveUserAsync(actorUserId);
        var alert = await _alertRepo.GetByIdAsync(alertId)
            ?? throw new KeyNotFoundException("Ошибка не найдена");

        var assignedToActor = alert.AssignedToUserId == actor.Id ||
            (!alert.AssignedToUserId.HasValue && string.Equals(alert.AssignedToUsername, actor.Username, StringComparison.OrdinalIgnoreCase));
        var handoffParticipant = alert.Handoffs.Any(item =>
            item.FromUserId == actor.Id || item.ToUserId == actor.Id);
        if (!IsAdministrator(actor) && !assignedToActor && !handoffParticipant)
            throw new UnauthorizedAccessException("История этой ошибки назначена другому менеджеру");

        return await _alertRepo.GetAuditEventsAsync(alertId);
    }

    public async Task RecordAlertViewedAsync(int alertId, int actorUserId)
    {
        var actor = await GetActiveUserAsync(actorUserId);
        var alert = await _alertRepo.GetByIdAsync(alertId)
            ?? throw new KeyNotFoundException("Ошибка не найдена");
        var assignedToActor = alert.AssignedToUserId == actor.Id ||
            (!alert.AssignedToUserId.HasValue && string.Equals(alert.AssignedToUsername, actor.Username, StringComparison.OrdinalIgnoreCase));
        var handoffParticipant = alert.Handoffs.Any(item =>
            item.FromUserId == actor.Id || item.ToUserId == actor.Id);
        var unassignedActiveAlert = !alert.AutoResolved && !alert.AssignedToUserId.HasValue;

        if (!IsAdministrator(actor) && !assignedToActor && !handoffParticipant && !unassignedActiveAlert)
            throw new UnauthorizedAccessException("Ошибка назначена другому менеджеру");

        if (await _alertRepo.HasAuditEventAsync(alertId, actor.Id, "viewed"))
            return;

        await _alertRepo.AddAuditEventAsync(CreateAuditEvent(
            alert,
            "viewed",
            actor,
            "Карточка ошибки открыта сотрудником",
            AlertAuditEvent.CaptureState(alert),
            alert.WorkflowStage,
            alert.WorkflowStage));
    }

    public async Task<Alert> AssignAlertAsync(int alertId, int assigneeUserId, int actorUserId)
    {
        var actor = await GetActiveUserAsync(actorUserId);
        var assignee = await GetActiveUserAsync(assigneeUserId);
        EnsureMayHandleErrors(actor);

        if (!IsAdministrator(actor) && assignee.Id != actor.Id &&
            !(IsManager(actor) && string.Equals(assignee.Role, "Technician", StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("Назначать другого сотрудника может только менеджер или администратор");
        if (!CanHandleErrors(assignee))
            throw new InvalidOperationException("У выбранного аккаунта нет доступа к работе с ошибками");

        var alert = await GetActiveAlertAsync(alertId);
        if (alert.AssignedToUserId.HasValue &&
            alert.AssignedToUserId != actor.Id &&
            !IsAdministrator(actor))
        {
            throw new UnauthorizedAccessException("Ошибка уже назначена другому сотруднику");
        }

        var previousValues = AlertAuditEvent.CaptureState(alert);
        var fromStage = alert.WorkflowStage;
        ApplyAssignment(alert, assignee, actor.Username);
        if (string.IsNullOrWhiteSpace(alert.WorkflowStage) || alert.WorkflowStage == "new")
        {
            var columns = _alertBoardRepo == null ? AlertBoardColumnDefaults.Create() : await _alertBoardRepo.GetColumnsAsync();
            var assignedColumn = columns.FirstOrDefault(column => column.Key == "assigned");
            if (assignedColumn != null && AlertBoardRoleVisibility.IsVisibleTo(assignedColumn,
                assignee.Role, IsAdministrator(assignee), assignee.AlertBoardColumnKeys))
                alert.WorkflowStage = "assigned";
        }
        await _alertRepo.UpdateAsync(alert, CreateAuditEvent(
            alert,
            "assigned",
            actor,
            $"Ответственным назначен {assignee.DisplayName}",
            previousValues,
            fromStage,
            alert.WorkflowStage));
        _logger.LogInformation(
            "Alert {AlertId} assigned to {Assignee} by {Actor}",
            alertId,
            assignee.Username,
            actor.Username);
        return alert;
    }

    public async Task<Alert> MoveAlertAsync(int alertId, string stage, int actorUserId)
    {
        var actor = await GetActiveUserAsync(actorUserId);
        EnsureMayHandleErrors(actor);
        var alert = await GetActiveAlertAsync(alertId);
        var previousValues = AlertAuditEvent.CaptureState(alert);
        var fromStage = alert.WorkflowStage;
        var columns = _alertBoardRepo == null
            ? AlertBoardColumnDefaults.Create()
            : await _alertBoardRepo.GetColumnsAsync();
        var targetColumn = columns.FirstOrDefault(column =>
            column.Key.Equals(stage?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Колонка для этого этапа не найдена");
        var targetStage = targetColumn.Key;
        if (!AlertBoardRoleVisibility.IsVisibleTo(targetColumn, actor.Role, IsAdministrator(actor), actor.AlertBoardColumnKeys))
            throw new UnauthorizedAccessException("Целевой этап скрыт настройками доступа аккаунта");
        var sourceTitle = columns.FirstOrDefault(column =>
            column.Key.Equals(fromStage, StringComparison.OrdinalIgnoreCase))?.Title ?? fromStage ?? "Без этапа";

        if (alert.AssignedToUserId.HasValue &&
            alert.AssignedToUserId != actor.Id &&
            !IsAdministrator(actor))
        {
            throw new UnauthorizedAccessException("Ошибка назначена другому менеджеру");
        }

        switch (targetStage.ToLowerInvariant())
        {
            case "new":
                alert.AssignedToUserId = null;
                alert.AssignedToUsername = null;
                alert.AssignedToDisplayName = null;
                alert.AssignedByUsername = null;
                alert.AssignedAt = null;
                alert.WorkReport = null;
                break;

            case "working":
                if (!alert.AssignedToUserId.HasValue)
                    ApplyAssignment(alert, actor, actor.Username);
                break;

            case "assigned":
                if (!alert.AssignedToUserId.HasValue)
                    ApplyAssignment(alert, actor, actor.Username);
                break;

            case "waiting":
                if (!alert.AssignedToUserId.HasValue)
                    ApplyAssignment(alert, actor, actor.Username);
                break;

            case "review":
                if (!HasWorkReport(alert))
                    throw new InvalidOperationException("Перед передачей на проверку добавьте сообщение в отчёт о работе");
                if (!alert.AssignedToUserId.HasValue)
                    ApplyAssignment(alert, actor, actor.Username);
                break;

            case "ready":
                if (!HasWorkReport(alert))
                    throw new InvalidOperationException("Перед переносом в «Готово к закрытию» добавьте сообщение в отчёт о работе");
                if (!IsManager(actor))
                    throw new UnauthorizedAccessException("Подтвердить работу и подготовить карточку к закрытию может только менеджер");
                if (!alert.AssignedToUserId.HasValue)
                    ApplyAssignment(alert, actor, actor.Username);
                break;

            default:
                if (!alert.AssignedToUserId.HasValue)
                    ApplyAssignment(alert, actor, actor.Username);
                alert.WorkflowStage = targetStage;
                break;
        }

        alert.WorkflowStage = targetStage;

        await _alertRepo.UpdateAsync(alert, CreateAuditEvent(
            alert,
            "stage_changed",
            actor,
            $"Карточка перемещена: «{sourceTitle}» → «{targetColumn.Title}»",
            previousValues,
            fromStage,
            targetStage));
        _logger.LogInformation("Alert {AlertId} moved to {Stage} by {Actor}", alertId, targetStage, actor.Username);
        return alert;
    }

    public async Task<Alert> UpdateAlertWorkAsync(
        int alertId,
        ErrorCategory category,
        string? workReport,
        bool serviceNotified,
        int actorUserId)
    {
        if (!Enum.IsDefined(category))
            throw new InvalidOperationException("Категория должна быть физической или технической");
        var normalizedReport = workReport?.Trim();
        if (normalizedReport?.Length > 4000)
            throw new InvalidOperationException("Отчёт не может быть длиннее 4000 символов");
        var actor = await GetActiveUserAsync(actorUserId);
        EnsureMayHandleErrors(actor);
        var alert = await GetActiveAlertAsync(alertId);
        var previousValues = AlertAuditEvent.CaptureState(alert);
        var previousCategory = alert.Category;
        var previousReport = alert.WorkReport;
        var previousServiceNotified = alert.ServiceNotified;
        var fromStage = alert.WorkflowStage;

        if (alert.AssignedToUserId.HasValue &&
            alert.AssignedToUserId != actor.Id &&
            !IsAdministrator(actor))
        {
            throw new UnauthorizedAccessException("Ошибка назначена другому менеджеру");
        }

        if (!alert.AssignedToUserId.HasValue)
            ApplyAssignment(alert, actor, actor.Username);

        alert.Category = category;
        alert.WorkReport = string.IsNullOrWhiteSpace(normalizedReport) ? null : normalizedReport;
        if (string.IsNullOrWhiteSpace(alert.WorkflowStage) ||
            alert.WorkflowStage.Equals("new", StringComparison.OrdinalIgnoreCase) ||
            alert.WorkflowStage.Equals("assigned", StringComparison.OrdinalIgnoreCase) ||
            alert.WorkflowStage.Equals("working", StringComparison.OrdinalIgnoreCase))
        {
            alert.WorkflowStage = alert.WorkReport == null ? "working" : "review";
        }

        if (category == ErrorCategory.Physical && serviceNotified)
        {
            if (!alert.ServiceNotified)
            {
                alert.ServiceNotifiedAt = DateTime.UtcNow;
                alert.ServiceNotifiedByUsername = actor.Username;
            }
            alert.ServiceNotified = true;
        }
        else
        {
            alert.ServiceNotified = false;
            alert.ServiceNotifiedAt = null;
            alert.ServiceNotifiedByUsername = null;
        }

        var changes = new List<string>();
        if (previousCategory != alert.Category)
            changes.Add($"тип: {CategoryLabel(previousCategory)} → {CategoryLabel(alert.Category)}");
        if (!string.Equals(previousReport, alert.WorkReport, StringComparison.Ordinal))
            changes.Add(alert.WorkReport == null
                ? "отчёт удалён"
                : previousReport == null ? "отчёт добавлен" : "отчёт изменён");
        if (previousServiceNotified != alert.ServiceNotified)
            changes.Add(alert.ServiceNotified ? "профильная служба уведомлена" : "отметка уведомления снята");
        var summary = changes.Count == 0
            ? "Данные работы сохранены без изменения"
            : "Изменены данные работы: " + string.Join(", ", changes);

        await _alertRepo.UpdateAsync(alert, CreateAuditEvent(
            alert,
            "work_updated",
            actor,
            summary,
            previousValues,
            fromStage,
            alert.WorkflowStage));
        _logger.LogInformation("Alert {AlertId} work report updated by {Actor}", alertId, actor.Username);
        return alert;
    }

    public async Task<Alert> ResolveAlertAsync(int alertId, int actorUserId)
    {
        var gate = CameraAlertLocks.ForCamera(await _alertRepo.GetCameraIdAsync(alertId));
        await gate.WaitAsync();
        try { return await ResolveAlertCoreAsync(alertId, actorUserId); }
        finally { gate.Release(); }
    }

    private async Task<Alert> ResolveAlertCoreAsync(int alertId, int actorUserId)
    {
        var actor = await GetActiveUserAsync(actorUserId);
        EnsureMayHandleErrors(actor);
        if (!IsManager(actor))
            throw new UnauthorizedAccessException("Закрыть заявку может только менеджер или администратор");
        var alert = await GetActiveAlertAsync(alertId);

        if (alert.Handoffs.Any(item => AlertHandoffStates.IsOpen(item.State)))
            throw new InvalidOperationException("Сначала завершите или верните активную передачу работы");

        if (!alert.AssignedToUserId.HasValue)
            throw new InvalidOperationException("Сначала назначьте ответственного менеджера");
        if (alert.AssignedToUserId != actor.Id && !IsAdministrator(actor))
            throw new UnauthorizedAccessException("Закрыть ошибку может ответственный менеджер или администратор");
        if (!HasWorkReport(alert))
            throw new InvalidOperationException("Перед закрытием добавьте сообщение в отчёт о выполненной работе");
        if (!string.Equals(alert.WorkflowStage, "ready", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Сначала подтвердите работу и перенесите карточку в «Готово к закрытию»");
        if (alert.Category == ErrorCategory.Physical && !alert.ServiceNotified && !alert.RecoveryDetectedAt.HasValue)
            throw new InvalidOperationException("Для физической неисправности отметьте, что профильная служба уведомлена");

        var previousValues = AlertAuditEvent.CaptureState(alert);
        var fromStage = alert.WorkflowStage;
        var now = DateTime.UtcNow;
        alert.AutoResolved = true;
        alert.ResolvedAt = now;
        alert.ResolvedByUsername = actor.Username;
        alert.ResolvedByDisplayName = actor.DisplayName;
        foreach (var issue in alert.Issues.Where(issue => !issue.RecoveredAt.HasValue))
        {
            issue.RecoveredAt = now;
            issue.ResolvedByUserId = actor.Id;
            issue.ResolvedByUsername = actor.Username;
            issue.ResolvedByDisplayName = actor.DisplayName;
        }
        await _alertRepo.UpdateAsync(alert, CreateAuditEvent(
            alert,
            "resolved",
            actor,
            "Ошибка закрыта после выполнения работ",
            previousValues,
            fromStage,
            "closed"));
        _logger.LogInformation("Alert {AlertId} resolved by {Actor}", alertId, actor.Username);
        return alert;
    }

    public async Task MarkConnectivityRecoveredAsync(int cameraId)
    {
        await _alertRepo.MarkConnectivityRecoveredAsync(cameraId);
    }

    public async Task MarkRecoveredAsync(int cameraId, AlertType alertType)
    {
        await _alertRepo.MarkRecoveredAsync(cameraId, alertType);
    }

    private async Task<AppUser> GetActiveUserAsync(int userId)
    {
        return await _alertRepo.GetActiveUserAsync(userId)
            ?? throw new KeyNotFoundException("Аккаунт не найден или отключён");
    }

    private async Task<Alert> GetActiveAlertAsync(int alertId)
    {
        var alert = await _alertRepo.GetByIdAsync(alertId)
            ?? throw new KeyNotFoundException("Ошибка не найдена");
        if (alert.AutoResolved)
            throw new InvalidOperationException("Ошибка уже закрыта");
        return alert;
    }

    private static void EnsureMayHandleErrors(AppUser user)
    {
        if (!CanHandleErrors(user))
            throw new UnauthorizedAccessException("У аккаунта нет доступа к работе с ошибками");
    }

    private static bool CanHandleErrors(AppUser user)
    {
        return user.IsActive && user.CanViewAlerts && user.CanRunChecks;
    }

    private static bool HasWorkReport(Alert alert) =>
        !string.IsNullOrWhiteSpace(alert.WorkReport) ||
        alert.Issues.SelectMany(issue => issue.Messages).Any(message => !string.IsNullOrWhiteSpace(message.Message));

    private static bool IsManager(AppUser user) => IsAdministrator(user) ||
        string.Equals(user.Role, "Operator", StringComparison.OrdinalIgnoreCase);

    private static bool IsAdministrator(AppUser user) =>
        user.CanManageUsers &&
        string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase);

    private static void ApplyAssignment(Alert alert, AppUser assignee, string assignedBy)
    {
        alert.AssignedToUserId = assignee.Id;
        alert.AssignedToUsername = assignee.Username;
        alert.AssignedToDisplayName = assignee.DisplayName;
        alert.AssignedByUsername = assignedBy;
        alert.AssignedAt = DateTime.UtcNow;
    }

    private static AlertAuditEvent CreateAuditEvent(
        Alert alert,
        string eventType,
        AppUser? actor,
        string summary,
        string? previousValues,
        string? fromStage,
        string? toStage)
    {
        return new AlertAuditEvent
        {
            AlertId = alert.Id,
            EventType = eventType,
            ActorUserId = actor?.Id,
            ActorUsername = actor?.Username ?? "system",
            ActorDisplayName = actor?.DisplayName ?? "Система",
            Summary = summary,
            FromStage = fromStage,
            ToStage = toStage,
            PreviousValuesJson = previousValues,
            NewValuesJson = AlertAuditEvent.CaptureState(alert),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static string CategoryLabel(ErrorCategory category) =>
        category == ErrorCategory.Physical ? "физическая" : "техническая";

    private static ErrorCategory Classify(AlertType alertType)
    {
        return alertType is AlertType.LensObstructed or AlertType.TemperatureHigh
            ? ErrorCategory.Physical
            : ErrorCategory.Technical;
    }
}
