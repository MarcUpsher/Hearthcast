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

    public Task<Snapshot> CorrectValuesAsync(
        Guid householdId,
        Guid snapshotId,
        Guid expectedRevision,
        IReadOnlyCollection<ItemSnapshotValue>? values = null,
        IReadOnlyCollection<FinancedAssetSnapshotValue>? financedAssetValues = null,
        CancellationToken cancellationToken = default)
    {
        return AmendAsync(householdId, snapshotId, expectedRevision, values, financedAssetValues, null, cancellationToken);
    }

    public async Task<Snapshot> AmendAsync(Guid householdId, Guid snapshotId, Guid expectedRevision,
        IReadOnlyCollection<ItemSnapshotValue>? values, IReadOnlyCollection<FinancedAssetSnapshotValue>? financedAssetValues,
        IReadOnlyCollection<ItemSnapshotValue>? addedItems, CancellationToken cancellationToken = default)
    {
        if (values is null && financedAssetValues is null && (addedItems?.Count ?? 0) == 0)
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
                if (snapshot.Revision != expectedRevision) throw new SnapshotConflictException();
                if (addedItems is { Count: > 0 })
                {
                    var ids = addedItems.Select(value => value.TrackedItemId).ToArray();
                    if (ids.Distinct().Count() != ids.Length || (values ?? []).Any(value => ids.Contains(value.TrackedItemId)))
                        throw new InvalidValueException("Each item must appear only once across additions and corrections.");
                    var items = await context.TrackedItems.Where(item => item.HouseholdId == householdId && ids.Contains(item.Id) && !item.IsRetired)
                        .Include(item => item.Group).ThenInclude(group => group.Category)
                        .Include(item => item.HouseholdAllocations).ToArrayAsync(token);
                    if (items.Length != ids.Length)
                        throw new InvalidValueException("Only active items in this household can be added.");
                    var people = await context.People.Where(person => person.HouseholdId == householdId).ToArrayAsync(token);
                    var savedPeople = snapshot.People.Select(person => person.Id).ToHashSet();
                    snapshot.AddItems(items, addedItems, people);
                    context.SnapshotPeople.AddRange(snapshot.People.Where(person => !savedPeople.Contains(person.Id)));
                    context.ItemSnapshots.AddRange(snapshot.ItemSnapshots.Where(item => ids.Contains(item.TrackedItemId)));
                }
                if ((values?.Count ?? 0) + (financedAssetValues?.Count ?? 0) > 0 || (addedItems?.Count ?? 0) == 0)
                    snapshot.CorrectValues(snapshot.Revision, values ?? [], financedAssetValues ?? []);
                return snapshot;
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new SnapshotConflictException(exception);
        }
    }

    public Task<Snapshot> InitializeHistoricalExchangeRatesAsync(Guid householdId, Guid snapshotId,
        Guid expectedRevision, CancellationToken cancellationToken = default) =>
        SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
        {
            var snapshot = await SnapshotGraph(context)
                .SingleOrDefaultAsync(s => s.Id == snapshotId && s.HouseholdId == householdId, token)
                ?? throw new ResourceNotFoundException("The snapshot does not belong to this household.");
            if (snapshot.Revision != expectedRevision) throw new SnapshotConflictException();
            if (snapshot.ExchangeRateLookupDate.HasValue) return snapshot;
            var lookupDate = snapshot.DatePrecision == DatePrecision.Month
                ? new DateOnly(snapshot.SnapshotDate.Year, snapshot.SnapshotDate.Month, 1) : snapshot.SnapshotDate;
            snapshot.InitializeExchangeRateContext(lookupDate);
            context.Entry(snapshot).Property(s => s.Revision).CurrentValue = Guid.NewGuid();
            context.Entry(snapshot).Property(s => s.UpdatedAtUtc).CurrentValue = DateTimeOffset.UtcNow;
            return snapshot;
        }, cancellationToken);

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
            snapshot.InitializeExchangeRateContext(DateOnly.FromDateTime(DateTime.UtcNow));
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
        return context.Snapshots.AsSplitQuery().Include(snapshot => snapshot.ExchangeRates)
            .Include(snapshot => snapshot.People)
            .Include(snapshot => snapshot.ItemSnapshots).ThenInclude(item => item.HolderSnapshotPerson)
            .Include(snapshot => snapshot.ItemSnapshots)
                .ThenInclude(item => item.HouseholdAllocations).ThenInclude(allocation => allocation.SnapshotPerson)
            .Include(snapshot => snapshot.FinancedAssetSnapshots).ThenInclude(asset => asset.Owners).ThenInclude(row => row.SnapshotPerson)
            .Include(snapshot => snapshot.FinancedAssetSnapshots).ThenInclude(asset => asset.LoanHolders).ThenInclude(row => row.SnapshotPerson)
            .Include(snapshot => snapshot.FinancedAssetSnapshots).ThenInclude(asset => asset.HouseholdAllocations).ThenInclude(row => row.SnapshotPerson);
    }
}
