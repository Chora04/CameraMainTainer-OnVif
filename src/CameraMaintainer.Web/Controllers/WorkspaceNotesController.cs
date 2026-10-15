using System.Security.Claims;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Web.Hubs;
using CameraMaintainer.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CameraMaintainer.Web.Controllers;

[ApiController]
[Route("api/workspace-notes")]
[Authorize]
public sealed class WorkspaceNotesController : ControllerBase
{
    private static readonly HashSet<string> AllowedTones =
        new(StringComparer.OrdinalIgnoreCase) { "info", "warning", "danger", "success" };

    private readonly AppDbContext _db;
    private readonly EventBroadcaster _events;

    public WorkspaceNotesController(AppDbContext db, EventBroadcaster events)
    {
        _db = db;
        _events = events;
    }

    [HttpGet]
    [Authorize(Policy = AccessPolicies.ViewAlerts)]
    public async Task<ActionResult> Get([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to)
    {
        var actor = await GetActorAsync();
        var now = DateTimeOffset.UtcNow;
        var rangeStart = (from ?? now.AddDays(-45)).UtcDateTime;
        var rangeEnd = (to ?? now.AddDays(90)).UtcDateTime;
        if (rangeEnd <= rangeStart || rangeEnd - rangeStart > TimeSpan.FromDays(370))
            return BadRequest(new { message = "Диапазон календаря должен быть от 1 до 370 дней" });

        var notes = await _db.WorkspaceNotes.AsNoTracking()
            .Where(note =>
                (note.CreatedByUserId == actor.Id ||
                 note.IsVisibleToAll ||
                 note.RecipientUserId == actor.Id) &&
                note.ScheduledFor >= rangeStart && note.ScheduledFor < rangeEnd)
            .OrderBy(note => note.ScheduledFor)
            .ThenBy(note => note.Id)
            .ToListAsync();
        var canShare = IsAdministrator(actor);
        var recipients = canShare
            ? await _db.AppUsers.AsNoTracking()
                .Where(user => user.IsActive && user.CanViewAlerts && user.Id != actor.Id)
                .OrderBy(user => user.DisplayName)
                .ThenBy(user => user.Username)
                .Select(user => new WorkspaceNoteRecipientDto(user.Id, user.Username, user.DisplayName))
                .ToListAsync()
            : [];
        return Ok(new
        {
            items = notes.Select(note => new
            {
                note.Id,
                note.Title,
                note.Body,
                note.Tone,
                note.ScheduledFor,
                note.CreatedByUserId,
                note.CreatedByUsername,
                note.CreatedByDisplayName,
                note.IsVisibleToAll,
                note.RecipientUserId,
                note.RecipientUsername,
                note.RecipientDisplayName,
                note.CreatedAt,
                note.UpdatedAt,
                note.CompletedAt,
                note.CompletedByUserId,
                note.CompletedByUsername,
                note.CompletedByDisplayName,
                canModify = note.CreatedByUserId == actor.Id
            }),
            canEdit = User.HasClaim(AccessPolicies.PermissionClaim, AccessPolicies.ViewAlerts) &&
                User.HasClaim(AccessPolicies.PermissionClaim, AccessPolicies.RunChecks),
            canShare,
            recipients
        });
    }

    [HttpPost]
    [Authorize(Policy = AccessPolicies.WorkAlerts)]
    public async Task<ActionResult> Create([FromBody] SaveWorkspaceNoteDto dto)
    {
        var actor = await GetActorAsync();
        NormalizedWorkspaceNote normalized;
        try { normalized = Normalize(dto); }
        catch (InvalidOperationException exception) { return BadRequest(new { message = exception.Message }); }
        var (audience, audienceError) = await ResolveAudienceAsync(dto, actor, existing: null);
        if (audienceError != null) return audienceError;
        var note = new WorkspaceNote
        {
            Title = normalized.Title,
            Body = normalized.Body,
            Tone = normalized.Tone,
            ScheduledFor = normalized.ScheduledFor,
            CreatedByUserId = actor.Id,
            CreatedByUsername = actor.Username,
            CreatedByDisplayName = actor.DisplayName
        };
        ApplyAudience(note, audience);
        _db.WorkspaceNotes.Add(note);
        await _db.SaveChangesAsync();
        await BroadcastAsync(note, "created", audience);
        return Ok(note);
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = AccessPolicies.WorkAlerts)]
    public async Task<ActionResult> Update(long id, [FromBody] SaveWorkspaceNoteDto dto)
    {
        var actor = await GetActorAsync();
        var note = await _db.WorkspaceNotes.SingleOrDefaultAsync(item =>
            item.Id == id && item.CreatedByUserId == actor.Id);
        if (note == null) return NotFound(new { message = "Заметка не найдена" });
        NormalizedWorkspaceNote normalized;
        try { normalized = Normalize(dto); }
        catch (InvalidOperationException exception) { return BadRequest(new { message = exception.Message }); }
        var previousAudience = WorkspaceNoteAudience.From(note);
        var (audience, audienceError) = await ResolveAudienceAsync(dto, actor, note);
        if (audienceError != null) return audienceError;
        note.Title = normalized.Title;
        note.Body = normalized.Body;
        note.Tone = normalized.Tone;
        note.ScheduledFor = normalized.ScheduledFor;
        ApplyAudience(note, audience);
        note.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await BroadcastAsync(note, "updated", previousAudience, audience);
        return Ok(note);
    }

