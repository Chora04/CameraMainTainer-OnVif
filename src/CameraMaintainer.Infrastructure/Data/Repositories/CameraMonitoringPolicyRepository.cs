using System.Text.Json;
using CameraMaintainer.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CameraMaintainer.Infrastructure.Data.Repositories;

public sealed class CameraMonitoringPolicyRepository(AppDbContext db)
{
    public const string SettingKey = "cameras.monitoring.policy";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CameraMonitoringPolicy> GetAsync()
    {
        var value = await db.Settings.AsNoTracking().Where(setting => setting.Key == SettingKey)
            .Select(setting => setting.Value).SingleOrDefaultAsync();
        if (value != null)
        {
            try
            {
                var policy = JsonSerializer.Deserialize<CameraMonitoringPolicy>(value, JsonOptions);
                if (policy?.IsValid == true) return policy;
            }
            catch (JsonException)
            {
                return new();
            }
        }
        return new();
    }

    public async Task SaveAsync(CameraMonitoringPolicy policy)
    {
        if (!policy.IsValid) throw new ArgumentOutOfRangeException(nameof(policy));
        var setting = await db.Settings.SingleOrDefaultAsync(item => item.Key == SettingKey);
        if (setting == null)
        {
            setting = new Setting { Key = SettingKey };
            db.Settings.Add(setting);
        }
        setting.Value = JsonSerializer.Serialize(policy, JsonOptions);
        setting.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
