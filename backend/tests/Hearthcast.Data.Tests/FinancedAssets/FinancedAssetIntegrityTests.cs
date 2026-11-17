using Hearthcast.Data.Validation;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hearthcast.Data.Tests.FinancedAssets;

public sealed class FinancedAssetIntegrityTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Theory]
    [InlineData("owner")]
    [InlineData("loan-holder")]
    public async Task LinkedPeopleCannotBeRemovedButMayStopParticipatingWhenUnallocated(string role)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var service = new FinancedAssetService(_database);
        await data.CreateAssetAsync(service,
            owners: [new(role == "owner" ? data.SecondPersonId : data.FirstPersonId, 10_000)],
            loanHolders: role == "loan-holder" ? [data.SecondPersonId] : [],
            allocations: [new(data.FirstPersonId, 10_000)]);
        var households = new HouseholdService(_database);
        await households.SetPersonParticipationAsync(data.HouseholdId, data.SecondPersonId, false);
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => households.RemovePersonAsync(data.HouseholdId, data.SecondPersonId));
        await using var context = _database.CreateDbContext();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"People\" WHERE \"Id\" = {data.SecondPersonId}"));
        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
    }

    [Fact]
    public async Task RetiredAssetsStillRequireEligibleAllocationRecipientsUntilReassigned()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var service = new FinancedAssetService(_database);
        var asset = await data.CreateAssetAsync(service);
        await service.SetRetiredAsync(data.HouseholdId, asset.Id, true);
        var households = new HouseholdService(_database);
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => households.SetPersonParticipationAsync(data.HouseholdId, data.SecondPersonId, false));
        await service.SetPersonAssociationsAsync(data.HouseholdId, asset.Id,
            [new(data.FirstPersonId, 10_000)], [], true, [new(data.FirstPersonId, 10_000)]);
        await households.SetPersonParticipationAsync(data.HouseholdId, data.SecondPersonId, false);
        await households.RemovePersonAsync(data.HouseholdId, data.SecondPersonId);
        await using var context = _database.CreateDbContext();
        Assert.Equal(data.FirstPersonId, Assert.Single(await context.People.ToArrayAsync()).Id);
    }

    [Fact]
    public async Task NonparticipantsMayOwnAndBorrowButCannotReceiveHouseholdAllocation()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database, secondParticipates: false);
        var service = new FinancedAssetService(_database);
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => data.CreateAssetAsync(service));
        var asset = await data.CreateAssetAsync(service, allocations: [new(data.FirstPersonId, 10_000)]);
        Assert.Equal(2, asset.Owners.Count);
        Assert.Equal(data.SecondPersonId, Assert.Single(asset.LoanHolders).PersonId);
    }

    [Theory]
    [InlineData("owner", false)]
    [InlineData("owner", true)]
    [InlineData("loan-holder", false)]
    [InlineData("loan-holder", true)]
    [InlineData("allocation", false)]
    [InlineData("allocation", true)]
    public async Task PostgreSqlRejectsForeignPeopleOrAssetsOnEveryAssociation(string role, bool changeAsset)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var other = await FinancedAssetTestSetup.CreateAsync(_database);
        var service = new FinancedAssetService(_database);
        await data.CreateAssetAsync(service);
        var foreign = await other.CreateAssetAsync(service);
        await using var context = _database.CreateDbContext();
        var table = role switch { "owner" => "FinancedAssetOwners", "loan-holder" => "FinancedAssetLoanHolders", _ => "FinancedAssetHouseholdAllocations" };
        var column = changeAsset ? "FinancedAssetId" : "PersonId";
        var id = changeAsset ? foreign.Id : other.FirstPersonId;
        // Identifiers come exclusively from the fixed test cases; values remain parameters.
        var sql = $"UPDATE \"{table}\" SET \"{column}\" = {{0}} WHERE \"HouseholdId\" = {{1}} AND \"PersonId\" = {{2}}";
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, id, data.HouseholdId, data.SecondPersonId));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Theory]
    [InlineData("PurchasePrice", "-1")]
    [InlineData("PurchasePrice", "NaN")]
    [InlineData("PurchasePrice", "Infinity")]
    [InlineData("PurchasePrice", "-Infinity")]
    [InlineData("OriginalLoanAmount", "-1")]
    [InlineData("OriginalLoanAmount", "NaN")]
    [InlineData("OriginalLoanAmount", "Infinity")]
    [InlineData("OriginalLoanAmount", "-Infinity")]
    public async Task PostgreSqlRejectsNegativeAndNonfiniteAcquisitionAmounts(string column, string amount)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        await data.CreateAssetAsync(new(_database));
        await using var context = _database.CreateDbContext();
        var sql = $"UPDATE \"FinancedAssets\" SET \"{column}\" = CAST({{0}} AS numeric)";
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, amount));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Theory]
    [InlineData("FinancedAssetOwners", 0)]
    [InlineData("FinancedAssetOwners", 10001)]
    [InlineData("FinancedAssetHouseholdAllocations", 0)]
    [InlineData("FinancedAssetHouseholdAllocations", 10001)]
    public async Task PostgreSqlEnforcesIndividualShareRanges(string table, int share)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        await data.CreateAssetAsync(new(_database));
        await using var context = _database.CreateDbContext();
        var sql = $"UPDATE \"{table}\" SET \"ShareBasisPoints\" = {{0}}";
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, share));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("loan-holder")]
    [InlineData("allocation")]
    public async Task PostgreSqlRejectsDuplicateAssociations(string role)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        await data.CreateAssetAsync(new(_database));
        await using var context = _database.CreateDbContext();
        var table = role switch { "owner" => "FinancedAssetOwners", "loan-holder" => "FinancedAssetLoanHolders", _ => "FinancedAssetHouseholdAllocations" };
        var sql = $"INSERT INTO \"{table}\" SELECT * FROM \"{table}\" LIMIT 1";
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }

    [Fact]
    public async Task PostgreSqlRejectsForeignGroupsAndReferencedGroupDeletion()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var other = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await data.CreateAssetAsync(new(_database));
        await using var context = _database.CreateDbContext();
        var foreign = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"FinancedAssets\" SET \"GroupId\" = {other.GroupId} WHERE \"Id\" = {asset.Id}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreign.SqlState);
        var deletion = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"ItemGroups\" WHERE \"Id\" = {data.GroupId}"));
        Assert.Equal(PostgresErrorCodes.RestrictViolation, deletion.SqlState);
    }

    [Theory]
    [InlineData("Name", " ")]
    [InlineData("Name", null)]
    [InlineData("CurrencyCode", "gbp")]
    [InlineData("CurrencyCode", null)]
    [InlineData("CountryCode", "123")]
    [InlineData("OriginalLoanAmount", null)]
    public async Task PostgreSqlEnforcesRequiredMetadata(string column, string? value)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        await data.CreateAssetAsync(new(_database));
        await using var context = _database.CreateDbContext();
        var sql = $"UPDATE \"FinancedAssets\" SET \"{column}\" = {{0}}";
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql, new object[] { value! }));
        Assert.Equal(value is null ? PostgresErrorCodes.NotNullViolation : PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task DatabaseFailureDuringAssociationReplacementPreservesAllPreviousLinks()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var service = new FinancedAssetService(_database);
        var asset = await data.CreateAssetAsync(service);
        await using var context = _database.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION "RejectNewLoanHolder"() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Synthetic replacement failure'; END;
            $$;
            CREATE TRIGGER "RejectNewLoanHolder" BEFORE INSERT ON "FinancedAssetLoanHolders"
            FOR EACH ROW EXECUTE FUNCTION "RejectNewLoanHolder"();
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => service.SetPersonAssociationsAsync(data.HouseholdId, asset.Id,
            [new(data.FirstPersonId, 10_000)], [data.FirstPersonId], true, [new(data.SecondPersonId, 10_000)]));
        var saved = await service.GetAsync(data.HouseholdId, asset.Id);
        Assert.Equal(2, saved.Owners.Count);
        Assert.All(saved.Owners, row => Assert.Equal(5_000, row.ShareBasisPoints));
        Assert.Equal(data.SecondPersonId, Assert.Single(saved.LoanHolders).PersonId);
        Assert.Equal(2, saved.HouseholdAllocations.Count);
        Assert.All(saved.HouseholdAllocations, row => Assert.Equal(5_000, row.ShareBasisPoints));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverlappingAllocationAndParticipationChangesPreserveEligibility(bool creating)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var original = creating ? null : await data.CreateAssetAsync(new(_database), include: false);
        var barrier = new ConcurrentSaveBarrier();
        var factory = new InterceptingContextFactory(_database, barrier);
        var service = new FinancedAssetService(factory);
        var owners = new FinancedAssetShare[] { new(data.FirstPersonId, 10_000) };
        var allocations = new FinancedAssetShare[] { new(data.SecondPersonId, 10_000) };
        var allocation = Record.ExceptionAsync(() => creating
            ? data.CreateAssetAsync(service, owners: owners, loanHolders: [], allocations: allocations)
            : service.SetPersonAssociationsAsync(data.HouseholdId, original!.Id, owners, [], true, allocations));
        var participation = Record.ExceptionAsync(() => new HouseholdService(factory)
            .SetPersonParticipationAsync(data.HouseholdId, data.SecondPersonId, false));
        var outcomes = await Task.WhenAll(allocation, participation);
        // Both operations must overlap; a serialization retry may reach SaveChanges again.
        Assert.True(barrier.Arrivals >= 2);
        Assert.Single(outcomes, error => error is null);
        if (outcomes[0] is not null)
        {
            Assert.IsType<BusinessRuleViolationException>(outcomes[0]);
        }
        else
        {
            Assert.IsType<BusinessRuleViolationException>(outcomes[1]);
        }
        await using var context = _database.CreateDbContext();
        var person = await context.People.SingleAsync(person => person.Id == data.SecondPersonId);
        var included = await context.FinancedAssets.AnyAsync(asset => asset.IncludeInHousehold);
        Assert.Equal(person.ParticipatesInHouseholdFinances, included);
        Assert.Equal(included ? 1 : 0, await context.FinancedAssetHouseholdAllocations.CountAsync());
    }
}
