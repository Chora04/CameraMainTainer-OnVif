using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Security;
using CameraMaintainer.Onvif;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Services;

/// <summary>
/// Automatically corrects camera PTZ position when wall obstruction is detected.
///
/// Strategy:
/// 1. When DirectionStatus = WallObstructed, attempt a recovery pan/tilt sweep
/// 2. Pan through the generic ONVIF position range, checking direction after each move
/// 3. If a clear view is found, set a PTZ preset "auto_recovered"
/// 4. If no clear view after 180° sweep, create critical alert
///
/// This is the autonomous correction for "камера повернута к стене".
/// </summary>
public class PtzAutoCorrectionService
{
    private readonly OnvifClient _onvif;
    private readonly ICameraRepository _cameraRepo;
    private readonly ICredentialEncryptor _encryptor;
    private readonly ICameraDirectionAnalyzer _directionAnalyzer;
    private readonly IFrameProcessor _frameProcessor;
    private readonly IAlertService _alertService;
    private readonly ILogger<PtzAutoCorrectionService>? _logger;

    // Sweep parameters
    public double SweepStep { get; set; } = 0.2;
    public double SweepMaximum { get; set; } = 1.0;
    public int NormalConfidenceThreshold { get; set; } = 80; // Min confidence % to accept as "clear"

    public PtzAutoCorrectionService(
        OnvifClient onvif,
        ICameraRepository cameraRepo,
        ICredentialEncryptor encryptor,
        ICameraDirectionAnalyzer directionAnalyzer,
        IFrameProcessor frameProcessor,
        IAlertService alertService,
        ILogger<PtzAutoCorrectionService>? logger = null)
    {
        _onvif = onvif;
        _cameraRepo = cameraRepo;
        _encryptor = encryptor;
        _directionAnalyzer = directionAnalyzer;
        _frameProcessor = frameProcessor;
        _alertService = alertService;
        _logger = logger;
    }

    /// <summary>
    /// Result of a PTZ auto-correction attempt.
    /// </summary>
    public record CorrectionResult(
        bool Success,
        string Strategy,
        double FinalPan,
        double FinalTilt,
        int Attempts,
        string Description
    );

