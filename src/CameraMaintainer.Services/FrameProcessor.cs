using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace CameraMaintainer.Services;

/// <summary>
/// JPEG frame processing pipeline using SkiaSharp (libjpeg-turbo).
/// Handles snapshot download, fast JPEG decode to grayscale, statistics, and re-encoding.
///
/// Memory strategy:
/// - JPEGs use codec-level scaling before allocating the bitmap; analysis defaults to 640px wide
/// - ArrayPool&lt;byte&gt; is used for temporary buffers
/// - SKBitmap is disposed immediately after pixel extraction
/// - Grayscale (Gray8) uses 1 byte/pixel vs 4 bytes/pixel for RGBA
/// </summary>
public sealed class FrameProcessor : IFrameProcessor, IDisposable
{
    private readonly HttpClient _http;
    private readonly HttpClient _authenticatedHttp;
    private readonly AuthorityCredentials? _authorityCredentials;
    private readonly ILogger<FrameProcessor>? _logger;

    // Default values
    private const int DefaultMaxWidth = 640;
    private const int DefaultJpegQuality = 75;
    private const int MaximumSnapshotBytes = 12 * 1024 * 1024;
    private static readonly SKSamplingOptions LinearSampling =
        new(SKFilterMode.Linear, SKMipmapMode.None);

    public FrameProcessor(
        HttpClient? http = null,
        ILogger<FrameProcessor>? logger = null,
        HttpClient? authenticatedHttp = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _logger = logger;

        if (authenticatedHttp != null)
        {
            _authenticatedHttp = authenticatedHttp;
        }
        else
        {
            _authorityCredentials = new AuthorityCredentials();
            _authenticatedHttp = new HttpClient(new HttpClientHandler
            {
                Credentials = _authorityCredentials,
                PreAuthenticate = true,
                CheckCertificateRevocationList = true
            })
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }
    }

    /// <inheritdoc />
    public async Task<FrameData> CaptureAndDecodeAsync(
        string snapshotUri,
        string? username = null,
        string? password = null,
        int maxWidth = DefaultMaxWidth,
        CancellationToken ct = default)
    {
        var downloadSw = Stopwatch.StartNew();

        try
        {
            byte[] imageBytes;

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                imageBytes = await DownloadWithAuthAsync(snapshotUri, username, password, ct);
            }
            else
            {
                imageBytes = await DownloadBoundedAsync(_http, snapshotUri, ct);
            }

            downloadSw.Stop();

            if (imageBytes.Length < 1000)
            {
                return new FrameData
                {
                    IsValid = false,
                    ErrorMessage = "Image too small (< 1 KB)",
                    OriginalSizeBytes = imageBytes.Length,
                    DownloadTimeMs = downloadSw.ElapsedMilliseconds,
                    SourceUri = snapshotUri
                };
            }

            var frame = DecodeJpegBytes(imageBytes, maxWidth);
            return frame with
            {
                DownloadTimeMs = downloadSw.ElapsedMilliseconds,
                SourceUri = snapshotUri,
                OriginalSizeBytes = imageBytes.Length,
                EncodedImage = imageBytes,
                ContentType = DetectImageContentType(imageBytes)
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            downloadSw.Stop();
            _logger?.LogDebug(ex, "Frame capture failed for {Uri}", snapshotUri);

            return new FrameData
            {
                IsValid = false,
                ErrorMessage = ex.Message,
                DownloadTimeMs = downloadSw.ElapsedMilliseconds,
                SourceUri = snapshotUri
            };
        }
    }

