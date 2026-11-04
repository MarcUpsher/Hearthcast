using System.Net;
using System.Net.Http.Json;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Data.Classification;
using Hearthcast.Data.ExchangeRates;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries.Models.Merged;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hearthcast.Api.Tests.Summaries;

public sealed class MergedSummaryApiTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private ApiTestFactory _factory = null!;
    private HttpClient _client = null!;
    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _factory = new ApiTestFactory(_database);
        _client = _factory.CreateClient();
    }
    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConvertsAllScopesUsingSavedSharesDebtSignsEquityAndSnapshotCurrencies(bool carry)
    {
        var data = await Setup();
        var snapshots = new SnapshotService(_database);
        var snapshot = carry ? await snapshots.CreateNextAsync(data.Home, new DateOnly(2023, 2, 1)) : data.Snapshot;
        snapshot = await new SnapshotExchangeRateService(_database, snapshots, new Rates(),
            NullLogger<SnapshotExchangeRateService>.Instance).CaptureMissingAsync(data.Home, snapshot.Id, snapshot.Revision);
        var report = await _client.GetFromJsonAsync<MergedSnapshotSummary>(Path(data.Home, snapshot.Id));
        Assert.NotNull(report);
        Assert.Equal(snapshot.Revision, report.Revision);
        Assert.Equal(new[] { "GBP", "USD", "ZAR" }, report.Currencies);
        Assert.Equal(110m, Cell(report.Household, "GBP").KnownSubtotal);
        Assert.Equal(220m, Cell(report.Household, "USD").KnownSubtotal);
        Assert.Equal(2200m, Cell(report.Household, "ZAR").KnownSubtotal);
        Assert.All(report.Household.Amounts, amount => Assert.True(amount.IsComplete));
        Assert.Equal(90m, Cell(report.HouseholdPeople.Single(p => p.PersonId == data.Alex).View, "GBP").KnownSubtotal);
        Assert.Equal(20m, Cell(report.HouseholdPeople.Single(p => p.PersonId == data.Morgan).View, "GBP").KnownSubtotal);
        Assert.Equal(82.5m, Cell(report.IndividualPeople.Single(p => p.PersonId == data.Alex).View, "GBP").KnownSubtotal);
        Assert.Equal(32.5m, Cell(report.IndividualPeople.Single(p => p.PersonId == data.Morgan).View, "GBP").KnownSubtotal);
        Assert.Equal(115m, Cell(report.AllRecorded, "GBP").KnownSubtotal);
        var entries = report.Household.Categories.SelectMany(c => c.Groups).SelectMany(g => g.Entries).ToArray();
        Assert.DoesNotContain(entries, entry => entry.Name == "Private holding");
        Assert.Equal(-30m, entries.Single(e => e.Name == "Loan").SourceAmount);
        Assert.Equal(600m, entries.Single(e => e.Name == "Home").SourceAmount);
        Assert.Equal(200m, entries.Single(e => e.Name == "Cash ZA").SourceAmount);
        Assert.Equal(carry ? 4 : 0, Cell(report.Household, "GBP").CarriedEntryCount);
        foreach (var column in report.Household.Amounts)
            Assert.Equal(column.KnownSubtotal, entries.Sum(e => e.Amounts.Single(a => a.CurrencyCode == column.CurrencyCode).KnownSubtotal));
    }

    [Fact]
    public async Task MissingRatesPreserveNativeAmountsAndFlagPartialTotalsWithoutInventingZero()
    {
        var data = await Setup();
        var report = (await _client.GetFromJsonAsync<MergedSnapshotSummary>(Path(data.Home, data.Snapshot.Id)))!;
        var gbp = Cell(report.Household, "GBP");
        Assert.Equal(70m, gbp.KnownSubtotal);
        Assert.Equal(2, gbp.MissingRateCount);
        Assert.False(gbp.IsComplete);
        Assert.Null(Cell(report.Household, "USD").KnownSubtotal);
        var entries = report.Household.Categories.SelectMany(c => c.Groups).SelectMany(g => g.Entries);
        var cash = entries.Single(e => e.Name == "Cash ZA");
        Assert.Equal(200m, cash.Amounts.Single(a => a.CurrencyCode == "ZAR").KnownSubtotal);
        Assert.Null(cash.Amounts.Single(a => a.CurrencyCode == "GBP").KnownSubtotal);
        var foreign = await _client.GetAsync(Path(Guid.NewGuid(), data.Snapshot.Id));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task UnknownValuesStayUnknownInEveryCurrencyAndMarkTotalsIncomplete()
    {
        var data = await Setup();
        var item = await new TrackedItemService(_database).CreateAsync(data.Home, data.Group, data.Alex,
            "Unknown", "ZAR", "ZA", true);
        var snapshots = new SnapshotService(_database);
        var saved = await snapshots.AmendAsync(data.Home, data.Snapshot.Id, data.Snapshot.Revision, null, null, [new(item.Id, null)]);
        saved = await new SnapshotExchangeRateService(_database, snapshots, new Rates(),
            NullLogger<SnapshotExchangeRateService>.Instance).CaptureMissingAsync(data.Home, saved.Id, saved.Revision);
        var report = (await _client.GetFromJsonAsync<MergedSnapshotSummary>(Path(data.Home, saved.Id)))!;
        Assert.Equal(110m, Cell(report.Household, "GBP").KnownSubtotal);
        Assert.All(report.Household.Amounts, amount => { Assert.Equal(1, amount.MissingValueCount); Assert.False(amount.IsComplete); });
        var unknown = report.Household.Categories.SelectMany(c => c.Groups).SelectMany(g => g.Entries).Single(e => e.Name == "Unknown");
        Assert.All(unknown.Amounts, amount => Assert.Null(amount.KnownSubtotal));
    }

    private static MergedAmount Cell(MergedView view, string currency) => view.Amounts.Single(a => a.CurrencyCode == currency);
    private static string Path(Guid home, Guid snapshot) => $"/api/households/{home}/snapshots/{snapshot}/merged-summary";
    private async Task<(Guid Home, Guid Alex, Guid Morgan, Guid Group, Snapshot Snapshot)> Setup()
    {
        var homes = new HouseholdService(_database);
        var home = await homes.CreateAsync("Example", "Alex");
        var alex = home.People.Single().Id;
        var morgan = await homes.CreatePersonAsync(home.Id, "Morgan", true);
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(home.Id, "Investments", FinancialType.Asset);
        var group = await classification.CreateGroupAsync(home.Id, category.Id, "Savings");
        var debts = await classification.CreateCategoryAsync(home.Id, "Debts", FinancialType.Debt);
        var debtGroup = await classification.CreateGroupAsync(home.Id, debts.Id, "Loans");
        var items = new TrackedItemService(_database);
        var gbp = await items.CreateAsync(home.Id, group.Id, alex, "Cash UK", "GBP", "GB", true);
        var zar = await items.CreateAsync(home.Id, group.Id, morgan.Id, "Cash ZA", "ZAR", "ZA", true, [new(alex, 5000), new(morgan.Id, 5000)]);
        var usd = await items.CreateAsync(home.Id, group.Id, alex, "Private holding", "USD", "US", false);
        var debt = await items.CreateAsync(home.Id, debtGroup.Id, alex, "Loan", "GBP", "GB", true);
        var asset = await new FinancedAssetService(_database).CreateAsync(home.Id, group.Id, "Home", "ZAR", "ZA",
            null, null, 900m, true, [new(alex, 2500), new(morgan.Id, 7500)], [morgan.Id], true, [new(alex, 5000), new(morgan.Id, 5000)]);
        var snapshot = await new SnapshotService(_database).CreateAsync(home.Id, new DateOnly(2023, 1, 1),
            [new(gbp.Id, 100m), new(zar.Id, 200m), new(usd.Id, 10m), new(debt.Id, 30m)],
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 1000m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 400m), new(asset.Id, FinancedAssetBalanceKind.AvailableToWithdraw, 100m)]);
        return (home.Id, alex, morgan.Id, group.Id, snapshot);
    }
    private sealed class Rates : IExchangeRateProvider
    {
        public string Source => "Test";
        public Task<IReadOnlyList<ExchangeRateQuote>> GetAsync(string baseCurrency, IReadOnlyList<string> quotes,
            DateOnly lookupDate, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExchangeRateQuote>>(
                quotes.Select(quote => new ExchangeRateQuote(baseCurrency, quote, quote == "ZAR" ? 20m : 2m, lookupDate)).ToArray());
    }
}
