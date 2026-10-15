using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CameraMaintainer.Web.Controllers;

[ApiController]
[Route("api/cameras/{cameraId:int}/frames")]
[Authorize(Policy = AccessPolicies.ViewCameras)]
public sealed class CameraFramesController(
    ICameraRepository cameraRepository,
    ICameraFrameArchiveService frameArchive) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetRecent(
        int cameraId,
        CancellationToken cancellationToken)
    {
        if (await cameraRepository.GetByIdAsync(cameraId) == null)
            return NotFound();

        var records = await frameArchive.GetRecentAsync(cameraId, cancellationToken);
        return Ok(records.Select(record => new
        {
            record.Id,
            record.CapturedAt,
            record.IsAnomalous,
            triggerCodes = record.TriggerCodes.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            record.DirectionStatus,
            record.ObstructionType,
            record.MeanBrightness,
            record.BrightnessStdDev,
            record.Entropy,
            record.EdgeDensity,
            record.TextureUniformityScore,
            record.DirectionConfidence,
            hasImage = record.ImageRelativePath != null,
            record.ImageSizeBytes,
            contentUrl = record.ImageRelativePath == null
                ? null
                : $"/api/cameras/{cameraId}/frames/{record.Id}/content"
        }));
    }

    [HttpGet("{recordId:long}/content")]
    public async Task<IActionResult> GetContent(
        int cameraId,
        long recordId,
        CancellationToken cancellationToken)
    {
        var content = await frameArchive.OpenContentAsync(cameraId, recordId, cancellationToken);
        if (content == null)
            return NotFound();

        Response.Headers.CacheControl = "private, max-age=86400, immutable";
        return File(content.Stream, content.ContentType, enableRangeProcessing: true);
    }
}
