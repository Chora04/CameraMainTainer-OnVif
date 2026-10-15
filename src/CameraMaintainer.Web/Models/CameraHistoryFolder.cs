using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Models;

namespace CameraMaintainer.Web.Models;

public sealed class CameraHistoryFolder
{
    public int? CameraId { get; init; }
    public string? Name { get; init; }
    public string? IpAddress { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string? StreetAddress { get; init; }
    public string Coordinates => Latitude.HasValue && Longitude.HasValue
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Latitude:0.######}, {Longitude:0.######}") : "Координаты не указаны";
    public int Cases { get; init; }
    public int Technical { get; init; }
    public int Physical { get; init; }
    public DateTime? LastClosedAt { get; init; }
    public double? AverageRepairSeconds { get; init; }

    // The caller supplies an already authorized query; aggregate in SQL, before paging.
    public static IQueryable<CameraHistoryFolder> From(IQueryable<Alert> visibleAlerts) =>
        visibleAlerts.Where(alert => alert.AutoResolved)
            .GroupBy(alert => new
            {
                alert.CameraId,
                Name = alert.Camera == null ? null : alert.Camera.Name,
                IpAddress = alert.Camera == null ? null : alert.Camera.IpAddress,
                Latitude = alert.Camera == null ? null : alert.Camera.Latitude,
                Longitude = alert.Camera == null ? null : alert.Camera.Longitude,
                StreetAddress = alert.Camera == null ? null : alert.Camera.StreetAddress
            })
#pragma warning disable CA1860 // EF Core translates Any() to EXISTS; Count would produce less efficient SQL.
            .Select(group => new CameraHistoryFolder
            {
                CameraId = group.Key.CameraId,
                Name = group.Key.Name,
                IpAddress = group.Key.IpAddress,
                Latitude = group.Key.Latitude,
                Longitude = group.Key.Longitude,
                StreetAddress = group.Key.StreetAddress,
                Cases = group.Count(),
                Technical = group.Sum(alert => alert.Issues.Any()
                    ? alert.Issues.Count(issue => issue.Category == ErrorCategory.Technical)
                    : (alert.Category == ErrorCategory.Technical ? 1 : 0)),
                Physical = group.Sum(alert => alert.Issues.Any()
                    ? alert.Issues.Count(issue => issue.Category == ErrorCategory.Physical)
                    : (alert.Category == ErrorCategory.Physical ? 1 : 0)),
                LastClosedAt = group.Max(alert => alert.ResolvedAt),
                AverageRepairSeconds = group.Average(alert =>
                    (alert.RecoveryDetectedAt ?? alert.ResolvedAt) >= alert.CreatedAt
                        ? (double?)((alert.RecoveryDetectedAt ?? alert.ResolvedAt)!.Value.Ticks - alert.CreatedAt.Ticks) / TimeSpan.TicksPerSecond
                        : null)
            });
#pragma warning restore CA1860

    public string AverageRepairText => FormatDuration(AverageRepairSeconds);

    public static string FormatDuration(double? seconds)
    {
        if (!seconds.HasValue || !double.IsFinite(seconds.Value) || seconds < 0) return "Нет данных";
        var minutes = (long)Math.Round(seconds.Value / 60, MidpointRounding.AwayFromZero);
        if (minutes < 1) return "< 1 мин";
        if (minutes < 60) return $"{minutes} мин";
        if (minutes < 1440) return $"{minutes / 60} ч {minutes % 60} мин";
        return $"{minutes / 1440} д {minutes % 1440 / 60} ч";
    }
}
