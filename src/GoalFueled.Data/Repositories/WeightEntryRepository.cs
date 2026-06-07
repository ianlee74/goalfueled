using GoalFueled.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GoalFueled.Data.Repositories;

public class WeightEntryRepository : IWeightEntryRepository
{
    private readonly GoalFueledDbContext _db;

    public WeightEntryRepository(GoalFueledDbContext db) => _db = db;

    public async Task<IReadOnlyList<WeightEntry>> ListForUserAsync(string userId, CancellationToken ct = default)
    {
        // A user's weight history is small; materialize then order by date in memory so the ordering does
        // not depend on the provider's translation of DateOnly comparisons (Cosmos vs in-memory).
        var entries = await _db.WeightEntries
            .Where(w => w.OwnerUserId == userId)
            .ToListAsync(ct);
        return entries.OrderByDescending(w => w.Date).ToList();
    }

    public async Task<WeightEntry?> GetForUserAndDateAsync(string userId, DateOnly date, CancellationToken ct = default)
    {
        var entries = await _db.WeightEntries
            .Where(w => w.OwnerUserId == userId)
            .ToListAsync(ct);
        return entries.FirstOrDefault(w => w.Date == date);
    }

    public async Task<WeightEntry> AddAsync(WeightEntry entry, CancellationToken ct = default)
    {
        _db.WeightEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task UpdateAsync(WeightEntry entry, CancellationToken ct = default)
    {
        _db.WeightEntries.Update(entry);
        await _db.SaveChangesAsync(ct);
    }
}
