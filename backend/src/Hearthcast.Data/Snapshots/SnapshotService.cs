using System.Data;
using Hearthcast.Data.Validation;
using Hearthcast.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Snapshots;

public sealed class SnapshotService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public Task<Snapshot> CreateAsync(
        Guid householdId,
        DateOnly? snapshotDate = null,
        IReadOnlyCollection<ItemSnapshotValue>? values = null,
        DatePrecision datePrecision = DatePrecision.Day,
        IReadOnlyCollection<FinancedAssetSnapshotValue>? financedAssetValues = null,
        CancellationToken cancellationToken = default)
    {
        return CaptureAsync(householdId, snapshotDate, values, datePrecision, false, null, financedAssetValues, cancellationToken);
    }

    public Task<Snapshot> CreateNextAsync(
        Guid householdId,
        DateOnly? snapshotDate = null,
        IReadOnlyCollection<ItemSnapshotValue>? values = null,
        DatePrecision datePrecision = DatePrecision.Day,
        Guid? sourceSnapshotId = null,
        IReadOnlyCollection<FinancedAssetSnapshotValue>? financedAssetValues = null,
        CancellationToken cancellationToken = default)
    {
        return CaptureAsync(householdId, snapshotDate, values, datePrecision, true, sourceSnapshotId, financedAssetValues, cancellationToken);
    }

    public async Task<Snapshot> CorrectValuesAsync(
        Guid householdId,
        Guid snapshotId,
        Guid expectedRevision,
        IReadOnlyCollection<ItemSnapshotValue>? values = null,
        IReadOnlyCollection<FinancedAssetSnapshotValue>? financedAssetValues = null,
        CancellationToken cancellationToken = default)
    {
        if (values is null && financedAssetValues is null)
        {
            throw new ArgumentNullException(nameof(values));
        }
        try
        {
            return await SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
            {
                var snapshot = await SnapshotGraph(context)
                    .SingleOrDefaultAsync(snapshot => snapshot.Id == snapshotId && snapshot.HouseholdId == householdId, token)
                    ?? throw new ResourceNotFoundException("The snapshot does not belong to this household.");
                snapshot.CorrectValues(expectedRevision, values ?? [], financedAssetValues ?? []);
                return snapshot;
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new SnapshotConflictException(exception);
        }
    }

    public async Task<Snapshot> GetAsync(
        Guid householdId, Guid snapshotId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        // Split queries must share a database view when corrections can run concurrently.
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        var snapshot = await SnapshotGraph(context).AsNoTrackingWithIdentityResolution()
            .SingleOrDefaultAsync(snapshot => snapshot.Id == snapshotId && snapshot.HouseholdId == householdId, cancellationToken)
            ?? throw new ResourceNotFoundException("The snapshot does not belong to this household.");
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    public async Task<IReadOnlyList<SnapshotInventoryEntry>> ListAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.Households.AnyAsync(row => row.Id == householdId, cancellationToken))
            throw new ResourceNotFoundException("The household does not exist.");
        return await context.Snapshots.AsNoTracking().Where(row => row.HouseholdId == householdId)
            .OrderByDescending(row => row.SnapshotDate).ThenBy(row => row.Id)
            .Select(row => new SnapshotInventoryEntry(row.Id, row.SnapshotDate, row.DatePrecision, row.Revision))
            .ToArrayAsync(cancellationToken);
    }

    private Task<Snapshot> CaptureAsync(
        Guid householdId, DateOnly? snapshotDate, IReadOnlyCollection<ItemSnapshotValue>? values,
        DatePrecision datePrecision, bool carryForward, Guid? sourceSnapshotId,
        IReadOnlyCollection<FinancedAssetSnapshotValue>? financedAssetValues, CancellationToken cancellationToken)
    {
        var date = snapshotDate ?? DateOnly.FromDateTime(DateTime.Today);
        var period = SnapshotPeriod.From(date, datePrecision);
        return SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
        {
            var household = await context.Households.Include(household => household.People)
                .SingleOrDefaultAsync(household => household.Id == householdId, token)
                ?? throw new ResourceNotFoundException("The household does not exist.");
            var items = await context.TrackedItems
                .Where(item => item.HouseholdId == householdId && !item.IsRetired)
                .Include(item => item.Group).ThenInclude(group => group.Category)
                .Include(item => item.HouseholdAllocations)
                .ToArrayAsync(token);
            var assets = await context.FinancedAssets.Where(asset => asset.HouseholdId == householdId && !asset.IsRetired)
                .AsSplitQuery().Include(asset => asset.Group).ThenInclude(group => group.Category)
                .Include(asset => asset.Owners).Include(asset => asset.LoanHolders).Include(asset => asset.HouseholdAllocations)
                .ToArrayAsync(token);
            var source = carryForward
                ? await ResolveSourceAsync(context, householdId, period, sourceSnapshotId, token)
                : null;
            var snapshot = new Snapshot(household, date, datePrecision, items, values ?? [], assets, financedAssetValues ?? [], source);
            context.Snapshots.Add(snapshot);
            return snapshot;
        }, cancellationToken);
    }

    private static async Task<Snapshot?> ResolveSourceAsync(
        HearthcastDbContext context, Guid householdId, SnapshotPeriod target, Guid? sourceSnapshotId,
        CancellationToken cancellationToken)
    {
        if (!sourceSnapshotId.HasValue)
        {
            var candidates = await context.Snapshots.AsNoTracking()
                .Where(snapshot => snapshot.HouseholdId == householdId && snapshot.SnapshotDate <= target.End)
                .ToArrayAsync(cancellationToken);
            if (candidates.Length == 0)
            {
                return null;
            }

            var periods = candidates.Select(snapshot =>
                (snapshot.Id, Period: SnapshotPeriod.From(snapshot.SnapshotDate, snapshot.DatePrecision))).ToArray();
            var latest = periods.MaxBy(candidate => candidate.Period.End);
            if (latest.Period.End >= target.Start
                || periods.Any(candidate => candidate.Id != latest.Id && candidate.Period.End >= latest.Period.Start))
            {
                throw new BusinessRuleViolationException("Snapshot ordering is ambiguous. Select the source snapshot explicitly.");
            }

            sourceSnapshotId = latest.Id;
        }

        var source = await context.Snapshots.AsNoTracking().AsSplitQuery().Include(snapshot => snapshot.ItemSnapshots)
            .Include(snapshot => snapshot.FinancedAssetSnapshots)
            .SingleOrDefaultAsync(snapshot => snapshot.Id == sourceSnapshotId && snapshot.HouseholdId == householdId, cancellationToken)
            ?? throw new ResourceNotFoundException("The source snapshot does not belong to this household.");
        if (source.SnapshotDate > target.End)
        {
            throw new InvalidValueException("A definitely future snapshot cannot be a carry source.", nameof(sourceSnapshotId));
        }

        return source;
    }

    private static IQueryable<Snapshot> SnapshotGraph(HearthcastDbContext context)
    {
        return context.Snapshots.AsSplitQuery()
            .Include(snapshot => snapshot.People)
            .Include(snapshot => snapshot.ItemSnapshots).ThenInclude(item => item.HolderSnapshotPerson)
            .Include(snapshot => snapshot.ItemSnapshots)
                .ThenInclude(item => item.HouseholdAllocations).ThenInclude(allocation => allocation.SnapshotPerson)
            .Include(snapshot => snapshot.FinancedAssetSnapshots).ThenInclude(asset => asset.Owners).ThenInclude(row => row.SnapshotPerson)
            .Include(snapshot => snapshot.FinancedAssetSnapshots).ThenInclude(asset => asset.LoanHolders).ThenInclude(row => row.SnapshotPerson)
            .Include(snapshot => snapshot.FinancedAssetSnapshots).ThenInclude(asset => asset.HouseholdAllocations).ThenInclude(row => row.SnapshotPerson);
    }
}
