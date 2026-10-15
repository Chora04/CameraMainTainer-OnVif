using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Services;
using CameraMaintainer.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace CameraMaintainer.Web.Controllers;

[ApiController]
[Route("api")]
public class HealthController : ControllerBase
{
    private readonly IHealthService _healthService;
    private readonly ICameraRepository _cameraRepository;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly int _maxConcurrency;

    public HealthController(
        IHealthService healthService,
        ICameraRepository cameraRepository,
        IServiceScopeFactory scopeFactory,
        HealthCheckConcurrencyGate concurrencyGate)
    {
        _healthService = healthService;
        _cameraRepository = cameraRepository;
        _scopeFactory = scopeFactory;
        _maxConcurrency = concurrencyGate.MaxConcurrency;
    }

    [Authorize(Policy = AccessPolicies.ViewCameras)]
    [HttpGet("cameras/{id}/health")]
    public async Task<ActionResult> GetCameraHealth(int id)
    {
        var latest = await _healthService.GetCurrentHealthAsync(id);
        if (latest == null) return NotFound();
        return Ok(ToDto(latest));
    }

    [Authorize(Policy = AccessPolicies.CheckCameras)]
    [HttpPost("cameras/{id}/health/check")]
    public async Task<ActionResult> TriggerHealthCheck(int id)
    {
        if (id <= 0 || await _cameraRepository.GetByIdAsync(id) == null)
            return NotFound(new { message = "Камера не найдена" });

        var result = await _healthService.CheckCameraAsync(id, HttpContext.RequestAborted);
        return Ok(ToDto(result));
    }

    [Authorize(Policy = AccessPolicies.CheckCameras)]
    [HttpPost("health/batch")]
    public async Task<ActionResult> TriggerBatchHealthCheck([FromBody] BatchHealthDto? dto)
    {
        if (dto?.All == true)
        {
            await _healthService.CheckAllDueAsync(HttpContext.RequestAborted);
            return Ok(new { message = "Массовая проверка камер завершена" });
        }

        var cameraIds = dto?.CameraIds?
            .Where(id => id > 0)
            .Distinct()
            .ToArray() ?? [];
        if (cameraIds.Length == 0)
        {
            return BadRequest(new
            {
                message = "Укажите cameraIds или установите all: true"
            });
        }
        if (cameraIds.Length > 1000)
            return BadRequest(new { message = "За один запрос можно проверить не более 1000 камер" });

        var existingCameraIds = (await _cameraRepository.GetExistingIdsAsync(cameraIds)).ToHashSet();
        var missingCameraIds = cameraIds.Where(id => !existingCameraIds.Contains(id)).ToArray();
        if (missingCameraIds.Length > 0)
        {
            return NotFound(new
            {
                message = "Некоторые камеры не найдены",
                cameraIds = missingCameraIds
            });
        }

        await OrderedAsyncWorkerPool.RunAsync(cameraIds, _maxConcurrency, async (id, cancellationToken) =>
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var healthService = scope.ServiceProvider.GetRequiredService<IHealthService>();
                await healthService.CheckCameraAsync(id, cancellationToken);
            }, HttpContext.RequestAborted);
        return Ok(new
        {
            message = $"Проверено камер: {cameraIds.Length}",
            cameraIds
        });
    }

    [Authorize(Policy = AccessPolicies.ViewMaintenance)]
    [HttpGet("health/queue")]
    public async Task<ActionResult> GetCheckQueue([FromQuery] int[]? ids = null)
    {
        if (ids is { Length: > 200 } || ids?.Any(id => id <= 0) == true)
            return BadRequest(new { message = "Укажите не более 200 корректных ID камер." });
        var now = DateTime.UtcNow;
        var queue = await _cameraRepository.GetCheckQueueAsync(ids is { Length: > 0 } ? ids : null);
        var items = queue.Select((item, index) =>
        {
            var waitingForCycle = item.IsStable && item.CyclesUntilCheck > 1;
            var nextCheckAt = item.StableChecksPaused || waitingForCycle ? null : item.LastCheckAt?.AddSeconds(item.CheckIntervalSeconds);
            var dueNow = item.AutomaticChecksEnabled && !item.StableChecksPaused && !waitingForCycle &&
                         (!nextCheckAt.HasValue || nextCheckAt <= now);
            var queueState = !item.AutomaticChecksEnabled ? "disabled" :
                item.StableChecksPaused ? "stable-paused" :
                waitingForCycle ? "stable-skipped" :
                dueNow ? "due" : "scheduled";

            return new CameraCheckQueueDto
            {
                CameraId = item.CameraId,
                Name = item.Name,
                IpAddress = item.IpAddress,
                Status = item.Status.ToString(),
                Position = ids is { Length: > 0 } ? item.Position : index + 1,
                AutomaticChecksEnabled = item.AutomaticChecksEnabled,
                CheckIntervalSeconds = item.CheckIntervalSeconds,
                LastCheckAt = item.LastCheckAt,
                NextCheckAt = nextCheckAt,
                DueNow = dueNow,
                HasPhysicalFault = item.HasPhysicalFault,
                ConsecutiveHealthyChecks = item.ConsecutiveHealthyChecks,
                IsStable = item.IsStable,
                EffectiveCheckIntervalSeconds = item.EffectiveCheckIntervalSeconds,
                StableChecksPaused = item.StableChecksPaused,
                NextCycleNumber = item.NextCycleNumber,
                StableCheckEveryCycles = item.StableCheckEveryCycles,
                CyclesUntilCheck = item.CyclesUntilCheck,
                QueueState = queueState
            };
        }).ToArray();

        return Ok(new { updatedAt = now, items });
    }

    [Authorize(Policy = AccessPolicies.ManageMaintenance)]
    [HttpPut("health/queue")]
    public async Task<ActionResult> UpdateCheckQueue([FromBody] UpdateCheckQueueDto dto)
    {
        var items = dto.Items ?? [];
        if (items.Any(item => item.CameraId <= 0 || item.Position <= 0))
            return BadRequest(new { message = "Камера и позиция очереди должны быть указаны." });
        if (items.Select(item => item.CameraId).Distinct().Count() != items.Count ||
            items.Select(item => item.Position).Distinct().Count() != items.Count)
            return BadRequest(new { message = "Камеры и позиции в очереди не должны повторяться." });
        if (items.Any(item => item.CheckIntervalSeconds is < 10 or > 86400))
            return BadRequest(new { message = "Интервал проверки должен быть от 10 до 86400 секунд." });

        try
        {
            await _cameraRepository.UpdateCheckQueueAsync(items
                .Select(item => new CameraCheckQueueUpdate(
                    item.CameraId,
                    item.Position,
                    item.AutomaticChecksEnabled,
                    item.CheckIntervalSeconds))
                .ToArray());
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }

        return Ok(new { message = "Очередь проверок сохранена." });
    }

    [Authorize(Policy = AccessPolicies.ManageMaintenance)]
    [HttpPatch("health/queue/{cameraId:int}/automatic-checks")]
    public async Task<ActionResult> SetAutomaticChecks(int cameraId, [FromBody] SetAutomaticChecksDto dto)
    {
        if (cameraId <= 0 || !await _cameraRepository.SetAutomaticChecksEnabledAsync(cameraId, dto.Enabled))
            return NotFound(new { message = "Камера не найдена." });

        return Ok(new
        {
            cameraId,
            automaticChecksEnabled = dto.Enabled,
            message = dto.Enabled
                ? "Камера добавлена в цикл автоматических проверок."
                : "Камера исключена из цикла автоматических проверок."
        });
    }

    [Authorize(Policy = AccessPolicies.ViewCameras)]
    [HttpGet("health/status")]
    public async Task<ActionResult> GetHealthSummary()
    {
        var summary = await _healthService.GetHealthSummaryAsync();
        return Ok(summary);
    }

    private static HealthRecordDto ToDto(HealthRecord record) => new()
    {
        Id = record.Id,
        CameraId = record.CameraId,
        Reachable = record.Reachable,
        StatusCode = record.StatusCode,
        ResponseTimeMs = record.ResponseTimeMs,
        TemperatureC = record.TemperatureC,
        UptimeSeconds = record.UptimeSeconds,
        ErrorMessage = record.ErrorMessage,
        DetailJson = record.DetailJson,
        CheckedAt = record.CheckedAt
    };
}

