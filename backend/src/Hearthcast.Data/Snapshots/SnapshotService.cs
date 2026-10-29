using System.Data;
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
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var household = await context.Households.Include(household => household.People)
            .SingleOrDefaultAsync(household => household.Id == householdId, cancellationToken)
            ?? throw new KeyNotFoundException("The household does not exist.");
        var items = await context.TrackedItems
            .Where(item => item.HouseholdId == householdId && !item.IsRetired)
            .Include(item => item.Group).ThenInclude(group => group.Category)
            .Include(item => item.HouseholdAllocations)
            .ToArrayAsync(cancellationToken);

        var snapshot = new Snapshot(household, snapshotDate ?? DateOnly.FromDateTime(DateTime.Today),
            datePrecision, items, values ?? []);
        context.Snapshots.Add(snapshot);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
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
