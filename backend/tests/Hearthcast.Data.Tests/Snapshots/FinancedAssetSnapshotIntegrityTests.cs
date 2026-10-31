using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.FinancedAssets;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Hearthcast.Data.Tests.Snapshots;

public sealed class FinancedAssetSnapshotIntegrityTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();
    private static readonly DateOnly January = new(2023, 1, 1);
    private static readonly DateOnly February = new(2023, 2, 1);
    private static FinancedAssetSnapshotValue[] Values(Guid id) =>
        [new(id, FinancedAssetBalanceKind.CurrentValuation, 1_400_000m), new(id, FinancedAssetBalanceKind.OutstandingLoanBalance, 600_000m), new(id, FinancedAssetBalanceKind.AvailableToWithdraw, 250_000m)];

    [Theory]
    [InlineData("CurrentValuation", "-1")]
    [InlineData("CurrentValuation", "NaN")]
    [InlineData("CurrentValuation", "Infinity")]
    [InlineData("OutstandingLoanBalance", "-1")]
    [InlineData("OutstandingLoanBalance", "NaN")]
    [InlineData("OutstandingLoanBalance", "Infinity")]
    [InlineData("AvailableToWithdraw", "-1")]
    [InlineData("AvailableToWithdraw", "NaN")]
    [InlineData("AvailableToWithdraw", "Infinity")]
    public async Task PostgreSqlEnforcesNonnegativeFiniteBalances(string balance, string amount)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        await new SnapshotService(_database).CreateAsync(data.HouseholdId, January, financedAssetValues: Values(asset.Id));
        await using var context = _database.CreateDbContext();
        var sql = $"UPDATE \"FinancedAssetSnapshots\" SET \"{balance}\" = CAST({{0}} AS numeric)";
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, amount));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Theory]
    [InlineData("CurrentValuation", "origin")]
    [InlineData("CurrentValuation", "date")]
    [InlineData("CurrentValuation", "self-source")]
    [InlineData("OutstandingLoanBalance", "origin")]
    [InlineData("OutstandingLoanBalance", "date")]
    [InlineData("OutstandingLoanBalance", "self-source")]
    [InlineData("AvailableToWithdraw", "origin")]
    [InlineData("AvailableToWithdraw", "date")]
    [InlineData("AvailableToWithdraw", "self-source")]
    public async Task PostgreSqlEnforcesEachBalanceState(string balance, string scenario)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var service = new SnapshotService(_database);
        await service.CreateAsync(data.HouseholdId, January, financedAssetValues: Values(asset.Id));
        var carried = await service.CreateNextAsync(data.HouseholdId, February);
        var id = Assert.Single(carried.FinancedAssetSnapshots).Id;
        await using var context = _database.CreateDbContext();
        var assignment = scenario switch
        {
            "origin" => $"\"{balance}ValueOrigin\" = 2", // Confirmed cannot retain a carry source.
            "date" => $"\"{balance}LastConfirmedPrecision\" = NULL",
            _ => $"\"{balance}CarriedFromSnapshotId\" = \"Id\""
        };
        var sql = $"UPDATE \"FinancedAssetSnapshots\" SET {assignment} WHERE \"Id\" = {{0}}";
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, id));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Theory]
    [InlineData("CurrentValuation")]
    [InlineData("OutstandingLoanBalance")]
    [InlineData("AvailableToWithdraw")]
    public async Task PostgreSqlRejectsCarrySourcesForAnotherAsset(string balance)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var first = await data.CreateAssetAsync(new(_database));
        var other = await data.CreateAssetAsync(new(_database));
        var service = new SnapshotService(_database);
        var original = await service.CreateAsync(data.HouseholdId, January, financedAssetValues: Values(first.Id));
        var carried = await service.CreateNextAsync(data.HouseholdId, February);
        var id = carried.FinancedAssetSnapshots.Single(row => row.FinancedAssetId == first.Id).Id;
        var sourceId = original.FinancedAssetSnapshots.Single(row => row.FinancedAssetId == other.Id).Id;
        await using var context = _database.CreateDbContext();
        var sql = $"UPDATE \"FinancedAssetSnapshots\" SET \"{balance}CarriedFromSnapshotId\" = {{0}} WHERE \"Id\" = {{1}}";
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, sourceId, id));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("loan-holder")]
    [InlineData("allocation")]
    public async Task PostgreSqlRejectsPeopleFromAnotherSnapshot(string role)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var service = new SnapshotService(_database);
        var original = await service.CreateAsync(data.HouseholdId, January, financedAssetValues: Values(asset.Id));
        var later = await service.CreateNextAsync(data.HouseholdId, February);
        var table = role switch { "owner" => "FinancedAssetSnapshotOwners", "loan-holder" => "FinancedAssetSnapshotLoanHolders", _ => "FinancedAssetSnapshotHouseholdAllocations" };
        var oldPerson = original.People.Single(person => person.PersonId == data.SecondPersonId).Id;
        var newPerson = later.People.Single(person => person.PersonId == data.SecondPersonId).Id;
        await using var context = _database.CreateDbContext();
        var sql = $"UPDATE \"{table}\" SET \"SnapshotPersonId\" = {{0}} WHERE \"SnapshotPersonId\" = {{1}}";
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, newPerson, oldPerson));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Fact]
    public async Task CurrencyChangesRequireExplicitValuesOrClearsForEveryApplicableBalance()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var service = new SnapshotService(_database);
        var first = await service.CreateAsync(data.HouseholdId, January, financedAssetValues: Values(asset.Id));
        await using var context = _database.CreateDbContext();
        var setup = await context.FinancedAssets.SingleAsync();
        context.Entry(setup).Property(row => row.CurrencyCode).CurrentValue = "GBP";
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<Hearthcast.Data.Validation.InvalidValueException>(() => service.CreateNextAsync(data.HouseholdId, February));
        await Assert.ThrowsAsync<Hearthcast.Data.Validation.InvalidValueException>(() => service.CreateNextAsync(data.HouseholdId, February,
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 10m)]));
        var supplied = Values(asset.Id).Select(value => value with { Amount = (decimal?)null }).ToArray();
        var changed = await service.CreateNextAsync(data.HouseholdId, February, financedAssetValues: supplied);
        Assert.Null(Assert.Single(changed.FinancedAssetSnapshots).CurrentValuation);
        Assert.Equal("GBP", Assert.Single(changed.FinancedAssetSnapshots).CurrencyCode);
        // The source FK itself also prevents an old-currency row being attached directly.
        var id = Assert.Single(changed.FinancedAssetSnapshots).Id;
        var sourceId = Assert.Single(first.FinancedAssetSnapshots).Id;
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "FinancedAssetSnapshots" SET "CurrentValuation" = 10, "CurrentValuationValueOrigin" = 3,
            "CurrentValuationCarriedFromSnapshotId" = {sourceId} WHERE "Id" = {id}
            """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedCaptureOrMixedCorrectionRollsBackBothTypes(bool correcting)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var item = await new TrackedItemService(_database).CreateAsync(data.HouseholdId, data.GroupId, data.FirstPersonId,
            "Cash", "ZAR", "ZA", true);
        var service = new SnapshotService(_database);
        var original = correcting ? await service.CreateAsync(data.HouseholdId, January, [new(item.Id, 50m)], financedAssetValues: Values(asset.Id)) : null;
        await using var context = _database.CreateDbContext();
        var operation = correcting ? "UPDATE" : "INSERT";
        var sql = $"""
            CREATE FUNCTION "RejectFinancedCapture"() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Synthetic financed capture failure'; END;
            $$;
            CREATE TRIGGER "RejectFinancedCapture" BEFORE {operation} ON "FinancedAssetSnapshots"
            FOR EACH ROW EXECUTE FUNCTION "RejectFinancedCapture"();
            """;
        await context.Database.ExecuteSqlRawAsync(sql);
        if (original is null)
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(data.HouseholdId, January, [new(item.Id, 50m)], financedAssetValues: Values(asset.Id)));
            Assert.Equal(0, await context.Snapshots.CountAsync());
            Assert.Equal(0, await context.ItemSnapshots.CountAsync());
            Assert.Equal(0, await context.SnapshotPeople.CountAsync());
            Assert.Equal(0, await context.FinancedAssetSnapshots.CountAsync());
        }
        else
        {
            var before = await service.GetAsync(data.HouseholdId, original.Id);
            await Assert.ThrowsAsync<DbUpdateException>(() => service.CorrectValuesAsync(data.HouseholdId, original.Id, original.Revision,
                [new(item.Id, 60m)], [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 1_500_000m)]));
            var retained = await service.GetAsync(data.HouseholdId, original.Id);
            Assert.Equal(before.Revision, retained.Revision);
            Assert.Equal(before.UpdatedAtUtc, retained.UpdatedAtUtc);
            Assert.Equal(50m, Assert.Single(retained.ItemSnapshots).Amount);
            Assert.Equal(1_400_000m, Assert.Single(retained.FinancedAssetSnapshots).CurrentValuation);
        }
    }

    [Fact]
    public async Task OneRevisionProtectsConcurrentOrdinaryAndFinancedCorrections()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var item = await new TrackedItemService(_database).CreateAsync(data.HouseholdId, data.GroupId, data.FirstPersonId,
            "Cash", "ZAR", "ZA", true);
        var service = new SnapshotService(_database);
        var original = await service.CreateAsync(data.HouseholdId, January, [new(item.Id, 50m)], financedAssetValues: Values(asset.Id));
        var barrier = new ConcurrentSaveBarrier();
        var concurrent = new SnapshotService(new InterceptingContextFactory(_database, barrier));
        var outcomes = await Task.WhenAll(
            Record.ExceptionAsync(() => concurrent.CorrectValuesAsync(data.HouseholdId, original.Id, original.Revision, [new(item.Id, 60m)])),
            Record.ExceptionAsync(() => concurrent.CorrectValuesAsync(data.HouseholdId, original.Id, original.Revision,
                financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 1_500_000m)])));
        Assert.Equal(2, barrier.Arrivals);
        Assert.Single(outcomes, error => error is null);
        Assert.IsType<SnapshotConflictException>(Assert.Single(outcomes, error => error is not null));
        var saved = await service.GetAsync(data.HouseholdId, original.Id);
        var ordinaryChanged = Assert.Single(saved.ItemSnapshots).Amount == 60m;
        Assert.Equal(ordinaryChanged ? 1_400_000m : 1_500_000m, Assert.Single(saved.FinancedAssetSnapshots).CurrentValuation);
    }
    [Fact]
    public async Task SnapshotMigrationPreservesExistingOrdinaryHistoryAndAssetSetup()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var item = await new TrackedItemService(_database).CreateAsync(data.HouseholdId, data.GroupId, data.FirstPersonId,
            "Cash", "ZAR", "ZA", true);
        var service = new SnapshotService(_database);
        var original = await service.CreateAsync(data.HouseholdId, January, [new(item.Id, 123.45m)]);
        var asset = await data.CreateAssetAsync(new(_database));
        await using var context = _database.CreateDbContext();
        await context.GetService<IMigrator>().MigrateAsync("20261030115628_AddFinancedAssetSetup");
        Assert.Equal(123.45m, (await context.ItemSnapshots.AsNoTracking().SingleAsync()).Amount);
        Assert.Equal(asset.Id, (await context.FinancedAssets.AsNoTracking().SingleAsync()).Id);
        await context.GetService<IMigrator>().MigrateAsync("20261030121729_AddFinancedAssetSnapshots");
        Assert.False(context.Database.HasPendingModelChanges());
        var retained = await service.GetAsync(data.HouseholdId, original.Id);
        Assert.Equal(original.Revision, retained.Revision);
        Assert.Empty(retained.FinancedAssetSnapshots);
        Assert.Equal(123.45m, Assert.Single(retained.ItemSnapshots).Amount);
        var next = await service.CreateNextAsync(data.HouseholdId, February, financedAssetValues: Values(asset.Id));
        Assert.Equal(800_000m, Assert.Single(next.FinancedAssetSnapshots).CurrentEquity);
        Assert.Equal(123.45m, Assert.Single(next.ItemSnapshots).Amount);
    }

    [Theory]
    [InlineData("owner-total")]
    [InlineData("allocation-total")]
    [InlineData("excluded-with-allocations")]
    [InlineData("nonparticipant")]
    [InlineData("category-type")]
    public async Task MalformedCurrentSetupCannotBecomeSavedHistory(string scenario)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        await data.CreateAssetAsync(new(_database));
        await using var context = _database.CreateDbContext();
        var sql = scenario switch
        {
            "owner-total" => "UPDATE \"FinancedAssetOwners\" SET \"ShareBasisPoints\" = 1",
            "allocation-total" => "UPDATE \"FinancedAssetHouseholdAllocations\" SET \"ShareBasisPoints\" = 1",
            "excluded-with-allocations" => "UPDATE \"FinancedAssets\" SET \"IncludeInHousehold\" = false",
            "nonparticipant" => "UPDATE \"People\" SET \"ParticipatesInHouseholdFinances\" = false WHERE \"DisplayName\" = 'Person B'",
            _ => "UPDATE \"Categories\" SET \"FinancialType\" = 2"
        };
        await context.Database.ExecuteSqlRawAsync(sql);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SnapshotService(_database).CreateAsync(data.HouseholdId, January));
        Assert.Equal(0, await context.Snapshots.CountAsync());
    }

    [Fact]
    public async Task UnknownConfirmationRemainsUnknownAndFutureConfirmationIsRejected()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        var service = new SnapshotService(_database);
        await service.CreateAsync(data.HouseholdId, January, financedAssetValues: Values(asset.Id));
        var carried = await service.CreateNextAsync(data.HouseholdId, February);
        var id = Assert.Single(carried.FinancedAssetSnapshots).Id;
        await using var context = _database.CreateDbContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "FinancedAssetSnapshots" SET "CurrentValuationLastConfirmedDate" = NULL,
                "CurrentValuationLastConfirmedPrecision" = NULL WHERE "Id" = {id}
            """);
        var next = await service.CreateNextAsync(data.HouseholdId, new DateOnly(2023, 3, 1));
        Assert.Null(Assert.Single(next.FinancedAssetSnapshots).CurrentValuationLastConfirmedDate);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "FinancedAssetSnapshots" SET "OutstandingLoanBalanceLastConfirmedDate" = {new DateOnly(2030, 1, 1)}
            WHERE "SnapshotId" = {next.Id}
            """);
        await Assert.ThrowsAsync<Hearthcast.Data.Validation.BusinessRuleViolationException>(() => service.CreateNextAsync(data.HouseholdId, new DateOnly(2023, 4, 1)));
    }

}
