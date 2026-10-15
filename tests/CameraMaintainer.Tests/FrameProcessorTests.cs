using CameraMaintainer.Services;
using SkiaSharp;
using System.Net;

namespace CameraMaintainer.Tests;

public sealed class FrameProcessorTests
{
    [Fact]
    public void ComputeStatistics_BalancedDarkAndBrightPixels_ReturnsExpectedValues()
    {
        using var processor = new FrameProcessor();
        var pixels = Enumerable.Repeat((byte)0, 50)
            .Concat(Enumerable.Repeat((byte)255, 50))
            .ToArray();

        var result = processor.ComputeStatistics(pixels, 10, 10);

        Assert.Equal(100, result.SampleCount);
        Assert.Equal(127.5, result.Mean);
        Assert.Equal(127.5, result.StdDev);
        Assert.Equal(1, result.Entropy);
        Assert.Equal((byte)0, result.Min);
        Assert.Equal((byte)255, result.Max);
        Assert.Equal(50, result.Histogram[0]);
        Assert.Equal(50, result.Histogram[255]);
    }

    [Fact]
    public void DecodeJpegBytes_DownscalesAndPreservesAspectRatio()
    {
        using var processor = new FrameProcessor();
        var jpeg = CreateJpeg(width: 1_600, height: 900);

        var result = processor.DecodeJpegBytes(jpeg, maxWidth: 640);

        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Equal(640, result.Width);
        Assert.Equal(360, result.Height);
        Assert.Equal(result.Width * result.Height, result.Pixels.Length);
    }

    [Fact]
    public void DecodeJpegBytes_InvalidData_ReturnsInvalidFrame()
    {
        using var processor = new FrameProcessor();

        var result = processor.DecodeJpegBytes(new byte[] { 1, 2, 3, 4 });

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.ErrorMessage!);
    }

    [Theory]
    [InlineData(3840, 2160, 640, 360)]
    [InlineData(2160, 3840, 640, 1138)]
    [InlineData(640, 480, 640, 480)]
    public void CodecScaling_PreservesDimensionsAndLuminance(int width, int height, int expectedWidth, int expectedHeight)
    {
        using var processor = new FrameProcessor();
        var result = processor.DecodeJpegBytes(CreateJpeg(width, height));
        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Equal(expectedWidth, result.Width);
        Assert.Equal(expectedHeight, result.Height);
        var statistics = processor.ComputeStatistics(result.Pixels, result.Width, result.Height);
        Assert.InRange(statistics.Mean, 115, 125);
        Assert.InRange(statistics.StdDev, 0, 2);
    }

    [Fact]
    public void EncodeToJpeg_ProducesDecodableImageWithOriginalDimensions()
    {
        using var processor = new FrameProcessor();
        const int width = 32;
        const int height = 24;
        var pixels = Enumerable.Range(0, width * height)
            .Select(index => (byte)(index % 256))
            .ToArray();

        var jpeg = processor.EncodeToJpeg(pixels, width, height, quality: 90);
        var result = processor.DecodeJpegBytes(jpeg);

        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Equal(width, result.Width);
        Assert.Equal(height, result.Height);
    }

    [Fact]
    public async Task CaptureAndDecodeAsync_RejectsOversizedSnapshotBeforeDownload()
    {
        using var http = new HttpClient(new StubHandler(() =>
        {
            var content = new StreamContent(Stream.Null);
            content.Headers.ContentLength = 13 * 1024 * 1024;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
        using var processor = new FrameProcessor(http);

        var result = await processor.CaptureAndDecodeAsync("http://camera/snapshot.jpg");

        Assert.False(result.IsValid);
        Assert.Contains("12 MB", result.ErrorMessage);
    }

    [Fact]
    public async Task CaptureAndDecodeAsync_ReturnsOriginalEncodedImageAndContentType()
    {
        var jpeg = CreateJpeg(width: 640, height: 480);
        using var http = new HttpClient(new StubHandler(() =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(jpeg)
            }));
        using var processor = new FrameProcessor(http);

        var result = await processor.CaptureAndDecodeAsync("http://camera/snapshot.jpg");

        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Equal(jpeg, result.EncodedImage);
        Assert.Equal("image/jpeg", result.ContentType);
    }

    [Fact]
    public async Task CaptureAndDecodeAsync_WithCredentials_UsesReusableAuthenticatedClientDirectly()
    {
        var regularRequests = 0;
        var authenticatedRequests = 0;
        using var regular = new HttpClient(new DelegatingStubHandler((_, _) =>
        {
            Interlocked.Increment(ref regularRequests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }));
        using var authenticated = new HttpClient(new DelegatingStubHandler((_, _) =>
        {
            Interlocked.Increment(ref authenticatedRequests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[100])
            });
        }));
        using var processor = new FrameProcessor(regular, authenticatedHttp: authenticated);

        _ = await processor.CaptureAndDecodeAsync(
            "http://camera/snapshot.jpg",
            "admin",
            "password");

        Assert.Equal(0, regularRequests);
        Assert.Equal(1, authenticatedRequests);
    }

    [Fact]
    public async Task CaptureAndDecodeAsync_WhenCallerCancels_PropagatesCancellation()
    {
        using var http = new HttpClient(new DelegatingStubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var processor = new FrameProcessor(http);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            processor.CaptureAndDecodeAsync(
                "http://camera/snapshot.jpg",
                ct: cancellation.Token));
    }

    private static byte[] CreateJpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    private sealed class StubHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory());
    }

    private sealed class DelegatingStubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}
