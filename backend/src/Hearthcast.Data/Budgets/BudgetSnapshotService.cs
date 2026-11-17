using System.Data;
using Hearthcast.Data.Households;
using Hearthcast.Data.Persistence;
using Hearthcast.Data.Persistence.Configurations;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hearthcast.Data.Budgets;

public sealed class BudgetSnapshotService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public const int DefaultHistoryPageSize = 25;
    public const int MaximumHistoryPageSize = 100;

    public Task<BudgetSnapshotCaptureResult> CreateAsync(Guid householdId, Guid snapshotId, BudgetSnapshotValues values,
        CancellationToken cancellationToken = default)
    {
        var request = BudgetCreateRequest.Freeze(householdId, snapshotId, values);
        return CreateFrozenAsync(householdId, snapshotId, request.Values, request.Hash, cancellationToken);
    }

    private async Task<BudgetSnapshotCaptureResult> CreateFrozenAsync(Guid householdId, Guid snapshotId,
        BudgetSnapshotValues values, string hash, CancellationToken token)
    {
        try
        {
            return await WriteAsync(async (context, _, cancellation) =>
            {
                var home = await FindHouseholdAsync(context, householdId, cancellation);
                var existing = await Graph(context).SingleOrDefaultAsync(s => s.HouseholdId == householdId && s.Id == snapshotId, cancellation);
                if (existing is not null) return Replay(existing, hash);
                if (await context.BudgetSnapshots.AnyAsync(s => s.Id == snapshotId, cancellation)) throw CreateConflict();
                await RequireAvailableDateAsync(context, householdId, values.EffectiveFrom, null, cancellation);
                var template = values.CopiedFromBudgetSnapshotId.HasValue
                    ? await FindAsync(context, householdId, values.CopiedFromBudgetSnapshotId.Value, cancellation) : null;
                if (template is not null) RequireEarlier(template, snapshotId, values.EffectiveFrom);
                // Attempt-local batches: an explicit source shared by many entries is loaded only once.
                var references = await LoadCaptureReferencesAsync(context, householdId, values.Entries, template, cancellation);
                var inputs = new List<BudgetEntryInput>();
                foreach (var entry in values.Entries)
                    inputs.Add(await ResolveAsync(context, householdId, snapshotId, values.EffectiveFrom, entry, null, template, cancellation, references));
                var plan = new BudgetSnapshot(snapshotId, home, values.EffectiveFrom, inputs, hash, template, values.Notes);
                context.BudgetSnapshots.Add(plan);
                return new BudgetSnapshotCaptureResult(plan, true);
            }, token, retryCreate: true);
        }
        catch (DbUpdateException error) when (IsDateConflict(error))
        {
            // A concurrent identical UUID can lose either the PK or date index race. Both recognise the retained create.
            await using var read = await contextFactory.CreateDbContextAsync(token);
            var saved = await Graph(read).AsNoTracking().SingleOrDefaultAsync(s => s.HouseholdId == householdId && s.Id == snapshotId, token);
            if (saved is not null) return Replay(saved, hash);
            throw DateConflict();
        }
    }

    public async Task<BudgetSnapshot> GetAsync(Guid householdId, Guid snapshotId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await FindAsync(context, householdId, snapshotId, cancellationToken);
    }

    public async Task<BudgetSnapshotHistoryPage> ListAsync(Guid householdId, DateOnly? before = null,
        int pageSize = DefaultHistoryPageSize, CancellationToken cancellationToken = default)
    {
        if (before.HasValue) BudgetRules.Date(before.Value);
        if (pageSize is < 1 or > MaximumHistoryPageSize) throw new InvalidValueException("Use a page size from 1 to 100.");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await FindHouseholdAsync(context, householdId, cancellationToken);
        var plans = await Graph(context).AsNoTracking().Where(s => s.HouseholdId == householdId && (!before.HasValue || s.EffectiveFrom < before.Value))
            .OrderByDescending(s => s.EffectiveFrom).Take(pageSize + 1).ToArrayAsync(cancellationToken);
        var selected = plans.Take(pageSize).ToArray();
        return new(selected, plans.Length > pageSize ? selected[^1].EffectiveFrom : null, pageSize);
    }

    public async Task<BudgetSnapshot?> FindCopySourceAsync(Guid householdId, DateOnly effectiveFrom,
        CancellationToken cancellationToken = default)
    {
        BudgetRules.Date(effectiveFrom);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await FindHouseholdAsync(context, householdId, cancellationToken);
        return await Graph(context).AsNoTracking().Where(s => s.HouseholdId == householdId && s.EffectiveFrom < effectiveFrom)
            .OrderByDescending(s => s.EffectiveFrom).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<BudgetEffectivePlan?> GetEffectiveAsync(Guid householdId, DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        BudgetRules.Date(asOf);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await FindHouseholdAsync(context, householdId, cancellationToken);
        var result = await EffectiveAsync(context, householdId, asOf, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<BudgetCurrentOutgoings> GetCurrentOutgoingsAsync(Guid householdId, DateOnly asOf,
        bool includeRetired = true, CancellationToken cancellationToken = default)
    {
        BudgetRules.Date(asOf);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await FindHouseholdAsync(context, householdId, cancellationToken);
        var effective = await EffectiveAsync(context, householdId, asOf, cancellationToken);
        var items = await context.BudgetItems.AsNoTracking().Where(i => i.HouseholdId == householdId && (includeRetired || !i.IsRetired))
            .OrderBy(i => i.Name).ThenBy(i => i.Code).ThenBy(i => i.Id).ToArrayAsync(cancellationToken);
        var entries = effective?.Snapshot.Entries.ToDictionary(e => e.BudgetItemId) ?? [];
        var result = new BudgetCurrentOutgoings(asOf, effective?.Snapshot.Id, effective?.Snapshot.EffectiveFrom,
            effective?.EffectiveUntilExclusive, effective?.Snapshot.IsComplete,
            items.Select(i => new BudgetCurrentOutgoing(i, entries.GetValueOrDefault(i.Id))).ToArray());
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public Task<BudgetSnapshot> CorrectAmountAsync(Guid householdId, Guid snapshotId, Guid expectedRevision, Guid entryId,
        decimal? amount, DateOnly? confirmedOn, string? notes, CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, _, token) =>
        {
            var plan = await FindEditableAsync(context, householdId, snapshotId, expectedRevision, token);
            FindEntry(plan, entryId);
            plan.CorrectAmount(expectedRevision, entryId, amount, confirmedOn, notes);
            return plan;
        }, cancellationToken);

    public Task<BudgetSnapshot> CorrectEntryMetadataAsync(Guid householdId, Guid snapshotId, Guid expectedRevision,
        Guid entryId, BudgetEntryMetadataValues metadata, CancellationToken cancellationToken = default)
    {
        var copy = BudgetCreateRequest.Metadata(metadata);
        return WriteAsync(async (context, _, token) =>
        {
            var plan = await FindEditableAsync(context, householdId, snapshotId, expectedRevision, token);
            var entry = FindEntry(plan, entryId);
            var resolved = await ResolveMetadataAsync(context, householdId, entry.BudgetItemId, copy, token);
            plan.CorrectEntryMetadata(expectedRevision, entryId, resolved);
            return plan;
        }, cancellationToken);
    }

    public Task<BudgetSnapshot> CorrectEntryAsync(Guid householdId, Guid snapshotId, Guid expectedRevision,
        Guid entryId, BudgetEntryValues values, CancellationToken cancellationToken = default)
    {
        var copy = BudgetCreateRequest.Entry(values);
        return WriteAsync(async (context, observed, token) =>
        {
            var plan = await FindEditableAsync(context, householdId, snapshotId, expectedRevision, token);
            var entry = FindEntry(plan, entryId);
            var input = await ResolveAsync(context, householdId, snapshotId, plan.EffectiveFrom, copy, entry, null, token);
            plan.CorrectEntry(expectedRevision, entryId, input);
            await DetachIncomingAsync(context, plan, [entryId], [], observed, token);
            return plan;
        }, cancellationToken);
    }

    public Task<BudgetSnapshot> AddEntryAsync(Guid householdId, Guid snapshotId, Guid expectedRevision,
        BudgetEntryValues values, CancellationToken cancellationToken = default)
    {
        var copy = BudgetCreateRequest.Entry(values);
        return WriteAsync(async (context, _, token) =>
        {
            var plan = await FindEditableAsync(context, householdId, snapshotId, expectedRevision, token);
            var input = await ResolveAsync(context, householdId, snapshotId, plan.EffectiveFrom, copy, null, null, token);
            plan.AddEntry(expectedRevision, input);
            context.BudgetEntries.Add(plan.Entries[^1]);
            return plan;
        }, cancellationToken);
    }

    public Task<BudgetSnapshot> RemoveEntryAsync(Guid householdId, Guid snapshotId, Guid expectedRevision, Guid entryId,
        CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, observed, token) =>
        {
            var plan = await FindEditableAsync(context, householdId, snapshotId, expectedRevision, token);
            FindEntry(plan, entryId);
            var detached = await DetachIncomingAsync(context, plan, [entryId], [entryId], observed, token);
            if (detached) await context.SaveChangesAsync(token); // Restrictive carry FKs clear before removal, inside this transaction.
            context.BudgetEntries.Remove(plan.RemoveEntry(expectedRevision, entryId));
            return plan;
        }, cancellationToken);

    public Task<BudgetSnapshot> CorrectHeaderAsync(Guid householdId, Guid snapshotId, Guid expectedRevision,
        DateOnly effectiveFrom, string? notes, CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, observed, token) =>
        {
            var plan = await FindEditableAsync(context, householdId, snapshotId, expectedRevision, token);
            BudgetRules.Date(effectiveFrom);
            await RequireAvailableDateAsync(context, householdId, effectiveFrom, snapshotId, token);
            if (effectiveFrom != plan.EffectiveFrom)
            {
                var ids = plan.Entries.Where(e => e.CarriedFromBudgetEntryId.HasValue).Select(e => e.CarriedFromBudgetEntryId!.Value).ToArray();
                var sources = await Graph(context).Where(s => s.HouseholdId == householdId
                    && (s.Id == plan.CopiedFromBudgetSnapshotId || s.Entries.Any(e => ids.Contains(e.Id)))).ToArrayAsync(token);
                var sourceEntries = sources.SelectMany(s => s.Entries.Select(e => (Source: s, Entry: e))).ToDictionary(x => x.Entry.Id);
                var invalid = plan.Entries.Where(e => e.CarriedFromBudgetEntryId.HasValue)
                    .Where(e => !sourceEntries.TryGetValue(e.CarriedFromBudgetEntryId!.Value, out var source)
                        || !Compatible(source.Source, source.Entry, plan, e, effectiveFrom)).Select(e => e.Id).ToArray();
                var clearTemplate = plan.CopiedFromBudgetSnapshotId.HasValue
                    && !sources.Any(s => s.Id == plan.CopiedFromBudgetSnapshotId && s.EffectiveFrom < effectiveFrom);
                plan.DetachSources(plan.Revision, invalid, clearTemplate);
                plan.CorrectHeader(plan.Revision, effectiveFrom, notes, sources);
                await DetachIncomingAsync(context, plan, plan.Entries.Select(e => e.Id).ToArray(), [], observed, token);
            }
            else plan.CorrectHeader(expectedRevision, effectiveFrom, notes);
            return plan;
        }, cancellationToken);

    private static async Task<BudgetEntryInput> ResolveAsync(HearthcastDbContext context, Guid householdId,
        Guid receivingId, DateOnly effectiveFrom, BudgetEntryValues values, BudgetEntry? existing,
        BudgetSnapshot? template, CancellationToken token, CaptureReferences? references = null)
    {
        var item = references is not null ? references.Items[values.BudgetItemId]
            : await context.BudgetItems.SingleOrDefaultAsync(i => i.HouseholdId == householdId && i.Id == values.BudgetItemId, token)
            ?? throw new ResourceNotFoundException("The budget item does not belong to this household.");
        BudgetSnapshot? sourcePlan = null;
        BudgetEntry? sourceEntry = null;
        if (values.Source is not null)
        {
            sourcePlan = references is not null ? references.Sources[values.Source.SnapshotId]
                : await FindAsync(context, householdId, values.Source.SnapshotId, token);
            sourceEntry = FindEntry(sourcePlan, values.Source.EntryId);
        }
        else if (template is not null)
        {
            sourceEntry = template.Entries.SingleOrDefault(e => e.BudgetItemId == item.Id);
            if (sourceEntry is not null) sourcePlan = template;
        }
        if (sourcePlan is not null)
        {
            RequireEarlier(sourcePlan, receivingId, effectiveFrom);
            if (sourceEntry!.BudgetItemId != item.Id) throw new InvalidValueException("The source entry must refer to the selected budget item.");
        }
        BudgetEntryMetadata metadata;
        if (values.Metadata is not null)
            metadata = await ResolveMetadataAsync(context, householdId, item.Id, values.Metadata, token, references);
        else if (values.RefreshFromSetup || (sourceEntry is null && (existing is null || existing.BudgetItemId != item.Id)))
        {
            var category = references is not null ? references.Categories[item.ExpenseCategoryId]
                : await FindCategoryAsync(context, householdId, item.ExpenseCategoryId, token);
            var merchant = !item.MerchantId.HasValue ? null : references is not null
                ? references.Merchants[item.MerchantId.Value] : await FindMerchantAsync(context, householdId, item.MerchantId.Value, token);
            metadata = BudgetEntryMetadata.FromSetup(item, category, merchant);
        }
        else metadata = BudgetEntryMetadata.FromEntry(sourceEntry ?? existing!);
        if (values.Mode == BudgetEntryValueMode.Carry)
        {
            if (sourcePlan is null || sourceEntry is null) throw new InvalidValueException("Carry requires an explicitly resolved earlier source entry.");
            return new(metadata, CarrySourceSnapshot: sourcePlan, CarrySourceEntryId: sourceEntry.Id);
        }
        return new(metadata, values.PlannedAmount, values.ConfirmedOn, SavedCurrencyContext: sourceEntry);
    }

    private static async Task<BudgetEntryMetadata> ResolveMetadataAsync(HearthcastDbContext context, Guid householdId,
        Guid itemId, BudgetEntryMetadataValues value, CancellationToken token, CaptureReferences? references = null)
    {
        if (references is null)
        {
            await FindCategoryAsync(context, householdId, value.ExpenseCategoryId, token);
            if (value.MerchantId.HasValue) await FindMerchantAsync(context, householdId, value.MerchantId.Value, token);
        }
        return new(householdId, itemId, value.ItemCode, value.ItemName, value.ExpenseCategoryId, value.ExpenseCategoryName,
            value.CurrencyCode, value.Frequency, value.IntervalCount, value.MerchantId, value.MerchantName, value.Notes);
    }

    // The dictionaries never escape this serializable attempt. Missing/foreign IDs are rejected
    // before entry resolution, and a retry reloads both setup and saved source aggregates.
    private static async Task<CaptureReferences> LoadCaptureReferencesAsync(HearthcastDbContext context, Guid householdId,
        IReadOnlyList<BudgetEntryValues> values, BudgetSnapshot? template, CancellationToken token)
    {
        var itemIds = values.Select(e => e.BudgetItemId).Distinct().ToArray();
        var items = itemIds.Length == 0 ? new Dictionary<Guid, BudgetItem>() : await context.BudgetItems
            .Where(i => i.HouseholdId == householdId && itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, token);
        if (items.Count != itemIds.Length) throw new ResourceNotFoundException("A budget item does not belong to this household.");

        var sources = new Dictionary<Guid, BudgetSnapshot>();
        if (template is not null) sources.Add(template.Id, template);
        var sourceIds = values.Where(e => e.Source is not null).Select(e => e.Source!.SnapshotId)
            .Distinct().Where(id => !sources.ContainsKey(id)).ToArray();
        if (sourceIds.Length > 0)
        {
            var loaded = await Graph(context).Where(s => s.HouseholdId == householdId && sourceIds.Contains(s.Id)).ToArrayAsync(token);
            if (loaded.Length != sourceIds.Length) throw new ResourceNotFoundException("A source budget plan does not belong to this household.");
            foreach (var source in loaded) sources.Add(source.Id, source);
        }

        var categoryIds = items.Values.Select(i => i.ExpenseCategoryId)
            .Concat(values.Where(e => e.Metadata is not null).Select(e => e.Metadata!.ExpenseCategoryId)).Distinct().ToArray();
        var categories = categoryIds.Length == 0 ? new Dictionary<Guid, ExpenseCategory>() : await context.ExpenseCategories
            .Where(c => c.HouseholdId == householdId && categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, token);
        if (categories.Count != categoryIds.Length) throw new ResourceNotFoundException("An expense category does not belong to this household.");
        var merchantIds = items.Values.Select(i => i.MerchantId).Concat(values.Select(e => e.Metadata?.MerchantId))
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        var merchants = merchantIds.Length == 0 ? new Dictionary<Guid, Merchant>() : await context.Merchants
            .Where(m => m.HouseholdId == householdId && merchantIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, token);
        if (merchants.Count != merchantIds.Length) throw new ResourceNotFoundException("A merchant does not belong to this household.");
        return new(items, sources, categories, merchants);
    }

    private sealed record CaptureReferences(IReadOnlyDictionary<Guid, BudgetItem> Items,
        IReadOnlyDictionary<Guid, BudgetSnapshot> Sources, IReadOnlyDictionary<Guid, ExpenseCategory> Categories,
        IReadOnlyDictionary<Guid, Merchant> Merchants);

    private static async Task<bool> DetachIncomingAsync(HearthcastDbContext context, BudgetSnapshot source,
        Guid[] changedEntryIds, Guid[] removedEntryIds, ObservedRevisions observed, CancellationToken token)
    {
        var receivers = await Graph(context).Where(s => s.HouseholdId == source.HouseholdId && s.Id != source.Id
            && (s.CopiedFromBudgetSnapshotId == source.Id || s.Entries.Any(e => e.CarriedFromBudgetEntryId.HasValue
                && changedEntryIds.Contains(e.CarriedFromBudgetEntryId.Value)))).ToArrayAsync(token);
        var sourceEntries = source.Entries.ToDictionary(e => e.Id);
        var changed = false;
        foreach (var receiver in receivers)
        {
            var ids = receiver.Entries.Where(e => e.CarriedFromBudgetEntryId.HasValue && changedEntryIds.Contains(e.CarriedFromBudgetEntryId.Value))
                .Where(e => removedEntryIds.Contains(e.CarriedFromBudgetEntryId!.Value)
                    || !sourceEntries.TryGetValue(e.CarriedFromBudgetEntryId.Value, out var original)
                    || !Compatible(source, original, receiver, e, receiver.EffectiveFrom)).Select(e => e.Id).ToArray();
            var template = receiver.CopiedFromBudgetSnapshotId == source.Id && source.EffectiveFrom >= receiver.EffectiveFrom;
            if (ids.Length == 0 && !template) continue;
            observed.Observe(receiver);
            receiver.DetachSources(receiver.Revision, ids, template);
            changed = true;
        }
        return changed;
    }

    private static bool Compatible(BudgetSnapshot source, BudgetEntry original, BudgetSnapshot receiver, BudgetEntry entry, DateOnly receivingDate) =>
        source.Id != receiver.Id && source.HouseholdId == receiver.HouseholdId && source.EffectiveFrom < receivingDate
        && original.BudgetItemId == entry.BudgetItemId && original.CurrencyCode == entry.CurrencyCode
        && original.Frequency == entry.Frequency && original.IntervalCount == entry.IntervalCount;

    private static async Task<BudgetEffectivePlan?> EffectiveAsync(HearthcastDbContext context, Guid householdId, DateOnly asOf, CancellationToken token)
    {
        var plan = await Graph(context).AsNoTracking().Where(s => s.HouseholdId == householdId && s.EffectiveFrom <= asOf)
            .OrderByDescending(s => s.EffectiveFrom).FirstOrDefaultAsync(token);
        if (plan is null) return null;
        var until = await context.BudgetSnapshots.Where(s => s.HouseholdId == householdId && s.EffectiveFrom > plan.EffectiveFrom)
            .OrderBy(s => s.EffectiveFrom).Select(s => (DateOnly?)s.EffectiveFrom).FirstOrDefaultAsync(token);
        return new(plan, until);
    }

    private async Task<T> WriteAsync<T>(Func<HearthcastDbContext, ObservedRevisions, CancellationToken, Task<T>> operation,
        CancellationToken token, bool retryCreate = false)
    {
        var observed = new ObservedRevisions();
        try
        {
            return await SerializableTransaction.ExecuteAsync(contextFactory,
                async (context, cancellation) =>
                {
                    await observed.ValidateAsync(context, cancellation);
                    return await operation(context, observed, cancellation);
                }, token, retryCreate ? "PK_BudgetSnapshots" : null);
        }
        catch (DbUpdateConcurrencyException error) { throw new BudgetSnapshotConflictException(error); }
        catch (DbUpdateException error) when (IsDateConflict(error) && !retryCreate) { throw DateConflict(); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_BudgetSnapshots" }) { throw CreateConflict(); }
    }

    private static bool IsDateConflict(DbUpdateException error) => error.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: BudgetSnapshotConfiguration.EffectiveDateIndexName };
    private static BusinessRuleViolationException DateConflict() => new("A budget plan already starts on this date in this household.");
    private static BusinessRuleViolationException CreateConflict() => new("The plan identifier cannot be used for this create request.");
    private static BudgetSnapshotCaptureResult Replay(BudgetSnapshot saved, string hash) => saved.CreationRequestHash == hash
        ? new(saved, false) : throw CreateConflict();

    private static void RequireEarlier(BudgetSnapshot source, Guid receiverId, DateOnly effectiveFrom)
    {
        if (source.Id == receiverId || source.EffectiveFrom >= effectiveFrom)
            throw new InvalidValueException("Select a strictly earlier source plan.");
    }

    private static async Task RequireAvailableDateAsync(HearthcastDbContext context, Guid householdId, DateOnly date,
        Guid? excludingId, CancellationToken token)
    {
        if (await context.BudgetSnapshots.AnyAsync(s => s.HouseholdId == householdId && s.EffectiveFrom == date && s.Id != excludingId, token))
            throw DateConflict();
    }

    private static IQueryable<BudgetSnapshot> Graph(HearthcastDbContext context) => context.BudgetSnapshots
        .Include(s => s.Entries.OrderBy(e => e.ItemCode).ThenBy(e => e.Id)).AsSingleQuery();
    private static async Task<BudgetSnapshot> FindAsync(HearthcastDbContext context, Guid householdId, Guid id, CancellationToken token) =>
        await Graph(context).SingleOrDefaultAsync(s => s.HouseholdId == householdId && s.Id == id, token)
        ?? throw new ResourceNotFoundException("The budget plan does not belong to this household.");
    private static BudgetEntry FindEntry(BudgetSnapshot plan, Guid id) => plan.Entries.SingleOrDefault(e => e.Id == id)
        ?? throw new ResourceNotFoundException("The entry does not belong to this budget plan.");
    private static async Task<BudgetSnapshot> FindEditableAsync(HearthcastDbContext context, Guid householdId, Guid id, Guid revision, CancellationToken token)
    {
        var plan = await FindAsync(context, householdId, id, token);
        if (revision == Guid.Empty) throw new InvalidValueException("Supply the saved budget revision.");
        if (revision != plan.Revision) throw new BudgetSnapshotConflictException();
        return plan;
    }
    private static async Task<Household> FindHouseholdAsync(HearthcastDbContext context, Guid id, CancellationToken token) =>
        await context.Households.SingleOrDefaultAsync(h => h.Id == id, token) ?? throw new ResourceNotFoundException("The household does not exist.");
    private static async Task<ExpenseCategory> FindCategoryAsync(HearthcastDbContext context, Guid householdId, Guid id, CancellationToken token) =>
        await context.ExpenseCategories.SingleOrDefaultAsync(c => c.HouseholdId == householdId && c.Id == id, token)
        ?? throw new ResourceNotFoundException("The category does not belong to this household.");
    private static async Task<Merchant> FindMerchantAsync(HearthcastDbContext context, Guid householdId, Guid id, CancellationToken token) =>
        await context.Merchants.SingleOrDefaultAsync(m => m.HouseholdId == householdId && m.Id == id, token)
        ?? throw new ResourceNotFoundException("The merchant does not belong to this household.");

    // A retry may reload a descendant whose link was removed by another edit. Retain the first
    // observed revision so even that disappearing dependency cannot silently change the operation.
    private sealed class ObservedRevisions
    {
        private readonly Dictionary<Guid, Guid> _values = [];
        private Guid _householdId;

        internal void Observe(BudgetSnapshot plan)
        {
            if (_values.TryGetValue(plan.Id, out var revision) && revision != plan.Revision)
                throw new BudgetSnapshotConflictException();
            _householdId = plan.HouseholdId;
            _values.TryAdd(plan.Id, plan.Revision);
        }

        internal async Task ValidateAsync(HearthcastDbContext context, CancellationToken token)
        {
            if (_values.Count == 0) return;
            var ids = _values.Keys.ToArray();
            var current = await context.BudgetSnapshots.Where(s => s.HouseholdId == _householdId && ids.Contains(s.Id))
                .Select(s => new { s.Id, s.Revision }).ToArrayAsync(token);
            if (current.Length != _values.Count || current.Any(s => _values[s.Id] != s.Revision))
                throw new BudgetSnapshotConflictException();
        }
    }
}
