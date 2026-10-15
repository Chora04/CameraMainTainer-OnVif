using System.Text.RegularExpressions;
using System.Security.Claims;
using System.Globalization;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CameraMaintainer.Web.Controllers;

[ApiController]
[Route("api/alert-board")]
[Authorize(Policy = AccessPolicies.ManageUsers)]
public sealed class AlertBoardController : ControllerBase
{
    private static readonly Regex ColorPattern = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private readonly AlertBoardRepository _repository;

    public AlertBoardController(AlertBoardRepository repository) => _repository = repository;

    [HttpGet("columns")]
    public async Task<ActionResult<List<AlertBoardColumn>>> GetColumns() => Ok(await _repository.GetColumnsAsync());

    [HttpPost("columns")]
    public async Task<ActionResult> AddColumn([FromBody] AlertBoardColumnDto dto)
    {
        var error = Validate(dto);
        if (error != null) return BadRequest(new { message = error });

        try
        {
            var column = await _repository.AddColumnAsync(new AlertBoardColumn
            {
                Key = "custom-" + Guid.NewGuid().ToString("N"),
                Title = dto.Title.Trim(),
                Subtitle = dto.Subtitle?.Trim() ?? string.Empty,
                Color = dto.Color.Trim().ToUpperInvariant(),
                VisibleToRoles = AlertBoardRoleVisibility.Normalize(dto.VisibleToRoles)
            });
            return Ok(column);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPut("columns/{key}")]
    public async Task<ActionResult> UpdateColumn(string key, [FromBody] AlertBoardColumnDto dto)
    {
        var error = Validate(dto);
        if (error != null) return BadRequest(new { message = error });

        try
        {
            await _repository.UpdateColumnAsync(key, new AlertBoardColumn
            {
                Title = dto.Title.Trim(),
                Subtitle = dto.Subtitle?.Trim() ?? string.Empty,
                Color = dto.Color.Trim().ToUpperInvariant(),
                VisibleToRoles = AlertBoardRoleVisibility.Normalize(dto.VisibleToRoles)
            });
            return Ok(await _repository.GetColumnsAsync());
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPut("columns/reorder")]
    public async Task<ActionResult> ReorderColumns([FromBody] ReorderAlertColumnsDto dto)
    {
        try
        {
            await _repository.ReorderColumnsAsync(dto.Keys ?? []);
            return Ok(await _repository.GetColumnsAsync());
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("columns/recommended")]
    public async Task<ActionResult> ApplyRecommendedWorkflow()
    {
        var columns = await _repository.ApplyRecommendedWorkflowAsync(
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture));
        return Ok(columns);
    }

    [HttpDelete("columns/{key}")]
    public async Task<ActionResult> DeleteColumn(string key, [FromBody] DeleteAlertColumnDto dto)
    {
        try
        {
            await _repository.DeleteColumnAsync(
                key,
                dto.MoveTo,
                int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture));
            return Ok(await _repository.GetColumnsAsync());
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private static string? Validate(AlertBoardColumnDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title) || dto.Title.Trim().Length > 64)
            return "Название колонки обязательно и не может быть длиннее 64 символов";
        if ((dto.Subtitle?.Trim().Length ?? 0) > 128)
            return "Описание колонки не может быть длиннее 128 символов";
        if (!ColorPattern.IsMatch(dto.Color?.Trim() ?? string.Empty))
            return "Цвет должен быть в формате #RRGGBB";
        return null;
    }
}

public sealed class AlertBoardColumnDto
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string Color { get; set; } = "#35c7bd";
    public List<string>? VisibleToRoles { get; set; }
}

public sealed class ReorderAlertColumnsDto
{
    public List<string>? Keys { get; set; }
}

public sealed class DeleteAlertColumnDto
{
    public string MoveTo { get; set; } = string.Empty;
}
