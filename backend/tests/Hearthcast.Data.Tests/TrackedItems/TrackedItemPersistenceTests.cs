using Hearthcast.Data.Classification;
using Hearthcast.Data.Households;
using Hearthcast.Data.Persistence;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Npgsql;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Tests.TrackedItems;

public sealed class TrackedItemPersistenceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private readonly TrackedItemService _items;

    public TrackedItemPersistenceTests()
    {
        _items = new TrackedItemService(_database);
    }

    public Task InitializeAsync()
    {
        return _database.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        return _database.DisposeAsync();
    }

    [Theory]
    [InlineData(FinancialType.Asset)]
    [InlineData(FinancialType.Debt)]
    public async Task PersonalItemsPersistWithClassificationAndDefaultHolderAllocation(FinancialType financialType)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database, financialType: financialType);
        var item = await _items.CreateAsync(setup.HouseholdId, setup.GroupId, setup.HolderPersonId,
            " Item A ", " gbp ", " za ", true);

        await using var context = _database.CreateDbContext();
        var saved = await context.TrackedItems.Include(item => item.Group).ThenInclude(group => group.Category)
            .Include(item => item.HouseholdAllocations).SingleAsync();
        Assert.Equal(item.Id, saved.Id);
        Assert.Equal("Item A", saved.Name);
        Assert.Equal("GBP", saved.CurrencyCode);
        Assert.Equal("ZA", saved.CountryCode);
        Assert.Equal(setup.HolderPersonId, saved.HolderPersonId);
        Assert.Equal(financialType, saved.Group.Category.FinancialType);
        Assert.True(saved.IncludeInHousehold);
        Assert.False(saved.IsRetired);
        var allocation = Assert.Single(saved.HouseholdAllocations);
        Assert.Equal(setup.HolderPersonId, allocation.PersonId);
        Assert.Equal(10_000, allocation.ShareBasisPoints);
    }

    [Fact]
    public async Task AnExcludedItemCanBeHeldByANonparticipant()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database, otherPersonParticipates: false);
        var item = await _items.CreateAsync(setup.HouseholdId, setup.GroupId, setup.OtherPersonId,
            "Item A", "GBP", "GB", false);

        await using var context = _database.CreateDbContext();
        var saved = await context.TrackedItems.SingleAsync();
        Assert.Equal(item.Id, saved.Id);
        Assert.Equal(setup.OtherPersonId, saved.HolderPersonId);
        Assert.False(saved.IncludeInHousehold);
        Assert.Equal(0, await context.TrackedItemHouseholdAllocations.CountAsync());
    }

    [Theory]
    [InlineData(null, "GBP", "GB")]
    [InlineData(" \t ", "GBP", "GB")]
    [InlineData("Item A", null, "GB")]
    [InlineData("Item A", "GB", "GB")]
    [InlineData("Item A", "G1P", "GB")]
    [InlineData("Item A", "GBP", null)]
    [InlineData("Item A", "GBP", "GBR")]
    public async Task InvalidMetadataDoesNotSaveAnItem(string? name, string? currencyCode, string? countryCode)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _items.CreateAsync(
            setup.HouseholdId, setup.GroupId, setup.HolderPersonId, name!, currencyCode!, countryCode!, false));

        await using var context = _database.CreateDbContext();
        Assert.Equal(0, await context.TrackedItems.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheServiceRejectsAHolderOrGroupFromAnotherHousehold(bool foreignGroup)
    {
        var first = await TrackedItemTestSetup.CreateAsync(_database);
        var second = await TrackedItemTestSetup.CreateAsync(_database);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _items.CreateAsync(first.HouseholdId,
            foreignGroup ? second.GroupId : first.GroupId,
            foreignGroup ? first.HolderPersonId : second.HolderPersonId, "Item A", "GBP", "GB", false));

        await using var context = _database.CreateDbContext();
        Assert.Equal(0, await context.TrackedItems.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostgresRejectsAHolderOrGroupFromAnotherHousehold(bool foreignGroup)
    {
        var first = await TrackedItemTestSetup.CreateAsync(_database);
        var second = await TrackedItemTestSetup.CreateAsync(_database);
        await using var context = _database.CreateDbContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertItemAsync(context,
            first.HouseholdId, foreignGroup ? second.GroupId : first.GroupId,
            foreignGroup ? first.HolderPersonId : second.HolderPersonId));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Theory]
    [InlineData(null, "GBP", "GB")]
    [InlineData("", "GBP", "GB")]
    [InlineData("Item A", "gbp", "GB")]
    [InlineData("Item A", "GBP", "G")]
    public async Task PostgresEnforcesRequiredNamesAndCodeFormats(string? name, string currencyCode, string countryCode)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        await using var context = _database.CreateDbContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertItemAsync(context,
            setup.HouseholdId, setup.GroupId, setup.HolderPersonId, name, currencyCode, countryCode));
        Assert.Equal(name is null ? PostgresErrorCodes.NotNullViolation : PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task ReferencedGroupsCannotBeDeleted()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        await setup.CreateItemAsync(_items, false);
        await using var context = _database.CreateDbContext();
        context.ItemGroups.Remove(await context.ItemGroups.SingleAsync());
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(1, await context.TrackedItems.CountAsync());
    }

    [Fact]
    public async Task HoldersCannotBeRemovedThroughTheServiceOrDirectDatabaseWrites()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        await setup.CreateItemAsync(_items, false);
        var households = new HouseholdService(_database);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            households.RemovePersonAsync(setup.HouseholdId, setup.HolderPersonId));

        await using var context = _database.CreateDbContext();
        context.People.Remove(await context.People.SingleAsync(person => person.Id == setup.HolderPersonId));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(2, await context.People.CountAsync());
    }

    [Fact]
    public async Task RenameAndRetirementPreserveIdentityAndAllocation()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items);
        await _items.SetRetiredAsync(setup.HouseholdId, item.Id, true);
        await using (var context = _database.CreateDbContext())
        {
            var saved = await context.TrackedItems.SingleAsync();
            saved.Rename(" Renamed item ");
            await context.SaveChangesAsync();
        }

        await using var verification = _database.CreateDbContext();
        var renamed = await verification.TrackedItems.Include(item => item.HouseholdAllocations).SingleAsync();
        Assert.Equal(item.Id, renamed.Id);
        Assert.Equal("Renamed item", renamed.Name);
        Assert.True(renamed.IsRetired);
        Assert.True(renamed.IncludeInHousehold);
        Assert.Equal(setup.HolderPersonId, renamed.HolderPersonId);
        Assert.Equal(10_000, Assert.Single(renamed.HouseholdAllocations).ShareBasisPoints);
    }

    private static Task<int> InsertItemAsync(HearthcastDbContext context,
        Guid householdId, Guid groupId, Guid holderId,
        string? name = "Item A", string currencyCode = "GBP", string countryCode = "GB")
    {
        return context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TrackedItems"
                ("Id", "HouseholdId", "GroupId", "HolderPersonId", "Name", "CurrencyCode", "CountryCode", "IncludeInHousehold", "IsRetired")
            VALUES ({Guid.NewGuid()}, {householdId}, {groupId}, {holderId}, {name}, {currencyCode}, {countryCode}, {false}, {false})
            """);
    }
}
