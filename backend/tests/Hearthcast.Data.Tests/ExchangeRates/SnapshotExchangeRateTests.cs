using Hearthcast.Data.Classification;
using Hearthcast.Data.ExchangeRates;
using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hearthcast.Data.Tests;

public sealed class SnapshotExchangeRateTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task CapturePersistsRatesAndEditsAndRetriesNeverRefreshExistingRates()
    {
        var (home, group, holder, snapshot) = await Setup();
        var provider = new Rates();
        var service = Service(provider);
        var saved = await service.CaptureMissingAsync(home, snapshot.Id, snapshot.Revision);
        Assert.Equal("Ready", saved.ExchangeRateStatus);
        Assert.Equal("GBP", saved.ExchangeRateBaseCurrency);
        Assert.NotEqual(snapshot.Revision, saved.Revision);
        var rate = Assert.Single(saved.ExchangeRates);
        Assert.Equal(20m, rate.Rate);
        Assert.Equal(snapshot.ExchangeRateLookupDate!.Value.AddDays(-2), rate.EffectiveDate);
        Assert.Equal("Test", rate.Source);
        var edited = await new SnapshotService(_database).CorrectValuesAsync(home, saved.Id, saved.Revision,
            [new(saved.ItemSnapshots.Single(i => i.CurrencyCode == "GBP").TrackedItemId, 50m)]);
        await service.CaptureMissingAsync(home, edited.Id, edited.Revision);
        Assert.Single(provider.Calls);
        var item = await new TrackedItemService(_database).CreateAsync(home, group, holder,
            "Additional currency", "USD", "US", true);
        var amended = await new SnapshotService(_database).AmendAsync(home, saved.Id, edited.Revision,
            null, null, [new(item.Id, 40m)]);
        var enriched = await service.CaptureMissingAsync(home, saved.Id, amended.Revision);
        Assert.Equal(2, enriched.ExchangeRates.Count);
        Assert.Equal(rate.RetrievedAtUtc, enriched.ExchangeRates.Single(r => r.QuoteCurrency == "ZAR").RetrievedAtUtc);
        Assert.Equal(new[] { "USD" }, provider.Calls[1].Quotes);
        Assert.All(provider.Calls, call => Assert.Equal(snapshot.ExchangeRateLookupDate, call.Date));
        Assert.Equal(100m, enriched.ItemSnapshots.Single(i => i.CurrencyCode == "ZAR").Amount);
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("timeout")]
    [InlineData("missing")]
    [InlineData("future")]
    [InlineData("zero")]
    public async Task ProviderFailureLeavesFinancialSnapshotAndRevisionIntactAndCanBeRetried(string failure)
    {
        var (home, _, _, snapshot) = await Setup();
        var provider = new Rates { Failure = failure };
        var service = Service(provider);
        var pending = await service.CaptureMissingAsync(home, snapshot.Id, snapshot.Revision);
        Assert.Equal("Pending", pending.ExchangeRateStatus);
        Assert.Equal(snapshot.Revision, pending.Revision);
        Assert.Empty(pending.ExchangeRates);
        Assert.All(pending.ItemSnapshots, item => Assert.Equal(100m, item.Amount));
        provider.Failure = null;
        var recovered = await service.CaptureMissingAsync(home, snapshot.Id, pending.Revision);
        Assert.Equal("Ready", recovered.ExchangeRateStatus);
        Assert.Single(recovered.ExchangeRates);
    }

    [Fact]
    public async Task SingleCurrencyKeepsLookupDateWhenAnotherCurrencyIsAddedLater()
    {
        var (home, group, holder, snapshot) = await Setup(singleCurrency: true);
        var provider = new Rates();
        var service = Service(provider);
        var saved = await service.CaptureMissingAsync(home, snapshot.Id, snapshot.Revision);
        Assert.Equal("NotRequired", saved.ExchangeRateStatus);
        Assert.Empty(provider.Calls);
        Assert.NotNull(saved.ExchangeRateLookupDate);
        var item = await new TrackedItemService(_database).CreateAsync(home, group, holder, "New currency", "ZAR", "ZA", true);
        var edited = await new SnapshotService(_database).AmendAsync(home, saved.Id, saved.Revision, null, null, [new(item.Id, 100m)]);
        var result = await service.CaptureMissingAsync(home, saved.Id, edited.Revision);
        Assert.Equal(saved.ExchangeRateLookupDate, provider.Calls.Single().Date);
        Assert.Equal("Ready", result.ExchangeRateStatus);
    }

    [Fact]
    public async Task ConcurrentEditDuringFetchDiscardsFetchedRatesInsteadOfOverwritingTheEdit()
    {
        var (home, _, _, snapshot) = await Setup();
        var provider = new Rates();
        provider.DuringFetch = async () => await new SnapshotService(_database).CorrectValuesAsync(home, snapshot.Id,
            snapshot.Revision, [new(snapshot.ItemSnapshots.First().TrackedItemId, 250m)]);
        var saved = await Service(provider).CaptureMissingAsync(home, snapshot.Id, snapshot.Revision);
        Assert.Empty(saved.ExchangeRates);
        Assert.Contains(saved.ItemSnapshots, item => item.Amount == 250m);
        Assert.NotEqual(snapshot.Revision, saved.Revision);
    }

    [Fact]
    public async Task ExistingSnapshotsWithoutCaptureContextAreNotAutomaticallyBackfilled()
    {
        var (home, _, _, snapshot) = await Setup();
        await using var context = _database.CreateDbContext();
        await context.Snapshots.Where(s => s.Id == snapshot.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(s => s.ExchangeRateLookupDate, (DateOnly?)null).SetProperty(s => s.ExchangeRateBaseCurrency, (string?)null));
        var provider = new Rates();
        var saved = await Service(provider).CaptureMissingAsync(home, snapshot.Id, snapshot.Revision);
        Assert.Equal("NotCaptured", saved.ExchangeRateStatus);
        Assert.Empty(provider.Calls);
    }

    private SnapshotExchangeRateService Service(Rates provider) => new(_database,
        new SnapshotService(_database), provider, NullLogger<SnapshotExchangeRateService>.Instance);

    private async Task<(Guid Home, Guid Group, Guid Holder, Snapshot Snapshot)> Setup(bool singleCurrency = false)
    {
        var home = await new HouseholdService(_database).CreateAsync("Example", "Alex");
        var holder = home.People.Single().Id;
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(home.Id, "Savings", FinancialType.Asset);
        var group = await classification.CreateGroupAsync(home.Id, category.Id, "Cash");
        var items = new TrackedItemService(_database);
        var first = await items.CreateAsync(home.Id, group.Id, holder, "First", "GBP", "GB", true);
        var values = new List<ItemSnapshotValue> { new(first.Id, 100m) };
        if (!singleCurrency)
        {
            var second = await items.CreateAsync(home.Id, group.Id, holder, "Second", "ZAR", "ZA", true);
            values.Add(new(second.Id, 100m));
        }
        var before = DateOnly.FromDateTime(DateTime.UtcNow);
        var snapshot = await new SnapshotService(_database).CreateAsync(home.Id, new DateOnly(2023, 1, 1), values, DatePrecision.Month);
        Assert.InRange(snapshot.ExchangeRateLookupDate!.Value, before, DateOnly.FromDateTime(DateTime.UtcNow));
        return (home.Id, group.Id, holder, snapshot);
    }

    private sealed class Rates : IExchangeRateProvider
    {
        public string Source => "Test";
        public string? Failure { get; set; }
        public Func<Task>? DuringFetch { get; set; }
        public List<(IReadOnlyList<string> Quotes, DateOnly Date)> Calls { get; } = [];
        public async Task<IReadOnlyList<ExchangeRateQuote>> GetAsync(string baseCurrency, IReadOnlyList<string> quotes,
            DateOnly lookupDate, CancellationToken cancellationToken)
        {
            Calls.Add((quotes, lookupDate));
            if (DuringFetch is not null) await DuringFetch();
            if (Failure == "offline") throw new HttpRequestException("Offline");
            if (Failure == "timeout") throw new TaskCanceledException("Timeout");
            if (Failure == "missing") return [];
            return quotes.Select(quote => new ExchangeRateQuote(baseCurrency, quote,
                Failure == "zero" ? 0m : 20m, Failure == "future" ? lookupDate.AddDays(1) : lookupDate.AddDays(-2))).ToArray();
        }
    }
}
