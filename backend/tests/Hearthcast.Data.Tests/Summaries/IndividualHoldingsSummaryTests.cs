using System.Globalization;
using Hearthcast.Data.Classification;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries;
using Hearthcast.Data.Summaries.Models.IndividualHoldings;
using Hearthcast.Data.Tests.FinancedAssets;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Tests.Summaries;

public sealed class IndividualHoldingsSummaryTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private static readonly DateOnly January = new(2023, 1, 1);
    private static readonly DateOnly February = new(2023, 2, 1);
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task HoldingsFollowActualHoldersAndOwnersAndReconcileAcrossCurrencies()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var householdService = new HouseholdService(_database);
        var updated = await householdService.AddPersonAsync(data.HouseholdId, "Child", false);
        var child = updated.People.Single(person => !person.ParticipatesInHouseholdFinances);
        await householdService.AddPersonAsync(data.HouseholdId, "No holdings", false);
        var asset = await data.CreateAssetAsync(new(_database),
            owners: [new(data.FirstPersonId, 2_500), new(data.SecondPersonId, 7_500)], withdrawal: false);
        var items = new TrackedItemService(_database);
        var a = await items.CreateAsync(data.HouseholdId, data.GroupId, data.FirstPersonId, "Pension", "ZAR", "ZA", true);
        var b = await items.CreateAsync(data.HouseholdId, data.GroupId, data.SecondPersonId, "Savings", "ZAR", "ZA", true,
            [new(data.FirstPersonId, 6_000), new(data.SecondPersonId, 4_000)]);
        var c = await items.CreateAsync(data.HouseholdId, data.GroupId, child.Id, "Savings", "ZAR", "ZA", false);
        var gbp = await items.CreateAsync(data.HouseholdId, data.GroupId, data.FirstPersonId, "ISA", "GBP", "GB", true);
        var classification = new ClassificationService(_database);
        var debtCategory = await classification.CreateCategoryAsync(data.HouseholdId, "Debt", FinancialType.Debt);
        var debtGroup = await classification.CreateGroupAsync(data.HouseholdId, debtCategory.Id, "Loans");
        var debt = await items.CreateAsync(data.HouseholdId, debtGroup.Id, data.FirstPersonId, "Loan", "ZAR", "ZA", true);
        var capture = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January,
            [new(a.Id, 100m), new(b.Id, 200m), new(c.Id, 10m), new(gbp.Id, 5m), new(debt.Id, 40m)],
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 1_000m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 200m)]);
        var report = await new IndividualHoldingsSummaryService(_database).GetAsync(data.HouseholdId, capture.Id);
        Assert.Equal(capture.Id, report.SnapshotId);
        Assert.Equal(capture.Revision, report.Revision);
        Assert.Equal(4, report.People.Count);
        var personA = report.People.Single(person => person.Person.PersonId == data.FirstPersonId);
        var personB = report.People.Single(person => person.Person.PersonId == data.SecondPersonId);
        Assert.Equal(260m, personA.Currencies.Single(currency => currency.CurrencyCode == "ZAR").HoldingsTotals.KnownNetPosition);
        Assert.Equal(800m, Assert.Single(personB.Currencies).HoldingsTotals.KnownNetPosition);
        var childHoldings = Assert.Single(report.People.Single(person => person.Person.PersonId == child.Id).Currencies);
        Assert.Equal(10m, childHoldings.HoldingsTotals.KnownNetPosition);
        Assert.False(Assert.Single(Assert.Single(Assert.Single(childHoldings.Categories).Groups).Items).IncludeInHousehold);
        Assert.Empty(report.People.Single(person => person.Person.DisplayName == "No holdings").Currencies);
        Assert.Equal(200m, Assert.Single(OwnedAssets(personA)).AttributedEquity);
        Assert.Equal(800m, Assert.Single(OwnedAssets(personA)).Asset.CurrentEquity);
        Assert.Equal(600m, Assert.Single(OwnedAssets(personB)).AttributedEquity);
        Assert.Equal(2_500, Assert.Single(OwnedAssets(personA)).OwnershipShareBasisPoints);
        var full = await new SnapshotSummaryService(_database).GetAsync(data.HouseholdId, capture.Id);
        foreach (var currency in full.Currencies)
        {
            var totals = report.People.SelectMany(person => person.Currencies).Where(row => row.CurrencyCode == currency.CurrencyCode).ToArray();
            Assert.Equal(currency.AllRecordedTotals.OrdinaryAssets.KnownSubtotal, totals.Sum(row => row.HoldingsTotals.OrdinaryAssets.KnownSubtotal));
            Assert.Equal(currency.AllRecordedTotals.OrdinaryDebts.KnownSubtotal, totals.Sum(row => row.HoldingsTotals.OrdinaryDebts.KnownSubtotal));
            Assert.Equal(currency.AllRecordedTotals.FinancedAssetEquity.KnownSubtotal, totals.Sum(row => row.HoldingsTotals.FinancedAssetEquity.KnownSubtotal));
            Assert.Equal(currency.AllRecordedTotals.KnownNetPosition, totals.Sum(row => row.HoldingsTotals.KnownNetPosition));
            Assert.All(totals, row => Assert.True(row.HoldingsTotals.IsComplete));
        }
        var allItems = report.People.SelectMany(person => person.Currencies).SelectMany(currency => currency.Categories)
            .SelectMany(category => category.Groups).SelectMany(group => group.Items).ToArray();
        Assert.Equal(5, allItems.Length);
        Assert.Equal(5, allItems.Select(item => item.ItemSnapshotId).Distinct().Count());
    }

    [Theory]
    [InlineData("100.01", 2_500)]
    [InlineData("-100.01", 2_500)]
    [InlineData("0", 5_000)]
    [InlineData("0.0000000000000000000000000001", 5_000)]
    [InlineData("-0.0000000000000000000000000001", 5_000)]
    [InlineData("79228162514264337593543950335", 5_000)]
    [InlineData("-79228162514264337593543950335", 5_000)]
    [InlineData("100.01", 1)]
    public async Task EquitySharesReconcileWithoutOverflowAndRemainStableAcrossCaptures(string text, int firstShare)
    {
        var equity = decimal.Parse(text, CultureInfo.InvariantCulture);
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database), include: false, withdrawal: false, originalLoan: 0m,
            owners: [new(data.FirstPersonId, firstShare), new(data.SecondPersonId, 10_000 - firstShare)]);
        var snapshots = new SnapshotService(_database);
        var first = await snapshots.CreateAsync(data.HouseholdId, January,
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, equity >= 0 ? equity : 0m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, equity < 0 ? -equity : 0m)]);
        var next = await snapshots.CreateNextAsync(data.HouseholdId, February);
        var service = new IndividualHoldingsSummaryService(_database);
        var report = await service.GetAsync(data.HouseholdId, first.Id);
        var later = await service.GetAsync(data.HouseholdId, next.Id);
        var amounts = report.People.SelectMany(OwnedAssets).Select(row => row.AttributedEquity!.Value).ToArray();
        Assert.Equal(equity, amounts.Sum());
        Assert.All(amounts, amount => Assert.True(equity >= 0 ? amount >= 0 : amount <= 0));
        foreach (var person in report.People)
        {
            Assert.Equal(Assert.Single(OwnedAssets(person)).AttributedEquity,
                Assert.Single(OwnedAssets(later.People.Single(row => row.Person.PersonId == person.Person.PersonId))).AttributedEquity);
        }
        if (text == "100.01")
        {
            Assert.Equal(firstShare == 2_500 ? 25.0025m : 0.010001m,
                Assert.Single(OwnedAssets(report.People.Single(person => person.Person.PersonId == data.FirstPersonId))).AttributedEquity);
        }
        if (Math.Abs(equity) == 0.0000000000000000000000000001m)
        {
            var winner = report.People.MinBy(person => person.Person.PersonId)!;
            Assert.Equal(equity, Assert.Single(OwnedAssets(winner)).AttributedEquity);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-2)]
    public async Task LargestFractionalRemainderWinsBeforePersonIdTieBreak(int unitCount)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var household = await new HouseholdService(_database).AddPersonAsync(data.HouseholdId, "Third owner", false);
        var third = household.People.Single(person => !person.ParticipatesInHouseholdFinances);
        var asset = await data.CreateAssetAsync(new(_database), include: false, withdrawal: false,
            owners: [new(data.FirstPersonId, 3_333), new(data.SecondPersonId, 3_333), new(third.Id, 3_334)]);
        const decimal unit = 0.0000000000000000000000000001m;
        var equity = unitCount * unit;
        var capture = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January,
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, Math.Max(equity, 0m)),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, Math.Max(-equity, 0m))]);
        var report = await new IndividualHoldingsSummaryService(_database).GetAsync(data.HouseholdId, capture.Id);
        Assert.Equal(Math.Sign(unitCount) * unit,
            Assert.Single(OwnedAssets(report.People.Single(person => person.Person.PersonId == third.Id))).AttributedEquity);
        var tiedOwners = report.People.Where(person => person.Person.PersonId != third.Id)
            .OrderBy(person => person.Person.PersonId).ToArray();
        Assert.Equal(Math.Abs(unitCount) == 2 ? Math.Sign(unitCount) * unit : 0m,
            Assert.Single(OwnedAssets(tiedOwners[0])).AttributedEquity);
        Assert.Equal(0m, Assert.Single(OwnedAssets(tiedOwners[1])).AttributedEquity);
        Assert.Equal(equity, report.People.SelectMany(OwnedAssets).Sum(asset => asset.AttributedEquity));
    }

    [Fact]
    public async Task OrdinaryHoldingsUseTheSavedHolderAfterLiveTransferAndSnapshotCorrection()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var item = await new TrackedItemService(_database).CreateAsync(data.HouseholdId, data.GroupId,
            data.FirstPersonId, "Saving", "ZAR", "ZA", true);
        var snapshots = new SnapshotService(_database);
        var capture = await snapshots.CreateAsync(data.HouseholdId, January, [new(item.Id, 100m)]);
        await using (var context = _database.CreateDbContext())
        {
            var current = await context.TrackedItems.SingleAsync(row => row.Id == item.Id);
            context.Entry(current).Property(row => row.HolderPersonId).CurrentValue = data.SecondPersonId;
            await context.SaveChangesAsync();
        }
        var service = new IndividualHoldingsSummaryService(_database);
        var original = await service.GetAsync(data.HouseholdId, capture.Id);
        Assert.Equal(100m, Assert.Single(original.People.Single(person => person.Person.PersonId == data.FirstPersonId).Currencies)
            .HoldingsTotals.KnownNetPosition);
        Assert.Empty(original.People.Single(person => person.Person.PersonId == data.SecondPersonId).Currencies);
        var corrected = await snapshots.CorrectValuesAsync(data.HouseholdId, capture.Id, capture.Revision, [new(item.Id, 200m)]);
        var report = await service.GetAsync(data.HouseholdId, capture.Id);
        Assert.Equal(corrected.Revision, report.Revision);
        Assert.Equal(200m, Assert.Single(report.People.Single(person => person.Person.PersonId == data.FirstPersonId).Currencies)
            .HoldingsTotals.KnownNetPosition);
        Assert.Empty(report.People.Single(person => person.Person.PersonId == data.SecondPersonId).Currencies);
        var next = await snapshots.CreateNextAsync(data.HouseholdId, February);
        var nextReport = await service.GetAsync(data.HouseholdId, next.Id);
        Assert.Empty(nextReport.People.Single(person => person.Person.PersonId == data.FirstPersonId).Currencies);
        Assert.Equal(200m, Assert.Single(nextReport.People.Single(person => person.Person.PersonId == data.SecondPersonId).Currencies)
            .HoldingsTotals.KnownNetPosition);
    }

    [Fact]
    public async Task IndividualReportsRemainAvailableWhenOnlyTheFullAggregateOverflows()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var items = new TrackedItemService(_database);
        var first = await items.CreateAsync(data.HouseholdId, data.GroupId, data.FirstPersonId, "First", "GBP", "GB", true);
        var second = await items.CreateAsync(data.HouseholdId, data.GroupId, data.SecondPersonId, "Second", "GBP", "GB", true);
        var capture = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January,
            [new(first.Id, decimal.MaxValue), new(second.Id, decimal.MaxValue)]);
        await Assert.ThrowsAsync<OverflowException>(() => new SnapshotSummaryService(_database).GetAsync(data.HouseholdId, capture.Id));
        var report = await new IndividualHoldingsSummaryService(_database).GetAsync(data.HouseholdId, capture.Id);
        Assert.Equal(2, report.People.Count);
        Assert.All(report.People, person => Assert.Equal(decimal.MaxValue, Assert.Single(person.Currencies).HoldingsTotals.KnownNetPosition));
        Assert.NotEmpty(System.Text.Json.JsonSerializer.Serialize(report));
    }

    [Fact]
    public async Task MissingAndCarriedValuesAreReportedPerHolderAndPerOwner()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database), withdrawal: false);
        var item = await new TrackedItemService(_database).CreateAsync(data.HouseholdId, data.GroupId,
            data.FirstPersonId, "Cash", "ZAR", "ZA", true);
        var snapshots = new SnapshotService(_database);
        var first = await snapshots.CreateAsync(data.HouseholdId, January, [new(item.Id, 0m)], datePrecision: DatePrecision.Month,
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 100m)]);
        var next = await snapshots.CreateNextAsync(data.HouseholdId, February);
        var summaries = new IndividualHoldingsSummaryService(_database);
        var report = await summaries.GetAsync(data.HouseholdId, next.Id);
        foreach (var person in report.People)
        {
            var currency = Assert.Single(person.Currencies);
            Assert.False(currency.HoldingsTotals.IsComplete);
            Assert.Equal(1, currency.HoldingsTotals.FinancedAssetEquity.MissingEntryCount);
            Assert.Equal(0, currency.HoldingsTotals.FinancedAssetEquity.CarriedForwardEntryCount);
            var owned = Assert.Single(OwnedAssets(person));
            Assert.Null(owned.AttributedEquity);
            Assert.Equal(SnapshotValueOrigin.CarriedForward, owned.Asset.CurrentValuation.ValueOrigin);
            Assert.Equal(Assert.Single(first.FinancedAssetSnapshots).Id, owned.Asset.CurrentValuation.CarriedFromEntryId);
        }
        Assert.Equal(1, Assert.Single(report.People.Single(person => person.Person.PersonId == data.FirstPersonId).Currencies)
            .HoldingsTotals.OrdinaryAssets.CarriedForwardEntryCount);
        await snapshots.CorrectValuesAsync(data.HouseholdId, next.Id, next.Revision,
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 20m)]);
        var corrected = await summaries.GetAsync(data.HouseholdId, next.Id);
        Assert.All(corrected.People, person =>
        {
            var totals = Assert.Single(person.Currencies).HoldingsTotals;
            Assert.True(totals.IsComplete);
            Assert.Equal(40m, totals.FinancedAssetEquity.KnownSubtotal);
            Assert.Equal(1, totals.FinancedAssetEquity.CarriedForwardEntryCount);
        });
    }

    [Fact]
    public async Task SavedOwnershipAndNamesSurviveLiveChangesAndCorrectionsUseSavedShares()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database), withdrawal: false,
            owners: [new(data.FirstPersonId, 2_500), new(data.SecondPersonId, 7_500)]);
        var snapshots = new SnapshotService(_database);
        var capture = await snapshots.CreateAsync(data.HouseholdId, January,
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 100m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 0m)]);
        await new FinancedAssetService(_database).SetPersonAssociationsAsync(data.HouseholdId, asset.Id,
            [new(data.SecondPersonId, 10_000)], [data.FirstPersonId], false, []);
        await new FinancedAssetService(_database).SetRetiredAsync(data.HouseholdId, asset.Id, true);
        await using (var context = _database.CreateDbContext())
        {
            (await context.People.SingleAsync(person => person.Id == data.FirstPersonId)).Rename("Changed");
            (await context.ItemGroups.SingleAsync()).Rename("Changed group");
            await context.SaveChangesAsync();
        }
        var service = new IndividualHoldingsSummaryService(_database);
        var old = await service.GetAsync(data.HouseholdId, capture.Id);
        var a = old.People.Single(person => person.Person.PersonId == data.FirstPersonId);
        Assert.Equal("Person A", a.Person.DisplayName);
        Assert.Equal(25m, Assert.Single(OwnedAssets(a)).AttributedEquity);
        Assert.Equal("Group A", Assert.Single(Assert.Single(Assert.Single(a.Currencies).Categories).Groups).GroupName);
        var changed = await snapshots.CorrectValuesAsync(data.HouseholdId, capture.Id, capture.Revision,
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 200m)]);
        var corrected = await service.GetAsync(data.HouseholdId, capture.Id);
        Assert.Equal(changed.Revision, corrected.Revision);
        Assert.Equal(50m, Assert.Single(OwnedAssets(corrected.People.Single(person => person.Person.PersonId == data.FirstPersonId))).AttributedEquity);
        Assert.Equal(25m, Assert.Single(OwnedAssets(a)).AttributedEquity);
    }

    [Fact]
    public async Task LoanHolderWithoutOwnershipDoesNotAcquireEquityAndDuplicatePersonNamesRemainDistinct()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database), withdrawal: false,
            owners: [new(data.FirstPersonId, 10_000)], loanHolders: [data.SecondPersonId]);
        await using (var context = _database.CreateDbContext())
        {
            foreach (var person in await context.People.ToArrayAsync()) person.Rename("Same name");
            await context.SaveChangesAsync();
        }
        var capture = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January,
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 100m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 0m)]);
        var report = await new IndividualHoldingsSummaryService(_database).GetAsync(data.HouseholdId, capture.Id);
        Assert.Equal(100m, Assert.Single(OwnedAssets(report.People.Single(person => person.Person.PersonId == data.FirstPersonId))).AttributedEquity);
        Assert.Empty(report.People.Single(person => person.Person.PersonId == data.SecondPersonId).Currencies);
    }

    [Fact]
    public async Task EmptyHistoryStaysEmptyAndRequestsRemainHouseholdScopedAndCancellable()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var capture = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January);
        await data.CreateAssetAsync(new(_database));
        var service = new IndividualHoldingsSummaryService(_database);
        var report = await service.GetAsync(data.HouseholdId, capture.Id);
        Assert.Equal(2, report.People.Count);
        Assert.All(report.People, person => Assert.Empty(person.Currencies));
        var other = await new HouseholdService(_database).CreateAsync("Other", "Other person");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(other.Id, capture.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(data.HouseholdId, Guid.NewGuid()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(data.HouseholdId, capture.Id, cancellation.Token));
    }

    private static IEnumerable<OwnedFinancedAssetSummary> OwnedAssets(PersonHoldingsSummary person) =>
        person.Currencies.SelectMany(currency => currency.Categories).SelectMany(category => category.Groups)
            .SelectMany(group => group.FinancedAssets);
}
