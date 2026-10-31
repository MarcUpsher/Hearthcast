using Hearthcast.Data.Classification;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries;
using Hearthcast.Data.Summaries.Models;
using Hearthcast.Data.Tests.FinancedAssets;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Tests.TrackedItems;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Tests.Summaries;

public sealed class SnapshotSummaryTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private static readonly DateOnly January = new(2023, 1, 1);
    private static readonly DateOnly February = new(2023, 2, 1);
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task WorkedExampleIncludesAllHoldingsAndKeepsCurrenciesAndComponentsSeparate()
    {
        var households = new HouseholdService(_database);
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var withChild = await households.AddPersonAsync(data.HouseholdId, "Child", false);
        var child = withChild.People.Single(person => !person.ParticipatesInHouseholdFinances);
        var classification = new ClassificationService(_database);
        var asset = await data.CreateAssetAsync(new(_database), originalLoan: 1_500_000m);
        var savings = await classification.CreateGroupAsync(data.HouseholdId, asset.Group.CategoryId, "Savings");
        var debtCategory = await classification.CreateCategoryAsync(data.HouseholdId, "Borrowing", FinancialType.Debt);
        var loans = await classification.CreateGroupAsync(data.HouseholdId, debtCategory.Id, "Personal loans");
        var items = new TrackedItemService(_database);
        var pension = await items.CreateAsync(data.HouseholdId, data.GroupId, data.FirstPersonId, "Pension", "ZAR", "ZA", true);
        var cash = await items.CreateAsync(data.HouseholdId, savings.Id, data.SecondPersonId, "Emergency", "ZAR", "ZA", true,
            [new(data.FirstPersonId, 6_000), new(data.SecondPersonId, 4_000)]);
        var childCash = await items.CreateAsync(data.HouseholdId, savings.Id, child.Id, "Child savings", "ZAR", "ZA", false);
        var debt = await items.CreateAsync(data.HouseholdId, loans.Id, data.FirstPersonId, "Loan", "ZAR", "ZA", true);
        var isa = await items.CreateAsync(data.HouseholdId, savings.Id, data.FirstPersonId, "ISA", "GBP", "GB", true);
        var snapshot = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January,
            [new(pension.Id, 200_000m), new(cash.Id, 100_000m), new(childCash.Id, 10_000m), new(debt.Id, 40_000m), new(isa.Id, 5_000m)],
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 2_000_000m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 1_200_000m)]);

        var result = await new SnapshotSummaryService(_database).GetAsync(data.HouseholdId, snapshot.Id);
        Assert.Equal(snapshot.Id, result.SnapshotId);
        Assert.Equal(snapshot.Revision, result.Revision);
        Assert.Equal(January, result.SnapshotDate);
        Assert.Equal(DatePrecision.Day, result.DatePrecision);
        Assert.Equal(3, result.People.Count);
        Assert.Equal(new[] { "GBP", "ZAR" }, result.Currencies.Select(currency => currency.CurrencyCode));
        var zar = result.Currencies.Single(currency => currency.CurrencyCode == "ZAR");
        Assert.Equal(310_000m, zar.AllRecordedTotals.OrdinaryAssets.KnownSubtotal);
        Assert.Equal(40_000m, zar.AllRecordedTotals.OrdinaryDebts.KnownSubtotal);
        Assert.Equal(800_000m, zar.AllRecordedTotals.FinancedAssetEquity.KnownSubtotal);
        Assert.Equal(1_070_000m, zar.AllRecordedTotals.KnownNetPosition);
        Assert.True(zar.AllRecordedTotals.IsComplete);
        Assert.Equal(5_000m, result.Currencies[0].AllRecordedTotals.KnownNetPosition);
        Assert.Equal(-40_000m, zar.Categories.Single(category => category.CategoryId == debtCategory.Id).AllRecordedTotals.KnownNetPosition);
        var savingsGroup = zar.Categories.SelectMany(category => category.Groups).Single(group => group.GroupId == savings.Id);
        Assert.Equal(110_000m, savingsGroup.AllRecordedTotals.KnownNetPosition);
        var excluded = savingsGroup.Items.Single(item => item.TrackedItemId == childCash.Id);
        Assert.False(excluded.IncludeInHousehold);
        Assert.Empty(excluded.HouseholdAllocations);
        Assert.Equal(child.Id, result.People.Single(person => person.SnapshotPersonId == excluded.HolderSnapshotPersonId).PersonId);
        var emergency = savingsGroup.Items.Single(item => item.TrackedItemId == cash.Id);
        Assert.Equal(data.SecondPersonId, result.People.Single(person => person.SnapshotPersonId == emergency.HolderSnapshotPersonId).PersonId);
        Assert.Equal(2, emergency.HouseholdAllocations.Count);
        var allocationsByPerson = emergency.HouseholdAllocations.ToDictionary(
            share => result.People.Single(person => person.SnapshotPersonId == share.SnapshotPersonId).PersonId,
            share => share.ShareBasisPoints);
        Assert.Equal(6_000, allocationsByPerson[data.FirstPersonId]);
        Assert.Equal(4_000, allocationsByPerson[data.SecondPersonId]);
        var home = Assert.Single(zar.Categories.SelectMany(category => category.Groups).SelectMany(group => group.FinancedAssets));
        Assert.Equal(asset.Id, home.FinancedAssetId);
        Assert.Equal(800_000m, home.CurrentEquity);
        Assert.Equal(300_000m, home.LoanBalanceReduction);
        Assert.Null(home.AvailableToWithdraw.Amount);
        Assert.Null(home.LoanBalanceReductionExcludingWithdrawableFunds);
        Assert.Equal(2, home.Owners.Count);
        Assert.Equal(data.SecondPersonId, result.People.Single(person => person.SnapshotPersonId == Assert.Single(home.LoanHolderSnapshotPersonIds)).PersonId);
    }

    [Fact]
    public async Task MissingAssetsAndDebtsRemainVisibleAndMakeOnlyAffectedSubtotalsIncomplete()
    {
        var data = await TrackedItemTestSetup.CreateAsync(_database);
        var items = new TrackedItemService(_database);
        var known = await data.CreateItemAsync(items);
        await items.CreateAsync(data.HouseholdId, data.GroupId, data.OtherPersonId, "Unknown saving", "GBP", "GB", false);
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(data.HouseholdId, "Debt", FinancialType.Debt);
        var group = await classification.CreateGroupAsync(data.HouseholdId, category.Id, "Loans");
        await items.CreateAsync(data.HouseholdId, group.Id, data.HolderPersonId, "Unknown debt", "GBP", "GB", true);
        var snapshot = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January, [new(known.Id, 100m)]);
        var result = await new SnapshotSummaryService(_database).GetAsync(data.HouseholdId, snapshot.Id);
        var currency = Assert.Single(result.Currencies);
        Assert.Equal(100m, currency.AllRecordedTotals.KnownNetPosition);
        Assert.Equal(new(100m, 1, 0), currency.AllRecordedTotals.OrdinaryAssets);
        Assert.Equal(new(0m, 1, 0), currency.AllRecordedTotals.OrdinaryDebts);
        Assert.True(currency.AllRecordedTotals.FinancedAssetEquity.IsComplete);
        Assert.False(currency.AllRecordedTotals.IsComplete);
        Assert.Equal(2, currency.Categories.SelectMany(category => category.Groups).SelectMany(group => group.Items)
            .Count(item => item.Balance.Amount is null && item.Balance.ValueOrigin == SnapshotValueOrigin.Unrecorded));
    }

    [Theory]
    [InlineData(null, 200, null, true)]
    [InlineData(100, null, 50, true)]
    [InlineData(null, null, null, true)]
    [InlineData(100, 200, null, false)]
    [InlineData(0, 0, 0, false)]
    public async Task EquityCompletenessIgnoresWithdrawableFundsAndRetainsZeroAndNegativeEquity(
        int? valuation, int? debt, int? access, bool missing)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var snapshot = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January,
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, valuation),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, debt),
                new(asset.Id, FinancedAssetBalanceKind.AvailableToWithdraw, access)]);
        var currency = Assert.Single((await new SnapshotSummaryService(_database).GetAsync(data.HouseholdId, snapshot.Id)).Currencies);
        Assert.Equal(missing ? 1 : 0, currency.AllRecordedTotals.FinancedAssetEquity.MissingEntryCount);
        Assert.Equal(!missing, currency.AllRecordedTotals.IsComplete);
        Assert.Equal(missing ? 0m : (decimal)(valuation!.Value - debt!.Value), currency.AllRecordedTotals.KnownNetPosition);
    }

    [Fact]
    public async Task CarriesRetainProvenanceAndCountKnownEquityOnceRegardlessOfCarriedBalanceCount()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var item = await new TrackedItemService(_database).CreateAsync(data.HouseholdId, data.GroupId,
            data.FirstPersonId, "Tiny holding", "ZAR", "ZA", true);
        var service = new SnapshotService(_database);
        var source = await service.CreateAsync(data.HouseholdId, January, [new(item.Id, 0.0000000000000000000000000001m)],
            datePrecision: DatePrecision.Month,
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 100m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 100m),
                new(asset.Id, FinancedAssetBalanceKind.AvailableToWithdraw, 10m)]);
        var next = await service.CreateNextAsync(data.HouseholdId, February);
        var currency = Assert.Single((await new SnapshotSummaryService(_database).GetAsync(data.HouseholdId, next.Id)).Currencies);
        Assert.Equal(0.0000000000000000000000000001m, currency.AllRecordedTotals.KnownNetPosition);
        Assert.Equal(1, currency.AllRecordedTotals.OrdinaryAssets.CarriedForwardEntryCount);
        Assert.Equal(new(0m, 0, 1), currency.AllRecordedTotals.FinancedAssetEquity);
        var group = Assert.Single(Assert.Single(currency.Categories).Groups);
        var balance = Assert.Single(group.Items).Balance;
        Assert.Equal(January, balance.LastConfirmedDate);
        Assert.Equal(DatePrecision.Month, balance.LastConfirmedPrecision);
        Assert.Equal(Assert.Single(source.ItemSnapshots).Id, balance.CarriedFromEntryId);
        var home = Assert.Single(group.FinancedAssets);
        Assert.Equal(SnapshotValueOrigin.CarriedForward, home.AvailableToWithdraw.ValueOrigin);
        Assert.Equal(Assert.Single(source.FinancedAssetSnapshots).Id, home.CurrentValuation.CarriedFromEntryId);

        var cleared = await service.CorrectValuesAsync(data.HouseholdId, next.Id, next.Revision,
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, null)]);
        var incomplete = Assert.Single((await new SnapshotSummaryService(_database).GetAsync(data.HouseholdId, cleared.Id)).Currencies);
        Assert.Equal(new(0m, 1, 0), incomplete.AllRecordedTotals.FinancedAssetEquity);
    }

    [Fact]
    public async Task ACarriedWithdrawalBalanceDoesNotMarkFreshEquityAsCarried()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var snapshots = new SnapshotService(_database);
        await snapshots.CreateAsync(data.HouseholdId, January,
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 100m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 80m),
                new(asset.Id, FinancedAssetBalanceKind.AvailableToWithdraw, 10m)]);
        var next = await snapshots.CreateNextAsync(data.HouseholdId, February,
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 110m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 75m)]);
        var currency = Assert.Single((await new SnapshotSummaryService(_database).GetAsync(data.HouseholdId, next.Id)).Currencies);
        Assert.Equal(new(35m, 0, 0), currency.AllRecordedTotals.FinancedAssetEquity);
        var entry = Assert.Single(Assert.Single(Assert.Single(currency.Categories).Groups).FinancedAssets);
        Assert.Equal(SnapshotValueOrigin.CarriedForward, entry.AvailableToWithdraw.ValueOrigin);
    }

    [Fact]
    public async Task ExcludedFinancedAssetWithoutWithdrawalFacilityStillContributesItsFullEquity()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database), include: false, withdrawal: false);
        var capture = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January,
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 100m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 25m)]);
        var currency = Assert.Single((await new SnapshotSummaryService(_database).GetAsync(data.HouseholdId, capture.Id)).Currencies);
        Assert.Equal(new(75m, 0, 0), currency.AllRecordedTotals.FinancedAssetEquity);
        var entry = Assert.Single(Assert.Single(Assert.Single(currency.Categories).Groups).FinancedAssets);
        Assert.False(entry.IncludeInHousehold);
        Assert.False(entry.HasWithdrawalFacility);
        Assert.Empty(entry.HouseholdAllocations);
        Assert.Equal(SnapshotValueOrigin.Unrecorded, entry.AvailableToWithdraw.ValueOrigin);
        Assert.Null(entry.LoanBalanceReductionExcludingWithdrawableFunds);
    }

    [Fact]
    public async Task HistoricalSummaryIgnoresCurrentSetupAndChangesOnlyAfterSnapshotCorrection()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var items = new TrackedItemService(_database);
        var item = await items.CreateAsync(data.HouseholdId, data.GroupId, data.FirstPersonId, "Original item", "ZAR", "ZA", true);
        var snapshots = new SnapshotService(_database);
        var capture = await snapshots.CreateAsync(data.HouseholdId, January, [new(item.Id, 100m)],
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 1_000m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 600m)]);
        var savedCapture = await snapshots.GetAsync(data.HouseholdId, capture.Id);
        var summaries = new SnapshotSummaryService(_database);
        await items.SetHouseholdAllocationAsync(data.HouseholdId, item.Id, false);
        await items.SetRetiredAsync(data.HouseholdId, item.Id, true);
        await new FinancedAssetService(_database).SetPersonAssociationsAsync(data.HouseholdId, asset.Id,
            [new(data.FirstPersonId, 10_000)], [data.FirstPersonId], false, []);
        await new FinancedAssetService(_database).SetRetiredAsync(data.HouseholdId, asset.Id, true);
        await using (var context = _database.CreateDbContext())
        {
            (await context.Households.SingleAsync()).Rename("New household");
            (await context.People.SingleAsync(person => person.Id == data.FirstPersonId)).Rename("New person");
            (await context.Categories.SingleAsync()).Rename("New category");
            (await context.ItemGroups.SingleAsync()).Rename("New group");
            (await context.TrackedItems.SingleAsync()).Rename("New item");
            (await context.FinancedAssets.SingleAsync()).Rename("New asset");
            await context.SaveChangesAsync();
        }
        var original = await summaries.GetAsync(data.HouseholdId, capture.Id);
        Assert.Equal("Household A", original.HouseholdName);
        Assert.Equal("Person A", original.People.Single(person => person.PersonId == data.FirstPersonId).DisplayName);
        var category = Assert.Single(Assert.Single(original.Currencies).Categories);
        Assert.Equal("Category A", category.CategoryName);
        var group = Assert.Single(category.Groups);
        Assert.Equal("Group A", group.GroupName);
        Assert.Equal("Original item", Assert.Single(group.Items).ItemName);
        Assert.True(Assert.Single(group.Items).IncludeInHousehold);
        var home = Assert.Single(group.FinancedAssets);
        Assert.Equal("Shared home", home.AssetName);
        Assert.Equal(2, home.Owners.Count);
        Assert.True(home.IncludeInHousehold);
        Assert.Equal(500m, group.AllRecordedTotals.KnownNetPosition);
        Assert.Equal(capture.Revision, original.Revision);
        Assert.Equal(savedCapture.CreatedAtUtc, original.CreatedAtUtc);
        Assert.Equal(savedCapture.UpdatedAtUtc, original.UpdatedAtUtc);
        var corrected = await snapshots.CorrectValuesAsync(data.HouseholdId, capture.Id, capture.Revision,
            [new(item.Id, 200m)], [new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 500m)]);
        var updated = await summaries.GetAsync(data.HouseholdId, capture.Id);
        Assert.Equal(corrected.Revision, updated.Revision);
        Assert.Equal(700m, Assert.Single(updated.Currencies).AllRecordedTotals.KnownNetPosition);
        Assert.Equal(500m, Assert.Single(original.Currencies).AllRecordedTotals.KnownNetPosition);
    }

    [Fact]
    public async Task EqualLabelsDoNotMergeDifferentCategoriesOrGroups()
    {
        var data = await TrackedItemTestSetup.CreateAsync(_database);
        var classification = new ClassificationService(_database);
        var firstCategory = await classification.CreateCategoryAsync(data.HouseholdId, "Same", FinancialType.Asset);
        var secondCategory = await classification.CreateCategoryAsync(data.HouseholdId, "Same", FinancialType.Debt);
        var firstGroup = await classification.CreateGroupAsync(data.HouseholdId, firstCategory.Id, "Same");
        var secondGroup = await classification.CreateGroupAsync(data.HouseholdId, firstCategory.Id, "Same");
        var thirdGroup = await classification.CreateGroupAsync(data.HouseholdId, secondCategory.Id, "Same");
        var items = new TrackedItemService(_database);
        var a = await items.CreateAsync(data.HouseholdId, firstGroup.Id, data.HolderPersonId, "Same", "GBP", "GB", true);
        var b = await items.CreateAsync(data.HouseholdId, secondGroup.Id, data.HolderPersonId, "Same", "GBP", "GB", true);
        var c = await items.CreateAsync(data.HouseholdId, thirdGroup.Id, data.HolderPersonId, "Same", "GBP", "GB", true);
        var capture = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January,
            [new(a.Id, 10m), new(b.Id, 20m), new(c.Id, 5m)]);
        var currency = Assert.Single((await new SnapshotSummaryService(_database).GetAsync(data.HouseholdId, capture.Id)).Currencies);
        Assert.Equal(25m, currency.AllRecordedTotals.KnownNetPosition);
        Assert.Equal(2, currency.Categories.Count);
        Assert.Equal(2, currency.Categories.Single(category => category.CategoryId == firstCategory.Id).Groups.Count);
        Assert.Equal(3, currency.Categories.SelectMany(category => category.Groups).SelectMany(group => group.Items).Count());
    }

    [Fact]
    public async Task EmptySnapshotPreservesRosterWithoutInventingCurrenciesOrCurrentInventory()
    {
        var data = await TrackedItemTestSetup.CreateAsync(_database);
        var snapshot = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January);
        await data.CreateItemAsync(new(_database));
        var result = await new SnapshotSummaryService(_database).GetAsync(data.HouseholdId, snapshot.Id);
        Assert.Equal(2, result.People.Count);
        Assert.Empty(result.Currencies);
    }

    [Fact]
    public async Task UnknownOrOtherHouseholdSnapshotsAreNotReturned()
    {
        var household = await new HouseholdService(_database).CreateAsync("One", "Person");
        var other = await new HouseholdService(_database).CreateAsync("Two", "Person");
        var capture = await new SnapshotService(_database).CreateAsync(household.Id, January);
        var service = new SnapshotSummaryService(_database);
        await Assert.ThrowsAsync<Hearthcast.Data.Validation.ResourceNotFoundException>(() => service.GetAsync(other.Id, capture.Id));
        await Assert.ThrowsAsync<Hearthcast.Data.Validation.ResourceNotFoundException>(() => service.GetAsync(household.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task CancelledRequestStopsWithoutProducingASummary()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SnapshotSummaryService(_database).GetAsync(Guid.NewGuid(), Guid.NewGuid(), cancellation.Token));
    }

    [Fact]
    public async Task NetPositionAtDecimalBoundaryCanBeReturnedAndSerialized()
    {
        var capture = await CreateNetPositionBoundaryAsync("group", decimal.MaxValue - 1m);
        var summary = await new SnapshotSummaryService(_database).GetAsync(capture.HouseholdId, capture.Id);
        Assert.Equal(decimal.MaxValue, Assert.Single(summary.Currencies).AllRecordedTotals.KnownNetPosition);
        var json = System.Text.Json.JsonSerializer.Serialize(summary);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(decimal.MaxValue, document.RootElement.GetProperty("Currencies")[0]
            .GetProperty("AllRecordedTotals").GetProperty("KnownNetPosition").GetDecimal());
    }

    [Theory]
    [InlineData("group")]
    [InlineData("category")]
    [InlineData("currency")]
    public async Task OutOfRangeNetPositionThrowsBeforeReturningTheSummary(string level)
    {
        var capture = await CreateNetPositionBoundaryAsync(level, decimal.MaxValue);
        await Assert.ThrowsAsync<OverflowException>(() =>
            new SnapshotSummaryService(_database).GetAsync(capture.HouseholdId, capture.Id));
    }

    private async Task<Snapshot> CreateNetPositionBoundaryAsync(string level, decimal ordinaryAmount)
    {
        var data = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await data.CreateItemAsync(new(_database));
        var assetGroupId = data.GroupId;
        if (level != "group")
        {
            var classification = new ClassificationService(_database);
            var categoryId = item.Group.CategoryId;
            if (level == "currency")
            {
                categoryId = (await classification.CreateCategoryAsync(data.HouseholdId, "Financed", FinancialType.Asset)).Id;
            }
            assetGroupId = (await classification.CreateGroupAsync(data.HouseholdId, categoryId, "Financed")).Id;
        }
        var asset = await new FinancedAssetService(_database).CreateAsync(data.HouseholdId, assetGroupId,
            "Asset", "GBP", "GB", null, null, 0m, false,
            [new(data.HolderPersonId, 10_000)], [], true, [new(data.HolderPersonId, 10_000)]);
        return await new SnapshotService(_database).CreateAsync(data.HouseholdId, January, [new(item.Id, ordinaryAmount)],
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 1m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 0m)]);
    }

    [Fact]
    public async Task OutOfRangeSubtotalThrowsInsteadOfClampingOrReturningAPartialResult()
    {
        var data = await TrackedItemTestSetup.CreateAsync(_database);
        var items = new TrackedItemService(_database);
        var first = await data.CreateItemAsync(items);
        var second = await items.CreateAsync(data.HouseholdId, data.GroupId, data.OtherPersonId, "Another", "GBP", "GB", true);
        var capture = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January,
            [new(first.Id, decimal.MaxValue), new(second.Id, decimal.MaxValue)]);
        await Assert.ThrowsAsync<OverflowException>(() => new SnapshotSummaryService(_database).GetAsync(data.HouseholdId, capture.Id));
    }
}