public record HealthRecordDto
{
    public int Id { get; set; }
    public int CameraId { get; set; }
    public bool Reachable { get; set; }
    public string? StatusCode { get; set; }
    public int ResponseTimeMs { get; set; }
    public double? TemperatureC { get; set; }
    public int? UptimeSeconds { get; set; }
    public string? ErrorMessage { get; set; }
    public string DetailJson { get; set; } = "{}";
    public DateTime CheckedAt { get; set; }
}

public record BatchHealthDto
{
    public List<int>? CameraIds { get; set; }
    public bool All { get; set; }
    public string? Filter { get; set; }
}

public sealed record CameraCheckQueueDto
{
    public int CameraId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? IpAddress { get; init; }
    public string Status { get; init; } = string.Empty;
    public int Position { get; init; }
    public bool AutomaticChecksEnabled { get; init; }
    public int CheckIntervalSeconds { get; init; }
    public DateTime? LastCheckAt { get; init; }
    public DateTime? NextCheckAt { get; init; }
    public bool DueNow { get; init; }
    public bool HasPhysicalFault { get; init; }
    public int ConsecutiveHealthyChecks { get; init; }
    public bool IsStable { get; init; }
    public int? EffectiveCheckIntervalSeconds { get; init; }
    public bool StableChecksPaused { get; init; }
    public long NextCycleNumber { get; init; }
    public int StableCheckEveryCycles { get; init; }
    public int? CyclesUntilCheck { get; init; }
    public string QueueState { get; init; } = string.Empty;
}

public sealed record UpdateCheckQueueDto
{
    public List<UpdateCheckQueueItemDto>? Items { get; init; }
}

public sealed record UpdateCheckQueueItemDto
{
    public int CameraId { get; init; }
    public int Position { get; init; }
    public bool AutomaticChecksEnabled { get; init; }
    public int CheckIntervalSeconds { get; init; }
}

public sealed record SetAutomaticChecksDto
{
    public bool Enabled { get; init; }
}
