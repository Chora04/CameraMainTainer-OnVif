using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Infrastructure.Data;
using CameraMaintainer.Onvif;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Services;

public class DiscoveryService : IDiscoveryService
{
    private readonly ICameraRepository _cameraRepo;
    private readonly AppDbContext _db;
    private readonly ILogger<DiscoveryService> _logger;
    private readonly IMonitoringEvents? _events;

    public DiscoveryService(
        ICameraRepository cameraRepo,
        AppDbContext db,
        ILogger<DiscoveryService> logger,
        IMonitoringEvents? events = null)
    {
        _cameraRepo = cameraRepo;
        _db = db;
        _logger = logger;
        _events = events;
    }

    public async Task<ScanRecord> ScanAsync(
        string scanType = "ws_discovery",
        string? ipRange = null,
        CancellationToken cancellationToken = default)
    {
        var scan = new ScanRecord
        {
            ScanType = scanType,
            Status = "running",
            StartedAt = DateTime.UtcNow
        };
        _db.ScanRecords.Add(scan);
        await _db.SaveChangesAsync(cancellationToken);
        var registryChanged = false;

        try
        {
            List<DiscoveryClient.DiscoveredDevice> devices;

            if (scanType == "ip_range" && !string.IsNullOrEmpty(ipRange))
            {
                _logger.LogInformation("Starting IP range scan: {IpRange}", ipRange);
                devices = await DiscoveryClient.ScanIpRangeAsync(ipRange, ct: cancellationToken);
            }
            else
            {
                _logger.LogInformation("Starting WS-Discovery multicast scan");
                devices = await DiscoveryClient.DiscoverAsync(5, cancellationToken);
            }

            scan.CamerasFound = devices.Count;
            var addedCount = 0;

            foreach (var device in devices)
            {
                var existing = await _cameraRepo.GetByIpAsync(device.IpAddress);
                if (existing != null) continue;

                var camera = new Camera
                {
                    Name = $"{device.Manufacturer ?? "Unknown"} - {device.IpAddress}",
                    ServiceUrl = device.ServiceUrl,
                    IpAddress = device.IpAddress,
                    Manufacturer = device.Manufacturer,
                    Model = device.Model,
                    SerialNumber = device.SerialNumber,
                    Status = CameraStatus.Unknown,
                    PtzSupported = device.Scopes.Any(s => s.Contains("ptz", StringComparison.OrdinalIgnoreCase))
                };

                await _cameraRepo.AddAsync(camera);
                addedCount++;
                registryChanged = true;

                _logger.LogInformation("Discovered new camera: {Name} at {Ip}", camera.Name, camera.IpAddress);
            }

            scan.CamerasAdded = addedCount;
            scan.Status = "completed";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            scan.Status = "cancelled";
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discovery scan failed");
            scan.Status = "failed";
        }
        finally
        {
            scan.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            if (registryChanged && _events != null)
                await _events.CameraRegistryChangedAsync();
        }

        return scan;
    }

    public async Task<ScanRecord?> GetScanStatusAsync(int scanId)
    {
        return await _db.ScanRecords.FindAsync(scanId);
    }

}
