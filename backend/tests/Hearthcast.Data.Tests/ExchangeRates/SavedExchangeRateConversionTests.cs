using Hearthcast.Data.Classification;
using Hearthcast.Data.ExchangeRates;
using Hearthcast.Data.Households;
using Hearthcast.Data.Projections;
using Hearthcast.Data.Projections.Calculation;
using Hearthcast.Data.Projections.Preparation;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hearthcast.Data.Tests.ExchangeRates;

public sealed class SavedExchangeRateConversionTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private static readonly DateOnly Date = new(2023, 1, 1);
    private sealed record Fixture(Guid Home, Guid Group, Snapshot Snapshot);
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Theory]
    [InlineData("GBP", 10)]
    [InlineData("USD", 12)]
    [InlineData("ZAR", 200)]
    public async Task ReportsAndPreparationAgreeOnSavedIdentityInverseAndCrossRates(string currency, int expected)
    {
        var data = await Setup(200);
        var report = await new MergedSnapshotSummaryService(_database).GetAsync(data.Home, data.Snapshot.Id);
        var amount = report.Household.Amounts.Single(a => a.CurrencyCode == currency);
        Assert.True(amount.IsComplete);
        Assert.Equal(expected, amount.KnownSubtotal);
        var prepared = await Prepare(data, currency);
        Assert.True(prepared.CanCalculate);
        Assert.Equal(200, Assert.Single(prepared.StartingPosition!.Currencies).Investments);
        Assert.Equal(expected, ProjectionCalculator.Calculate(prepared.Assumptions, prepared.StartingPosition).Months[0].Nominal.Investments);
        var stored = await new SnapshotService(_database).GetAsync(data.Home, data.Snapshot.Id);
        Assert.Equal(data.Snapshot.Revision, stored.Revision);
        Assert.Equal(2, stored.ExchangeRates.Count);
    }

    [Fact]
    public async Task NativeCurrencyWorksWithoutQuotesWhileForeignAmountsRemainPartialAndPreparationBlocks()
    {
        var data = await Setup(200, capture: false);
        var report = await new MergedSnapshotSummaryService(_database).GetAsync(data.Home, data.Snapshot.Id);
        var native = report.Household.Amounts.Single(a => a.CurrencyCode == "ZAR");
        Assert.True(native.IsComplete);
        Assert.Equal(200, native.KnownSubtotal);
        Assert.True((await Prepare(data, "ZAR")).CanCalculate);
        AssertMissing(report.Household.Amounts.Single(a => a.CurrencyCode == "USD"));
        var foreign = await Prepare(data, "USD");
        Assert.False(foreign.CanCalculate);
        Assert.Null(foreign.StartingPosition);
        Assert.Contains(foreign.Issues, issue => issue.Kind == ProjectionPreparationIssueKind.MissingExchangeRate);
    }

    [Theory]
    [InlineData("ZAR")]
    [InlineData("USD")]
    public async Task EitherMissingCrossRateLegPreservesEachConsumersMissingDataPolicy(string missing)
    {
        var data = await Setup(200);
        await using (var context = _database.CreateDbContext())
        {
            context.Set<SnapshotExchangeRate>().Remove(await context.Set<SnapshotExchangeRate>()
                .SingleAsync(rate => rate.SnapshotId == data.Snapshot.Id && rate.QuoteCurrency == missing));
            await context.SaveChangesAsync();
        }
        var report = await new MergedSnapshotSummaryService(_database).GetAsync(data.Home, data.Snapshot.Id);
        AssertMissing(report.Household.Amounts.Single(a => a.CurrencyCode == "USD"));
        var prepared = await Prepare(data, "USD");
        Assert.False(prepared.CanCalculate);
        Assert.Null(prepared.StartingPosition);
        Assert.Equal(ProjectionPreparationIssueKind.MissingExchangeRate, Assert.Single(prepared.Issues).Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportsHistoryAndPreparationRejectNonzeroAmountOrCrossRateUnderflow(bool crossRate)
    {
        var data = await Setup(crossRate ? 1 : .00000000001m,
            new Rates(crossRate ? 100000000000000000m : 1m, .000000000000000001m));
        // Full merged reports also resolve the inverse pair, which may overflow
        // before the forward pair underflows. Both must reject an invalid conversion.
        await RejectNumericConversion(data, crossRate ? "numeric" : "precision");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportsHistoryAndPreparationRejectConversionOverflow(bool crossRate)
    {
        var data = crossRate
            ? await Setup(1, new Rates(.000000000000000001m, 100000000000000000m))
            : await Setup(decimal.MaxValue / 2, new Rates(1, 3));
        await RejectNumericConversion(data, "numeric");
    }

    [Fact]
    public async Task ARecordedZeroRemainsKnownAndCompleteWithARepresentableTinyConversionRate()
    {
        var data = await Setup(0, new Rates(1, .000000000000000001m));
        var report = await new MergedSnapshotSummaryService(_database).GetAsync(data.Home, data.Snapshot.Id);
        var amount = report.Household.Amounts.Single(a => a.CurrencyCode == "USD");
        Assert.True(amount.IsComplete);
        Assert.Equal(0, amount.KnownSubtotal);
        var prepared = await Prepare(data, "USD");
        Assert.True(prepared.CanCalculate);
        Assert.Equal(0, ProjectionCalculator.Calculate(prepared.Assumptions, prepared.StartingPosition!).Months[0].Nominal.Investments);
    }

    [Fact]
    public async Task NegativeDebtConversionCannotSilentlyBecomeZeroInReportsOrHistory()
    {
        var data = await Setup(.00000000001m, new Rates(1, .000000000000000001m), debt: true);
        var summary = await Assert.ThrowsAsync<InvalidValueException>(() =>
            new MergedSnapshotSummaryService(_database).GetAsync(data.Home, data.Snapshot.Id));
        Assert.Contains("precision", summary.Message);
        var history = await Assert.ThrowsAsync<InvalidValueException>(() => History(data));
        Assert.Contains("precision", history.Message);
    }

    private async Task RejectNumericConversion(Fixture data, string message)
    {
        var summary = await Assert.ThrowsAsync<InvalidValueException>(() =>
            new MergedSnapshotSummaryService(_database).GetAsync(data.Home, data.Snapshot.Id));
        var history = await Assert.ThrowsAsync<InvalidValueException>(() => History(data));
        var preparation = await Assert.ThrowsAsync<InvalidValueException>(() => Prepare(data, "USD"));
        Assert.Contains(message, summary.Message);
        Assert.Contains(message, history.Message);
        Assert.Contains(message, preparation.Message);
    }

    private Task<Hearthcast.Data.Summaries.Models.History.HistoricalSummarySeries> History(Fixture data) =>
        new HistoricalSummarySeriesService(_database, new SnapshotService(_database))
            .GetAsync(data.Home, Date, Date, 1, "USD");

    private Task<ProjectionPreparation> Prepare(Fixture data, string currency) =>
        new ProjectionPreparationService(_database).PrepareAsync(data.Home, data.Snapshot.Id,
            new("", currency, 1, 0, false, [new("ZAR", 0, 0, 0, 0)]),
            [new(data.Group, ProjectionBucket.Investment)], []);

    private static void AssertMissing(Hearthcast.Data.Summaries.Models.Merged.MergedAmount amount)
    {
        Assert.Null(amount.KnownSubtotal);
        Assert.False(amount.IsComplete);
        Assert.Equal(1, amount.MissingRateCount);
        Assert.Equal(0, amount.MissingValueCount);
    }

    private async Task<Fixture> Setup(decimal amount, Rates? rates = null, bool capture = true, bool debt = false)
    {
        var home = await new HouseholdService(_database).CreateAsync("Example", "Alex");
        var person = home.People.Single().Id;
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(home.Id, "Balances", debt ? FinancialType.Debt : FinancialType.Asset);
        var group = await classification.CreateGroupAsync(home.Id, category.Id, "Accounts");
        var items = new TrackedItemService(_database);
        var zar = await items.CreateAsync(home.Id, group.Id, person, "Included account", "ZAR", "ZA", true);
        var gbp = await items.CreateAsync(home.Id, group.Id, person, "Other account", "GBP", "GB", false);
        var usd = await items.CreateAsync(home.Id, group.Id, person, "Another account", "USD", "US", false);
        var snapshots = new SnapshotService(_database);
        var snapshot = await snapshots.CreateAsync(home.Id, Date, [new(zar.Id, amount), new(gbp.Id, 0), new(usd.Id, 0)]);
        if (capture)
            snapshot = await new SnapshotExchangeRateService(_database, snapshots, rates ?? new Rates(20, 1.2m),
                NullLogger<SnapshotExchangeRateService>.Instance).CaptureMissingAsync(home.Id, snapshot.Id, snapshot.Revision);
        return new(home.Id, group.Id, snapshot);
    }

    private sealed class Rates(decimal zar, decimal usd) : IExchangeRateProvider
    {
        public string Source => "Test";
        public Task<IReadOnlyList<ExchangeRateQuote>> GetAsync(string baseCurrency, IReadOnlyList<string> quotes,
            DateOnly lookupDate, CancellationToken token) => Task.FromResult<IReadOnlyList<ExchangeRateQuote>>(
                quotes.Select(quote => new ExchangeRateQuote(baseCurrency, quote, quote == "ZAR" ? zar : usd, lookupDate)).ToArray());
    }
}
