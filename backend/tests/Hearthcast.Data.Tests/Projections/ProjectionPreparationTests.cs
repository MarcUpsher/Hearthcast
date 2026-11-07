using Hearthcast.Data.Classification;
using Hearthcast.Data.ExchangeRates;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Projections;
using Hearthcast.Data.Projections.Calculation;
using Hearthcast.Data.Projections.Preparation;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.FinancedAssets;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hearthcast.Data.Tests.Projections;

public sealed class ProjectionPreparationTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private static readonly DateOnly January = new(2023, 1, 1);
    private ProjectionPreparationService Service => new(_database);
    private SnapshotService Snapshots => new(_database);
    private static ProjectionPresetSettings Assumptions(string currency = "GBP") => new("", currency, 1, 0, false, new[] { "GBP", "ZAR", "USD" }.Select(c => new ProjectionCurrencySettings(c, c == currency ? 10 : 0, 0, c == currency ? 5 : 0, 0)).ToArray());
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    private async Task<(FinancedAssetTestSetup Setup, TrackedItem Item, FinancedAsset Asset)> Setup()
    {
        var setup = await FinancedAssetTestSetup.CreateAsync(_database);
        var item = await new TrackedItemService(_database).CreateAsync(setup.HouseholdId, setup.GroupId,
            setup.FirstPersonId, "Investment", "ZAR", "ZA", true,
            [new(setup.FirstPersonId, 5000), new(setup.SecondPersonId, 5000)]);
        var asset = await setup.CreateAssetAsync(new(_database), owners: [new(setup.FirstPersonId, 2500), new(setup.SecondPersonId, 7500)]);
        return (setup, item, asset);
    }

    private Task<Snapshot> Capture(FinancedAssetTestSetup d, TrackedItem item, FinancedAsset asset,
        decimal? amount = 100, decimal? valuation = 1000, decimal? debt = 200) =>
        Snapshots.CreateAsync(d.HouseholdId, January, [new(item.Id, amount)], financedAssetValues:
            [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, valuation),
             new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, debt),
             new(asset.Id, FinancedAssetBalanceKind.AvailableToWithdraw, 100)]);

    [Fact]
    public async Task UsesCopiedHouseholdScopeAndDoesNotDoubleCountGroupAssetsOrAllocations()
    {
        var (d, item, asset) = await Setup();
        var items = new TrackedItemService(_database);
        var excluded = await items.CreateAsync(d.HouseholdId, d.GroupId, d.FirstPersonId, "Personal", "ZAR", "ZA", false);
        var classification = new ClassificationService(_database);
        var cat = await classification.CreateCategoryAsync(d.HouseholdId, "Debt", FinancialType.Debt);
        var group = await classification.CreateGroupAsync(d.HouseholdId, cat.Id, "Other loans");
        var debt = await items.CreateAsync(d.HouseholdId, group.Id, d.FirstPersonId, "Other debt", "ZAR", "ZA", true);
        var snapshot = await Capture(d, item, asset);
        await items.RenameAsync(d.HouseholdId, item.Id, "Renamed");
        await items.SetRetiredAsync(d.HouseholdId, item.Id, true);
        await items.SetHouseholdAllocationAsync(d.HouseholdId, item.Id, false);
        await classification.RenameGroupAsync(d.HouseholdId, item.Group.CategoryId, d.GroupId, "Changed group");
        var result = await Service.PrepareAsync(d.HouseholdId, snapshot.Id, Assumptions("ZAR"),
            [new(d.GroupId, ProjectionBucket.Investment)], [new(asset.Id, 100, 0, 0, 0, "ZAR")]);
        Assert.True(result.CanCalculate);
        Assert.Equal(100, ProjectionCalculator.Calculate(result.Assumptions, result.StartingPosition!).Months[0].Nominal.Investments);
        Assert.Equal(0, ProjectionCalculator.Calculate(result.Assumptions, result.StartingPosition!).Months[0].Nominal.Savings);
        Assert.Equal(1000, Assert.Single(result.StartingPosition!.FinancedAssets).AssetValue);
        Assert.Equal(200, result.StartingPosition!.FinancedAssets[0].LoanBalance);
        var initial = ProjectionCalculator.Calculate(result.Assumptions, result.StartingPosition).Months[0];
        Assert.Equal(900, initial.Nominal.NetPosition);
        Assert.Equal("Investment", result.Coverage.Single(c => c.SourceId == item.Id).Name);
        Assert.Equal(ProjectionCoverageDisposition.ExcludedFromHousehold, result.Coverage.Single(c => c.SourceId == excluded.Id).Disposition);
        Assert.Equal(ProjectionCoverageDisposition.NotSelected, result.Coverage.Single(c => c.SourceId == debt.Id).Disposition);
        Assert.Equal(FinancialType.Debt, result.Coverage.Single(c => c.SourceId == debt.Id).FinancialType);
        Assert.Empty(result.Issues);
        var after = await Snapshots.GetAsync(d.HouseholdId, snapshot.Id);
        Assert.Equal(snapshot.Revision, after.Revision);
        Assert.Empty(after.ExchangeRates);
    }

    [Fact]
    public async Task ConvertsOrdinaryBalancesAndKeepsNativeLoanAmountsWithSavedCrossRate()
    {
        var (d, item, asset) = await Setup();
        var items = new TrackedItemService(_database);
        var gbp = await items.CreateAsync(d.HouseholdId, d.GroupId, d.FirstPersonId, "Pounds", "GBP", "GB", true);
        var usd = await items.CreateAsync(d.HouseholdId, d.GroupId, d.SecondPersonId, "Dollars", "USD", "US", true);
        var snapshot = await Snapshots.CreateAsync(d.HouseholdId, January,
            [new(item.Id, 200), new(gbp.Id, 10), new(usd.Id, 24)], financedAssetValues:
            [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 2000), new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 200)]);
        var provider = new Rates();
        snapshot = await new SnapshotExchangeRateService(_database, Snapshots, provider, NullLogger<SnapshotExchangeRateService>.Instance)
            .CaptureMissingAsync(d.HouseholdId, snapshot.Id, snapshot.Revision);
        Assert.Equal(1, provider.Calls);
        var result = await Service.PrepareAsync(d.HouseholdId, snapshot.Id, Assumptions("USD"),
            [new(d.GroupId, ProjectionBucket.Savings)], [new(asset.Id, 300, 0, 0, 0, "ZAR")]);
        Assert.True(result.CanCalculate);
        Assert.Equal(48, ProjectionCalculator.Calculate(result.Assumptions, result.StartingPosition!).Months[0].Nominal.Savings); // £10 + R200 = £20 -> $24, plus $24.
        var loan = Assert.Single(result.StartingPosition!.FinancedAssets);
        Assert.Equal(.06m, loan.ConversionRate);
        Assert.Equal(2000, loan.AssetValue);
        Assert.Equal(200, loan.LoanBalance);
        Assert.Equal(300, loan.Assumptions.MonthlyLoanPaymentInAssetCurrency);
        var projected = ProjectionCalculator.Calculate(result.Assumptions, result.StartingPosition);
        Assert.Equal(6, projected.Months[1].RedirectedToInvestments);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(snapshot.Revision, (await Snapshots.GetAsync(d.HouseholdId, snapshot.Id)).Revision);
    }

    [Fact]
    public async Task MissingRatesBlockCrossCurrencyButNotSameCurrencyOrUnselectedValues()
    {
        var (d, item, asset) = await Setup();
        var snapshot = await Capture(d, item, asset);
        var result = await Service.PrepareAsync(d.HouseholdId, snapshot.Id, Assumptions(),
            [new(d.GroupId, ProjectionBucket.Investment)], [new(asset.Id, 100, 0, 0, 0, "ZAR")]);
        Assert.False(result.CanCalculate);
        Assert.Null(result.StartingPosition);
        Assert.Equal(ProjectionPreparationIssueKind.MissingExchangeRate, Assert.Single(result.Issues).Kind);
        result = await Service.PrepareAsync(d.HouseholdId, snapshot.Id, Assumptions(), [], []);
        Assert.True(result.CanCalculate);
        Assert.Empty(result.Issues);
        Assert.All(result.Coverage, row => Assert.Equal(ProjectionCoverageDisposition.NotSelected, row.Disposition));
    }

    [Theory]
    [InlineData("Item")]
    [InlineData("Valuation")]
    [InlineData("Debt")]
    public async Task UnknownSelectedValuesBlockInsteadOfBecomingZero(string missing)
    {
        var (d, item, asset) = await Setup();
        var snapshot = await Capture(d, item, asset, missing == "Item" ? null : 100,
            missing == "Valuation" ? null : 1000, missing == "Debt" ? null : 200);
        var result = await Service.PrepareAsync(d.HouseholdId, snapshot.Id, Assumptions("ZAR"),
            [new(d.GroupId, ProjectionBucket.Investment)], [new(asset.Id, 100, 0, 0, 0, "ZAR")]);
        Assert.False(result.CanCalculate);
        Assert.Null(result.StartingPosition);
        Assert.Equal(ProjectionPreparationIssueKind.MissingValue, Assert.Single(result.Issues).Kind);
    }

    [Fact]
    public async Task MissingSelectionsAreReportedAndConfirmedZeroDebtCanRedirect()
    {
        var (d, item, asset) = await Setup();
        var snapshot = await Capture(d, item, asset, debt: 0);
        var result = await Service.PrepareAsync(d.HouseholdId, snapshot.Id, Assumptions("ZAR"),
            [new(Guid.NewGuid(), ProjectionBucket.Investment)], [new(Guid.NewGuid(), 100, 0, 0, 0, "ZAR")]);
        Assert.False(result.CanCalculate);
        Assert.Contains(result.Issues, i => i.Kind == ProjectionPreparationIssueKind.MissingGroup);
        Assert.Contains(result.Issues, i => i.Kind == ProjectionPreparationIssueKind.MissingFinancedAsset);
        result = await Service.PrepareAsync(d.HouseholdId, snapshot.Id, Assumptions("ZAR"), [], [new(asset.Id, 100, 0, 0, 0, "ZAR")]);
        Assert.True(result.CanCalculate);
        Assert.Equal(100, ProjectionCalculator.Calculate(result.Assumptions, result.StartingPosition!).Months[1].RedirectedToInvestments);
    }

    [Fact]
    public async Task CarriedValuesAreUsableButFlaggedAndWithdrawableBalanceIsNotAdded()
    {
        var (d, item, asset) = await Setup();
        var first = await Capture(d, item, asset);
        var next = await Snapshots.CreateNextAsync(d.HouseholdId, January.AddMonths(1));
        var result = await Service.PrepareAsync(d.HouseholdId, next.Id, Assumptions("ZAR"),
            [new(d.GroupId, ProjectionBucket.Investment)], [new(asset.Id, 100, 0, 0, 0, "ZAR")]);
        Assert.True(result.CanCalculate);
        Assert.Equal(2, result.Issues.Count);
        Assert.All(result.Issues, i => Assert.Equal(ProjectionIssueSeverity.Warning, i.Severity));
        Assert.Equal(900, ProjectionCalculator.Calculate(result.Assumptions, result.StartingPosition!).Months[0].Nominal.NetPosition);
        Assert.All(result.Coverage, c => Assert.True(c.UsesCarriedValues));
    }

    [Fact]
    public async Task SavedPresetsWorkWithDifferentSnapshotsWithoutMutatingThem()
    {
        var (d, item, asset) = await Setup();
        var first = await Capture(d, item, asset);
        var next = await Snapshots.CreateNextAsync(d.HouseholdId, January.AddMonths(1), [new(item.Id, 250)]);
        var preset = await new ProjectionPresetService(_database).CreateAsync(d.HouseholdId, Assumptions("ZAR") with { Name = "Baseline" },
            [new(d.GroupId, ProjectionBucket.Investment)], [new(asset.Id, 100, 0, 0, 0, "ZAR")]);
        var old = await Service.PrepareSavedPresetAsync(d.HouseholdId, first.Id, preset.Id);
        var recent = await Service.PrepareSavedPresetAsync(d.HouseholdId, next.Id, preset.Id);
        Assert.Equal(100, old.StartingPosition!.Currencies.Single(c => c.CurrencyCode == "ZAR").Investments);
        Assert.Equal(250, recent.StartingPosition!.Currencies.Single(c => c.CurrencyCode == "ZAR").Investments);
        Assert.Equal(first.Revision, old.SnapshotRevision);
        Assert.Equal(next.Revision, recent.SnapshotRevision);
        Assert.Equal(first.SnapshotDate, old.StartingPosition.Date);
        Assert.Equal(next.SnapshotDate, recent.StartingPosition.Date);
        var other = await new HouseholdService(_database).CreateAsync("Other", "Other person");
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.PrepareSavedPresetAsync(other.Id, first.Id, preset.Id));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.PrepareAsync(other.Id, first.Id, Assumptions(), [], []));
        await using var context = _database.CreateDbContext();
        Assert.Equal(2, await context.Snapshots.CountAsync());
        Assert.Equal(preset.UpdatedAtUtc.ToUnixTimeMilliseconds(), (await context.ProjectionPresets.SingleAsync()).UpdatedAtUtc.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task DebtSelectionsAndExcludedAssetsCannotBecomeHouseholdModelInputs()
    {
        var (d, item, asset) = await Setup();
        var classification = new ClassificationService(_database);
        var cat = await classification.CreateCategoryAsync(d.HouseholdId, "Debt", FinancialType.Debt);
        var group = await classification.CreateGroupAsync(d.HouseholdId, cat.Id, "Loans");
        await new TrackedItemService(_database).CreateAsync(d.HouseholdId, group.Id, d.FirstPersonId, "Debt", "ZAR", "ZA", true);
        await new FinancedAssetService(_database).SetPersonAssociationsAsync(d.HouseholdId, asset.Id,
            [new(d.FirstPersonId, 10000)], [d.FirstPersonId], false, []);
        var snapshot = await Capture(d, item, asset);
        var result = await Service.PrepareAsync(d.HouseholdId, snapshot.Id, Assumptions("ZAR"),
            [new(group.Id, ProjectionBucket.Savings)], [new(asset.Id, 100, 0, 0, 0, "ZAR")]);
        Assert.False(result.CanCalculate);
        Assert.Contains(result.Issues, i => i.Kind == ProjectionPreparationIssueKind.DebtGroupSelected);
        Assert.Contains(result.Issues, i => i.Kind == ProjectionPreparationIssueKind.ExcludedFinancedAsset);
    }

    [Fact]
    public async Task EmptyHouseholdGroupIsFlaggedRatherThanAssumedToHaveZeroAssets()
    {
        var (d, item, asset) = await Setup();
        await new TrackedItemService(_database).SetHouseholdAllocationAsync(d.HouseholdId, item.Id, false);
        var snapshot = await Capture(d, item, asset);
        var result = await Service.PrepareAsync(d.HouseholdId, snapshot.Id, Assumptions("ZAR"),
            [new(d.GroupId, ProjectionBucket.Investment)], []);
        Assert.False(result.CanCalculate);
        Assert.Equal(ProjectionPreparationIssueKind.NoHouseholdItems, Assert.Single(result.Issues).Kind);
    }

    [Fact]
    public async Task InvalidUnsavedSelectionsUseTheSameValidationAsSavedPresets()
    {
        var (d, item, asset) = await Setup();
        var snapshot = await Capture(d, item, asset);
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.PrepareAsync(d.HouseholdId, snapshot.Id, Assumptions(),
            [new(d.GroupId, ProjectionBucket.Investment), new(d.GroupId, ProjectionBucket.Savings)], []));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.PrepareAsync(d.HouseholdId, snapshot.Id, Assumptions(), [null!], []));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.PrepareAsync(d.HouseholdId, snapshot.Id, Assumptions(), [], [null!]));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.PrepareAsync(d.HouseholdId, snapshot.Id,
            Assumptions() with { DurationYears = 0 }, [], []));
    }

    [Theory]
    [InlineData("Item")]
    [InlineData("Valuation")]
    [InlineData("Debt")]
    public async Task OrdinaryAndFinancedBalancesRejectEquivalentConversionUnderflow(string component)
    {
        var (d, item, asset) = await Setup();
        await AddExcludedCurrencies(d);
        const decimal tiny = .00000000001m;
        var snapshot = await Capture(d, item, asset, component == "Item" ? tiny : 100,
            component == "Valuation" ? tiny : 1000, component == "Debt" ? tiny : 0);
        snapshot = await new SnapshotExchangeRateService(_database, Snapshots,
            new Rates { ZarRate = 1m, UsdRate = .000000000000000001m }, NullLogger<SnapshotExchangeRateService>.Instance)
            .CaptureMissingAsync(d.HouseholdId, snapshot.Id, snapshot.Revision);
        var failure = await Assert.ThrowsAsync<InvalidValueException>(() => Service.PrepareAsync(d.HouseholdId,
            snapshot.Id, Assumptions("USD"), component == "Item" ? [new(d.GroupId, ProjectionBucket.Investment)] : [],
            component == "Item" ? [] : [new(asset.Id, 0, 0, 0, 0, "ZAR")]));
        Assert.Contains("numeric precision", failure.Message);
    }

    [Theory]
    [InlineData("USD", "ZAR")]
    [InlineData("ZAR", "USD")]
    public async Task EitherMissingCrossRateLegBlocksWithoutPartialInputsOrDuplicateDiagnostics(string missing, string retained)
    {
        var (d, item, asset) = await Setup();
        await AddExcludedCurrencies(d);
        var snapshot = await Capture(d, item, asset);
        snapshot = await new SnapshotExchangeRateService(_database, Snapshots, new Rates(), NullLogger<SnapshotExchangeRateService>.Instance)
            .CaptureMissingAsync(d.HouseholdId, snapshot.Id, snapshot.Revision);
        await using (var context = _database.CreateDbContext())
        {
            // Create an incomplete saved-rate fixture. No provider is used during preparation.
            context.Set<SnapshotExchangeRate>().Remove(await context.Set<SnapshotExchangeRate>().SingleAsync(r => r.SnapshotId == snapshot.Id && r.QuoteCurrency == missing));
            await context.SaveChangesAsync();
        }
        var stored = await Snapshots.GetAsync(d.HouseholdId, snapshot.Id);
        Assert.Equal(retained, Assert.Single(stored.ExchangeRates).QuoteCurrency);
        var result = await Service.PrepareAsync(d.HouseholdId, snapshot.Id, Assumptions("USD"),
            [new(d.GroupId, ProjectionBucket.Investment)], [new(asset.Id, 100, 0, 0, 0, "ZAR")]);
        Assert.False(result.CanCalculate);
        Assert.Null(result.StartingPosition);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(ProjectionPreparationIssueKind.MissingExchangeRate, issue.Kind);
        Assert.Equal(ProjectionIssueSeverity.Error, issue.Severity);
        Assert.Contains("ZAR to USD", issue.Message);
    }

    private async Task AddExcludedCurrencies(FinancedAssetTestSetup d)
    {
        var items = new TrackedItemService(_database);
        await items.CreateAsync(d.HouseholdId, d.GroupId, d.FirstPersonId, "Pounds", "GBP", "GB", false);
        await items.CreateAsync(d.HouseholdId, d.GroupId, d.FirstPersonId, "Dollars", "USD", "US", false);
    }

    [Fact]
    public async Task SelectedNativeCurrencyNeedsItsOwnConfiguredPotRatherThanUsingViewingCurrencyAssumptions()
    {
        var (d, item, asset) = await Setup();
        var snapshot = await Capture(d, item, asset);
        var settings = new ProjectionPresetSettings("", "ZAR", 1, 0, false, [new("GBP", 0, 0, 0, 0)]);
        var result = await Service.PrepareAsync(d.HouseholdId, snapshot.Id, settings, [new(d.GroupId, ProjectionBucket.Investment)], []);
        Assert.False(result.CanCalculate);
        Assert.Null(result.StartingPosition);
        Assert.Contains(result.Issues, i => i.Kind == ProjectionPreparationIssueKind.MissingCurrencySettings);
    }

    private sealed class Rates : IExchangeRateProvider
    {
        public int Calls { get; private set; }
        public decimal ZarRate { get; init; } = 20m;
        public decimal UsdRate { get; init; } = 1.2m;
        public string Source => "Test";
        public Task<IReadOnlyList<ExchangeRateQuote>> GetAsync(string baseCurrency, IReadOnlyList<string> quotes, DateOnly date, CancellationToken token)
        {
            Calls++;
            Assert.Equal("GBP", baseCurrency);
            return Task.FromResult<IReadOnlyList<ExchangeRateQuote>>(quotes.Select(q => new ExchangeRateQuote(baseCurrency, q,
                q == "ZAR" ? ZarRate : UsdRate, date)).ToArray());
        }
    }
}
