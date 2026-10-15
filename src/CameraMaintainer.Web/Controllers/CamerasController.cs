using System.Buffers;
using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Security;
using CameraMaintainer.Onvif;
using CameraMaintainer.Services;
using CameraMaintainer.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CameraMaintainer.Web.Services;
using CameraMaintainer.Web.Models;
using System.Net;

namespace CameraMaintainer.Web.Controllers;

[ApiController]
[Route("api/cameras")]
public class CamerasController : ControllerBase
{
    private const int MaximumSnapshotBytes = 12 * 1024 * 1024;

    private readonly SnapshotCache _snapshots;

    private readonly ICameraRepository _cameraRepo;
    private readonly ICredentialEncryptor _encryptor;
    private readonly IOnvifCapabilityCache _capabilityCache;
    private readonly OnvifClient _onvif;
    private readonly IFrameProcessor _frameProcessor;
    private readonly ICameraDirectionAnalyzer _directionAnalyzer;
    private readonly PtzAutoCorrectionService _ptzCorrection;
    private readonly ILogger<CamerasController> _logger;
    private readonly ICameraFrameArchiveService? _frameArchive;

    public CamerasController(
        ICameraRepository cameraRepo,
        ICredentialEncryptor encryptor,
        IOnvifCapabilityCache capabilityCache,
        OnvifClient onvif,
        IFrameProcessor frameProcessor,
        ICameraDirectionAnalyzer directionAnalyzer,
        PtzAutoCorrectionService ptzCorrection,
        ILogger<CamerasController> logger,
        SnapshotCache snapshots,
        ICameraFrameArchiveService? frameArchive = null)
    {
        _cameraRepo = cameraRepo;
        _encryptor = encryptor;
        _capabilityCache = capabilityCache;
        _onvif = onvif;
        _frameProcessor = frameProcessor;
        _directionAnalyzer = directionAnalyzer;
        _ptzCorrection = ptzCorrection;
        _logger = logger;
        _snapshots = snapshots;
        _frameArchive = frameArchive;
    }

