using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CameraMaintainer.Web.Controllers;

[ApiController]
[Route("api")]
public class MaintenanceController : ControllerBase
{
    private readonly IMaintenanceService _maintService;

    public MaintenanceController(IMaintenanceService maintService)
    {
        _maintService = maintService;
    }

    [Authorize(Policy = AccessPolicies.ManageMaintenance)]
    [HttpPost("cameras/{cameraId}/maintenance/backup")]
    public async Task<ActionResult> BackupConfig(int cameraId, [FromBody] BackupDto? dto)
    {
        var backup = await _maintService.BackupConfigAsync(
            cameraId,
            dto?.BackupType ?? "full",
            User.Identity?.Name ?? "manual",
            HttpContext.RequestAborted);
        return Ok(backup);
    }

    [Authorize(Policy = AccessPolicies.ViewMaintenance)]
    [HttpGet("cameras/{cameraId}/maintenance/backups")]
    public async Task<ActionResult> GetBackups(int cameraId)
    {
        var backups = await _maintService.GetBackupsAsync(cameraId);
        return Ok(backups);
    }

    [Authorize(Policy = AccessPolicies.ManageMaintenance)]
    [HttpPost("cameras/{cameraId}/maintenance/reboot")]
    public async Task<ActionResult> Reboot(int cameraId)
    {
        var job = await _maintService.RebootCameraAsync(cameraId);
        return Ok(job);
    }

    [Authorize(Policy = AccessPolicies.ManageMaintenance)]
    [HttpPost("cameras/{cameraId}/maintenance/factory-reset")]
    public async Task<ActionResult> FactoryReset(int cameraId, [FromBody] FactoryResetDto? dto)
    {
        var mode = dto?.Mode?.Trim().ToLowerInvariant() ?? "hard";
        if (mode is not "hard" and not "soft")
            return BadRequest(new { message = "Режим сброса должен быть hard или soft" });

        var job = await _maintService.FactoryResetCameraAsync(cameraId, mode);
        return Ok(job);
    }

    [Authorize(Policy = AccessPolicies.ViewMaintenance)]
    [HttpGet("jobs/{jobId}")]
    public async Task<ActionResult> GetJobStatus(int jobId)
    {
        var job = await _maintService.GetJobStatusAsync(jobId);
        if (job == null) return NotFound();
        return Ok(job);
    }

}

public record BackupDto(string BackupType = "full");
public record FactoryResetDto(string Mode = "hard");
