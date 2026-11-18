using Hearthcast.Data.Persistence;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Spending;

public sealed class SpendingCategorySummaryService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public async Task<SpendingCategorySummary> GetAsync(Guid householdId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (from == default || to == default || from > to)
            throw new InvalidValueException("Supply valid start and end dates in ascending order.");

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.Households.AsNoTracking().AnyAsync(h => h.Id == householdId, cancellationToken))
            throw new ResourceNotFoundException("The household was not found.");

        var records = await context.SpendingRecords.AsNoTracking()
            .Where(r => r.HouseholdId == householdId && r.VoidedAtUtc == null
                && r.SpendingDate >= from && r.SpendingDate <= to)
            .OrderBy(r => r.SpendingDate).ThenBy(r => r.CreatedAtUtc).ThenBy(r => r.Id)
            .Include(r => r.Lines.OrderBy(l => l.Position)).AsSingleQuery()
            .ToArrayAsync(cancellationToken);

        try
        {
            return Summarize(householdId, from, to, records);
        }
        catch (OverflowException)
        {
            throw new InvalidValueException("The recorded spending summary exceeds the supported numeric range.");
        }
    }

    private static SpendingCategorySummary Summarize(Guid householdId, DateOnly from, DateOnly to,
        IReadOnlyList<SpendingRecord> records)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
            { ["NotChecked"] = 0, ["Reconciled"] = 0, ["Unreconciled"] = 0 };
        var warnings = new List<SpendingReceiptWarning>();
        foreach (var record in records)
        {
            if (record.ReceiptTotal is not decimal receipt)
            {
                counts["NotChecked"]++;
                continue;
            }
            var lineTotal = record.Lines.Aggregate(0m, (sum, line) => checked(sum + line.NetAmount));
            counts[receipt == lineTotal ? "Reconciled" : "Unreconciled"]++;
            if (receipt != lineTotal)
                warnings.Add(new(record.Id, record.SpendingDate, record.MerchantName, record.CurrencyCode,
                    receipt, lineTotal, checked(receipt - lineTotal)));
        }

        var groups = records.SelectMany(record => record.Lines.Select(line => new { record, line }))
            .GroupBy(x => new { x.line.ExpenseCategoryId, x.line.CurrencyCode })
            .Select(group =>
            {
                var amount = group.Aggregate(0m, (sum, x) => checked(sum +
                    (x.record.Kind == SpendingRecordKind.Refund ? -x.line.NetAmount : x.line.NetAmount)));
                var label = group.OrderByDescending(x => x.record.SpendingDate)
                    .ThenByDescending(x => x.record.CreatedAtUtc).ThenByDescending(x => x.record.Id)
                    .ThenByDescending(x => x.line.Position).First().line.ExpenseCategoryName;
                return new SpendingCategoryTotal(group.Key.ExpenseCategoryId, label, group.Key.CurrencyCode,
                    group.Max(x => x.line.CurrencyMinorUnits), amount);
            })
            .OrderBy(x => x.CurrencyCode, StringComparer.Ordinal).ThenBy(x => x.CategoryName, StringComparer.Ordinal)
            .ThenBy(x => x.ExpenseCategoryId).ToArray();

        return new(householdId, from, to, groups, counts, warnings);
    }
}
