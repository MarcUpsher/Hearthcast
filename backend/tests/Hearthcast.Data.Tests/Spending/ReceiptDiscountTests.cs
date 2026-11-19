using System.Data;
using System.Data.Common;
using Hearthcast.Data.Households;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Hearthcast.Data.Tests.Spending;

public sealed class ReceiptDiscountTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase database = new();
    private SpendingRecordService Records => new(database);
    private SpendingSetupService Setup => new(database);
    private SpendingCategorySummaryService Summary => new(database);
    private static readonly DateOnly Today = new(2026, 11, 19);
    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();
    private sealed record Data(Guid Home, Merchant Merchant, ExpenseCategory Food, ExpenseCategory Cleaning, SpendingItem Item)
    {
        internal SpendingRecordValues Values => new(Merchant.Id, Today, "GBP", [new(Food.Id, 21.95m, Item.Id, 1, 21.95m)], ReceiptTotal: 19.85m,
            Discounts: [new(Food.Id, 2.10m, "£8 Stir Fry Dine In")]);
    }
    private async Task<Data> Seed()
    {
        var home = await new HouseholdService(database).CreateAsync("Home", "Alex");
        var merchant = await Setup.CreateMerchantAsync(home.Id, "Waitrose");
        var food = await Setup.CreateCategoryAsync(home.Id, "Food"); var cleaning = await Setup.CreateCategoryAsync(home.Id, "Cleaning");
        var item = await Setup.CreateItemAsync(home.Id, food.Id, "BANANAS", "Organic Bananas", "pack", "Pack");
        return new(home.Id, merchant, food, cleaning, item);
    }
    [Fact]
    public async Task ReceiptDiscountReconcilesCategoryAndRecordWithoutChangingPrintedOrRememberedPrices()
    {
        var d = await Seed(); var usual = await Setup.CreateUsualPriceAsync(d.Home, d.Item.Id, d.Merchant.Id, "GBP", 21.95m);
        var record = (await Records.CreateAsync(d.Home, Guid.NewGuid(), d.Values)).Record;
        Assert.Equal(21.95m, record.GrossAmount); Assert.Equal(2.10m, record.DiscountAmount); Assert.Equal(19.85m, record.TotalAmount);
        Assert.Equal("Reconciled", record.ReconciliationStatus); Assert.Equal(0, record.ReceiptDifference);
        Assert.Equal(21.95m, record.Lines[0].NetAmount); Assert.Equal(21.95m, record.Lines[0].UnitPrice);
        Assert.Equal(usual.Revision, (await Setup.GetUsualPriceAsync(d.Home, usual.Id)).Revision);
        var report = await Summary.GetAsync(d.Home, Today, Today); Assert.Equal(19.85m, report.Categories.Single().NetAmount); Assert.Empty(report.Warnings);
        var paid = await Records.FindLastPaidAsync(d.Home, d.Item.Id, d.Merchant.Id, "GBP", Today);
        Assert.True(paid!.HasReceiptDiscount); Assert.Equal(21.95m, paid.UnitPrice);
        Assert.Equal(record.Id, (await Records.ListPageAsync(d.Home)).Records.Single().Id);
        Assert.Equal(2.10m, (await Records.GetAsync(d.Home, record.Id)).Discounts.Single().Amount);
    }
    [Fact]
    public async Task MultipleCategoriesRefundsVoidsAndDateRangesUseSignedNetAmounts()
    {
        var d = await Seed();
        var values = d.Values with { Lines = [new(d.Food.Id, 20), new(d.Cleaning.Id, 10)], ReceiptTotal = 25,
            Discounts = [new(d.Food.Id, 2), new(d.Food.Id, 1), new(d.Cleaning.Id, 2)] };
        var payment = (await Records.CreateAsync(d.Home, Guid.NewGuid(), values)).Record;
        await Records.CreateAsync(d.Home, Guid.NewGuid(), values with { SpendingDate = Today.AddDays(1), Kind = SpendingRecordKind.Refund, OriginalPaymentId = payment.Id });
        var today = await Summary.GetAsync(d.Home, Today, Today); Assert.Equal(17, today.Categories.Single(c => c.ExpenseCategoryId == d.Food.Id).NetAmount);
        Assert.Equal(8, today.Categories.Single(c => c.ExpenseCategoryId == d.Cleaning.Id).NetAmount);
        var both = await Summary.GetAsync(d.Home, Today, Today.AddDays(1)); Assert.All(both.Categories, c => Assert.Equal(0, c.NetAmount));
        await Records.VoidAsync(d.Home, payment.Id, payment.Revision); var remaining = await Summary.GetAsync(d.Home, Today, Today.AddDays(1));
        Assert.Equal(-17, remaining.Categories.Single(c => c.ExpenseCategoryId == d.Food.Id).NetAmount);
    }
    [Fact]
    public async Task DiscountCorrectionPreservesLinesLabelsUsualPriceSourcesAndLegacyCreateReplay()
    {
        var d = await Seed(); var request = d.Values with { Discounts = null }; var id = Guid.NewGuid();
        var original = (await Records.CreateAsync(d.Home, id, request, [new(1, 21.95m)])).Record;
        var price = (await Setup.ListUsualPricesAsync(d.Home, d.Item.Id)).Single();
        await Setup.RenameCategoryAsync(d.Home, d.Food.Id, "Groceries");
        var updated = await Records.CorrectDiscountsAsync(d.Home, id, original.Revision, [new(d.Food.Id, 2.10m, "Bundle")]);
        Assert.Equal(original.Lines[0].Id, updated.Lines[0].Id); Assert.Equal("Food", updated.Lines[0].ExpenseCategoryName);
        Assert.Equal("Groceries", updated.Discounts[0].ExpenseCategoryName); Assert.Equal(original.CreationRequestHash, updated.CreationRequestHash);
        Assert.Equal(price.SourceSpendingLineId, (await Setup.GetUsualPriceAsync(d.Home, price.Id)).SourceSpendingLineId);
        Assert.False((await Records.CreateAsync(d.Home, id, request, [new(1, 21.95m)])).WasCreated);
        await Assert.ThrowsAsync<SpendingRecordConflictException>(() => Records.CorrectDiscountsAsync(d.Home, id, original.Revision, []));
        var cleared = await Records.CorrectDiscountsAsync(d.Home, id, updated.Revision, []); Assert.Empty(cleared.Discounts); Assert.Equal(21.95m, cleared.TotalAmount);
    }
    [Fact]
    public async Task VersionTwoReplayAndLegacyReplacementPreserveDiscountsUntilExplicitlyCleared()
    {
        var d = await Seed(); var original = (await Records.CreateAsync(d.Home, Guid.NewGuid(), d.Values)).Record;
        var updated = await Records.ReplaceAsync(d.Home, original.Id, original.Revision, d.Merchant.Id, Today, d.Values.Lines, 19.85m, null, "Changed");
        Assert.Equal(2.10m, updated.DiscountAmount); Assert.Equal(original.Discounts[0].Id, updated.Discounts[0].Id);
        var replay = await Records.CreateAsync(d.Home, original.Id, d.Values); Assert.False(replay.WasCreated); Assert.Equal(updated.Revision, replay.Record.Revision);
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Records.CreateAsync(d.Home, original.Id, d.Values with { Discounts = [] }));
        var cleared = await Records.ReplaceAsync(d.Home, original.Id, updated.Revision, d.Merchant.Id, Today, d.Values.Lines, null, null, null, discounts: []);
        Assert.Empty(cleared.Discounts);
    }
    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(22)] [InlineData(0.001)]
    public async Task InvalidDiscountOrCategoryOverdrawCannotPartiallyWrite(decimal amount)
    {
        var d = await Seed(); var original = (await Records.CreateAsync(d.Home, Guid.NewGuid(), d.Values)).Record;
        await Assert.ThrowsAsync<InvalidValueException>(() => Records.CorrectDiscountsAsync(d.Home, original.Id, original.Revision, [new(d.Food.Id, amount)]));
        Assert.Equal(original.Revision, (await Records.GetAsync(d.Home, original.Id)).Revision);
        await Assert.ThrowsAsync<InvalidValueException>(() => Records.CorrectLineAsync(d.Home, original.Id, original.Revision, original.Lines[0].Id, 1, 1, 1, PromotionStatus.Unknown, null));
        Assert.Equal(21.95m, (await Records.GetAsync(d.Home, original.Id)).GrossAmount);
    }
    [Fact]
    public async Task CategoryMustBePresentAndScopedAndFailedReplacementRollsBackPriceDetachment()
    {
        var d = await Seed(); var foreign = await Seed();
        var original = (await Records.CreateAsync(d.Home, Guid.NewGuid(), d.Values, [new(1, 21.95m)])).Record;
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Records.CorrectDiscountsAsync(d.Home, original.Id, original.Revision, [new(foreign.Food.Id, 1)]));
        await Assert.ThrowsAsync<InvalidValueException>(() => Records.CorrectDiscountsAsync(d.Home, original.Id, original.Revision, [new(d.Cleaning.Id, 1)]));
        await Assert.ThrowsAsync<InvalidValueException>(() => Records.ReplaceAsync(d.Home, original.Id, original.Revision, d.Merchant.Id, Today, [new(d.Cleaning.Id, 1)], null, null, null));
        var saved = await Records.GetAsync(d.Home, original.Id); Assert.Equal(original.Revision, saved.Revision);
        Assert.Equal(original.Lines[0].Id, (await Setup.ListUsualPricesAsync(d.Home, d.Item.Id)).Single().SourceSpendingLineId);
        await Records.VoidAsync(d.Home, original.Id, original.Revision);
        var voided = await Records.GetAsync(d.Home, original.Id);
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Records.CorrectDiscountsAsync(d.Home, original.Id, voided.Revision, []));
    }
    [Fact]
    public async Task MigrationRoundTripKeepsOldReceiptsAndMatchingCreateHashes()
    {
        var d = await Seed(); var request = d.Values with { Discounts = null };
        var original = (await Records.CreateAsync(d.Home, Guid.NewGuid(), request)).Record;
        await using (var context = database.CreateDbContext()) {
            var migrator = context.GetService<IMigrator>(); await migrator.MigrateAsync("20261119133802_AddBudgetAllowances"); await migrator.MigrateAsync();
            Assert.False(context.Database.HasPendingModelChanges());
        }
        var replay = await Records.CreateAsync(d.Home, original.Id, request); Assert.False(replay.WasCreated); Assert.Equivalent(original, replay.Record);
    }
    [Theory]
    [InlineData("UPDATE \"SpendingDiscounts\" SET \"Amount\" = -1")]
    [InlineData("UPDATE \"SpendingDiscounts\" SET \"Amount\" = 0.001")]
    [InlineData("UPDATE \"SpendingDiscounts\" SET \"Position\" = 0")]
    [InlineData("UPDATE \"SpendingDiscounts\" SET \"CurrencyCode\" = 'USD'")]
    public async Task PersistenceEnforcesAmountPositionAndParentCurrency(string sql)
    {
        var d = await Seed(); await Records.CreateAsync(d.Home, Guid.NewGuid(), d.Values);
        await using var context = database.CreateDbContext(); await Assert.ThrowsAsync<Npgsql.PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql));
    }

    [Theory]
    [InlineData("detail")] [InlineData("page")] [InlineData("list")] [InlineData("summary")]
    public async Task SplitReadsAvoidCollectionMultiplicationAndKeepOneSnapshotAcrossConcurrentCorrection(string read)
    {
        var d = await Seed();
        var values = d.Values with { Lines = Enumerable.Range(0, 20).Select(_ => new SpendingLineValues(d.Food.Id, 2m)).ToArray(),
            Discounts = Enumerable.Range(0, 10).Select(_ => new SpendingDiscountValues(d.Food.Id, 1m)).ToArray(), ReceiptTotal = 30m };
        var original = (await Records.CreateAsync(d.Home, Guid.NewGuid(), values)).Record;
        // After parent rows have been read, replace the discount rows in another transaction.
        var interceptor = new SplitReadProbe(async () => {
            await Records.CorrectDiscountsAsync(d.Home, original.Id, original.Revision, [new(d.Food.Id, 5m)]);
        });
        var factory = new InterceptingContextFactory(database, interceptor);
        var records = new SpendingRecordService(factory);
        if (read == "summary") {
            var report = await new SpendingCategorySummaryService(factory).GetAsync(d.Home, Today, Today);
            Assert.Equal(30m, report.Categories.Single().NetAmount);
            Assert.Equal(1, report.ReconciliationRecordCounts["Reconciled"]);
            Assert.Empty(report.Warnings);
        } else {
            var record = read switch {
                "detail" => await records.GetAsync(d.Home, original.Id),
                "page" => (await records.ListPageAsync(d.Home, pageSize: 1)).Records.Single(),
                _ => (await records.ListAsync(d.Home)).Single()
            };
            Assert.Equal(original.Revision, record.Revision);
            Assert.Equal(20, record.Lines.Count); Assert.Equal(10, record.Discounts.Count);
            Assert.Equal(30m, record.TotalAmount); Assert.Equal("Reconciled", record.ReconciliationStatus);
        }
        Assert.True(interceptor.Corrected);
        Assert.Equal(3, interceptor.RecordQueries.Count);
        Assert.DoesNotContain(interceptor.RecordQueries, sql => sql.Contains("JOIN \"SpendingLines\"") && sql.Contains("JOIN \"SpendingDiscounts\""));
        Assert.Equal(35m, (await Records.GetAsync(d.Home, original.Id)).TotalAmount);
    }

    private sealed class SplitReadProbe(Func<Task> correct) : DbCommandInterceptor
    {
        internal List<string> RecordQueries { get; } = [];
        internal bool Corrected { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"SpendingRecords\"")) {
                Assert.Equal(IsolationLevel.RepeatableRead, command.Transaction!.IsolationLevel);
                RecordQueries.Add(command.CommandText);
                if (RecordQueries.Count == 2) { await correct(); Corrected = true; }
            }
            return result;
        }
    }
}
