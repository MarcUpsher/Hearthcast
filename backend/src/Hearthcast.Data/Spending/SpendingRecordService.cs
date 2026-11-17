using Hearthcast.Data.Persistence;
using Hearthcast.Data.Persistence.Configurations;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hearthcast.Data.Spending;

public sealed class SpendingRecordService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public Task<SpendingRecordCaptureResult> CreateAsync(Guid householdId, Guid recordId, SpendingRecordValues values,
        IReadOnlyList<RememberSpendingPrice>? rememberPrices = null, CancellationToken cancellationToken = default)
    {
        var request = SpendingCreateRequest.Freeze(householdId, recordId, values, rememberPrices);
        return WriteAsync(async (context, token) =>
        {
            await RequireHouseholdAsync(context, householdId, token);
            var existing = await Graph(context).SingleOrDefaultAsync(r => r.HouseholdId == householdId && r.Id == recordId, token);
            if (existing is not null)
            {
                if (existing.CreationRequestHash != request.Hash) throw CreateConflict();
                return new SpendingRecordCaptureResult(existing, false);
            }
            if (await context.SpendingRecords.AnyAsync(r => r.Id == recordId, token)) throw CreateConflict();
            var merchant = await FindMerchantAsync(context, householdId, request.Values.MerchantId, token);
            var lines = await ResolveLinesAsync(context, householdId, request.Values.Lines, token);
            var original = request.Values.OriginalPaymentId.HasValue
                ? await FindAsync(context, householdId, request.Values.OriginalPaymentId.Value, token) : null;
            var record = new SpendingRecord(recordId, merchant, request.Values.SpendingDate, request.Values.CurrencyCode,
                lines, request.Hash, request.Values.Kind, original, request.Values.ReceiptTotal,
                request.Values.ExternalReference, request.Values.Notes);
            context.SpendingRecords.Add(record);
            await RememberAsync(context, record, request.Prices, null, token);
            return new SpendingRecordCaptureResult(record, true);
        }, cancellationToken, retryCreate: true);
    }

    public async Task<SpendingRecord> GetAsync(Guid householdId, Guid recordId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await FindAsync(context, householdId, recordId, cancellationToken);
    }

    public async Task<IReadOnlyList<SpendingRecord>> ListAsync(Guid householdId, DateOnly? from = null,
        DateOnly? to = null, bool includeVoided = false, CancellationToken cancellationToken = default)
    {
        if (from > to) throw new InvalidValueException("The start date cannot be after the end date.");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await RequireHouseholdAsync(context, householdId, cancellationToken);
        return await Graph(context).AsNoTracking().Where(r => r.HouseholdId == householdId
            && (includeVoided || r.VoidedAtUtc == null) && (!from.HasValue || r.SpendingDate >= from.Value)
            && (!to.HasValue || r.SpendingDate <= to.Value))
            .OrderByDescending(r => r.SpendingDate).ThenByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id)
            .ToArrayAsync(cancellationToken);
    }

    public Task<SpendingRecord> CorrectHeaderAsync(Guid householdId, Guid recordId, Guid expectedRevision,
        DateOnly spendingDate, decimal? receiptTotal, string? externalReference, string? notes,
        CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, token) =>
        {
            var record = await FindAsync(context, householdId, recordId, token);
            RequireRevision(record, expectedRevision);
            record.CorrectHeader(expectedRevision, spendingDate, receiptTotal, externalReference, notes);
            return record;
        }, cancellationToken);

    public Task<SpendingRecord> CorrectLineAsync(Guid householdId, Guid recordId, Guid expectedRevision,
        Guid lineId, decimal netAmount, decimal? quantity, decimal? unitPrice, PromotionStatus promotionStatus,
        string? notes, IReadOnlyList<RememberSpendingPrice>? rememberPrices = null, CancellationToken cancellationToken = default)
    {
        var prices = FreezePrices(rememberPrices);
        return WriteAsync(async (context, token) =>
        {
            var record = await FindAsync(context, householdId, recordId, token);
            RequireRevision(record, expectedRevision);
            if (!record.Lines.Any(l => l.Id == lineId))
                throw new ResourceNotFoundException("The line does not belong to this spending record.");
            record.CorrectLine(expectedRevision, lineId, netAmount, quantity, unitPrice, promotionStatus, notes);
            var detached = quantity is null ? await DetachSourcesAsync(context, householdId, [lineId], token) : null;
            await RememberAsync(context, record, prices, detached, token);
            return record;
        }, cancellationToken);
    }

    public Task<SpendingRecord> ReplaceAsync(Guid householdId, Guid recordId, Guid expectedRevision,
        Guid merchantId, DateOnly spendingDate, IReadOnlyList<SpendingLineValues> lines, decimal? receiptTotal,
        string? externalReference, string? notes, IReadOnlyList<RememberSpendingPrice>? rememberPrices = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var copy = lines.ToArray();
        var prices = FreezePrices(rememberPrices);
        return WriteAsync(async (context, token) =>
        {
            var record = await FindAsync(context, householdId, recordId, token);
            RequireRevision(record, expectedRevision);
            var merchant = await FindMerchantAsync(context, householdId, merchantId, token);
            var resolved = await ResolveLinesAsync(context, householdId, copy, token);
            // Restrictive source FKs must be cleared before the old line rows are removed.
            var detached = await DetachSourcesAsync(context, householdId, record.Lines.Select(l => l.Id).ToArray(), token);
            var old = record.Replace(expectedRevision, merchant, spendingDate, resolved, receiptTotal, externalReference, notes);
            context.SpendingLines.RemoveRange(old);
            // Explicit tracking makes the ownership of newly generated replacement rows unambiguous.
            context.SpendingLines.AddRange(record.Lines);
            await RememberAsync(context, record, prices, detached, token);
            return record;
        }, cancellationToken);
    }

    public Task<SpendingRecord> VoidAsync(Guid householdId, Guid recordId, Guid expectedRevision,
        CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, token) =>
        {
            var record = await FindAsync(context, householdId, recordId, token);
            RequireRevision(record, expectedRevision);
            await DetachSourcesAsync(context, householdId, record.Lines.Select(l => l.Id).ToArray(), token);
            record.Void(expectedRevision);
            return record;
        }, cancellationToken);

    public async Task<SpendingLastPaidPrice?> FindLastPaidAsync(Guid householdId, Guid itemId, Guid merchantId,
        string currencyCode, DateOnly onOrBefore, Guid? excludeRecordId = null, CancellationToken cancellationToken = default)
    {
        if (onOrBefore == default) throw new InvalidValueException("Supply the lookup date.");
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var item = await context.SpendingItems.AsNoTracking().SingleOrDefaultAsync(i => i.HouseholdId == householdId && i.Id == itemId, cancellationToken)
            ?? throw new ResourceNotFoundException("The item does not belong to this household.");
        await FindMerchantAsync(context, householdId, merchantId, cancellationToken);
        if (excludeRecordId.HasValue && !await context.SpendingRecords.AnyAsync(r => r.HouseholdId == householdId && r.Id == excludeRecordId.Value, cancellationToken))
            throw new ResourceNotFoundException("The excluded record does not belong to this household.");
        // Reads can still inspect historical codes if the current capture policy changes.
        var currency = RequiredAlphabeticCode.Normalize(currencyCode, 3);
        return await context.SpendingLines.AsNoTracking().Join(context.SpendingRecords,
                l => new { l.HouseholdId, Id = l.SpendingRecordId }, r => new { r.HouseholdId, r.Id }, (l, r) => new { Line = l, Record = r })
            .Where(x => x.Record.HouseholdId == householdId && x.Record.Kind == SpendingRecordKind.Payment
                && x.Record.VoidedAtUtc == null && x.Record.MerchantId == merchantId && x.Record.CurrencyCode == currency
                && x.Record.SpendingDate <= onOrBefore && (!excludeRecordId.HasValue || x.Record.Id != excludeRecordId.Value)
                && x.Line.SpendingItemId == itemId && x.Line.UnitKey == item.UnitKey && x.Line.Quantity != null && x.Line.UnitPrice != null)
            .OrderByDescending(x => x.Record.SpendingDate).ThenByDescending(x => x.Record.CreatedAtUtc)
            .ThenByDescending(x => x.Record.Id).ThenByDescending(x => x.Line.Position).ThenByDescending(x => x.Line.Id)
            .Select(x => new SpendingLastPaidPrice(x.Record.Id, x.Line.Id, x.Record.SpendingDate, x.Record.CurrencyCode,
                x.Line.UnitKey!, x.Line.UnitLabel!, x.Line.Quantity!.Value, x.Line.UnitPrice!.Value, x.Line.NetAmount, x.Line.PromotionStatus))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<SpendingLineInput[]> ResolveLinesAsync(HearthcastDbContext context, Guid householdId,
        IReadOnlyList<SpendingLineValues> values, CancellationToken token)
    {
        if (values.Count == 0 || values.Any(l => l is null)) throw new InvalidValueException("Supply at least one valid line.");
        var categoryIds = values.Select(l => l.ExpenseCategoryId).Distinct().ToArray();
        var itemIds = values.Where(l => l.SpendingItemId.HasValue).Select(l => l.SpendingItemId!.Value).Distinct().ToArray();
        var categories = await context.ExpenseCategories.Where(c => c.HouseholdId == householdId && categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, token);
        var items = await context.SpendingItems.Where(i => i.HouseholdId == householdId && itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, token);
        if (categories.Count != categoryIds.Length || items.Count != itemIds.Length)
            throw new ResourceNotFoundException("A line category or item does not belong to this household.");
        return values.Select(l => new SpendingLineInput(categories[l.ExpenseCategoryId], l.NetAmount,
            l.SpendingItemId.HasValue ? items[l.SpendingItemId.Value] : null, l.Quantity, l.UnitPrice,
            l.UnitKey, l.UnitLabel, l.Description, l.ReceiptDescription, l.MerchantItemReference, l.PromotionStatus, l.Notes)).ToArray();
    }

    private static async Task<Dictionary<Guid, Guid>> DetachSourcesAsync(HearthcastDbContext context, Guid householdId,
        Guid[] lineIds, CancellationToken token)
    {
        var prices = await context.SpendingItemMerchantPrices.Where(p => p.HouseholdId == householdId
            && p.SourceSpendingLineId.HasValue && lineIds.Contains(p.SourceSpendingLineId.Value)).ToArrayAsync(token);
        var original = prices.ToDictionary(p => p.Id, p => p.Revision);
        foreach (var price in prices) price.ClearSource();
        if (prices.Length > 0) await context.SaveChangesAsync(token);
        return original;
    }

    private static async Task RememberAsync(HearthcastDbContext context, SpendingRecord record,
        IReadOnlyList<RememberSpendingPrice> choices, IReadOnlyDictionary<Guid, Guid>? detachedRevisions, CancellationToken token)
    {
        var identities = new HashSet<Guid>(); // Merchant/currency/unit are fixed by the record and item.
        foreach (var choice in choices)
        {
            if (choice.ExpectedRevision == Guid.Empty) throw new InvalidValueException("Supply a nonempty price revision, or null to create only.");
            var line = record.Lines.SingleOrDefault(l => l.Position == choice.LinePosition)
                ?? throw new InvalidValueException("Select a saved line position for the usual-price choice.");
            if (line.SpendingItemId is null || line.Quantity is null || line.UnitPrice is null
                || record.Kind != SpendingRecordKind.Payment || record.IsVoided)
                throw new InvalidValueException("Only itemised payment lines can supply usual prices.");
            if (!identities.Add(line.SpendingItemId.Value)) throw new InvalidValueException("Choose at most one source per item/merchant/currency/unit.");
            var price = await context.SpendingItemMerchantPrices.SingleOrDefaultAsync(p => p.HouseholdId == record.HouseholdId
                && p.SpendingItemId == line.SpendingItemId && p.MerchantId == record.MerchantId
                && p.CurrencyCode == record.CurrencyCode && p.UnitKey == line.UnitKey, token);
            if (price is null)
            {
                if (choice.ExpectedRevision.HasValue) throw new SpendingPriceConflictException();
                var item = await context.SpendingItems.SingleAsync(i => i.HouseholdId == record.HouseholdId && i.Id == line.SpendingItemId, token);
                var merchant = await FindMerchantAsync(context, record.HouseholdId, record.MerchantId, token);
                price = new SpendingItemMerchantPrice(item, merchant, record.CurrencyCode, choice.ChosenUnitPrice);
                context.SpendingItemMerchantPrices.Add(price);
            }
            else
            {
                var revision = detachedRevisions is not null && detachedRevisions.TryGetValue(price.Id, out var beforeDetachment)
                    ? beforeDetachment : price.Revision;
                if (choice.ExpectedRevision != revision) throw new SpendingPriceConflictException();
            }
            price.RememberFrom(record, line, choice.ChosenUnitPrice);
        }
    }

    private async Task<T> WriteAsync<T>(Func<HearthcastDbContext, CancellationToken, Task<T>> operation,
        CancellationToken token, bool retryCreate = false)
    {
        try
        {
            return await SerializableTransaction.ExecuteAsync(contextFactory, operation, token, retryCreate ? "PK_SpendingRecords" : null);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            if (exception.Entries.Any(e => e.Entity is SpendingItemMerchantPrice)) throw new SpendingPriceConflictException(exception);
            throw new SpendingRecordConflictException(exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: SpendingSetupConfigurationRules.UsualPriceIndex })
        {
            throw new SpendingPriceConflictException(exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_SpendingRecords" })
        {
            throw CreateConflict();
        }
    }

    private static BusinessRuleViolationException CreateConflict() => new("The record identifier cannot be used for this create request.");

    private static void RequireRevision(SpendingRecord record, Guid expected)
    {
        if (expected == Guid.Empty) throw new InvalidValueException("Supply the saved record revision.");
        if (record.Revision != expected) throw new SpendingRecordConflictException();
    }

    private static RememberSpendingPrice[] FreezePrices(IReadOnlyList<RememberSpendingPrice>? prices)
    {
        var copy = (prices ?? []).ToArray();
        if (copy.Any(p => p is null)) throw new InvalidValueException("Price choices cannot contain null entries.");
        return copy;
    }

    private static IQueryable<SpendingRecord> Graph(HearthcastDbContext context) =>
        context.SpendingRecords.Include(r => r.Lines.OrderBy(l => l.Position)).AsSingleQuery();

    private static async Task<SpendingRecord> FindAsync(HearthcastDbContext context, Guid householdId, Guid id, CancellationToken token) =>
        await Graph(context).SingleOrDefaultAsync(r => r.HouseholdId == householdId && r.Id == id, token)
        ?? throw new ResourceNotFoundException("The spending record does not belong to this household.");

    private static async Task<Merchant> FindMerchantAsync(HearthcastDbContext context, Guid householdId, Guid id, CancellationToken token) =>
        await context.Merchants.SingleOrDefaultAsync(m => m.HouseholdId == householdId && m.Id == id, token)
        ?? throw new ResourceNotFoundException("The merchant does not belong to this household.");

    private static async Task RequireHouseholdAsync(HearthcastDbContext context, Guid householdId, CancellationToken token)
    {
        if (!await context.Households.AnyAsync(h => h.Id == householdId, token))
            throw new ResourceNotFoundException("The household does not exist.");
    }
}
