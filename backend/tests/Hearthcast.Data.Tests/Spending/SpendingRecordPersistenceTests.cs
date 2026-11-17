using Hearthcast.Data.Households;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Hearthcast.Data.Tests.Spending;

public sealed class SpendingRecordPersistenceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    private sealed record Data(Household Home, ExpenseCategory Category, Merchant Merchant,
        SpendingItem Item, SpendingRecord Record, SpendingItemMerchantPrice Price);

    private async Task<Data> SeedAsync(string currency = "GBP", bool itemised = true)
    {
        var home = Household.Create("Home", "Alex");
        var category = new ExpenseCategory(home, "Food");
        var merchant = new Merchant(home, "Shop");
        var item = new SpendingItem(category, "EGGS", "Eggs", "pack-6", "Six eggs");
        var record = new SpendingRecord(Guid.NewGuid(), merchant, new(2026, 11, 17), currency,
            [new(category, 3m, itemised ? item : null, itemised ? 1m : null, itemised ? 3m : null)], new string('a', 64),
            receiptTotal: 4m, externalReference: "Receipt", notes: "Notes");
        var price = new SpendingItemMerchantPrice(item, merchant, currency, 3m);
        await using var context = _database.CreateDbContext();
        context.AddRange(home, category, merchant, item, record, price);
        await context.SaveChangesAsync();
        return new(home, category, merchant, item, record, price);
    }

    [Fact]
    public async Task QuickAndItemisedRecordsRoundTripWithCopiedMetadataAndIndependentTotals()
    {
        var d = await SeedAsync();
        var quick = new SpendingRecord(Guid.NewGuid(), d.Merchant, d.Record.SpendingDate, "GBP",
            [new(d.Category, 7m, Description: "Coffee"), new(d.Category, 5.5m, Description: "Scones")], new string('b', 64));
        await using (var context = _database.CreateDbContext())
        {
            context.Add(quick);
            var item = await context.SpendingItems.SingleAsync();
            var category = await context.ExpenseCategories.SingleAsync();
            var merchant = await context.Merchants.SingleAsync();
            item.Rename("Renamed item"); item.SetRetired(true); item.SetUnitLabel("Different label");
            category.Rename("Renamed category"); merchant.Rename("Renamed shop");
            await context.SaveChangesAsync();
        }
        await using var read = _database.CreateDbContext();
        var saved = await read.SpendingRecords.Include(r => r.Lines).SingleAsync(r => r.Id == d.Record.Id);
        Assert.Equal("Shop", saved.MerchantName);
        Assert.Equal("Food", saved.Lines[0].ExpenseCategoryName);
        Assert.Equal("Eggs", saved.Lines[0].ItemName);
        Assert.Equal("Six eggs", saved.Lines[0].UnitLabel);
        Assert.Equal(3m, saved.TotalAmount);
        Assert.Equal("Unreconciled", saved.ReconciliationStatus);
        Assert.Equal(d.Record.Revision, saved.Revision);
        Assert.Equal(d.Record.CreationRequestHash, saved.CreationRequestHash);
        var split = await read.SpendingRecords.Include(r => r.Lines).SingleAsync(r => r.Id == quick.Id);
        Assert.Equal(12.5m, split.TotalAmount);
        Assert.All(split.Lines, l => Assert.Null(l.SpendingItemId));
        Assert.Equal(15.5m, await read.SpendingLines.SumAsync(l => l.NetAmount));
    }

    [Fact]
    public async Task ItemisingAQuickRecordReplacesItsLinesAndPreservesCreateIdentityAndReceiptTotal()
    {
        var d = await SeedAsync(itemised: false);
        await using (var context = _database.CreateDbContext())
        {
            var record = await context.SpendingRecords.Include(r => r.Lines).SingleAsync();
            var removed = record.ReplaceLines(record.Revision, [new(d.Category, 2m, d.Item, 1m, 2m), new(d.Category, 1m)]);
            context.SpendingLines.RemoveRange(removed);
            await context.SaveChangesAsync();
        }
        await using var read = _database.CreateDbContext();
        var saved = await read.SpendingRecords.Include(r => r.Lines).SingleAsync();
        Assert.Equal(2, await read.SpendingLines.CountAsync());
        Assert.Equal(3m, saved.TotalAmount);
        Assert.Equal(4m, saved.ReceiptTotal);
        Assert.Equal(d.Record.Id, saved.Id);
        Assert.Equal(d.Record.CreatedAtUtc, saved.CreatedAtUtc);
        Assert.Equal(d.Record.CreationRequestHash, saved.CreationRequestHash);
        Assert.NotEqual(d.Record.Revision, saved.Revision);
    }

    [Fact]
    public async Task RefundLinksSurviveVoidingTheOriginalAndCountOnlyRecordedMoney()
    {
        var d = await SeedAsync();
        var refund = new SpendingRecord(Guid.NewGuid(), d.Merchant, d.Record.SpendingDate, "GBP",
            [new(d.Category, 2m)], new string('b', 64), SpendingRecordKind.Refund, d.Record);
        await using (var context = _database.CreateDbContext())
        {
            context.Add(refund);
            var original = await context.SpendingRecords.Include(r => r.Lines).SingleAsync();
            original.Void(original.Revision);
            await context.SaveChangesAsync();
        }
        await using var read = _database.CreateDbContext();
        var records = await read.SpendingRecords.Include(r => r.Lines).ToArrayAsync();
        Assert.Equal(-2m, records.Sum(r => r.CountedAmount));
        Assert.Equal(d.Record.Id, records.Single(r => r.Id == refund.Id).OriginalSpendingRecordId);
        Assert.Equal(2, await read.SpendingLines.CountAsync());
    }

    [Fact]
    public async Task ChildCorrectionsParticipateInTheParentConcurrencyBoundary()
    {
        var d = await SeedAsync();
        await using var first = _database.CreateDbContext();
        await using var second = _database.CreateDbContext();
        var winner = await first.SpendingRecords.Include(r => r.Lines).SingleAsync();
        var loser = await second.SpendingRecords.Include(r => r.Lines).SingleAsync();
        winner.CorrectLine(winner.Revision, winner.Lines[0].Id, 2m, 1m, 2m, PromotionStatus.Yes, null);
        loser.CorrectLine(loser.Revision, loser.Lines[0].Id, 1m, 1m, 1m, PromotionStatus.No, null);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var read = _database.CreateDbContext();
        var saved = await read.SpendingRecords.Include(r => r.Lines).SingleAsync();
        Assert.Equal(2m, saved.TotalAmount);
        Assert.Equal(PromotionStatus.Yes, saved.Lines[0].PromotionStatus);
        Assert.Equal(winner.Revision, saved.Revision);
        Assert.NotEqual(d.Record.Revision, saved.Revision);
    }

    [Fact]
    public async Task SourceDetachmentAllowsReceiptReplacementWithoutChangingTheRememberedPrice()
    {
        var d = await SeedAsync();
        await using (var context = _database.CreateDbContext())
        {
            var record = await context.SpendingRecords.Include(r => r.Lines).SingleAsync();
            var price = await context.SpendingItemMerchantPrices.SingleAsync();
            price.RememberFrom(record, record.Lines[0], 2m);
            await context.SaveChangesAsync();
        }
        await using (var context = _database.CreateDbContext())
        {
            var record = await context.SpendingRecords.Include(r => r.Lines).SingleAsync();
            var price = await context.SpendingItemMerchantPrices.SingleAsync();
            var revision = price.Revision;
            await using var transaction = await context.Database.BeginTransactionAsync();
            price.ClearSource();
            await context.SaveChangesAsync(); // Clear restrictive FK before removing its source row.
            var removed = record.ReplaceLines(record.Revision, [new(d.Category, 3m)]);
            context.SpendingLines.RemoveRange(removed);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            Assert.NotEqual(revision, price.Revision);
        }
        await using var read = _database.CreateDbContext();
        var saved = await read.SpendingItemMerchantPrices.SingleAsync();
        Assert.Null(saved.SourceSpendingLineId);
        Assert.Equal(2m, saved.UsualUnitPrice);
        Assert.Single(await read.SpendingLines.ToArrayAsync());
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1000000000000")]
    [InlineData("0.001")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public async Task DatabaseRejectsInvalidLineAndReceiptAmounts(string value)
    {
        var d = await SeedAsync();
        await using var context = _database.CreateDbContext();
        var lineError = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "SpendingLines" SET "NetAmount" = CAST({value} AS numeric) WHERE "SpendingRecordId" = {d.Record.Id}
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, lineError.SqlState);
        var receiptError = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "SpendingRecords" SET "ReceiptTotal" = CAST({value} AS numeric) WHERE "Id" = {d.Record.Id}
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, receiptError.SqlState);
    }

    [Theory]
    [InlineData("quantity", "0")]
    [InlineData("quantity", "1000000")]
    [InlineData("quantity", "0.0000001")]
    [InlineData("price", "-1")]
    [InlineData("price", "1000000000")]
    [InlineData("price", "0.0000001")]
    public async Task DatabaseRejectsInvalidQuantityAndUnitPrice(string field, string value)
    {
        var d = await SeedAsync();
        await using var context = _database.CreateDbContext();
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            if (field == "quantity")
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingLines" SET "Quantity" = CAST({value} AS numeric) WHERE "SpendingRecordId" = {d.Record.Id}""");
            else
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingLines" SET "UnitPrice" = CAST({value} AS numeric) WHERE "SpendingRecordId" = {d.Record.Id}""");
        });
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Theory]
    [InlineData("quantityPair")]
    [InlineData("itemPair")]
    [InlineData("promotion")]
    [InlineData("position")]
    [InlineData("description")]
    [InlineData("currency")]
    [InlineData("precision")]
    public async Task DatabaseRejectsMalformedLineShapesAndParentCurrencyMismatches(string field)
    {
        var d = await SeedAsync();
        await using var context = _database.CreateDbContext();
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            if (field == "quantityPair")
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingLines" SET "UnitPrice" = NULL WHERE "SpendingRecordId" = {d.Record.Id}""");
            else if (field == "itemPair")
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingLines" SET "ItemName" = NULL WHERE "SpendingRecordId" = {d.Record.Id}""");
            else if (field == "promotion")
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingLines" SET "PromotionStatus" = {0} WHERE "SpendingRecordId" = {d.Record.Id}""");
            else if (field == "position")
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingLines" SET "Position" = {0} WHERE "SpendingRecordId" = {d.Record.Id}""");
            else if (field == "description")
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingLines" SET "Description" = {"\tText\n"} WHERE "SpendingRecordId" = {d.Record.Id}""");
            else if (field == "currency")
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingLines" SET "CurrencyCode" = {"ZAR"} WHERE "SpendingRecordId" = {d.Record.Id}""");
            else
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingLines" SET "CurrencyMinorUnits" = {3} WHERE "SpendingRecordId" = {d.Record.Id}""");
        });
        Assert.Equal(field is "currency" or "precision" ? PostgresErrorCodes.ForeignKeyViolation : PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Theory]
    [InlineData("merchant")]
    [InlineData("category")]
    [InlineData("item")]
    [InlineData("source")]
    [InlineData("record")]
    public async Task DatabaseRejectsCrossHouseholdReferences(string field)
    {
        var a = await SeedAsync(); var b = await SeedAsync();
        await using var context = _database.CreateDbContext();
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            if (field == "merchant")
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingRecords" SET "MerchantId" = {b.Merchant.Id} WHERE "Id" = {a.Record.Id}""");
            else if (field == "category")
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingLines" SET "ExpenseCategoryId" = {b.Category.Id} WHERE "SpendingRecordId" = {a.Record.Id}""");
            else if (field == "item")
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingLines" SET "SpendingItemId" = {b.Item.Id} WHERE "SpendingRecordId" = {a.Record.Id}""");
            else if (field == "source")
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingItemMerchantPrices" SET "SourceSpendingLineId" = {b.Record.Lines[0].Id} WHERE "Id" = {a.Price.Id}""");
            else
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingLines" SET "SpendingRecordId" = {b.Record.Id}, "Position" = {2} WHERE "SpendingRecordId" = {a.Record.Id}""");
        });
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Theory]
    [InlineData("refundTarget")]
    [InlineData("foreignTarget")]
    [InlineData("self")]
    [InlineData("nullKind")]
    [InlineData("paymentLink")]
    public async Task DatabaseEnforcesRefundOriginalTypeHouseholdAndNullConsistency(string field)
    {
        var d = await SeedAsync(); var other = await SeedAsync();
        var refund = new SpendingRecord(Guid.NewGuid(), d.Merchant, d.Record.SpendingDate, "GBP",
            [new(d.Category, 1m)], new string('b', 64), SpendingRecordKind.Refund);
        var otherRefund = new SpendingRecord(Guid.NewGuid(), d.Merchant, d.Record.SpendingDate, "GBP",
            [new(d.Category, 1m)], new string('c', 64), SpendingRecordKind.Refund);
        await using var context = _database.CreateDbContext();
        context.AddRange(refund, otherRefund); await context.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            if (field == "nullKind")
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingRecords" SET "OriginalSpendingRecordId" = {d.Record.Id}, "OriginalSpendingRecordKind" = NULL WHERE "Id" = {refund.Id}""");
            else
            {
                var target = field == "foreignTarget" ? other.Record.Id : field == "refundTarget" ? otherRefund.Id : field == "self" ? refund.Id : d.Record.Id;
                var receiving = field == "paymentLink" ? d.Record.Id : refund.Id;
                await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "SpendingRecords" SET "OriginalSpendingRecordId" = {target}, "OriginalSpendingRecordKind" = {1} WHERE "Id" = {receiving}""");
            }
        });
        // A self-reference is row-invalid; a foreign target fails the scoped FK.
        Assert.Equal(field is "foreignTarget" or "refundTarget" ? PostgresErrorCodes.ForeignKeyViolation : PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task SourceReferencesRestrictDeletionWithoutLosingPrices()
    {
        var d = await SeedAsync();
        await using var context = _database.CreateDbContext();
        var record = await context.SpendingRecords.Include(r => r.Lines).SingleAsync();
        var price = await context.SpendingItemMerchantPrices.SingleAsync();
        price.RememberFrom(record, record.Lines[0], 2m); await context.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""DELETE FROM "SpendingLines" WHERE "SpendingRecordId" = {record.Id}"""));
        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
        Assert.Equal(2m, (await context.SpendingItemMerchantPrices.AsNoTracking().SingleAsync()).UsualUnitPrice);
    }

    [Fact]
    public async Task AReferencedOriginalPaymentCannotBeReclassifiedAsARefund()
    {
        var d = await SeedAsync();
        var refund = new SpendingRecord(Guid.NewGuid(), d.Merchant, d.Record.SpendingDate, "GBP",
            [new(d.Category, 1m)], new string('b', 64), SpendingRecordKind.Refund, d.Record);
        await using var context = _database.CreateDbContext();
        context.Add(refund); await context.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "SpendingRecords" SET "Kind" = {2} WHERE "Id" = {d.Record.Id}
            """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Fact]
    public async Task ExplicitReplacementUpdatesMerchantAndMetadataWhilePreservingTheCreateIdentity()
    {
        var d = await SeedAsync();
        var newMerchant = new Merchant(d.Home, "Other shop");
        await using (var context = _database.CreateDbContext())
        {
            context.Add(newMerchant);
            var record = await context.SpendingRecords.Include(r => r.Lines).SingleAsync();
            var removed = record.Replace(record.Revision, newMerchant, new(2026, 11, 1),
                [new(d.Category, 10m)], 10m, "Corrected reference", null);
            context.SpendingLines.RemoveRange(removed);
            await context.SaveChangesAsync();
        }
        await using var read = _database.CreateDbContext();
        var saved = await read.SpendingRecords.Include(r => r.Lines).SingleAsync();
        Assert.Equal(newMerchant.Id, saved.MerchantId);
        Assert.Equal("Other shop", saved.MerchantName);
        Assert.Equal(d.Record.CurrencyCode, saved.CurrencyCode);
        Assert.Equal(d.Record.CreationRequestHash, saved.CreationRequestHash);
        Assert.Equal(10m, saved.CountedAmount);
        Assert.Single(await read.SpendingLines.ToArrayAsync());
    }

    [Fact]
    public async Task SameDayMerchantAndReceiptReferencesAreNotPurchaseIdentityKeys()
    {
        var d = await SeedAsync();
        var next = new SpendingRecord(Guid.NewGuid(), d.Merchant, d.Record.SpendingDate, "GBP",
            [new(d.Category, 1m)], d.Record.CreationRequestHash, externalReference: d.Record.ExternalReference);
        await using var context = _database.CreateDbContext();
        context.Add(next); await context.SaveChangesAsync();
        Assert.Equal(2, await context.SpendingRecords.CountAsync());
        Assert.NotEqual(d.Record.Id, next.Id);
    }

    [Fact]
    public async Task MigrationPreservesExistingSetupAndCanRollBackAndReapplyWithoutChangingPrices()
    {
        var home = await new HouseholdService(_database).CreateAsync("Home", "Alex");
        var setup = new SpendingSetupService(_database);
        var category = await setup.CreateCategoryAsync(home.Id, "Food");
        var merchant = await setup.CreateMerchantAsync(home.Id, "Shop");
        var item = await setup.CreateItemAsync(home.Id, category.Id, "EGGS", "Eggs", "pack-6", "Six eggs");
        var price = await setup.CreateUsualPriceAsync(home.Id, item.Id, merchant.Id, "GBP", 3.123456m);
        await using var context = _database.CreateDbContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261117122142_HardenSpendingSetup");
        await migrator.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        var preserved = await context.SpendingItemMerchantPrices.SingleAsync();
        Assert.Equal(price.Revision, preserved.Revision);
        Assert.Equal(price.UsualUnitPrice, preserved.UsualUnitPrice);
        Assert.Null(preserved.SourceSpendingLineId);
        Assert.Equal(item.Code, (await context.SpendingItems.SingleAsync()).Code);
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync());
    }
}