    /// <summary>
    /// Attempt to auto-correct camera position when wall obstruction is detected.
    /// Returns the correction result with final position if successful.
    /// </summary>
    public async Task<CorrectionResult> TryCorrectAsync(
        int cameraId,
        string? profileToken = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var camera = await _cameraRepo.GetByIdAsync(cameraId);
        if (camera?.Credential == null)
        {
            return new CorrectionResult(false, "none", 0, 0, 0,
                "No credentials configured for camera");
        }

        if (!camera.PtzSupported)
        {
            return new CorrectionResult(false, "none", 0, 0, 0,
                "PTZ is not supported on this camera");
        }

        var password = _encryptor.Decrypt(camera.Credential.PasswordEnc);
        var username = camera.Credential.Username;

        try
        {
            // Get PTZ profiles
            var ptzProfiles = await _onvif.GetPtzProfilesAsync(
                camera.ServiceUrl, username, password, 5000);

            var activeProfile = profileToken != null
                ? ptzProfiles.FirstOrDefault(p => p.Token == profileToken)
                : ptzProfiles.FirstOrDefault();

            if (activeProfile == null)
            {
                return new CorrectionResult(false, "none", 0, 0, 0,
                    "No PTZ profile available");
            }

            var token = activeProfile.Token;

            // Get current snapshot URI for direction checking
            var profiles = await _onvif.GetProfilesAsync(
                camera.ServiceUrl, username, password, 5000);
            var mediaProfile = profiles.FirstOrDefault();
            if (mediaProfile == null)
            {
                return new CorrectionResult(false, "none", 0, 0, 0,
                    "No media profile available");
            }

            var snapshotUri = await _onvif.GetSnapshotUriAsync(
                camera.ServiceUrl, username, password, mediaProfile.Token, 5000);

            if (string.IsNullOrEmpty(snapshotUri))
            {
                return new CorrectionResult(false, "none", 0, 0, 0,
                    "Cannot get snapshot URI");
            }

            _logger?.LogInformation(
                "PTZ auto-correction started for camera {CameraId} ({Name})", cameraId, camera.Name);

            // ── Strategy 1: Full pan sweep ──
            // Try center first (pan=0, tilt=0)
            await _onvif.AbsoluteMoveAsync(camera.ServiceUrl, username, password,
                token, pan: 0, tilt: 0, zoom: 0, timeoutMs: 5000);
            await Task.Delay(1500, cancellationToken); // Wait for camera to settle

            var frame = await _frameProcessor.CaptureAndDecodeAsync(snapshotUri, username, password,
                maxWidth: 640, ct: cancellationToken);
            if (frame.IsValid)
            {
                var result = _directionAnalyzer.Analyze(frame, cameraId);

                if (result.Status == DirectionStatus.Normal && IsConfidenceAccepted(result.Confidence))
                {
                    // Already clear at center
                    await SaveRecoveryPreset(camera, username, password, token, "auto_recovered_center");
                    return new CorrectionResult(true, "center_clear", 0, 0, 1,
                        $"Center position is clear (confidence={result.Confidence:F2})");
                }
            }

            // Sweep left, then right
            var directions = new[] { -1.0, 1.0 }; // left, right
            var sweepStep = Math.Clamp(SweepStep, 0.05, 1.0);
            var sweepMaximum = Math.Clamp(SweepMaximum, sweepStep, 1.0);
            var steps = Math.Max(1, (int)Math.Ceiling(sweepMaximum / sweepStep));
            int attempts = 0;

            foreach (var dir in directions)
            {
                for (int step = 1; step <= steps; step++)
                {
                    attempts++;
                    cancellationToken.ThrowIfCancellationRequested();
                    double pan = dir * Math.Min(step * sweepStep, sweepMaximum);

                    _logger?.LogDebug("Sweep attempt {Attempt}: normalized pan={Pan:F2}", attempts, pan);

                    // Move camera
                    await _onvif.AbsoluteMoveAsync(camera.ServiceUrl, username, password,
                        token, pan, tilt: 0, zoom: 0, timeoutMs: 5000);
                    await Task.Delay(1500, cancellationToken);

                    // Capture and analyze
                    frame = await _frameProcessor.CaptureAndDecodeAsync(
                        snapshotUri, username, password, maxWidth: 640, ct: cancellationToken);

                    if (!frame.IsValid) continue;

                    var dirResult = _directionAnalyzer.Analyze(frame, cameraId);

                    if (dirResult.Status == DirectionStatus.Normal && IsConfidenceAccepted(dirResult.Confidence))
                    {
                        var presetName = pan > 0
                            ? $"auto_recovered_right_{pan:F0}"
                            : $"auto_recovered_left_{Math.Abs(pan):F0}";

                        await SaveRecoveryPreset(camera, username, password, token, presetName);

                        _logger?.LogInformation(
                            "PTZ auto-correction SUCCESS for camera {CameraId}: normalized pan={Pan:F2}, " +
                            "confidence={Conf:F2}, edgeDensity={Edge:F1}%",
                            cameraId, pan, dirResult.Confidence, dirResult.EdgeDensity);

                        await _alertService.CreateAlertAsync(
                            cameraId, AlertType.ConfigurationChanged, AlertSeverity.Info,
                            $"Camera {camera.Name} auto-corrected from wall obstruction",
                            $"New normalized position: pan={pan:F2}, tilt=0. " +
                            $"Preset saved as '{presetName}'.");

                        return new CorrectionResult(true, "sweep", pan, 0, attempts,
                            $"Clear view found at normalized pan={pan:F2} after {attempts} attempts");
                    }

                    // If we see partial obstruction, try smaller movements
                    if (dirResult.Status == DirectionStatus.PartiallyObstructed)
                    {
                        _logger?.LogDebug("Partially obstructed at normalized pan={Pan:F2}; continuing sweep", pan);
                    }
                }
            }

            // ── Strategy 2: Return to last known good preset ──
            var lastGoodPreset = camera.PtzPresets
                .FirstOrDefault(p => p.PresetName.StartsWith("auto_recovered_", StringComparison.Ordinal));
            if (lastGoodPreset != null)
            {
                _logger?.LogInformation("Returning to last good preset: {Preset}", lastGoodPreset.PresetName);
                await _onvif.GotoPresetAsync(camera.ServiceUrl, username, password,
                    lastGoodPreset.ProfileToken, lastGoodPreset.PresetToken, 5000);
                await Task.Delay(1500, cancellationToken);

                return new CorrectionResult(true, "return_to_preset", 0, 0, attempts,
                    $"Returned to saved preset '{lastGoodPreset.PresetName}'");
            }

            // All strategies failed
            _logger?.LogWarning("PTZ auto-correction FAILED for camera {CameraId} after {Attempts} attempts",
                cameraId, attempts);

            await _alertService.CreateAlertAsync(
                cameraId, AlertType.AutoRecoveryFailed, AlertSeverity.Critical,
                $"Camera {camera.Name} — auto-correction failed after {attempts} sweep attempts",
                "Camera may be physically stuck, lens cap on, or in complete darkness. " +
                "Manual inspection required.");

            return new CorrectionResult(false, "exhausted", 0, 0, attempts,
                $"No clear view found after {attempts} sweep attempts");

        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "PTZ auto-correction error for camera {CameraId}", cameraId);
            return new CorrectionResult(false, "error", 0, 0, 0,
                $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Quick check — is the camera currently facing a wall?
    /// </summary>
    public async Task<(bool IsObstructed, DirectionStatus Status, double Confidence)> QuickCheckAsync(
        int cameraId, CancellationToken ct = default)
    {
        var camera = await _cameraRepo.GetByIdAsync(cameraId);
        if (camera?.Credential == null)
            return (false, DirectionStatus.Unknown, 0);

        var password = _encryptor.Decrypt(camera.Credential.PasswordEnc);
        var username = camera.Credential.Username;

        try
        {
            var profiles = await _onvif.GetProfilesAsync(
                camera.ServiceUrl, username, password, 5000);
            var profile = profiles.FirstOrDefault();
            if (profile == null) return (false, DirectionStatus.Unknown, 0);

            var snapshotUri = await _onvif.GetSnapshotUriAsync(
                camera.ServiceUrl, username, password, profile.Token, 5000);

            if (string.IsNullOrEmpty(snapshotUri))
                return (false, DirectionStatus.Unknown, 0);

            var frame = await _frameProcessor.CaptureAndDecodeAsync(
                snapshotUri, username, password, maxWidth: 640, ct: ct);

            if (!frame.IsValid) return (false, DirectionStatus.Unknown, 0);

            var result = _directionAnalyzer.Analyze(frame, cameraId);
            bool isObstructed = result.Status is DirectionStatus.WallObstructed
                or DirectionStatus.PartiallyObstructed;

            return (isObstructed, result.Status, result.Confidence);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "PTZ quick check failed for camera {CameraId}", cameraId);
            return (false, DirectionStatus.Unknown, 0);
        }
    }

    private bool IsConfidenceAccepted(double confidence)
        => confidence >= Math.Clamp(NormalConfidenceThreshold, 1, 100) / 100.0;

    private async Task SaveRecoveryPreset(
        Camera camera, string username, string password,
        string profileToken, string presetName)
    {
        try
        {
            var presetToken = await _onvif.SetPresetAsync(
                camera.ServiceUrl, username, password, profileToken, presetName);

            // Remove old recovery presets (keep only the latest)
            var oldPresets = camera.PtzPresets
                .Where(p => p.PresetName.StartsWith("auto_recovered_", StringComparison.Ordinal))
                .ToList();

            foreach (var old in oldPresets)
                camera.PtzPresets.Remove(old);

            camera.PtzPresets.Add(new PtzPreset
            {
                CameraId = camera.Id,
                ProfileToken = profileToken,
                PresetToken = presetToken,
                PresetName = presetName
            });

            await _cameraRepo.UpdateAsync(camera);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save recovery preset '{Name}'", presetName);
        }
    }
}
