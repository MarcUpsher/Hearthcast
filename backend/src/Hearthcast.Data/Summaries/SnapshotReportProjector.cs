using Hearthcast.Data.Classification;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries.Models;

namespace Hearthcast.Data.Summaries;

// Projects saved entries once, without calculating totals for any report scope.
internal static class SnapshotReportProjector
{
    public static SnapshotReportData Project(Snapshot snapshot, bool includedInHouseholdOnly = false)
    {
        var items = snapshot.ItemSnapshots.Where(item => !includedInHouseholdOnly || item.IncludeInHousehold).ToLookup(item => new GroupKey(item.CurrencyCode,
            item.CategoryId, item.CategoryName, item.FinancialType, item.GroupId, item.GroupName));
        var assets = snapshot.FinancedAssetSnapshots.Where(asset => !includedInHouseholdOnly || asset.IncludeInHousehold).ToLookup(asset => new GroupKey(asset.CurrencyCode,
            asset.CategoryId, asset.CategoryName, asset.FinancialType, asset.GroupId, asset.GroupName));
        var groups = items.Select(group => group.Key).Union(assets.Select(group => group.Key))
            .OrderBy(key => key.CurrencyCode, StringComparer.Ordinal)
            .ThenBy(key => key.CategoryName, StringComparer.Ordinal).ThenBy(key => key.CategoryId)
            .ThenBy(key => key.GroupName, StringComparer.Ordinal).ThenBy(key => key.GroupId)
            .Select(key => new SnapshotReportGroup(key.CurrencyCode, key.CategoryId, key.CategoryName,
                key.FinancialType, key.GroupId, key.GroupName,
                items[key].OrderBy(item => item.ItemName, StringComparer.Ordinal).ThenBy(item => item.Id)
                    .Select(MapItem).ToArray(),
                assets[key].OrderBy(asset => asset.AssetName, StringComparer.Ordinal).ThenBy(asset => asset.Id)
                    .Select(MapFinancedAsset).ToArray())).ToArray();
        var people = snapshot.People.OrderBy(person => person.DisplayName, StringComparer.Ordinal).ThenBy(person => person.Id)
            .Select(person => new SnapshotPersonSummary(person.Id, person.PersonId, person.DisplayName,
                person.ParticipatesInHouseholdFinances)).ToArray();
        return new(snapshot, people, groups);
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

    private readonly record struct GroupKey(string CurrencyCode, Guid CategoryId, string CategoryName,
        FinancialType FinancialType, Guid GroupId, string GroupName);
}
