using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Tests.TrackedItems;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Tests.Snapshots;

public sealed class SnapshotCorrectionTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private readonly SnapshotService _snapshots;
    private readonly TrackedItemService _items;

    public SnapshotCorrectionTests()
    {
        _snapshots = new SnapshotService(_database);
        _items = new TrackedItemService(_database);
    }

    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task ReconfirmationAndClearingUpdateOnlySelectedEntriesAndPreserveSavedContext()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var firstItem = await setup.CreateItemAsync(_items);
        var secondItem = await setup.CreateItemAsync(_items);
        await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(firstItem.Id, 0m), new(secondItem.Id, 200m)]);
        var carried = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 2, 17), datePrecision: DatePrecision.Month);
        await _items.SetRetiredAsync(setup.HouseholdId, firstItem.Id, true);
        await _items.SetHouseholdAllocationAsync(setup.HouseholdId, firstItem.Id, false);
        var beforeCorrection = await _snapshots.GetAsync(setup.HouseholdId, carried.Id);
        var before = DateTimeOffset.UtcNow;
        var confirmed = await _snapshots.CorrectValuesAsync(setup.HouseholdId, carried.Id, carried.Revision, [new(firstItem.Id, 0m)]);
        var after = DateTimeOffset.UtcNow;
        var saved = await _snapshots.GetAsync(setup.HouseholdId, carried.Id);
        var entry = saved.ItemSnapshots.Single(item => item.TrackedItemId == firstItem.Id);
        Assert.Equal(SnapshotValueOrigin.Confirmed, entry.ValueOrigin);
        Assert.Equal(0m, entry.Amount);
        Assert.Equal(new DateOnly(2023, 2, 1), entry.LastConfirmedDate);
        Assert.Equal(DatePrecision.Month, entry.LastConfirmedPrecision);
        Assert.Null(entry.CarriedFromItemSnapshotId);
        Assert.True(entry.IncludeInHousehold);
        Assert.Single(entry.HouseholdAllocations);
        Assert.Equal(beforeCorrection.CreatedAtUtc, confirmed.CreatedAtUtc);
        Assert.InRange(confirmed.UpdatedAtUtc, before, after);
        Assert.NotEqual(carried.Revision, confirmed.Revision);
        var untouched = saved.ItemSnapshots.Single(item => item.TrackedItemId == secondItem.Id);
        Assert.Equal(SnapshotValueOrigin.CarriedForward, untouched.ValueOrigin);
        Assert.Equal(new DateOnly(2023, 1, 1), untouched.LastConfirmedDate);

        var cleared = await _snapshots.CorrectValuesAsync(setup.HouseholdId, carried.Id, saved.Revision, [new(secondItem.Id, null)]);
        var missing = cleared.ItemSnapshots.Single(item => item.TrackedItemId == secondItem.Id);
        Assert.Null(missing.Amount);
        Assert.Null(missing.LastConfirmedDate);
        Assert.Null(missing.CarriedFromItemSnapshotId);
        Assert.Equal(SnapshotValueOrigin.Unrecorded, missing.ValueOrigin);
        var next = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 3, 1));
        Assert.Null(Assert.Single(next.ItemSnapshots).Amount);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("negative")]
    [InlineData("unknown")]
    [InlineData("not-in-snapshot")]
    [InlineData("null")]
    public async Task InvalidBulkCorrectionsPreserveAllExistingValuesAndTheRevision(string scenario)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var first = await setup.CreateItemAsync(_items);
        var second = await setup.CreateItemAsync(_items);
        var original = await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(first.Id, 100m), new(second.Id, 200m)]);
        var added = await setup.CreateItemAsync(_items);
        ItemSnapshotValue[]? values = scenario switch
        {
            "empty" => [],
            "duplicate" => [new(first.Id, 50m), new(first.Id, 60m)],
            "negative" => [new(first.Id, 50m), new(second.Id, -1m)],
            "unknown" => [new(first.Id, 50m), new(Guid.NewGuid(), 1m)],
            "not-in-snapshot" => [new(first.Id, 50m), new(added.Id, 1m)],
            _ => null
        };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _snapshots.CorrectValuesAsync(setup.HouseholdId, original.Id, original.Revision, values!));
        var saved = await _snapshots.GetAsync(setup.HouseholdId, original.Id);
        Assert.Equal(original.Revision, saved.Revision);
        Assert.Equal(100m, saved.ItemSnapshots.Single(item => item.TrackedItemId == first.Id).Amount);
        Assert.Equal(200m, saved.ItemSnapshots.Single(item => item.TrackedItemId == second.Id).Amount);
    }

    [Fact]
    public async Task ForeignSnapshotsAndForeignItemIdsCannotBeCorrectedThroughAnotherHousehold()
    {
        var first = await TrackedItemTestSetup.CreateAsync(_database);
        var second = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await first.CreateItemAsync(_items);
        var otherItem = await second.CreateItemAsync(_items);
        var capture = await _snapshots.CreateAsync(first.HouseholdId, values: [new(item.Id, 100m)]);
        await Assert.ThrowsAsync<Hearthcast.Data.Validation.ResourceNotFoundException>(() => _snapshots.CorrectValuesAsync(second.HouseholdId, capture.Id, capture.Revision, [new(item.Id, 1m)]));
        await Assert.ThrowsAsync<Hearthcast.Data.Validation.InvalidValueException>(() => _snapshots.CorrectValuesAsync(first.HouseholdId, capture.Id, capture.Revision, [new(otherItem.Id, 1m)]));
        await Assert.ThrowsAsync<Hearthcast.Data.Validation.ResourceNotFoundException>(() => _snapshots.CorrectValuesAsync(first.HouseholdId, Guid.NewGuid(), capture.Revision, [new(item.Id, 1m)]));
        Assert.Equal(100m, Assert.Single((await _snapshots.GetAsync(first.HouseholdId, capture.Id)).ItemSnapshots).Amount);
    }

    [Fact]
    public async Task AStaleEditIsRejectedAndExistingDescendantsRemainIndependentCopies()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items);
        var original = await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(item.Id, 100m)]);
        var later = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 2, 1));
        var beforeCorrection = await _snapshots.GetAsync(setup.HouseholdId, later.Id);
        await _snapshots.CorrectValuesAsync(setup.HouseholdId, original.Id, original.Revision, [new(item.Id, null)]);
        await Assert.ThrowsAsync<SnapshotConflictException>(() => _snapshots.CorrectValuesAsync(setup.HouseholdId, original.Id, original.Revision, [new(item.Id, 500m)]));
        var unchanged = await _snapshots.GetAsync(setup.HouseholdId, later.Id);
        var copied = Assert.Single(unchanged.ItemSnapshots);
        Assert.Equal(100m, copied.Amount);
        Assert.Equal(SnapshotValueOrigin.CarriedForward, copied.ValueOrigin);
        Assert.Equal(original.ItemSnapshots.Single().Id, copied.CarriedFromItemSnapshotId);
        Assert.Equal(later.Revision, unchanged.Revision);
        Assert.Equal(beforeCorrection.UpdatedAtUtc, unchanged.UpdatedAtUtc);
    }

    [Fact]
    public async Task ConcurrentCorrectionsWithTheSameRevisionCommitExactlyOneCompleteEdit()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var first = await setup.CreateItemAsync(_items);
        var second = await setup.CreateItemAsync(_items);
        var capture = await _snapshots.CreateAsync(setup.HouseholdId, values: [new(first.Id, 100m), new(second.Id, 200m)]);
        var barrier = new ConcurrentSaveBarrier();
        var snapshots = new SnapshotService(new InterceptingContextFactory(_database, barrier));
        var changes = new[] { 1m, 2m }.Select(value => Record.ExceptionAsync(() =>
            snapshots.CorrectValuesAsync(setup.HouseholdId, capture.Id,
                capture.Revision, [new(first.Id, value), new(second.Id, value * 10m)]))).ToArray();
        var outcomes = await Task.WhenAll(changes);
        Assert.Equal(2, barrier.Arrivals);
        Assert.Single(outcomes, exception => exception is null);
        Assert.IsType<SnapshotConflictException>(Assert.Single(outcomes, exception => exception is not null));
        var saved = await _snapshots.GetAsync(setup.HouseholdId, capture.Id);
        var firstAmount = saved.ItemSnapshots.Single(item => item.TrackedItemId == first.Id).Amount;
        Assert.Contains(firstAmount, new decimal?[] { 1m, 2m });
        Assert.Equal(firstAmount * 10m, saved.ItemSnapshots.Single(item => item.TrackedItemId == second.Id).Amount);
    }

    [Fact]
    public async Task AFailureOnTheSecondEntryUpdateRollsBackValuesRevisionAndEditTime()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var first = await setup.CreateItemAsync(_items);
        var second = await setup.CreateItemAsync(_items);
        var capture = await _snapshots.CreateAsync(setup.HouseholdId, values: [new(first.Id, 100m), new(second.Id, 200m)]);
        var before = await _snapshots.GetAsync(setup.HouseholdId, capture.Id);
        await using var context = _database.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE SEQUENCE "CorrectionAttempts";
            CREATE FUNCTION "FailSecondCorrection"() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF nextval('"CorrectionAttempts"') = 2 THEN
                    RAISE EXCEPTION 'Synthetic correction failure';
                END IF;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER "FailSecondCorrection" BEFORE UPDATE ON "ItemSnapshots"
            FOR EACH ROW EXECUTE FUNCTION "FailSecondCorrection"();
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => _snapshots.CorrectValuesAsync(setup.HouseholdId, capture.Id,
            capture.Revision, [new(first.Id, 1m), new(second.Id, 2m)]));
        var saved = await _snapshots.GetAsync(setup.HouseholdId, capture.Id);
        Assert.Equal(before.Revision, saved.Revision);
        Assert.Equal(before.UpdatedAtUtc, saved.UpdatedAtUtc);
        Assert.Equal(100m, saved.ItemSnapshots.Single(item => item.TrackedItemId == first.Id).Amount);
        Assert.Equal(200m, saved.ItemSnapshots.Single(item => item.TrackedItemId == second.Id).Amount);
    }
}
