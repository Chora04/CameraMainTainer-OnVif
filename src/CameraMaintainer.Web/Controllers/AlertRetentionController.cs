using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CameraMaintainer.Web.Controllers;

[ApiController]
[Route("api/settings/error-retention")]
[Authorize(Policy = AccessPolicies.ManageUsers)]
public sealed class AlertRetentionController : ControllerBase
{
    private readonly AlertRetentionService _retention;

    public AlertRetentionController(AlertRetentionService retention)
    {
        _retention = retention;
    }

    [HttpGet]
    public async Task<ActionResult<AlertRetentionOverview>> Get(CancellationToken cancellationToken)
    {
        return Ok(await _retention.GetOverviewAsync(DateTime.UtcNow, cancellationToken));
    }

    [HttpPut]
    public async Task<ActionResult<AlertRetentionOverview>> Save(
        [FromBody] AlertRetentionRequest request,
        CancellationToken cancellationToken)
    {
        await _retention.SavePolicyAsync(request.RetentionMonths, cancellationToken);
        await _retention.CleanupAsync(DateTime.UtcNow, cancellationToken);
        return Ok(await _retention.GetOverviewAsync(DateTime.UtcNow, cancellationToken));
    }

    [HttpPost("cleanup")]
    public async Task<ActionResult<AlertRetentionOverview>> Cleanup(CancellationToken cancellationToken)
    {
        await _retention.CleanupAsync(DateTime.UtcNow, cancellationToken);
        return Ok(await _retention.GetOverviewAsync(DateTime.UtcNow, cancellationToken));
    }
}

public sealed record AlertRetentionRequest(int RetentionMonths);