    [HttpPost("{id:long}/complete")]
    [Authorize(Policy = AccessPolicies.WorkAlerts)]
    public async Task<ActionResult> Complete(long id, [FromBody] CompleteWorkspaceNoteDto dto)
    {
        var actor = await GetActorAsync();
        var note = await _db.WorkspaceNotes.SingleOrDefaultAsync(item =>
            item.Id == id && item.CreatedByUserId == actor.Id);
        if (note == null) return NotFound(new { message = "Заметка не найдена" });
        if (dto.Completed)
        {
            note.CompletedAt = DateTime.UtcNow;
            note.CompletedByUserId = actor.Id;
            note.CompletedByUsername = actor.Username;
            note.CompletedByDisplayName = actor.DisplayName;
        }
        else
        {
            note.CompletedAt = null;
            note.CompletedByUserId = null;
            note.CompletedByUsername = null;
            note.CompletedByDisplayName = null;
        }
        note.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await BroadcastAsync(note, dto.Completed ? "completed" : "reopened", WorkspaceNoteAudience.From(note));
        return Ok(note);
    }

    [HttpDelete("{id:long}")]
    [Authorize(Policy = AccessPolicies.WorkAlerts)]
    public async Task<ActionResult> Delete(long id)
    {
        var actor = await GetActorAsync();
        var note = await _db.WorkspaceNotes.SingleOrDefaultAsync(item =>
            item.Id == id && item.CreatedByUserId == actor.Id);
        if (note == null) return NotFound(new { message = "Заметка не найдена" });
        _db.WorkspaceNotes.Remove(note);
        await _db.SaveChangesAsync();
        await BroadcastAsync(note, "deleted", WorkspaceNoteAudience.From(note));
        return NoContent();
    }

