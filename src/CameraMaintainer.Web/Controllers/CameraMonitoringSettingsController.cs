using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CameraMaintainer.Web.Controllers;

[ApiController]
[Route("api/settings/camera-monitoring")]
[Authorize(Policy = AccessPolicies.ManageMaintenance)]
public sealed class CameraMonitoringSettingsController(CameraMonitoringPolicyRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CameraMonitoringPolicy>> Get() => Ok(await repository.GetAsync());

    [HttpPut]
    public async Task<ActionResult> Save(CameraMonitoringPolicy policy)
    {
        if (!policy.IsValid)
            return BadRequest(new
            {
                message = "Проверьте интервалы мониторинга, лимит истории и допустимые диапазоны показателей кадра."
            });
        await repository.SaveAsync(policy);
        return Ok(policy);
    }
}
