using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Infrastructure.Security;
using CameraMaintainer.Onvif;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Services;

public class ConfigService : IConfigService
{
    private readonly ICameraRepository _cameraRepo;
    private readonly OnvifClient _onvif;
    private readonly ICredentialEncryptor _encryptor;
    private readonly ILogger<ConfigService> _logger;

    public ConfigService(
        ICameraRepository cameraRepo,
        OnvifClient onvif,
        ICredentialEncryptor encryptor,
        ILogger<ConfigService> logger)
    {
        _cameraRepo = cameraRepo;
        _onvif = onvif;
        _encryptor = encryptor;
        _logger = logger;
    }

    public async Task<NetworkConfig> GetNetworkConfigAsync(int cameraId)
    {
        var camera = await GetCameraWithCredential(cameraId);
        var password = _encryptor.Decrypt(camera.Credential!.PasswordEnc);
        return await _onvif.GetNetworkConfigAsync(camera.ServiceUrl, camera.Credential.Username, password);
    }

    public async Task<List<MediaProfile>> GetMediaProfilesAsync(int cameraId)
    {
        var camera = await GetCameraWithCredential(cameraId);
        var password = _encryptor.Decrypt(camera.Credential!.PasswordEnc);
        return await _onvif.GetProfilesAsync(camera.ServiceUrl, camera.Credential.Username, password);
    }

    public async Task<List<UserInfo>> GetUsersAsync(int cameraId)
    {
        var camera = await GetCameraWithCredential(cameraId);
        var password = _encryptor.Decrypt(camera.Credential!.PasswordEnc);
        return await _onvif.GetUsersAsync(camera.ServiceUrl, camera.Credential.Username, password);
    }

    public async Task CreateUserAsync(int cameraId, string newUsername, string newPassword, string userLevel)
    {
        var camera = await GetCameraWithCredential(cameraId);
        var password = _encryptor.Decrypt(camera.Credential!.PasswordEnc);
        await _onvif.CreateUserAsync(camera.ServiceUrl, camera.Credential.Username, password,
            newUsername, newPassword, userLevel);
    }

    public async Task DeleteUserAsync(int cameraId, string targetUsername)
    {
        var camera = await GetCameraWithCredential(cameraId);
        if (string.Equals(
            camera.Credential!.Username,
            targetUsername,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Нельзя удалить учётную запись, которую система использует для подключения к камере.");
        }

        var password = _encryptor.Decrypt(camera.Credential!.PasswordEnc);
        await _onvif.DeleteUserAsync(camera.ServiceUrl, camera.Credential.Username, password, targetUsername);
    }

    public async Task SyncDateTimeAsync(int cameraId)
    {
        var camera = await GetCameraWithCredential(cameraId);
        var password = _encryptor.Decrypt(camera.Credential!.PasswordEnc);

        var currentTime = await _onvif.GetSystemDateTimeAsync(
            camera.ServiceUrl, camera.Credential.Username, password);

        // If camera time is off by more than 5 seconds, sync it
        var diff = Math.Abs((currentTime.UtcDateTime - DateTime.UtcNow).TotalSeconds);
        if (diff > 5)
        {
            _logger.LogInformation("Syncing time for camera {CameraId} (off by {Diff}s)", cameraId, diff);
            await _onvif.SetSystemDateTimeAsync(
                camera.ServiceUrl, camera.Credential.Username, password, DateTime.UtcNow);
        }
    }

    private async Task<Camera> GetCameraWithCredential(int cameraId)
    {
        var camera = await _cameraRepo.GetByIdAsync(cameraId);
        if (camera == null)
            throw new ArgumentException($"Camera {cameraId} not found");
        if (camera.Credential == null)
            throw new InvalidOperationException($"No credentials for camera {cameraId}");
        return camera;
    }
}