    private async Task<AppUser> GetActorAsync()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
            throw new UnauthorizedAccessException("Сеанс недействителен");
        return await _db.AppUsers.AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == actorId && user.IsActive)
            ?? throw new UnauthorizedAccessException("Аккаунт неактивен или удалён");
    }

    private async Task<(WorkspaceNoteAudience Audience, ActionResult? Error)> ResolveAudienceAsync(
        SaveWorkspaceNoteDto dto,
        AppUser actor,
        WorkspaceNote? existing)
    {
        if (!IsAdministrator(actor))
        {
            if (dto.IsVisibleToAll || dto.RecipientUserId.HasValue)
            {
                return (WorkspaceNoteAudience.Private, StatusCode(
                    StatusCodes.Status403Forbidden,
                    new { message = "Выбирать получателей заметки может только администратор" }));
            }

            return (existing == null
                ? WorkspaceNoteAudience.Private
                : WorkspaceNoteAudience.From(existing), null);
        }

        if (dto.IsVisibleToAll && dto.RecipientUserId.HasValue)
        {
            return (WorkspaceNoteAudience.Private, BadRequest(new
            {
                message = "Выберите одного получателя или вариант «Всем», но не оба варианта"
            }));
        }

        if (dto.IsVisibleToAll)
            return (WorkspaceNoteAudience.All, null);

        if (!dto.RecipientUserId.HasValue || dto.RecipientUserId.Value == actor.Id)
            return (WorkspaceNoteAudience.Private, null);

        if (existing?.RecipientUserId == dto.RecipientUserId.Value)
            return (WorkspaceNoteAudience.From(existing), null);

        var recipient = await _db.AppUsers.AsNoTracking()
            .Where(user =>
                user.Id == dto.RecipientUserId.Value &&
                user.IsActive &&
                user.CanViewAlerts)
            .Select(user => new WorkspaceNoteRecipientDto(user.Id, user.Username, user.DisplayName))
            .SingleOrDefaultAsync();
        if (recipient == null)
        {
            return (WorkspaceNoteAudience.Private, BadRequest(new
            {
                message = "Получатель не найден, неактивен или не имеет доступа к календарю"
            }));
        }

        return (new WorkspaceNoteAudience(
            false,
            recipient.Id,
            recipient.Username,
            recipient.DisplayName), null);
    }

    private static void ApplyAudience(WorkspaceNote note, WorkspaceNoteAudience audience)
    {
        note.IsVisibleToAll = audience.IsVisibleToAll;
        note.RecipientUserId = audience.RecipientUserId;
        note.RecipientUsername = audience.RecipientUsername;
        note.RecipientDisplayName = audience.RecipientDisplayName;
    }

    private static bool IsAdministrator(AppUser actor) =>
        AlertVisibilityPolicy.IsAdministrator(actor.Role, actor.CanManageUsers);

    private static NormalizedWorkspaceNote Normalize(SaveWorkspaceNoteDto dto)
    {
        var title = dto.Title?.Trim();
        var body = dto.Body?.Trim();
        var tone = dto.Tone?.Trim().ToLowerInvariant() ?? "info";
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Введите название заметки");
        if (title.Length > 256)
            throw new InvalidOperationException("Название не может быть длиннее 256 символов");
        if (body?.Length > 4000)
            throw new InvalidOperationException("Текст заметки не может быть длиннее 4000 символов");
        if (!AllowedTones.Contains(tone))
            throw new InvalidOperationException("Неизвестный цвет заметки");
        if (!dto.ScheduledFor.HasValue)
            throw new InvalidOperationException("Укажите дату и время заметки");
        return new NormalizedWorkspaceNote(title, string.IsNullOrWhiteSpace(body) ? null : body,
            tone, dto.ScheduledFor.Value.UtcDateTime);
    }

    private async Task BroadcastAsync(
        WorkspaceNote note,
        string action,
        params WorkspaceNoteAudience[] audiences)
    {
        var payload = new { noteId = note.Id, action };
        if (audiences.Any(audience => audience.IsVisibleToAll))
        {
            await _events.BroadcastAllAsync("workspace.note_changed", payload);
            await _events.BroadcastAllAsync("notification.changed", payload);
            return;
        }

        var userIds = audiences
            .Select(audience => audience.RecipientUserId)
            .OfType<int>()
            .Append(note.CreatedByUserId)
            .Distinct()
            .ToArray();
        await _events.BroadcastUsersAsync("workspace.note_changed", payload, userIds);
        await _events.BroadcastUsersAsync("notification.changed", payload, userIds);
    }

    private sealed record NormalizedWorkspaceNote(string Title, string? Body, string Tone, DateTime ScheduledFor);

    private sealed record WorkspaceNoteAudience(
        bool IsVisibleToAll,
        int? RecipientUserId,
        string? RecipientUsername,
        string? RecipientDisplayName)
    {
        public static WorkspaceNoteAudience Private { get; } = new(false, null, null, null);
        public static WorkspaceNoteAudience All { get; } = new(true, null, null, null);

        public static WorkspaceNoteAudience From(WorkspaceNote note) => new(
            note.IsVisibleToAll,
            note.RecipientUserId,
            note.RecipientUsername,
            note.RecipientDisplayName);
    }
}

public sealed class SaveWorkspaceNoteDto
{
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? Tone { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public bool IsVisibleToAll { get; set; }
    public int? RecipientUserId { get; set; }
}

public sealed class CompleteWorkspaceNoteDto
{
    public bool Completed { get; set; } = true;
}

public sealed record WorkspaceNoteRecipientDto(int Id, string Username, string DisplayName);