    /// <inheritdoc />
    public FrameData DecodeJpegBytes(byte[] jpegBytes, int maxWidth = DefaultMaxWidth)
    {
        var decodeSw = Stopwatch.StartNew();

        try
        {
            if (jpegBytes.Length > MaximumSnapshotBytes)
                throw new InvalidDataException("Snapshot exceeds the 12 MB safety limit.");
            using var data = SKData.CreateCopy(jpegBytes);
            using var codec = SKCodec.Create(data);
            var source = codec.Info;
            if (source.Width <= 0 || source.Height <= 0 || (long)source.Width * source.Height > 64_000_000)
                throw new InvalidDataException("Snapshot dimensions exceed the safety limit.");

            // Convert to grayscale — 1 byte per pixel, minimum RAM
            maxWidth = Math.Clamp(maxWidth, 1, 4096);
            var scale = Math.Min(1, Math.Min(maxWidth / (double)source.Width, 4096.0 / source.Height));
            var targetWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
            var targetHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
            // JPEG codecs support eighth-size steps. Round upward to avoid upscaling analysis pixels.
            var dimensions = codec.GetScaledDimensions((float)(Math.Ceiling(scale * 8) / 8));
            if ((long)dimensions.Width * dimensions.Height > 16_000_000)
                throw new InvalidDataException("Decoded snapshot exceeds the pixel safety limit.");
            var decodeInfo = new SKImageInfo(dimensions.Width, dimensions.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var skBitmap = new SKBitmap(decodeInfo);
            var decodeResult = codec.GetPixels(decodeInfo, skBitmap.GetPixels());
            if (decodeResult != SKCodecResult.Success)
                throw new InvalidDataException($"Snapshot decode failed: {decodeResult}.");

            using var grayBitmap = new SKBitmap(
                targetWidth,
                targetHeight,
                SKColorType.Gray8,
                SKAlphaType.Opaque);
            using var canvas = new SKCanvas(grayBitmap);

            // Downscale if needed to keep analysis fast and RAM low
            if (targetWidth != skBitmap.Width || targetHeight != skBitmap.Height)
            {
                // Resize directly to grayscale surface
                canvas.DrawBitmap(skBitmap,
                    new SKRect(0, 0, targetWidth, targetHeight),
                    LinearSampling);
            }
            else
            {
                // No scaling needed — convert to grayscale via color filter
                using var paint = new SKPaint
                {
                    ColorFilter = SKColorFilter.CreateColorMatrix(new float[]
                    {
                        0.299f, 0.587f, 0.114f, 0, 0,  // Red → luminance
                        0.299f, 0.587f, 0.114f, 0, 0,  // Green → luminance
                        0.299f, 0.587f, 0.114f, 0, 0,  // Blue → luminance
                        0, 0, 0, 1, 0                    // Alpha passthrough
                    })
                };
                canvas.DrawBitmap(skBitmap, 0, 0, SKSamplingOptions.Default, paint);
            }

            canvas.Flush();

            var width = grayBitmap.Width;
            var height = grayBitmap.Height;
            var pixelCount = width * height;

            // Extract raw grayscale pixels
            var pixels = new byte[pixelCount];
            var ptr = grayBitmap.GetPixels();
            CopyBitmapToBuffer(grayBitmap, ptr, pixels, width, height);

            decodeSw.Stop();

            return new FrameData
            {
                Pixels = pixels,
                Width = width,
                Height = height,
                IsValid = true,
                DecodeTimeMs = decodeSw.ElapsedMilliseconds,
                ContentType = "image/jpeg"
            };
        }
        catch (Exception ex)
        {
            decodeSw.Stop();
            _logger?.LogDebug(ex, "JPEG decode failed");

            return new FrameData
            {
                IsValid = false,
                ErrorMessage = $"Decode error: {ex.Message}",
                DecodeTimeMs = decodeSw.ElapsedMilliseconds
            };
        }
    }

    /// <inheritdoc />
    public byte[] EncodeToJpeg(byte[] grayPixels, int width, int height, int quality = DefaultJpegQuality)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
        if (grayPixels.Length < width * height)
            throw new ArgumentException("Pixel buffer is smaller than the image dimensions.", nameof(grayPixels));

        using var bitmap = new SKBitmap(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
        var ptr = bitmap.GetPixels();
        CopyBufferToBitmap(grayPixels, bitmap, ptr, width, height);

        quality = Math.Clamp(quality, 10, 100);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);

        return data.ToArray();
    }

