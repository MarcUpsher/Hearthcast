using System.Globalization;
using Hearthcast.Data.Classification;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Hearthcast.Data.Tests.FinancedAssets;

public sealed class FinancedAssetPersistenceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task SharedOwnershipLoanNamesAndHouseholdAllocationAreIndependent()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var service = new FinancedAssetService(_database);
        var asset = await data.CreateAssetAsync(service, allocations: [new(data.FirstPersonId, 10_000)],
            purchaseDate: new DateOnly(2015, 7, 17), name: " Shared home ", currency: " zar ", country: "za");
        var saved = await service.GetAsync(data.HouseholdId, asset.Id);
        Assert.Equal("Shared home", saved.Name);
        Assert.Equal("ZAR", saved.CurrencyCode);
        Assert.Equal("ZA", saved.CountryCode);
        Assert.Equal(new DateOnly(2015, 7, 17), saved.PurchaseDate);
        Assert.Equal(1_100_000m, saved.PurchasePrice);
        Assert.Equal(1_000_000m, saved.OriginalLoanAmount);
        Assert.True(saved.HasWithdrawalFacility);
        Assert.Equal(2, saved.Owners.Count);
        Assert.All(saved.Owners, owner => Assert.Equal(5_000, owner.ShareBasisPoints));
        Assert.Equal(data.SecondPersonId, Assert.Single(saved.LoanHolders).PersonId);
        Assert.Equal(data.FirstPersonId, Assert.Single(saved.HouseholdAllocations).PersonId);
        Assert.Equal(10_000, Assert.Single(saved.HouseholdAllocations).ShareBasisPoints);
        Assert.Equal(FinancialType.Asset, saved.Group.Category.FinancialType);
        await using var context = _database.CreateDbContext();
        Assert.Empty(await context.TrackedItems.ToArrayAsync());
    }

    [Fact]
    public async Task UnknownAcquisitionDetailsAndARepaidLoanRemainRepresentable()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database, secondParticipates: false);
        var service = new FinancedAssetService(_database);
        var asset = await data.CreateAssetAsync(service, purchasePrice: null, loanHolders: [], include: false, withdrawal: false);
        var saved = await service.GetAsync(data.HouseholdId, asset.Id);
        Assert.Null(saved.PurchaseDate);
        Assert.Null(saved.PurchasePrice);
        Assert.Equal(1_000_000m, saved.OriginalLoanAmount);
        Assert.False(saved.HasWithdrawalFacility);
        Assert.False(saved.IncludeInHousehold);
        Assert.Empty(saved.HouseholdAllocations);
        Assert.Empty(saved.LoanHolders);
        Assert.Equal(2, saved.Owners.Count); // Ownership does not require financial participation.
    }

    [Theory]
    [InlineData("0")]
    [InlineData("123456.789123")]
    [InlineData("79228162514264337593543950335")]
    [InlineData("0.0000000000000000000000000001")]
    public async Task AcquisitionAmountsRoundTripExactly(string text)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var amount = decimal.Parse(text, CultureInfo.InvariantCulture);
        var service = new FinancedAssetService(_database);
        var asset = await data.CreateAssetAsync(service, purchasePrice: amount, originalLoan: amount);
        var saved = await service.GetAsync(data.HouseholdId, asset.Id);
        Assert.Equal(amount, saved.PurchasePrice);
        Assert.Equal(amount, saved.OriginalLoanAmount);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("currency")]
    [InlineData("country")]
    [InlineData("price")]
    [InlineData("loan")]
    [InlineData("debt-category")]
    public async Task InvalidSetupLeavesNoAssetOrAssociations(string scenario)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database,
            financialType: scenario == "debt-category" ? FinancialType.Debt : FinancialType.Asset);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => data.CreateAssetAsync(new(_database),
            name: scenario == "name" ? " " : "Home", currency: scenario == "currency" ? "12" : "GBP",
            country: scenario == "country" ? "UK1" : "GB", purchasePrice: scenario == "price" ? -1m : 0m,
            originalLoan: scenario == "loan" ? -1m : 0m));
        await AssertNoAssetAsync();
    }

    [Theory]
    [InlineData("owners-empty")]
    [InlineData("owners-under")]
    [InlineData("owners-over")]
    [InlineData("owners-zero")]
    [InlineData("owners-negative")]
    [InlineData("owners-duplicate")]
    [InlineData("loans-duplicate")]
    [InlineData("allocations-empty")]
    [InlineData("allocations-under")]
    [InlineData("allocations-over")]
    [InlineData("allocations-zero")]
    [InlineData("allocations-duplicate")]
    [InlineData("excluded-allocations")]
    public async Task InvalidPersonAssociationsAreRejectedAtomically(string scenario)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        FinancedAssetShare[] owners = [new(data.FirstPersonId, 5_000), new(data.SecondPersonId, 5_000)];
        FinancedAssetShare[] allocations = [new(data.FirstPersonId, 10_000)];
        Guid[] loans = [data.SecondPersonId];
        switch (scenario)
        {
            case "owners-empty": owners = []; break;
            case "owners-under": owners = [new(data.FirstPersonId, 9_999)]; break;
            case "owners-over": owners = [new(data.FirstPersonId, 10_001)]; break;
            case "owners-zero": owners = [new(data.FirstPersonId, 0), new(data.SecondPersonId, 10_000)]; break;
            case "owners-negative": owners = [new(data.FirstPersonId, -1), new(data.SecondPersonId, 10_000)]; break;
            case "owners-duplicate": owners = [new(data.FirstPersonId, 5_000), new(data.FirstPersonId, 5_000)]; break;
            case "loans-duplicate": loans = [data.SecondPersonId, data.SecondPersonId]; break;
            case "allocations-empty": allocations = []; break;
            case "allocations-under": allocations = [new(data.FirstPersonId, 9_999)]; break;
            case "allocations-over": allocations = [new(data.FirstPersonId, 10_001)]; break;
            case "allocations-zero": allocations = [new(data.FirstPersonId, 0), new(data.SecondPersonId, 10_000)]; break;
            case "allocations-duplicate": allocations = [new(data.FirstPersonId, 5_000), new(data.FirstPersonId, 5_000)]; break;
        }
        await Assert.ThrowsAnyAsync<ArgumentException>(() => data.CreateAssetAsync(new(_database), owners, loans,
            include: scenario != "excluded-allocations", allocations: allocations));
        await AssertNoAssetAsync();
    }

    [Theory]
    [InlineData("group")]
    [InlineData("owner")]
    [InlineData("loan-holder")]
    [InlineData("allocation")]
    public async Task ServicesRejectForeignHouseholdReferences(string role)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var other = await FinancedAssetTestSetup.CreateAsync(_database);
        if (role == "group") data = data with { GroupId = other.GroupId };
        await Assert.ThrowsAsync<KeyNotFoundException>(() => data.CreateAssetAsync(new(_database),
            owners: role == "owner" ? [new(other.FirstPersonId, 10_000)] : null,
            loanHolders: role == "loan-holder" ? [other.FirstPersonId] : null,
            allocations: role == "allocation" ? [new(other.FirstPersonId, 10_000)] : null));
        await AssertNoAssetAsync();
    }

    [Fact]
    public async Task HouseholdScopingAlsoAppliesToGetChangesAndRetirement()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var other = await FinancedAssetTestSetup.CreateAsync(_database);
        var service = new FinancedAssetService(_database);
        var asset = await data.CreateAssetAsync(service);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(other.HouseholdId, asset.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetRetiredAsync(other.HouseholdId, asset.Id, true));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetPersonAssociationsAsync(other.HouseholdId, asset.Id,
            [new(other.FirstPersonId, 10_000)], [], false, []));
        Assert.False((await service.GetAsync(data.HouseholdId, asset.Id)).IsRetired);
    }

    [Fact]
    public async Task AssociationReplacementExclusionRetirementAndRestorePreserveIdentity()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var service = new FinancedAssetService(_database);
        var asset = await data.CreateAssetAsync(service);
        await service.SetPersonAssociationsAsync(data.HouseholdId, asset.Id,
            [new(data.SecondPersonId, 10_000)], [data.FirstPersonId, data.SecondPersonId], false, []);
        await service.SetRetiredAsync(data.HouseholdId, asset.Id, true);
        var excluded = await service.GetAsync(data.HouseholdId, asset.Id);
        Assert.Equal(data.SecondPersonId, Assert.Single(excluded.Owners).PersonId);
        Assert.Equal(2, excluded.LoanHolders.Count);
        Assert.Empty(excluded.HouseholdAllocations);
        Assert.True(excluded.IsRetired);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetPersonAssociationsAsync(data.HouseholdId, asset.Id,
            [new(data.FirstPersonId, 10_000)], [], true, []));
        var unchanged = await service.GetAsync(data.HouseholdId, asset.Id);
        Assert.Equal(data.SecondPersonId, Assert.Single(unchanged.Owners).PersonId);
        Assert.Equal(2, unchanged.LoanHolders.Count);
        await service.SetPersonAssociationsAsync(data.HouseholdId, asset.Id,
            [new(data.FirstPersonId, 3_333), new(data.SecondPersonId, 6_667)], [], true,
            [new(data.FirstPersonId, 6_667), new(data.SecondPersonId, 3_333)]);
        await service.SetRetiredAsync(data.HouseholdId, asset.Id, false);
        await using (var context = _database.CreateDbContext())
        {
            (await context.FinancedAssets.SingleAsync()).Rename("Updated home");
            await context.SaveChangesAsync();
        }
        var restored = await service.GetAsync(data.HouseholdId, asset.Id);
        Assert.Equal(asset.Id, restored.Id);
        Assert.Equal("Updated home", restored.Name);
        Assert.False(restored.IsRetired);
        Assert.Empty(restored.LoanHolders);
        Assert.Equal(3_333, restored.Owners.Single(row => row.PersonId == data.FirstPersonId).ShareBasisPoints);
        Assert.Equal(6_667, restored.HouseholdAllocations.Single(row => row.PersonId == data.FirstPersonId).ShareBasisPoints);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task RetirementReturnsCompleteAssociationsAndClassification(bool include, bool retire)
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var service = new FinancedAssetService(_database);
        var asset = await data.CreateAssetAsync(service, include: include,
            owners: [new(data.FirstPersonId, 3_333), new(data.SecondPersonId, 6_667)],
            allocations: include ? [new(data.FirstPersonId, 10_000)] : []);
        // Exercise reactivation as well as retirement with a fresh service context for each call.
        await service.SetRetiredAsync(data.HouseholdId, asset.Id, !retire);
        var result = await service.SetRetiredAsync(data.HouseholdId, asset.Id, retire);

        Assert.Equal(asset.Id, result.Id);
        Assert.Equal(retire, result.IsRetired);
        Assert.Equal(include, result.IncludeInHousehold);
        Assert.Equal(data.GroupId, result.Group.Id);
        Assert.Equal(FinancialType.Asset, result.Group.Category.FinancialType);
        Assert.Equal(2, result.Owners.Count);
        Assert.Equal(3_333, result.Owners.Single(row => row.PersonId == data.FirstPersonId).ShareBasisPoints);
        Assert.Equal(6_667, result.Owners.Single(row => row.PersonId == data.SecondPersonId).ShareBasisPoints);
        Assert.All(result.Owners, row => Assert.Equal(row.PersonId, row.Person.Id));
        var loanHolder = Assert.Single(result.LoanHolders);
        Assert.Equal(data.SecondPersonId, loanHolder.Person.Id);
        if (include)
        {
            var allocation = Assert.Single(result.HouseholdAllocations);
            Assert.Equal(data.FirstPersonId, allocation.Person.Id);
            Assert.Equal(10_000, allocation.ShareBasisPoints);
        }
        else
        {
            Assert.Empty(result.HouseholdAllocations);
        }
        var persisted = await service.GetAsync(data.HouseholdId, asset.Id);
        Assert.Equal(retire, persisted.IsRetired);
        Assert.Equal(result.Owners.Select(row => (row.PersonId, row.ShareBasisPoints)).OrderBy(row => row.PersonId),
            persisted.Owners.Select(row => (row.PersonId, row.ShareBasisPoints)).OrderBy(row => row.PersonId));
        Assert.Equal(result.HouseholdAllocations.Count, persisted.HouseholdAllocations.Count);
        Assert.Equal(loanHolder.PersonId, Assert.Single(persisted.LoanHolders).PersonId);
    }

    [Fact]
    public async Task AssociationInsertFailureRollsBackTheCompleteSetup()
    {
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        await using var context = _database.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION "RejectFinancedAllocation"() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Synthetic association failure'; END;
            $$;
            CREATE TRIGGER "RejectFinancedAllocation" BEFORE INSERT ON "FinancedAssetHouseholdAllocations"
            FOR EACH ROW EXECUTE FUNCTION "RejectFinancedAllocation"();
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => data.CreateAssetAsync(new(_database)));
        await AssertNoAssetAsync();
    }

    [Fact]
    public async Task SetupMigrationPreservesExistingSnapshotsAndCanBeRolledBackAndReapplied()
    {
        await using var context = _database.CreateDbContext();
        const string previous = "20261030112256_AddSnapshotCarryForwardAndCorrections";
        const string setupMigration = "20261030115628_AddFinancedAssetSetup";
        await context.GetService<IMigrator>().MigrateAsync(previous);
        var data = await FinancedAssetTestSetup.CreateAsync(_database);
        var snapshots = new Hearthcast.Data.Snapshots.SnapshotService(_database);
        var snapshot = await snapshots.CreateAsync(data.HouseholdId);
        await context.GetService<IMigrator>().MigrateAsync(setupMigration);
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(setupMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(snapshot.Revision, (await snapshots.GetAsync(data.HouseholdId, snapshot.Id)).Revision);
        var asset = await data.CreateAssetAsync(new(_database));
        Assert.Equal(asset.Id, (await new FinancedAssetService(_database).GetAsync(data.HouseholdId, asset.Id)).Id);
        await context.GetService<IMigrator>().MigrateAsync(previous);
        await context.GetService<IMigrator>().MigrateAsync(setupMigration);
        Assert.Empty(await context.FinancedAssets.ToArrayAsync());
        Assert.Equal(snapshot.Revision, (await snapshots.GetAsync(data.HouseholdId, snapshot.Id)).Revision);
    }

    private async Task AssertNoAssetAsync()
    {
        await using var context = _database.CreateDbContext();
        Assert.Equal(0, await context.FinancedAssets.CountAsync());
        Assert.Equal(0, await context.FinancedAssetOwners.CountAsync());
        Assert.Equal(0, await context.FinancedAssetLoanHolders.CountAsync());
        Assert.Equal(0, await context.FinancedAssetHouseholdAllocations.CountAsync());
    }
}
