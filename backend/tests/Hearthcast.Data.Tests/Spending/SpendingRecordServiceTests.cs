using System.Globalization;
using Hearthcast.Data.Households;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hearthcast.Data.Tests.Spending;

public sealed class SpendingRecordServiceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private SpendingRecordService Service => new(_database);
    private SpendingSetupService Setup => new(_database);
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    private sealed record Data(Guid Home, ExpenseCategory Category, Merchant Merchant, SpendingItem Item)
    {
        internal SpendingLineValues Detailed(decimal amount = 3m) => new(Category.Id, amount, Item.Id, 1m, amount);
        internal SpendingRecordValues Values(decimal amount = 3m) => new(Merchant.Id, new(2026, 11, 17), "GBP", [Detailed(amount)]);
    }

    private async Task<Data> SeedAsync()
    {
        var home = await new HouseholdService(_database).CreateAsync("Home", "Alex");
        var category = await Setup.CreateCategoryAsync(home.Id, "Food");
        var merchant = await Setup.CreateMerchantAsync(home.Id, "Shop");
        var item = await Setup.CreateItemAsync(home.Id, category.Id, "EGGS", "Eggs", "pack-6", "Six eggs");
        return new(home.Id, category, merchant, item);
    }

    private async Task<SpendingRecord> CaptureAsync(Data d, SpendingRecordValues? values = null,
        IReadOnlyList<RememberSpendingPrice>? prices = null) =>
        (await Service.CreateAsync(d.Home, Guid.NewGuid(), values ?? d.Values(), prices)).Record;

    [Fact]
    public async Task CaptureSupportsMixedDetailAndDoesNotImplicitlyAdoptAPurchasePrice()
    {
        var d = await SeedAsync();
        var price = await Setup.CreateUsualPriceAsync(d.Home, d.Item.Id, d.Merchant.Id, "GBP", 3m);
        var record = await CaptureAsync(d, d.Values() with
        {
            Lines = [new(d.Category.Id, 5m, Description: "Quick amount"), d.Detailed(2m), d.Detailed(3m)], ReceiptTotal = 11m
        });
        Assert.Equal(10m, record.TotalAmount);
        Assert.Equal("Unreconciled", record.ReconciliationStatus);
        Assert.Equal(3, record.Lines.Count);
        Assert.Equal(new[] { 1, 2, 3 }, record.Lines.Select(l => l.Position));
        var saved = await Service.GetAsync(d.Home, record.Id);
        Assert.Equal(record.Revision, saved.Revision);
        Assert.Equal(10m, saved.TotalAmount);
        Assert.Equal(price.Revision, (await Setup.GetUsualPriceAsync(d.Home, price.Id)).Revision);
        Assert.Equal(record.Id, Assert.Single(await Service.ListAsync(d.Home)).Id);
    }

    [Fact]
    public async Task CreateRetriesCanonicalizeRequestDataAndReturnCurrentStateWithoutReplayingPrices()
    {
        var d = await SeedAsync();
        var id = Guid.NewGuid();
        var values = d.Values() with { CurrencyCode = " gbp ", Notes = " Notes ", ReceiptTotal = 3.00m };
        var result = await Service.CreateAsync(d.Home, id, values, [new(1, 3.00m)]);
        Assert.True(result.WasCreated);
        var price = Assert.Single(await Setup.ListUsualPricesAsync(d.Home, d.Item.Id));
        await Setup.RenameMerchantAsync(d.Home, d.Merchant.Id, "Renamed");
        await Setup.RenameCategoryAsync(d.Home, d.Category.Id, "Renamed");
        await Setup.UpdateItemAsync(d.Home, d.Item.Id, d.Category.Id, "Renamed", "Box of six", null);
        await Setup.SetItemRetiredAsync(d.Home, d.Item.Id, true);
        await Setup.SetMerchantRetiredAsync(d.Home, d.Merchant.Id, true);
        var newPrice = await Setup.UpdateUsualPriceAsync(d.Home, price.Id, price.Revision, 5m);
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var retry = await Service.CreateAsync(d.Home, id, values with
            {
                CurrencyCode = "GBP", Notes = "Notes", ReceiptTotal = 3m,
                Lines = [d.Detailed(3.0000m)]
            }, [new(1, 3m)]);
            Assert.False(retry.WasCreated);
            Assert.Equal(result.Record.Revision, retry.Record.Revision);
            Assert.Equal("Shop", retry.Record.MerchantName);
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
        var corrected = await Service.CorrectHeaderAsync(d.Home, id, result.Record.Revision, new(2026, 11, 1), null, null, "Corrected");
        var replay = await Service.CreateAsync(d.Home, id, values, [new(1, 3m)]);
        Assert.False(replay.WasCreated);
        Assert.Equal(corrected.Revision, replay.Record.Revision);
        Assert.Equal("Corrected", replay.Record.Notes);
        var voided = await Service.VoidAsync(d.Home, id, corrected.Revision);
        var afterVoid = await Service.CreateAsync(d.Home, id, values, [new(1, 3m)]);
        Assert.True(afterVoid.Record.IsVoided);
        Assert.Equal(voided.Revision, afterVoid.Record.Revision);
        Assert.Equal(newPrice.Revision, (await Setup.GetUsualPriceAsync(d.Home, price.Id)).Revision);
        await using var context = _database.CreateDbContext();
        Assert.Equal(1, await context.SpendingRecords.CountAsync());
        Assert.Equal(1, await context.SpendingLines.CountAsync());
    }

    [Fact]
    public async Task ChangedPayloadOrForeignUuidCollisionConflictsWithoutRevealingOrChangingTheOtherRecord()
    {
        var d = await SeedAsync(); var other = await SeedAsync();
        var record = await CaptureAsync(d);
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Service.CreateAsync(d.Home, record.Id, d.Values(2m)));
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Service.CreateAsync(other.Home, record.Id, other.Values()));
        Assert.Equal(record.Revision, (await Service.GetAsync(d.Home, record.Id)).Revision);
        Assert.Empty(await Service.ListAsync(other.Home));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentCreatesWithOneUuidPersistOnceAndResolveIdenticalRetries(bool differentPayload)
    {
        var d = await SeedAsync();
        var service = new SpendingRecordService(new InterceptingContextFactory(_database, new ConcurrentSaveBarrier()));
        var id = Guid.NewGuid();
        async Task<(SpendingRecordCaptureResult? Result, Exception? Error)> Attempt(decimal amount)
        {
            try { return (await service.CreateAsync(d.Home, id, d.Values(amount)), null); }
            catch (Exception error) { return (null, error); }
        }
        var results = await Task.WhenAll(Attempt(3m), Attempt(differentPayload ? 2m : 3m));
        Assert.Single(results, r => r.Result?.WasCreated == true);
        if (differentPayload) Assert.IsType<BusinessRuleViolationException>(Assert.Single(results, r => r.Error is not null).Error);
        else Assert.Single(results, r => r.Result?.WasCreated == false);
        await using var context = _database.CreateDbContext();
        Assert.Equal(1, await context.SpendingRecords.CountAsync());
        Assert.Equal(1, await context.SpendingLines.CountAsync());
    }

    [Fact]
    public async Task ExplicitPriceAdoptionIsAtomicAndRequiresTheExistingRevision()
    {
        var d = await SeedAsync();
        var record = await CaptureAsync(d, prices: [new(1, 2m)]);
        var price = Assert.Single(await Setup.ListUsualPricesAsync(d.Home, d.Item.Id));
        Assert.Equal(2m, price.UsualUnitPrice);
        Assert.Equal(record.Lines[0].Id, price.SourceSpendingLineId);
        await Assert.ThrowsAsync<SpendingPriceConflictException>(() => CaptureAsync(d, prices: [new(1, 3m)]));
        await Assert.ThrowsAsync<SpendingPriceConflictException>(() => CaptureAsync(d, prices: [new(1, 3m, Guid.NewGuid())]));
        Assert.Single(await Service.ListAsync(d.Home));
        var next = await CaptureAsync(d, prices: [new(1, 3m, price.Revision)]);
        var saved = await Setup.GetUsualPriceAsync(d.Home, price.Id);
        Assert.Equal(next.Lines[0].Id, saved.SourceSpendingLineId);
        Assert.NotEqual(price.Revision, saved.Revision);
    }

    [Theory]
    [InlineData("quick")]
    [InlineData("amountOnly")]
    [InlineData("refund")]
    [InlineData("position")]
    [InlineData("duplicate")]
    [InlineData("negative")]
    [InlineData("emptyRevision")]
    public async Task InvalidPriceChoicesCannotPartiallySaveReceipts(string error)
    {
        var d = await SeedAsync();
        var values = d.Values();
        IReadOnlyList<RememberSpendingPrice> choices = [new(1, 2m)];
        if (error == "quick") values = values with { Lines = [new(d.Category.Id, 3m)] };
        if (error == "amountOnly") values = values with { Lines = [new(d.Category.Id, 3m, d.Item.Id)] };
        if (error == "refund") values = values with { Kind = SpendingRecordKind.Refund };
        if (error == "position") choices = [new(2, 2m)];
        if (error == "duplicate") { values = values with { Lines = [d.Detailed(), d.Detailed()] }; choices = [new(1, 2m), new(2, 2m)]; }
        if (error == "negative") choices = [new(1, -1m)];
        if (error == "emptyRevision") choices = [new(1, 2m, Guid.Empty)];
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateAsync(d.Home, Guid.NewGuid(), values, choices));
        await using var context = _database.CreateDbContext();
        Assert.Equal(0, await context.SpendingRecords.CountAsync());
        Assert.Equal(0, await context.SpendingLines.CountAsync());
        Assert.Equal(0, await context.SpendingItemMerchantPrices.CountAsync());
    }

    [Fact]
    public async Task ReplacementDetachesSourcesAndCanExplicitlyReadoptUsingThePreDetachRevision()
    {
        var d = await SeedAsync();
        var record = await CaptureAsync(d, prices: [new(1, 2m)]);
        var price = Assert.Single(await Setup.ListUsualPricesAsync(d.Home, d.Item.Id));
        var replacement = await Service.ReplaceAsync(d.Home, record.Id, record.Revision, d.Merchant.Id,
            record.SpendingDate, [d.Detailed(4m), new(d.Category.Id, 1m)], 5m, null, null, [new(1, 4m, price.Revision)]);
        var adopted = await Setup.GetUsualPriceAsync(d.Home, price.Id);
        Assert.Equal(replacement.Lines[0].Id, adopted.SourceSpendingLineId);
        Assert.Equal(4m, adopted.UsualUnitPrice);
        Assert.Equal(5m, replacement.TotalAmount);
        Assert.Equal(record.CreationRequestHash, replacement.CreationRequestHash);
        Assert.DoesNotContain(replacement.Lines, l => l.Id == record.Lines[0].Id);
        var quick = await Service.ReplaceAsync(d.Home, record.Id, replacement.Revision, d.Merchant.Id,
            record.SpendingDate, [new(d.Category.Id, 5m)], null, null, null);
        var detached = await Setup.GetUsualPriceAsync(d.Home, price.Id);
        Assert.Null(detached.SourceSpendingLineId);
        Assert.Equal(4m, detached.UsualUnitPrice);
        Assert.Equal(5m, quick.TotalAmount);
        await using var context = _database.CreateDbContext();
        Assert.Equal(1, await context.SpendingLines.CountAsync());
    }

    [Fact]
    public async Task CorrectingAmountsPreservesNamesAndPriceButRemovingQuantityDetachesItsSource()
    {
        var d = await SeedAsync();
        var record = await CaptureAsync(d, prices: [new(1, 3m)]);
        var price = Assert.Single(await Setup.ListUsualPricesAsync(d.Home, d.Item.Id));
        await Setup.UpdateItemAsync(d.Home, d.Item.Id, d.Category.Id, "Renamed", "Box of six", null);
        await Setup.RenameCategoryAsync(d.Home, d.Category.Id, "Groceries");
        var correction = await Service.CorrectLineAsync(d.Home, record.Id, record.Revision, record.Lines[0].Id,
            2m, 1m, 2m, PromotionStatus.Yes, "Sale");
        Assert.Equal("Eggs", correction.Lines[0].ItemName);
        Assert.Equal("Food", correction.Lines[0].ExpenseCategoryName);
        Assert.Equal("Six eggs", correction.Lines[0].UnitLabel);
        var retained = await Setup.GetUsualPriceAsync(d.Home, price.Id);
        Assert.Equal(3m, retained.UsualUnitPrice);
        Assert.Equal(price.SourceSpendingLineId, retained.SourceSpendingLineId);
        Assert.Equal(price.Revision, retained.Revision);
        var amountOnly = await Service.CorrectLineAsync(d.Home, record.Id, correction.Revision, record.Lines[0].Id,
            2m, null, null, PromotionStatus.Unknown, null);
        Assert.Null(amountOnly.Lines[0].Quantity);
        Assert.Null((await Setup.GetUsualPriceAsync(d.Home, price.Id)).SourceSpendingLineId);
    }

    [Fact]
    public async Task VoidingRetainsHistoryClearsSourcesAndPreservesLinkedRefunds()
    {
        var d = await SeedAsync();
        var record = await CaptureAsync(d, prices: [new(1, 3m)]);
        var refund = await CaptureAsync(d, d.Values(1m) with { Kind = SpendingRecordKind.Refund, OriginalPaymentId = record.Id });
        var price = Assert.Single(await Setup.ListUsualPricesAsync(d.Home, d.Item.Id));
        var voided = await Service.VoidAsync(d.Home, record.Id, record.Revision);
        Assert.True(voided.IsVoided);
        Assert.Single(voided.Lines);
        Assert.Equal(0m, voided.CountedAmount);
        Assert.Equal(-1m, Assert.Single(await Service.ListAsync(d.Home)).CountedAmount);
        Assert.Equal(2, (await Service.ListAsync(d.Home, includeVoided: true)).Count);
        Assert.Equal(record.Id, (await Service.GetAsync(d.Home, refund.Id)).OriginalSpendingRecordId);
        var saved = await Setup.GetUsualPriceAsync(d.Home, price.Id);
        Assert.Null(saved.SourceSpendingLineId);
        Assert.Equal(3m, saved.UsualUnitPrice);
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Service.CorrectHeaderAsync(d.Home, record.Id, voided.Revision, record.SpendingDate, null, null, null));
        await Assert.ThrowsAsync<InvalidValueException>(() => CaptureAsync(d, d.Values(1m) with { Kind = SpendingRecordKind.Refund, OriginalPaymentId = record.Id }));
    }

    [Fact]
    public async Task InvalidReplacementAfterSourceDetachmentRollsBackTheWholeTransaction()
    {
        var d = await SeedAsync();
        var record = await CaptureAsync(d, prices: [new(1, 3m)]);
        var price = Assert.Single(await Setup.ListUsualPricesAsync(d.Home, d.Item.Id));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.ReplaceAsync(d.Home, record.Id, record.Revision,
            d.Merchant.Id, record.SpendingDate, [d.Detailed(-1m)], null, null, null));
        var saved = await Service.GetAsync(d.Home, record.Id);
        var savedPrice = await Setup.GetUsualPriceAsync(d.Home, price.Id);
        Assert.Equal(record.Revision, saved.Revision);
        Assert.Equal(record.Lines[0].Id, saved.Lines[0].Id);
        Assert.Equal(price.Revision, savedPrice.Revision);
        Assert.Equal(price.SourceSpendingLineId, savedPrice.SourceSpendingLineId);
    }

    [Fact]
    public async Task DatabaseFailureAfterDetachmentRollsBackHeaderLinesAndPriceChanges()
    {
        var d = await SeedAsync();
        var record = await CaptureAsync(d, prices: [new(1, 3m)]);
        var price = Assert.Single(await Setup.ListUsualPricesAsync(d.Home, d.Item.Id));
        await using var context = _database.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION "FailNewSpendingLine"() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Synthetic line failure'; END; $$;
            CREATE TRIGGER "FailNewSpendingLine" BEFORE INSERT ON "SpendingLines"
            FOR EACH ROW EXECUTE FUNCTION "FailNewSpendingLine"();
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => Service.ReplaceAsync(d.Home, record.Id, record.Revision,
            d.Merchant.Id, record.SpendingDate, [d.Detailed(4m)], null, null, "Changed"));
        Assert.Equal(record.Revision, (await Service.GetAsync(d.Home, record.Id)).Revision);
        var savedPrice = await Setup.GetUsualPriceAsync(d.Home, price.Id);
        Assert.Equal(price.Revision, savedPrice.Revision);
        Assert.Equal(price.SourceSpendingLineId, savedPrice.SourceSpendingLineId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LatePriceSaveFailureRollsBackTheNewReceiptAndItsLines(bool existingPrice)
    {
        var d = await SeedAsync();
        var price = existingPrice ? await Setup.CreateUsualPriceAsync(d.Home, d.Item.Id, d.Merchant.Id, "GBP", 3m) : null;
        await using var context = _database.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION "FailRememberedPrice"() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Synthetic price failure'; END; $$;
            CREATE TRIGGER "FailRememberedPrice" BEFORE INSERT OR UPDATE ON "SpendingItemMerchantPrices"
            FOR EACH ROW EXECUTE FUNCTION "FailRememberedPrice"();
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => Service.CreateAsync(d.Home, Guid.NewGuid(), d.Values(2m), [new(1, 2m, price?.Revision)]));
        Assert.Equal(0, await context.SpendingRecords.CountAsync());
        Assert.Equal(0, await context.SpendingLines.CountAsync());
        if (price is null) Assert.Equal(0, await context.SpendingItemMerchantPrices.CountAsync());
        else
        {
            var saved = await Setup.GetUsualPriceAsync(d.Home, price.Id);
            Assert.Equal(price.Revision, saved.Revision);
            Assert.Null(saved.SourceSpendingLineId);
            Assert.Equal(3m, saved.UsualUnitPrice);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReceiptCleanupDoesNotLoseAConcurrentManualPriceEdit(bool voidRecord)
    {
        var d = await SeedAsync();
        var record = await CaptureAsync(d, prices: [new(1, 3m)]);
        var price = Assert.Single(await Setup.ListUsualPricesAsync(d.Home, d.Item.Id));
        var factory = new InterceptingContextFactory(_database, new ConcurrentSaveBarrier());
        var records = new SpendingRecordService(factory);
        var setup = new SpendingSetupService(factory);
        var cleanup = Record.ExceptionAsync(() => voidRecord ? records.VoidAsync(d.Home, record.Id, record.Revision)
            : records.ReplaceAsync(d.Home, record.Id, record.Revision, d.Merchant.Id, record.SpendingDate, [new(d.Category.Id, 5m)], null, null, null));
        var manual = Record.ExceptionAsync(() => setup.UpdateUsualPriceAsync(d.Home, price.Id, price.Revision, 7m));
        var results = await Task.WhenAll(cleanup, manual);
        Assert.Null(results[0]);
        if (results[1] is not null) Assert.IsType<SpendingPriceConflictException>(results[1]);
        var saved = await Setup.GetUsualPriceAsync(d.Home, price.Id);
        Assert.Null(saved.SourceSpendingLineId);
        Assert.Equal(results[1] is null ? 7m : 3m, saved.UsualUnitPrice);
        var savedRecord = await Service.GetAsync(d.Home, record.Id);
        Assert.Equal(voidRecord, savedRecord.IsVoided);
        if (!voidRecord) Assert.Equal(5m, savedRecord.TotalAmount);
    }

    [Fact]
    public async Task PriceChoiceOrderDoesNotChangeTheCreateHashOrReplayAdoption()
    {
        var d = await SeedAsync();
        var item = await Setup.CreateItemAsync(d.Home, d.Category.Id, "MILK", "Milk", "litre", "Litre");
        var values = d.Values() with { Lines = [d.Detailed(), new(d.Category.Id, 2m, item.Id, 1m, 2m)] };
        var id = Guid.NewGuid();
        var first = await Service.CreateAsync(d.Home, id, values, [new(2, 2m), new(1, 3m)]);
        var prices = await Setup.ListUsualPricesAsync(d.Home, d.Item.Id);
        var retry = await Service.CreateAsync(d.Home, id, values, [new(1, 3.00m), new(2, 2.00m)]);
        Assert.False(retry.WasCreated);
        Assert.Equal(first.Record.Revision, retry.Record.Revision);
        Assert.Equal(Assert.Single(prices).Revision, Assert.Single(await Setup.ListUsualPricesAsync(d.Home, d.Item.Id)).Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentPriceAdoptionAllowsOneReceiptAndRejectsTheLosingPriceUpdate(bool existingPrice)
    {
        var d = await SeedAsync();
        var revision = existingPrice ? (await Setup.CreateUsualPriceAsync(d.Home, d.Item.Id, d.Merchant.Id, "GBP", 3m)).Revision : (Guid?)null;
        var service = new SpendingRecordService(new InterceptingContextFactory(_database, new ConcurrentSaveBarrier()));
        var results = await Task.WhenAll(new[] { 2m, 4m }.Select(async amount =>
        {
            try { return (Result: await service.CreateAsync(d.Home, Guid.NewGuid(), d.Values(amount), [new(1, amount, revision)]), Error: (Exception?)null); }
            catch (Exception error) { return (Result: (SpendingRecordCaptureResult?)null, Error: error); }
        }));
        var winner = Assert.Single(results, r => r.Error is null).Result!;
        Assert.IsType<SpendingPriceConflictException>(Assert.Single(results, r => r.Error is not null).Error);
        Assert.Single(await Service.ListAsync(d.Home));
        var saved = Assert.Single(await Setup.ListUsualPricesAsync(d.Home, d.Item.Id));
        Assert.Equal(winner.Record.Lines[0].Id, saved.SourceSpendingLineId);
        Assert.Equal(winner.Record.TotalAmount, saved.UsualUnitPrice);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentCorrectionsOrVoidCannotOverwriteTheWinningRecord(bool voidOther)
    {
        var d = await SeedAsync();
        var record = await CaptureAsync(d);
        var service = new SpendingRecordService(new InterceptingContextFactory(_database, new ConcurrentSaveBarrier()));
        var correction = Record.ExceptionAsync(() => service.CorrectLineAsync(d.Home, record.Id, record.Revision, record.Lines[0].Id, 2m, 1m, 2m, PromotionStatus.Yes, null));
        var other = Record.ExceptionAsync(() => voidOther ? service.VoidAsync(d.Home, record.Id, record.Revision)
            : service.CorrectHeaderAsync(d.Home, record.Id, record.Revision, record.SpendingDate, null, null, "Changed"));
        var results = await Task.WhenAll(correction, other);
        Assert.Single(results, e => e is null);
        Assert.IsType<SpendingRecordConflictException>(Assert.Single(results, e => e is not null));
    }

    [Fact]
    public async Task LastPaidUsesExactIdentityDateAndEligiblePaymentLinesWithDeterministicLineOrder()
    {
        var d = await SeedAsync();
        var first = await CaptureAsync(d, d.Values() with { SpendingDate = new(2026, 1, 1) });
        var second = await CaptureAsync(d, d.Values(2m) with { SpendingDate = new(2026, 1, 2), Lines = [d.Detailed(2m), d.Detailed(3m) with { Quantity = 2m, NetAmount = 5m }] });
        await CaptureAsync(d, d.Values(1m) with { SpendingDate = new(2026, 1, 3), Kind = SpendingRecordKind.Refund });
        await CaptureAsync(d, d.Values() with { SpendingDate = new(2026, 1, 4), Lines = [new(d.Category.Id, 5m, d.Item.Id)] });
        var voided = await CaptureAsync(d, d.Values(1m) with { SpendingDate = new(2026, 1, 5) });
        await Service.VoidAsync(d.Home, voided.Id, voided.Revision);
        await CaptureAsync(d, d.Values(9m) with { SpendingDate = new(2026, 2, 1) });
        var asOf = new DateOnly(2026, 1, 31);
        var last = (await Service.FindLastPaidAsync(d.Home, d.Item.Id, d.Merchant.Id, " gbp ", asOf))!;
        Assert.Equal(second.Id, last.SpendingRecordId);
        Assert.Equal(second.Lines[1].Id, last.SpendingLineId);
        Assert.Equal(3m, last.UnitPrice);
        Assert.Equal(2.50m, last.EffectiveUnitPrice);
        Assert.Equal(first.Id, (await Service.FindLastPaidAsync(d.Home, d.Item.Id, d.Merchant.Id, "GBP", new(2026, 1, 1)))!.SpendingRecordId);
        Assert.Equal(first.Id, (await Service.FindLastPaidAsync(d.Home, d.Item.Id, d.Merchant.Id, "GBP", asOf, second.Id))!.SpendingRecordId);
        Assert.Null(await Service.FindLastPaidAsync(d.Home, d.Item.Id, d.Merchant.Id, "ZAR", asOf));
        Assert.Null(await Service.FindLastPaidAsync(d.Home, d.Item.Id, d.Merchant.Id, "ZZZ", asOf));
        var merchant = await Setup.CreateMerchantAsync(d.Home, "Other shop");
        Assert.Null(await Service.FindLastPaidAsync(d.Home, d.Item.Id, merchant.Id, "GBP", asOf));
        var item = await Setup.CreateItemAsync(d.Home, d.Category.Id, "OTHER", "Other", "pack-12", "Twelve");
        Assert.Null(await Service.FindLastPaidAsync(d.Home, item.Id, d.Merchant.Id, "GBP", asOf));
        await Setup.SetItemRetiredAsync(d.Home, d.Item.Id, true);
        Assert.Equal(second.Id, (await Service.FindLastPaidAsync(d.Home, d.Item.Id, d.Merchant.Id, "GBP", asOf))!.SpendingRecordId);
    }

    [Fact]
    public async Task LastPaidBreaksEqualDateAndCreationTimeTiesByRecordIdentity()
    {
        var d = await SeedAsync();
        var first = await CaptureAsync(d, d.Values(2m));
        var second = await CaptureAsync(d, d.Values(4m));
        await using var context = _database.CreateDbContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "SpendingRecords" SET "CreatedAtUtc" = {new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)} WHERE "HouseholdId" = {d.Home}
            """);
        var expected = new[] { first, second }.OrderByDescending(r => r.Id).First();
        var last = (await Service.FindLastPaidAsync(d.Home, d.Item.Id, d.Merchant.Id, "GBP", first.SpendingDate))!;
        Assert.Equal(expected.Id, last.SpendingRecordId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrForeignReferencesCannotBeReadOrWrittenThroughTheHousehold(bool foreign)
    {
        var d = await SeedAsync(); var other = await SeedAsync();
        var own = await CaptureAsync(d);
        var theirs = await CaptureAsync(other);
        var merchant = foreign ? other.Merchant.Id : Guid.NewGuid();
        var category = foreign ? other.Category.Id : Guid.NewGuid();
        var item = foreign ? other.Item.Id : Guid.NewGuid();
        var record = foreign ? theirs.Id : Guid.NewGuid();
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.GetAsync(d.Home, record));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.VoidAsync(d.Home, record, theirs.Revision));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CorrectHeaderAsync(d.Home, record, theirs.Revision, own.SpendingDate, null, null, null));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CorrectLineAsync(d.Home, record, theirs.Revision, theirs.Lines[0].Id, 1m, 1m, 1m, PromotionStatus.Unknown, null));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CorrectLineAsync(d.Home, own.Id, own.Revision, theirs.Lines[0].Id, 1m, 1m, 1m, PromotionStatus.Unknown, null));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => CaptureAsync(d, d.Values() with { MerchantId = merchant }));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => CaptureAsync(d, d.Values() with { Lines = [new(category, 1m)] }));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => CaptureAsync(d, d.Values() with { Lines = [new(d.Category.Id, 1m, item)] }));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => CaptureAsync(d, d.Values() with { Kind = SpendingRecordKind.Refund, OriginalPaymentId = record }));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.ReplaceAsync(d.Home, own.Id, own.Revision, merchant, own.SpendingDate, [d.Detailed()], null, null, null));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.FindLastPaidAsync(d.Home, item, d.Merchant.Id, "GBP", own.SpendingDate));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.FindLastPaidAsync(d.Home, d.Item.Id, merchant, "GBP", own.SpendingDate));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.FindLastPaidAsync(d.Home, d.Item.Id, d.Merchant.Id, "GBP", own.SpendingDate, record));
        Assert.Single(await Service.ListAsync(d.Home));
        Assert.Equal(own.Revision, (await Service.GetAsync(d.Home, own.Id)).Revision);
    }

    [Fact]
    public async Task DateRangesEmptyInventoriesAndMissingParentsHaveDistinctResults()
    {
        var d = await SeedAsync();
        Assert.Empty(await Service.ListAsync(d.Home));
        var record = await CaptureAsync(d);
        Assert.Empty(await Service.ListAsync(d.Home, new(2026, 1, 1), new(2026, 1, 31)));
        Assert.Single(await Service.ListAsync(d.Home, record.SpendingDate, record.SpendingDate));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.ListAsync(d.Home, new(2026, 2, 1), new(2026, 1, 1)));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.ListAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), d.Values()));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CorrectHeaderAsync(d.Home, record.Id, Guid.Empty, record.SpendingDate, null, null, null));
    }

    [Fact]
    public async Task CancellationLeavesNoReceiptAndDoesNotDetachExistingPriceSources()
    {
        var d = await SeedAsync();
        var record = await CaptureAsync(d, prices: [new(1, 3m)]);
        var price = Assert.Single(await Setup.ListUsualPricesAsync(d.Home, d.Item.Id));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.CreateAsync(d.Home, Guid.NewGuid(), d.Values(), cancellationToken: cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.VoidAsync(d.Home, record.Id, record.Revision, cancellation.Token));
        Assert.Single(await Service.ListAsync(d.Home));
        Assert.Equal(price.Revision, (await Setup.GetUsualPriceAsync(d.Home, price.Id)).Revision);
    }

    [Fact]
    public async Task OriginalPaymentStatusBatchIsScopedAndReportsCurrentVoidStateWithoutChangingRefunds()
    {
        var d = await SeedAsync(); var foreign = await SeedAsync();
        var payment = await CaptureAsync(d); var otherPayment = await CaptureAsync(foreign);
        var refund = await CaptureAsync(d, d.Values() with { Kind = SpendingRecordKind.Refund, OriginalPaymentId = payment.Id });
        Assert.False((await Service.GetOriginalPaymentVoidStatesAsync(d.Home, [payment.Id, payment.Id]))[payment.Id]);
        await Service.VoidAsync(d.Home, payment.Id, payment.Revision);
        Assert.True((await Service.GetOriginalPaymentVoidStatesAsync(d.Home, [payment.Id]))[payment.Id]);
        Assert.Equal(refund.Revision, (await Service.GetAsync(d.Home, refund.Id)).Revision);
        Assert.Empty(await Service.GetOriginalPaymentVoidStatesAsync(d.Home, []));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.GetOriginalPaymentVoidStatesAsync(d.Home, [otherPayment.Id]));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.GetOriginalPaymentVoidStatesAsync(d.Home, [refund.Id]));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.GetOriginalPaymentVoidStatesAsync(d.Home, [Guid.NewGuid()]));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.GetOriginalPaymentVoidStatesAsync(Guid.NewGuid(), []));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.GetOriginalPaymentVoidStatesAsync(d.Home, [Guid.Empty]));
    }
}
