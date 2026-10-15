using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Infrastructure.Security;
using CameraMaintainer.Onvif;

namespace CameraMaintainer.Services;

public class PtzService : IPtzService
{
    private readonly ICameraRepository _cameraRepo;
    private readonly AppDbContext _db;
    private readonly OnvifClient _onvif;
    private readonly ICredentialEncryptor _encryptor;

    public PtzService(
        ICameraRepository cameraRepo,
        AppDbContext db,
        OnvifClient onvif,
        ICredentialEncryptor encryptor)
    {
        _cameraRepo = cameraRepo;
        _db = db;
        _onvif = onvif;
        _encryptor = encryptor;
    }

    public async Task<List<PtzPreset>> GetPresetsAsync(int cameraId)
    {
        var camera = await GetCameraWithCredential(cameraId);
        var password = _encryptor.Decrypt(camera.Credential!.PasswordEnc);

        // Get media profiles first, then PTZ presets for each
        var profiles = await _onvif.GetProfilesAsync(camera.ServiceUrl, camera.Credential.Username, password);
        var defaultProfile = profiles.FirstOrDefault();

        if (defaultProfile == null)
            return new List<PtzPreset>();

        var onvifPresets = await _onvif.GetPresetsAsync(
            camera.ServiceUrl, camera.Credential.Username, password, defaultProfile.Token);

        // Sync with database
        var result = new List<PtzPreset>();
        foreach (var p in onvifPresets)
        {
            var preset = new PtzPreset
            {
                CameraId = cameraId,
                ProfileToken = defaultProfile.Token,
                PresetToken = p.Token,
                PresetName = p.Name
            };

            // Try to find existing or add
            var existing = camera.PtzPresets
                .FirstOrDefault(pp => pp.ProfileToken == defaultProfile.Token && pp.PresetToken == p.Token);

            if (existing != null)
                result.Add(existing);
            else
            {
                _db.PtzPresets.Add(preset);
                result.Add(preset);
            }
        }

        await _db.SaveChangesAsync();
        return result;
    }

    public async Task<PtzPreset> SetPresetAsync(int cameraId, string profileToken, string presetName)
    {
        var camera = await GetCameraWithCredential(cameraId);
        var password = _encryptor.Decrypt(camera.Credential!.PasswordEnc);

        var token = await _onvif.SetPresetAsync(
            camera.ServiceUrl, camera.Credential.Username, password, profileToken, presetName);

        var preset = new PtzPreset
        {
            CameraId = cameraId,
            ProfileToken = profileToken,
            PresetToken = token,
            PresetName = presetName
        };

        _db.PtzPresets.Add(preset);
        await _db.SaveChangesAsync();

        return preset;
    }

    public async Task GotoPresetAsync(int cameraId, string profileToken, string presetToken)
    {
        var camera = await GetCameraWithCredential(cameraId);
        var password = _encryptor.Decrypt(camera.Credential!.PasswordEnc);

        await _onvif.GotoPresetAsync(
            camera.ServiceUrl, camera.Credential.Username, password, profileToken, presetToken);
    }

    public async Task MoveAbsoluteAsync(int cameraId, string profileToken, double pan, double tilt, double zoom)
    {
        var camera = await GetCameraWithCredential(cameraId);
        var password = _encryptor.Decrypt(camera.Credential!.PasswordEnc);

        await _onvif.AbsoluteMoveAsync(
            camera.ServiceUrl, camera.Credential.Username, password, profileToken, pan, tilt, zoom);
    }

    public async Task MoveRelativeAsync(int cameraId, string profileToken, double pan, double tilt, double zoom)
    {
        var camera = await GetCameraWithCredential(cameraId);
        var password = _encryptor.Decrypt(camera.Credential!.PasswordEnc);

        await _onvif.RelativeMoveAsync(
            camera.ServiceUrl, camera.Credential.Username, password, profileToken, pan, tilt, zoom);
    }

    public async Task StopAsync(int cameraId, string profileToken)
    {
        var camera = await GetCameraWithCredential(cameraId);
        var password = _encryptor.Decrypt(camera.Credential!.PasswordEnc);

        await _onvif.StopMoveAsync(
            camera.ServiceUrl, camera.Credential.Username, password, profileToken);
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
