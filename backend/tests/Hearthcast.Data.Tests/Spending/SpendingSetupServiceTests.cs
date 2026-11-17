using Hearthcast.Data.Households;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hearthcast.Data.Tests.Spending;

public sealed class SpendingSetupServiceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private SpendingSetupService Service => new(_database);
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Theory]
    [InlineData(65, 1)]
    [InlineData(1, 33)]
    [InlineData(4096, 1)]
    [InlineData(1, 4096)]
    public async Task ServiceRejectsOverlengthKeysWithoutWritingPartialSetup(int codeLength, int unitLength)
    {
        var d = await SeedAsync();
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateItemAsync(d.Home, d.Category.Id,
            SpendingSetupTestText.Key(codeLength), "New", SpendingSetupTestText.Key(unitLength), "Each"));
        Assert.Single(await Service.ListItemsAsync(d.Home));
        Assert.Single(await Service.ListUsualPricesAsync(d.Home, d.Item.Id));
    }

    [Fact]
    public async Task ServiceRoundTripsMaximumKeysAndTheirPriceRelationship()
    {
        var d = await SeedAsync();
        var item = await Service.CreateItemAsync(d.Home, d.Category.Id, SpendingSetupTestText.Key(64), "New",
            SpendingSetupTestText.Key(32), "Each");
        var price = await Service.CreateUsualPriceAsync(d.Home, item.Id, d.Merchant.Id, "GBP", 1);
        Assert.Equal(64, (await Service.GetItemAsync(d.Home, item.Id)).Code.Length);
        Assert.Equal(item.UnitKey, (await Service.GetUsualPriceAsync(d.Home, price.Id)).UnitKey);
        Assert.Equal(price.Id, (await Service.FindUsualPriceAsync(d.Home, item.Id, d.Merchant.Id, "GBP"))!.Id);
    }

    private async Task<(Guid Home, ExpenseCategory Category, Merchant Merchant, SpendingItem Item, SpendingItemMerchantPrice Price)> SeedAsync()
    {
        var home = await new HouseholdService(_database).CreateAsync("Home", "Alex");
        var category = await Service.CreateCategoryAsync(home.Id, " Food ");
        var merchant = await Service.CreateMerchantAsync(home.Id, " Shop ");
        var item = await Service.CreateItemAsync(home.Id, category.Id, " eggs-6 ", " Eggs ", " PACK-6 ", "Six eggs", " Notes ");
        var price = await Service.CreateUsualPriceAsync(home.Id, item.Id, merchant.Id, " gbp ", 3.123456m);
        return (home.Id, category, merchant, item, price);
    }

    [Fact]
    public async Task ServiceCreatesEditsAndReadsSetupWithoutChangingCodeUnitOrRememberedPrice()
    {
        var d = await SeedAsync();
        var newCategory = await Service.CreateCategoryAsync(d.Home, "Groceries");
        await Service.RenameCategoryAsync(d.Home, d.Category.Id, " Other food ");
        await Service.RenameMerchantAsync(d.Home, d.Merchant.Id, " Shop renamed ");
        var updated = await Service.UpdateItemAsync(d.Home, d.Item.Id, newCategory.Id, " Large eggs ", " Box of six ", null);
        Assert.Equal("Large eggs", updated.Name);
        Assert.Equal("EGGS-6", updated.Code);
        Assert.Equal("pack-6", updated.UnitKey);
        Assert.Equal(newCategory.Id, updated.ExpenseCategoryId);
        Assert.Equal("Box of six", updated.UnitLabel);
        Assert.Null(updated.Notes);
        Assert.Equal("Other food", (await Service.GetCategoryAsync(d.Home, d.Category.Id)).Name);
        Assert.Equal("Shop renamed", (await Service.GetMerchantAsync(d.Home, d.Merchant.Id)).Name);
        Assert.Equal(updated.Id, (await Service.GetItemAsync(d.Home, d.Item.Id)).Id);
        var price = await Service.GetUsualPriceAsync(d.Home, d.Price.Id);
        Assert.Equal(3.123456m, price.UsualUnitPrice);
        Assert.Equal("Six eggs", price.UnitLabel);
        Assert.Equal(d.Price.Revision, price.Revision);
    }

    [Fact]
    public async Task InventoryIsScopedOrderedAndSupportsReversibleRetirementWithoutCascades()
    {
        var d = await SeedAsync();
        var other = await SeedAsync();
        var category = await Service.CreateCategoryAsync(d.Home, "A category");
        var merchant = await Service.CreateMerchantAsync(d.Home, "A shop");
        var item = await Service.CreateItemAsync(d.Home, category.Id, "A", "A product", "each", "Each");
        await Service.SetCategoryRetiredAsync(d.Home, category.Id, true);
        await Service.SetMerchantRetiredAsync(d.Home, merchant.Id, true);
        // Retiring a parent does not retire or hide its active items.
        Assert.False((await Service.GetItemAsync(d.Home, item.Id)).IsRetired);
        Assert.Contains(await Service.ListItemsAsync(d.Home, false), i => i.Id == item.Id);
        await Service.SetItemRetiredAsync(d.Home, item.Id, true);
        Assert.Equal(new[] { category.Id, d.Category.Id }, (await Service.ListCategoriesAsync(d.Home)).Select(c => c.Id));
        Assert.Equal(new[] { merchant.Id, d.Merchant.Id }, (await Service.ListMerchantsAsync(d.Home)).Select(m => m.Id));
        Assert.Equal(new[] { item.Id, d.Item.Id }, (await Service.ListItemsAsync(d.Home)).Select(i => i.Id));
        Assert.Equal(d.Category.Id, Assert.Single(await Service.ListCategoriesAsync(d.Home, false)).Id);
        Assert.Equal(d.Merchant.Id, Assert.Single(await Service.ListMerchantsAsync(d.Home, false)).Id);
        Assert.Equal(d.Item.Id, Assert.Single(await Service.ListItemsAsync(d.Home, false)).Id);
        await Service.SetCategoryRetiredAsync(d.Home, category.Id, false);
        await Service.SetMerchantRetiredAsync(d.Home, merchant.Id, false);
        await Service.SetItemRetiredAsync(d.Home, item.Id, false);
        Assert.Equal(2, (await Service.ListCategoriesAsync(d.Home, false)).Count);
        Assert.Equal(2, (await Service.ListMerchantsAsync(d.Home, false)).Count);
        Assert.Equal(2, (await Service.ListItemsAsync(d.Home, false)).Count);
        Assert.Equal(d.Price.Id, Assert.Single(await Service.ListUsualPricesAsync(d.Home, d.Item.Id)).Id);
        Assert.Equal(other.Price.Id, Assert.Single(await Service.ListUsualPricesAsync(other.Home, other.Item.Id)).Id);
    }

    [Fact]
    public async Task EmptyInventoriesAreValidButMissingHouseholdsAreNot()
    {
        var home = await new HouseholdService(_database).CreateAsync("Home", "Alex");
        Assert.Empty(await Service.ListCategoriesAsync(home.Id));
        Assert.Empty(await Service.ListMerchantsAsync(home.Id));
        Assert.Empty(await Service.ListItemsAsync(home.Id));
        var missing = Guid.NewGuid();
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.ListCategoriesAsync(missing));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.ListMerchantsAsync(missing));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.ListItemsAsync(missing));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CreateCategoryAsync(missing, "Food"));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CreateMerchantAsync(missing, "Shop"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAndForeignIdsCannotBeReadOrChangedThroughAnotherHousehold(bool foreign)
    {
        var d = await SeedAsync();
        var other = await SeedAsync();
        var categoryId = foreign ? other.Category.Id : Guid.NewGuid();
        var merchantId = foreign ? other.Merchant.Id : Guid.NewGuid();
        var itemId = foreign ? other.Item.Id : Guid.NewGuid();
        var priceId = foreign ? other.Price.Id : Guid.NewGuid();
        var service = Service;
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetCategoryAsync(d.Home, categoryId));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.RenameCategoryAsync(d.Home, categoryId, "Renamed"));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.SetCategoryRetiredAsync(d.Home, categoryId, true));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetMerchantAsync(d.Home, merchantId));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.RenameMerchantAsync(d.Home, merchantId, "Renamed"));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.SetMerchantRetiredAsync(d.Home, merchantId, true));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetItemAsync(d.Home, itemId));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.UpdateItemAsync(d.Home, itemId, d.Category.Id, "New", "Each", null));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.SetItemRetiredAsync(d.Home, itemId, true));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetUsualPriceAsync(d.Home, priceId));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.UpdateUsualPriceAsync(d.Home, priceId, other.Price.Revision, 2));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.ListUsualPricesAsync(d.Home, itemId));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.CreateItemAsync(d.Home, categoryId, "NEW", "New", "each", "Each"));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.UpdateItemAsync(d.Home, d.Item.Id, categoryId, "New", "Each", null));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.CreateUsualPriceAsync(d.Home, itemId, d.Merchant.Id, "GBP", 2));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.CreateUsualPriceAsync(d.Home, d.Item.Id, merchantId, "GBP", 2));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.FindUsualPriceAsync(d.Home, itemId, d.Merchant.Id, "GBP"));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.FindUsualPriceAsync(d.Home, d.Item.Id, merchantId, "GBP"));
        Assert.Equal("Eggs", (await service.GetItemAsync(d.Home, d.Item.Id)).Name);
        Assert.Equal("Eggs", (await service.GetItemAsync(other.Home, other.Item.Id)).Name);
        Assert.False((await service.GetCategoryAsync(other.Home, other.Category.Id)).IsRetired);
        Assert.False((await service.GetMerchantAsync(other.Home, other.Merchant.Id)).IsRetired);
        Assert.Equal(other.Price.Revision, (await service.GetUsualPriceAsync(other.Home, other.Price.Id)).Revision);
    }

    [Fact]
    public async Task RetiredSetupRemainsExplicitlyUsableAndCodesRemainReserved()
    {
        var d = await SeedAsync();
        await Service.SetCategoryRetiredAsync(d.Home, d.Category.Id, true);
        await Service.SetMerchantRetiredAsync(d.Home, d.Merchant.Id, true);
        await Service.SetItemRetiredAsync(d.Home, d.Item.Id, true);
        var historical = await Service.CreateItemAsync(d.Home, d.Category.Id, "OLD", "Historical", "each", "Each");
        await Service.UpdateItemAsync(d.Home, historical.Id, d.Category.Id, "Historical edited", "Each", null);
        var price = await Service.CreateUsualPriceAsync(d.Home, d.Item.Id, d.Merchant.Id, "ZAR", 20);
        Assert.Equal(price.Id, (await Service.FindUsualPriceAsync(d.Home, d.Item.Id, d.Merchant.Id, "ZAR"))!.Id);
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Service.CreateItemAsync(d.Home, d.Category.Id, "eGgS-6", "Duplicate", "each", "Each"));
        Assert.True((await Service.GetItemAsync(d.Home, d.Item.Id)).IsRetired);
    }

    [Fact]
    public async Task PriceLookupUsesExactItemMerchantCurrencyAndDoesNotGuessMissingPrices()
    {
        var d = await SeedAsync();
        var merchant = await Service.CreateMerchantAsync(d.Home, "Other shop");
        var item = await Service.CreateItemAsync(d.Home, d.Category.Id, "EGGS-12", "Eggs", "pack-12", "Twelve eggs");
        Assert.Null(await Service.FindUsualPriceAsync(d.Home, d.Item.Id, merchant.Id, "GBP"));
        Assert.Null(await Service.FindUsualPriceAsync(d.Home, d.Item.Id, d.Merchant.Id, "ZAR"));
        Assert.Null(await Service.FindUsualPriceAsync(d.Home, item.Id, d.Merchant.Id, "GBP"));
        Assert.Empty(await Service.ListUsualPricesAsync(d.Home, item.Id));
        await Service.CreateUsualPriceAsync(d.Home, d.Item.Id, d.Merchant.Id, "ZAR", 50);
        await Service.CreateUsualPriceAsync(d.Home, d.Item.Id, merchant.Id, "GBP", 2);
        await Service.CreateUsualPriceAsync(d.Home, item.Id, d.Merchant.Id, "GBP", 5);
        Assert.Equal(d.Price.Id, (await Service.FindUsualPriceAsync(d.Home, d.Item.Id, d.Merchant.Id, " gbp "))!.Id);
        var prices = await Service.ListUsualPricesAsync(d.Home, d.Item.Id);
        Assert.Equal(new[] { "GBP", "GBP", "ZAR" }, prices.Select(p => p.CurrencyCode));
        Assert.All(prices, p => Assert.Equal("pack-6", p.UnitKey));
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Service.CreateUsualPriceAsync(d.Home, d.Item.Id, d.Merchant.Id, "gBp", 1));
        Assert.Equal(d.Price.Revision, (await Service.GetUsualPriceAsync(d.Home, d.Price.Id)).Revision);
    }

    [Theory]
    [InlineData("label")]
    [InlineData("notes")]
    [InlineData("name")]
    public async Task InvalidItemUpdateRollsBackAllEarlierMetadataChanges(string field)
    {
        var d = await SeedAsync();
        var category = await Service.CreateCategoryAsync(d.Home, "Other");
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.UpdateItemAsync(d.Home, d.Item.Id, category.Id,
            field == "name" ? " " : "Changed", field == "label" ? " " : "Changed label",
            field == "notes" ? "bad\0notes" : "Changed notes"));
        var item = await Service.GetItemAsync(d.Home, d.Item.Id);
        Assert.Equal("Eggs", item.Name);
        Assert.Equal("Six eggs", item.UnitLabel);
        Assert.Equal("Notes", item.Notes);
        Assert.Equal(d.Category.Id, item.ExpenseCategoryId);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("unit")]
    [InlineData("name")]
    [InlineData("label")]
    [InlineData("notes")]
    public async Task InvalidItemCreateDoesNotLeavePartialSetup(string field)
    {
        var d = await SeedAsync();
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateItemAsync(d.Home, d.Category.Id,
            field == "code" ? "bad code" : "NEW", field == "name" ? " " : "New",
            field == "unit" ? "bad unit" : "each", field == "label" ? " " : "Each",
            field == "notes" ? "bad\0notes" : null));
        Assert.Single(await Service.ListItemsAsync(d.Home));
        Assert.Single(await Service.ListUsualPricesAsync(d.Home, d.Item.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" \t ")]
    [InlineData("bad\0name")]
    public async Task InvalidCategoryAndMerchantNamesDoNotCreateOrChangeRecords(string? name)
    {
        var d = await SeedAsync();
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateCategoryAsync(d.Home, name!));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateMerchantAsync(d.Home, name!));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.RenameCategoryAsync(d.Home, d.Category.Id, name!));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.RenameMerchantAsync(d.Home, d.Merchant.Id, name!));
        Assert.Equal("Food", Assert.Single(await Service.ListCategoriesAsync(d.Home)).Name);
        Assert.Equal("Shop", Assert.Single(await Service.ListMerchantsAsync(d.Home)).Name);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1000000000")]
    [InlineData("0.0000001")]
    public async Task InvalidPriceCreateAndUpdatePreserveTheSavedPriceAndRevision(string text)
    {
        var d = await SeedAsync();
        var amount = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateUsualPriceAsync(d.Home, d.Item.Id, d.Merchant.Id, "ZAR", amount));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.UpdateUsualPriceAsync(d.Home, d.Price.Id, d.Price.Revision, amount));
        var saved = await Service.GetUsualPriceAsync(d.Home, d.Price.Id);
        Assert.Equal(d.Price.UsualUnitPrice, saved.UsualUnitPrice);
        Assert.Equal(d.Price.Revision, saved.Revision);
        Assert.Single(await Service.ListUsualPricesAsync(d.Home, d.Item.Id));
    }

    [Theory]
    [InlineData("GB")]
    [InlineData("GB1")]
    public async Task InvalidCurrencyCannotBeCreatedOrUsedAsALookup(string currency)
    {
        var d = await SeedAsync();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Service.CreateUsualPriceAsync(d.Home, d.Item.Id, d.Merchant.Id, currency, 1));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Service.FindUsualPriceAsync(d.Home, d.Item.Id, d.Merchant.Id, currency));
        Assert.Single(await Service.ListUsualPricesAsync(d.Home, d.Item.Id));
    }

    [Fact]
    public async Task UsualPriceChangesAndExplicitReconfirmationAdvanceRevisionAndRejectStaleEdits()
    {
        var d = await SeedAsync();
        var first = await Service.UpdateUsualPriceAsync(d.Home, d.Price.Id, d.Price.Revision, 0);
        Assert.NotEqual(d.Price.Revision, first.Revision);
        Assert.Equal(0m, first.UsualUnitPrice);
        var second = await Service.UpdateUsualPriceAsync(d.Home, d.Price.Id, first.Revision, 0);
        Assert.NotEqual(first.Revision, second.Revision);
        await Assert.ThrowsAsync<SpendingPriceConflictException>(() => Service.UpdateUsualPriceAsync(d.Home, d.Price.Id, d.Price.Revision, 2));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.UpdateUsualPriceAsync(d.Home, d.Price.Id, Guid.Empty, 2));
        Assert.Equal(second.Revision, (await Service.GetUsualPriceAsync(d.Home, d.Price.Id)).Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentDuplicateCreatesReturnADomainConflictAndPersistOneRecord(bool price)
    {
        var d = await SeedAsync();
        var barrier = new ConcurrentSaveBarrier();
        var service = new SpendingSetupService(new InterceptingContextFactory(_database, barrier));
        async Task<Exception?> AttemptAsync() => await Record.ExceptionAsync(async () =>
        {
            if (price) await service.CreateUsualPriceAsync(d.Home, d.Item.Id, d.Merchant.Id, "ZAR", 50);
            else await service.CreateItemAsync(d.Home, d.Category.Id, "NEW", "New", "each", "Each");
        });
        var results = await Task.WhenAll(AttemptAsync(), AttemptAsync());
        Assert.Null(Assert.Single(results, e => e is null));
        Assert.IsType<BusinessRuleViolationException>(Assert.Single(results, e => e is not null));
        await using var context = _database.CreateDbContext();
        if (price) Assert.Equal(1, await context.SpendingItemMerchantPrices.CountAsync(p => p.CurrencyCode == "ZAR"));
        else Assert.Equal(1, await context.SpendingItems.CountAsync(i => i.Code == "NEW"));
    }

    [Fact]
    public async Task ConcurrentPriceUpdatesDoNotOverwriteTheWinnerOnSerializableRetry()
    {
        var d = await SeedAsync();
        var barrier = new ConcurrentSaveBarrier();
        var service = new SpendingSetupService(new InterceptingContextFactory(_database, barrier));
        var results = await Task.WhenAll(new[] { 2m, 4m }.Select(async amount =>
            (Amount: amount, Error: await Record.ExceptionAsync(() => service.UpdateUsualPriceAsync(d.Home, d.Price.Id, d.Price.Revision, amount)))));
        var winner = Assert.Single(results, r => r.Error is null);
        Assert.IsType<SpendingPriceConflictException>(Assert.Single(results, r => r.Error is not null).Error);
        var saved = await Service.GetUsualPriceAsync(d.Home, d.Price.Id);
        Assert.Equal(winner.Amount, saved.UsualUnitPrice);
        Assert.NotEqual(d.Price.Revision, saved.Revision);
    }

    [Fact]
    public async Task DatabaseSaveFailureRollsBackItemEdits()
    {
        var d = await SeedAsync();
        await using var context = _database.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION "FailSpendingItemUpdate"() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Synthetic update failure'; END; $$;
            CREATE TRIGGER "FailSpendingItemUpdate" BEFORE UPDATE ON "SpendingItems"
            FOR EACH ROW EXECUTE FUNCTION "FailSpendingItemUpdate"();
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => Service.UpdateItemAsync(d.Home, d.Item.Id, d.Category.Id, "Changed", "Changed label", null));
        var saved = await Service.GetItemAsync(d.Home, d.Item.Id);
        Assert.Equal("Eggs", saved.Name);
        Assert.Equal("Notes", saved.Notes);
        Assert.Equal("Six eggs", saved.UnitLabel);
    }

    [Fact]
    public async Task CancelledOperationsDoNotCreateSetupOrUpdatePrices()
    {
        var d = await SeedAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.CreateCategoryAsync(d.Home, "New", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.CreateItemAsync(d.Home, d.Category.Id,
            "NEW", "New", "each", "Each", cancellationToken: cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.UpdateUsualPriceAsync(d.Home, d.Price.Id, d.Price.Revision,
            2, cancellation.Token));
        Assert.Single(await Service.ListCategoriesAsync(d.Home));
        Assert.Single(await Service.ListItemsAsync(d.Home));
        Assert.Equal(d.Price.Revision, (await Service.GetUsualPriceAsync(d.Home, d.Price.Id)).Revision);
    }
}
