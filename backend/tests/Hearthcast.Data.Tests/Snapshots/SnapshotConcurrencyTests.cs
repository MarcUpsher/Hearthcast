using System.Data.Common;
using Hearthcast.Data.Persistence;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Tests.TrackedItems;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hearthcast.Data.Tests.Snapshots;

public sealed class SnapshotConcurrencyTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task AReadSpanningAConcurrentCorrectionReturnsOneCoherentSnapshot()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(new TrackedItemService(_database));
        var snapshots = new SnapshotService(_database);
        var asset = await new FinancedAssetService(_database).CreateAsync(setup.HouseholdId, setup.GroupId,
            "Home", "GBP", "GB", null, null, 100m, false,
            [new(setup.HolderPersonId, 10_000)], [], true, [new(setup.HolderPersonId, 10_000)]);
        var original = await snapshots.CreateAsync(setup.HouseholdId, values: [new(item.Id, 100m)],
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 1_000m)]);
        await using var context = _database.CreateDbContext();
        var pause = new PauseBeforeSecondQuery();
        var options = new DbContextOptionsBuilder<HearthcastDbContext>()
            .UseNpgsql(context.Database.GetConnectionString()).AddInterceptors(pause).Options;
        var reader = new SnapshotService(new ContextFactory(options));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var inFlightRead = reader.GetAsync(setup.HouseholdId, original.Id, timeout.Token);
        Snapshot changed;
        try
        {
            await pause.HeaderRead.Task.WaitAsync(timeout.Token);
            changed = await snapshots.CorrectValuesAsync(setup.HouseholdId, original.Id, original.Revision, [new(item.Id, 200m)],
                [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 2_000m)], cancellationToken: timeout.Token);
        }
        finally
        {
            pause.ContinueReading.TrySetResult();
        }
        var consistent = await inFlightRead;
        Assert.Equal(original.Revision, consistent.Revision);
        Assert.Equal(100m, Assert.Single(consistent.ItemSnapshots).Amount);
        Assert.Equal(1_000m, Assert.Single(consistent.FinancedAssetSnapshots).CurrentValuation);
        var current = await snapshots.GetAsync(setup.HouseholdId, original.Id);
        Assert.Equal(changed.Revision, current.Revision);
        Assert.Equal(200m, Assert.Single(current.ItemSnapshots).Amount);
        Assert.Equal(2_000m, Assert.Single(current.FinancedAssetSnapshots).CurrentValuation);
    }

    [Fact]
    public async Task TheEfConcurrencyTokenAlsoRejectsAStaleTrackedUpdate()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var capture = await new SnapshotService(_database).CreateAsync(setup.HouseholdId);
        await using var first = _database.CreateDbContext();
        await using var second = _database.CreateDbContext();
        var firstSnapshot = await first.Snapshots.SingleAsync(snapshot => snapshot.Id == capture.Id);
        var secondSnapshot = await second.Snapshots.SingleAsync(snapshot => snapshot.Id == capture.Id);
        first.Entry(firstSnapshot).Property(snapshot => snapshot.Revision).CurrentValue = Guid.NewGuid();
        second.Entry(secondSnapshot).Property(snapshot => snapshot.Revision).CurrentValue = Guid.NewGuid();
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    private sealed class ContextFactory(DbContextOptions<HearthcastDbContext> options) : IDbContextFactory<HearthcastDbContext>
    {
        public HearthcastDbContext CreateDbContext() => new(options);
    }

    private sealed class PauseBeforeSecondQuery : DbCommandInterceptor
    {
        private int _queryCount;
        public TaskCompletionSource HeaderRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueReading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _queryCount) == 2)
            {
                HeaderRead.TrySetResult();
                await ContinueReading.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
