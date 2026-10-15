using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text.Json;
using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.CircuitBreaker;
using CameraMaintainer.Infrastructure.Data.Repositories;
using CameraMaintainer.Infrastructure.Security;
using CameraMaintainer.Onvif;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Services;

public class HealthService : IHealthService
{
    private readonly ICameraRepository _cameraRepo;
    private readonly HealthRepository _healthRepo;
    private readonly AlertRepository _alertRepo;
    private readonly OnvifClient _onvif;
    private readonly IAutoRecoveryService _autoRecovery;
    private readonly IAlertService _alertService;
    private readonly ICredentialEncryptor _encryptor;
    private readonly CircuitBreaker _breaker;
    private readonly LensObstructionDetector _lensDetector;
    private readonly IFrameProcessor _frameProcessor;
    private readonly ICameraDirectionAnalyzer _directionAnalyzer;
    private readonly IFirmwareService _firmwareService;
    private readonly PtzAutoCorrectionService _ptzCorrection;
    private readonly HealthCheckConcurrencyGate _concurrencyGate;
    private readonly ILogger<HealthService> _logger;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ICameraFrameArchiveService? _frameArchive;

    public HealthService(
        ICameraRepository cameraRepo,
        HealthRepository healthRepo,
        AlertRepository alertRepo,
        OnvifClient onvif,
        IAutoRecoveryService autoRecovery,
        IAlertService alertService,
        ICredentialEncryptor encryptor,
        CircuitBreaker breaker,
        LensObstructionDetector lensDetector,
        IFrameProcessor frameProcessor,
        ICameraDirectionAnalyzer directionAnalyzer,
        IFirmwareService firmwareService,
        PtzAutoCorrectionService ptzCorrection,
        HealthCheckConcurrencyGate concurrencyGate,
        ILogger<HealthService> logger,
        IServiceScopeFactory? scopeFactory = null,
        ICameraFrameArchiveService? frameArchive = null)
    {
        _cameraRepo = cameraRepo;
        _healthRepo = healthRepo;
        _alertRepo = alertRepo;
        _onvif = onvif;
        _autoRecovery = autoRecovery;
        _alertService = alertService;
        _encryptor = encryptor;
        _breaker = breaker;
        _lensDetector = lensDetector;
        _frameProcessor = frameProcessor;
        _directionAnalyzer = directionAnalyzer;
        _firmwareService = firmwareService;
        _ptzCorrection = ptzCorrection;
        _concurrencyGate = concurrencyGate;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _frameArchive = frameArchive;
    }

    public Task<HealthRecord> CheckCameraAsync(
        int cameraId,
        CancellationToken cancellationToken = default)
    {
        return _concurrencyGate.RunCameraAsync(
            cameraId,
            () => CheckCameraCoreAsync(cameraId, cancellationToken),
            cancellationToken);
    }

