using Hearthcast.Data.Households;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Hearthcast.Data.Tests.Spending;

public sealed class SpendingAdditionalCostTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase database = new();
    private SpendingRecordService Records => new(database);
    private SpendingSetupService Setup => new(database);
    private SpendingCategorySummaryService Summary => new(database);
    private static readonly DateOnly Day = new(2026, 11, 19);
    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();
    private sealed record Data(Guid Home, Merchant Shop, ExpenseCategory Pets, ExpenseCategory Delivery, SpendingItem Item)
    {
        internal SpendingLineValues Product => new(Pets.Id, 45.98m, Item.Id, 2m, 22.99m);
        internal SpendingLineValues Cost => new(Pets.Id, 3.99m, Description: "Delivery", IsAdditionalCost: true);
        internal SpendingRecordValues Values => new(Shop.Id, Day, "GBP", [Product, Cost], ReceiptTotal: 49.97m);
    }
    private async Task<Data> Seed()
    {
        var home = await new HouseholdService(database).CreateAsync("Home", "Alex");
        var shop = await Setup.CreateMerchantAsync(home.Id, "VetUK");
        var pets = await Setup.CreateCategoryAsync(home.Id, "Pet Consumables");
        var delivery = await Setup.CreateCategoryAsync(home.Id, "Delivery");
        var item = await Setup.CreateItemAsync(home.Id, pets.Id, "FLEA", "Flea treatment", "pack", "Pack");
        return new(home.Id, shop, pets, delivery, item);
    }

    [Fact]
    public async Task VetUkChargeReconcilesWithoutChangingProductPricesAndSurvivesReadsAndCorrections()
    {
        var d = await Seed(); var original = (await Records.CreateAsync(d.Home, Guid.NewGuid(), d.Values, [new(1, 22.99m)])).Record;
        Assert.Equal(45.98m, original.PurchaseAmount); Assert.Equal(3.99m, original.AdditionalCostAmount);
        Assert.Equal(49.97m, original.GrossAmount); Assert.Equal(49.97m, original.TotalAmount); Assert.Equal("Reconciled", original.ReconciliationStatus);
        Assert.Equal(22.99m, original.Lines[0].UnitPrice); Assert.False(original.Lines[0].IsAdditionalCost); Assert.True(original.Lines[1].IsAdditionalCost);
        Assert.True((await Records.GetAsync(d.Home, original.Id)).Lines[1].IsAdditionalCost);
        Assert.True((await Records.ListPageAsync(d.Home)).Records.Single().Lines[1].IsAdditionalCost);
        var report = await Summary.GetAsync(d.Home, Day, Day); Assert.Equal(49.97m, report.Categories.Single().NetAmount); Assert.Empty(report.Warnings);
        var price = (await Setup.ListUsualPricesAsync(d.Home, d.Item.Id)).Single();
        var last = await Records.FindLastPaidAsync(d.Home, d.Item.Id, d.Shop.Id, "GBP", Day);
        Assert.Equal(22.99m, last!.UnitPrice);
        var updated = await Records.CorrectLineAsync(d.Home, original.Id, original.Revision, original.Lines[1].Id, 4.99m, null, null, PromotionStatus.Unknown, "Courier");
        Assert.True(updated.Lines[1].IsAdditionalCost); Assert.Equal(45.98m, updated.PurchaseAmount); Assert.Equal(50.97m, updated.TotalAmount);
        Assert.Equal(original.Lines[0].Id, updated.Lines[0].Id);
        Assert.Equal(price.Revision, (await Setup.GetUsualPriceAsync(d.Home, price.Id)).Revision);
        Assert.Equal(original.Lines[0].Id, (await Setup.GetUsualPriceAsync(d.Home, price.Id)).SourceSpendingLineId);
        var replay = await Records.CreateAsync(d.Home, original.Id, d.Values, [new(1, 22.99m)]);
        Assert.False(replay.WasCreated); Assert.Equal(updated.Revision, replay.Record.Revision);
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Records.CreateAsync(d.Home, original.Id,
            d.Values with { Lines = [d.Product, d.Cost with { IsAdditionalCost = false }] }, [new(1, 22.99m)]));
    }

    [Fact]
    public async Task CategoryChargesDiscountsRefundsAndVoidsCountExactlyOnce()
    {
        var d = await Seed(); var values = d.Values with { Lines = [d.Product, d.Cost with { ExpenseCategoryId = d.Delivery.Id }],
            Discounts = [new(d.Pets.Id, 2m), new(d.Delivery.Id, 1m)], ReceiptTotal = 46.97m };
        var paid = (await Records.CreateAsync(d.Home, Guid.NewGuid(), values)).Record;
        Assert.Equal(46.97m, paid.TotalAmount); Assert.Equal(49.97m, paid.GrossAmount);
        var report = await Summary.GetAsync(d.Home, Day, Day);
        Assert.Equal(43.98m, report.Categories.Single(c => c.ExpenseCategoryId == d.Pets.Id).NetAmount);
        Assert.Equal(2.99m, report.Categories.Single(c => c.ExpenseCategoryId == d.Delivery.Id).NetAmount);
        Assert.Empty(report.Warnings);
        var refund = (await Records.CreateAsync(d.Home, Guid.NewGuid(), values with { Kind = SpendingRecordKind.Refund, OriginalPaymentId = paid.Id, SpendingDate = Day.AddDays(1) })).Record;
        Assert.Equal(-46.97m, refund.CountedAmount);
        Assert.All((await Summary.GetAsync(d.Home, Day, Day.AddDays(1))).Categories, c => Assert.Equal(0m, c.NetAmount));
        await Records.VoidAsync(d.Home, refund.Id, refund.Revision);
        Assert.Equal(46.97m, (await Summary.GetAsync(d.Home, Day, Day.AddDays(1))).Categories.Sum(c => c.NetAmount));
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(0.001)]
    public async Task InvalidChargeCorrectionIsAtomic(decimal amount)
    {
        var d = await Seed(); var original = (await Records.CreateAsync(d.Home, Guid.NewGuid(), d.Values)).Record;
        await Assert.ThrowsAsync<InvalidValueException>(() => Records.CorrectLineAsync(d.Home, original.Id, original.Revision, original.Lines[1].Id, amount, null, null, PromotionStatus.Unknown, null));
        Assert.Equal(original.Revision, (await Records.GetAsync(d.Home, original.Id)).Revision);
        Assert.Equal(3.99m, (await Records.GetAsync(d.Home, original.Id)).AdditionalCostAmount);
    }

    [Fact]
    public async Task ChargesRejectProductUnitsAndMissingDescriptionsAndReplacementRetainsFlag()
    {
        var d = await Seed();
        foreach (var invalid in new[] { d.Cost with { SpendingItemId = d.Item.Id }, d.Cost with { Description = " " },
            d.Cost with { Quantity = 1m, UnitPrice = 3.99m, UnitKey = "each", UnitLabel = "Each" } })
            await Assert.ThrowsAsync<InvalidValueException>(() => Records.CreateAsync(d.Home, Guid.NewGuid(), d.Values with { Lines = [d.Product, invalid] }));
        var original = (await Records.CreateAsync(d.Home, Guid.NewGuid(), d.Values)).Record;
        var changed = await Records.ReplaceAsync(d.Home, original.Id, original.Revision, d.Shop.Id, Day, d.Values.Lines, 49.97m, null, "Corrected header");
        Assert.True(changed.Lines[1].IsAdditionalCost); Assert.Equal(49.97m, changed.TotalAmount);
        await Assert.ThrowsAsync<SpendingRecordConflictException>(() => Records.CorrectLineAsync(d.Home, original.Id, original.Revision, changed.Lines[1].Id, 5m, null, null, PromotionStatus.Unknown, null));
    }

    [Fact]
    public async Task MigrationDefaultsOldLinesToNormalWithoutInferringFromDescriptionOrChangingCreateIdentity()
    {
        var d = await Seed(); var values = d.Values with { Lines = [d.Product, d.Cost with { IsAdditionalCost = false }] };
        var original = (await Records.CreateAsync(d.Home, Guid.NewGuid(), values)).Record;
        await using (var context = database.CreateDbContext()) {
            var migrator = context.GetService<IMigrator>(); await migrator.MigrateAsync("20261119185319_AddReceiptDiscounts"); await migrator.MigrateAsync();
            Assert.False(context.Database.HasPendingModelChanges());
        }
        var replay = await Records.CreateAsync(d.Home, original.Id, values); Assert.False(replay.WasCreated);
        Assert.Equal(original.Revision, replay.Record.Revision); Assert.All(replay.Record.Lines, l => Assert.False(l.IsAdditionalCost));
        Assert.Equal(49.97m, replay.Record.PurchaseAmount); Assert.Equal(0, replay.Record.AdditionalCostAmount);
    }

    [Theory]
    [InlineData("UPDATE \"SpendingLines\" SET \"NetAmount\" = 0 WHERE \"IsAdditionalCost\"")]
    [InlineData("UPDATE \"SpendingLines\" SET \"Description\" = NULL WHERE \"IsAdditionalCost\"")]
    [InlineData("UPDATE \"SpendingLines\" SET \"IsAdditionalCost\" = TRUE WHERE \"SpendingItemId\" IS NOT NULL")]
    [InlineData("UPDATE \"SpendingLines\" SET \"UnitKey\" = 'each', \"UnitLabel\" = 'Each', \"Quantity\" = 1, \"UnitPrice\" = 3.99 WHERE \"IsAdditionalCost\"")]
    public async Task DatabaseEnforcesChargeShape(string sql)
    {
        var d = await Seed(); await Records.CreateAsync(d.Home, Guid.NewGuid(), d.Values);
        await using var context = database.CreateDbContext(); await Assert.ThrowsAsync<Npgsql.PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql));
    }
}
