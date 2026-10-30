using Hearthcast.Data.Classification;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries.Models;
using Hearthcast.Data.Summaries.Models.Household;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Summaries;

internal static class HouseholdSummaryCalculator
{
    public static SnapshotHouseholdSummary Calculate(Snapshot savedSnapshot)
    {
        // Own the scope selection so callers cannot accidentally supply excluded entries.
        var snapshot = SnapshotReportProjector.Project(savedSnapshot, includedInHouseholdOnly: true);
        var originalPersonIds = snapshot.People.ToDictionary(person => person.SnapshotPersonId, person => person.PersonId);
        var itemShares = snapshot.Groups.SelectMany(group => group.Items).ToDictionary(item => item.ItemSnapshotId,
            item => SnapshotShareAllocator.Allocate(item.Balance.Amount, item.HouseholdAllocations, originalPersonIds));
        var equityShares = snapshot.Groups.SelectMany(group => group.FinancedAssets).ToDictionary(asset => asset.FinancedAssetSnapshotId,
            asset => SnapshotShareAllocator.Allocate(asset.CurrentEquity, asset.HouseholdAllocations, originalPersonIds));
        var currencies = CreateCurrencies(snapshot.Groups, null, itemShares, equityShares);
        var people = snapshot.People.Select(person => new HouseholdPersonSummary(person,
            CreateCurrencies(snapshot.Groups, person.SnapshotPersonId, itemShares, equityShares))).ToArray();
        var header = snapshot.Header;
        return new(header.Id, header.HouseholdId, header.HouseholdName, header.SnapshotDate,
            header.DatePrecision, header.Revision, header.CreatedAtUtc, header.UpdatedAtUtc, currencies, people);
    }

    private static HouseholdCurrencySummary[] CreateCurrencies(IReadOnlyList<SnapshotReportGroup> source, Guid? personId,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, decimal?>> itemShares,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, decimal?>> equityShares)
    {
        return source.GroupBy(group => group.CurrencyCode).Select(currency =>
        {
            var categories = currency.GroupBy(group => (group.CategoryId, group.CategoryName, group.FinancialType))
                .Select(category =>
                {
                    var groups = category.Select(group => CreateGroup(group, personId, itemShares, equityShares))
                        .Where(group => group.Items.Count != 0 || group.FinancedAssets.Count != 0).ToArray();
                    return new HouseholdCategorySummary(category.Key.CategoryId, category.Key.CategoryName, category.Key.FinancialType,
                        SnapshotSummaryAggregation.Combine(groups.Select(group => group.Totals)), groups);
                }).Where(category => category.Groups.Count != 0).ToArray();
            return new HouseholdCurrencySummary(currency.Key,
                SnapshotSummaryAggregation.Combine(categories.Select(category => category.Totals)), categories);
        }).Where(currency => currency.Categories.Count != 0).ToArray();
    }

    private static HouseholdGroupSummary CreateGroup(SnapshotReportGroup group, Guid? personId,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, decimal?>> itemShares,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, decimal?>> equityShares)
    {
        var items = group.Items.Where(item => !personId.HasValue || itemShares[item.ItemSnapshotId].ContainsKey(personId.Value))
            .Select(item => new HouseholdItemContribution(item,
                personId.HasValue ? item.HouseholdAllocations.Single(row => row.SnapshotPersonId == personId).ShareBasisPoints
                    : AllocationShareRules.FullShareBasisPoints,
                personId.HasValue ? itemShares[item.ItemSnapshotId][personId.Value] : item.Balance.Amount)).ToArray();
        var assets = group.FinancedAssets.Where(asset => !personId.HasValue || equityShares[asset.FinancedAssetSnapshotId].ContainsKey(personId.Value))
            .Select(asset => new HouseholdFinancedAssetContribution(asset,
                personId.HasValue ? asset.HouseholdAllocations.Single(row => row.SnapshotPersonId == personId).ShareBasisPoints
                    : AllocationShareRules.FullShareBasisPoints,
                personId.HasValue ? equityShares[asset.FinancedAssetSnapshotId][personId.Value] : asset.CurrentEquity)).ToArray();
        var ordinary = SnapshotSummaryAggregation.Subtotal(items.Select(item =>
            (item.ContributingAmount, item.Item.Balance.ValueOrigin == SnapshotValueOrigin.CarriedForward)));
        var equity = SnapshotSummaryAggregation.Subtotal(assets.Select(asset => (asset.AttributedEquity,
            asset.Asset.CurrentValuation.ValueOrigin == SnapshotValueOrigin.CarriedForward
                || asset.Asset.OutstandingLoanBalance.ValueOrigin == SnapshotValueOrigin.CarriedForward)));
        var empty = new SnapshotAmountSubtotal(0m, 0, 0);
        return new(group.GroupId, group.GroupName,
            new(group.FinancialType == FinancialType.Asset ? ordinary : empty,
                group.FinancialType == FinancialType.Debt ? ordinary : empty, equity), items, assets);
    }
}
