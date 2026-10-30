using Hearthcast.Data.Classification;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries.Models;

namespace Hearthcast.Data.Summaries;

internal static class SnapshotSummaryCalculator
{
    public static SnapshotSummary Calculate(Snapshot snapshot)
    {
        var data = SnapshotReportProjector.Project(snapshot);
        var currencies = data.Groups.GroupBy(group => group.CurrencyCode).Select(currency =>
        {
            var categories = currency.GroupBy(group => (group.CategoryId, group.CategoryName, group.FinancialType))
                .Select(category =>
                {
                    var groups = category.Select(CreateGroup).ToArray();
                    return new SnapshotCategorySummary(category.Key.CategoryId, category.Key.CategoryName,
                        category.Key.FinancialType, SnapshotSummaryAggregation.Combine(groups.Select(group => group.AllRecordedTotals)), groups);
                }).ToArray();
            return new SnapshotCurrencySummary(currency.Key,
                SnapshotSummaryAggregation.Combine(categories.Select(category => category.AllRecordedTotals)), categories);
        }).ToArray();
        return new(snapshot.Id, snapshot.HouseholdId, snapshot.HouseholdName, snapshot.SnapshotDate,
            snapshot.DatePrecision, snapshot.Revision, snapshot.CreatedAtUtc, snapshot.UpdatedAtUtc, data.People, currencies);
    }

    private static SnapshotGroupSummary CreateGroup(SnapshotReportGroup group)
    {
        var ordinary = SnapshotSummaryAggregation.Subtotal(group.Items.Select(item =>
            (item.Balance.Amount, item.Balance.ValueOrigin == SnapshotValueOrigin.CarriedForward)));
        var empty = new SnapshotAmountSubtotal(0m, 0, 0);
        var equity = SnapshotSummaryAggregation.Subtotal(group.FinancedAssets.Select(asset => (asset.CurrentEquity,
            asset.CurrentValuation.ValueOrigin == SnapshotValueOrigin.CarriedForward
                || asset.OutstandingLoanBalance.ValueOrigin == SnapshotValueOrigin.CarriedForward)));
        return new(group.GroupId, group.GroupName,
            new(group.FinancialType == FinancialType.Asset ? ordinary : empty,
                group.FinancialType == FinancialType.Debt ? ordinary : empty, equity), group.Items, group.FinancedAssets);
    }
}
