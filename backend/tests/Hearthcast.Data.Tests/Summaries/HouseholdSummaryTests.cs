using System.Globalization;
using Hearthcast.Data.Classification;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries;
using Hearthcast.Data.Summaries.Models.Household;
using Hearthcast.Data.Tests.FinancedAssets;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Tests.Summaries;

public sealed class HouseholdSummaryTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private static readonly DateOnly January = new(2023, 1, 1);
    private static readonly DateOnly February = new(2023, 2, 1);
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task HouseholdCountsIncludedValuesOnceAndUsesAllocationsInsteadOfHoldings()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var household = await new HouseholdService(_database).AddPersonAsync(data.HouseholdId, "Child", false);
        var child = household.People.Single(person => !person.ParticipatesInHouseholdFinances);
        var asset = await data.CreateAssetAsync(new(_database), withdrawal: false,
            owners: [new(data.FirstPersonId, 2_500), new(data.SecondPersonId, 7_500)]);
        var items = new TrackedItemService(_database);
        var pension = await items.CreateAsync(data.HouseholdId, data.GroupId, data.FirstPersonId, "Pension", "ZAR", "ZA", true);
        var cash = await items.CreateAsync(data.HouseholdId, data.GroupId, data.SecondPersonId, "Savings", "ZAR", "ZA", true,
            [new(data.FirstPersonId, 6_000), new(data.SecondPersonId, 4_000)]);
        var excluded = await items.CreateAsync(data.HouseholdId, data.GroupId, child.Id, "Child savings", "ZAR", "ZA", false);
        var isa = await items.CreateAsync(data.HouseholdId, data.GroupId, data.FirstPersonId, "ISA", "GBP", "GB", true,
            [new(data.SecondPersonId, 10_000)]);
        var classification = new ClassificationService(_database);
        var debtCategory = await classification.CreateCategoryAsync(data.HouseholdId, "Debt", FinancialType.Debt);
        var debtGroup = await classification.CreateGroupAsync(data.HouseholdId, debtCategory.Id, "Loans");
        var debt = await items.CreateAsync(data.HouseholdId, debtGroup.Id, data.FirstPersonId, "Loan", "ZAR", "ZA", true,
            [new(data.FirstPersonId, 2_500), new(data.SecondPersonId, 7_500)]);
        var capture = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January,
            [new(pension.Id, 100m), new(cash.Id, 200m), new(excluded.Id, 10m), new(isa.Id, 5m), new(debt.Id, 40m)],
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 1_000m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 200m)]);
        var report = await new HouseholdSummaryService(_database).GetAsync(data.HouseholdId, capture.Id);
        Assert.Equal(capture.Id, report.SnapshotId);
        Assert.Equal(capture.Revision, report.Revision);
        Assert.Equal(new[] { "GBP", "ZAR" }, report.Currencies.Select(currency => currency.CurrencyCode));
        var zar = report.Currencies.Single(currency => currency.CurrencyCode == "ZAR");
        Assert.Equal(300m, zar.Totals.OrdinaryAssets.KnownSubtotal);
        Assert.Equal(40m, zar.Totals.OrdinaryDebts.KnownSubtotal);
        Assert.Equal(800m, zar.Totals.FinancedAssetEquity.KnownSubtotal);
        Assert.Equal(1_060m, zar.Totals.KnownNetPosition);
        Assert.True(zar.Totals.IsComplete);
        var a = report.People.Single(person => person.Person.PersonId == data.FirstPersonId);
        var b = report.People.Single(person => person.Person.PersonId == data.SecondPersonId);
        Assert.Equal(610m, Assert.Single(a.Currencies).Totals.KnownNetPosition);
        Assert.Equal(450m, b.Currencies.Single(currency => currency.CurrencyCode == "ZAR").Totals.KnownNetPosition);
        Assert.Equal(5m, b.Currencies.Single(currency => currency.CurrencyCode == "GBP").Totals.KnownNetPosition);
        Assert.Empty(report.People.Single(person => person.Person.PersonId == child.Id).Currencies);
        Assert.Equal(400m, Assert.Single(Groups(a.Currencies).SelectMany(group => group.FinancedAssets)).AttributedEquity);
        var attributedCash = Assert.Single(Groups(a.Currencies).SelectMany(group => group.Items), row => row.Item.TrackedItemId == cash.Id);
        Assert.Equal(120m, attributedCash.ContributingAmount);
        Assert.Equal(6_000, attributedCash.AllocationShareBasisPoints);
        Assert.Equal(200m, attributedCash.Item.Balance.Amount);
        Assert.Equal(data.SecondPersonId, report.People.Single(person => person.Person.SnapshotPersonId == attributedCash.Item.HolderSnapshotPersonId).Person.PersonId);
        var fullCash = Groups(report.Currencies).SelectMany(group => group.Items).Single(row => row.Item.TrackedItemId == cash.Id);
        Assert.Equal(10_000, fullCash.AllocationShareBasisPoints);
        Assert.Equal(200m, fullCash.ContributingAmount);
        Assert.DoesNotContain(Groups(report.Currencies).SelectMany(group => group.Items), row => row.Item.TrackedItemId == excluded.Id);
        foreach (var currency in report.Currencies)
        {
            var people = report.People.SelectMany(person => person.Currencies).Where(row => row.CurrencyCode == currency.CurrencyCode).ToArray();
            Assert.Equal(currency.Totals.OrdinaryAssets.KnownSubtotal, people.Sum(row => row.Totals.OrdinaryAssets.KnownSubtotal));
            Assert.Equal(currency.Totals.OrdinaryDebts.KnownSubtotal, people.Sum(row => row.Totals.OrdinaryDebts.KnownSubtotal));
            Assert.Equal(currency.Totals.FinancedAssetEquity.KnownSubtotal, people.Sum(row => row.Totals.FinancedAssetEquity.KnownSubtotal));
            Assert.Equal(currency.Totals.KnownNetPosition, people.Sum(row => row.Totals.KnownNetPosition));
        }
        var full = await new SnapshotSummaryService(_database).GetAsync(data.HouseholdId, capture.Id);
        Assert.Equal(1_070m, full.Currencies.Single(currency => currency.CurrencyCode == "ZAR").AllRecordedTotals.KnownNetPosition);
        Assert.NotEmpty(System.Text.Json.JsonSerializer.Serialize(report));
    }

    [Theory]
    [InlineData("100.01")]
    [InlineData("-100.01")]
    [InlineData("0")]
    [InlineData("0.0000000000000000000000000001")]
    [InlineData("-0.0000000000000000000000000001")]
    [InlineData("79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335")]
    public async Task AllocatedEquityReconcilesAndPreservesDecimalBoundaries(string text)
    {
        var amount = decimal.Parse(text, CultureInfo.InvariantCulture);
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database), withdrawal: false, originalLoan: 0m,
            owners: [new(data.FirstPersonId, 10_000)],
            allocations: [new(data.FirstPersonId, 2_500), new(data.SecondPersonId, 7_500)]);
        var capture = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January,
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, Math.Max(amount, 0m)),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, Math.Max(-amount, 0m))]);
        var report = await new HouseholdSummaryService(_database).GetAsync(data.HouseholdId, capture.Id);
        Assert.Equal(amount, Assert.Single(report.Currencies).Totals.KnownNetPosition);
        Assert.Equal(amount, report.People.Sum(person => Assert.Single(person.Currencies).Totals.KnownNetPosition));
        if (Math.Abs(amount) == 100.01m)
        {
            Assert.Equal(Math.Sign(amount) * 25.0025m,
                Assert.Single(report.People.Single(person => person.Person.PersonId == data.FirstPersonId).Currencies).Totals.KnownNetPosition);
        }
    }

    [Theory]
    [InlineData(FinancialType.Asset)]
    [InlineData(FinancialType.Debt)]
    public async Task SharedOrdinaryAmountsUseTheSameExactAllocationAndDebtSignRules(FinancialType type)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database, financialType: type);
        var item = await new TrackedItemService(_database).CreateAsync(data.HouseholdId, data.GroupId,
            data.FirstPersonId, "Shared amount", "ZAR", "ZA", true,
            [new(data.FirstPersonId, 2_500), new(data.SecondPersonId, 7_500)]);
        var capture = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January, [new(item.Id, 100.01m)]);
        var report = await new HouseholdSummaryService(_database).GetAsync(data.HouseholdId, capture.Id);
        var sign = type == FinancialType.Asset ? 1m : -1m;
        Assert.Equal(sign * 100.01m, Assert.Single(report.Currencies).Totals.KnownNetPosition);
        Assert.Equal(sign * 25.0025m, Assert.Single(report.People.Single(person => person.Person.PersonId == data.FirstPersonId).Currencies).Totals.KnownNetPosition);
        Assert.Equal(sign * 75.0075m, Assert.Single(report.People.Single(person => person.Person.PersonId == data.SecondPersonId).Currencies).Totals.KnownNetPosition);
    }

    [Fact]
    public async Task ExcludedMissingValuesAndOverflowingAssetDetailsCannotAffectHouseholdReport()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database), include: false, originalLoan: 0m);
        var items = new TrackedItemService(_database);
        var cash = await items.CreateAsync(data.HouseholdId, data.GroupId, data.FirstPersonId, "Cash", "ZAR", "ZA", true);
        await items.CreateAsync(data.HouseholdId, data.GroupId, data.SecondPersonId, "Unknown", "EUR", "IE", false);
        var capture = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January, [new(cash.Id, 100m)],
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 0m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, decimal.MaxValue),
                new(asset.Id, FinancedAssetBalanceKind.AvailableToWithdraw, 1m)]);
        await Assert.ThrowsAsync<OverflowException>(() => new SnapshotSummaryService(_database).GetAsync(data.HouseholdId, capture.Id));
        var report = await new HouseholdSummaryService(_database).GetAsync(data.HouseholdId, capture.Id);
        var currency = Assert.Single(report.Currencies);
        Assert.Equal("ZAR", currency.CurrencyCode);
        Assert.Equal(100m, currency.Totals.KnownNetPosition);
        Assert.True(currency.Totals.IsComplete);
        Assert.Empty(Groups(report.Currencies).SelectMany(group => group.FinancedAssets));
    }

    [Fact]
    public async Task MissingAndCarriedCountsDescribeEachScopeAndCorrectionsUpdateAllocations()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var cash = await new TrackedItemService(_database).CreateAsync(data.HouseholdId, data.GroupId,
            data.FirstPersonId, "Cash", "ZAR", "ZA", true,
            [new(data.FirstPersonId, 5_000), new(data.SecondPersonId, 5_000)]);
        var snapshots = new SnapshotService(_database);
        var first = await snapshots.CreateAsync(data.HouseholdId, January, datePrecision: DatePrecision.Month,
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 100m)]);
        var next = await snapshots.CreateNextAsync(data.HouseholdId, February);
        var service = new HouseholdSummaryService(_database);
        var missing = await service.GetAsync(data.HouseholdId, next.Id);
        var totals = Assert.Single(missing.Currencies).Totals;
        Assert.Equal(1, totals.OrdinaryAssets.MissingEntryCount);
        Assert.Equal(1, totals.FinancedAssetEquity.MissingEntryCount);
        Assert.False(totals.IsComplete);
        Assert.All(missing.People, person =>
        {
            var personal = Assert.Single(person.Currencies).Totals;
            Assert.Equal(1, personal.OrdinaryAssets.MissingEntryCount);
            Assert.Equal(1, personal.FinancedAssetEquity.MissingEntryCount);
        });
        var changed = await snapshots.CorrectValuesAsync(data.HouseholdId, next.Id, next.Revision, [new(cash.Id, 0m)],
            [new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 20m)]);
        var known = await service.GetAsync(data.HouseholdId, next.Id);
        Assert.Equal(changed.Revision, known.Revision);
        Assert.True(Assert.Single(known.Currencies).Totals.IsComplete);
        Assert.Equal(1, Assert.Single(known.Currencies).Totals.FinancedAssetEquity.CarriedForwardEntryCount);
        Assert.All(known.People, person =>
        {
            var currency = Assert.Single(person.Currencies);
            Assert.Equal(40m, currency.Totals.KnownNetPosition);
            Assert.Equal(1, currency.Totals.FinancedAssetEquity.CarriedForwardEntryCount);
            var entry = Assert.Single(Groups(person.Currencies).SelectMany(group => group.FinancedAssets));
            Assert.Equal(Assert.Single(first.FinancedAssetSnapshots).Id, entry.Asset.CurrentValuation.CarriedFromEntryId);
            Assert.Equal(DatePrecision.Month, entry.Asset.CurrentValuation.LastConfirmedPrecision);
            Assert.Null(entry.Asset.AvailableToWithdraw.Amount);
        });
        var later = await snapshots.CreateNextAsync(data.HouseholdId, new DateOnly(2023, 3, 1));
        var carried = await service.GetAsync(data.HouseholdId, later.Id);
        Assert.Equal(1, Assert.Single(carried.Currencies).Totals.OrdinaryAssets.CarriedForwardEntryCount);
    }

    [Fact]
    public async Task SavedInclusionAndAllocationsSurviveLiveChangesAndCorrectionsWithoutRetirement()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database), withdrawal: false,
            allocations: [new(data.FirstPersonId, 10_000)]);
        var items = new TrackedItemService(_database);
        var cash = await items.CreateAsync(data.HouseholdId, data.GroupId, data.SecondPersonId, "Cash", "ZAR", "ZA", true,
            [new(data.FirstPersonId, 6_000), new(data.SecondPersonId, 4_000)]);
        var snapshots = new SnapshotService(_database);
        var capture = await snapshots.CreateAsync(data.HouseholdId, January, [new(cash.Id, 100m)],
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 100m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 0m)]);
        await items.SetHouseholdAllocationAsync(data.HouseholdId, cash.Id, false);
        await new FinancedAssetService(_database).SetPersonAssociationsAsync(data.HouseholdId, asset.Id,
            [new(data.SecondPersonId, 10_000)], [data.SecondPersonId], true,
            [new(data.FirstPersonId, 5_000), new(data.SecondPersonId, 5_000)]);
        await using (var context = _database.CreateDbContext())
        {
            (await context.People.SingleAsync(person => person.Id == data.FirstPersonId)).Rename("Changed");
            await context.SaveChangesAsync();
        }
        var service = new HouseholdSummaryService(_database);
        var original = await service.GetAsync(data.HouseholdId, capture.Id);
        var originalA = original.People.Single(person => person.Person.PersonId == data.FirstPersonId);
        Assert.Equal("Person A", originalA.Person.DisplayName);
        Assert.Equal(160m, Assert.Single(originalA.Currencies).Totals.KnownNetPosition);
        var corrected = await snapshots.CorrectValuesAsync(data.HouseholdId, capture.Id, capture.Revision, [new(cash.Id, 200m)]);
        var report = await service.GetAsync(data.HouseholdId, capture.Id);
        Assert.Equal(corrected.Revision, report.Revision);
        Assert.Equal(220m, Assert.Single(report.People.Single(person => person.Person.PersonId == data.FirstPersonId).Currencies).Totals.KnownNetPosition);
        Assert.Equal(80m, Assert.Single(report.People.Single(person => person.Person.PersonId == data.SecondPersonId).Currencies).Totals.KnownNetPosition);
        Assert.Equal(200m, Assert.Single(original.Currencies).Totals.KnownNetPosition);
        var next = await snapshots.CreateNextAsync(data.HouseholdId, February);
        var nextCash = Assert.Single(next.ItemSnapshots);
        Assert.Equal(cash.Id, nextCash.TrackedItemId);
        Assert.False(nextCash.IncludeInHousehold);
        Assert.Equal(200m, nextCash.Amount);
        var nextReport = await service.GetAsync(data.HouseholdId, next.Id);
        Assert.Equal(100m, Assert.Single(nextReport.Currencies).Totals.KnownNetPosition);
        Assert.All(nextReport.People, person => Assert.Equal(50m, Assert.Single(person.Currencies).Totals.KnownNetPosition));
    }

    [Fact]
    public async Task EnablingHouseholdInclusionDoesNotRewritePreviouslyExcludedSnapshotsEvenAfterCorrection()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var assetService = new FinancedAssetService(_database);
        var asset = await data.CreateAssetAsync(assetService, include: false, withdrawal: false);
        var items = new TrackedItemService(_database);
        var cash = await items.CreateAsync(data.HouseholdId, data.GroupId, data.SecondPersonId,
            "Cash", "ZAR", "ZA", false);
        var snapshots = new SnapshotService(_database);
        var original = await snapshots.CreateAsync(data.HouseholdId, January, [new(cash.Id, 100m)],
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 100m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 0m)]);
        await items.SetHouseholdAllocationAsync(data.HouseholdId, cash.Id, true,
            [new(data.FirstPersonId, 6_000), new(data.SecondPersonId, 4_000)]);
        await assetService.SetPersonAssociationsAsync(data.HouseholdId, asset.Id,
            [new(data.FirstPersonId, 5_000), new(data.SecondPersonId, 5_000)], [data.SecondPersonId], true,
            [new(data.FirstPersonId, 2_500), new(data.SecondPersonId, 7_500)]);
        var service = new HouseholdSummaryService(_database);
        var oldReport = await service.GetAsync(data.HouseholdId, original.Id);
        Assert.Empty(oldReport.Currencies);
        Assert.All(oldReport.People, person => Assert.Empty(person.Currencies));
        var next = await snapshots.CreateNextAsync(data.HouseholdId, February);
        var included = await service.GetAsync(data.HouseholdId, next.Id);
        Assert.Equal(200m, Assert.Single(included.Currencies).Totals.KnownNetPosition);
        Assert.Equal(85m, Assert.Single(included.People.Single(person => person.Person.PersonId == data.FirstPersonId).Currencies).Totals.KnownNetPosition);
        Assert.Equal(115m, Assert.Single(included.People.Single(person => person.Person.PersonId == data.SecondPersonId).Currencies).Totals.KnownNetPosition);
        var corrected = await snapshots.CorrectValuesAsync(data.HouseholdId, original.Id, original.Revision,
            [new(cash.Id, 200m)], [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 200m)]);
        var correctedReport = await service.GetAsync(data.HouseholdId, original.Id);
        Assert.Equal(corrected.Revision, correctedReport.Revision);
        Assert.Empty(correctedReport.Currencies);
        Assert.All(correctedReport.People, person => Assert.Empty(person.Currencies));
        var full = await new SnapshotSummaryService(_database).GetAsync(data.HouseholdId, original.Id);
        Assert.Equal(400m, Assert.Single(full.Currencies).AllRecordedTotals.KnownNetPosition);
        Assert.Equal(200m, Assert.Single((await service.GetAsync(data.HouseholdId, next.Id)).Currencies).Totals.KnownNetPosition);
    }

    [Fact]
    public async Task RetirementLeavesSavedHouseholdReportsIntactAndOmitsEntriesFromNextCapture()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var assetService = new FinancedAssetService(_database);
        var asset = await data.CreateAssetAsync(assetService, withdrawal: false);
        var items = new TrackedItemService(_database);
        var cash = await items.CreateAsync(data.HouseholdId, data.GroupId, data.FirstPersonId,
            "Cash", "ZAR", "ZA", true);
        var snapshots = new SnapshotService(_database);
        var original = await snapshots.CreateAsync(data.HouseholdId, January, [new(cash.Id, 100m)],
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 100m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 0m)]);
        await items.SetRetiredAsync(data.HouseholdId, cash.Id, true);
        await assetService.SetRetiredAsync(data.HouseholdId, asset.Id, true);
        var service = new HouseholdSummaryService(_database);
        Assert.Equal(200m, Assert.Single((await service.GetAsync(data.HouseholdId, original.Id)).Currencies).Totals.KnownNetPosition);
        var next = await snapshots.CreateNextAsync(data.HouseholdId, February);
        Assert.Empty(next.ItemSnapshots);
        Assert.Empty(next.FinancedAssetSnapshots);
        var report = await service.GetAsync(data.HouseholdId, next.Id);
        Assert.Empty(report.Currencies);
        Assert.Equal(2, report.People.Count);
        Assert.All(report.People, person => Assert.Empty(person.Currencies));
    }

    [Fact]
    public async Task IncludedAggregateOverflowFailsTheWholeResponseInsteadOfReturningOnlyPersonalTotals()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var items = new TrackedItemService(_database);
        var a = await items.CreateAsync(data.HouseholdId, data.GroupId, data.FirstPersonId, "First", "ZAR", "ZA", true);
        var b = await items.CreateAsync(data.HouseholdId, data.GroupId, data.SecondPersonId, "Second", "ZAR", "ZA", true);
        var capture = await new SnapshotService(_database).CreateAsync(data.HouseholdId, January,
            [new(a.Id, decimal.MaxValue), new(b.Id, decimal.MaxValue)]);
        await Assert.ThrowsAsync<OverflowException>(() => new HouseholdSummaryService(_database).GetAsync(data.HouseholdId, capture.Id));
    }

    [Fact]
    public async Task EmptyHouseholdHistoryKeepsRosterWithoutInventingCurrenciesAndEnforcesRequestBoundaries()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        await data.CreateAssetAsync(new(_database), include: false);
        var snapshots = new SnapshotService(_database);
        var capture = await snapshots.CreateAsync(data.HouseholdId, January);
        var service = new HouseholdSummaryService(_database);
        var report = await service.GetAsync(data.HouseholdId, capture.Id);
        Assert.Empty(report.Currencies);
        Assert.Equal(2, report.People.Count);
        Assert.All(report.People, person => Assert.Empty(person.Currencies));
        var other = await new HouseholdService(_database).CreateAsync("Other", "Other");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(other.Id, capture.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(data.HouseholdId, Guid.NewGuid()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(data.HouseholdId, capture.Id, cancellation.Token));
    }

    private static IEnumerable<HouseholdGroupSummary> Groups(IEnumerable<HouseholdCurrencySummary> currencies) =>
        currencies.SelectMany(currency => currency.Categories).SelectMany(category => category.Groups);
}
