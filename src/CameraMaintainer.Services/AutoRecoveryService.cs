using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Infrastructure.Security;
using CameraMaintainer.Onvif;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Services;

public class AutoRecoveryService : IAutoRecoveryService
{
    private readonly ICameraRepository _cameraRepo;
    private readonly IAlertService _alertService;
    private readonly OnvifClient _onvif;
    private readonly ICredentialEncryptor _encryptor;
    private readonly ILogger<AutoRecoveryService> _logger;

    public AutoRecoveryService(
        ICameraRepository cameraRepo,
        IAlertService alertService,
        OnvifClient onvif,
        ICredentialEncryptor encryptor,
        ILogger<AutoRecoveryService> logger)
    {
        _cameraRepo = cameraRepo;
        _alertService = alertService;
        _onvif = onvif;
        _encryptor = encryptor;
        _logger = logger;
    }

    /// <summary>
    /// Attempt to automatically recover a failing camera.
    /// Strategies: wait and retry, then reboot when the camera still accepts ONVIF commands.
    /// Returns true if recovery succeeded.
    /// </summary>
    public async Task<bool> TryAutoRecoverAsync(
        int cameraId,
        string statusCode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var camera = await _cameraRepo.GetByIdAsync(cameraId);
        if (camera == null || camera.Credential == null) return false;

        var password = _encryptor.Decrypt(camera.Credential.PasswordEnc);
        var username = camera.Credential.Username;

        _logger.LogInformation("Auto-recovery started for camera {CameraId} ({Ip}), status: {Status}",
            cameraId, camera.IpAddress, statusCode);

        // Strategy 1: Wait and retry (for transient network issues)
        if (statusCode is "timeout" or "connection_refused")
        {
            _logger.LogInformation("Retry strategy: waiting 30s before retry for camera {CameraId}", cameraId);
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

            try
            {
                await _onvif.GetDeviceInfoAsync(camera.ServiceUrl, username, password, 15000);
                _logger.LogInformation("Camera {CameraId} recovered after retry", cameraId);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Retry failed for camera {CameraId}", cameraId);
            }
        }

        // Strategy 2: Reboot camera (for cameras that are still reachable but malfunctioning)
        if (statusCode is "soap_fault" or "unknown_error")
        {
            try
            {
                _logger.LogInformation("Reboot strategy: sending reboot command to camera {CameraId}", cameraId);
                await _onvif.RebootAsync(camera.ServiceUrl, username, password, 10000);

                // Log the maintenance action
                _logger.LogInformation("Camera {CameraId} rebooted — waiting 120s for it to come back", cameraId);

                // Wait for camera to come back
                await Task.Delay(TimeSpan.FromSeconds(120), cancellationToken);

                try
                {
                    await _onvif.GetDeviceInfoAsync(camera.ServiceUrl, username, password, 15000);
                    _logger.LogInformation("Camera {CameraId} recovered after reboot", cameraId);
                    return true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Camera {CameraId} did not come back after reboot", cameraId);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reboot command failed for camera {CameraId}", cameraId);
            }
        }

        // All strategies failed — create critical alert for manager
        await _alertService.CreateAlertAsync(
            cameraId,
            AlertType.AutoRecoveryFailed,
            AlertSeverity.Critical,
            $"Camera {camera.Name} ({camera.IpAddress}) auto-recovery failed",
            $"Status: {statusCode}. All automated recovery attempts failed. Manual intervention required."
        );

        return false;
    }
}
