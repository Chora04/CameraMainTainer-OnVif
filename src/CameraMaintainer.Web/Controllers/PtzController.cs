using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CameraMaintainer.Web.Controllers;

[ApiController]
[Route("api/cameras/{cameraId}/ptz")]
public class PtzController : ControllerBase
{
    private readonly IPtzService _ptzService;

    public PtzController(IPtzService ptzService)
    {
        _ptzService = ptzService;
    }

    [Authorize(Policy = AccessPolicies.ViewCameras)]
    [HttpGet("presets")]
    public async Task<ActionResult> GetPresets(int cameraId)
    {
        var presets = await _ptzService.GetPresetsAsync(cameraId);
        return Ok(presets);
    }

    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpPost("presets")]
    public async Task<ActionResult> SetPreset(int cameraId, [FromBody] SetPresetDto dto)
    {
        if (!IsValidToken(dto.ProfileToken) || string.IsNullOrWhiteSpace(dto.PresetName) || dto.PresetName.Trim().Length > 128)
            return BadRequest(new { message = "Укажите корректные профиль и название пресета до 128 символов." });

        var preset = await _ptzService.SetPresetAsync(cameraId, dto.ProfileToken, dto.PresetName);
        return Ok(preset);
    }

    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpPost("goto")]
    public async Task<ActionResult> GotoPreset(int cameraId, [FromBody] GotoPresetDto dto)
    {
        if (!IsValidToken(dto.ProfileToken) || !IsValidToken(dto.PresetToken))
            return BadRequest(new { message = "Профиль или пресет не указан." });

        await _ptzService.GotoPresetAsync(cameraId, dto.ProfileToken, dto.PresetToken);
        return Ok(new { message = "Переход к предустановленной позиции" });
    }

    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpPost("move")]
    public async Task<ActionResult> Move(int cameraId, [FromBody] MoveDto dto)
    {
        if (!IsValidToken(dto.ProfileToken) ||
            !double.IsFinite(dto.Pan) || !double.IsFinite(dto.Tilt) || !double.IsFinite(dto.Zoom))
            return BadRequest(new { message = "Координаты PTZ должны быть конечными числами." });

        var mode = dto.Mode.Trim().ToLowerInvariant();
        var positionInRange = dto.Pan is >= -1 and <= 1 &&
            dto.Tilt is >= -1 and <= 1 &&
            dto.Zoom is >= -1 and <= 1;
        if (!positionInRange || (mode == "absolute" && dto.Zoom < 0))
            return BadRequest(new { message = "Координаты PTZ выходят за диапазон ONVIF от -1 до 1." });

        if (mode == "relative")
            await _ptzService.MoveRelativeAsync(cameraId, dto.ProfileToken, dto.Pan, dto.Tilt, dto.Zoom);
        else if (mode == "absolute")
            await _ptzService.MoveAbsoluteAsync(cameraId, dto.ProfileToken, dto.Pan, dto.Tilt, dto.Zoom);
        else
            return BadRequest(new { message = "Режим PTZ должен быть absolute или relative." });

        return Ok(new { message = "Команда перемещения отправлена" });
    }

    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpPost("stop")]
    public async Task<ActionResult> Stop(int cameraId, [FromBody] ProfileDto dto)
    {
        if (!IsValidToken(dto.ProfileToken))
            return BadRequest(new { message = "Профиль PTZ не указан." });

        await _ptzService.StopAsync(cameraId, dto.ProfileToken);
        return Ok(new { message = "Команда остановки отправлена" });
    }

    private static bool IsValidToken(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;
}

public record SetPresetDto(string ProfileToken, string PresetName);
public record GotoPresetDto(string ProfileToken, string PresetToken);
public record MoveDto(string ProfileToken, double Pan, double Tilt, double Zoom, string Mode = "absolute");
public record ProfileDto(string ProfileToken);
