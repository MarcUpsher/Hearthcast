using Hearthcast.Data.Classification;
using Hearthcast.Data.ExchangeRates;
using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hearthcast.Data.Tests.ExchangeRates;

public sealed class HistoricalExchangeRateBackfillTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task BackfillUsesHistoricalDatesPreservesCompletedSnapshotsAndResumesOnlyMissingRates()
    {
        var home = await new HouseholdService(_database).CreateAsync("Example", "Alex");
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(home.Id, "Savings", FinancialType.Asset);
        var group = await classification.CreateGroupAsync(home.Id, category.Id, "Cash");
        var items = new TrackedItemService(_database);
        var first = await items.CreateAsync(home.Id, group.Id, home.People.Single().Id, "First", "GBP", "GB", true);
        var snapshots = new SnapshotService(_database);
        var single = await snapshots.CreateAsync(home.Id, new DateOnly(2022, 12, 1), [new(first.Id, 100m)], DatePrecision.Month);
        var second = await items.CreateAsync(home.Id, group.Id, home.People.Single().Id, "Second", "ZAR", "ZA", true);
        var values = new ItemSnapshotValue[] { new(first.Id, 100m), new(second.Id, 200m) };
        var month = await snapshots.CreateAsync(home.Id, new DateOnly(2023, 1, 25), values, DatePrecision.Month);
        var day = await snapshots.CreateAsync(home.Id, new DateOnly(2023, 1, 17), values);
        await using var context = _database.CreateDbContext();
        // Simulate snapshots written before the exchange-rate schema existed.
        await context.Snapshots.ExecuteUpdateAsync(set => set.SetProperty(s => s.ExchangeRateLookupDate, (DateOnly?)null)
            .SetProperty(s => s.ExchangeRateBaseCurrency, (string?)null));
        var current = await snapshots.CreateAsync(home.Id, new DateOnly(2024, 1, 1), values);
        var provider = new Rates();
        var capture = new SnapshotExchangeRateService(_database, snapshots, provider, NullLogger<SnapshotExchangeRateService>.Instance);
        current = await capture.CaptureMissingAsync(home.Id, current.Id, current.Revision);
        provider.Calls.Clear();
        provider.FailMonth = true;
        var backfill = new HistoricalExchangeRateBackfillService(_database, snapshots, capture,
            NullLogger<HistoricalExchangeRateBackfillService>.Instance);
        var report = await backfill.RunAsync();
        Assert.Equal(4, report.Total);
        Assert.Equal(3, report.Initialized);
        Assert.Equal(2, report.Ready);
        Assert.Equal(1, report.NotRequired);
        Assert.Equal(1, report.Remaining);
        Assert.Equal(new[] { new DateOnly(2023, 1, 1), new DateOnly(2023, 1, 17) }, provider.Calls);
        var completedDay = await snapshots.GetAsync(home.Id, day.Id);
        Assert.Equal(new DateOnly(2023, 1, 17), completedDay.ExchangeRateLookupDate);
        Assert.Equal(new DateOnly(2023, 1, 16), completedDay.ExchangeRates.Single().EffectiveDate);
        var historicalSingle = await snapshots.GetAsync(home.Id, single.Id);
        Assert.Equal(new DateOnly(2022, 12, 1), historicalSingle.ExchangeRateLookupDate);
        provider.FailMonth = false;
        provider.Calls.Clear();
        var resumed = await backfill.RunAsync();
        Assert.Equal(0, resumed.Initialized);
        Assert.Equal(0, resumed.Remaining);
        Assert.Equal(new DateOnly(2023, 1, 1), Assert.Single(provider.Calls));
        var completedMonth = await snapshots.GetAsync(home.Id, month.Id);
        Assert.Equal(new DateOnly(2023, 1, 1), completedMonth.ExchangeRateLookupDate);
        Assert.Equal(new DateOnly(2022, 12, 31), completedMonth.ExchangeRates.Single().EffectiveDate);
        Assert.Equal(current.Revision, (await snapshots.GetAsync(home.Id, current.Id)).Revision);
        Assert.Equal(completedDay.Revision, (await snapshots.GetAsync(home.Id, day.Id)).Revision);
        Assert.Equal(historicalSingle.Revision, (await snapshots.GetAsync(home.Id, single.Id)).Revision);
        Assert.Equal(new[] { 100m, 200m }, completedMonth.ItemSnapshots.Select(i => i.Amount!.Value).Order());
        Assert.Equal(month.SnapshotDate, completedMonth.SnapshotDate);
        Assert.Equal(month.DatePrecision, completedMonth.DatePrecision);
        Assert.Equal(month.ItemSnapshots.Select(i => i.Id).Order(), completedMonth.ItemSnapshots.Select(i => i.Id).Order());
        provider.Calls.Clear();
        var again = await backfill.RunAsync();
        Assert.Equal(0, again.Initialized);
        Assert.Equal(0, again.Remaining);
        Assert.Empty(provider.Calls);
        Assert.Equal(completedMonth.Revision, (await snapshots.GetAsync(home.Id, month.Id)).Revision);
    }

    private sealed class Rates : IExchangeRateProvider
    {
        public string Source => "Test";
        public bool FailMonth { get; set; }
        public List<DateOnly> Calls { get; } = [];
        public Task<IReadOnlyList<ExchangeRateQuote>> GetAsync(string baseCurrency, IReadOnlyList<string> quotes,
            DateOnly lookupDate, CancellationToken cancellationToken)
        {
            Calls.Add(lookupDate);
            if (FailMonth && lookupDate == new DateOnly(2023, 1, 1)) throw new HttpRequestException("Unavailable");
            return Task.FromResult<IReadOnlyList<ExchangeRateQuote>>(quotes.Select(quote =>
                new ExchangeRateQuote(baseCurrency, quote, 20m, lookupDate.AddDays(-1))).ToArray());
        }
    }
}
