using System.Data;
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
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if (!await context.Households.AsNoTracking().AnyAsync(h => h.Id == householdId, cancellationToken))
            throw new ResourceNotFoundException("The household was not found.");

        var records = await context.SpendingRecords.AsNoTracking()
            .Where(r => r.HouseholdId == householdId && r.VoidedAtUtc == null
                && r.SpendingDate >= from && r.SpendingDate <= to)
            .OrderBy(r => r.SpendingDate).ThenBy(r => r.CreatedAtUtc).ThenBy(r => r.Id)
            .Include(r => r.Lines.OrderBy(l => l.Position)).Include(r => r.Discounts.OrderBy(d => d.Position)).AsSplitQuery()
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

    private sealed record Contribution(SpendingRecord Record, Guid CategoryId, string CategoryName, string CurrencyCode, int MinorUnits, decimal Amount, int Kind, int Position);

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
            var lineTotal = record.TotalAmount;
            counts[receipt == lineTotal ? "Reconciled" : "Unreconciled"]++;
            if (receipt != lineTotal)
                warnings.Add(new(record.Id, record.SpendingDate, record.MerchantName, record.CurrencyCode,
                    receipt, lineTotal, checked(receipt - lineTotal)));
        }

        var groups = records.SelectMany(record => record.Lines.Select(line => new Contribution(record, line.ExpenseCategoryId,
                line.ExpenseCategoryName, line.CurrencyCode, line.CurrencyMinorUnits, line.NetAmount, 0, line.Position))
            .Concat(record.Discounts.Select(discount => new Contribution(record, discount.ExpenseCategoryId,
                discount.ExpenseCategoryName, discount.CurrencyCode, discount.CurrencyMinorUnits, -discount.Amount, 1, discount.Position))))
            .GroupBy(x => new { x.CategoryId, x.CurrencyCode })
            .Select(group =>
            {
                var amount = group.Aggregate(0m, (sum, x) => checked(sum +
                    (x.Record.Kind == SpendingRecordKind.Refund ? -x.Amount : x.Amount)));
                var label = group.OrderByDescending(x => x.Record.SpendingDate)
                    .ThenByDescending(x => x.Record.CreatedAtUtc).ThenByDescending(x => x.Record.Id)
                    .ThenByDescending(x => x.Kind).ThenByDescending(x => x.Position).First().CategoryName;
                return new SpendingCategoryTotal(group.Key.CategoryId, label, group.Key.CurrencyCode,
                    group.Max(x => x.MinorUnits), amount);
            })
            .OrderBy(x => x.CurrencyCode, StringComparer.Ordinal).ThenBy(x => x.CategoryName, StringComparer.Ordinal)
            .ThenBy(x => x.ExpenseCategoryId).ToArray();

        return new(householdId, from, to, groups, counts, warnings);
    }
}
