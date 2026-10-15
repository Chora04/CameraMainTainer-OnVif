using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Web.Hubs;
using CameraMaintainer.Web.Localization;
using CameraMaintainer.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CameraMaintainer.Web.Controllers;

[ApiController]
[Route("api/alerts")]
public class AlertsController : ControllerBase
{
    private readonly IAlertService _alertService;
    private readonly IAlertHandoffService _handoffService;
    private readonly AppDbContext _db;
    private readonly AlertBoardRepository _alertBoardRepository;
    private readonly EventBroadcaster _events;
    private readonly ILogger<AlertsController> _logger;

    public AlertsController(
        IAlertService alertService,
        IAlertHandoffService handoffService,
        AppDbContext db,
        AlertBoardRepository alertBoardRepository,
        EventBroadcaster events,
        ILogger<AlertsController> logger)
    {
        _alertService = alertService;
        _handoffService = handoffService;
        _db = db;
        _alertBoardRepository = alertBoardRepository;
        _events = events;
        _logger = logger;
    }

    [Authorize(Policy = AccessPolicies.ViewAlerts)]
    [HttpGet]
    public async Task<ActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        [FromQuery] string? filter = null)
    {
        if (!User.HasClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewAlertHistory))
            filter = "active";

        var requestedPage = Math.Max(1, page);
        var requestedPerPage = Math.Clamp(perPage, 1, 5000);
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
            return Unauthorized(new { message = "Сеанс недействителен" });

        var actor = await _db.AppUsers.AsNoTracking().SingleOrDefaultAsync(user => user.Id == actorId);
        if (actor == null || !actor.IsActive || !actor.CanViewAlerts)
            return Forbid();
        var query = AlertVisibilityPolicy.ApplyToQuery(_db.Alerts.AsNoTracking(), actor,
            await _alertBoardRepository.GetColumnsAsync());
        if (filter == "active") query = query.Where(alert => !alert.AutoResolved);
        var alerts = await query.Include(alert => alert.Camera).Include(alert => alert.Issues).AsSplitQuery()
            .OrderByDescending(alert => alert.CreatedAt).ThenByDescending(alert => alert.Id)
            .Skip((int)Math.Min(int.MaxValue, (long)(requestedPage - 1) * requestedPerPage))
            .Take(requestedPerPage)
            .ToListAsync();

        return Ok(alerts);
    }

    [Authorize(Policy = AccessPolicies.ViewAlerts)]
    [HttpGet("summary")]
    public async Task<ActionResult> GetSummary()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
            return Unauthorized();
        var actor = await _db.AppUsers.AsNoTracking().SingleOrDefaultAsync(user => user.Id == actorId);
        if (actor == null || !actor.IsActive || !actor.CanViewAlerts) return Forbid();
        var query = AlertVisibilityPolicy.ApplyToQuery(_db.Alerts.AsNoTracking(), actor,
            await _alertBoardRepository.GetColumnsAsync());
        var counts = await query.GroupBy(alert => new
        {
            alert.AutoResolved,
            Physical = alert.Issues.Any(issue => issue.Category == Core.Enums.ErrorCategory.Physical) ||
                    (!alert.Issues.Any() && alert.Category == Core.Enums.ErrorCategory.Physical),
            Technical = alert.Issues.Any(issue => issue.Category == Core.Enums.ErrorCategory.Technical) ||
                    (!alert.Issues.Any() && alert.Category == Core.Enums.ErrorCategory.Technical)
        })
            .Select(group => new { group.Key.AutoResolved, group.Key.Physical, group.Key.Technical, Count = group.Count() }).ToListAsync();
        return Ok(new
        {
            active = counts.Where(item => !item.AutoResolved).Sum(item => item.Count),
            physical = counts.Where(item => !item.AutoResolved && item.Physical).Sum(item => item.Count),
            technical = counts.Where(item => !item.AutoResolved && item.Technical).Sum(item => item.Count),
            history = counts.Where(item => item.AutoResolved).Sum(item => item.Count)
        });
    }

    [Authorize(Policy = AccessPolicies.WorkAlerts)]
    [HttpPost("{id:int}/assign")]
    public async Task<ActionResult> Assign(int id, [FromBody] AssignAlertDto dto)
    {
        return await ExecuteAsync(async actorId =>
        {
            var alert = await _alertService.AssignAlertAsync(id, dto.UserId ?? actorId, actorId);
            return Ok(alert);
        }, id);
    }

    [Authorize(Policy = AccessPolicies.ViewAlerts)]
    [HttpGet("{id:int}")]
    public async Task<ActionResult> GetById(int id)
    {
        var targetId = await _db.Alerts.IgnoreQueryFilters().Where(alert => alert.Id == id)
            .Select(alert => (int?)(alert.MergedIntoAlertId ?? alert.Id)).SingleOrDefaultAsync();
        if (!targetId.HasValue) return NotFound();
        id = targetId.Value;
        return await ExecuteAsync(async actorId =>
        {
            var alert = await _db.Alerts.AsNoTracking()
                .Include(item => item.Camera)
                .Include(item => item.Issues)
                    .ThenInclude(issue => issue.Messages)
                .AsSplitQuery()
                .SingleAsync(item => item.Id == id);
            var actor = await _db.AppUsers.AsNoTracking().SingleAsync(user => user.Id == actorId);
            var administrator = AlertVisibilityPolicy.IsAdministrator(actor.Role, actor.CanManageUsers);
            var columns = await _alertBoardRepository.GetColumnsAsync();
            var stage = AlertVisibilityPolicy.EffectiveStage(alert);
            var currentColumn = columns.FirstOrDefault(column => column.Key.Equals(stage, StringComparison.OrdinalIgnoreCase));
            var visibleColumns = columns.Where(column => AlertBoardRoleVisibility.IsVisibleTo(
                column, actor.Role, administrator, actor.AlertBoardColumnKeys)).ToList();
            var canEdit = !alert.AutoResolved && actor.IsActive && actor.CanViewAlerts && actor.CanRunChecks &&
                visibleColumns.Any(column => column.Key.Equals(stage, StringComparison.OrdinalIgnoreCase)) &&
                (administrator || !alert.AssignedToUserId.HasValue || alert.AssignedToUserId == actorId);
            return Ok(new
            {
                alert,
                cameraName = alert.Camera?.Name,
                cameraLocation = alert.Camera?.StreetAddress,
                cameraIp = alert.Camera?.IpAddress,
                cameraFirmwareVersion = alert.Camera?.FirmwareVersion,
                cameraStatus = alert.Camera?.Status.ToString(),
                alertSerialNumber = AlertSerialNumber.Format(
                    alert.Camera?.Name, alert.CameraSequenceNumber, alert.Id),
                displayTitle = CameraErrorMessages.Title(alert.AlertType, alert.Title, alert.Description),
                stageTitle = alert.AutoResolved ? "Закрыта" : currentColumn?.Title ?? "Этап недоступен",
                canEdit,
                canAssignSelf = canEdit && !alert.AssignedToUserId.HasValue,
                canCreateHandoff = canEdit && (administrator || alert.AssignedToUserId == actorId),
                stages = visibleColumns.Where(_ => canEdit).Select(column => new { column.Key, column.Title })
            });
        }, id, allowHandoffAccess: true);
    }

    [Authorize(Policy = AccessPolicies.ViewAlerts)]
    [HttpGet("{id:int}/history")]
    public async Task<ActionResult> GetHistory(int id)
    {
        return await ExecuteAsync(async actorId =>
        {
            var events = await _alertService.GetAlertAuditAsync(id, actorId);
            return Ok(events);
        }, id, allowHandoffAccess: true);
    }

    [Authorize(Policy = AccessPolicies.ViewAlertHistory)]
    [HttpGet("camera/{cameraId:int}/history")]
    public async Task<ActionResult> GetCameraHistory(int cameraId, [FromQuery] int limit = 30)
    {
        return await ExecuteAsync(async actorId =>
        {
            var alerts = await _alertService.GetCameraAlertHistoryAsync(cameraId, actorId, limit);
            var visible = await FilterForBoardAccessAsync(alerts, actorId);
            var cameraName = await _db.Cameras.AsNoTracking()
                .Where(camera => camera.Id == cameraId)
                .Select(camera => camera.Name)
                .SingleOrDefaultAsync();
            return Ok(visible.Select(alert => new
            {
                alert.Id,
                alert.CameraId,
                alert.Title,
                alert.Description,
                alert.AlertType,
                alert.CameraSequenceNumber,
                alertSerialNumber = AlertSerialNumber.Format(cameraName, alert.CameraSequenceNumber, alert.Id),
                displayTitle = CameraErrorMessages.Title(alert.AlertType, alert.Title, alert.Description),
                alert.Category,
                alert.Severity,
                alert.CreatedAt,
                alert.ResolvedAt,
                alert.RecoveryDetectedAt,
                alert.AutoResolved,
                alert.WorkflowStage,
                alert.AssignedToUserId,
                alert.AssignedToUsername,
                alert.AssignedToDisplayName,
                alert.WorkReport,
                alert.ServiceNotified,
                alert.ResolvedByUsername,
                alert.ResolvedByDisplayName,
                alert.Issues,
                auditEvents = alert.AuditEvents
            }));
        });
    }

    [Authorize(Policy = AccessPolicies.ViewAlerts)]
    [HttpPost("{id:int}/viewed")]
    public async Task<ActionResult> RecordViewed(int id)
    {
        return await ExecuteAsync(async actorId =>
        {
            await _alertService.RecordAlertViewedAsync(id, actorId);
            return NoContent();
        }, id, allowHandoffAccess: true);
    }

    [Authorize(Policy = AccessPolicies.ViewAlerts)]
    [HttpGet("{id:int}/handoffs")]
    public async Task<ActionResult> GetHandoffs(int id)
    {
        return await ExecuteAsync(async actorId =>
        {
            var handoffs = await _handoffService.GetForAlertAsync(id, actorId);
            var openForActor = handoffs.LastOrDefault(item =>
                item.ToUserId == actorId &&
                AlertHandoffStates.IsOpen(item.State) &&
                !item.ViewedAt.HasValue);
            if (openForActor != null)
            {
                await _handoffService.ViewAsync(openForActor.Id, actorId);
                handoffs = await _handoffService.GetForAlertAsync(id, actorId);
                await BroadcastHandoffAsync(id, openForActor.Id, "viewed");
            }

            return Ok(new
            {
                actorUserId = actorId,
                items = handoffs.Select(item => ToHandoffDto(item, actorId))
            });
        }, id, allowHandoffAccess: true);
    }

    [Authorize(Policy = AccessPolicies.ViewAlerts)]
    [HttpGet("handoff-reasons")]
    public async Task<ActionResult> GetHandoffReasons([FromQuery] bool includeInactive = false)
    {
        if (includeInactive && !User.HasClaim(AccessPolicies.PermissionClaim, AccessPolicies.ManageUsers))
            return Forbid();

        var reasons = await _handoffService.GetReasonsAsync(includeInactive);
        return Ok(reasons.Select(item => new
        {
            item.Id,
            item.Key,
            item.Name,
            item.IsActive,
            item.SortOrder
        }));
    }

    [Authorize(Policy = AccessPolicies.ManageUsers)]
    [HttpPost("handoff-reasons")]
    public async Task<ActionResult> CreateHandoffReason([FromBody] SaveHandoffReasonDto dto)
    {
        return await ExecuteAsync(async actorId =>
        {
            var reason = await _handoffService.SaveReasonAsync(null, dto.Name, dto.IsActive, actorId);
            return Ok(reason);
        });
    }

    [Authorize(Policy = AccessPolicies.ManageUsers)]
    [HttpPut("handoff-reasons/{reasonId:int}")]
    public async Task<ActionResult> UpdateHandoffReason(int reasonId, [FromBody] SaveHandoffReasonDto dto)
    {
        return await ExecuteAsync(async actorId =>
        {
            var reason = await _handoffService.SaveReasonAsync(reasonId, dto.Name, dto.IsActive, actorId);
            return Ok(reason);
        });
    }

    [Authorize(Policy = AccessPolicies.WorkAlerts)]
    [HttpPost("{id:int}/handoffs")]
    public async Task<ActionResult> CreateHandoff(int id, [FromBody] CreateHandoffDto dto)
    {
        return await ExecuteAsync(async actorId =>
        {
            var handoff = await _handoffService.CreateAsync(
                id,
                dto.ToUserId,
                dto.ReasonId,
                dto.Comment,
                dto.DueHours,
                actorId);
            await BroadcastHandoffAsync(id, handoff.Id, "created");
            return Ok(ToHandoffDto(handoff, actorId));
        }, id);
    }

    [Authorize(Policy = AccessPolicies.WorkAlerts)]
    [HttpPost("{id:int}/handoffs/{handoffId:long}/view")]
    public async Task<ActionResult> ViewHandoff(int id, long handoffId)
    {
        return await ExecuteAsync(async actorId =>
        {
            await EnsureHandoffAlertAsync(id, handoffId);
            var handoff = await _handoffService.ViewAsync(handoffId, actorId);
            EnsureAlertId(id, handoff);
            await BroadcastHandoffAsync(id, handoff.Id, "viewed");
            return Ok(ToHandoffDto(handoff, actorId));
        }, id, allowHandoffAccess: true);
    }

    [Authorize(Policy = AccessPolicies.WorkAlerts)]
    [HttpPost("{id:int}/handoffs/{handoffId:long}/accept")]
    public async Task<ActionResult> AcceptHandoff(int id, long handoffId, [FromBody] HandoffCommentDto dto)
    {
        return await ExecuteAsync(async actorId =>
        {
            await EnsureHandoffAlertAsync(id, handoffId);
            var handoff = await _handoffService.AcceptAsync(handoffId, dto.Comment, actorId);
            EnsureAlertId(id, handoff);
            await BroadcastHandoffAsync(id, handoff.Id, "accepted");
            return Ok(ToHandoffDto(handoff, actorId));
        }, id, allowHandoffAccess: true);
    }

    [Authorize(Policy = AccessPolicies.WorkAlerts)]
    [HttpPost("{id:int}/handoffs/{handoffId:long}/forward")]
    public async Task<ActionResult> ForwardHandoff(int id, long handoffId, [FromBody] CreateHandoffDto dto)
    {
        return await ExecuteAsync(async actorId =>
        {
            await EnsureHandoffAlertAsync(id, handoffId);
            var handoff = await _handoffService.ForwardAsync(
                handoffId,
                dto.ToUserId,
                dto.ReasonId,
                dto.Comment,
                dto.DueHours,
                actorId);
            EnsureAlertId(id, handoff);
            await BroadcastHandoffAsync(id, handoff.Id, "forwarded");
            return Ok(ToHandoffDto(handoff, actorId));
        }, id, allowHandoffAccess: true);
    }

    [Authorize(Policy = AccessPolicies.WorkAlerts)]
    [HttpPost("{id:int}/handoffs/{handoffId:long}/complete")]
    public async Task<ActionResult> CompleteHandoff(int id, long handoffId, [FromBody] HandoffCommentDto dto)
    {
        return await ExecuteAsync(async actorId =>
        {
            await EnsureHandoffAlertAsync(id, handoffId);
            var handoff = await _handoffService.CompleteAsync(handoffId, dto.Comment ?? string.Empty, actorId);
            EnsureAlertId(id, handoff);
            await BroadcastHandoffAsync(id, handoff.Id, "completed");
            return Ok(ToHandoffDto(handoff, actorId));
        }, id, allowHandoffAccess: true);
    }

    [Authorize(Policy = AccessPolicies.WorkAlerts)]
    [HttpPost("{id:int}/handoffs/{handoffId:long}/reject")]
    public async Task<ActionResult> RejectHandoff(int id, long handoffId, [FromBody] HandoffCommentDto dto)
    {
        return await ExecuteAsync(async actorId =>
        {
            await EnsureHandoffAlertAsync(id, handoffId);
            var handoff = await _handoffService.RejectAsync(handoffId, dto.Comment ?? string.Empty, actorId);
            EnsureAlertId(id, handoff);
            await BroadcastHandoffAsync(id, handoff.Id, "rejected");
            return Ok(ToHandoffDto(handoff, actorId));
        }, id, allowHandoffAccess: true);
    }

    [Authorize(Policy = AccessPolicies.WorkAlerts)]
    [HttpPut("{id:int}/work")]
    public async Task<ActionResult> UpdateWork(int id, [FromBody] UpdateAlertWorkDto dto)
    {
        if (!Enum.TryParse<Core.Enums.ErrorCategory>(dto.Category, true, out var category) || !Enum.IsDefined(category))
            return BadRequest(new { message = "Категория должна быть физической или технической" });

        return await ExecuteAsync(async actorId =>
        {
            var alert = await _alertService.UpdateAlertWorkAsync(
                id,
                category,
                dto.WorkReport,
                dto.ServiceNotified,
                actorId);
            return Ok(alert);
        }, id);
    }

    [Authorize(Policy = AccessPolicies.WorkAlerts)]
    [HttpPost("{id:int}/issues/{issueId:int}/messages")]
    public async Task<ActionResult> AddIssueMessage(
        int id,
        int issueId,
        [FromBody] AddAlertIssueMessageDto dto)
    {
        return await ExecuteAsync(async actorId =>
        {
            var text = dto.Message?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return BadRequest(new { message = "Введите сообщение отчёта" });
            if (text.Length > 4000)
                return BadRequest(new { message = "Сообщение не может быть длиннее 4000 символов" });

            var actor = await _db.AppUsers.SingleAsync(user => user.Id == actorId && user.IsActive);
            var alert = await _db.Alerts
                .Include(item => item.Issues)
                .SingleAsync(item => item.Id == id);
            if (alert.AutoResolved)
                throw new InvalidOperationException("Закрытая карточка доступна только для чтения");

            var administrator = AlertVisibilityPolicy.IsAdministrator(actor.Role, actor.CanManageUsers);
            if (alert.AssignedToUserId.HasValue && alert.AssignedToUserId != actorId && !administrator)
                throw new UnauthorizedAccessException("Ошибка назначена другому сотруднику");

            var issue = alert.Issues.SingleOrDefault(item => item.Id == issueId)
                ?? throw new KeyNotFoundException("Неисправность не относится к выбранной карточке");
            var now = DateTime.UtcNow;
            if (!alert.AssignedToUserId.HasValue)
            {
                alert.AssignedToUserId = actor.Id;
                alert.AssignedToUsername = actor.Username;
                alert.AssignedToDisplayName = actor.DisplayName;
                alert.AssignedByUsername = actor.Username;
                alert.AssignedAt = now;
            }
            if (string.IsNullOrWhiteSpace(alert.WorkflowStage) ||
                alert.WorkflowStage.Equals("new", StringComparison.OrdinalIgnoreCase) ||
                alert.WorkflowStage.Equals("assigned", StringComparison.OrdinalIgnoreCase))
            {
                alert.WorkflowStage = "working";
            }

            var message = new AlertIssueMessage
            {
                AlertIssueId = issue.Id,
                AuthorUserId = actor.Id,
                AuthorUsername = actor.Username,
                AuthorDisplayName = actor.DisplayName,
                Message = text,
                CreatedAt = now
            };
            _db.AlertIssueMessages.Add(message);
            alert.UpdatedAt = now;
            _db.AlertAuditEvents.Add(new AlertAuditEvent
            {
                AlertId = alert.Id,
                EventType = "issue_message_added",
                ActorUserId = actor.Id,
                ActorUsername = actor.Username,
                ActorDisplayName = actor.DisplayName,
                Summary = $"Добавлено сообщение к неисправности «{issue.Title}»",
                FromStage = alert.WorkflowStage,
                ToStage = alert.WorkflowStage,
                CreatedAt = now
            });
            await _db.SaveChangesAsync();
            await _events.BroadcastAllAsync("alerts.changed", new { alertId = id, issueId });
            await _events.BroadcastAllAsync("notification.changed", new { alertId = id, issueId });
            return Ok(message);
        }, id);
    }

    [Authorize(Policy = AccessPolicies.WorkAlerts)]
    [HttpPost("{id:int}/stage")]
    public async Task<ActionResult> MoveStage(int id, [FromBody] MoveAlertStageDto dto)
    {
        return await ExecuteAsync(async actorId =>
        {
            var alert = await _alertService.MoveAlertAsync(id, dto.Stage, actorId);
            return Ok(alert);
        }, id);
    }

    [Authorize(Policy = AccessPolicies.WorkAlerts)]
    [HttpPost("{id:int}/resolve")]
    public async Task<ActionResult> Resolve(int id)
    {
        return await ExecuteAsync(async actorId =>
        {
            var alert = await _alertService.ResolveAlertAsync(id, actorId);
            return Ok(alert);
        }, id);
    }

    private async Task<ActionResult> ExecuteAsync(
        Func<int, Task<ActionResult>> action,
        int? alertId = null,
        bool allowHandoffAccess = false)
    {
        var idValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idValue, out var actorId))
            return Unauthorized(new { message = "Сеанс недействителен" });

        try
        {
            if (alertId.HasValue)
                await EnsureBoardAccessAsync(alertId.Value, actorId, allowHandoffAccess);
            return await action(actorId);
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private async Task EnsureBoardAccessAsync(int alertId, int actorId, bool allowHandoffAccess)
    {
        var actor = await _db.AppUsers.AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == actorId && user.IsActive)
            ?? throw new UnauthorizedAccessException("Аккаунт неактивен или удалён");
        var alert = await _db.Alerts.AsNoTracking()
            .Include(item => item.Handoffs)
            .SingleOrDefaultAsync(item => item.Id == alertId)
            ?? throw new KeyNotFoundException("Ошибка не найдена");

        var isAdministrator = AlertVisibilityPolicy.IsAdministrator(actor.Role, actor.CanManageUsers);
        if (!isAdministrator && !AlertVisibilityPolicy.IsVisibleToOperator(alert, actor.Id, actor.Username))
            throw new UnauthorizedAccessException("Ошибка назначена другому сотруднику");

        if (alert.AutoResolved && !actor.CanViewAlertHistory)
            throw new UnauthorizedAccessException("История ошибок скрыта настройками доступа аккаунта");

        if (alert.AutoResolved || (allowHandoffAccess && AlertVisibilityPolicy.IsHandoffParticipant(alert, actorId)))
            return;

        var columns = await _alertBoardRepository.GetColumnsAsync();
        var stage = AlertVisibilityPolicy.EffectiveStage(alert);
        var column = columns.FirstOrDefault(item =>
            item.Key.Equals(stage, StringComparison.OrdinalIgnoreCase));
        if (column == null || !AlertBoardRoleVisibility.IsVisibleTo(
                column,
                actor.Role,
                isAdministrator,
                actor.AlertBoardColumnKeys))
        {
            throw new UnauthorizedAccessException("Этот этап очереди скрыт настройками доступа аккаунта");
        }
    }

    private async Task<List<Alert>> FilterForBoardAccessAsync(IEnumerable<Alert> alerts, int actorId)
    {
        var actor = await _db.AppUsers.AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == actorId && user.IsActive)
            ?? throw new UnauthorizedAccessException("Аккаунт неактивен или удалён");
        return AlertVisibilityPolicy.FilterForBoardAccess(
            alerts,
            await _alertBoardRepository.GetColumnsAsync(),
            actor.Role,
            AlertVisibilityPolicy.IsAdministrator(actor.Role, actor.CanManageUsers),
            actor.AlertBoardColumnKeys);
    }

    private async Task BroadcastHandoffAsync(int alertId, long handoffId, string action)
    {
        await _events.BroadcastAllAsync("alert.handoff_changed", new { alertId, handoffId, action });
        await _events.BroadcastAllAsync("notification.changed", new { alertId, handoffId, action });
    }

    private async Task EnsureHandoffAlertAsync(int alertId, long handoffId)
    {
        if (!await _db.AlertHandoffs.AnyAsync(item => item.Id == handoffId && item.AlertId == alertId))
            throw new KeyNotFoundException("Передача не относится к выбранной ошибке");
    }

    private static void EnsureAlertId(int alertId, AlertHandoff handoff)
    {
        if (handoff.AlertId != alertId)
            throw new KeyNotFoundException("Передача не относится к выбранной ошибке");
    }

    private static object ToHandoffDto(AlertHandoff item, int actorUserId) => new
    {
        item.Id,
        item.AlertId,
        item.ParentHandoffId,
        item.ReasonId,
        item.ReasonName,
        item.FromUserId,
        item.FromUsername,
        item.FromDisplayName,
        item.ToUserId,
        item.ToUsername,
        item.ToDisplayName,
        item.Comment,
        item.AcceptanceComment,
        item.CompletionComment,
        item.State,
        item.CreatedAt,
        item.DueAt,
        item.ViewedAt,
        item.AcceptedAt,
        item.CompletedAt,
        isRecipient = item.ToUserId == actorUserId,
        canAccept = item.ToUserId == actorUserId && item.State == AlertHandoffStates.Pending,
        canForward = item.ToUserId == actorUserId && item.State == AlertHandoffStates.Accepted,
        canComplete = item.ToUserId == actorUserId && item.State == AlertHandoffStates.Accepted,
        canReject = item.ToUserId == actorUserId && AlertHandoffStates.IsOpen(item.State)
    };
}

public sealed class AssignAlertDto
{
    public int? UserId { get; set; }
}

public sealed class UpdateAlertWorkDto
{
    public string Category { get; set; } = "Technical";
    public string? WorkReport { get; set; }
    public bool ServiceNotified { get; set; }
}

public sealed class MoveAlertStageDto
{
    public string Stage { get; set; } = "working";
}

public sealed class AddAlertIssueMessageDto
{
    public string? Message { get; set; }
}

public sealed class CreateHandoffDto
{
    public int ToUserId { get; set; }
    public int ReasonId { get; set; }
    public string Comment { get; set; } = string.Empty;
    public int DueHours { get; set; } = 24;
}

public sealed class HandoffCommentDto
{
    public string? Comment { get; set; }
}

public sealed class SaveHandoffReasonDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
