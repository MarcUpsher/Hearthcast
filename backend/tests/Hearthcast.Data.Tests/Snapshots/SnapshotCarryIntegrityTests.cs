using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Tests.TrackedItems;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Hearthcast.Data.Tests.Snapshots;

public sealed class SnapshotCarryIntegrityTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Theory]
    [InlineData("different-item")]
    [InlineData("different-currency")]
    [InlineData("foreign-household")]
    public async Task PostgreSqlEnforcesMatchingSourceIdentityAndCurrency(string scenario)
    {
        var items = new TrackedItemService(_database);
        var snapshots = new SnapshotService(_database);
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(items);
        var original = await snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(item.Id, 10m)]);
        Guid invalidSourceId;
        if (scenario == "different-currency")
        {
            await using var change = _database.CreateDbContext();
            change.Entry(await change.TrackedItems.SingleAsync()).Property(entry => entry.CurrencyCode).CurrentValue = "ZAR";
            await change.SaveChangesAsync();
            var differentCurrency = await snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 2), [new(item.Id, 190m)]);
            invalidSourceId = Assert.Single(differentCurrency.ItemSnapshots).Id;
            change.ChangeTracker.Clear();
            change.Entry(await change.TrackedItems.SingleAsync()).Property(entry => entry.CurrencyCode).CurrentValue = "GBP";
            await change.SaveChangesAsync();
        }
        else
        {
            var other = scenario == "foreign-household" ? await TrackedItemTestSetup.CreateAsync(_database) : setup;
            var otherItem = await other.CreateItemAsync(items);
            var source = await snapshots.CreateAsync(other.HouseholdId, new DateOnly(2023, 1, 2), [new(otherItem.Id, 50m)]);
            invalidSourceId = source.ItemSnapshots.Single(entry => entry.TrackedItemId == otherItem.Id).Id;
        }
        var carried = await snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 2, 1), sourceSnapshotId: original.Id);
        var entryId = carried.ItemSnapshots.Single(entry => entry.TrackedItemId == item.Id).Id;
        await using var context = _database.CreateDbContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ItemSnapshots\" SET \"CarriedFromItemSnapshotId\" = {invalidSourceId} WHERE \"Id\" = {entryId}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Theory]
    [InlineData("no-source")]
    [InlineData("self-source")]
    [InlineData("missing-amount")]
    [InlineData("half-confirmation")]
    [InlineData("invalid-precision")]
    [InlineData("confirmed-with-source")]
    public async Task PostgreSqlRejectsInconsistentCarryMetadata(string invalidState)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(new TrackedItemService(_database));
        var snapshots = new SnapshotService(_database);
        await snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(item.Id, 10m)]);
        var carried = await snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 2, 1));
        var entryId = carried.ItemSnapshots.Single().Id;
        var update = invalidState switch
        {
            "no-source" => "\"CarriedFromItemSnapshotId\" = NULL",
            "self-source" => "\"CarriedFromItemSnapshotId\" = \"Id\"",
            "missing-amount" => "\"Amount\" = NULL",
            "half-confirmation" => "\"LastConfirmedDate\" = NULL",
            "invalid-precision" => "\"LastConfirmedPrecision\" = 9",
            _ => "\"ValueOrigin\" = 2"
        };
        await using var context = _database.CreateDbContext();
        var sql = $"UPDATE \"ItemSnapshots\" SET {update} WHERE \"Id\" = @entryId";
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, new NpgsqlParameter("entryId", entryId)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task FutureConfirmationMetadataCannotBeCarriedEvenIfTheSourceDateLooksEarlier()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(new TrackedItemService(_database));
        var snapshots = new SnapshotService(_database);
        await snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(item.Id, 10m)]);
        await using var context = _database.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("UPDATE \"ItemSnapshots\" SET \"LastConfirmedDate\" = DATE '2024-01-01'");
        await Assert.ThrowsAsync<Hearthcast.Data.Validation.BusinessRuleViolationException>(() => snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 2, 1)));
        Assert.Equal(1, await context.Snapshots.CountAsync());
    }

    [Theory]
    [InlineData(1, 9_999)]
    [InlineData(3_333, 6_667)]
    public async Task FractionalSharesAreCopiedExactlyWhileNewCapturesUseTheCurrentAllocation(int firstShare, int secondShare)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var items = new TrackedItemService(_database);
        var item = await setup.CreateItemAsync(items, allocations:
            [new(setup.HolderPersonId, firstShare), new(setup.OtherPersonId, secondShare)]);
        var snapshots = new SnapshotService(_database);
        var first = await snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(item.Id, 0.01m)]);
        await items.SetHouseholdAllocationAsync(setup.HouseholdId, item.Id, true);
        var next = await snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 2, 1));
        var saved = await snapshots.GetAsync(setup.HouseholdId, first.Id);
        var oldShares = Assert.Single(saved.ItemSnapshots).HouseholdAllocations;
        Assert.Equal(firstShare, oldShares.Single(share => share.SnapshotPerson.PersonId == setup.HolderPersonId).ShareBasisPoints);
        Assert.Equal(secondShare, oldShares.Single(share => share.SnapshotPerson.PersonId == setup.OtherPersonId).ShareBasisPoints);
        Assert.Equal(10_000, Assert.Single(Assert.Single(next.ItemSnapshots).HouseholdAllocations).ShareBasisPoints);
        Assert.Equal(0.01m, Assert.Single(next.ItemSnapshots).Amount);
    }

    [Fact]
    public async Task DowngradeCannotSilentlyDiscardExistingCarryProvenance()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(new TrackedItemService(_database));
        var snapshots = new SnapshotService(_database);
        var original = await snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(item.Id, 10m)]);
        var carried = await snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 2, 1));
        await using var context = _database.CreateDbContext();
        // Test this migration's rollback boundary independently of later setup migrations.
        await context.GetService<IMigrator>().MigrateAsync("20261030112256_AddSnapshotCarryForwardAndCorrections");
        var appliedBefore = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync("20261029230521_CreateInitialPostgresSchema"));
        var retained = await context.Snapshots.AsNoTracking().Include(snapshot => snapshot.ItemSnapshots)
            .SingleAsync(snapshot => snapshot.Id == carried.Id);
        Assert.Equal(original.ItemSnapshots.Single().Id, Assert.Single(retained.ItemSnapshots).CarriedFromItemSnapshotId);
        Assert.Equal(10m, Assert.Single(retained.ItemSnapshots).Amount);
        Assert.Equal(appliedBefore, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        await context.Database.MigrateAsync();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task IncrementalMigrationPreservesExistingSnapshotsAndInitializesTheirRevisions()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(new TrackedItemService(_database));
        var snapshots = new SnapshotService(_database);
        var original = await snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(item.Id, 123.45m)]);
        await using var context = _database.CreateDbContext();
        await context.GetService<IMigrator>().MigrateAsync("20261029230521_CreateInitialPostgresSchema");
        await context.Database.MigrateAsync();
        var restored = await snapshots.GetAsync(setup.HouseholdId, original.Id);
        Assert.NotEqual(Guid.Empty, restored.Revision);
        Assert.Equal(123.45m, Assert.Single(restored.ItemSnapshots).Amount);
        Assert.Equal(SnapshotValueOrigin.Confirmed, Assert.Single(restored.ItemSnapshots).ValueOrigin);
        var carried = await snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 2, 1));
        Assert.Equal(123.45m, Assert.Single(carried.ItemSnapshots).Amount);
        await snapshots.CorrectValuesAsync(setup.HouseholdId, restored.Id, restored.Revision, [new(item.Id, 124m)]);
        Assert.Equal(123.45m, Assert.Single((await snapshots.GetAsync(setup.HouseholdId, carried.Id)).ItemSnapshots).Amount);
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
