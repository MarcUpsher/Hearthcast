using Hearthcast.Data.Classification;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries.Models;

namespace Hearthcast.Data.Summaries;

// Accepts the complete, consistently loaded graph from SnapshotService; never reads current setup.
internal static class SnapshotSummaryCalculator
{
    public static SnapshotSummary Calculate(Snapshot snapshot)
    {
        var items = snapshot.ItemSnapshots.ToLookup(item => new GroupKey(item.CurrencyCode,
            item.CategoryId, item.CategoryName, item.FinancialType, item.GroupId, item.GroupName));
        var assets = snapshot.FinancedAssetSnapshots.ToLookup(asset => new GroupKey(asset.CurrencyCode,
            asset.CategoryId, asset.CategoryName, asset.FinancialType, asset.GroupId, asset.GroupName));
        var groups = items.Select(group => group.Key).Union(assets.Select(group => group.Key))
            .Select(key => (Key: key, Summary: CreateGroup(key, items[key], assets[key]))).ToArray();
        var currencies = groups.GroupBy(group => group.Key.CurrencyCode)
            .OrderBy(currency => currency.Key, StringComparer.Ordinal)
            .Select(currency =>
            {
                var categories = currency.GroupBy(group => (group.Key.CategoryId, group.Key.CategoryName, group.Key.FinancialType))
                    .OrderBy(category => category.Key.CategoryName, StringComparer.Ordinal).ThenBy(category => category.Key.CategoryId)
                    .Select(category =>
                    {
                        var categoryGroups = category.OrderBy(group => group.Key.GroupName, StringComparer.Ordinal)
                            .ThenBy(group => group.Key.GroupId).Select(group => group.Summary).ToArray();
                        return new SnapshotCategorySummary(category.Key.CategoryId, category.Key.CategoryName,
                            category.Key.FinancialType, Combine(categoryGroups.Select(group => group.AllRecordedTotals)), categoryGroups);
                    }).ToArray();
                return new SnapshotCurrencySummary(currency.Key,
                    Combine(categories.Select(category => category.AllRecordedTotals)), categories);
            }).ToArray();
        return new SnapshotSummary(snapshot.Id, snapshot.HouseholdId, snapshot.HouseholdName, snapshot.SnapshotDate,
            snapshot.DatePrecision, snapshot.Revision, snapshot.CreatedAtUtc, snapshot.UpdatedAtUtc,
            snapshot.People.OrderBy(person => person.DisplayName, StringComparer.Ordinal).ThenBy(person => person.Id)
                .Select(person => new SnapshotPersonSummary(person.Id, person.PersonId, person.DisplayName,
                    person.ParticipatesInHouseholdFinances)).ToArray(), currencies);
    }

    private static SnapshotGroupSummary CreateGroup(GroupKey key, IEnumerable<ItemSnapshot> items,
        IEnumerable<FinancedAssetSnapshot> assets)
    {
        var itemSummaries = items.OrderBy(item => item.ItemName, StringComparer.Ordinal).ThenBy(item => item.Id)
            .Select(MapItem).ToArray();
        var assetSummaries = assets.OrderBy(asset => asset.AssetName, StringComparer.Ordinal).ThenBy(asset => asset.Id)
            .Select(MapFinancedAsset).ToArray();
        var ordinary = Subtotal(itemSummaries.Select(item => (item.Balance.Amount, item.Balance.ValueOrigin == SnapshotValueOrigin.CarriedForward)));
        var empty = new SnapshotAmountSubtotal(0m, 0, 0);
        var equity = Subtotal(assetSummaries.Select(asset => (asset.CurrentEquity,
            asset.CurrentValuation.ValueOrigin == SnapshotValueOrigin.CarriedForward
                || asset.OutstandingLoanBalance.ValueOrigin == SnapshotValueOrigin.CarriedForward)));
        return new SnapshotGroupSummary(key.GroupId, key.GroupName,
            new(key.FinancialType == FinancialType.Asset ? ordinary : empty,
                key.FinancialType == FinancialType.Debt ? ordinary : empty, equity), itemSummaries, assetSummaries);
    }

