using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CameraMaintainer.Web.Controllers;

[ApiController]
[Route("api/cameras/{cameraId}/config")]
public class ConfigController : ControllerBase
{
    private readonly IConfigService _configService;

    public ConfigController(IConfigService configService)
    {
        _configService = configService;
    }

    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpGet("network")]
    public async Task<ActionResult> GetNetworkConfig(int cameraId)
    {
        var config = await _configService.GetNetworkConfigAsync(cameraId);
        return Ok(config);
    }

    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpGet("video")]
    [HttpGet("media-profiles")]
    public async Task<ActionResult> GetVideoProfiles(int cameraId)
    {
        var profiles = await _configService.GetMediaProfilesAsync(cameraId);
        return Ok(profiles);
    }

    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpGet("users")]
    public async Task<ActionResult> GetUsers(int cameraId)
    {
        var users = await _configService.GetUsersAsync(cameraId);
        return Ok(users);
    }

    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpPost("users")]
    public async Task<ActionResult> CreateUser(int cameraId, [FromBody] CreateUserDto dto)
    {
        var username = dto.Username.Trim();
        if (username.Length is < 1 or > 64 || username.Any(char.IsControl))
            return BadRequest(new { message = "Имя пользователя должно содержать от 1 до 64 символов." });
        if (dto.Password.Length is < 8 or > 128)
            return BadRequest(new { message = "Пароль должен содержать от 8 до 128 символов." });

        var userLevel = NormalizeUserLevel(dto.UserLevel);
        if (userLevel == null)
            return BadRequest(new { message = "Допустимые уровни: Administrator, Operator, User или Anonymous." });

        await _configService.CreateUserAsync(cameraId, username, dto.Password, userLevel);
        return Ok(new { message = "Пользователь камеры создан" });
    }

    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpDelete("users/{username}")]
    public async Task<ActionResult> DeleteUser(int cameraId, string username)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 64)
            return BadRequest(new { message = "Имя пользователя не указано." });

        try
        {
            await _configService.DeleteUserAsync(cameraId, username.Trim());
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }

        return Ok(new { message = "Пользователь камеры удалён" });
    }

    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpPost("sync-time")]
    public async Task<ActionResult> SyncTime(int cameraId)
    {
        await _configService.SyncDateTimeAsync(cameraId);
        return Ok(new { message = "Время синхронизировано" });
    }

    private static string? NormalizeUserLevel(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "user" => "User",
            "administrator" => "Administrator",
            "operator" => "Operator",
            "anonymous" => "Anonymous",
            _ => null
        };
    }
}

public record CreateUserDto
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? UserLevel { get; set; }
}