    [Authorize(Policy = AccessPolicies.ViewCameras)]
    [HttpGet]
    public async Task<ActionResult<List<Camera>>> GetAll(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] bool? automaticChecksEnabled,
        [FromQuery] int page = 1,
        [FromQuery] int perPage = 50,
        [FromQuery] string? sortBy = "id",
        [FromQuery] bool sortDesc = false)
    {
        var filter = new CameraFilter
        {
            Status = status,
            Search = search,
            AutomaticChecksEnabled = automaticChecksEnabled,
            Page = page,
            PerPage = perPage,
            SortBy = sortBy,
            SortDesc = sortDesc
        };

        var cameras = await _cameraRepo.GetAllAsync(filter);

        // Strip credentials from response
        foreach (var camera in cameras)
            camera.Credential = null;

        return Ok(cameras);
    }

    [Authorize(Policy = AccessPolicies.ViewCameras)]
    [HttpGet("lookup")]
    public async Task<ActionResult<List<Camera>>> GetVisible([FromQuery] int[] ids)
    {
        if (ids.Length > 200 || ids.Any(id => id <= 0))
            return BadRequest(new { message = "Укажите не более 200 корректных ID камер." });
        return Ok(await _cameraRepo.GetByIdsAsync(ids.Distinct().ToArray()));
    }

    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpPost]
    public async Task<ActionResult<Camera>> Create([FromBody] CreateCameraDto dto)
    {
        var serviceUrlError = ValidateServiceUrl(dto.ServiceUrl);
        if (serviceUrlError != null)
            return BadRequest(new { message = serviceUrlError });
        if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.IpAddress))
            return BadRequest(new { message = "Название и IP-адрес обязательны" });
        if (dto.Port < 1 || dto.Port > 65535)
            return BadRequest(new { message = "Порт должен быть в диапазоне 1-65535" });
        if (dto.CheckIntervalSeconds is < 10 or > 86400)
            return BadRequest(new { message = "Интервал проверки должен быть от 10 до 86400 секунд" });
        if (string.IsNullOrWhiteSpace(dto.Username) != string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest(new { message = "Для доступа к камере укажите имя пользователя и пароль вместе" });

        // Check for duplicate IP
        if (!string.IsNullOrEmpty(dto.IpAddress))
        {
            var existing = await _cameraRepo.GetByIpAsync(dto.IpAddress.Trim());
            if (existing != null)
                return Conflict(new { message = $"Камера с IP-адресом {dto.IpAddress} уже существует (№ {existing.Id})" });
        }

        var camera = new Camera
        {
            Name = dto.Name.Trim(),
            IpAddress = dto.IpAddress.Trim(),
            Port = dto.Port,
            Latitude = dto.Location?.Latitude,
            Longitude = dto.Location?.Longitude,
            StreetAddress = dto.Location?.StreetAddress?.Trim(),
            ServiceUrl = !string.IsNullOrEmpty(dto.ServiceUrl)
                ? dto.ServiceUrl.Trim()
                : $"http://{dto.IpAddress.Trim()}:{dto.Port}/onvif/device_service",
            Manufacturer = dto.Manufacturer,
            Model = dto.Model,
            Notes = dto.Notes?.Trim() ?? string.Empty,
            Tags = dto.Tags?.Trim() ?? "[]",
            AutoBackup = dto.AutoBackup,
            AutoRecovery = dto.AutoRecovery,
            CheckIntervalSeconds = dto.CheckIntervalSeconds,
            Status = CameraMaintainer.Core.Enums.CameraStatus.Unknown,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Attach credential BEFORE AddAsync so EF Core inserts both in one transaction
        if (!string.IsNullOrEmpty(dto.Username) && !string.IsNullOrEmpty(dto.Password))
        {
            camera.Credential = new Credential
            {
                Username = dto.Username,
                PasswordEnc = _encryptor.Encrypt(dto.Password),
                AuthType = dto.AuthType ?? "wsse",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Camera = camera
            };
        }

        await _cameraRepo.AddAsync(camera);

        _logger.LogInformation("Camera added: {Name} at {Ip} (ID: {Id}, hasCreds: {HasCreds})",
            camera.Name, camera.IpAddress, camera.Id, camera.Credential != null);
        return Ok(camera);
    }

    [Authorize(Policy = AccessPolicies.ViewCameras)]
    [HttpGet("{id}")]
    public async Task<ActionResult<Camera>> GetById(int id)
    {
        var camera = await _cameraRepo.GetByIdAsync(id);
        if (camera == null) return NotFound();

        camera.Credential = null; // Never return credentials
        return Ok(camera);
    }

    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpGet("{id}/settings")]
    public async Task<ActionResult<CameraSettingsDto>> GetSettings(int id)
    {
        var camera = await _cameraRepo.GetByIdAsync(id);
        if (camera == null) return NotFound();

        return Ok(new CameraSettingsDto
        {
            Id = camera.Id,
            Name = camera.Name,
            IpAddress = camera.IpAddress,
            Port = camera.Port,
            Location = new CameraLocationInput { Latitude = camera.Latitude, Longitude = camera.Longitude, StreetAddress = camera.StreetAddress },
            ServiceUrl = camera.ServiceUrl,
            Notes = camera.Notes,
            Tags = camera.Tags,
            AutoBackup = camera.AutoBackup,
            AutoRecovery = camera.AutoRecovery,
            CheckIntervalSeconds = camera.CheckIntervalSeconds,
            Username = camera.Credential?.Username,
            AuthType = camera.Credential?.AuthType ?? "wsse",
            HasCredentials = camera.Credential != null
        });
    }

    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpPut("{id}")]
    public async Task<ActionResult<Camera>> Update(int id, [FromBody] CameraUpdateDto dto)
    {
        var serviceUrlError = ValidateServiceUrl(dto.ServiceUrl);
        if (serviceUrlError != null)
            return BadRequest(new { message = serviceUrlError });
        var camera = await _cameraRepo.GetByIdAsync(id);
        if (camera == null) return NotFound();

        var requestedIp = dto.IpAddress?.Trim();
        if (!string.IsNullOrWhiteSpace(requestedIp) && requestedIp != camera.IpAddress)
        {
            var existing = await _cameraRepo.GetByIpAsync(requestedIp);
            if (existing != null && existing.Id != id)
                return Conflict(new { message = $"Камера с IP-адресом {requestedIp} уже существует (№ {existing.Id})" });
        }

        if (dto.Port.HasValue && (dto.Port.Value < 1 || dto.Port.Value > 65535))
            return BadRequest(new { message = "Порт должен быть в диапазоне 1-65535" });
        if (dto.CheckIntervalSeconds is < 10 or > 86400)
            return BadRequest(new { message = "Интервал проверки должен быть от 10 до 86400 секунд" });

        var connectionChanged = false;
        if (!string.IsNullOrWhiteSpace(dto.Name)) camera.Name = dto.Name.Trim();
        if (!string.IsNullOrWhiteSpace(requestedIp) && requestedIp != camera.IpAddress)
        {
            camera.IpAddress = requestedIp;
            connectionChanged = true;
        }
        if (dto.Port.HasValue && dto.Port.Value != camera.Port)
        {
            camera.Port = dto.Port.Value;
            connectionChanged = true;
        }
        if (!string.IsNullOrWhiteSpace(dto.ServiceUrl))
        {
            var previousServiceUrl = camera.ServiceUrl;
            camera.ServiceUrl = dto.ServiceUrl.Trim();
            if (!string.Equals(previousServiceUrl, camera.ServiceUrl, StringComparison.OrdinalIgnoreCase))
            {
                connectionChanged = true;
                _capabilityCache.Invalidate(previousServiceUrl);
            }
        }
        else if (connectionChanged)
            camera.ServiceUrl = $"http://{camera.IpAddress}:{camera.Port}/onvif/device_service";
        if (dto.Notes != null) camera.Notes = dto.Notes.Trim();
        if (dto.Tags != null) camera.Tags = dto.Tags.Trim();
        if (dto.AutoBackup.HasValue) camera.AutoBackup = dto.AutoBackup.Value;
        if (dto.AutoRecovery.HasValue) camera.AutoRecovery = dto.AutoRecovery.Value;
        if (dto.CheckIntervalSeconds.HasValue) camera.CheckIntervalSeconds = dto.CheckIntervalSeconds.Value;
        if (dto.Location != null)
        {
            camera.Latitude = dto.Location.Latitude;
            camera.Longitude = dto.Location.Longitude;
            camera.StreetAddress = dto.Location.StreetAddress?.Trim();
        }

        await _cameraRepo.UpdateAsync(camera);
        if (connectionChanged) _snapshots.Remove(id);
        camera.Credential = null;
        return Ok(camera);
    }

    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpPost("location/resolve")]
    public async Task<IActionResult> ResolveLocation([FromBody] CameraLocationInput location,
        [FromServices] CameraGeocodingService geocoder, CancellationToken cancellationToken)
    {
        if (!location.Latitude.HasValue || !location.Longitude.HasValue)
            return BadRequest(new { message = "Укажите широту и долготу." });
        try
        {
            var address = await geocoder.ResolveAsync(location.Latitude.Value, location.Longitude.Value, cancellationToken);
            return address == null ? NotFound(new { message = "Улица для этих координат не найдена. Адрес можно указать вручную." }) : Ok(new { streetAddress = address });
        }
        catch (GeocodingUnavailableException error) { return StatusCode(error.StatusCode, new { message = error.Message }); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return StatusCode(504, new { message = "Сервис адресов не ответил вовремя. Координаты можно сохранить без адреса." }); }
        catch (HttpRequestException) { return StatusCode(502, new { message = "Сервис адресов недоступен. Попробуйте позже или укажите улицу вручную." }); }
    }

    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var camera = await _cameraRepo.GetByIdAsync(id);
        if (camera == null) return NotFound();

        _capabilityCache.Invalidate(camera.ServiceUrl);
        if (_frameArchive != null)
            await _frameArchive.DeleteForCameraAsync(id, HttpContext.RequestAborted);
        await _cameraRepo.DeleteAsync(id);
        _snapshots.Remove(id);
        return NoContent();
    }

    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpPatch("{id}/credentials")]
    public async Task<ActionResult> SetCredentials(int id, [FromBody] CredentialDto dto)
    {
        var camera = await _cameraRepo.GetByIdAsync(id);
        if (camera == null) return NotFound();

        if (string.IsNullOrWhiteSpace(dto.Username))
            return BadRequest(new { message = "Имя пользователя камеры не может быть пустым" });
        if (camera.Credential == null && string.IsNullOrEmpty(dto.Password))
            return BadRequest(new { message = "Для новых учётных данных требуется пароль" });

        if (camera.Credential == null)
        {
            camera.Credential = new Credential
            {
                CameraId = id,
                Username = dto.Username.Trim(),
                PasswordEnc = _encryptor.Encrypt(dto.Password!),
                AuthType = dto.AuthType ?? "wsse"
            };
        }
        else
        {
            camera.Credential.Username = dto.Username.Trim();
            if (!string.IsNullOrEmpty(dto.Password))
                camera.Credential.PasswordEnc = _encryptor.Encrypt(dto.Password);
            camera.Credential.AuthType = dto.AuthType ?? "wsse";
            camera.Credential.UpdatedAt = DateTime.UtcNow;
        }

        await _cameraRepo.UpdateAsync(camera);
        _snapshots.Remove(id);
        return Ok(new { message = "Учётные данные сохранены" });
    }

    [Authorize(Policy = AccessPolicies.ViewCameras)]
    [HttpGet("{id}/snapshot")]
    public async Task<IActionResult> GetSnapshot(int id, CancellationToken cancellationToken)
    {
        var camera = await _cameraRepo.GetByIdAsync(id);
        if (camera == null) return NotFound();
        if (camera.Credential == null)
            return BadRequest(new { message = "Учётные данные камеры не настроены" });

        var password = _encryptor.Decrypt(camera.Credential.PasswordEnc);

        try
        {
            var frame = await _snapshots.GetOrCreateAsync(FrameKey(camera),
                token => GetOnvifSnapshotFrameAsync(camera, password, token), cancellationToken);
            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            return File(frame.Bytes, frame.ContentType);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get snapshot for camera {CameraId}", id);
            var cached = GetCachedFrame(camera);
            if (cached != null)
                return cached;

            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = "Не удалось получить снимок с камеры",
                code = "snapshot_failed"
            });
        }
    }

    [Authorize(Policy = AccessPolicies.ViewCameras)]
    [HttpGet("{id}/live-frame")]
    public async Task<IActionResult> GetLiveFrame(int id, CancellationToken cancellationToken, [FromQuery] bool preferCached = false)
    {
        var camera = await _cameraRepo.GetByIdAsync(id);
        if (camera == null) return NotFound();
        if (camera.Credential == null)
            return BadRequest(new { message = "Учётные данные камеры не настроены" });

        if (preferCached && GetCachedFrame(camera) is { } lastFrame) return lastFrame;
        var password = _encryptor.Decrypt(camera.Credential.PasswordEnc);

        try
        {
            var frame = await _snapshots.GetOrCreateAsync(FrameKey(camera),
                token => GetOnvifSnapshotFrameAsync(camera, password, token), cancellationToken);
            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            return File(frame.Bytes, frame.ContentType);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get latest ONVIF frame for camera {CameraId}", id);
            var cached = GetCachedFrame(camera);
            if (cached != null)
                return cached;

            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = "Не удалось получить последний кадр ONVIF с камеры",
                code = "live_frame_failed"
            });
        }
    }

    private async Task<(byte[] Bytes, string ContentType)> GetOnvifSnapshotFrameAsync(
        Camera camera,
        string password,
        CancellationToken cancellationToken)
    {
        var snapshotUri = await ResolveSnapshotUriAsync(camera, password, cancellationToken);
        using var http = CreateSnapshotHttpClient(camera, password);

        try
        {
            return await DownloadSnapshotFrameAsync(http, snapshotUri, cancellationToken);
        }
        catch
        {
            _snapshots.RemoveUri(camera.Id);
            throw;
        }
    }

    private async Task<string> ResolveSnapshotUriAsync(
        Camera camera,
        string password,
        CancellationToken cancellationToken)
    {
        if (camera.Credential == null)
            throw new InvalidOperationException("Camera credentials are not configured");

        string snapshotUri;
        var cached = _snapshots.GetUri(FrameKey(camera));
        if (cached != null)
        {
            snapshotUri = cached;
        }
        else
        {
            var profiles = await _onvif.GetProfilesAsync(
                camera.ServiceUrl,
                camera.Credential.Username,
                password,
                timeoutMs: 5000);

            var profile = profiles.FirstOrDefault()
                ?? throw new InvalidOperationException("ONVIF media profile was not found");

            snapshotUri = await _onvif.GetSnapshotUriAsync(
                camera.ServiceUrl,
                camera.Credential.Username,
                password,
                profile.Token,
                timeoutMs: 5000);

            if (!string.IsNullOrWhiteSpace(snapshotUri))
            {
                _snapshots.SetUri(FrameKey(camera), snapshotUri);
            }
        }

        if (string.IsNullOrWhiteSpace(snapshotUri))
            throw new InvalidOperationException("ONVIF snapshot URI is not available for this camera");

        cancellationToken.ThrowIfCancellationRequested();
        return snapshotUri;
    }

    private static HttpClient CreateSnapshotHttpClient(Camera camera, string password)
    {
        if (camera.Credential == null)
            throw new InvalidOperationException("Camera credentials are not configured");

        var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(camera.Credential.Username, password),
            PreAuthenticate = true,
            CheckCertificateRevocationList = true
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    private static async Task<(byte[] Bytes, string ContentType)> DownloadSnapshotFrameAsync(
        HttpClient http,
        string snapshotUri,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(
            AddCacheBuster(snapshotUri),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaximumSnapshotBytes)
            throw new InvalidDataException("Snapshot exceeds the 12 MB safety limit.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream(
            response.Content.Headers.ContentLength is > 0
                ? (int)Math.Min(response.Content.Headers.ContentLength.Value, MaximumSnapshotBytes)
                : 64 * 1024);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0)
                    break;
                if (destination.Length + read > MaximumSnapshotBytes)
                    throw new InvalidDataException("Snapshot exceeds the 12 MB safety limit.");

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            var bytes = destination.ToArray();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            return (bytes, contentType);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string AddCacheBuster(string uri)
    {
        var separator = uri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{uri}{separator}_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    }

    private static string? ValidateServiceUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return "Адрес ONVIF должен быть абсолютным HTTP/HTTPS URL без логина и пароля.";
        }

        return null;
    }

    private static SnapshotKey FrameKey(Camera camera)
        => new(camera.Id, camera.ServiceUrl, camera.Credential!.Username, camera.Credential.UpdatedAt);

    private FileContentResult? GetCachedFrame(Camera camera)
    {
        var frame = _snapshots.Get(FrameKey(camera));
        if (frame == null)
            return null;

        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers["X-Frame-Stale"] = "true";
        Response.Headers["X-Frame-Captured-At"] = frame.CapturedAt.ToString("O");
        return File(frame.Bytes, frame.ContentType);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Direction / Obstruction Endpoints
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Get the last known direction status for a camera.
    /// </summary>
    [Authorize(Policy = AccessPolicies.ViewCameras)]
    [HttpGet("{id}/direction")]
    public async Task<ActionResult> GetDirectionStatus(int id)
    {
        var camera = await _cameraRepo.GetByIdAsync(id);
        if (camera == null) return NotFound();

        return Ok(new
        {
            cameraId = camera.Id,
            cameraName = camera.Name,
            directionStatus = camera.DirectionStatus,
            lastCheckedAt = camera.DirectionLastCheckedAt,
            edgeDensity = camera.LastEdgeDensity,
            textureScore = camera.LastTextureScore
        });
    }

    /// <summary>
    /// Force a new direction analysis by capturing a snapshot and analyzing it.
    /// Returns the full direction analysis result.
    /// </summary>
    [Authorize(Policy = AccessPolicies.CheckCameras)]
    [HttpPost("{id}/direction/analyze")]
    public async Task<ActionResult> AnalyzeDirection(int id, CancellationToken cancellationToken)
    {
        var camera = await _cameraRepo.GetByIdAsync(id);
        if (camera == null) return NotFound();
        if (camera.Credential == null)
            return BadRequest(new { message = "Учётные данные камеры не настроены" });

        var password = _encryptor.Decrypt(camera.Credential.PasswordEnc);

        try
        {
            // Get snapshot URI
            var profiles = await _onvif.GetProfilesAsync(
                camera.ServiceUrl, camera.Credential.Username, password, 5000);
            var profile = profiles.FirstOrDefault();
            if (profile == null)
                return BadRequest(new { message = "Профиль видеопотока недоступен" });

            var snapshotUri = await _onvif.GetSnapshotUriAsync(
                camera.ServiceUrl, camera.Credential.Username, password, profile.Token, 5000);

            if (string.IsNullOrEmpty(snapshotUri))
                return BadRequest(new { message = "Адрес получения снимка недоступен" });

            // Capture and analyze
            var frame = await _frameProcessor.CaptureAndDecodeAsync(
                snapshotUri, camera.Credential.Username, password,
                maxWidth: 640, ct: cancellationToken);

            if (!frame.IsValid)
                return StatusCode(502, new { message = "Не удалось получить кадр", code = "frame_invalid" });

            var stats = _frameProcessor.ComputeStatistics(frame.Pixels, frame.Width, frame.Height);
            var direction = _directionAnalyzer.Analyze(frame, camera.Id, stats);

            // Update camera record
            camera.DirectionStatus = direction.Status.ToString();
            camera.DirectionLastCheckedAt = DateTime.UtcNow;
            camera.LastEdgeDensity = direction.EdgeDensity;
            camera.LastTextureScore = direction.TextureUniformityScore;
            await _cameraRepo.UpdateAsync(camera);

            return Ok(new
            {
                cameraId = camera.Id,
                direction = new
                {
                    status = direction.Status.ToString(),
                    confidence = direction.Confidence,
                    description = direction.Description,
                    edgeDensity = direction.EdgeDensity,
                    textureUniformityScore = direction.TextureUniformityScore,
                    meanBrightness = direction.MeanBrightness,
                    brightnessStdDev = direction.BrightnessStdDev,
                    entropy = direction.Entropy
                },
                statistics = new
                {
                    mean = stats.Mean,
                    stdDev = stats.StdDev,
                    variance = stats.Variance,
                    min = stats.Min,
                    max = stats.Max,
                    median = stats.Median,
                    sampleCount = stats.SampleCount
                },
                frameInfo = new
                {
                    width = frame.Width,
                    height = frame.Height,
                    originalSizeBytes = frame.OriginalSizeBytes,
                    downloadTimeMs = frame.DownloadTimeMs,
                    decodeTimeMs = frame.DecodeTimeMs
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Direction analysis failed for camera {CameraId}", id);
            return StatusCode(502, new { message = "Не удалось выполнить анализ кадра", code = "direction_analysis_failed" });
        }
    }

    /// <summary>
    /// Trigger PTZ auto-correction when wall obstruction is detected.
    /// Camera will perform a pan sweep to find a clear view.
    /// </summary>
    [Authorize(Policy = AccessPolicies.ManageCameras)]
    [HttpPost("{id}/ptz/correct")]
    public async Task<ActionResult> AutoCorrectPtz(int id, CancellationToken cancellationToken)
    {
        var result = await _ptzCorrection.TryCorrectAsync(
            id,
            cancellationToken: cancellationToken);

        if (result.Success)
        {
            return Ok(new
            {
                success = true,
                strategy = result.Strategy,
                finalPan = result.FinalPan,
                finalTilt = result.FinalTilt,
                attempts = result.Attempts,
                description = result.Description
            });
        }

        return StatusCode(502, new
        {
            success = false,
            strategy = result.Strategy,
            attempts = result.Attempts,
            description = result.Description,
            message = result.Description
        });
    }

    /// <summary>
    /// Quick check — is the camera currently facing a wall?
    /// </summary>
    [Authorize(Policy = AccessPolicies.CheckCameras)]
    [HttpGet("{id}/direction/quick")]
    public async Task<ActionResult> QuickDirectionCheck(int id, CancellationToken cancellationToken)
    {
        var (isObstructed, status, confidence) = await _ptzCorrection.QuickCheckAsync(id, cancellationToken);

        return Ok(new
        {
            cameraId = id,
            isObstructed,
            status = status.ToString(),
            confidence = Math.Round(confidence, 3)
        });
    }

}

public record CameraUpdateDto
{
    public CameraLocationInput? Location { get; set; }
    public string? Name { get; set; }
    public string? IpAddress { get; set; }
    public int? Port { get; set; }
    public string? ServiceUrl { get; set; }
    public string? Notes { get; set; }
    public string? Tags { get; set; }
    public bool? AutoBackup { get; set; }
    public bool? AutoRecovery { get; set; }
    public int? CheckIntervalSeconds { get; set; }
}

public record CredentialDto
{
    public string Username { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string? AuthType { get; set; }
}

public record CameraSettingsDto
{
    public CameraLocationInput? Location { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public int Port { get; set; }
    public string ServiceUrl { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Tags { get; set; } = "[]";
    public bool AutoBackup { get; set; }
    public bool AutoRecovery { get; set; }
    public int CheckIntervalSeconds { get; set; }
    public string? Username { get; set; }
    public string AuthType { get; set; } = "wsse";
    public bool HasCredentials { get; set; }
}

public record CreateCameraDto
{
    public CameraLocationInput? Location { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 80;
    public string? ServiceUrl { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? AuthType { get; set; }
    public string? Notes { get; set; }
    public string? Tags { get; set; }
    public bool AutoBackup { get; set; } = true;
    public bool AutoRecovery { get; set; } = true;
    public int CheckIntervalSeconds { get; set; } = 3600;
}
