using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Tests.TrackedItems;
using Hearthcast.Data.TrackedItems;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Tests.Snapshots;

public sealed class SnapshotIntegrityTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _database = new();
    private readonly SnapshotService _snapshots;
    private readonly TrackedItemService _items;

    public SnapshotIntegrityTests()
    {
        _snapshots = new SnapshotService(_database);
        _items = new TrackedItemService(_database);
    }

    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task AFailedAllocationInsertRollsBackTheWholeCapture()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        await setup.CreateItemAsync(_items);
        await using var context = _database.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER "FailSnapshotAllocationInsert" BEFORE INSERT ON "ItemSnapshotHouseholdAllocations"
            BEGIN
                SELECT RAISE(ABORT, 'Synthetic insert failure');
            END;
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => _snapshots.CreateAsync(setup.HouseholdId));
        Assert.Empty(await context.Snapshots.ToListAsync());
        Assert.Empty(await context.SnapshotPeople.ToListAsync());
        Assert.Empty(await context.ItemSnapshots.ToListAsync());
        Assert.Empty(await context.ItemSnapshotHouseholdAllocations.ToListAsync());
        Assert.Equal(1, await context.TrackedItems.CountAsync());
    }

    [Theory]
    [InlineData("missing-share")]
    [InlineData("excluded-with-share")]
    [InlineData("ineligible-recipient")]
    [InlineData("no-participant")]
    public async Task CaptureRejectsMalformedCurrentSetupRatherThanSavingInconsistentHistory(string scenario)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        await setup.CreateItemAsync(_items);
        await using var context = _database.CreateDbContext();
        switch (scenario)
        {
            case "missing-share":
                await context.Database.ExecuteSqlRawAsync("DELETE FROM TrackedItemHouseholdAllocations"); break;
            case "excluded-with-share":
                await context.Database.ExecuteSqlRawAsync("UPDATE TrackedItems SET IncludeInHousehold = 0"); break;
            case "ineligible-recipient":
                await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE People SET ParticipatesInHouseholdFinances = 0 WHERE Id = {setup.HolderPersonId}"); break;
            case "no-participant":
                await context.Database.ExecuteSqlRawAsync("UPDATE People SET ParticipatesInHouseholdFinances = 0"); break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => _snapshots.CreateAsync(setup.HouseholdId));
        Assert.Equal(0, await context.Snapshots.CountAsync());
    }

    [Theory]
    [InlineData("holder")]
    [InlineData("recipient")]
    [InlineData("entry")]
    public async Task SqliteRejectsReferencesToAnotherSnapshotEvenWithinTheSameHousehold(string reference)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        await setup.CreateItemAsync(_items);
        var first = await _snapshots.CreateAsync(setup.HouseholdId);
        var second = await _snapshots.CreateAsync(setup.HouseholdId);
        var entry = Assert.Single(first.ItemSnapshots);
        var foreignPerson = second.People.Single(person => person.PersonId == setup.HolderPersonId);
        await using var context = _database.CreateDbContext();
        var exception = await Assert.ThrowsAsync<SqliteException>(() => reference switch
        {
            "holder" => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE ItemSnapshots SET HolderSnapshotPersonId = {foreignPerson.Id} WHERE Id = {entry.Id}"),
            "recipient" => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE ItemSnapshotHouseholdAllocations SET SnapshotPersonId = {foreignPerson.Id} WHERE ItemSnapshotId = {entry.Id}"),
            _ => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE ItemSnapshotHouseholdAllocations SET ItemSnapshotId = {second.ItemSnapshots.Single().Id} WHERE ItemSnapshotId = {entry.Id}")
        });
        Assert.Equal(787, exception.SqliteExtendedErrorCode);
    }

    [Theory]
    [InlineData("person")]
    [InlineData("item")]
    [InlineData("category")]
    [InlineData("group")]
    [InlineData("snapshot-parent")]
    public async Task SqliteRejectsOriginalIdentitiesOrParentsFromAnotherHousehold(string reference)
    {
        var first = await TrackedItemTestSetup.CreateAsync(_database);
        await first.CreateItemAsync(_items);
        var snapshot = await _snapshots.CreateAsync(first.HouseholdId);
        var second = await TrackedItemTestSetup.CreateAsync(_database);
        var otherItem = await second.CreateItemAsync(_items);
        var otherSnapshot = await _snapshots.CreateAsync(second.HouseholdId);
        var entryId = snapshot.ItemSnapshots.Single().Id;
        var personId = snapshot.People.Single(person => person.PersonId == first.HolderPersonId).Id;
        await using var context = _database.CreateDbContext();
        var exception = await Assert.ThrowsAsync<SqliteException>(() => reference switch
        {
            "person" => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE SnapshotPeople SET PersonId = {second.HolderPersonId} WHERE Id = {personId}"),
            "item" => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE ItemSnapshots SET TrackedItemId = {otherItem.Id} WHERE Id = {entryId}"),
            "category" => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE ItemSnapshots SET CategoryId = {otherItem.Group.CategoryId} WHERE Id = {entryId}"),
            "group" => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE ItemSnapshots SET GroupId = {second.GroupId} WHERE Id = {entryId}"),
            _ => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE SnapshotPeople SET SnapshotId = {otherSnapshot.Id} WHERE Id = {personId}")
        });
        Assert.Equal(787, exception.SqliteExtendedErrorCode);
    }

    [Theory]
    [InlineData("person")]
    [InlineData("item")]
    public async Task SqliteEnforcesOneEntryPerOriginalIdentityInASnapshot(string duplicate)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        await setup.CreateItemAsync(_items);
        await setup.CreateItemAsync(_items);
        var snapshot = await _snapshots.CreateAsync(setup.HouseholdId);
        await using var context = _database.CreateDbContext();
        var exception = await Assert.ThrowsAsync<SqliteException>(() => duplicate == "person"
            ? context.Database.ExecuteSqlInterpolatedAsync($"UPDATE SnapshotPeople SET PersonId = {setup.HolderPersonId} WHERE PersonId = {setup.OtherPersonId}")
            : context.Database.ExecuteSqlInterpolatedAsync($"UPDATE ItemSnapshots SET TrackedItemId = {snapshot.ItemSnapshots.First().TrackedItemId} WHERE Id = {snapshot.ItemSnapshots.Last().Id}"));
        Assert.Equal(2067, exception.SqliteExtendedErrorCode);
    }

    [Theory]
    [InlineData("missing-confirmed-date")]
    [InlineData("missing-confirmed-precision")]
    [InlineData("unrecorded-with-amount")]
    [InlineData("confirmed-without-amount")]
    [InlineData("invalid-precision")]
    [InlineData("invalid-origin")]
    public async Task SqliteRejectsInconsistentAmountAndConfirmationMetadata(string invalidState)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items);
        await _snapshots.CreateAsync(setup.HouseholdId, values: [new(item.Id, 0m)]);
        await using var context = _database.CreateDbContext();
        var sql = invalidState switch
        {
            "missing-confirmed-date" => "UPDATE ItemSnapshots SET LastConfirmedDate = NULL",
            "missing-confirmed-precision" => "UPDATE ItemSnapshots SET LastConfirmedPrecision = NULL",
            "unrecorded-with-amount" => "UPDATE ItemSnapshots SET ValueOrigin = 1",
            "confirmed-without-amount" => "UPDATE ItemSnapshots SET Amount = NULL",
            "invalid-precision" => "UPDATE ItemSnapshots SET LastConfirmedPrecision = 99",
            _ => "UPDATE ItemSnapshots SET ValueOrigin = 99"
        };
        var exception = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(sql));
        Assert.Equal(275, exception.SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task SnapshotHistoryProtectsAFormerParticipantWithoutCurrentItems()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var snapshot = await _snapshots.CreateAsync(setup.HouseholdId);
        var households = new HouseholdService(_database);
        await households.SetPersonParticipationAsync(setup.HouseholdId, setup.OtherPersonId, false);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            households.RemovePersonAsync(setup.HouseholdId, setup.OtherPersonId));
        Assert.Contains("snapshot history", exception.Message);

        await using var context = _database.CreateDbContext();
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM People WHERE Id = {setup.OtherPersonId}"));
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM Snapshots WHERE Id = {snapshot.Id}"));
        Assert.Equal(2, await context.SnapshotPeople.CountAsync());
        Assert.Equal(1, await context.Snapshots.CountAsync());
    }

    [Fact]
    public async Task ARecordedItemCannotBeDeletedEvenWhenItHasNoCurrentAllocations()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items, false);
        await _snapshots.CreateAsync(setup.HouseholdId);
        await using var context = _database.CreateDbContext();
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM TrackedItems WHERE Id = {item.Id}"));
        Assert.Equal(1, await context.ItemSnapshots.CountAsync());
    }
}