    private async Task<HealthRecord> CheckCameraCoreAsync(
        int cameraId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var camera = await _cameraRepo.GetByIdAsync(cameraId);
        if (camera == null)
            throw new ArgumentException($"Camera {cameraId} not found");

        var checkId = Guid.NewGuid().ToString("N")[..12];
        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CheckId"] = checkId,
            ["CameraId"] = cameraId,
            ["CameraIp"] = camera.IpAddress ?? string.Empty
        });
        _logger.LogDebug("Camera health check started");

        if (camera.Credential == null)
        {
            camera.LastCheckAt = DateTime.UtcNow;
            await _cameraRepo.UpdateAsync(camera);

            var noCredentialsRecord = new HealthRecord
            {
                CameraId = cameraId,
                Reachable = false,
                StatusCode = "no_credentials",
                ErrorMessage = "No credentials configured",
                CheckedAt = DateTime.UtcNow
            };
            await _healthRepo.UpsertLatestAsync(noCredentialsRecord);
            await _alertService.CreateAlertAsync(
                cameraId,
                AlertType.AuthenticationFailed,
                AlertSeverity.Warning,
                $"Не настроен доступ к камере {camera.Name}",
                "Укажите логин и пароль ONVIF в настройках камеры.");
            return noCredentialsRecord;
        }

        // Check circuit breaker (with exponential backoff)
        if (!_breaker.AllowCall(cameraId))
        {
            _logger.LogDebug("Circuit breaker open for camera {CameraId}, skipping check", cameraId);
            camera.LastCheckAt = DateTime.UtcNow;
            camera.ConsecutiveHealthyChecks = 0;
            await _cameraRepo.UpdateAsync(camera);

            var currentRecord = await _healthRepo.GetLatestAsync(cameraId);
            if (currentRecord != null)
                return currentRecord;

            var circuitRecord = new HealthRecord
            {
                CameraId = cameraId,
                Reachable = false,
                StatusCode = "circuit_open",
                ErrorMessage = "Circuit breaker open — cooldown active",
                CheckedAt = DateTime.UtcNow
            };
            await _healthRepo.UpsertLatestAsync(circuitRecord);
            return circuitRecord;
        }

        var sw = Stopwatch.StartNew();
        string password;
        var onvifCallCount = 0;
        var onvifTotalMs = 0L;
        var failureStage = "network_probe";
        int? failureTimeoutMs = 3000;

        try
        {
            password = _encryptor.Decrypt(camera.Credential.PasswordEnc);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            sw.Stop();
            var record = new HealthRecord
            {
                CameraId = cameraId,
                Reachable = false,
                StatusCode = "credentials_invalid",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                ErrorMessage = "Stored camera credentials cannot be decrypted.",
                CheckedAt = DateTime.UtcNow
            };
            await _healthRepo.UpsertLatestAsync(record);
            await _alertService.CreateAlertAsync(
                cameraId,
                AlertType.AuthenticationFailed,
                AlertSeverity.Warning,
                $"Ошибка учётных данных камеры {camera.Name}",
                record.ErrorMessage);
            return record;
        }

        try
        {
            // ── 0. Multi-ping for telemetry ──
            var optionalChecksSucceeded = true;
            double? pingAvgMs = null;
            double? jitterMs = null;
            double? packetLoss = null;
            int pingSamples = 0;

            if (!string.IsNullOrEmpty(camera.IpAddress))
            {
                var pingResults = new List<long>();
                using var ping = new Ping();
                for (int i = 0; i < 3; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var reply = await ping.SendPingAsync(camera.IpAddress, 1000);
                        if (reply.Status == IPStatus.Success)
                            pingResults.Add(reply.RoundtripTime);
                    }
                    catch (Exception error) when (error is PingException or InvalidOperationException or ArgumentException)
                    {
                        _logger.LogDebug(error, "Ping telemetry failed for camera {CameraId}", cameraId);
                    }
                }

                pingSamples = pingResults.Count;
                if (pingResults.Count > 0)
                {
                    pingAvgMs = pingResults.Average();
                    if (pingResults.Count > 1)
                    {
                        var avg = pingAvgMs.Value;
                        jitterMs = Math.Sqrt(pingResults.Select(r => Math.Pow(r - avg, 2)).Average());
                    }
                    packetLoss = (3 - pingResults.Count) / 3.0 * 100.0;
                }
                else
                {
                    packetLoss = 100.0;
                }
            }

            // ── 1. Get device info ──
            failureStage = "onvif_device_info";
            failureTimeoutMs = 10000;
            var callSw = Stopwatch.StartNew();
            var deviceInfo = await _onvif.GetDeviceInfoAsync(
                camera.ServiceUrl, camera.Credential.Username, password);
            onvifCallCount++;
            onvifTotalMs += callSw.ElapsedMilliseconds;
            failureStage = "processing_camera_data";
            failureTimeoutMs = null;

            // ── 2. Time drift check ──
            double? timeDriftSec = null;
            DateTime? cameraUtcTime = null;
            try
            {
                callSw.Restart();
                var camTime = await _onvif.GetSystemDateTimeAsync(
                    camera.ServiceUrl, camera.Credential.Username, password, 5000);
                onvifCallCount++;
                onvifTotalMs += callSw.ElapsedMilliseconds;
                cameraUtcTime = camTime.UtcDateTime;
                timeDriftSec = Math.Abs((camTime.UtcDateTime - DateTime.UtcNow).TotalSeconds);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                optionalChecksSucceeded = false;
                _logger.LogDebug(ex, "Optional ONVIF time stage failed after {ElapsedMs}ms", callSw.ElapsedMilliseconds);
            }

            // ── 3. Snapshot + lens obstruction + direction analysis ──
            string? snapshotUrl = null;
            LensObstructionDetector.ObstructionResult? lensResult = null;
            try
            {
                callSw.Restart();
                var profiles = await _onvif.GetProfilesAsync(
                    camera.ServiceUrl, camera.Credential.Username, password, 5000);
                onvifCallCount++;
                onvifTotalMs += callSw.ElapsedMilliseconds;

                var mainProfile = profiles.FirstOrDefault();
                if (mainProfile != null)
                {
                    callSw.Restart();
                    snapshotUrl = await _onvif.GetSnapshotUriAsync(
                        camera.ServiceUrl, camera.Credential.Username, password,
                        mainProfile.Token, 5000);
                    onvifCallCount++;
                    onvifTotalMs += callSw.ElapsedMilliseconds;
                }

                if (!string.IsNullOrEmpty(snapshotUrl))
                {
                    lensResult = await _lensDetector.AnalyzeAsync(
                        snapshotUrl, cameraId: camera.Id,
                        username: camera.Credential.Username,
                        password: password,
                        ct: cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                optionalChecksSucceeded = false;
                _logger.LogDebug(ex, "Optional snapshot stage failed after {ElapsedMs}ms", callSw.ElapsedMilliseconds);
            }

            sw.Stop();

            if (_frameArchive != null && lensResult is
                {
                    ErrorMessage: null,
                    EncodedImage.Length: > 0
                })
            {
                try
                {
                    await _frameArchive.RecordAsync(new CameraFrameCapture
                    {
                        CameraId = cameraId,
                        CapturedAt = DateTime.UtcNow,
                        IsObstructed = lensResult.IsObstructed,
                        ObstructionType = lensResult.ObstructionType,
                        DirectionStatus = lensResult.DirectionStatus.ToString(),
                        MeanBrightness = lensResult.MeanBrightness,
                        BrightnessStdDev = Math.Sqrt(Math.Max(0, lensResult.Variance)),
                        Entropy = lensResult.Entropy,
                        EdgeDensity = lensResult.EdgeDensity,
                        TextureUniformityScore = lensResult.TextureUniformityScore,
                        DirectionConfidence = lensResult.DirectionConfidence,
                        EncodedImage = lensResult.EncodedImage,
                        ImageContentType = lensResult.ImageContentType
                    }, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not store frame history for camera {CameraId}", cameraId);
                }
                finally
                {
                    lensResult = lensResult with { EncodedImage = [] };
                }
            }

            // ── Update camera metadata ──
            camera.Manufacturer = deviceInfo.Manufacturer;
            camera.Model = deviceInfo.Model;
            camera.SerialNumber = deviceInfo.SerialNumber;
            camera.FirmwareVersion = deviceInfo.FirmwareVersion;
            // Standard ONVIF has no portable device-temperature operation.
            camera.TemperatureC = null;
            camera.UpdatedAt = DateTime.UtcNow;

            // Record firmware version change if needed
            if (!string.IsNullOrEmpty(deviceInfo.FirmwareVersion) &&
                deviceInfo.FirmwareVersion != "Unknown")
            {
                failureStage = "firmware_history";
                await _firmwareService.RecordVersionAsync(
                    camera.Id, deviceInfo.FirmwareVersion, deviceInfo.HardwareId,
                    deviceInfo.Manufacturer, deviceInfo.Model);
            }

            // Store direction analysis results
            if (lensResult != null)
            {
                camera.DirectionStatus = lensResult.DirectionStatus.ToString();
                camera.DirectionLastCheckedAt = DateTime.UtcNow;
                camera.LastEdgeDensity = lensResult.EdgeDensity;
                camera.LastTextureScore = lensResult.TextureUniformityScore;
            }

            failureStage = "camera_metadata_save";
            await _cameraRepo.UpdateAsync(camera);

            // Update camera status
            failureStage = "camera_status_save";
            await _cameraRepo.UpdateStatusAsync(cameraId, CameraStatus.Online, DateTime.UtcNow);
            _breaker.RecordSuccess(cameraId);
            failureStage = "alert_evaluation";

            // ── Alerts ──
            if (timeDriftSec > 30)
            {
                await _alertService.CreateAlertAsync(cameraId, AlertType.ConfigurationChanged,
                    AlertSeverity.Warning,
                    $"Camera time drift: {timeDriftSec:F0}s",
                    $"Camera: {cameraUtcTime:O}, Server: {DateTime.UtcNow:O}");
            }

            // Direction/wall obstruction alert
            if (lensResult?.IsObstructed == true)
            {
                var severity = lensResult.DirectionStatus == DirectionStatus.WallObstructed
                    ? AlertSeverity.Warning
                    : lensResult.DirectionStatus == DirectionStatus.Overexposed
                        ? AlertSeverity.Critical
                        : AlertSeverity.Warning;

                await _alertService.CreateAlertAsync(cameraId, AlertType.LensObstructed,
                    severity,
                    $"Lens issue: {lensResult.ObstructionType} ({lensResult.DirectionStatus})",
                    $"Direction: {lensResult.DirectionDescription}, " +
                    $"Mean={lensResult.MeanBrightness:F1}, EdgeDensity={lensResult.EdgeDensity:F1}%, " +
                    $"Confidence={lensResult.DirectionConfidence:F2}");

                // Trigger PTZ auto-correction for wall obstruction (autonomous recovery)
                if (lensResult.DirectionStatus == DirectionStatus.WallObstructed &&
                    lensResult.DirectionConfidence > 0.80 &&
                    camera.PtzSupported &&
                    camera.AutoRecovery)
                {
                    _logger?.LogInformation(
                        "Triggering PTZ auto-correction for camera {CameraId} — wall obstruction detected with {Conf:F0}% confidence",
                        cameraId, lensResult.DirectionConfidence * 100);

                    try
                    {
                        var corrResult = await _ptzCorrection.TryCorrectAsync(
                            cameraId,
                            cancellationToken: cancellationToken);
                        _logger?.LogInformation(
                            "PTZ auto-correction result for camera {CameraId}: {Success}, {Description}",
                            cameraId, corrResult.Success, corrResult.Description);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "PTZ auto-correction failed for camera {CameraId}", cameraId);
                    }
                }
            }
            // Add all detected faults before marking known faults as recovered.
            if (timeDriftSec is <= 30)
                await _alertService.MarkRecoveredAsync(cameraId, AlertType.ConfigurationChanged);
            if (lensResult?.ConfirmsRecovery == true)
            {
                await _alertService.MarkRecoveredAsync(
                    cameraId,
                    AlertType.LensObstructed);
            }
            await _alertRepo.MarkConnectivityRecoveredAsync(cameraId);

            // ── Telemetry detail ──
            double? bandwidthEstimate = null;
            if (lensResult?.SnapshotSizeBytes > 0 && lensResult?.DownloadTimeMs > 0)
            {
                bandwidthEstimate = (double)lensResult.SnapshotSizeBytes / lensResult.DownloadTimeMs;
            }

            var detailData = new
            {
                PingAvgMs = pingAvgMs,
                JitterMs = jitterMs,
                PacketLossPercent = packetLoss,
                PingSamples = pingSamples,
                deviceInfo.Manufacturer,
                deviceInfo.Model,
                deviceInfo.FirmwareVersion,
                deviceInfo.SerialNumber,
                CameraTime = cameraUtcTime?.ToString("O"),
                TimeDriftSeconds = timeDriftSec,
                SnapshotAvailable = !string.IsNullOrEmpty(snapshotUrl),
                LensObstructed = lensResult?.IsObstructed ?? false,
                ObstructionType = lensResult?.ObstructionType ?? "unknown",
                DirectionStatus = lensResult?.DirectionStatus.ToString() ?? "Unknown",
                DirectionConfidence = lensResult?.DirectionConfidence ?? 0,
                EdgeDensity = lensResult?.EdgeDensity ?? 0,
                TextureUniformity = lensResult?.TextureUniformityScore ?? 0,
                MeanBrightness = lensResult?.MeanBrightness ?? 0,
                ImageEntropy = lensResult?.Entropy ?? 0,
                SnapshotDownloadMs = lensResult?.DownloadTimeMs ?? 0,
                SnapshotDecodeMs = lensResult?.DecodeTimeMs ?? 0,
                SnapshotSizeBytes = lensResult?.SnapshotSizeBytes ?? 0,
                BandwidthEstimateBPerMs = bandwidthEstimate,
                OnvifCallCount = onvifCallCount,
                OnvifTotalResponseMs = onvifTotalMs
            };

            var record = new HealthRecord
            {
                CameraId = cameraId,
                Reachable = true,
                StatusCode = "ok",
                ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                TemperatureC = null,
                JitterMs = jitterMs,
                PacketLossPercent = packetLoss,
                SnapshotSizeBytes = lensResult?.SnapshotSizeBytes ?? 0,
                DirectionStatus = lensResult?.DirectionStatus.ToString() ?? "Unknown",
                CheckedAt = DateTime.UtcNow,
                DetailJson = JsonSerializer.Serialize(detailData)
            };

            failureStage = "health_result_save";
            await _healthRepo.UpsertLatestAsync(record, passedAllChecks: optionalChecksSucceeded &&
                timeDriftSec is <= 30 && lensResult?.ConfirmsRecovery == true && packetLoss == 0);
            _logger!.LogDebug(
                "Camera health check completed: status={StatusCode}, duration={DurationMs}ms, onvifCalls={OnvifCallCount}",
                record.StatusCode,
                record.ResponseTimeMs,
                onvifCallCount);
            return record;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _breaker.RecordFailure(cameraId);

            var statusCode = ClassifyError(ex);

            camera.ConsecutiveFailures++;
            camera.LastCheckAt = DateTime.UtcNow;
            if (camera.ConsecutiveFailures >= 6)
                camera.Status = CameraStatus.Offline;
            else if (camera.ConsecutiveFailures >= 3)
                camera.Status = CameraStatus.Suspect;

            await _cameraRepo.UpdateAsync(camera);

            // Auto-recovery
            if (camera.AutoRecovery && _breaker.GetFailureCount(cameraId) >= 3)
            {
                _logger.LogWarning("Attempting auto-recovery for camera {CameraId}", cameraId);
                var recovered = await _autoRecovery.TryAutoRecoverAsync(
                    cameraId,
                    statusCode,
                    cancellationToken);
                if (recovered)
                {
                    _breaker.Reset(cameraId);
                    await _cameraRepo.UpdateStatusAsync(cameraId, CameraStatus.Online, DateTime.UtcNow);
                    // Wait for a successful scheduled check before recording recovery.
                }
            }

            var record = new HealthRecord
            {
                CameraId = cameraId,
                Reachable = false,
                StatusCode = statusCode,
                ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                ErrorMessage = ex.Message,
                DetailJson = JsonSerializer.Serialize(new
                {
                    FailureCode = statusCode,
                    FailureStage = failureStage,
                    DurationMs = (int)sw.ElapsedMilliseconds,
                    TimeoutMs = statusCode == "timeout" ? failureTimeoutMs : null
                }),
                CheckedAt = DateTime.UtcNow
            };

            await _healthRepo.UpsertLatestAsync(record);

            var alertType = statusCode == "auth_failed"
                ? AlertType.AuthenticationFailed
                : AlertType.CameraOffline;
            var severity = camera.Status == CameraStatus.Offline
                ? AlertSeverity.Critical
                : AlertSeverity.Warning;
            await _alertService.CreateAlertAsync(
                cameraId,
                alertType,
                severity,
                $"Ошибка проверки камеры {camera.Name}",
                $"{statusCode}: {ex.Message}",
                new AlertDiagnostic
                {
                    FailureCode = statusCode,
                    FailureStage = failureStage,
                    DurationMs = record.ResponseTimeMs,
                    TimeoutMs = statusCode == "timeout" ? failureTimeoutMs : null
                });

            _logger.LogWarning(
                ex,
                "Camera health check failed: status={StatusCode}, duration={DurationMs}ms",
                statusCode,
                record.ResponseTimeMs);
            return record;
        }
    }

    public Task CheckAllDueAsync(CancellationToken cancellationToken = default)
        => _concurrencyGate.RunCycleAsync(() => CheckQueueCycleAsync(cancellationToken), cancellationToken);

    private async Task CheckQueueCycleAsync(CancellationToken cancellationToken)
    {
        var cycle = await _cameraRepo.GetCheckCycleAsync(DateTime.UtcNow);
        var dueCameraIds = cycle.CameraIds;

        _logger.LogInformation(
            "Health queue cycle {Cycle}: {Count} cameras due, configured order, continuous workers={Concurrency}",
            cycle.Number,
            dueCameraIds.Count,
            _concurrencyGate.MaxConcurrency);

        // IDs are claimed in strict queue order. Completion may be out of order,
        // but each result remains attached to its CameraId and stored position.
        await OrderedAsyncWorkerPool.RunAsync(
            dueCameraIds,
            _concurrencyGate.MaxConcurrency,
            async (cameraId, workerCancellationToken) =>
            {
                try
                {
                    if (_scopeFactory != null)
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var healthService = scope.ServiceProvider.GetRequiredService<IHealthService>();
                        await healthService.CheckCameraAsync(cameraId, workerCancellationToken);
                    }
                    else
                    {
                        await CheckCameraAsync(cameraId, workerCancellationToken);
                    }
                }
                catch (OperationCanceledException) when (workerCancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Health check failed for camera {CameraId}", cameraId);
                }
            },
            cancellationToken);

        // Previewing the queue and individual checks must not advance this counter.
        await _cameraRepo.CompleteCheckCycleAsync(cycle, cancellationToken);
        _logger.LogInformation("Health check cycle {Cycle} completed", cycle.Number);
    }

    public async Task<HealthRecord?> GetCurrentHealthAsync(int cameraId)
    {
        return await _healthRepo.GetLatestAsync(cameraId);
    }

    public async Task<HealthSummary> GetHealthSummaryAsync()
    {
        var counts = await _cameraRepo.GetStatusCountsAsync();
        var online = counts.GetValueOrDefault(CameraStatus.Online);
        var suspect = counts.GetValueOrDefault(CameraStatus.Suspect);
        var offline = counts.GetValueOrDefault(CameraStatus.Offline);
        var unknown = counts.GetValueOrDefault(CameraStatus.Unknown);
        var avgTemp = await _healthRepo.GetAverageTemperatureAsync();

        return new HealthSummary
        {
            Total = online + suspect + offline + unknown,
            Online = online,
            Suspect = suspect,
            Offline = offline,
            Unknown = unknown,
            AverageTemperature = avgTemp
        };
    }

    private static string ClassifyError(Exception ex)
    {
        return ex switch
        {
            TaskCanceledException => "timeout",
            TimeoutException => "timeout",
            OnvifSoapException soapEx when soapEx.IsAuthenticationFailure
                => "auth_failed",
            System.Net.Http.HttpRequestException httpEx
                when httpEx.StatusCode == System.Net.HttpStatusCode.Unauthorized
                => "auth_failed",
            System.Net.Http.HttpRequestException httpEx
                when httpEx.StatusCode == System.Net.HttpStatusCode.NotFound
                => "not_found",
            System.Net.Http.HttpRequestException httpEx when httpEx.StatusCode.HasValue
                => "http_error",
            System.Net.Http.HttpRequestException => "connection_refused",
            InvalidOperationException => "soap_fault",
            _ => "unknown_error"
        };
    }
}
