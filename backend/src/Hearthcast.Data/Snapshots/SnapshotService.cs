using Hearthcast.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Snapshots;

public sealed class SnapshotService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public async Task<Snapshot> CreateAsync(
        Guid householdId,
        DateOnly? snapshotDate = null,
        IReadOnlyCollection<ItemSnapshotValue>? values = null,
        DatePrecision datePrecision = DatePrecision.Day,
        CancellationToken cancellationToken = default)
    {
        return await SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
        {
            var household = await context.Households.Include(household => household.People)
                .SingleOrDefaultAsync(household => household.Id == householdId, token)
                ?? throw new KeyNotFoundException("The household does not exist.");
            var items = await context.TrackedItems
                .Where(item => item.HouseholdId == householdId && !item.IsRetired)
                .Include(item => item.Group).ThenInclude(group => group.Category)
                .Include(item => item.HouseholdAllocations)
                .ToArrayAsync(token);

            var snapshot = new Snapshot(household, snapshotDate ?? DateOnly.FromDateTime(DateTime.Today),
                datePrecision, items, values ?? []);
            context.Snapshots.Add(snapshot);
            return snapshot;
        }, cancellationToken);
    }

    public async Task<Snapshot> GetAsync(
        Guid householdId, Guid snapshotId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Snapshots.AsNoTrackingWithIdentityResolution().AsSplitQuery()
            .Include(snapshot => snapshot.People)
            .Include(snapshot => snapshot.ItemSnapshots).ThenInclude(item => item.HolderSnapshotPerson)
            .Include(snapshot => snapshot.ItemSnapshots)
                .ThenInclude(item => item.HouseholdAllocations).ThenInclude(allocation => allocation.SnapshotPerson)
            .SingleOrDefaultAsync(snapshot => snapshot.Id == snapshotId && snapshot.HouseholdId == householdId, cancellationToken)
            ?? throw new KeyNotFoundException("The snapshot does not belong to this household.");
    }
}