    /// <inheritdoc />
    public ImageStatistics ComputeStatistics(byte[] grayPixels, int width, int height, int sampleStep = 1)
    {
        var histogram = new int[256];
        long sum = 0;
        long sumSq = 0;
        byte min = 255;
        byte max = 0;
        int sampleCount = 0;

        var pixelCount = width * height;
        var step = Math.Max(1, sampleStep);

        for (int i = 0; i < pixelCount; i += step)
        {
            byte b = grayPixels[i];
            histogram[b]++;
            sum += b;
            sumSq += b * b;
            if (b < min) min = b;
            if (b > max) max = b;
            sampleCount++;
        }

        if (sampleCount < 10)
            return new ImageStatistics { Histogram = histogram, SampleCount = sampleCount };

        double mean = (double)sum / sampleCount;
        double variance = ((double)sumSq / sampleCount) - (mean * mean);
        if (variance < 0) variance = 0;
        double stdDev = Math.Sqrt(variance);

        // Shannon entropy
        double entropy = 0;
        for (int i = 0; i < 256; i++)
        {
            if (histogram[i] > 0)
            {
                double p = (double)histogram[i] / sampleCount;
                entropy -= p * Math.Log2(p);
            }
        }

        // Median
        byte median = 0;
        int cumulativeCount = 0;
        int halfCount = sampleCount / 2;
        for (int i = 0; i < 256; i++)
        {
            cumulativeCount += histogram[i];
            if (cumulativeCount >= halfCount)
            {
                median = (byte)i;
                break;
            }
        }

        return new ImageStatistics
        {
            Histogram = histogram,
            Mean = Math.Round(mean, 2),
            Variance = Math.Round(variance, 2),
            StdDev = Math.Round(stdDev, 2),
            Entropy = Math.Round(entropy, 3),
            Min = min,
            Max = max,
            Median = median,
            SampleCount = sampleCount
        };
    }

    // ── Private helpers ──

    private static void CopyBitmapToBuffer(
        SKBitmap bitmap,
        IntPtr source,
        byte[] destination,
        int width,
        int height)
    {
        for (var row = 0; row < height; row++)
        {
            var sourceRow = IntPtr.Add(source, row * bitmap.RowBytes);
            System.Runtime.InteropServices.Marshal.Copy(
                sourceRow,
                destination,
                row * width,
                width);
        }
    }

    private static void CopyBufferToBitmap(
        byte[] source,
        SKBitmap bitmap,
        IntPtr destination,
        int width,
        int height)
    {
        for (var row = 0; row < height; row++)
        {
            var destinationRow = IntPtr.Add(destination, row * bitmap.RowBytes);
            System.Runtime.InteropServices.Marshal.Copy(
                source,
                row * width,
                destinationRow,
                width);
        }
    }

    private async Task<byte[]> DownloadWithAuthAsync(string url, string username, string password, CancellationToken ct)
    {
        var uri = new Uri(url, UriKind.Absolute);
        _authorityCredentials?.Set(uri, username, password);
        return await DownloadBoundedAsync(_authenticatedHttp, url, ct);
    }

    private static async Task<byte[]> DownloadBoundedAsync(
        HttpClient client,
        string url,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            url,
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

            return destination.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string DetectImageContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return "image/png";
        }

        if (bytes.Length >= 12 &&
            bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
            bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
        {
            return "image/webp";
        }

        return "image/jpeg";
    }

    public void Dispose()
    {
        _http.Dispose();
        _authenticatedHttp.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class AuthorityCredentials : ICredentials
    {
        private readonly ConcurrentDictionary<string, NetworkCredential> _credentials =
            new(StringComparer.OrdinalIgnoreCase);

        public void Set(Uri uri, string username, string password)
        {
            _credentials[GetCredentialKey(uri)] = new NetworkCredential(username, password);
        }

        public NetworkCredential? GetCredential(Uri uri, string authType)
        {
            return _credentials.TryGetValue(GetCredentialKey(uri), out var credential)
                ? credential
                : null;
        }

        private static string GetCredentialKey(Uri uri) => uri.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
            UriFormat.UriEscaped);
    }
}
