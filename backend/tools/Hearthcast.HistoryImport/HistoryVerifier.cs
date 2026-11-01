using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries;
using Microsoft.EntityFrameworkCore;
using Hearthcast.Data.Persistence;
using static Hearthcast.HistoryImport.PreparedHistory;

namespace Hearthcast.HistoryImport;

internal static class HistoryVerifier
{
    public static async Task<int> VerifyAsync(HearthcastDbContext context, PreparedHistory plan,
        ExpectedTotals[] totals, CancellationToken token)
    {
        var saved = await context.Snapshots.AsNoTrackingWithIdentityResolution().AsSplitQuery()
            .Include(s => s.People)
            .Include(s => s.ItemSnapshots).ThenInclude(i => i.HolderSnapshotPerson)
            .Include(s => s.ItemSnapshots).ThenInclude(i => i.HouseholdAllocations).ThenInclude(a => a.SnapshotPerson)
            .Include(s => s.FinancedAssetSnapshots).ThenInclude(a => a.Owners).ThenInclude(a => a.SnapshotPerson)
            .Include(s => s.FinancedAssetSnapshots).ThenInclude(a => a.LoanHolders).ThenInclude(a => a.SnapshotPerson)
            .Include(s => s.FinancedAssetSnapshots).ThenInclude(a => a.HouseholdAllocations).ThenInclude(a => a.SnapshotPerson)
            .ToDictionaryAsync(s => s.Id,token);
        Require(saved.Count == plan.Snapshots.Count, "Saved snapshot count differs from input.");
        Require(totals.Select(t => (t.SnapshotRef,t.CurrencyCode)).Distinct().Count() == totals.Length, "Duplicate expected totals.");
        Require(totals.All(t => plan.Snapshots.ContainsKey(t.SnapshotRef)), "Totals reference an unknown snapshot.");
        var checkedTotals = 0;
        foreach (var (reference,constructed) in plan.Snapshots)
        {
            token.ThrowIfCancellationRequested();
            var snapshot = saved[constructed.Id];
            VerifyEntries(plan,reference,snapshot);
            var full = SnapshotSummaryCalculator.Calculate(snapshot);
            var individual = IndividualHoldingsSummaryCalculator.Calculate(SnapshotReportProjector.Project(snapshot));
            var household = HouseholdSummaryCalculator.Calculate(snapshot);
            var expected = totals.Where(t => t.SnapshotRef == reference).ToArray();
            Require(full.Currencies.Select(c => c.CurrencyCode).ToHashSet().SetEquals(expected.Select(t => t.CurrencyCode)),
                $"{reference}: expected currencies do not match saved summary.");
            foreach (var e in expected)
            {
                var prefix = $"{reference}/{e.CurrencyCode}";
                var all = full.Currencies.Single(c => c.CurrencyCode == e.CurrencyCode).AllRecordedTotals.KnownNetPosition;
                var home = household.Currencies.SingleOrDefault(c => c.CurrencyCode == e.CurrencyCode);
                Equal(e.AllRecorded,all,prefix+" all recorded");
                Equal(e.Household,home?.Totals.KnownNetPosition ?? 0,prefix+" household");
                Equal(e.Excluded,all-(home?.Totals.KnownNetPosition ?? 0),prefix+" excluded");
                foreach (var (personRef,person) in plan.People)
                {
                    Equal(e.Individuals.GetValueOrDefault(personRef),individual.People.Single(p => p.Person.PersonId == person.Id)
                        .Currencies.SingleOrDefault(c => c.CurrencyCode == e.CurrencyCode)?.HoldingsTotals.KnownNetPosition ?? 0,prefix+" individual "+personRef);
                    Equal(e.HouseholdByPerson.GetValueOrDefault(personRef),household.People.Single(p => p.Person.PersonId == person.Id)
                        .Currencies.SingleOrDefault(c => c.CurrencyCode == e.CurrencyCode)?.Totals.KnownNetPosition ?? 0,prefix+" allocation "+personRef);
                }
                Require(e.Individuals.Keys.Concat(e.HouseholdByPerson.Keys).All(plan.People.ContainsKey), "Totals contain an unknown person.");
                Require(e.HouseholdByCategory.Keys.All(plan.Categories.ContainsKey) && e.HouseholdByGroup.Keys.All(plan.Groups.ContainsKey), "Totals contain an unknown classification.");
                foreach (var (id,category) in plan.Categories)
                    Equal(e.HouseholdByCategory.GetValueOrDefault(id),home?.Categories.SingleOrDefault(c => c.CategoryId==category.Id)?.Totals.KnownNetPosition ?? 0,prefix+" category "+id);
                foreach (var (id,group) in plan.Groups)
                    Equal(e.HouseholdByGroup.GetValueOrDefault(id),home?.Categories.SelectMany(c => c.Groups).SingleOrDefault(g => g.GroupId==group.Id)?.Totals.KnownNetPosition ?? 0,prefix+" group "+id);
                var carried = snapshot.ItemSnapshots.Count(i => i.CurrencyCode==e.CurrencyCode && i.ValueOrigin==SnapshotValueOrigin.CarriedForward)
                    + snapshot.FinancedAssetSnapshots.Count(a => a.CurrencyCode==e.CurrencyCode
                        && (a.CurrentValuationValueOrigin==SnapshotValueOrigin.CarriedForward || a.OutstandingLoanBalanceValueOrigin==SnapshotValueOrigin.CarriedForward));
                Equal(e.CarriedContributions,carried,prefix+" carried entries");
                checkedTotals++;
            }
        }
        Require(checkedTotals==totals.Length, "Some expected totals were not checked.");
        return checkedTotals;
    }

