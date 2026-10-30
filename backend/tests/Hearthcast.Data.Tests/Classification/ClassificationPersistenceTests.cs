using Hearthcast.Data.Validation;
using Hearthcast.Data.Classification;
using Hearthcast.Data.Households;
using Hearthcast.Data.Tests.Infrastructure;
using Npgsql;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Tests.Classification;

public sealed class ClassificationPersistenceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private readonly HouseholdService _households;
    private readonly ClassificationService _classification;

    public ClassificationPersistenceTests()
    {
        _households = new HouseholdService(_database);
        _classification = new ClassificationService(_database);
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
    public async Task CategoryAndGroupPersistWithAnExplicitFinancialType(FinancialType financialType)
    {
        var household = await _households.CreateAsync("Household A", "Person A");
        var category = await _classification.CreateCategoryAsync(household.Id, " Category A ", financialType);
        var group = await _classification.CreateGroupAsync(household.Id, category.Id, " Group A ");

        await using var context = _database.CreateDbContext();
        var saved = await context.ItemGroups.Include(item => item.Category).SingleAsync();
        Assert.Equal(group.Id, saved.Id);
        Assert.Equal(category.Id, saved.CategoryId);
        Assert.Equal(household.Id, saved.HouseholdId);
        Assert.Equal(household.Id, saved.Category.HouseholdId);
        Assert.Equal("Category A", saved.Category.Name);
        Assert.Equal("Group A", saved.Name);
        Assert.Equal(financialType, saved.Category.FinancialType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task AnUndefinedFinancialTypeCannotBeSaved(int financialType)
    {
        var household = await _households.CreateAsync("Household A", "Person A");

        await Assert.ThrowsAsync<InvalidValueException>(() =>
            _classification.CreateCategoryAsync(household.Id, "Category A", (FinancialType)financialType));

        await using var context = _database.CreateDbContext();
        Assert.Equal(0, await context.Categories.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task CategoryNamesAreRequired(string? name)
    {
        var household = await _households.CreateAsync("Household A", "Person A");

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _classification.CreateCategoryAsync(household.Id, name!, FinancialType.Asset));

        await using var context = _database.CreateDbContext();
        Assert.Equal(0, await context.Categories.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task GroupNamesAreRequired(string? name)
    {
        var household = await _households.CreateAsync("Household A", "Person A");
        var category = await _classification.CreateCategoryAsync(household.Id, "Category A", FinancialType.Asset);

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _classification.CreateGroupAsync(household.Id, category.Id, name!));

        await using var context = _database.CreateDbContext();
        Assert.Equal(0, await context.ItemGroups.CountAsync());
    }

    [Fact]
    public async Task ACategoryRequiresAnExistingHousehold()
    {
        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            _classification.CreateCategoryAsync(Guid.NewGuid(), "Category A", FinancialType.Asset));

        await using var context = _database.CreateDbContext();
        Assert.Equal(0, await context.Categories.CountAsync());
    }

    [Fact]
    public async Task AGroupRequiresAnExistingCategory()
    {
        var household = await _households.CreateAsync("Household A", "Person A");

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            _classification.CreateGroupAsync(household.Id, Guid.NewGuid(), "Group A"));

        await using var context = _database.CreateDbContext();
        Assert.Equal(0, await context.ItemGroups.CountAsync());
    }

    [Fact]
    public async Task TheServiceRejectsACategoryFromAnotherHousehold()
    {
        var first = await _households.CreateAsync("Household A", "Person A");
        var second = await _households.CreateAsync("Household B", "Person B");
        var category = await _classification.CreateCategoryAsync(second.Id, "Category B", FinancialType.Asset);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            _classification.CreateGroupAsync(first.Id, category.Id, "Group A"));

        await using var context = _database.CreateDbContext();
        Assert.Equal(0, await context.ItemGroups.CountAsync());
    }

    [Fact]
    public async Task PostgresRejectsACategoryWithoutAHousehold()
    {
        await using var context = _database.CreateDbContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Categories" ("Id", "HouseholdId", "Name", "FinancialType")
                VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {"Category A"}, {1})
                """));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Fact]
    public async Task PostgresRejectsAGroupWithoutACategory()
    {
        var household = await _households.CreateAsync("Household A", "Person A");
        await using var context = _database.CreateDbContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ItemGroups" ("Id", "HouseholdId", "CategoryId", "Name")
                VALUES ({Guid.NewGuid()}, {household.Id}, {Guid.NewGuid()}, {"Group A"})
                """));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Fact]
    public async Task TheCompositeForeignKeyRejectsACategoryInAnotherExistingHousehold()
    {
        var first = await _households.CreateAsync("Household A", "Person A");
        var second = await _households.CreateAsync("Household B", "Person B");
        var category = await _classification.CreateCategoryAsync(second.Id, "Category B", FinancialType.Asset);
        await using var context = _database.CreateDbContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ItemGroups" ("Id", "HouseholdId", "CategoryId", "Name")
                VALUES ({Guid.NewGuid()}, {first.Id}, {category.Id}, {"Group A"})
                """));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task PostgresRejectsUndefinedFinancialTypes(int financialType)
    {
        var household = await _households.CreateAsync("Household A", "Person A");
        await using var context = _database.CreateDbContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Categories" ("Id", "HouseholdId", "Name", "FinancialType")
                VALUES ({Guid.NewGuid()}, {household.Id}, {"Category A"}, {financialType})
                """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "")]
    [InlineData(false, "   ")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    public async Task PostgresRequiresCategoryAndGroupNames(bool isGroup, string? name)
    {
        var household = await _households.CreateAsync("Household A", "Person A");
        var category = await _classification.CreateCategoryAsync(household.Id, "Category A", FinancialType.Asset);
        await using var context = _database.CreateDbContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            if (isGroup)
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "ItemGroups" ("Id", "HouseholdId", "CategoryId", "Name")
                    VALUES ({Guid.NewGuid()}, {household.Id}, {category.Id}, {name})
                    """);
            }
            else
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Categories" ("Id", "HouseholdId", "Name", "FinancialType")
                    VALUES ({Guid.NewGuid()}, {household.Id}, {name}, {1})
                    """);
            }
        });

        Assert.Equal(name is null ? PostgresErrorCodes.NotNullViolation : PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(1, await context.Categories.CountAsync());
        Assert.Equal(0, await context.ItemGroups.CountAsync());
    }

    [Fact]
    public async Task RenamingPreservesIdentityAndRelationships()
    {
        var household = await _households.CreateAsync("Household A", "Person A");
        var category = await _classification.CreateCategoryAsync(household.Id, "Category A", FinancialType.Debt);
        var group = await _classification.CreateGroupAsync(household.Id, category.Id, "Group A");

        await using (var context = _database.CreateDbContext())
        {
            var saved = await context.ItemGroups.Include(item => item.Category).SingleAsync();
            saved.Category.Rename(" Renamed category ");
            saved.Rename(" Renamed group ");
            await context.SaveChangesAsync();
        }

        await using var verification = _database.CreateDbContext();
        var renamed = await verification.ItemGroups.Include(item => item.Category).SingleAsync();
        Assert.Equal(group.Id, renamed.Id);
        Assert.Equal(category.Id, renamed.CategoryId);
        Assert.Equal(household.Id, renamed.HouseholdId);
        Assert.Equal("Renamed category", renamed.Category.Name);
        Assert.Equal("Renamed group", renamed.Name);
        Assert.Equal(FinancialType.Debt, renamed.Category.FinancialType);
    }

    [Fact]
    public async Task AReferencedCategoryCannotBeDeleted()
    {
        var household = await _households.CreateAsync("Household A", "Person A");
        var category = await _classification.CreateCategoryAsync(household.Id, "Category A", FinancialType.Asset);
        await _classification.CreateGroupAsync(household.Id, category.Id, "Group A");

        await using (var context = _database.CreateDbContext())
        {
            context.Categories.Remove(await context.Categories.SingleAsync());
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.RestrictViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
        }

        await using var verification = _database.CreateDbContext();
        Assert.Equal(1, await verification.Categories.CountAsync());
        Assert.Equal(1, await verification.ItemGroups.CountAsync());
    }

    [Fact]
    public async Task AnUnusedCategoryCanBeDeletedWithoutDeletingItsHousehold()
    {
        var household = await _households.CreateAsync("Household A", "Person A");
        await _classification.CreateCategoryAsync(household.Id, "Category A", FinancialType.Asset);
        await using var context = _database.CreateDbContext();

        context.Categories.Remove(await context.Categories.SingleAsync());
        await context.SaveChangesAsync();

        Assert.Equal(0, await context.Categories.CountAsync());
        Assert.Equal(1, await context.Households.CountAsync());
        Assert.Equal(1, await context.People.CountAsync());
    }

}
