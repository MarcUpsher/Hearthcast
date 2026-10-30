using Hearthcast.Data.Snapshots;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Tests.TrackedItems;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hearthcast.Data.Tests.Snapshots;

public sealed class PostgresTransactionTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Theory]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.RaiseException)]
    public async Task OnlyAbortedSerializableTransactionsAreRetriedAsACompleteCapture(string sqlState)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(new TrackedItemService(_database));
        await using var context = _database.CreateDbContext();
        // Sequence increments survive rollback, so only the first insert attempt fails.
        var triggerSql = $"""
            CREATE SEQUENCE "CaptureAttempts";
            CREATE FUNCTION "FailFirstCapture"() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF nextval('"CaptureAttempts"') = 1 THEN
                    RAISE EXCEPTION USING ERRCODE = '{sqlState}', MESSAGE = 'Synthetic transaction failure';
                END IF;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER "FailFirstCapture" BEFORE INSERT ON "Snapshots"
            FOR EACH ROW EXECUTE FUNCTION "FailFirstCapture"();
            """;
        await context.Database.ExecuteSqlRawAsync(triggerSql);
        var service = new SnapshotService(_database);
        if (sqlState == PostgresErrorCodes.RaiseException)
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(setup.HouseholdId));
            Assert.Equal(0, await context.Snapshots.CountAsync());
            Assert.Equal(0, await context.SnapshotPeople.CountAsync());
        }
        else
        {
            var created = await service.CreateAsync(setup.HouseholdId, values: [new(item.Id, 123m)]);
            var saved = await service.GetAsync(setup.HouseholdId, created.Id);
            Assert.Equal(123m, Assert.Single(saved.ItemSnapshots).Amount);
            Assert.Equal(1, await context.Snapshots.CountAsync());
            Assert.Equal(2, await context.SnapshotPeople.CountAsync());
            Assert.Equal(1, await context.ItemSnapshots.CountAsync());
            Assert.Equal(1, await context.ItemSnapshotHouseholdAllocations.CountAsync());
        }
    }

    [Theory]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    public async Task ExhaustedRetriesLeaveNoPartialCapture(string sqlState)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(new TrackedItemService(_database));
        await InstallRepeatedFailureAsync(sqlState);
        // Npgsql wraps exhausted transient save errors in InvalidOperationException.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SnapshotService(_database).CreateAsync(setup.HouseholdId, values: [new(item.Id, 123m)]));
        Assert.Equal(sqlState, Assert.IsType<PostgresException>(exception.GetBaseException()).SqlState);
        await AssertNoCaptureAsync(expectedAttempts: 5);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    public async Task CancellationAfterAnAbortedAttemptStopsRetriesAndLeavesNoPartialCapture(string sqlState)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(new TrackedItemService(_database));
        await InstallRepeatedFailureAsync(sqlState);
        using var cancellation = new CancellationTokenSource();
        var cancelAfterFailure = new CancelAfterSaveFailure(cancellation);
        var snapshots = new SnapshotService(new InterceptingContextFactory(_database, cancelAfterFailure));
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => snapshots.CreateAsync(
            setup.HouseholdId, values: [new(item.Id, 123m)], cancellationToken: cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(1, cancelAfterFailure.Failures);
        await AssertNoCaptureAsync(expectedAttempts: 1);
    }

    private async Task InstallRepeatedFailureAsync(string sqlState)
    {
        await using var context = _database.CreateDbContext();
        // Fail late, after the header, people and entry have been inserted. Sequence values survive rollback.
        if (sqlState is not (PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected))
        {
            throw new ArgumentOutOfRangeException(nameof(sqlState));
        }
        // PostgreSQL function bodies cannot use command parameters; only the two fixed codes above are allowed.
        var triggerSql = $"""
            CREATE SEQUENCE "RepeatedCaptureFailures";
            CREATE FUNCTION "FailEveryAllocation"() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                PERFORM nextval('"RepeatedCaptureFailures"');
                RAISE EXCEPTION USING ERRCODE = '{sqlState}', MESSAGE = 'Synthetic repeated failure';
            END;
            $$;
            CREATE TRIGGER "FailEveryAllocation" BEFORE INSERT ON "ItemSnapshotHouseholdAllocations"
            FOR EACH ROW EXECUTE FUNCTION "FailEveryAllocation"();
            """;
        await context.Database.ExecuteSqlRawAsync(triggerSql);
    }

    private async Task AssertNoCaptureAsync(long expectedAttempts)
    {
        await using var context = _database.CreateDbContext();
        Assert.Equal(expectedAttempts, await context.Database.SqlQueryRaw<long>(
            """
            SELECT last_value AS "Value" FROM "RepeatedCaptureFailures"
            """).SingleAsync());
        Assert.Equal(0, await context.Snapshots.CountAsync());
        Assert.Equal(0, await context.SnapshotPeople.CountAsync());
        Assert.Equal(0, await context.ItemSnapshots.CountAsync());
        Assert.Equal(0, await context.ItemSnapshotHouseholdAllocations.CountAsync());
        Assert.Equal(1, await context.Households.CountAsync());
        Assert.Equal(1, await context.TrackedItems.CountAsync());
    }

    private sealed class CancelAfterSaveFailure(CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public int Failures { get; private set; }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            // Cancel at a deterministic point after PostgreSQL aborts, before retry backoff starts.
            Failures++;
            cancellation.Cancel();
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public async Task NativeNumericConstraintsRejectInvalidAmountsEvenThroughDirectSql(string invalidAmount)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(new TrackedItemService(_database));
        await new SnapshotService(_database).CreateAsync(setup.HouseholdId, values: [new(item.Id, 0m)]);
        await using var context = _database.CreateDbContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ItemSnapshots\" SET \"Amount\" = CAST({invalidAmount} AS numeric)"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(0m, (await context.ItemSnapshots.SingleAsync()).Amount);
    }

    [Fact]
    public async Task NativeNumericSupportsExactDatabaseAggregation()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var items = new TrackedItemService(_database);
        var first = await setup.CreateItemAsync(items);
        var second = await setup.CreateItemAsync(items);
        await new SnapshotService(_database).CreateAsync(setup.HouseholdId,
            values: [new(first.Id, 0.1m), new(second.Id, 0.2m)]);
        await using var context = _database.CreateDbContext();
        Assert.Equal(0.3m, await context.ItemSnapshots.SumAsync(item => item.Amount));
    }
}