    private static void VerifyEntries(PreparedHistory plan, string period, Snapshot saved)
    {
        var constructed = plan.Snapshots[period];
        Require(saved.SnapshotDate==constructed.SnapshotDate && saved.DatePrecision==constructed.DatePrecision
            && saved.HouseholdName==plan.Household.Name && saved.People.Count==plan.People.Count, "Snapshot header differs from input.");
        var inputItems = plan.Tables.ItemValues.Where(r => r.SnapshotRef==period).ToArray();
        Require(inputItems.Length==saved.ItemSnapshots.Count, $"{period}: saved item membership differs.");
        foreach (var r in inputItems)
        {
            var item = saved.ItemSnapshots.Single(i => i.TrackedItemId==plan.Items[r.ItemRef].Id);
            var expected = constructed.ItemSnapshots.Single(i => i.TrackedItemId==item.TrackedItemId);
            Require(item.ItemName==expected.ItemName && item.HolderSnapshotPerson.PersonId==expected.HolderSnapshotPerson.PersonId
                && item.GroupId==expected.GroupId && item.GroupName==expected.GroupName && item.CategoryId==expected.CategoryId
                && item.CategoryName==expected.CategoryName && item.CurrencyCode==expected.CurrencyCode
                && item.CountryCode==expected.CountryCode && item.IncludeInHousehold==expected.IncludeInHousehold,
                $"{period}/{r.ItemRef}: saved item metadata differs.");
            Require(item.HouseholdAllocations.Select(a => (a.SnapshotPerson.PersonId,a.ShareBasisPoints)).ToHashSet()
                .SetEquals(expected.HouseholdAllocations.Select(a => (a.SnapshotPerson.PersonId,a.ShareBasisPoints))), "Saved allocations differ.");
            Guid? sourceId = r.CarryFromSnapshotRef is null ? null : plan.Snapshots[r.CarryFromSnapshotRef].ItemSnapshots
                .Single(i => i.TrackedItemId==item.TrackedItemId).Id;
            Require((item.Amount,item.ValueOrigin,item.LastConfirmedDate,item.LastConfirmedPrecision,item.CarriedFromItemSnapshotId)
                == (r.Amount,r.ValueOrigin,r.LastConfirmedDate,r.LastConfirmedPrecision,sourceId), $"{period}/{r.ItemRef}: value or carry provenance differs from input.");
            Require(r.ValueOrigin==SnapshotValueOrigin.CarriedForward || r.CarryFromSnapshotRef is null, "Only carried values may specify a source.");
        }
        var inputAssets = plan.Tables.AssetValues.Where(r => r.SnapshotRef==period).ToArray();
        Require(inputAssets.Select(r => r.AssetRef).Distinct().Count()==saved.FinancedAssetSnapshots.Count, "Saved financed membership differs.");
        foreach (var r in inputAssets)
        {
            var asset = saved.FinancedAssetSnapshots.Single(a => a.FinancedAssetId==plan.Assets[r.AssetRef].Id);
            var expected = constructed.FinancedAssetSnapshots.Single(a => a.Id==asset.Id);
            Require(asset.Owners.Select(a => (a.SnapshotPerson.PersonId,a.ShareBasisPoints)).ToHashSet().SetEquals(
                expected.Owners.Select(a => (a.SnapshotPerson.PersonId,a.ShareBasisPoints))) &&
                asset.HouseholdAllocations.Select(a => (a.SnapshotPerson.PersonId,a.ShareBasisPoints)).ToHashSet().SetEquals(
                expected.HouseholdAllocations.Select(a => (a.SnapshotPerson.PersonId,a.ShareBasisPoints))), "Saved financed shares differ.");
            Guid? sourceId = r.CarryFromSnapshotRef is null ? null : plan.Snapshots[r.CarryFromSnapshotRef].FinancedAssetSnapshots
                .Single(a => a.FinancedAssetId==asset.FinancedAssetId).Id;
            Require(State(asset,r.BalanceKind)==(r.Amount,r.ValueOrigin,r.LastConfirmedDate,r.LastConfirmedPrecision,sourceId),
                $"{period}/{r.AssetRef}/{r.BalanceKind}: financed value or provenance differs.");
            Require(r.ValueOrigin==SnapshotValueOrigin.CarriedForward || r.CarryFromSnapshotRef is null, "Only carried values may specify a source.");
        }
    }

    private static (decimal?,SnapshotValueOrigin,DateOnly?,DatePrecision?,Guid?) State(FinancedAssetSnapshot a, FinancedAssetBalanceKind kind) => kind switch
    {
        FinancedAssetBalanceKind.CurrentValuation => (a.CurrentValuation,a.CurrentValuationValueOrigin,a.CurrentValuationLastConfirmedDate,a.CurrentValuationLastConfirmedPrecision,a.CurrentValuationCarriedFromSnapshotId),
        FinancedAssetBalanceKind.OutstandingLoanBalance => (a.OutstandingLoanBalance,a.OutstandingLoanBalanceValueOrigin,a.OutstandingLoanBalanceLastConfirmedDate,a.OutstandingLoanBalanceLastConfirmedPrecision,a.OutstandingLoanBalanceCarriedFromSnapshotId),
        FinancedAssetBalanceKind.AvailableToWithdraw => (a.AvailableToWithdraw,a.AvailableToWithdrawValueOrigin,a.AvailableToWithdrawLastConfirmedDate,a.AvailableToWithdrawLastConfirmedPrecision,a.AvailableToWithdrawCarriedFromSnapshotId),
        _ => throw new InvalidDataException("Unknown financed balance kind.")
    };

    private static void Equal(decimal expected, decimal actual, string scope) =>
        Require(expected==actual,$"{scope}: expected {expected}, got {actual}.");
}
