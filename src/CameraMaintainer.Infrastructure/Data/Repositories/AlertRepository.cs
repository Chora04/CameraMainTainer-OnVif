using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CameraMaintainer.Infrastructure.Data.Repositories;

public class AlertRepository
{
    private readonly AppDbContext _db;
    private readonly IMonitoringEvents? _events;

    public AlertRepository(AppDbContext db, IMonitoringEvents? events = null)
    {
        _db = db;
        _events = events;
    }

    public async Task<Alert> AddAsync(Alert alert, AlertAuditEvent? auditEvent = null)
    {
        var now = DateTime.UtcNow;
        alert.CreatedAt = now;
        alert.UpdatedAt = now;
        EnsureIssues(alert);
        _db.Alerts.Add(alert);
        if (auditEvent != null)
        {
            auditEvent.Alert = alert;
            auditEvent.CreatedAt = now;
            _db.AlertAuditEvents.Add(auditEvent);
        }
        await _db.SaveChangesAsync();
        if (_events != null)
            await _events.AlertsChangedAsync();
        return alert;
    }

    public async Task<int?> ReserveNextCameraSequenceAsync(int? cameraId)
    {
        if (!cameraId.HasValue)
            return null;

        var camera = await _db.Cameras.SingleAsync(item => item.Id == cameraId.Value);
        var sequence = Math.Max(1, camera.NextAlertSequence);
        camera.NextAlertSequence = checked(sequence + 1);
        return sequence;
    }

