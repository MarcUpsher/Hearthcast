using System.Text.Json;
using Hearthcast.Data.Households;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Tests.Spending;

public sealed class SpendingItemIdentityTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private SpendingSetupService Setup => new(_database);
    private SpendingRecordService Records => new(_database);
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    private sealed record Data(Guid Home, ExpenseCategory Category, Merchant Merchant, SpendingItem Item)
    {
        public SpendingRecordValues Values => new(Merchant.Id, new(2026, 11, 17), "GBP",
            [new(Category.Id, 3m, Item.Id, 1m, 3m, ReceiptDescription: "SHOP EGGS", MerchantItemReference: "123", Notes: "Keep")],
            ReceiptTotal: 3m, Notes: "Receipt notes");
    }

    private async Task<Data> SeedAsync()
    {
        var home = await new HouseholdService(_database).CreateAsync("Home", "Alex");
        var category = await Setup.CreateCategoryAsync(home.Id, "Food");
        var merchant = await Setup.CreateMerchantAsync(home.Id, "Shop");
        var item = await Setup.CreateItemAsync(home.Id, category.Id, "SHOP-EGGS", "Shop eggs", "pack-6", "Six eggs");
        return new(home.Id, category, merchant, item);
    }

    [Fact]
    public async Task RenameUpdatesAllLinkedHistoryOnlyAndPreservesAmountsSourcesAndRetryIdentity()
    {
        var d = await SeedAsync(); var other = await SeedAsync();
        var otherRecord = (await Records.CreateAsync(other.Home, Guid.NewGuid(), other.Values)).Record;
        var unlinked = (await Records.CreateAsync(d.Home, Guid.NewGuid(), d.Values with
        { Lines = [new(d.Category.Id, 3m, Description: "Unlinked amount")] })).Record;
        var values = d.Values with
        {
            Lines = [d.Values.Lines[0], new(d.Category.Id, 2m, d.Item.Id, 1m, 2m),
                new(d.Category.Id, 1m, Description: "Delivery", IsAdditionalCost: true)],
            Discounts = [new(d.Category.Id, 1m, "Offer")], ReceiptTotal = 5m
        };
        var payment = (await Records.CreateAsync(d.Home, Guid.NewGuid(), values, [new(1, 3m)])).Record;
        var price = Assert.Single(await Setup.ListUsualPricesAsync(d.Home, d.Item.Id));
        var refund = (await Records.CreateAsync(d.Home, Guid.NewGuid(), d.Values with
        { Kind = SpendingRecordKind.Refund, OriginalPaymentId = payment.Id })).Record;
        var voided = (await Records.CreateAsync(d.Home, Guid.NewGuid(), d.Values)).Record;
        voided = await Records.VoidAsync(d.Home, voided.Id, voided.Revision);
        await Setup.SetItemRetiredAsync(d.Home, d.Item.Id, true);
        var category = await Setup.CreateCategoryAsync(d.Home, "Other category");

        var item = await Setup.UpdateItemAsync(d.Home, d.Item.Id, category.Id, " eggs ", " Eggs ", "Box of six", "New notes");
        Assert.Equal("EGGS", item.Code); Assert.Equal("Eggs", item.Name);
        Assert.Equal(d.Item.Id, item.Id); Assert.Equal(d.Item.UnitKey, item.UnitKey); Assert.True(item.IsRetired);
        foreach (var before in new[] { payment, refund, voided })
        {
            var after = await Records.GetAsync(d.Home, before.Id);
            Assert.NotEqual(before.Revision, after.Revision);
            Assert.True(after.UpdatedAtUtc >= before.UpdatedAtUtc);
            Assert.Equal(UnchangedRecordData(before), UnchangedRecordData(after));
            foreach (var line in after.Lines.Where(l => l.SpendingItemId == item.Id))
            { Assert.Equal("EGGS", line.ItemCode); Assert.Equal("Eggs", line.ItemName); }
        }
        Assert.Equal(JsonSerializer.Serialize(unlinked), JsonSerializer.Serialize(await Records.GetAsync(d.Home, unlinked.Id)));
        Assert.Equal(JsonSerializer.Serialize(otherRecord), JsonSerializer.Serialize(await Records.GetAsync(other.Home, otherRecord.Id)));
        Assert.Equal(JsonSerializer.Serialize(price), JsonSerializer.Serialize(await Setup.GetUsualPriceAsync(d.Home, price.Id)));
        var lastPaid = await Records.FindLastPaidAsync(d.Home, item.Id, d.Merchant.Id, "GBP", new(2026, 11, 21));
        Assert.Equal(payment.Lines[1].Id, lastPaid!.SpendingLineId); Assert.Equal(2m, lastPaid.UnitPrice);
        Assert.True(lastPaid.HasReceiptDiscount);
        var retry = await Records.CreateAsync(d.Home, payment.Id, values, [new(1, 3m)]);
        Assert.False(retry.WasCreated); Assert.Equal("Eggs", retry.Record.Lines[0].ItemName);
        await Assert.ThrowsAsync<SpendingRecordConflictException>(() => Records.CorrectHeaderAsync(d.Home, payment.Id,
            payment.Revision, payment.SpendingDate, payment.ReceiptTotal, null, "Stale edit"));

        var revision = retry.Record.Revision;
        await Setup.UpdateItemAsync(d.Home, item.Id, category.Id, "eggs", "Eggs", "Different label", null);
        Assert.Equal(revision, (await Records.GetAsync(d.Home, payment.Id)).Revision);
        var reused = await Setup.CreateItemAsync(d.Home, d.Category.Id, "SHOP-EGGS", "Different eggs", "each", "Each");
        Assert.NotEqual(item.Id, reused.Id);
        Assert.Equal(item.Id, (await Records.GetAsync(d.Home, payment.Id)).Lines[0].SpendingItemId);
    }

    // Compare the entire saved record, excluding just the fields intentionally changed by a rename.
    private static string UnchangedRecordData(SpendingRecord record)
    {
        var json = JsonSerializer.SerializeToNode(record)!.AsObject();
        json.Remove("Revision"); json.Remove("UpdatedAtUtc");
        foreach (var line in json["Lines"]!.AsArray())
        { line!.AsObject().Remove("ItemCode"); line.AsObject().Remove("ItemName"); }
        return json.ToJsonString();
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("retiredDuplicate")]
    [InlineData("invalid")]
    [InlineData("overlong")]
    [InlineData("blank")]
    public async Task RejectedCodeChangePreservesCatalogueHistoryAndRevision(string error)
    {
        var d = await SeedAsync();
        var taken = await Setup.CreateItemAsync(d.Home, d.Category.Id, "TAKEN", "Other", "each", "Each");
        if (error == "retiredDuplicate") await Setup.SetItemRetiredAsync(d.Home, taken.Id, true);
        var record = (await Records.CreateAsync(d.Home, Guid.NewGuid(), d.Values)).Record;
        var code = error switch { "invalid" => "bad code", "overlong" => new string('A', 65), "blank" => " ", _ => " taken " };
        var exception = await Record.ExceptionAsync(() => Setup.UpdateItemAsync(d.Home, d.Item.Id, d.Category.Id,
            code, "Changed name", "Changed label", null));
        if (error is "duplicate" or "retiredDuplicate") Assert.IsType<BusinessRuleViolationException>(exception);
        else Assert.IsType<InvalidValueException>(exception);
        Assert.Equal(JsonSerializer.Serialize(d.Item), JsonSerializer.Serialize(await Setup.GetItemAsync(d.Home, d.Item.Id)));
        Assert.Equal(JsonSerializer.Serialize(record), JsonSerializer.Serialize(await Records.GetAsync(d.Home, record.Id)));
    }

    [Fact]
    public async Task FailureUpdatingHistoryRollsBackCatalogueAndEveryReceipt()
    {
        var d = await SeedAsync();
        var record = (await Records.CreateAsync(d.Home, Guid.NewGuid(), d.Values)).Record;
        await using var context = _database.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION "FailIdentityUpdate"() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Synthetic history failure'; END; $$;
            CREATE TRIGGER "FailIdentityUpdate" BEFORE UPDATE ON "SpendingLines"
            FOR EACH ROW EXECUTE FUNCTION "FailIdentityUpdate"();
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => Setup.UpdateItemAsync(d.Home, d.Item.Id, d.Category.Id,
            "EGGS", "Eggs", "Six eggs", null));
        Assert.Equal(JsonSerializer.Serialize(d.Item), JsonSerializer.Serialize(await Setup.GetItemAsync(d.Home, d.Item.Id)));
        Assert.Equal(JsonSerializer.Serialize(record), JsonSerializer.Serialize(await Records.GetAsync(d.Home, record.Id)));
    }

    [Fact]
    public async Task MetadataOnlyEditsPreserveOlderDivergentLabelsUntilIdentityActuallyChanges()
    {
        var d = await SeedAsync();
        var record = (await Records.CreateAsync(d.Home, Guid.NewGuid(), d.Values)).Record;
        await using (var context = _database.CreateDbContext())
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "SpendingLines" SET "ItemName" = 'Earlier name'
                WHERE "HouseholdId" = {d.Home} AND "SpendingItemId" = {d.Item.Id}
                """);
        var before = await Records.GetAsync(d.Home, record.Id);
        await Setup.UpdateItemAsync(d.Home, d.Item.Id, d.Category.Id, " shop-eggs ", " Shop eggs ", "Box of six", "Notes only");
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await Records.GetAsync(d.Home, record.Id)));
        await Setup.UpdateItemAsync(d.Home, d.Item.Id, d.Category.Id, "EGGS", "Eggs", "Box of six", null);
        var after = await Records.GetAsync(d.Home, record.Id);
        Assert.NotEqual(before.Revision, after.Revision);
        Assert.Equal("Eggs", after.Lines[0].ItemName);
        Assert.Equal("EGGS", after.Lines[0].ItemCode);
    }

    [Fact]
    public async Task ConcurrentCaptureAndRenameCannotLeaveOldIdentityInNewReceipt()
    {
        var d = await SeedAsync();
        var factory = new InterceptingContextFactory(_database, new ConcurrentSaveBarrier());
        var capture = new SpendingRecordService(factory).CreateAsync(d.Home, Guid.NewGuid(), d.Values);
        var rename = new SpendingSetupService(factory).UpdateItemAsync(d.Home, d.Item.Id, d.Category.Id,
            "EGGS", "Eggs", "Six eggs", null);
        await Task.WhenAll(capture, rename);
        var saved = await Records.GetAsync(d.Home, (await capture).Record.Id);
        Assert.Equal("EGGS", Assert.Single(saved.Lines).ItemCode);
        Assert.Equal("Eggs", saved.Lines[0].ItemName);
    }
}
