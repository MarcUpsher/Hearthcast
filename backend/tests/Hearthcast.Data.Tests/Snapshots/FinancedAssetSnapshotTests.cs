using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.FinancedAssets;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Tests.Snapshots;

public sealed class FinancedAssetSnapshotTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();
    private static readonly DateOnly January = new(2023, 1, 1);
    private static readonly DateOnly February = new(2023, 2, 1);
    private static FinancedAssetSnapshotValue Value(Guid id, FinancedAssetBalanceKind kind, decimal? amount) => new(id, kind, amount);
    private static FinancedAssetSnapshotValue[] Values(Guid id, decimal? valuation = 1_400_000m, decimal? debt = 600_000m, decimal? access = 250_000m) =>
        [Value(id, FinancedAssetBalanceKind.CurrentValuation, valuation), Value(id, FinancedAssetBalanceKind.OutstandingLoanBalance, debt), Value(id, FinancedAssetBalanceKind.AvailableToWithdraw, access)];

    [Fact]
    public async Task OneCaptureContainsBothTypesAndCopiesReportingContext()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var item = await new TrackedItemService(_database).CreateAsync(data.HouseholdId, data.GroupId, data.FirstPersonId,
            "Cash reserve", "ZAR", "ZA", true);
        var service = new SnapshotService(_database);
        var capture = await service.CreateAsync(data.HouseholdId, January, [new(item.Id, 100m)],
            financedAssetValues: Values(asset.Id));
        var saved = await service.GetAsync(data.HouseholdId, capture.Id);
        Assert.Equal(100m, Assert.Single(saved.ItemSnapshots).Amount);
        var entry = Assert.Single(saved.FinancedAssetSnapshots);
        Assert.Equal(saved.Id, entry.SnapshotId);
        Assert.Equal(asset.Id, entry.FinancedAssetId);
        Assert.Equal("Shared home", entry.AssetName);
        Assert.Equal(1_100_000m, entry.PurchasePrice);
        Assert.Equal(1_000_000m, entry.OriginalLoanAmount);
        Assert.Equal(2, entry.Owners.Count);
        Assert.All(entry.Owners, row => Assert.Equal(5_000, row.ShareBasisPoints));
        Assert.Equal(data.SecondPersonId, Assert.Single(entry.LoanHolders).SnapshotPerson.PersonId);
        Assert.Equal(2, entry.HouseholdAllocations.Count);
        Assert.All(entry.Owners, row => Assert.Same(saved.People.Single(person => person.Id == row.SnapshotPersonId), row.SnapshotPerson));
        Assert.Equal(800_000m, entry.CurrentEquity);
        Assert.Equal(400_000m, entry.LoanBalanceReduction);
        Assert.Equal(150_000m, entry.LoanBalanceReductionExcludingWithdrawableFunds);
        await new FinancedAssetService(_database).SetPersonAssociationsAsync(data.HouseholdId, asset.Id,
            [new(data.FirstPersonId, 10_000)], [data.FirstPersonId], false, []);
        await using (var context = _database.CreateDbContext())
        {
            (await context.FinancedAssets.SingleAsync()).Rename("Changed asset");
            (await context.Categories.SingleAsync()).Rename("Changed category");
            (await context.ItemGroups.SingleAsync()).Rename("Changed group");
            (await context.People.SingleAsync(person => person.Id == data.SecondPersonId)).Rename("Changed person");
            context.Entry(await context.FinancedAssets.SingleAsync()).Property(row => row.OriginalLoanAmount).CurrentValue = 999m;
            await context.SaveChangesAsync();
        }
        var later = await service.CreateNextAsync(data.HouseholdId, February);
        var current = Assert.Single(later.FinancedAssetSnapshots);
        Assert.False(current.IncludeInHousehold);
        Assert.Single(current.Owners);
        Assert.Equal("Changed asset", current.AssetName);
        Assert.Equal(999m, current.OriginalLoanAmount);
        var historical = Assert.Single((await service.GetAsync(data.HouseholdId, saved.Id)).FinancedAssetSnapshots);
        Assert.Equal("Shared home", historical.AssetName);
        Assert.Equal("Category A", historical.CategoryName);
        Assert.Equal("Group A", historical.GroupName);
        Assert.Equal("Person B", Assert.Single(historical.LoanHolders).SnapshotPerson.DisplayName);
        Assert.Equal(1_000_000m, historical.OriginalLoanAmount);
        Assert.Equal(2, historical.Owners.Count);
        Assert.True(historical.IncludeInHousehold);
    }

    [Theory]
    [InlineData(100, 200, 0, -100, 999800, 999800)]
    [InlineData(100, 0, 0, 100, 1000000, 1000000)]
    [InlineData(null, 200, null, null, 999800, null)]
    [InlineData(100, null, 0, null, null, null)]
    [InlineData(100, 200, null, -100, 999800, null)]
    [InlineData(100, 2000000, 0, -1999900, -1000000, -1000000)]
    public async Task CalculationsDependOnlyOnTheirInputsAndDoNotClampNegativeResults(
        int? valuation, int? debt, int? access, int? equity, int? reduction, int? excluding)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var capture = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January,
            financedAssetValues: Values(asset.Id, valuation, debt, access));
        var entry = Assert.Single(capture.FinancedAssetSnapshots);
        Assert.Equal((decimal?)equity, entry.CurrentEquity);
        Assert.Equal((decimal?)reduction, entry.LoanBalanceReduction);
        Assert.Equal((decimal?)excluding, entry.LoanBalanceReductionExcludingWithdrawableFunds);
    }

    [Fact]
    public async Task OutOfRangeReductionThrowsAnExplicitOverflowWithoutChangingRecordedBalances()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var service = new SnapshotService(_database);
        var capture = await service.CreateAsync(data.HouseholdId, January,
            financedAssetValues: Values(asset.Id, 0m, decimal.MaxValue, asset.OriginalLoanAmount + 1m));
        var saved = Assert.Single((await service.GetAsync(data.HouseholdId, capture.Id)).FinancedAssetSnapshots);

        var exception = Assert.Throws<OverflowException>(() => saved.LoanBalanceReductionExcludingWithdrawableFunds);
        Assert.Contains("loan balance reduction excluding withdrawable funds", exception.Message);
        Assert.IsType<OverflowException>(exception.InnerException);
        Assert.Equal(decimal.MaxValue, saved.OutstandingLoanBalance);
        Assert.Equal(asset.OriginalLoanAmount + 1m, saved.AvailableToWithdraw);
        Assert.Equal(-decimal.MaxValue, saved.CurrentEquity);
        Assert.Equal(asset.OriginalLoanAmount - decimal.MaxValue, saved.LoanBalanceReduction);

        await service.CorrectValuesAsync(data.HouseholdId, capture.Id, capture.Revision,
            financedAssetValues: [Value(asset.Id, FinancedAssetBalanceKind.AvailableToWithdraw, asset.OriginalLoanAmount)]);
        var corrected = Assert.Single((await service.GetAsync(data.HouseholdId, capture.Id)).FinancedAssetSnapshots);
        Assert.Equal(-decimal.MaxValue, corrected.LoanBalanceReductionExcludingWithdrawableFunds);
    }

    [Fact]
    public async Task BalancesCarryAndClearIndependentlyWithoutSearchingPastMissingValues()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var service = new SnapshotService(_database);
        var first = await service.CreateAsync(data.HouseholdId, January, datePrecision: DatePrecision.Month,
            financedAssetValues: Values(asset.Id));
        var next = await service.CreateNextAsync(data.HouseholdId, February, financedAssetValues:
            [Value(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 590_000m), Value(asset.Id, FinancedAssetBalanceKind.AvailableToWithdraw, null)]);
        var entry = Assert.Single(next.FinancedAssetSnapshots);
        Assert.Equal(1_400_000m, entry.CurrentValuation);
        Assert.Equal(SnapshotValueOrigin.CarriedForward, entry.CurrentValuationValueOrigin);
        Assert.Equal(January, entry.CurrentValuationLastConfirmedDate);
        Assert.Equal(DatePrecision.Month, entry.CurrentValuationLastConfirmedPrecision);
        Assert.Equal(Assert.Single(first.FinancedAssetSnapshots).Id, entry.CurrentValuationCarriedFromSnapshotId);
        Assert.Equal(SnapshotValueOrigin.Confirmed, entry.OutstandingLoanBalanceValueOrigin);
        Assert.Equal(February, entry.OutstandingLoanBalanceLastConfirmedDate);
        Assert.Null(entry.AvailableToWithdraw);
        Assert.Null(entry.AvailableToWithdrawCarriedFromSnapshotId);
        Assert.Equal(810_000m, entry.CurrentEquity);
        Assert.Null(entry.LoanBalanceReductionExcludingWithdrawableFunds);
        var third = await service.CreateNextAsync(data.HouseholdId, new DateOnly(2023, 3, 1));
        var thirdEntry = Assert.Single(third.FinancedAssetSnapshots);
        Assert.Null(thirdEntry.AvailableToWithdraw);
        Assert.Equal(entry.Id, thirdEntry.CurrentValuationCarriedFromSnapshotId);
        Assert.Equal(January, thirdEntry.CurrentValuationLastConfirmedDate);
        Assert.Equal(February, thirdEntry.OutstandingLoanBalanceLastConfirmedDate);
        var fresh = await service.CreateAsync(data.HouseholdId, new DateOnly(2023, 4, 1));
        Assert.Null(Assert.Single(fresh.FinancedAssetSnapshots).CurrentValuation);
    }

    [Theory]
    [InlineData(FinancedAssetBalanceKind.CurrentValuation)]
    [InlineData(FinancedAssetBalanceKind.OutstandingLoanBalance)]
    [InlineData(FinancedAssetBalanceKind.AvailableToWithdraw)]
    public async Task EveryBalancePreservesExactAmountsAndCanBeReconfirmedAtZero(FinancedAssetBalanceKind kind)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var service = new SnapshotService(_database);
        var first = await service.CreateAsync(data.HouseholdId, January,
            financedAssetValues: [Value(asset.Id, kind, 0.0000000000000000000000000001m)]);
        var second = await service.CreateNextAsync(data.HouseholdId, February, datePrecision: DatePrecision.Month);
        var saved = Assert.Single((await service.GetAsync(data.HouseholdId, second.Id)).FinancedAssetSnapshots);
        var amount = kind switch { FinancedAssetBalanceKind.CurrentValuation => saved.CurrentValuation,
            FinancedAssetBalanceKind.OutstandingLoanBalance => saved.OutstandingLoanBalance, _ => saved.AvailableToWithdraw };
        Assert.Equal(0.0000000000000000000000000001m, amount);
        var carriedMetadata = kind switch
        {
            FinancedAssetBalanceKind.CurrentValuation => (saved.CurrentValuationValueOrigin, saved.CurrentValuationLastConfirmedDate,
                saved.CurrentValuationLastConfirmedPrecision, saved.CurrentValuationCarriedFromSnapshotId),
            FinancedAssetBalanceKind.OutstandingLoanBalance => (saved.OutstandingLoanBalanceValueOrigin, saved.OutstandingLoanBalanceLastConfirmedDate,
                saved.OutstandingLoanBalanceLastConfirmedPrecision, saved.OutstandingLoanBalanceCarriedFromSnapshotId),
            _ => (saved.AvailableToWithdrawValueOrigin, saved.AvailableToWithdrawLastConfirmedDate,
                saved.AvailableToWithdrawLastConfirmedPrecision, saved.AvailableToWithdrawCarriedFromSnapshotId)
        };
        Assert.Equal((SnapshotValueOrigin.CarriedForward, (DateOnly?)January, (DatePrecision?)DatePrecision.Day,
            (Guid?)Assert.Single(first.FinancedAssetSnapshots).Id), carriedMetadata);

        await service.CorrectValuesAsync(data.HouseholdId, second.Id, second.Revision,
            financedAssetValues: [Value(asset.Id, kind, 0m)]);
        var row = Assert.Single((await service.GetAsync(data.HouseholdId, second.Id)).FinancedAssetSnapshots);
        Assert.Equal(0m, kind switch { FinancedAssetBalanceKind.CurrentValuation => row.CurrentValuation,
            FinancedAssetBalanceKind.OutstandingLoanBalance => row.OutstandingLoanBalance, _ => row.AvailableToWithdraw });
        var confirmedMetadata = kind switch
        {
            FinancedAssetBalanceKind.CurrentValuation => (row.CurrentValuationValueOrigin, row.CurrentValuationLastConfirmedDate,
                row.CurrentValuationLastConfirmedPrecision, row.CurrentValuationCarriedFromSnapshotId),
            FinancedAssetBalanceKind.OutstandingLoanBalance => (row.OutstandingLoanBalanceValueOrigin, row.OutstandingLoanBalanceLastConfirmedDate,
                row.OutstandingLoanBalanceLastConfirmedPrecision, row.OutstandingLoanBalanceCarriedFromSnapshotId),
            _ => (row.AvailableToWithdrawValueOrigin, row.AvailableToWithdrawLastConfirmedDate,
                row.AvailableToWithdrawLastConfirmedPrecision, row.AvailableToWithdrawCarriedFromSnapshotId)
        };
        Assert.Equal((SnapshotValueOrigin.Confirmed, (DateOnly?)February, (DatePrecision?)DatePrecision.Month, (Guid?)null), confirmedMetadata);
    }

    [Fact]
    public async Task CorrectionsAreAtomicAcrossBothTypesAndLaterCopiesRemainIndependent()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var item = await new TrackedItemService(_database).CreateAsync(data.HouseholdId, data.GroupId, data.FirstPersonId,
            "Cash", "ZAR", "ZA", true);
        var service = new SnapshotService(_database);
        var original = await service.CreateAsync(data.HouseholdId, January, [new(item.Id, 50m)], financedAssetValues: Values(asset.Id));
        var later = await service.CreateNextAsync(data.HouseholdId, February);
        await new FinancedAssetService(_database).SetRetiredAsync(data.HouseholdId, asset.Id, true);
        var changed = await service.CorrectValuesAsync(data.HouseholdId, original.Id, original.Revision,
            [new(item.Id, 60m)], [Value(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 1_500_000m)]);
        Assert.NotEqual(original.Revision, changed.Revision);
        Assert.Equal(60m, Assert.Single(changed.ItemSnapshots).Amount);
        Assert.Equal(900_000m, Assert.Single(changed.FinancedAssetSnapshots).CurrentEquity);
        await Assert.ThrowsAsync<SnapshotConflictException>(() => service.CorrectValuesAsync(data.HouseholdId, original.Id, original.Revision,
            financedAssetValues: [Value(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 1m)]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.CorrectValuesAsync(data.HouseholdId, original.Id, changed.Revision,
            [new(item.Id, 0m)], [Value(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, -1m)]));
        var retained = await service.GetAsync(data.HouseholdId, original.Id);
        Assert.Equal(changed.Revision, retained.Revision);
        Assert.Equal(60m, Assert.Single(retained.ItemSnapshots).Amount);
        var independent = Assert.Single((await service.GetAsync(data.HouseholdId, later.Id)).FinancedAssetSnapshots);
        Assert.Equal(1_400_000m, independent.CurrentValuation);
        Assert.Equal(Assert.Single(original.FinancedAssetSnapshots).Id, independent.CurrentValuationCarriedFromSnapshotId);
        Assert.Empty((await service.CreateNextAsync(data.HouseholdId, new DateOnly(2023, 3, 1))).FinancedAssetSnapshots);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("foreign")]
    [InlineData("retired")]
    [InlineData("negative")]
    [InlineData("duplicate")]
    [InlineData("invalid-kind")]
    [InlineData("no-facility")]
    public async Task InvalidCapturesDoNotSaveAnyPartialHistory(string scenario)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var other = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database), withdrawal: scenario != "no-facility");
        var foreign = await other.CreateAssetAsync(new(_database));
        if (scenario == "retired") await new FinancedAssetService(_database).SetRetiredAsync(data.HouseholdId, asset.Id, true);
        var id = scenario == "unknown" ? Guid.NewGuid() : scenario == "foreign" ? foreign.Id : asset.Id;
        var kind = scenario == "no-facility" ? FinancedAssetBalanceKind.AvailableToWithdraw : FinancedAssetBalanceKind.CurrentValuation;
        if (scenario == "invalid-kind") kind = (FinancedAssetBalanceKind)99;
        FinancedAssetSnapshotValue[] values = [Value(id, kind, scenario == "negative" ? -1m : 10m)];
        if (scenario == "duplicate") values = [values[0], values[0]];
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new SnapshotService(_database).CreateAsync(data.HouseholdId, January, financedAssetValues: values));
        await using var context = _database.CreateDbContext();
        Assert.Equal(0, await context.Snapshots.CountAsync());
        Assert.Equal(0, await context.SnapshotPeople.CountAsync());
        Assert.Equal(0, await context.FinancedAssetSnapshots.CountAsync());
    }

    [Fact]
    public async Task FacilityChangesUseCurrentSetupWhileCorrectionsUseSavedSetup()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var service = new SnapshotService(_database);
        var original = await service.CreateAsync(data.HouseholdId, January, financedAssetValues: Values(asset.Id));
        await using (var context = _database.CreateDbContext())
        {
            var current = await context.FinancedAssets.SingleAsync();
            context.Entry(current).Property(row => row.HasWithdrawalFacility).CurrentValue = false;
            await context.SaveChangesAsync();
        }
        var without = await service.CreateNextAsync(data.HouseholdId, February);
        var entry = Assert.Single(without.FinancedAssetSnapshots);
        Assert.Null(entry.AvailableToWithdraw);
        Assert.Equal(800_000m, entry.CurrentEquity);
        Assert.Null(entry.LoanBalanceReductionExcludingWithdrawableFunds);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CorrectValuesAsync(data.HouseholdId, without.Id, without.Revision,
            financedAssetValues: [Value(asset.Id, FinancedAssetBalanceKind.AvailableToWithdraw, 1m)]));
        var corrected = await service.CorrectValuesAsync(data.HouseholdId, original.Id, original.Revision,
            financedAssetValues: [Value(asset.Id, FinancedAssetBalanceKind.AvailableToWithdraw, 240_000m)]);
        Assert.Equal(240_000m, Assert.Single(corrected.FinancedAssetSnapshots).AvailableToWithdraw);
    }

    [Fact]
    public async Task ReplacedAndReactivatedAssetsDoNotFindOlderOrSameNamedBalances()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var assets = new FinancedAssetService(_database);
        var first = await data.CreateAssetAsync(assets);
        var service = new SnapshotService(_database);
        await service.CreateAsync(data.HouseholdId, January, financedAssetValues: Values(first.Id));
        await assets.SetRetiredAsync(data.HouseholdId, first.Id, true);
        var replacement = await data.CreateAssetAsync(assets);
        var next = await service.CreateNextAsync(data.HouseholdId, February);
        Assert.Equal(replacement.Id, Assert.Single(next.FinancedAssetSnapshots).FinancedAssetId);
        Assert.Null(Assert.Single(next.FinancedAssetSnapshots).CurrentValuation);
        await assets.SetRetiredAsync(data.HouseholdId, first.Id, false);
        var restored = await service.CreateNextAsync(data.HouseholdId, new DateOnly(2023, 3, 1));
        Assert.Equal(2, restored.FinancedAssetSnapshots.Count);
        Assert.All(restored.FinancedAssetSnapshots, row => Assert.Null(row.CurrentValuation));
    }
}
