using CameraMaintainer.Core.Models;

namespace CameraMaintainer.Core.Interfaces;

public interface ICameraRepository
{
    Task<Camera?> GetByIdAsync(int id);
    Task<List<int>> GetExistingIdsAsync(IReadOnlyCollection<int> ids);
    Task<List<Camera>> GetAllAsync(CameraFilter? filter = null);
    Task<int> GetCountAsync(CameraFilter? filter = null);
    Task<Dictionary<Enums.CameraStatus, int>> GetStatusCountsAsync();
    Task<List<Camera>> GetByIdsAsync(IReadOnlyCollection<int> ids);
    Task<Camera> AddAsync(Camera camera);
    Task<Camera> UpdateAsync(Camera camera);
    Task DeleteAsync(int id);
    Task UpdateStatusAsync(int cameraId, Enums.CameraStatus status, DateTime? lastSeen = null);
    /// <summary>
    /// Returns due cameras in their configured queue order.
    /// Physical faults retain their position; stable cameras use the monitoring policy.
    /// </summary>
    Task<List<int>> GetDueCameraIdsAsync(DateTime now);
    Task<CameraCheckCycle> GetCheckCycleAsync(DateTime now);
    Task CompleteCheckCycleAsync(CameraCheckCycle cycle, CancellationToken cancellationToken = default);
    Task<List<CameraCheckQueueItem>> GetCheckQueueAsync(IReadOnlyCollection<int>? cameraIds = null);
    Task UpdateCheckQueueAsync(IReadOnlyList<CameraCheckQueueUpdate> items);
    Task<bool> SetAutomaticChecksEnabledAsync(int cameraId, bool enabled);
    Task<List<int>> GetCameraIdsDueForBackupAsync(DateTime createdBefore);
    Task<Camera?> GetByIpAsync(string ipAddress);
}

public class CameraFilter
{
    public string? Status { get; set; }
    public bool? AutomaticChecksEnabled { get; set; }
    public string? Search { get; set; }
    public string? Tags { get; set; }
    public bool Attention { get; set; }
    public int? AfterId { get; set; }
    public int Page { get; set; } = 1;
    public int PerPage { get; set; } = 50;
    public string? SortBy { get; set; }
    public bool SortDesc { get; set; }
}
