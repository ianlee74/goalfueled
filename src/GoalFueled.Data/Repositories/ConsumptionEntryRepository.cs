using GoalFueled.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GoalFueled.Data.Repositories;

public class ConsumptionEntryRepository : IConsumptionEntryRepository
{
    private readonly GoalFueledDbContext _db;

    public ConsumptionEntryRepository(GoalFueledDbContext db) => _db = db;

    public async Task<IReadOnlyList<ConsumptionEntry>> ListForUserAndDateAsync(
        string userId, DateOnly date, CancellationToken ct = default)
    {
        // A user's entries are few; materialize then filter/order by date in memory so this does not
        // depend on the provider's translation of DateOnly comparisons (Cosmos vs in-memory).
        var entries = await _db.ConsumptionEntries
            .Where(c => c.OwnerUserId == userId)
            .ToListAsync(ct);
        return entries
            .Where(c => c.Date == date)
            .OrderByDescending(c => c.CreatedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<ConsumptionEntry>> ListForUserInRangeAsync(
        string userId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // As above: materialize the user's (few) entries then filter the date range in memory so this
        // does not depend on the provider's translation of DateOnly comparisons (Cosmos vs in-memory).
        var entries = await _db.ConsumptionEntries
            .Where(c => c.OwnerUserId == userId)
            .ToListAsync(ct);
        return entries
            .Where(c => c.Date >= from && c.Date <= to)
            .OrderByDescending(c => c.CreatedAt)
            .ToList();
    }

    public Task<ConsumptionEntry?> GetByIdAsync(string id, CancellationToken ct = default) =>
        _db.ConsumptionEntries.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<ConsumptionEntry> AddAsync(ConsumptionEntry entry, CancellationToken ct = default)
    {
        _db.ConsumptionEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task UpdateAsync(ConsumptionEntry entry, CancellationToken ct = default)
    {
        _db.ConsumptionEntries.Update(entry);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(ConsumptionEntry entry, CancellationToken ct = default)
    {
        _db.ConsumptionEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);
    }
}
