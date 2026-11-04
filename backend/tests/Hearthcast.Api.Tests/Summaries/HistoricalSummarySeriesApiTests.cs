using Hearthcast.Data.Summaries.Models.History;
using System.Net;
using System.Net.Http.Json;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Data.Classification;
using Hearthcast.Data.ExchangeRates;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hearthcast.Api.Tests.Summaries;

public sealed class HistoricalSummarySeriesApiTests : IAsyncLifetime
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

    [Fact]
    public async Task SelectsLatestRecordedSnapshotPerCalendarBucketAndPreservesMissingConversions()
    {
        var (home, investments, jan, feb, april) = await Setup();
        var zar = (await _client.GetFromJsonAsync<HistoricalSummarySeries>(Path(home, 3, "ZAR")))!;
        Assert.Equal(new[] { "GBP", "ZAR" }, zar.AvailableCurrencies);
        Assert.Equal(new[] { feb.Id, april.Id }, zar.Points.Select(p => p.SnapshotId));
        Assert.Equal(new decimal?[] { 200m, 500m }, zar.Points.Select(p => p.NetPosition.KnownSubtotal).ToArray());
        Assert.All(zar.Points, p => Assert.True(p.NetPosition.IsComplete));
        Assert.Equal(investments, zar.Points[0].Categories.Single().CategoryId);
        Assert.Equal(200m, zar.Points[0].Categories.Single().Amount.KnownSubtotal);
        Assert.Equal(0m, zar.Points[0].FinancedEquity.KnownSubtotal);
        Assert.Equal(april.Revision, zar.Points[1].Revision);

        var gbp = (await _client.GetFromJsonAsync<HistoricalSummarySeries>(Path(home, 1, "GBP")))!;
        Assert.Equal(new[] { jan.Id, feb.Id, april.Id }, gbp.Points.Select(p => p.SnapshotId));
        Assert.Null(gbp.Points[0].NetPosition.KnownSubtotal);
        Assert.Equal(1, gbp.Points[0].NetPosition.MissingRateCount);
        Assert.False(gbp.Points[0].NetPosition.IsComplete);
        Assert.Equal(25m, gbp.Points[2].NetPosition.KnownSubtotal);
        Assert.True(gbp.Points[2].NetPosition.IsComplete);
    }

    [Fact]
    public async Task ReportsEquitySeparatelyAndRetainsPartialNetPosition()
    {
        var (home, _, _, _, _) = await Setup();
        var classifications = new ClassificationService(_database);
        var property = await classifications.CreateCategoryAsync(home, "Property", FinancialType.Asset);
        var group = await classifications.CreateGroupAsync(home, property.Id, "Home");
        var person = (await new HouseholdService(_database).GetAsync(home)).People.Single().Id;
        var asset = await new FinancedAssetService(_database).CreateAsync(home, group.Id, "Example", "ZAR", "ZA",
            null, null, 1000m, true, [new(person, 10000)], [person], true, [new(person, 10000)]);
        var snapshot = await new SnapshotService(_database).CreateAsync(home, new DateOnly(2023, 7, 1),
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 1000m),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 400m)]);
        var report = (await _client.GetFromJsonAsync<HistoricalSummarySeries>(Path(home, 1, "ZAR")))!;
        var point = report.Points.Single(p => p.SnapshotId == snapshot.Id);
        Assert.Equal(600m, point.FinancedEquity.KnownSubtotal);
        Assert.Equal(600m, point.NetPosition.KnownSubtotal);
        Assert.Equal(0m, point.Categories.Single(c => c.CategoryId == property.Id).Amount.KnownSubtotal);
        Assert.False(point.NetPosition.IsComplete); // Existing ordinary items were not recorded in July.
    }

    [Fact]
    public async Task PersonInvestmentContributionsUseSavedHouseholdAllocations()
    {
        var home = await new HouseholdService(_database).CreateAsync("Example", "Alex");
        var alex = home.People.Single().Id;
        var morgan = await new HouseholdService(_database).CreatePersonAsync(home.Id, "Morgan", true);
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(home.Id, "Investments", FinancialType.Asset);
        var group = await classification.CreateGroupAsync(home.Id, category.Id, "Funds");
        var items = new TrackedItemService(_database);
        var shared = await items.CreateAsync(home.Id, group.Id, alex, "Shared", "ZAR", "ZA", true,
            [new(alex, 5000), new(morgan.Id, 5000)]);
        var own = await items.CreateAsync(home.Id, group.Id, morgan.Id, "Own", "ZAR", "ZA", true);
        var excluded = await items.CreateAsync(home.Id, group.Id, alex, "Excluded", "ZAR", "ZA", false);
        var snapshot = await new SnapshotService(_database).CreateAsync(home.Id, new DateOnly(2023, 1, 1),
            [new(shared.Id, 200m), new(own.Id, 100m), new(excluded.Id, 50m)]);
        var series = (await _client.GetFromJsonAsync<HistoricalSummarySeries>(Path(home.Id, 1, "ZAR")))!;
        var point = series.Points.Single(p => p.SnapshotId == snapshot.Id);
        Assert.Equal(300m, point.Categories.Single().Amount.KnownSubtotal);
        Assert.Equal(100m, point.People.Single(p => p.PersonId == alex).Categories.Single().Amount.KnownSubtotal);
        Assert.Equal(200m, point.People.Single(p => p.PersonId == morgan.Id).Categories.Single().Amount.KnownSubtotal);
        Assert.Equal("Alex", point.People.Single(p => p.PersonId == alex).DisplayName);
    }

    [Fact]
    public async Task RejectsInvalidInputsAndOtherHouseholds()
    {
        var (home, _, _, _, _) = await Setup();
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(Path(home, 2, "ZAR"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync(Path(home, 1, "ZZ"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(Path(Guid.NewGuid(), 1, "ZAR"))).StatusCode);
    }

    private static string Path(Guid home, int interval, string currency) =>
        $"/api/households/{home}/summary-history?from=2023-01-01&to=2023-12-31&intervalMonths={interval}&currency={currency}";

    private async Task<(Guid Home, Guid Investments, Snapshot Jan, Snapshot Feb, Snapshot April)> Setup()
    {
        var home = await new HouseholdService(_database).CreateAsync("Example", "Alex");
        var person = home.People.Single().Id;
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(home.Id, "Investments", FinancialType.Asset);
        var group = await classification.CreateGroupAsync(home.Id, category.Id, "Funds");
        var items = new TrackedItemService(_database);
        var zar = await items.CreateAsync(home.Id, group.Id, person, "Fund ZA", "ZAR", "ZA", true);
        var snapshots = new SnapshotService(_database);
        var jan = await snapshots.CreateAsync(home.Id, new DateOnly(2023, 1, 1), [new(zar.Id, 100m)]);
        var feb = await snapshots.CreateAsync(home.Id, new DateOnly(2023, 2, 1), [new(zar.Id, 200m)]);
        var gbp = await items.CreateAsync(home.Id, group.Id, person, "Fund UK", "GBP", "GB", true);
        var april = await snapshots.CreateAsync(home.Id, new DateOnly(2023, 4, 1), [new(zar.Id, 300m), new(gbp.Id, 10m)]);
        april = await new SnapshotExchangeRateService(_database, snapshots, new Rates(),
            NullLogger<SnapshotExchangeRateService>.Instance).CaptureMissingAsync(home.Id, april.Id, april.Revision);
        return (home.Id, category.Id, jan, feb, april);
    }
    private sealed class Rates : IExchangeRateProvider
    {
        public string Source => "Test";
        public Task<IReadOnlyList<ExchangeRateQuote>> GetAsync(string baseCurrency, IReadOnlyList<string> quotes,
            DateOnly date, CancellationToken token) => Task.FromResult<IReadOnlyList<ExchangeRateQuote>>(
                quotes.Select(quote => new ExchangeRateQuote(baseCurrency, quote, 20m, date)).ToArray());
    }
}
