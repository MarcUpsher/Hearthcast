using Hearthcast.Data.Classification;
using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Hearthcast.Data.Tests.Spending;

public sealed class SpendingSetupPersistenceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatabaseRejectsOverlengthIdentityStrings(bool unit)
    {
        var d = await SeedAsync();
        await using var context = _database.CreateDbContext();
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            if (unit)
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingItems" SET "UnitKey" = {SpendingSetupTestText.Key(33)} WHERE "Id" = {d.Item.Id}""");
            else
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingItems" SET "Code" = {SpendingSetupTestText.Key(65).ToUpperInvariant()} WHERE "Id" = {d.Item.Id}""");
        });
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, error.SqlState);
    }

    [Fact]
    public async Task DatabaseRejectsSurroundingOrOnlyWhitespaceForEveryDescriptiveField()
    {
        var d = await SeedAsync();
        await using var context = _database.CreateDbContext();
        var fields = new[]
        {
            ("ExpenseCategories", "Name", d.Category.Id), ("Merchants", "Name", d.Merchant.Id),
            ("SpendingItems", "Name", d.Item.Id), ("SpendingItems", "UnitLabel", d.Item.Id),
            ("SpendingItems", "Notes", d.Item.Id), ("SpendingItemMerchantPrices", "UnitLabel", d.Price.Id)
        };
        foreach (var (table, column, id) in fields)
        foreach (var c in SpendingSetupRules.TrimCharacters)
        foreach (var value in new[] { $"{c}Text", $"Text{c}", c.ToString() })
        {
            // Identifiers are from the fixed list above; values remain SQL parameters.
            var sql = System.Runtime.CompilerServices.FormattableStringFactory.Create(
                $"UPDATE \"{table}\" SET \"{column}\" = {{0}} WHERE \"Id\" = {{1}}", value, id);
            var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(sql));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
    }

    [Fact]
    public async Task HardeningMigrationPreservesExistingSetupAndPriceRevision()
    {
        var d = await SeedAsync();
        await using var context = _database.CreateDbContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261117105049_AddSpendingSetup");
        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();
        var item = await context.SpendingItems.SingleAsync();
        var price = await context.SpendingItemMerchantPrices.SingleAsync();
        Assert.Equal(d.Item.Code, item.Code);
        Assert.Equal(d.Item.UnitKey, item.UnitKey);
        Assert.Equal(d.Price.Revision, price.Revision);
        Assert.Equal(d.Price.UsualUnitPrice, price.UsualUnitPrice);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task HardeningMigrationRefusesToTruncateAnExistingIdentity()
    {
        var d = await SeedAsync();
        await using var context = _database.CreateDbContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261117105049_AddSpendingSetup");
        var overlong = SpendingSetupTestText.Key(65).ToUpperInvariant();
        await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingItems" SET "Code" = {overlong} WHERE "Id" = {d.Item.Id}""");
        var before = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
        Assert.Contains("Cannot tighten spending identity limits", error.MessageText);
        Assert.Equal(before, await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(overlong, await context.Database.SqlQuery<string>($"""SELECT "Code" AS "Value" FROM "SpendingItems" WHERE "Id" = {d.Item.Id}""").SingleAsync());
        await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingItems" SET "Code" = {d.Item.Code} WHERE "Id" = {d.Item.Id}""");
        await migrator.MigrateAsync();
    }

    private async Task<(Household Home, ExpenseCategory Category, Merchant Merchant, SpendingItem Item, SpendingItemMerchantPrice Price)> SeedAsync()
    {
        var home = Household.Create("Home", "Alex");
        var category = new ExpenseCategory(home, "Food");
        var merchant = new Merchant(home, "Shop");
        var item = new SpendingItem(category, " eggs-6 ", "Eggs", " PACK-6 ", "Six eggs", "Notes");
        var price = new SpendingItemMerchantPrice(item, merchant, " gbp ", 3.123456m);
        await using var context = _database.CreateDbContext();
        context.AddRange(home, category, merchant, item, price);
        await context.SaveChangesAsync();
        return (home, category, merchant, item, price);
    }

    [Fact]
    public async Task SetupRoundTripsAndRetirementDoesNotDeleteRelationshipsOrPrices()
    {
        var d = await SeedAsync();
        await using (var context = _database.CreateDbContext())
        {
            var category = await context.ExpenseCategories.SingleAsync();
            var merchant = await context.Merchants.SingleAsync();
            var item = await context.SpendingItems.SingleAsync();
            category.Rename("Groceries");
            category.SetRetired(true);
            merchant.Rename("Shop renamed");
            merchant.SetRetired(true);
            item.Rename("Large eggs");
            item.SetRetired(true);
            item.SetNotes(null);
            await context.SaveChangesAsync();
        }
        await using var read = _database.CreateDbContext();
        var saved = await read.SpendingItems.SingleAsync();
        var price = await read.SpendingItemMerchantPrices.SingleAsync();
        Assert.Equal(d.Item.Id, saved.Id);
        Assert.Equal(d.Home.Id, saved.HouseholdId);
        Assert.Equal(d.Category.Id, saved.ExpenseCategoryId);
        Assert.Equal("EGGS-6", saved.Code);
        Assert.Equal("pack-6", saved.UnitKey);
        Assert.True(saved.IsRetired);
        Assert.Null(saved.Notes);
        Assert.Equal(d.Merchant.Id, price.MerchantId);
        Assert.Equal("GBP", price.CurrencyCode);
        Assert.Equal(3.123456m, price.UsualUnitPrice);
        Assert.Equal(d.Price.Revision, price.Revision);
        Assert.True((await read.ExpenseCategories.SingleAsync()).IsRetired);
        Assert.True((await read.Merchants.SingleAsync()).IsRetired);
    }

    [Fact]
    public async Task ItemCodesRemainReservedAfterRetirementButCanBeReusedInAnotherHousehold()
    {
        var d = await SeedAsync();
        await using (var context = _database.CreateDbContext())
        {
            (await context.SpendingItems.SingleAsync()).SetRetired(true);
            await context.SaveChangesAsync();
            context.Add(new SpendingItem(d.Category, "EgGs-6", "Other", "each", "Each"));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        await SeedAsync(); // Same normalized code in another household is valid.
        await using var read = _database.CreateDbContext();
        Assert.Equal(2, await read.SpendingItems.CountAsync());
    }

    [Fact]
    public async Task UsualPriceIsUniquePerItemMerchantCurrencyAndUnit()
    {
        var d = await SeedAsync();
        await using (var context = _database.CreateDbContext())
        {
            context.Add(new SpendingItemMerchantPrice(d.Item, d.Merchant, "GBP", 2));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }
        await using var add = _database.CreateDbContext();
        var otherShop = new Merchant(d.Home, "Other shop");
        var otherPack = new SpendingItem(d.Category, "EGGS-12", "Eggs", "pack-12", "Twelve eggs");
        add.AddRange(otherShop, otherPack,
            new SpendingItemMerchantPrice(d.Item, d.Merchant, "ZAR", 55),
            new SpendingItemMerchantPrice(d.Item, otherShop, "GBP", 2),
            new SpendingItemMerchantPrice(otherPack, d.Merchant, "GBP", 5));
        await add.SaveChangesAsync();
        Assert.Equal(4, await add.SpendingItemMerchantPrices.CountAsync());
    }

    [Fact]
    public async Task StaleUsualPriceEditIsRejectedAndTheWinningPriceSurvives()
    {
        await SeedAsync();
        await using var first = _database.CreateDbContext();
        await using var second = _database.CreateDbContext();
        var winner = await first.SpendingItemMerchantPrices.SingleAsync();
        var stale = await second.SpendingItemMerchantPrices.SingleAsync();
        winner.SetUsualUnitPrice(3.20m);
        await first.SaveChangesAsync();
        stale.SetUsualUnitPrice(2m);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var read = _database.CreateDbContext();
        Assert.Equal(3.20m, (await read.SpendingItemMerchantPrices.SingleAsync()).UsualUnitPrice);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1000000000")]
    [InlineData("0.0000001")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public async Task DatabaseRejectsInvalidPricesRatherThanRoundingThem(string price)
    {
        var d = await SeedAsync();
        await using var context = _database.CreateDbContext();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "SpendingItemMerchantPrices" SET "UsualUnitPrice" = CAST({price} AS numeric) WHERE "Id" = {d.Price.Id}
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal(3.123456m, (await context.SpendingItemMerchantPrices.SingleAsync()).UsualUnitPrice);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.000001")]
    [InlineData("999999999.999999")]
    public async Task BoundaryPricesAreStoredExactly(string text)
    {
        var d = await SeedAsync();
        var amount = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        await using (var context = _database.CreateDbContext())
        {
            var price = await context.SpendingItemMerchantPrices.SingleAsync();
            price.SetUsualUnitPrice(amount);
            await context.SaveChangesAsync();
        }
        await using var read = _database.CreateDbContext();
        Assert.Equal(amount, (await read.SpendingItemMerchantPrices.SingleAsync()).UsualUnitPrice);
        Assert.Equal(amount, await read.Database.SqlQuery<decimal>($"""
            SELECT SUM("UsualUnitPrice") AS "Value" FROM "SpendingItemMerchantPrices" WHERE "HouseholdId" = {d.Home.Id}
            """).SingleAsync());
    }

    [Theory]
    [InlineData("category")]
    [InlineData("item")]
    [InlineData("merchant")]
    public async Task DatabaseRejectsCrossHouseholdReferences(string association)
    {
        var a = await SeedAsync();
        var b = await SeedAsync();
        await using var context = _database.CreateDbContext();
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            if (association == "category")
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "SpendingItems" SET "ExpenseCategoryId" = {b.Category.Id} WHERE "Id" = {a.Item.Id}
                    """);
            else if (association == "item")
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "SpendingItemMerchantPrices" SET "SpendingItemId" = {b.Item.Id} WHERE "Id" = {a.Price.Id}
                    """);
            else
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "SpendingItemMerchantPrices" SET "MerchantId" = {b.Merchant.Id} WHERE "Id" = {a.Price.Id}
                    """);
        });
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Fact]
    public async Task DatabaseRejectsAUsualPriceForTheWrongUnitAndProtectsAReferencedUnitKey()
    {
        var d = await SeedAsync();
        await using var context = _database.CreateDbContext();
        var wrongUnit = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "SpendingItemMerchantPrices" SET "UnitKey" = {"pack-12"} WHERE "Id" = {d.Price.Id}
            """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, wrongUnit.SqlState);
        var changedMeaning = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "SpendingItems" SET "UnitKey" = {"pack-12"} WHERE "Id" = {d.Item.Id}
            """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, changedMeaning.SqlState);
    }

    [Theory]
    [InlineData("category")]
    [InlineData("merchant")]
    [InlineData("item")]
    [InlineData("household")]
    public async Task DatabaseRestrictsDeletionOfReferencedIdentities(string identity)
    {
        var d = await SeedAsync();
        await using var context = _database.CreateDbContext();
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            if (identity == "category")
                await context.Database.ExecuteSqlInterpolatedAsync($"""DELETE FROM "ExpenseCategories" WHERE "Id" = {d.Category.Id}""");
            else if (identity == "merchant")
                await context.Database.ExecuteSqlInterpolatedAsync($"""DELETE FROM "Merchants" WHERE "Id" = {d.Merchant.Id}""");
            else if (identity == "item")
                await context.Database.ExecuteSqlInterpolatedAsync($"""DELETE FROM "SpendingItems" WHERE "Id" = {d.Item.Id}""");
            else
                await context.Database.ExecuteSqlInterpolatedAsync($"""DELETE FROM "Households" WHERE "Id" = {d.Home.Id}""");
        });
        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
        Assert.Equal(1, await context.SpendingItemMerchantPrices.CountAsync());
    }

    [Fact]
    public async Task MigrationPreservesExistingFinancialHistoryAndCanBeRolledBackAndReapplied()
    {
        var home = await new HouseholdService(_database).CreateAsync("Home", "Alex");
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(home.Id, "Savings", FinancialType.Asset);
        var group = await classification.CreateGroupAsync(home.Id, category.Id, "Cash");
        var item = await new TrackedItemService(_database).CreateAsync(home.Id, group.Id, home.People.Single().Id,
            "Account", "GBP", "GB", true);
        var snapshots = new SnapshotService(_database);
        var snapshot = await snapshots.CreateAsync(home.Id, new DateOnly(2026, 1, 1), [new(item.Id, 123.456789m)]);
        await using var context = _database.CreateDbContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261108171917_AddProjectionExpenseTargets");
        await migrator.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync());
        var preserved = await snapshots.GetAsync(home.Id, snapshot.Id);
        Assert.Equal(snapshot.Revision, preserved.Revision);
        Assert.Equal(123.456789m, Assert.Single(preserved.ItemSnapshots).Amount);
        Assert.Equal("Account", Assert.Single(preserved.ItemSnapshots).ItemName);
        await SeedAsync();
        Assert.Equal(1, await context.SpendingItems.CountAsync());
        Assert.Equal(1, await context.Snapshots.CountAsync());
    }

    [Theory]
    [InlineData("category", "")]
    [InlineData("category", "\t\n")]
    [InlineData("merchant", "  ")]
    [InlineData("name", "\t")]
    [InlineData("code", "eggs-6")]
    [InlineData("code", " EGGS-6 ")]
    [InlineData("unit", "PACK-6")]
    [InlineData("label", "")]
    [InlineData("notes", "  ")]
    [InlineData("currency", "gbp")]
    [InlineData("currency", "GB1")]
    [InlineData("price-label", " ")]
    public async Task DatabaseRejectsBlankOrNoncanonicalSetupText(string field, string value)
    {
        var d = await SeedAsync();
        await using var context = _database.CreateDbContext();
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            switch (field)
            {
                case "category":
                    await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "ExpenseCategories" SET "Name" = {value} WHERE "Id" = {d.Category.Id}""");
                    break;
                case "merchant":
                    await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "Merchants" SET "Name" = {value} WHERE "Id" = {d.Merchant.Id}""");
                    break;
                case "name":
                    await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingItems" SET "Name" = {value} WHERE "Id" = {d.Item.Id}""");
                    break;
                case "code":
                    await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingItems" SET "Code" = {value} WHERE "Id" = {d.Item.Id}""");
                    break;
                case "unit":
                    await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingItems" SET "UnitKey" = {value} WHERE "Id" = {d.Item.Id}""");
                    break;
                case "label":
                    await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingItems" SET "UnitLabel" = {value} WHERE "Id" = {d.Item.Id}""");
                    break;
                case "notes":
                    await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingItems" SET "Notes" = {value} WHERE "Id" = {d.Item.Id}""");
                    break;
                case "currency":
                    await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingItemMerchantPrices" SET "CurrencyCode" = {value} WHERE "Id" = {d.Price.Id}""");
                    break;
                case "price-label":
                    await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingItemMerchantPrices" SET "UnitLabel" = {value} WHERE "Id" = {d.Price.Id}""");
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(field));
            }
        });
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatabaseRequiresExistingHouseholdsForCategoriesAndMerchants(bool merchant)
    {
        await using var context = _database.CreateDbContext();
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            if (merchant)
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Merchants" ("Id", "HouseholdId", "Name", "IsRetired")
                    VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {"Shop"}, {false})
                    """);
            else
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "ExpenseCategories" ("Id", "HouseholdId", "Name", "IsRetired")
                    VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {"Food"}, {false})
                    """);
        });
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }
}
