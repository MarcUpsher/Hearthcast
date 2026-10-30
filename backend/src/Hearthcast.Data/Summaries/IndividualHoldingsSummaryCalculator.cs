using Hearthcast.Data.Classification;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries.Models;
using Hearthcast.Data.Summaries.Models.IndividualHoldings;

namespace Hearthcast.Data.Summaries;

internal static class IndividualHoldingsSummaryCalculator
{
    public static SnapshotIndividualHoldingsSummary Calculate(SnapshotReportData snapshot)
    {
        var originalPersonIds = snapshot.People.ToDictionary(person => person.SnapshotPersonId, person => person.PersonId);
        var equity = snapshot.Groups
            .SelectMany(group => group.FinancedAssets).ToDictionary(asset => asset.FinancedAssetSnapshotId,
                asset => OwnershipEquityAllocator.Allocate(asset, originalPersonIds));
        var people = snapshot.People.Select(person =>
        {
            var currencies = snapshot.Groups.GroupBy(group => group.CurrencyCode).Select(currency =>
            {
                var categories = currency.GroupBy(group => (group.CategoryId, group.CategoryName, group.FinancialType)).Select(category =>
                {
                    var groups = category.Select(group => CreateGroup(group, category.Key.FinancialType, person.SnapshotPersonId, equity))
                        .Where(group => group.Items.Count != 0 || group.FinancedAssets.Count != 0).ToArray();
                    return new PersonHoldingsCategorySummary(category.Key.CategoryId, category.Key.CategoryName, category.Key.FinancialType,
                        SnapshotSummaryAggregation.Combine(groups.Select(group => group.HoldingsTotals)), groups);
                }).Where(category => category.Groups.Count != 0).ToArray();
                return new PersonHoldingsCurrencySummary(currency.Key,
                    SnapshotSummaryAggregation.Combine(categories.Select(category => category.HoldingsTotals)), categories);
            }).Where(currency => currency.Categories.Count != 0).ToArray();
            return new PersonHoldingsSummary(person, currencies);
        }).ToArray();
        var header = snapshot.Header;
        return new(header.Id, header.HouseholdId, header.HouseholdName, header.SnapshotDate,
            header.DatePrecision, header.Revision, header.CreatedAtUtc, header.UpdatedAtUtc, people);
    }

    private static PersonHoldingsGroupSummary CreateGroup(SnapshotReportGroup group, FinancialType financialType,
        Guid personId, IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, decimal?>> equity)
    {
        var items = group.Items.Where(item => item.HolderSnapshotPersonId == personId).ToArray();
        var assets = group.FinancedAssets.Where(asset => asset.Owners.Any(owner => owner.SnapshotPersonId == personId))
            .Select(asset => new OwnedFinancedAssetSummary(asset,
                asset.Owners.Single(owner => owner.SnapshotPersonId == personId).ShareBasisPoints,
                equity[asset.FinancedAssetSnapshotId][personId])).ToArray();
        var ordinary = SnapshotSummaryAggregation.Subtotal(items.Select(item =>
            (item.Balance.Amount, item.Balance.ValueOrigin == SnapshotValueOrigin.CarriedForward)));
        var financed = SnapshotSummaryAggregation.Subtotal(assets.Select(asset => (asset.AttributedEquity,
            asset.Asset.CurrentValuation.ValueOrigin == SnapshotValueOrigin.CarriedForward
                || asset.Asset.OutstandingLoanBalance.ValueOrigin == SnapshotValueOrigin.CarriedForward)));
        var empty = new SnapshotAmountSubtotal(0m, 0, 0);
        return new(group.GroupId, group.GroupName,
            new(financialType == FinancialType.Asset ? ordinary : empty,
                financialType == FinancialType.Debt ? ordinary : empty, financed), items, assets);
    }
}