    public async Task<Alert?> GetByIdAsync(int id)
    {
        return await _db.Alerts
            .Include(a => a.Camera)
            .Include(a => a.Handoffs)
            .Include(a => a.Issues)
                .ThenInclude(issue => issue.Messages)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<List<Alert>> GetForCameraAsync(
        int cameraId,
        int limit,
        int? assignedToUserId = null,
        string? assignedToUsername = null)
    {
        var query = _db.Alerts
            .AsNoTracking()
            .Where(alert => alert.CameraId == cameraId);

        if (assignedToUserId.HasValue)
        {
            query = query.Where(alert =>
                alert.AssignedToUserId == assignedToUserId ||
                alert.Handoffs.Any(handoff =>
                    handoff.FromUserId == assignedToUserId ||
                    handoff.ToUserId == assignedToUserId) ||
                (!alert.AssignedToUserId.HasValue && !string.IsNullOrEmpty(assignedToUsername) &&
                 alert.AssignedToUsername == assignedToUsername));
        }

        return await query
            .Include(alert => alert.AuditEvents)
            .Include(alert => alert.Issues)
            .AsSplitQuery()
            .OrderByDescending(alert => alert.CreatedAt)
            .ThenByDescending(alert => alert.Id)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<List<AlertAuditEvent>> GetAuditEventsAsync(int alertId)
    {
        return await _db.AlertAuditEvents
            .AsNoTracking()
            .Where(item => item.AlertId == alertId)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync();
    }

    public async Task<bool> HasAuditEventAsync(int alertId, int actorUserId, string eventType)
    {
        return await _db.AlertAuditEvents.AnyAsync(item =>
            item.AlertId == alertId &&
            item.ActorUserId == actorUserId &&
            item.EventType == eventType);
    }

    public async Task AddAuditEventAsync(AlertAuditEvent auditEvent)
    {
        auditEvent.CreatedAt = DateTime.UtcNow;
        _db.AlertAuditEvents.Add(auditEvent);
        await _db.SaveChangesAsync();
        if (_events != null)
            await _events.AlertsChangedAsync();
    }

    public async Task<AppUser?> GetActiveUserAsync(int id)
    {
        return await _db.AppUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == id && user.IsActive);
    }

    public async Task<Alert> UpdateAsync(Alert alert, AlertAuditEvent? auditEvent = null)
    {
        alert.UpdatedAt = DateTime.UtcNow;
        if (auditEvent != null)
        {
            auditEvent.AlertId = alert.Id;
            auditEvent.CreatedAt = alert.UpdatedAt;
            _db.AlertAuditEvents.Add(auditEvent);
        }
        await _db.SaveChangesAsync();
        if (_events != null)
            await _events.AlertsChangedAsync();
        return alert;
    }

    public async Task MarkConnectivityRecoveredAsync(int cameraId)
    {
        await MarkRecoveredAsync(cameraId, [AlertType.CameraOffline, AlertType.AuthenticationFailed, AlertType.AutoRecoveryFailed]);
    }

    public async Task MarkRecoveredAsync(int cameraId, AlertType alertType)
    {
        await MarkRecoveredAsync(cameraId, [alertType]);
    }

    private async Task MarkRecoveredAsync(int cameraId, AlertType[] types)
    {
        var gate = CameraAlertLocks.ForCamera(cameraId);
        await gate.WaitAsync();
        try { await MarkRecoveredCoreAsync(cameraId, types); }
        finally { gate.Release(); }
    }

    private async Task MarkRecoveredCoreAsync(int cameraId, AlertType[] types)
    {
        var alerts = await _db.Alerts.Include(alert => alert.Issues)
            .Where(alert => alert.CameraId == cameraId && !alert.AutoResolved && alert.RecoveryDetectedAt == null)
            .ToListAsync();
        var now = DateTime.UtcNow;
        var changed = false;
        foreach (var alert in alerts)
        {
            EnsureIssues(alert);
            var recovered = alert.Issues.Where(issue => types.Contains(issue.AlertType) && issue.RecoveredAt == null).ToList();
            if (recovered.Count == 0) continue;
            changed = true;
            var previousValues = AlertAuditEvent.CaptureState(alert);
            var fromStage = alert.WorkflowStage;
            foreach (var issue in recovered)
            {
                issue.RecoveredAt = now;
                issue.ResolvedByUserId = null;
                issue.ResolvedByUsername = "system";
                issue.ResolvedByDisplayName = "Система (автоматически)";
            }
            var allRecovered = alert.Issues.All(issue => issue.RecoveredAt.HasValue);
            if (allRecovered)
            {
                alert.RecoveryDetectedAt = now;
                alert.WorkflowStage = "ready";
            }
            RefreshSummary(alert);
            alert.UpdatedAt = now;
            _db.AlertAuditEvents.Add(new AlertAuditEvent
            {
                AlertId = alert.Id,
                EventType = allRecovered ? "recovery_detected" : "issue_recovered",
                Summary = allRecovered
                    ? "Все неисправности исчезли автоматически. Ожидается подтверждение закрытия менеджером"
                    : "Проверка подтвердила восстановление: " + string.Join("; ", recovered.Select(issue => issue.Title)),
                FromStage = fromStage,
                ToStage = alert.WorkflowStage,
                PreviousValuesJson = previousValues,
                NewValuesJson = AlertAuditEvent.CaptureState(alert),
                CreatedAt = now
            });
        }

        // Recovery is not a manager's approval: preserve reports, owners and open handoffs.
        if (!changed) return;
        await _db.SaveChangesAsync();
        if (_events != null)
            await _events.AlertsChangedAsync();
    }

    public async Task<Alert?> GetActiveForCameraAsync(int cameraId)
    {
        var alert = await _db.Alerts.Include(a => a.Issues)
            .Where(a => a.CameraId == cameraId && !a.AutoResolved)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).FirstOrDefaultAsync();
        if (alert != null) EnsureIssues(alert);
        return alert;
    }

    public async Task ResetStabilityAsync(int cameraId)
    {
        var camera = await _db.Cameras.FindAsync(cameraId);
        if (camera != null) camera.ConsecutiveHealthyChecks = 0;
    }

    public Task<int?> GetCameraIdAsync(int alertId) => _db.Alerts.Where(alert => alert.Id == alertId)
        .Select(alert => alert.CameraId).SingleOrDefaultAsync();

    public static void EnsureIssues(Alert alert)
    {
        if (alert.Issues.Count == 0) alert.Issues.Add(AlertIssue.FromAlert(alert));
    }

    public static void RefreshSummary(Alert alert)
    {
        var active = alert.Issues.Where(issue => issue.RecoveredAt == null).ToList();
        var relevant = active.Count > 0 ? active : alert.Issues.ToList();
        if (relevant.Count == 0) return;
        var primary = relevant.OrderByDescending(issue => issue.Severity).ThenBy(issue => issue.StartedAt).First();
        alert.AlertType = primary.AlertType;
        alert.Title = primary.Title;
        alert.Description = primary.Description;
        alert.Severity = primary.Severity;
        alert.Category = relevant.Any(issue => issue.Category == ErrorCategory.Physical) ? ErrorCategory.Physical : ErrorCategory.Technical;
    }
}
