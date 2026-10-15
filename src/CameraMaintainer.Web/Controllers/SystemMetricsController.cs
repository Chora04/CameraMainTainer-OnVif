using CameraMaintainer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CameraMaintainer.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/system/metrics")]
public sealed class SystemMetricsController(SystemMetricsService metrics) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult<SystemMetricsSnapshot> Get() => Ok(metrics.GetSnapshot());
}
