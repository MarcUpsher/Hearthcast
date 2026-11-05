using Hearthcast.Data.Summaries.Models.History;
using Hearthcast.Data.Persistence;
using Hearthcast.Data.Summaries.Models.Merged;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Summaries;

public sealed class HistoricalSummarySeriesService(IDbContextFactory<HearthcastDbContext> contextFactory,
    Snapshots.SnapshotService snapshots)
{
    public async Task<HistoricalSummarySeries> GetAsync(Guid householdId, DateOnly from, DateOnly to,
        int intervalMonths, string currencyCode, CancellationToken cancellationToken = default)
    {
        if (from > to) throw new InvalidValueException("The start date must be on or before the end date.");
        if (intervalMonths is not (1 or 3 or 6 or 12))
            throw new InvalidValueException("Select a monthly, three-month, six-month or yearly interval.");
        var requestedCode = currencyCode?.Trim();
        if (requestedCode?.Length != 3 || requestedCode.Any(character => character is not (>= 'A' and <= 'Z')
            and not (>= 'a' and <= 'z')))
            throw new InvalidValueException("Select a three-letter currency code.");
        var currency = RequiredAlphabeticCode.Normalize(currencyCode, 3);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.Households.AnyAsync(h => h.Id == householdId, cancellationToken))
            throw new ResourceNotFoundException("The household does not exist.");
        var itemCurrencies = await context.ItemSnapshots.AsNoTracking().Where(i => i.HouseholdId == householdId)
            .Select(i => i.CurrencyCode).Distinct().ToArrayAsync(cancellationToken);
        var assetCurrencies = await context.FinancedAssetSnapshots.AsNoTracking().Where(a => a.HouseholdId == householdId)
            .Select(a => a.CurrencyCode).Distinct().ToArrayAsync(cancellationToken);
        var availableCurrencies = itemCurrencies.Concat(assetCurrencies).Distinct().Order(StringComparer.Ordinal).ToArray();
        var inventory = await context.Snapshots.AsNoTracking()
            .Where(s => s.HouseholdId == householdId && s.SnapshotDate >= from && s.SnapshotDate <= to)
            .Select(s => new { s.Id, s.SnapshotDate, s.CreatedAtUtc })
            .ToArrayAsync(cancellationToken);
        var selected = inventory.GroupBy(s => Bucket(s.SnapshotDate, intervalMonths))
            .Select(group => group.OrderByDescending(s => s.SnapshotDate).ThenByDescending(s => s.CreatedAtUtc)
                .ThenBy(s => s.Id).First())
            // Keep the first actual reading even when its calendar bucket ends later.
            .Concat(inventory.OrderBy(s => s.SnapshotDate).ThenByDescending(s => s.CreatedAtUtc)
                .ThenBy(s => s.Id).Take(1))
            .DistinctBy(s => s.Id)
            .OrderBy(s => s.SnapshotDate).ToArray();
        var loaded = (await snapshots.GetManyAsync(householdId, selected.Select(s => s.Id).ToArray(), cancellationToken))
            .ToDictionary(s => s.Id);
        var points = new List<HistoricalSummaryPoint>(selected.Length);
        foreach (var row in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = loaded[row.Id];
            MergedSnapshotSummary merged;
            try { merged = MergedSnapshotSummaryCalculator.Calculate(snapshot, [currency]); }
            catch (OverflowException) { throw new InvalidValueException("The converted summary exceeds the supported numeric range."); }
            HistoricalCategoryPoint[] Categories(MergedView view) => view.Categories.Select(category =>
                new HistoricalCategoryPoint(category.CategoryId, category.CategoryName, category.FinancialType,
                    Sum(currency, category.Groups.SelectMany(g => g.Entries)
                        .Where(entry => entry.Kind == MergedEntryKind.Item)
                        .Select(entry => entry.Amounts.Single())))).ToArray();
            var people = merged.HouseholdPeople.Select(person => new HistoricalPersonPoint(person.PersonId,
                snapshot.People.Single(saved => saved.PersonId == person.PersonId).DisplayName,
                Categories(person.View))).ToArray();
            var equity = Sum(currency, merged.Household.Categories.SelectMany(c => c.Groups)
                .SelectMany(g => g.Entries).Where(entry => entry.Kind == MergedEntryKind.FinancedAsset)
                .Select(entry => entry.Amounts.Single()));
            points.Add(new(snapshot.Id, snapshot.SnapshotDate, snapshot.Revision,
                merged.Household.Amounts.Single(), equity, Categories(merged.Household), people));
        }
        return new(from, to, intervalMonths, currency, availableCurrencies, points);
    }

    private static DateOnly Bucket(DateOnly date, int intervalMonths)
    {
        var month = ((date.Month - 1) / intervalMonths) * intervalMonths + 1;
        return new(date.Year, month, 1);
    }

    private static MergedAmount Sum(string currency, IEnumerable<MergedAmount> source)
    {
        var cells = source.ToArray();
        var known = cells.Where(c => c.KnownSubtotal.HasValue).ToArray();
        return new(currency, cells.Length == 0 ? 0m : known.Length == 0 ? null : known.Sum(c => c.KnownSubtotal!.Value),
            cells.Sum(c => c.MissingValueCount), cells.Sum(c => c.MissingRateCount), cells.Sum(c => c.CarriedEntryCount));
    }
}
