using System.Data.Common;
using Hearthcast.Data.Households;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hearthcast.Data.Tests.Spending;

public sealed class SpendingCategorySummaryServiceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private SpendingCategorySummaryService Summary => new(_database);
    private SpendingRecordService Records => new(_database);
    private SpendingSetupService Setup => new(_database);
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    private sealed record Data(Guid Home, ExpenseCategory Food, ExpenseCategory Dining, Merchant Merchant);

    private async Task<Data> SeedAsync()
    {
        var home = await new HouseholdService(_database).CreateAsync("Home", "Alex");
        return new(home.Id, await Setup.CreateCategoryAsync(home.Id, "Food"),
            await Setup.CreateCategoryAsync(home.Id, "Eating Out"),
            await Setup.CreateMerchantAsync(home.Id, "Shop"));
    }

    private async Task<SpendingRecord> CaptureAsync(Data data, DateOnly date,
        IReadOnlyList<SpendingLineValues> lines, string currency = "GBP",
        SpendingRecordKind kind = SpendingRecordKind.Payment, Guid? original = null,
        decimal? receipt = null, Guid? id = null) =>
        (await Records.CreateAsync(data.Home, id ?? Guid.NewGuid(),
            new(data.Merchant.Id, date, currency, lines, kind, original, receipt))).Record;

    [Fact]
    public async Task ArbitraryRangeIncludesBothBoundariesAndExcludesDatesOnEitherSide()
    {
        var data = await SeedAsync();
        var from = new DateOnly(2025, 12, 28);
        var to = new DateOnly(2026, 1, 10);
        foreach (var (date, amount) in new[]
        {
            (from.AddDays(-1), 100m), (from, 10m),
            (new DateOnly(2026, 1, 1), 2m), (to, 3m), (to.AddDays(1), 200m)
        })
            await CaptureAsync(data, date, [new(data.Food.Id, amount)]);

        var report = await Summary.GetAsync(data.Home, from, to);

        Assert.Equal(data.Home, report.HouseholdId);
        Assert.Equal(from, report.From);
        Assert.Equal(to, report.To);
        Assert.Equal(15m, Assert.Single(report.Categories).NetAmount);
        Assert.Equal(3, report.ReconciliationRecordCounts["NotChecked"]);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public async Task SingleDayRangeCanSelectLeapDay()
    {
        var data = await SeedAsync();
        var day = new DateOnly(2024, 2, 29);
        await CaptureAsync(data, day.AddDays(-1), [new(data.Food.Id, 100m)]);
        await CaptureAsync(data, day, [new(data.Food.Id, 4m)]);
        await CaptureAsync(data, day.AddDays(1), [new(data.Food.Id, 200m)]);

        var report = await Summary.GetAsync(data.Home, day, day);

        Assert.Equal(4m, Assert.Single(report.Categories).NetAmount);
        Assert.Equal(1, report.ReconciliationRecordCounts["NotChecked"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RejectsDefaultDatesAndReversedRanges(int scenario)
    {
        var data = await SeedAsync();
        var from = scenario == 0 ? default : new DateOnly(2026, 1, 2);
        var to = scenario == 1 ? default : new DateOnly(2026, 1, 1);
        await Assert.ThrowsAsync<InvalidValueException>(() => Summary.GetAsync(data.Home, from, to));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsUnknownAndEmptyHouseholdIdentifiers(bool empty)
    {
        var day = new DateOnly(2026, 1, 1);
        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            Summary.GetAsync(empty ? Guid.Empty : Guid.NewGuid(), day, day));
    }

    [Fact]
    public async Task KnownHouseholdWithNoEligibleRecordsHasEmptyTotalsAndZeroReconciliationCounts()
    {
        var data = await SeedAsync();
        var day = new DateOnly(2026, 1, 1);
        await CaptureAsync(data, day.AddDays(-1), [new(data.Food.Id, 10m)], receipt: 20m);

        var report = await Summary.GetAsync(data.Home, day, day);

        Assert.Empty(report.Categories);
        Assert.Empty(report.Warnings);
        Assert.Equal(3, report.ReconciliationRecordCounts.Count);
        Assert.All(report.ReconciliationRecordCounts.Values, count => Assert.Equal(0, count));
    }

    [Fact]
    public async Task OtherHouseholdSpendingAndWarningsAreExcluded()
    {
        var selected = await SeedAsync();
        var other = await SeedAsync();
        var day = new DateOnly(2026, 1, 1);
        await CaptureAsync(selected, day, [new(selected.Food.Id, 5m)], receipt: 5m);
        await CaptureAsync(other, day, [new(other.Food.Id, 100m)], receipt: 200m);

        var report = await Summary.GetAsync(selected.Home, day, day);

        var row = Assert.Single(report.Categories);
        Assert.Equal(selected.Food.Id, row.ExpenseCategoryId);
        Assert.Equal(5m, row.NetAmount);
        Assert.Empty(report.Warnings);
        Assert.Equal(1, report.ReconciliationRecordCounts["Reconciled"]);
        Assert.Equal(0, report.ReconciliationRecordCounts["Unreconciled"]);
    }

    [Fact]
    public async Task QuickSplitAndItemisedCaptureUseSavedNetAmountsIncludingOverridesAndDuplicateItems()
    {
        var data = await SeedAsync();
        var day = new DateOnly(2026, 1, 1);
        var item = await Setup.CreateItemAsync(data.Home, data.Food.Id, "EGGS", "Eggs", "pack-6", "Six eggs");
        await Setup.CreateUsualPriceAsync(data.Home, item.Id, data.Merchant.Id, "GBP", 9m);
        await CaptureAsync(data, day, [new(data.Food.Id, 5m)]);
        await CaptureAsync(data, day, [new(data.Food.Id, 7m), new(data.Dining.Id, 8m)], receipt: 15m);
        await CaptureAsync(data, day,
            [new(data.Food.Id, 3.25m, item.Id, 2m, 3m), new(data.Food.Id, 1.75m, item.Id, 1m, 3m)], receipt: 5m);

        var report = await Summary.GetAsync(data.Home, day, day);

        Assert.Collection(report.Categories,
            row => { Assert.Equal(data.Dining.Id, row.ExpenseCategoryId); Assert.Equal(8m, row.NetAmount); },
            row => { Assert.Equal(data.Food.Id, row.ExpenseCategoryId); Assert.Equal(17m, row.NetAmount); });
        Assert.Empty(report.Warnings);
        Assert.Equal(1, report.ReconciliationRecordCounts["NotChecked"]);
        Assert.Equal(2, report.ReconciliationRecordCounts["Reconciled"]);
    }

    [Fact]
    public async Task CurrencyGroupsStaySeparateAndKeepExactAmountsAndMinorUnits()
    {
        var data = await SeedAsync();
        var day = new DateOnly(2026, 1, 1);
        await CaptureAsync(data, day, [new(data.Food.Id, 0.10m), new(data.Food.Id, 0.20m)]);
        await CaptureAsync(data, day, [new(data.Food.Id, 7.01m)], "EUR");
        await CaptureAsync(data, day, [new(data.Food.Id, 9m)], "JPY");

        var report = await Summary.GetAsync(data.Home, day, day);

        Assert.Collection(report.Categories,
            row => { Assert.Equal("EUR", row.CurrencyCode); Assert.Equal(2, row.CurrencyMinorUnits); Assert.Equal(7.01m, row.NetAmount); },
            row => { Assert.Equal("GBP", row.CurrencyCode); Assert.Equal(2, row.CurrencyMinorUnits); Assert.Equal(0.30m, row.NetAmount); },
            row => { Assert.Equal("JPY", row.CurrencyCode); Assert.Equal(0, row.CurrencyMinorUnits); Assert.Equal(9m, row.NetAmount); });
        Assert.All(report.Categories, row => Assert.Equal(data.Food.Id, row.ExpenseCategoryId));
    }

    [Theory]
    [InlineData(2, 3)]
    [InlineData(3, 2)]
    public async Task HistoricalPrecisionsShareOneCategoryCurrencyTotalWithoutRoundingOrSplittingLabels(
        int firstPrecision, int secondPrecision)
    {
        var data = await SeedAsync();
        var day = new DateOnly(2026, 1, 1);

        async Task SaveHistoricalAsync(ExpenseCategory category, DateOnly date, int precision, decimal amount)
        {
            // Simulate a historical import under a different currency policy. All schema checks/FKs remain enabled.
            var record = new SpendingRecord(Guid.NewGuid(), data.Merchant, date, "GBP",
                [new(category, 0m)], new string('0', 64), receiptTotal: 0m);
            await using var context = _database.CreateDbContext();
            context.Entry(record).Property(r => r.CurrencyMinorUnits).CurrentValue = precision;
            context.Entry(record).Property(r => r.ReceiptTotal).CurrentValue = amount;
            var line = Assert.Single(record.Lines);
            context.Entry(line).Property(l => l.CurrencyMinorUnits).CurrentValue = precision;
            context.Entry(line).Property(l => l.NetAmount).CurrentValue = amount;
            context.SpendingRecords.Add(record);
            await context.SaveChangesAsync();
        }

        await SaveHistoricalAsync(data.Food, day, firstPrecision, firstPrecision == 2 ? 1.25m : 1.255m);
        var renamed = await Setup.RenameCategoryAsync(data.Home, data.Food.Id, "Groceries");
        await SaveHistoricalAsync(renamed, day.AddDays(1), secondPrecision, secondPrecision == 2 ? 2.34m : 2.345m);

        var report = await Summary.GetAsync(data.Home, day, day.AddDays(1));

        var row = Assert.Single(report.Categories);
        Assert.Equal(data.Food.Id, row.ExpenseCategoryId);
        Assert.Equal("GBP", row.CurrencyCode);
        Assert.Equal("Groceries", row.CategoryName);
        Assert.Equal(3, row.CurrencyMinorUnits);
        Assert.Equal(3.595m, row.NetAmount);
        Assert.Equal(2, report.ReconciliationRecordCounts["Reconciled"]);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public async Task VoidsAreExcludedButRefundsCountOnTheirOwnDateEvenAfterOriginalIsVoided()
    {
        var data = await SeedAsync();
        var day = new DateOnly(2026, 1, 1);
        var original = await CaptureAsync(data, day.AddDays(-1), [new(data.Food.Id, 20m)]);
        await CaptureAsync(data, day, [new(data.Food.Id, 5m)], kind: SpendingRecordKind.Refund, original: original.Id);
        await Records.VoidAsync(data.Home, original.Id, original.Revision);
        var voided = await CaptureAsync(data, day, [new(data.Food.Id, 100m)], receipt: 200m);
        await Records.VoidAsync(data.Home, voided.Id, voided.Revision);
        var voidedRefund = await CaptureAsync(data, day, [new(data.Food.Id, 30m)], kind: SpendingRecordKind.Refund, receipt: 40m);
        await Records.VoidAsync(data.Home, voidedRefund.Id, voidedRefund.Revision);
        var payment = await CaptureAsync(data, day, [new(data.Dining.Id, 8m)]);
        await CaptureAsync(data, day, [new(data.Dining.Id, 8m)], kind: SpendingRecordKind.Refund, original: payment.Id);

        var report = await Summary.GetAsync(data.Home, day, day);

        Assert.Collection(report.Categories,
            row => { Assert.Equal(data.Dining.Id, row.ExpenseCategoryId); Assert.Equal(0m, row.NetAmount); },
            row => { Assert.Equal(data.Food.Id, row.ExpenseCategoryId); Assert.Equal(-5m, row.NetAmount); });
        Assert.Equal(3, report.ReconciliationRecordCounts["NotChecked"]);
        Assert.Equal(0, report.ReconciliationRecordCounts["Unreconciled"]);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public async Task SplitReceiptWarningsAreOncePerRecordAndKeepUnsignedRefundDifference()
    {
        var data = await SeedAsync();
        var day = new DateOnly(2026, 1, 1);
        var payment = await CaptureAsync(data, day, [new(data.Food.Id, 10m), new(data.Dining.Id, 15m)], receipt: 30m);
        var refund = await CaptureAsync(data, day, [new(data.Food.Id, 1m), new(data.Dining.Id, 2m)],
            kind: SpendingRecordKind.Refund, receipt: 4m);
        await CaptureAsync(data, day, [new(data.Food.Id, 0m)]);
        await CaptureAsync(data, day, [new(data.Food.Id, 8m)], receipt: 8m);

        var report = await Summary.GetAsync(data.Home, day, day);

        Assert.Equal(2, report.Warnings.Count);
        var paymentWarning = Assert.Single(report.Warnings, w => w.SpendingRecordId == payment.Id);
        Assert.Equal(new SpendingReceiptWarning(payment.Id, day, "Shop", "GBP", 30m, 25m, 5m), paymentWarning);
        var refundWarning = Assert.Single(report.Warnings, w => w.SpendingRecordId == refund.Id);
        Assert.Equal(new SpendingReceiptWarning(refund.Id, day, "Shop", "GBP", 4m, 3m, 1m), refundWarning);
        Assert.Equal(17m, Assert.Single(report.Categories, c => c.ExpenseCategoryId == data.Food.Id).NetAmount);
        Assert.Equal(13m, Assert.Single(report.Categories, c => c.ExpenseCategoryId == data.Dining.Id).NetAmount);
        Assert.Equal(1, report.ReconciliationRecordCounts["NotChecked"]);
        Assert.Equal(1, report.ReconciliationRecordCounts["Reconciled"]);
        Assert.Equal(2, report.ReconciliationRecordCounts["Unreconciled"]);
    }

    [Fact]
    public async Task HistoricalCategoryIdentityAndLabelsSurviveSetupRenameReclassificationAndRetirement()
    {
        var data = await SeedAsync();
        var day = new DateOnly(2026, 1, 1);
        var item = await Setup.CreateItemAsync(data.Home, data.Food.Id, "EGGS", "Eggs", "pack-6", "Six eggs");
        await CaptureAsync(data, day, [new(data.Food.Id, 2m, item.Id, 1m, 2m)]);
        await Setup.RenameCategoryAsync(data.Home, data.Food.Id, "Groceries");
        await CaptureAsync(data, day.AddDays(1), [new(data.Food.Id, 3m)]);
        await Setup.RenameCategoryAsync(data.Home, data.Food.Id, "Current name only");
        await CaptureAsync(data, day.AddDays(2), [new(data.Food.Id, 100m)]);
        await Setup.UpdateItemAsync(data.Home, item.Id, data.Dining.Id, "Changed item", "Box", null);
        await Setup.RenameMerchantAsync(data.Home, data.Merchant.Id, "Changed merchant");
        await Setup.SetCategoryRetiredAsync(data.Home, data.Food.Id, true);
        await Setup.SetItemRetiredAsync(data.Home, item.Id, true);
        await Setup.SetMerchantRetiredAsync(data.Home, data.Merchant.Id, true);

        var original = await Summary.GetAsync(data.Home, day, day);
        var combined = await Summary.GetAsync(data.Home, day, day.AddDays(1));

        Assert.Equal("Food", Assert.Single(original.Categories).CategoryName);
        var combinedRow = Assert.Single(combined.Categories);
        Assert.Equal(data.Food.Id, combinedRow.ExpenseCategoryId);
        Assert.Equal("Groceries", combinedRow.CategoryName);
        Assert.Equal(5m, combinedRow.NetAmount);
    }

    [Theory]
    [InlineData("date")]
    [InlineData("created")]
    [InlineData("id")]
    public async Task MostRecentSavedLabelUsesDocumentedTieBreaks(string decidingField)
    {
        var data = await SeedAsync();
        var day = new DateOnly(2026, 1, 1);
        var first = await CaptureAsync(data, decidingField == "date" ? day.AddDays(1) : day,
            [new(data.Food.Id, 1m)], id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        await Setup.RenameCategoryAsync(data.Home, data.Food.Id, "Renamed");
        var second = await CaptureAsync(data, day, [new(data.Food.Id, 2m)],
            id: Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var timestamp = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await using (var context = _database.CreateDbContext())
        {
            await context.SpendingRecords.Where(r => r.Id == first.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedAtUtc,
                    decidingField == "created" ? timestamp.AddMinutes(1) : timestamp));
            await context.SpendingRecords.Where(r => r.Id == second.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedAtUtc, timestamp));
        }

        var report = await Summary.GetAsync(data.Home, day, day.AddDays(1));

        Assert.Equal(decidingField == "id" ? "Renamed" : "Food", Assert.Single(report.Categories).CategoryName);
        Assert.Equal(3m, Assert.Single(report.Categories).NetAmount);
    }

    [Fact]
    public async Task IdenticalCategoryNamesDoNotMergeDistinctIdentitiesAndRowsHaveStableOrdering()
    {
        var data = await SeedAsync();
        var otherFood = await Setup.CreateCategoryAsync(data.Home, "Food");
        var day = new DateOnly(2026, 1, 1);
        await CaptureAsync(data, day, [new(data.Food.Id, 1m), new(otherFood.Id, 2m), new(data.Dining.Id, 3m)]);

        var report = await Summary.GetAsync(data.Home, day, day);

        Assert.Equal(data.Dining.Id, report.Categories[0].ExpenseCategoryId);
        Assert.Equal(new[] { data.Food.Id, otherFood.Id }.OrderBy(id => id),
            report.Categories.Skip(1).Select(c => c.ExpenseCategoryId));
        Assert.Equal(1m, Assert.Single(report.Categories, c => c.ExpenseCategoryId == data.Food.Id).NetAmount);
        Assert.Equal(2m, Assert.Single(report.Categories, c => c.ExpenseCategoryId == otherFood.Id).NetAmount);
    }

    [Fact]
    public async Task FullRangeExceedsHistoryPageAndUsesOneRecordQueryWithoutChangingSavedState()
    {
        var data = await SeedAsync();
        var day = new DateOnly(2026, 1, 1);
        var item = await Setup.CreateItemAsync(data.Home, data.Food.Id, "EGGS", "Eggs", "pack-6", "Six eggs");
        var price = await Setup.CreateUsualPriceAsync(data.Home, item.Id, data.Merchant.Id, "GBP", 9m);
        var count = SpendingRecordService.DefaultHistoryPageSize + 1;
        for (var i = 0; i < count; i++)
            await CaptureAsync(data, day, [new(data.Food.Id, 0.10m, item.Id, 1m, 0.10m)]);
        await using var beforeContext = _database.CreateDbContext();
        var before = await beforeContext.SpendingRecords.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Revision);
        var interceptor = new ReadCommands();
        var service = new SpendingCategorySummaryService(new InterceptingContextFactory(_database, interceptor));

        var report = await service.GetAsync(data.Home, day, day);

        Assert.Equal(count * 0.10m, Assert.Single(report.Categories).NetAmount);
        Assert.Equal(count, report.ReconciliationRecordCounts["NotChecked"]);
        Assert.Equal(2, interceptor.Commands.Count); // Household existence, then eligible records and lines together.
        Assert.Single(interceptor.Commands, sql => sql.Contains("FROM \"SpendingRecords\"", StringComparison.Ordinal));
        await using var afterContext = _database.CreateDbContext();
        var after = await afterContext.SpendingRecords.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Revision);
        Assert.Equal(before.OrderBy(p => p.Key), after.OrderBy(p => p.Key));
        Assert.Equal(count, await afterContext.SpendingLines.CountAsync());
        var savedPrice = await Setup.GetUsualPriceAsync(data.Home, price.Id);
        Assert.Equal(price.Revision, savedPrice.Revision);
        Assert.Equal(9m, savedPrice.UsualUnitPrice);
    }

    private sealed class ReadCommands : DbCommandInterceptor
    {
        internal List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