    private static ItemSnapshotSummary MapItem(ItemSnapshot item) => new(
        ItemSnapshotId: item.Id,
        TrackedItemId: item.TrackedItemId,
        ItemName: item.ItemName,
        CountryCode: item.CountryCode,
        HolderSnapshotPersonId: item.HolderSnapshotPersonId,
        IncludeInHousehold: item.IncludeInHousehold,
        Balance: new(
            Amount: item.Amount,
            ValueOrigin: item.ValueOrigin,
            LastConfirmedDate: item.LastConfirmedDate,
            LastConfirmedPrecision: item.LastConfirmedPrecision,
            CarriedFromEntryId: item.CarriedFromItemSnapshotId),
        HouseholdAllocations: item.HouseholdAllocations.OrderBy(row => row.SnapshotPersonId)
            .Select(row => new SnapshotPersonShare(row.SnapshotPersonId, row.ShareBasisPoints)).ToArray());

    private static FinancedAssetSnapshotSummary MapFinancedAsset(FinancedAssetSnapshot asset) => new(
        FinancedAssetSnapshotId: asset.Id,
        FinancedAssetId: asset.FinancedAssetId,
        AssetName: asset.AssetName,
        CountryCode: asset.CountryCode,
        PurchaseDate: asset.PurchaseDate,
        PurchasePrice: asset.PurchasePrice,
        OriginalLoanAmount: asset.OriginalLoanAmount,
        HasWithdrawalFacility: asset.HasWithdrawalFacility,
        IncludeInHousehold: asset.IncludeInHousehold,
        Owners: asset.Owners.OrderBy(row => row.SnapshotPersonId)
            .Select(row => new SnapshotPersonShare(row.SnapshotPersonId, row.ShareBasisPoints)).ToArray(),
        LoanHolderSnapshotPersonIds: asset.LoanHolders.Select(row => row.SnapshotPersonId).Order().ToArray(),
        HouseholdAllocations: asset.HouseholdAllocations.OrderBy(row => row.SnapshotPersonId)
            .Select(row => new SnapshotPersonShare(row.SnapshotPersonId, row.ShareBasisPoints)).ToArray(),
        CurrentValuation: new(
            Amount: asset.CurrentValuation,
            ValueOrigin: asset.CurrentValuationValueOrigin,
            LastConfirmedDate: asset.CurrentValuationLastConfirmedDate,
            LastConfirmedPrecision: asset.CurrentValuationLastConfirmedPrecision,
            CarriedFromEntryId: asset.CurrentValuationCarriedFromSnapshotId),
        OutstandingLoanBalance: new(
            Amount: asset.OutstandingLoanBalance,
            ValueOrigin: asset.OutstandingLoanBalanceValueOrigin,
            LastConfirmedDate: asset.OutstandingLoanBalanceLastConfirmedDate,
            LastConfirmedPrecision: asset.OutstandingLoanBalanceLastConfirmedPrecision,
            CarriedFromEntryId: asset.OutstandingLoanBalanceCarriedFromSnapshotId),
        AvailableToWithdraw: new(
            Amount: asset.AvailableToWithdraw,
            ValueOrigin: asset.AvailableToWithdrawValueOrigin,
            LastConfirmedDate: asset.AvailableToWithdrawLastConfirmedDate,
            LastConfirmedPrecision: asset.AvailableToWithdrawLastConfirmedPrecision,
            CarriedFromEntryId: asset.AvailableToWithdrawCarriedFromSnapshotId),
        CurrentEquity: asset.CurrentEquity,
        LoanBalanceReduction: asset.LoanBalanceReduction,
        LoanBalanceReductionExcludingWithdrawableFunds: asset.LoanBalanceReductionExcludingWithdrawableFunds);

    private static SnapshotAmountSubtotal Subtotal(IEnumerable<(decimal? Amount, bool Carried)> entries)
    {
        var values = entries.ToArray();
        return new(values.Sum(value => value.Amount ?? 0m), values.Count(value => !value.Amount.HasValue),
            values.Count(value => value.Amount.HasValue && value.Carried));
    }

    private static SnapshotSummaryTotals Combine(IEnumerable<SnapshotSummaryTotals> totals)
    {
        var values = totals.ToArray();
        return new(Sum(values.Select(value => value.OrdinaryAssets)), Sum(values.Select(value => value.OrdinaryDebts)),
            Sum(values.Select(value => value.FinancedAssetEquity)));
    }

    private static SnapshotAmountSubtotal Sum(IEnumerable<SnapshotAmountSubtotal> subtotals)
    {
        var values = subtotals.ToArray();
        return new(values.Sum(value => value.KnownSubtotal), values.Sum(value => value.MissingEntryCount),
            values.Sum(value => value.CarriedForwardEntryCount));
    }

    private readonly record struct GroupKey(string CurrencyCode, Guid CategoryId, string CategoryName,
        FinancialType FinancialType, Guid GroupId, string GroupName);
}
