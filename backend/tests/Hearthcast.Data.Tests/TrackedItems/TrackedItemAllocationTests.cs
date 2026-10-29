using Hearthcast.Data.Households;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Tests.TrackedItems;

public sealed class TrackedItemAllocationTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _database = new();
    private readonly TrackedItemService _items;
    private readonly HouseholdService _households;

    public TrackedItemAllocationTests()
    {
        _items = new TrackedItemService(_database);
        _households = new HouseholdService(_database);
    }

    public Task InitializeAsync()
    {
        return _database.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        return _database.DisposeAsync();
    }

    [Fact]
    public async Task SharedAllocationDoesNotChangeTheActualHolder()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        await setup.CreateItemAsync(_items, allocations:
            [new(setup.HolderPersonId, 5_000), new(setup.OtherPersonId, 5_000)]);

        await using var context = _database.CreateDbContext();
        var saved = await context.TrackedItems.Include(item => item.HouseholdAllocations).SingleAsync();
        Assert.Equal(setup.HolderPersonId, saved.HolderPersonId);
        Assert.Equal(2, saved.HouseholdAllocations.Count);
        Assert.All(saved.HouseholdAllocations, share => Assert.Equal(5_000, share.ShareBasisPoints));
    }

    [Fact]
    public async Task ANonparticipatingHolderRequiresAnExplicitAllocationToParticipants()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database, otherPersonParticipates: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _items.CreateAsync(
            setup.HouseholdId, setup.GroupId, setup.OtherPersonId, "Item A", "GBP", "GB", true));

        var item = await _items.CreateAsync(setup.HouseholdId, setup.GroupId, setup.OtherPersonId,
            "Item A", "GBP", "GB", true, [new(setup.HolderPersonId, 10_000)]);
        await using var context = _database.CreateDbContext();
        Assert.Equal(setup.OtherPersonId, item.HolderPersonId);
        Assert.Equal(setup.HolderPersonId, (await context.TrackedItemHouseholdAllocations.SingleAsync()).PersonId);
        Assert.Equal(1, await context.TrackedItems.CountAsync());
    }

    [Theory]
    [InlineData(4_000, 5_000)]
    [InlineData(6_000, 5_000)]
    public async Task IncludedAllocationsMustTotalExactlyOneHundredPercent(int firstShare, int secondShare)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        await Assert.ThrowsAsync<ArgumentException>(() => setup.CreateItemAsync(_items, allocations:
            [new(setup.HolderPersonId, firstShare), new(setup.OtherPersonId, secondShare)]));

        await using var context = _database.CreateDbContext();
        Assert.Equal(0, await context.TrackedItems.CountAsync());
    }

    [Fact]
    public async Task ExplicitlyEmptyIncludedAllocationsAreNotReplacedWithDefaults()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        await Assert.ThrowsAsync<ArgumentException>(() => setup.CreateItemAsync(_items, allocations: []));
    }

    [Fact]
    public async Task DuplicateRecipientsAreRejectedEvenWhenTheirSharesTotalOneHundredPercent()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        await Assert.ThrowsAsync<ArgumentException>(() => setup.CreateItemAsync(_items, allocations:
            [new(setup.HolderPersonId, 5_000), new(setup.HolderPersonId, 5_000)]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(10_001)]
    public async Task IndividualSharesMustBeWithinRange(int share)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => setup.CreateItemAsync(_items, allocations:
            [new(setup.HolderPersonId, share)]));
    }

    [Fact]
    public async Task ExcludedItemsCannotHaveHouseholdAllocations()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        await Assert.ThrowsAsync<ArgumentException>(() => setup.CreateItemAsync(_items, false,
            [new(setup.HolderPersonId, 10_000)]));
    }

    [Fact]
    public async Task NonparticipantsCannotReceiveAnAllocation()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database, otherPersonParticipates: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.CreateItemAsync(_items, allocations:
            [new(setup.HolderPersonId, 5_000), new(setup.OtherPersonId, 5_000)]));
    }

    [Fact]
    public async Task AllocationRecipientsMustBelongToTheSameHousehold()
    {
        var first = await TrackedItemTestSetup.CreateAsync(_database);
        var second = await TrackedItemTestSetup.CreateAsync(_database);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => first.CreateItemAsync(_items, allocations:
            [new(second.OtherPersonId, 10_000)]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqliteRejectsAllocationItemsOrRecipientsFromAnotherHousehold(bool foreignItem)
    {
        var first = await TrackedItemTestSetup.CreateAsync(_database);
        var second = await TrackedItemTestSetup.CreateAsync(_database);
        var firstItem = await first.CreateItemAsync(_items, false);
        var secondItem = await second.CreateItemAsync(_items, false);
        var itemId = foreignItem ? secondItem.Id : firstItem.Id;
        var personId = foreignItem ? first.HolderPersonId : second.HolderPersonId;
        await using var context = _database.CreateDbContext();

        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "TrackedItemHouseholdAllocations" ("TrackedItemId", "HouseholdId", "PersonId", "ShareBasisPoints")
                VALUES ({itemId}, {first.HouseholdId}, {personId}, {10_000})
                """));
        Assert.Equal(787, exception.SqliteExtendedErrorCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(10_001)]
    public async Task SqliteEnforcesTheIndividualShareRange(int share)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items, false);
        await using var context = _database.CreateDbContext();
        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "TrackedItemHouseholdAllocations" ("TrackedItemId", "HouseholdId", "PersonId", "ShareBasisPoints")
                VALUES ({item.Id}, {setup.HouseholdId}, {setup.HolderPersonId}, {share})
                """));
        Assert.Equal(275, exception.SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task AllocationsCanBeReplacedExcludedAndRestoredWithoutChangingTheHolder()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items);
        await _items.SetHouseholdAllocationAsync(setup.HouseholdId, item.Id, true,
            [new(setup.HolderPersonId, 5_000), new(setup.OtherPersonId, 5_000)]);
        await using (var shared = _database.CreateDbContext())
        {
            Assert.Equal(2, await shared.TrackedItemHouseholdAllocations.CountAsync());
        }

        await _items.SetHouseholdAllocationAsync(setup.HouseholdId, item.Id, true,
            [new(setup.OtherPersonId, 10_000)]);
        await using (var reassigned = _database.CreateDbContext())
        {
            Assert.Equal(setup.OtherPersonId,
                (await reassigned.TrackedItemHouseholdAllocations.SingleAsync()).PersonId);
        }

        await _items.SetHouseholdAllocationAsync(setup.HouseholdId, item.Id, false);
        await using (var excluded = _database.CreateDbContext())
        {
            Assert.Equal(0, await excluded.TrackedItemHouseholdAllocations.CountAsync());
            Assert.False((await excluded.TrackedItems.SingleAsync()).IncludeInHousehold);
        }

        await _items.SetHouseholdAllocationAsync(setup.HouseholdId, item.Id, true);
        await using var restored = _database.CreateDbContext();
        var saved = await restored.TrackedItems.Include(item => item.HouseholdAllocations).SingleAsync();
        Assert.Equal(item.Id, saved.Id);
        Assert.Equal(setup.HolderPersonId, saved.HolderPersonId);
        Assert.Equal(setup.HolderPersonId, Assert.Single(saved.HouseholdAllocations).PersonId);
        Assert.Equal(10_000, Assert.Single(saved.HouseholdAllocations).ShareBasisPoints);
    }

    [Fact]
    public async Task InvalidReconfigurationPreservesThePreviousAllocation()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items);
        await Assert.ThrowsAsync<ArgumentException>(() => _items.SetHouseholdAllocationAsync(
            setup.HouseholdId, item.Id, true, [new(setup.OtherPersonId, 9_000)]));

        await using var context = _database.CreateDbContext();
        var saved = await context.TrackedItemHouseholdAllocations.SingleAsync();
        Assert.Equal(setup.HolderPersonId, saved.PersonId);
        Assert.Equal(10_000, saved.ShareBasisPoints);
    }

    [Fact]
    public async Task RecipientsMustBeReallocatedBeforeTheyCanStopParticipatingOrBeRemoved()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items, allocations: [new(setup.OtherPersonId, 10_000)]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _households.SetPersonParticipationAsync(setup.HouseholdId, setup.OtherPersonId, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _households.RemovePersonAsync(setup.HouseholdId, setup.OtherPersonId));

        await _items.SetHouseholdAllocationAsync(setup.HouseholdId, item.Id, true);
        await _households.SetPersonParticipationAsync(setup.HouseholdId, setup.OtherPersonId, false);
        await _households.RemovePersonAsync(setup.HouseholdId, setup.OtherPersonId);

        await using var context = _database.CreateDbContext();
        Assert.Equal(setup.HolderPersonId, (await context.People.SingleAsync()).Id);
        Assert.Equal(setup.HolderPersonId, (await context.TrackedItemHouseholdAllocations.SingleAsync()).PersonId);
    }

    [Fact]
    public async Task AHolderCanStopParticipatingAfterTheirAllocationIsReassigned()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items, allocations: [new(setup.OtherPersonId, 10_000)]);
        await _households.SetPersonParticipationAsync(setup.HouseholdId, setup.HolderPersonId, false);

        await using var context = _database.CreateDbContext();
        Assert.False((await context.People.SingleAsync(person => person.Id == setup.HolderPersonId))
            .ParticipatesInHouseholdFinances);
        Assert.Equal(setup.HolderPersonId, (await context.TrackedItems.SingleAsync()).HolderPersonId);
        Assert.Equal(setup.OtherPersonId, (await context.TrackedItemHouseholdAllocations.SingleAsync()).PersonId);
    }

    [Fact]
    public async Task ConcurrentAllocationAndParticipationChangesCannotCreateAnIneligibleAllocation()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items, false);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allocate = Task.Run(async () =>
        {
            await start.Task;
            return await Record.ExceptionAsync(() => _items.SetHouseholdAllocationAsync(
                setup.HouseholdId, item.Id, true, [new(setup.OtherPersonId, 10_000)]));
        });
        var stopParticipating = Task.Run(async () =>
        {
            await start.Task;
            return await Record.ExceptionAsync(() => _households.SetPersonParticipationAsync(
                setup.HouseholdId, setup.OtherPersonId, false));
        });
        start.SetResult();
        var outcomes = await Task.WhenAll(allocate, stopParticipating);
        Assert.Single(outcomes, exception => exception is null);
        Assert.IsType<InvalidOperationException>(Assert.Single(outcomes, exception => exception is not null));

        await using var context = _database.CreateDbContext();
        var recipient = await context.People.SingleAsync(person => person.Id == setup.OtherPersonId);
        Assert.Equal(recipient.ParticipatesInHouseholdFinances, (await context.TrackedItems.SingleAsync()).IncludeInHousehold);
        Assert.Equal(recipient.ParticipatesInHouseholdFinances ? 1 : 0,
            await context.TrackedItemHouseholdAllocations.CountAsync());
    }

    [Fact]
    public async Task AFailedAllocationInsertRollsBackTheNewItem()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        await using var context = _database.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER "FailAllocationInsert" BEFORE INSERT ON "TrackedItemHouseholdAllocations"
            BEGIN
                SELECT RAISE(ABORT, 'Synthetic allocation failure');
            END;
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() => setup.CreateItemAsync(_items));
        Assert.Equal(0, await context.TrackedItems.CountAsync());
        Assert.Equal(0, await context.TrackedItemHouseholdAllocations.CountAsync());
    }
}
