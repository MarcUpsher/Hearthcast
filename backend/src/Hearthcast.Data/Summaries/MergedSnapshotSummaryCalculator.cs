using Hearthcast.Data.Classification;
using Hearthcast.Data.ExchangeRates;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries.Models;
using Hearthcast.Data.Summaries.Models.Merged;

namespace Hearthcast.Data.Summaries;

internal static class MergedSnapshotSummaryCalculator
{
    public static MergedSnapshotSummary Calculate(Snapshot snapshot, IReadOnlyList<string>? requestedCurrencies = null)
    {
        var household = HouseholdSummaryCalculator.Calculate(snapshot);
        var individual = IndividualHoldingsSummaryCalculator.Calculate(SnapshotReportProjector.Project(snapshot));
        var all = SnapshotSummaryCalculator.Calculate(snapshot);
        var currencies = requestedCurrencies ?? snapshot.Currencies;
        var exchangeRates = new SavedExchangeRateConverter(snapshot.ExchangeRateBaseCurrency, snapshot.ExchangeRates);
        var people = snapshot.People.ToDictionary(p => p.Id, p => p.DisplayName);
        string Holder(ItemSnapshotSummary item) => "Held by " + people[item.HolderSnapshotPersonId];
        string Share(int basisPoints, string label) => basisPoints == 10000 ? "" : $" · {basisPoints / 100m}% {label}";

        List<Row> HouseholdRows(IReadOnlyList<Models.Household.HouseholdCurrencySummary> source)
        {
            var rows = new List<Row>();
            foreach (var currency in source)
                foreach (var category in currency.Categories)
                    foreach (var group in category.Groups)
                    {
                        foreach (var item in group.Items)
                            rows.Add(ItemRow(category.CategoryId, category.CategoryName, category.FinancialType,
                                group.GroupId, group.GroupName, currency.CurrencyCode, item.Item, item.ContributingAmount,
                                Holder(item.Item) + Share(item.AllocationShareBasisPoints, "household share")));
                        foreach (var asset in group.FinancedAssets)
                            rows.Add(AssetRow(category.CategoryId, category.CategoryName, category.FinancialType,
                                group.GroupId, group.GroupName, currency.CurrencyCode, asset.Asset, asset.AttributedEquity,
                                "Equity" + Share(asset.AllocationShareBasisPoints, "household share")));
                    }
            return rows;
        }

        var individualViews = individual.People.Select(person =>
        {
            var rows = new List<Row>();
            foreach (var currency in person.Currencies)
                foreach (var category in currency.Categories)
                    foreach (var group in category.Groups)
                    {
                        foreach (var item in group.Items)
                            rows.Add(ItemRow(category.CategoryId, category.CategoryName, category.FinancialType,
                                group.GroupId, group.GroupName, currency.CurrencyCode, item, item.Balance.Amount, Holder(item)));
                        foreach (var asset in group.FinancedAssets)
                            rows.Add(AssetRow(category.CategoryId, category.CategoryName, category.FinancialType,
                                group.GroupId, group.GroupName, currency.CurrencyCode, asset.Asset, asset.AttributedEquity,
                                "Equity" + Share(asset.OwnershipShareBasisPoints, "ownership")));
                    }
            return new MergedPerson(person.Person.PersonId, Build(rows));
        }).ToArray();
        var allRows = new List<Row>();
        foreach (var currency in all.Currencies)
            foreach (var category in currency.Categories)
                foreach (var group in category.Groups)
                {
                    foreach (var item in group.Items)
                        allRows.Add(ItemRow(category.CategoryId, category.CategoryName, category.FinancialType,
                            group.GroupId, group.GroupName, currency.CurrencyCode, item, item.Balance.Amount, Holder(item)));
                    foreach (var asset in group.FinancedAssets)
                        allRows.Add(AssetRow(category.CategoryId, category.CategoryName, category.FinancialType,
                            group.GroupId, group.GroupName, currency.CurrencyCode, asset, asset.CurrentEquity, "Equity"));
                }
        return new(snapshot.Id, snapshot.Revision, currencies, Build(HouseholdRows(household.Currencies)),
            household.People.Select(p => new MergedPerson(p.Person.PersonId, Build(HouseholdRows(p.Currencies)))).ToArray(),
            individualViews, Build(allRows));

        MergedView Build(IReadOnlyList<Row> rows)
        {
            var categories = rows.GroupBy(r => (r.CategoryId, r.CategoryName, r.FinancialType))
                .OrderBy(c => c.Key.CategoryName, StringComparer.Ordinal).ThenBy(c => c.Key.CategoryId)
                .ThenBy(c => c.Key.FinancialType).Select(category =>
                {
                    var groups = category.GroupBy(r => (r.GroupId, r.GroupName))
                        .OrderBy(g => g.Key.GroupName, StringComparer.Ordinal).ThenBy(g => g.Key.GroupId).Select(group =>
                        {
                            var entries = group.OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Key)
                                .Select(r => new MergedEntry(r.Key, r.Name, r.Description, r.Currency, r.Amount, r.Status,
                                    r.InstitutionName, r.FundName, currencies.Select(target => Convert(r, target)).ToArray(), r.Kind)).ToArray();
                            return new MergedGroup(group.Key.GroupId, group.Key.GroupName, Sum(entries.Select(e => e.Amounts)), entries);
                        }).ToArray();
                    return new MergedCategory(category.Key.CategoryId, category.Key.CategoryName, category.Key.FinancialType,
                        Sum(groups.Select(g => g.Amounts)), groups);
                }).ToArray();
            return new(Sum(categories.Select(c => c.Amounts)), categories);
        }

