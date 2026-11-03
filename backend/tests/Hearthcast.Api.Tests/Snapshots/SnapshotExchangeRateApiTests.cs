using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hearthcast.Data.ExchangeRates;
using Microsoft.EntityFrameworkCore;
using Hearthcast.Api.Snapshots.Contracts;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Data.Classification;
using Hearthcast.Data.Households;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;

namespace Hearthcast.Api.Tests.Snapshots;

public sealed class SnapshotExchangeRateApiTests : IAsyncLifetime
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
    public async Task SuccessfulProviderReturnsPersistedRatesOrPendingWhenRateStorageFails(bool rejectInsert)
    {
        var home = await new HouseholdService(_database).CreateAsync("Example", "Alex");
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(home.Id, "Savings", FinancialType.Asset);
        var group = await classification.CreateGroupAsync(home.Id, category.Id, "Cash");
        var items = new TrackedItemService(_database);
        var first = await items.CreateAsync(home.Id, group.Id, home.People.Single().Id, "First", "GBP", "GB", true);
        var second = await items.CreateAsync(home.Id, group.Id, home.People.Single().Id, "Second", "ZAR", "ZA", true);
        await using var context = _database.CreateDbContext();
        if (rejectInsert)
            await context.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION "RejectRate"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Synthetic rate insert failure'; END;
                $$;
                CREATE TRIGGER "RejectRate" BEFORE INSERT ON "SnapshotExchangeRates"
                FOR EACH ROW EXECUTE FUNCTION "RejectRate"();
                """);
        var provider = new AvailableRates(_database);
        await using var factory = new ApiTestFactory(_database, provider);
        using var client = factory.CreateClient();
        var path = $"/api/households/{home.Id}/snapshots";
        var response = await client.PostAsJsonAsync(path, new
        {
            snapshotDate = "2023-01-01",
            datePrecision = 2,
            values = new[] { new { trackedItemId = first.Id, amount = 100m }, new { trackedItemId = second.Id, amount = 200m } }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<SnapshotResponse>())!;
        Assert.Equal(rejectInsert ? "Pending" : "Ready", saved.ExchangeRateStatus);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(new[] { 100m, 200m }, saved.ItemSnapshots.Select(i => i.Amount!.Value).Order());
        Assert.Equal(1, await context.Snapshots.CountAsync());
        var persisted = (await client.GetFromJsonAsync<SnapshotResponse>(path + $"/{saved.Id}"))!;
        Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(persisted));
        if (rejectInsert)
        {
            Assert.Equal(provider.FinancialRevision, saved.Revision);
            Assert.Empty(saved.ExchangeRates);
            Assert.Equal(0, await context.Set<Hearthcast.Data.Snapshots.SnapshotExchangeRate>().CountAsync());
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER \"RejectRate\" ON \"SnapshotExchangeRates\"");
            var retry = await client.PostAsJsonAsync(path + $"/{saved.Id}/exchange-rates/retry",
                new { expectedRevision = saved.Revision });
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            var recovered = (await retry.Content.ReadFromJsonAsync<SnapshotResponse>())!;
            Assert.Equal("Ready", recovered.ExchangeRateStatus);
            Assert.NotEqual(saved.Revision, recovered.Revision);
            Assert.Single(recovered.ExchangeRates);
            Assert.Equal(1, await context.Snapshots.CountAsync());
        }
        else
        {
            Assert.NotEqual(provider.FinancialRevision, saved.Revision);
            var rate = Assert.Single(saved.ExchangeRates);
            Assert.Equal("GBP", rate.BaseCurrency);
            Assert.Equal("ZAR", rate.QuoteCurrency);
            Assert.Equal(20m, rate.Rate);
            Assert.Equal("Test", rate.Source);
            Assert.Equal(saved.ExchangeRateLookupDate!.Value.AddDays(-1), rate.EffectiveDate);
        }
    }

    private sealed class AvailableRates(PostgresTestDatabase database) : IExchangeRateProvider
    {
        public string Source => "Test";
        public int Calls { get; private set; }
        public Guid FinancialRevision { get; private set; }
        public async Task<IReadOnlyList<ExchangeRateQuote>> GetAsync(string baseCurrency, IReadOnlyList<string> quotes,
            DateOnly lookupDate, CancellationToken cancellationToken)
        {
            Calls++;
            await using var context = database.CreateDbContext();
            FinancialRevision = await context.Snapshots.Select(s => s.Revision).SingleAsync(cancellationToken);
            return quotes.Select(quote => new ExchangeRateQuote(baseCurrency, quote, 20m, lookupDate.AddDays(-1))).ToArray();
        }
    }

    [Fact]
    public async Task OfflineProviderStillReturnsCreatedSnapshotAndRetryRequiresItsRevision()
    {
        var home = await new HouseholdService(_database).CreateAsync("Example", "Alex");
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(home.Id, "Savings", FinancialType.Asset);
        var group = await classification.CreateGroupAsync(home.Id, category.Id, "Cash");
        var items = new TrackedItemService(_database);
        var first = await items.CreateAsync(home.Id, group.Id, home.People.Single().Id, "First", "GBP", "GB", true);
        var second = await items.CreateAsync(home.Id, group.Id, home.People.Single().Id, "Second", "ZAR", "ZA", true);
        var path = $"/api/households/{home.Id}/snapshots";
        var response = await _client.PostAsJsonAsync(path, new
        {
            snapshotDate = "2023-01-01",
            datePrecision = 2,
            values = new[] { new { trackedItemId = first.Id, amount = 100m }, new { trackedItemId = second.Id, amount = 200m } }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<SnapshotResponse>())!;
        Assert.Equal("Pending", saved.ExchangeRateStatus);
        Assert.Equal("GBP", saved.ExchangeRateBaseCurrency);
        Assert.NotNull(saved.ExchangeRateLookupDate);
        Assert.Equal(new[] { 100m, 200m }, saved.ItemSnapshots.Select(i => i.Amount!.Value).Order());
        var retryPath = path + $"/{saved.Id}/exchange-rates/retry";
        var retry = await _client.PostAsJsonAsync(retryPath, new { expectedRevision = saved.Revision });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(saved.Revision, (await retry.Content.ReadFromJsonAsync<SnapshotResponse>())!.Revision);
        var stale = await _client.PostAsJsonAsync(retryPath, new { expectedRevision = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var persisted = (await _client.GetFromJsonAsync<SnapshotResponse>(path + $"/{saved.Id}"))!;
        Assert.Equal(saved.Revision, persisted.Revision);
        Assert.Empty(persisted.ExchangeRates);
    }
}
