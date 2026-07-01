using GoalFueled.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GoalFueled.Data.Repositories;

public class AppSettingsRepository : IAppSettingsRepository
{
    private readonly GoalFueledDbContext _db;

    public AppSettingsRepository(GoalFueledDbContext db) => _db = db;

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Id == key, ct);
        return setting?.Value;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Id == key, ct);
        if (setting is null)
        {
            _db.AppSettings.Add(new AppSetting { Id = key, Value = value });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
            _db.AppSettings.Update(setting);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Id == key, ct);
        if (setting is null) return;
        _db.AppSettings.Remove(setting);
        await _db.SaveChangesAsync(ct);
    }
}