        MergedAmount Convert(Row row, string target)
        {
            var factor = exchangeRates.ResolveRate(row.Currency, target);
            return new(target, row.Amount.HasValue && factor.HasValue ? SavedExchangeRateConverter.Convert(row.Amount.Value, factor.Value) : null,
                row.Amount.HasValue ? 0 : 1, factor.HasValue ? 0 : 1, row.IsCarried ? 1 : 0);
        }
        IReadOnlyList<MergedAmount> Sum(IEnumerable<IReadOnlyList<MergedAmount>> source)
        {
            var cells = source.SelectMany(s => s).ToArray();
            return currencies.Select(currency =>
            {
                var column = cells.Where(c => c.CurrencyCode == currency).ToArray();
                var known = column.Where(c => c.KnownSubtotal.HasValue).ToArray();
                return new MergedAmount(currency, column.Length == 0 ? 0m : known.Length == 0 ? null : known.Sum(c => c.KnownSubtotal!.Value),
                    column.Sum(c => c.MissingValueCount), column.Sum(c => c.MissingRateCount), column.Sum(c => c.CarriedEntryCount));
            }).ToArray();
        }
    }

    private sealed record Row(Guid CategoryId, string CategoryName, FinancialType FinancialType,
        Guid GroupId, string GroupName, string Currency, string Key, string Name, string Description,
        decimal? Amount, MergedEntryKind Kind, bool IsCarried, string Status, string? InstitutionName = null, string? FundName = null);
    private static Row ItemRow(Guid categoryId, string categoryName, FinancialType type, Guid groupId, string groupName,
        string currency, ItemSnapshotSummary item, decimal? amount, string description) =>
        new(categoryId, categoryName, type, groupId, groupName, currency, "item:" + item.ItemSnapshotId,
            item.ItemName, description + (type == FinancialType.Debt ? " · Debt" : ""), type == FinancialType.Debt ? -amount : amount,
            MergedEntryKind.Item, item.Balance.ValueOrigin == SnapshotValueOrigin.CarriedForward,
            Status(item.Balance.ValueOrigin), item.InstitutionName, item.FundName);
    private static Row AssetRow(Guid categoryId, string categoryName, FinancialType type, Guid groupId, string groupName,
        string currency, FinancedAssetSnapshotSummary asset, decimal? amount, string description)
    {
        var isCarried = amount.HasValue && (asset.CurrentValuation.ValueOrigin == SnapshotValueOrigin.CarriedForward
            || asset.OutstandingLoanBalance.ValueOrigin == SnapshotValueOrigin.CarriedForward);
        return new(categoryId, categoryName, type, groupId, groupName, currency, "asset:" + asset.FinancedAssetSnapshotId,
            asset.AssetName, description, amount, MergedEntryKind.FinancedAsset, isCarried,
            amount is null ? "Missing balances" : isCarried ? "Carried forward" : "Confirmed");
    }
    private static string Status(SnapshotValueOrigin origin) => origin == SnapshotValueOrigin.CarriedForward
        ? "Carried forward" : origin == SnapshotValueOrigin.Confirmed ? "Confirmed" : "Unknown";
}
