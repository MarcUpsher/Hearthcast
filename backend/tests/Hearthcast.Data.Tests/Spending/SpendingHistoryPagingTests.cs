using System.Data.Common;
using Hearthcast.Data.Households;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hearthcast.Data.Tests.Spending;

public sealed class SpendingHistoryPagingTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    private async Task<(Guid Home, SpendingRecord[] Records)> SeedAsync(int count)
    {
        var home = await new HouseholdService(_database).CreateAsync("Home", "Alex");
        var setup = new SpendingSetupService(_database);
        var merchant = await setup.CreateMerchantAsync(home.Id, "Shop");
        var category = await setup.CreateCategoryAsync(home.Id, "Food");
        var service = new SpendingRecordService(_database);
        var records = new List<SpendingRecord>();
        for (var i = 0; i < count; i++)
            records.Add((await service.CreateAsync(home.Id, Guid.NewGuid(), new(merchant.Id, new(2026, 11, 17), "GBP",
                [new(category.Id, 1m), new(category.Id, 2m), new(category.Id, 3m)]))).Record);
        await using var context = _database.CreateDbContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "SpendingRecords" SET "CreatedAtUtc" = {new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)} WHERE "HouseholdId" = {home.Id}
            """);
        return (home.Id, records.ToArray());
    }

    [Fact]
    public async Task CursorContinuesEqualDateAndTimestampRecordsWithoutSplittingReceipts()
    {
        var d = await SeedAsync(7);
        var service = new SpendingRecordService(_database);
        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await service.ListPageAsync(d.Home, pageSize: 2, cursor: cursor);
            Assert.InRange(page.Records.Count, 1, 2);
            Assert.All(page.Records, r => { Assert.Equal(3, r.Lines.Count); Assert.Equal(6m, r.TotalAmount); });
            seen.AddRange(page.Records.Select(r => r.Id));
            cursor = page.NextCursor;
        } while (cursor is not null);
        Assert.Equal(d.Records.OrderByDescending(r => r.Id).Select(r => r.Id), seen);
        Assert.Equal(7, seen.Distinct().Count());
    }

    [Fact]
    public async Task ParentLimitAppearsBeforeTheLineJoinInGeneratedSql()
    {
        var d = await SeedAsync(3);
        var capture = new CaptureSql();
        var service = new SpendingRecordService(new InterceptingContextFactory(_database, capture));
        var page = await service.ListPageAsync(d.Home, pageSize: 1);
        Assert.Single(page.Records);
        Assert.Equal(3, page.Records[0].Lines.Count);
        var sql = Assert.Single(capture.Commands, c => c.Contains("JOIN \"SpendingLines\""));
        Assert.True(sql.IndexOf("LIMIT", StringComparison.Ordinal) < sql.IndexOf("JOIN \"SpendingLines\"", StringComparison.Ordinal));
        Assert.DoesNotContain("OFFSET", sql);
    }

    [Fact]
    public async Task CursorsAreBoundToHouseholdFiltersAndPageSize()
    {
        var a = await SeedAsync(3); var b = await SeedAsync(1);
        var service = new SpendingRecordService(_database);
        var page = await service.ListPageAsync(a.Home, pageSize: 1);
        Assert.NotNull(page.NextCursor);
        await Assert.ThrowsAsync<InvalidValueException>(() => service.ListPageAsync(b.Home, pageSize: 1, cursor: page.NextCursor));
        await Assert.ThrowsAsync<InvalidValueException>(() => service.ListPageAsync(a.Home, pageSize: 2, cursor: page.NextCursor));
        await Assert.ThrowsAsync<InvalidValueException>(() => service.ListPageAsync(a.Home, includeVoided: true, pageSize: 1, cursor: page.NextCursor));
        await Assert.ThrowsAsync<InvalidValueException>(() => service.ListPageAsync(a.Home, from: new(2026, 1, 1), pageSize: 1, cursor: page.NextCursor));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(101, null)]
    [InlineData(1, "")]
    [InlineData(1, "bad cursor")]
    [InlineData(1, "YWJj")]
    [InlineData(1, "bnVsbA")]
    public async Task InvalidLimitsAndCursorsAreValidationErrors(int size, string? cursor)
    {
        var d = await SeedAsync(0);
        await Assert.ThrowsAsync<InvalidValueException>(() => new SpendingRecordService(_database).ListPageAsync(d.Home, pageSize: size, cursor: cursor));
    }

    [Fact]
    public async Task FiltersVoidHandlingAndEmptyParentsRemainExplicit()
    {
        var d = await SeedAsync(2);
        var service = new SpendingRecordService(_database);
        await service.VoidAsync(d.Home, d.Records[0].Id, d.Records[0].Revision);
        Assert.Single((await service.ListPageAsync(d.Home)).Records);
        Assert.Equal(2, (await service.ListPageAsync(d.Home, includeVoided: true)).Records.Count);
        var empty = await service.ListPageAsync(d.Home, from: new(2026, 1, 1), to: new(2026, 1, 31));
        Assert.Empty(empty.Records);
        Assert.Null(empty.NextCursor);
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.ListPageAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidValueException>(() => service.ListPageAsync(d.Home, from: new(2026, 2, 1), to: new(2026, 1, 1)));
    }

    private sealed class CaptureSql : DbCommandInterceptor
    {
        internal List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
