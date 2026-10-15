using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CameraMaintainer.Web.Controllers;

[ApiController]
[Route("api/discovery")]
public class DiscoveryController : ControllerBase
{
    private readonly IDiscoveryService _discoveryService;
    private readonly ILogger<DiscoveryController> _logger;

    public DiscoveryController(IDiscoveryService discoveryService, ILogger<DiscoveryController> logger)
    {
        _discoveryService = discoveryService;
        _logger = logger;
    }

    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpPost("scan")]
    public async Task<ActionResult> StartScan(
        [FromBody] ScanRequestDto? dto,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Discovery scan requested: type={Type}", dto?.ScanType ?? "ws_discovery");

        var scan = await _discoveryService.ScanAsync(
            dto?.ScanType ?? "ws_discovery",
            dto?.IpRange,
            cancellationToken);

        return Ok(scan);
    }

    [Authorize(Policy = AccessPolicies.ViewCameras)]
    [HttpGet("scan/{scanId}")]
    public async Task<ActionResult> GetScanStatus(int scanId)
    {
        var scan = await _discoveryService.GetScanStatusAsync(scanId);
        if (scan == null) return NotFound();
        return Ok(scan);
    }
}

public record ScanRequestDto
{
    public string ScanType { get; set; } = "ws_discovery";
    public string? IpRange { get; set; }
}
