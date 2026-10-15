using System.Globalization;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Infrastructure.Data;

public sealed class CameraFrameArchiveService : ICameraFrameArchiveService
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly AppDbContext _db;
    private readonly Repositories.CameraMonitoringPolicyRepository _policyRepository;
    private readonly ILogger<CameraFrameArchiveService> _logger;
    private readonly string _rootPath;
    private readonly string _rootPrefix;

    public CameraFrameArchiveService(
        AppDbContext db,
        Repositories.CameraMonitoringPolicyRepository policyRepository,
        string rootPath,
        ILogger<CameraFrameArchiveService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _db = db;
        _policyRepository = policyRepository;
        _logger = logger;
        _rootPath = Path.GetFullPath(rootPath);
        _rootPrefix = _rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
    }

    public async Task<CameraFrameRecord?> RecordAsync(
        CameraFrameCapture capture,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.CameraId <= 0)
            throw new ArgumentOutOfRangeException(nameof(capture), "Camera ID must be positive.");

        var policy = await _policyRepository.GetAsync();
        if (!policy.StoreFrameHistory)
            return null;

        var triggerCodes = EvaluateAnomalies(capture, policy);
        var isAnomalous = triggerCodes.Count > 0;
        string? relativePath = null;
        string? contentType = null;
        long? imageSize = null;

        if (isAnomalous && policy.StoreAnomalyImages && capture.EncodedImage.Length > 0)
        {
            contentType = NormalizeContentType(capture.ImageContentType);
            relativePath = CreateRelativePath(capture.CameraId, capture.CapturedAt, contentType);
            await WriteImageAsync(relativePath, capture.EncodedImage, cancellationToken);
            imageSize = capture.EncodedImage.LongLength;
        }

        var record = new CameraFrameRecord
        {
            CameraId = capture.CameraId,
            CapturedAt = capture.CapturedAt,
            IsAnomalous = isAnomalous,
            TriggerCodes = string.Join(',', triggerCodes),
            DirectionStatus = Limit(capture.DirectionStatus, 30, "Unknown"),
            ObstructionType = Limit(capture.ObstructionType, 40, "none"),
            MeanBrightness = capture.MeanBrightness,
            BrightnessStdDev = capture.BrightnessStdDev,
            Entropy = capture.Entropy,
            EdgeDensity = capture.EdgeDensity,
            TextureUniformityScore = capture.TextureUniformityScore,
            DirectionConfidence = capture.DirectionConfidence,
            ImageRelativePath = relativePath,
            ImageContentType = contentType,
            ImageSizeBytes = imageSize
        };

        try
        {
            _db.CameraFrameRecords.Add(record);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            if (relativePath != null)
                TryDeleteFile(relativePath);
            throw;
        }

        await PruneAsync(capture.CameraId, policy.FrameHistoryLimitPerCamera, cancellationToken);
        return record;
    }

    public async Task<IReadOnlyList<CameraFrameRecord>> GetRecentAsync(
        int cameraId,
        CancellationToken cancellationToken = default)
    {
        var policy = await _policyRepository.GetAsync();
        if (!policy.StoreFrameHistory)
            return [];

        return await _db.CameraFrameRecords.AsNoTracking()
            .Where(record => record.CameraId == cameraId)
            .OrderByDescending(record => record.CapturedAt)
            .ThenByDescending(record => record.Id)
            .Take(policy.FrameHistoryLimitPerCamera)
            .ToListAsync(cancellationToken);
    }

    public async Task<CameraFrameContent?> OpenContentAsync(
        int cameraId,
        long recordId,
        CancellationToken cancellationToken = default)
    {
        var record = await _db.CameraFrameRecords.AsNoTracking()
            .Where(item => item.Id == recordId && item.CameraId == cameraId)
            .Select(item => new
            {
                item.ImageRelativePath,
                item.ImageContentType
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(record?.ImageRelativePath))
            return null;

        var fullPath = ResolvePath(record.ImageRelativePath);
        if (!File.Exists(fullPath))
        {
            _logger.LogWarning(
                "Archived frame file is missing for camera {CameraId}, record {RecordId}",
                cameraId,
                recordId);
            return null;
        }

        var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return new CameraFrameContent(
            stream,
            NormalizeContentType(record.ImageContentType),
            Path.GetFileName(fullPath));
    }

    public async Task DeleteForCameraAsync(
        int cameraId,
        CancellationToken cancellationToken = default)
    {
        var records = await _db.CameraFrameRecords
            .Where(record => record.CameraId == cameraId)
            .ToListAsync(cancellationToken);

        var imagePaths = records
            .Select(record => record.ImageRelativePath)
            .Where(path => path != null)
            .Cast<string>()
            .ToArray();

        _db.CameraFrameRecords.RemoveRange(records);
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var imagePath in imagePaths)
            TryDeleteFile(imagePath);
    }

    private async Task PruneAsync(
        int cameraId,
        int limit,
        CancellationToken cancellationToken)
    {
        var excess = await _db.CameraFrameRecords
            .Where(record => record.CameraId == cameraId)
            .OrderByDescending(record => record.CapturedAt)
            .ThenByDescending(record => record.Id)
            .Skip(limit)
            .ToListAsync(cancellationToken);
        if (excess.Count == 0)
            return;

        var imagePaths = excess
            .Select(record => record.ImageRelativePath)
            .Where(path => path != null)
            .Cast<string>()
            .ToArray();

        _db.CameraFrameRecords.RemoveRange(excess);
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var imagePath in imagePaths)
            TryDeleteFile(imagePath);
    }

    private static List<string> EvaluateAnomalies(
        CameraFrameCapture capture,
        CameraMonitoringPolicy policy)
    {
        var triggers = new List<string>(5);
        if (capture.IsObstructed)
            triggers.Add("obstruction");
        if (!capture.DirectionStatus.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            triggers.Add("direction");
        if (capture.MeanBrightness < policy.NormalBrightnessMinimum)
            triggers.Add("brightness_low");
        else if (capture.MeanBrightness > policy.NormalBrightnessMaximum)
            triggers.Add("brightness_high");
        if (capture.BrightnessStdDev < policy.NormalContrastMinimum)
            triggers.Add("contrast_low");
        if (capture.Entropy < policy.NormalEntropyMinimum)
            triggers.Add("entropy_low");
        return triggers.Distinct(StringComparer.Ordinal).ToList();
    }

    private async Task WriteImageAsync(
        string relativePath,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var fullPath = ResolvePath(relativePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Archive image directory is invalid.");
        Directory.CreateDirectory(directory);

        var temporaryPath = fullPath + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string CreateRelativePath(int cameraId, DateTime capturedAt, string contentType)
    {
        var utc = capturedAt.Kind == DateTimeKind.Utc ? capturedAt : capturedAt.ToUniversalTime();
        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
        var fileName = $"{utc:yyyyMMddTHHmmssfff}_{Guid.NewGuid():N}{extension}";
        return Path.Combine(
                cameraId.ToString(CultureInfo.InvariantCulture),
                utc.ToString("yyyy", CultureInfo.InvariantCulture),
                utc.ToString("MM", CultureInfo.InvariantCulture),
                fileName)
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    private string ResolvePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException("Archive path must be relative.");

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalized));
        if (!fullPath.StartsWith(_rootPrefix, PathComparison))
            throw new InvalidDataException("Archive path escapes the configured storage root.");
        return fullPath;
    }

    private void TryDeleteFile(string relativePath)
    {
        try
        {
            var fullPath = ResolvePath(relativePath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.LogWarning(exception, "Could not delete archived frame {RelativePath}", relativePath);
        }
    }

    private static string NormalizeContentType(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        "image/png" => "image/png",
        "image/webp" => "image/webp",
        _ => "image/jpeg"
    };

    private static string Limit(string? value, int length, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length <= length ? normalized : normalized[..length];
    }
}
