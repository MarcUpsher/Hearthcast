using Hearthcast.Data.Budgets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Hearthcast.Data.Tests.Budgets;

public sealed class BudgetSetupServiceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private BudgetSetupService Service => new(_database);
    private SpendingSetupService Spending => new(_database);
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    private sealed record Data(Guid Home, ExpenseCategory Category, Merchant Merchant, BudgetItem Item);
    private async Task<Data> SeedAsync()
    {
        var home = await new HouseholdService(_database).CreateAsync("Home", "Alex");
        var category = await Spending.CreateCategoryAsync(home.Id, " Utilities ");
        var merchant = await Spending.CreateMerchantAsync(home.Id, " Supplier ");
        var item = await Service.CreateItemAsync(home.Id, category.Id, " energy ", " Energy ", " gbp ",
            BudgetFrequency.Month, merchantId: merchant.Id, notes: " Usage ");
        return new(home.Id, category, merchant, item);
    }

    [Fact]
    public async Task SetupCanBeCreatedEditedClearedAndRetiredWithoutChangingCodeOrSavedPlansAndSpending()
    {
        var d = await SeedAsync();
        Assert.Equal("ENERGY", d.Item.Code);
        Assert.Equal("Energy", d.Item.Name);
        Assert.Equal("GBP", d.Item.CurrencyCode);
        Assert.Equal(1, d.Item.IntervalCount);
        Assert.Equal("Usage", d.Item.Notes);
        BudgetSnapshot original;
        SpendingRecord receipt;
        await using (var context = _database.CreateDbContext())
        {
            var home = await context.Households.SingleAsync();
            original = new BudgetSnapshot(Guid.NewGuid(), home, new(2026, 1, 1),
                [new(BudgetEntryMetadata.FromSetup(d.Item, d.Category, d.Merchant), 200, new(2025, 12, 28))], new string('a', 64));
            receipt = new SpendingRecord(Guid.NewGuid(), d.Merchant, new(2026, 1, 5), "GBP", [new(d.Category, 20)], new string('b', 64));
            context.AddRange(original, receipt); await context.SaveChangesAsync();
        }
        var category = await Spending.CreateCategoryAsync(d.Home, "Household bills");
        var merchant = await Spending.CreateMerchantAsync(d.Home, "Other provider");
        var changed = await Service.UpdateItemAsync(d.Home, d.Item.Id, category.Id, " Energy allowance ", " zar ",
            BudgetFrequency.Year, 3, merchant.Id, " Updated ");
        Assert.Equal(d.Item.Id, changed.Id);
        Assert.Equal("ENERGY", changed.Code);
        Assert.Equal("Energy allowance", changed.Name);
        Assert.Equal(category.Id, changed.ExpenseCategoryId);
        Assert.Equal(merchant.Id, changed.MerchantId);
        Assert.Equal("ZAR", changed.CurrencyCode);
        Assert.Equal(BudgetFrequency.Year, changed.Frequency);
        Assert.Equal(3, changed.IntervalCount);
        Assert.Equal("Updated", changed.Notes);
        changed = await Service.UpdateItemAsync(d.Home, d.Item.Id, category.Id, changed.Name, "ZAR", BudgetFrequency.Year, 3, merchant.Id, "Updated", new DateOnly(2026, 11, 25));
        await Service.SetItemRetiredAsync(d.Home, d.Item.Id, true);
        changed = await Service.UpdateItemAsync(d.Home, d.Item.Id, category.Id, changed.Name, "ZAR", BudgetFrequency.Week, 2, null, " \u2000 ");
        Assert.Equal(new DateOnly(2026, 11, 25), changed.PaymentAnchorDate);
        Assert.True(changed.IsRetired);
        Assert.Null(changed.MerchantId);
        Assert.Null(changed.Notes);
        Assert.Equal(changed.Id, (await Service.GetItemAsync(d.Home, changed.Id)).Id);
        await using var read = _database.CreateDbContext();
        var saved = await read.BudgetSnapshots.AsNoTracking().Include(s => s.Entries).SingleAsync();
        Assert.Equivalent(original, saved);
        Assert.Equivalent(receipt, await read.SpendingRecords.AsNoTracking().Include(r => r.Lines).SingleAsync());
    }

    [Fact]
    public async Task ListingsHaveStableNameCodeOrderAndExplicitItemRetirementFilteringInHouseholdScope()
    {
        var d = await SeedAsync();
        var foreign = await SeedAsync();
        var second = await Service.CreateItemAsync(d.Home, d.Category.Id, "ENERGY-2", "Energy", "GBP", BudgetFrequency.Month);
        var first = await Service.CreateItemAsync(d.Home, d.Category.Id, "BILLS", "Bills", "USD", BudgetFrequency.Year);
        await Service.SetItemRetiredAsync(d.Home, d.Item.Id, true);
        await Spending.SetCategoryRetiredAsync(d.Home, d.Category.Id, true);
        await Spending.SetMerchantRetiredAsync(d.Home, d.Merchant.Id, true);
        Assert.Equal(new[] { first.Id, d.Item.Id, second.Id }, (await Service.ListItemsAsync(d.Home)).Select(i => i.Id));
        Assert.Equal(new[] { first.Id, second.Id }, (await Service.ListItemsAsync(d.Home, false)).Select(i => i.Id));
        Assert.Equal(foreign.Item.Id, Assert.Single(await Service.ListItemsAsync(foreign.Home)).Id);
        await Service.SetItemRetiredAsync(d.Home, d.Item.Id, false);
        Assert.Equal(3, (await Service.ListItemsAsync(d.Home, false)).Count);
    }

    [Fact]
    public async Task EmptyInventoriesAreValidButMissingHouseholdsCannotBeReadOrWritten()
    {
        var home = await new HouseholdService(_database).CreateAsync("Home", "Alex");
        Assert.Empty(await Service.ListItemsAsync(home.Id));
        var missing = Guid.NewGuid();
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.ListItemsAsync(missing));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.GetItemAsync(missing, Guid.NewGuid()));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CreateItemAsync(missing, Guid.NewGuid(), "NEW", "New", "GBP", BudgetFrequency.Month));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.UpdateItemAsync(missing, Guid.NewGuid(), Guid.NewGuid(), "New", "GBP", BudgetFrequency.Month, 1, null, null));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.SetItemRetiredAsync(missing, Guid.NewGuid(), true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignAndMissingItemsAndSetupReferencesCannotBeUsedInAnotherHousehold(bool foreign)
    {
        var d = await SeedAsync();
        var other = await SeedAsync();
        var itemId = foreign ? other.Item.Id : Guid.NewGuid();
        var categoryId = foreign ? other.Category.Id : Guid.NewGuid();
        var merchantId = foreign ? other.Merchant.Id : Guid.NewGuid();
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.GetItemAsync(d.Home, itemId));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.SetItemRetiredAsync(d.Home, itemId, true));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.UpdateItemAsync(d.Home, itemId, d.Category.Id, "Changed", "USD", BudgetFrequency.Year, 3, null, null));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CreateItemAsync(d.Home, categoryId, "NEW", "New", "GBP", BudgetFrequency.Month));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CreateItemAsync(d.Home, d.Category.Id, "NEW", "New", "GBP", BudgetFrequency.Month, merchantId: merchantId));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.UpdateItemAsync(d.Home, d.Item.Id, categoryId, "Changed", "USD", BudgetFrequency.Year, 3, null, null));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.UpdateItemAsync(d.Home, d.Item.Id, d.Category.Id, "Changed", "USD", BudgetFrequency.Year, 3, merchantId, null));
        Assert.Equivalent(d.Item, await Service.GetItemAsync(d.Home, d.Item.Id));
        Assert.Equivalent(other.Item, await Service.GetItemAsync(other.Home, other.Item.Id));
        Assert.Single(await Service.ListItemsAsync(d.Home));
        Assert.Single(await Service.ListItemsAsync(other.Home));
    }

    [Fact]
    public async Task EmptyMerchantIdentifierIsMissingRatherThanAnInstructionToClearIt()
    {
        var d = await SeedAsync();
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CreateItemAsync(d.Home, d.Category.Id, "NEW", "New", "GBP", BudgetFrequency.Month, merchantId: Guid.Empty));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.UpdateItemAsync(d.Home, d.Item.Id, d.Category.Id, "Changed", "GBP", BudgetFrequency.Month, 1, Guid.Empty, null));
        Assert.Equivalent(d.Item, await Service.GetItemAsync(d.Home, d.Item.Id));
    }

    [Fact]
    public async Task RetiredSetupIsExplicitlyUsableAndCodesStayReservedSeparatelyFromSpendingCodes()
    {
        var d = await SeedAsync();
        await Spending.SetCategoryRetiredAsync(d.Home, d.Category.Id, true);
        await Spending.SetMerchantRetiredAsync(d.Home, d.Merchant.Id, true);
        await Service.SetItemRetiredAsync(d.Home, d.Item.Id, true);
        var old = await Service.CreateItemAsync(d.Home, d.Category.Id, "OLD", "Historical", "GBP", BudgetFrequency.Month, merchantId: d.Merchant.Id);
        await Service.UpdateItemAsync(d.Home, old.Id, d.Category.Id, "Historical edited", "GBP", BudgetFrequency.Month, 1, d.Merchant.Id, null);
        var error = await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Service.CreateItemAsync(d.Home, d.Category.Id, " eNeRgY ", "Duplicate", "GBP", BudgetFrequency.Week));
        Assert.Contains("including retired", error.Message);
        var spendingItem = await Spending.CreateItemAsync(d.Home, d.Category.Id, "ENERGY", "Meter credit", "each", "Each");
        Assert.Equal(d.Item.Code, spendingItem.Code);
        Assert.True((await Service.GetItemAsync(d.Home, d.Item.Id)).IsRetired);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("longCode")]
    [InlineData("name")]
    [InlineData("currency")]
    [InlineData("currencyShape")]
    [InlineData("frequency")]
    [InlineData("interval")]
    [InlineData("notes")]
    public async Task InvalidCreationDoesNotPersistAnySetup(string field)
    {
        var d = await SeedAsync();
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateItemAsync(d.Home, d.Category.Id,
            field == "code" ? "bad code" : field == "longCode" ? new string('A', 65) : "NEW",
            field == "name" ? "\u00a0\u2000" : "New", field == "currency" ? "ZZZ" : field == "currencyShape" ? "GB1" : "USD",
            field == "frequency" ? (BudgetFrequency)0 : BudgetFrequency.Year, field == "interval" ? 0 : 3,
            d.Merchant.Id, field == "notes" ? "bad\0notes" : "New notes"));
        Assert.Single(await Service.ListItemsAsync(d.Home));
        Assert.Equivalent(d.Item, await Service.GetItemAsync(d.Home, d.Item.Id));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("currency")]
    [InlineData("frequency")]
    [InlineData("interval")]
    [InlineData("notes")]
    public async Task InvalidUpdatesRollBackAllEarlierChanges(string field)
    {
        var d = await SeedAsync();
        var category = await Spending.CreateCategoryAsync(d.Home, "Other");
        var merchant = await Spending.CreateMerchantAsync(d.Home, "Other supplier");
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.UpdateItemAsync(d.Home, d.Item.Id, category.Id,
            field == "name" ? " " : "Changed", field == "currency" ? "ZZZ" : "USD",
            field == "frequency" ? (BudgetFrequency)4 : BudgetFrequency.Year, field == "interval" ? -1 : 3,
            merchant.Id, field == "notes" ? "bad\0notes" : "Changed notes"));
        Assert.Equivalent(d.Item, await Service.GetItemAsync(d.Home, d.Item.Id));
    }

    [Fact]
    public async Task MaximumCodesAndLargeRecurrenceIntervalsRoundTripWithAnOptionalMerchant()
    {
        var d = await SeedAsync();
        var item = await Service.CreateItemAsync(d.Home, d.Category.Id, new string('a', 64), "Allowance", "JPY", BudgetFrequency.Week, int.MaxValue);
        var saved = await Service.GetItemAsync(d.Home, item.Id);
        Assert.Equal(new string('A', 64), saved.Code);
        Assert.Equal(int.MaxValue, saved.IntervalCount);
        Assert.Null(saved.MerchantId);
        Assert.Null(saved.Notes);
        Assert.Equal("JPY", saved.CurrencyCode);
    }

    [Fact]
    public async Task HistoricalSetupCurrencyCanBeRetainedDuringEditsButNotSelectedForANewItemOrChangedCode()
    {
        var d = await SeedAsync();
        await using (var context = _database.CreateDbContext())
            await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "BudgetItems" SET "CurrencyCode" = {"ZZZ"} WHERE "Id" = {d.Item.Id}""");
        var changed = await Service.UpdateItemAsync(d.Home, d.Item.Id, d.Category.Id, "Historical allowance", " zzz ", BudgetFrequency.Year, 2, null, null);
        Assert.Equal("ZZZ", changed.CurrencyCode);
        Assert.Equal("Historical allowance", changed.Name);
        Assert.Equal(BudgetFrequency.Year, changed.Frequency);
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateItemAsync(d.Home, d.Category.Id, "NEW", "New", "ZZZ", BudgetFrequency.Month));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.UpdateItemAsync(d.Home, d.Item.Id, d.Category.Id, "Invalid", "YYY", BudgetFrequency.Month, 1, null, null));
        Assert.Equivalent(changed, await Service.GetItemAsync(d.Home, d.Item.Id));
        var supported = await Service.UpdateItemAsync(d.Home, d.Item.Id, d.Category.Id, "Current allowance", "USD", BudgetFrequency.Month, 1, null, null);
        Assert.Equal("USD", supported.CurrencyCode);
    }

    [Fact]
    public async Task ConcurrentDuplicateCreatesReturnOneBusinessConflictAndSaveOneItem()
    {
        var d = await SeedAsync();
        var barrier = new ConcurrentSaveBarrier();
        var service = new BudgetSetupService(new InterceptingContextFactory(_database, barrier));
        var errors = await Task.WhenAll(new[] { "new", " NEW " }.Select(code => Record.ExceptionAsync(() =>
            service.CreateItemAsync(d.Home, d.Category.Id, code, "New", "GBP", BudgetFrequency.Month))));
        Assert.Null(Assert.Single(errors, e => e is null));
        Assert.IsType<BusinessRuleViolationException>(Assert.Single(errors, e => e is not null));
        Assert.Equal(1, (await Service.ListItemsAsync(d.Home)).Count(i => i.Code == "NEW"));
    }

    [Fact]
    public async Task ConcurrentMetadataAndRetirementEditsReloadOnRetryWithoutLosingEitherChange()
    {
        var d = await SeedAsync();
        var barrier = new ConcurrentSaveBarrier();
        var service = new BudgetSetupService(new InterceptingContextFactory(_database, barrier));
        await Task.WhenAll(
            service.UpdateItemAsync(d.Home, d.Item.Id, d.Category.Id, "Edited", "USD", BudgetFrequency.Year, 3, null, "Edited notes"),
            service.SetItemRetiredAsync(d.Home, d.Item.Id, true));
        var saved = await Service.GetItemAsync(d.Home, d.Item.Id);
        Assert.True(saved.IsRetired);
        Assert.Equal("Edited", saved.Name);
        Assert.Equal("USD", saved.CurrencyCode);
        Assert.Null(saved.MerchantId);
        Assert.Equal("Edited notes", saved.Notes);
    }

    [Fact]
    public async Task DatabaseFailureRollsBackAllSetupEditsAndIsNotMisreportedAsADuplicateCode()
    {
        var d = await SeedAsync();
        await using var context = _database.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION "FailBudgetSetupUpdate"() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE unique_violation USING MESSAGE = 'Synthetic unrelated constraint', CONSTRAINT = 'UnrelatedBudgetUnique'; END; $$;
            CREATE TRIGGER "FailBudgetSetupUpdate" BEFORE UPDATE ON "BudgetItems"
            FOR EACH ROW EXECUTE FUNCTION "FailBudgetSetupUpdate"();
            """);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => Service.UpdateItemAsync(d.Home, d.Item.Id, d.Category.Id,
            "Changed", "USD", BudgetFrequency.Year, 3, null, null));
        Assert.Equal("UnrelatedBudgetUnique", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
        Assert.Equivalent(d.Item, await Service.GetItemAsync(d.Home, d.Item.Id));
    }

    [Fact]
    public async Task CancellationDoesNotCreateOrUpdateOrRetireItems()
    {
        var d = await SeedAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.CreateItemAsync(d.Home, d.Category.Id, "NEW", "New", "GBP", BudgetFrequency.Month, cancellationToken: cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.UpdateItemAsync(d.Home, d.Item.Id, d.Category.Id, "Changed", "USD", BudgetFrequency.Year, 3, null, null, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.SetItemRetiredAsync(d.Home, d.Item.Id, true, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.ListItemsAsync(d.Home, cancellationToken: cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.GetItemAsync(d.Home, d.Item.Id, cancellation.Token));
        Assert.Single(await Service.ListItemsAsync(d.Home));
        Assert.Equivalent(d.Item, await Service.GetItemAsync(d.Home, d.Item.Id));
    }
    [Fact]
    public async Task PaymentDateRoundTripsCanBeClearedAndIsPreservedByOlderEdits()
    {
        var d = await SeedAsync();
        var date = new DateOnly(2026, 11, 19);
        var created = await Service.CreateItemAsync(d.Home, d.Category.Id, "SCHEDULED", "Scheduled", "GBP", BudgetFrequency.Month, 1, null, null, date);
        Assert.Equal(date, (await Service.GetItemAsync(d.Home, created.Id)).PaymentAnchorDate);
        var changed = await Service.UpdateItemAsync(d.Home, created.Id, d.Category.Id, "Changed", "GBP", BudgetFrequency.Week, 2, null, null);
        Assert.Equal(date, changed.PaymentAnchorDate);
        changed = await Service.UpdateItemAsync(d.Home, created.Id, d.Category.Id, "Changed", "GBP", BudgetFrequency.Month, 1, null, null, new DateOnly(2024, 2, 29));
        Assert.Equal(new DateOnly(2024, 2, 29), (await Service.GetItemAsync(d.Home, created.Id)).PaymentAnchorDate);
        changed = await Service.UpdateItemAsync(d.Home, created.Id, d.Category.Id, "Changed", "GBP", BudgetFrequency.Month, 1, null, null, (DateOnly?)null);
        Assert.Null(changed.PaymentAnchorDate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonFinitePaymentDatesRejectCreationAndRollBackOtherEditedDefaults(bool maximum)
    {
        var d = await SeedAsync(); var date = maximum ? DateOnly.MaxValue : DateOnly.MinValue;
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateItemAsync(d.Home, d.Category.Id, "BAD-DATE", "Invalid", "GBP", BudgetFrequency.Month, 1, null, null, date));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.UpdateItemAsync(d.Home, d.Item.Id, d.Category.Id, "Changed", "USD", BudgetFrequency.Year, 3, null, "Changed", date));
        Assert.Equivalent(d.Item, await Service.GetItemAsync(d.Home, d.Item.Id)); Assert.Single(await Service.ListItemsAsync(d.Home));
    }

    [Theory]
    [InlineData("0001-01-01")]
    [InlineData("9999-12-31")]
    [InlineData("infinity")]
    [InlineData("-infinity")]
    public async Task DatabaseRejectsNonFiniteOrReservedPaymentDates(string date)
    {
        var d = await SeedAsync(); await using var context = _database.CreateDbContext();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "BudgetItems" SET "PaymentAnchorDate" = {date}::date WHERE "Id" = {d.Item.Id}"""));
        Assert.Equal("CK_BudgetItems_PaymentAnchorDate", error.ConstraintName);
        Assert.Null((await Service.GetItemAsync(d.Home, d.Item.Id)).PaymentAnchorDate);
    }

    [Fact]
    public async Task PaymentDateMigrationPreservesExistingOutgoingRows()
    {
        var d = await SeedAsync(); await using var context = _database.CreateDbContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261117190213_AddBudgetPlans"); await migrator.MigrateAsync();
        Assert.Equivalent(d.Item, await Service.GetItemAsync(d.Home, d.Item.Id));
        Assert.False(context.Database.HasPendingModelChanges());
    }

}
