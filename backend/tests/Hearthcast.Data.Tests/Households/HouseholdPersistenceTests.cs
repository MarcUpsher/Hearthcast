using Hearthcast.Data.Validation;
using Hearthcast.Data.Households;
using Hearthcast.Data.Persistence;
using Hearthcast.Data.Tests.Infrastructure;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Hearthcast.Data.Tests.Households;

public sealed class HouseholdPersistenceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private readonly HouseholdService _service;

    public HouseholdPersistenceTests()
    {
        _service = new HouseholdService(_database);
    }

    public Task InitializeAsync()
    {
        return _database.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        return _database.DisposeAsync();
    }

    [Fact]
    public async Task CreationPersistsTheHouseholdAndItsFirstParticipantTogether()
    {
        var created = await _service.CreateAsync(" Household A ", " Person A ");

        await using var context = CreateContext();
        var household = await context.Households.Include(household => household.People).SingleAsync();
        var person = Assert.Single(household.People);
        Assert.Equal(created.Id, household.Id);
        Assert.NotEqual(Guid.Empty, household.Id);
        Assert.Equal("Household A", household.Name);
        Assert.Equal("Person A", person.DisplayName);
        Assert.Equal(household.Id, person.HouseholdId);
        Assert.Same(household, person.Household);
        Assert.True(person.ParticipatesInHouseholdFinances);
    }

    [Theory]
    [InlineData(null, "Person A")]
    [InlineData("", "Person A")]
    [InlineData(" \t ", "Person A")]
    [InlineData("Household A", null)]
    [InlineData("Household A", "")]
    [InlineData("Household A", " \t ")]
    public async Task InvalidNamesDoNotLeaveAnEmptyHousehold(string? name, string? firstPerson)
    {
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(() => _service.CreateAsync(name!, firstPerson!));
        Assert.Equal(string.IsNullOrWhiteSpace(name) ? "name" : "displayName", exception.ParamName);

        await using var context = CreateContext();
        Assert.Equal(0, await context.Households.CountAsync());
        Assert.Equal(0, await context.People.CountAsync());
    }

    [Fact]
    public async Task APersonCanBelongWithoutParticipatingInHouseholdFinances()
    {
        var household = await _service.CreateAsync("Household A", "Person A");
        await _service.AddPersonAsync(household.Id, "Person B", participatesInHouseholdFinances: false);

        await using var context = CreateContext();
        var people = await context.People.Where(person => person.HouseholdId == household.Id).ToListAsync();
        Assert.Equal(2, people.Count);
        Assert.False(Assert.Single(people, person => person.DisplayName == "Person B")
            .ParticipatesInHouseholdFinances);
    }

    [Fact]
    public async Task TheFinalPersonCannotBeRemoved()
    {
        var household = await _service.CreateAsync("Household A", "Person A");

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            _service.RemovePersonAsync(household.Id, Assert.Single(household.People).Id));

        await using var context = CreateContext();
        Assert.Equal(1, await context.People.CountAsync());
    }

    [Fact]
    public async Task AParticipantInAnotherHouseholdCannotSatisfyTheMinimum()
    {
        var household = await _service.CreateAsync("Household A", "Person A");
        await _service.CreateAsync("Household B", "Person B");

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            _service.SetPersonParticipationAsync(household.Id, Assert.Single(household.People).Id, false));

        await using var context = CreateContext();
        Assert.True(await context.People.Where(person => person.HouseholdId == household.Id)
            .Select(person => person.ParticipatesInHouseholdFinances).SingleAsync());
    }

    [Fact]
    public async Task ARemainingNonparticipantDoesNotPermitRemovingTheOnlyParticipant()
    {
        var household = await _service.CreateAsync("Household A", "Person A");
        await _service.AddPersonAsync(household.Id, "Person B", false);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            _service.RemovePersonAsync(household.Id, Assert.Single(household.People).Id));

        await using var context = CreateContext();
        Assert.Equal(2, await context.People.CountAsync());
    }

    [Fact]
    public async Task ParticipationCanBeTransferredBeforeRemovingAPerson()
    {
        var household = await _service.CreateAsync("Household A", "Person A");
        var firstPersonId = Assert.Single(household.People).Id;
        var updated = await _service.AddPersonAsync(household.Id, "Person B", true);
        var secondPersonId = Assert.Single(updated.People, person => person.Id != firstPersonId).Id;

        await _service.SetPersonParticipationAsync(household.Id, firstPersonId, false);
        await _service.RemovePersonAsync(household.Id, firstPersonId);

        await using var context = CreateContext();
        var remaining = await context.People.SingleAsync();
        Assert.Equal(secondPersonId, remaining.Id);
        Assert.True(remaining.ParticipatesInHouseholdFinances);
    }

    [Fact]
    public async Task APersonFromAnotherHouseholdCannotBeChangedThroughThisHousehold()
    {
        var household = await _service.CreateAsync("Household A", "Person A");
        var other = await _service.CreateAsync("Household B", "Person B");

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            _service.SetPersonParticipationAsync(household.Id, Assert.Single(other.People).Id, false));

        await using var context = CreateContext();
        Assert.Equal(2, await context.People.CountAsync(person => person.ParticipatesInHouseholdFinances));
    }

    [Fact]
    public async Task ConcurrentParticipationChangesPreserveAtLeastOneParticipant()
    {
        var household = await _service.CreateAsync("Household A", "Person A");
        var updated = await _service.AddPersonAsync(household.Id, "Person B", true);
        var barrier = new ConcurrentSaveBarrier();
        var service = new HouseholdService(new InterceptingContextFactory(_database, barrier));
        var changes = updated.People.Select(person => Record.ExceptionAsync(() =>
            service.SetPersonParticipationAsync(household.Id, person.Id, false))).ToArray();
        var outcomes = await Task.WhenAll(changes);
        Assert.Equal(2, barrier.Arrivals);
        Assert.Single(outcomes, exception => exception is null);
        Assert.IsType<BusinessRuleViolationException>(Assert.Single(outcomes, exception => exception is not null));

        await using var context = CreateContext();
        Assert.Equal(1, await context.People.CountAsync(person => person.ParticipatesInHouseholdFinances));
    }

    [Fact]
    public async Task AFailedPersonInsertRollsBackTheNewHousehold()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION "FailPersonInsertFunction"() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'Synthetic insert failure';
            END;
            $$;
            CREATE TRIGGER "FailPersonInsert" BEFORE INSERT ON "People"
            FOR EACH ROW EXECUTE FUNCTION "FailPersonInsertFunction"();
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() => _service.CreateAsync("Household A", "Person A"));

        Assert.Equal(0, await context.Households.CountAsync());
        Assert.Equal(0, await context.People.CountAsync());
    }

    [Fact]
    public async Task PostgresRejectsAnOrphanPerson()
    {
        await using var context = CreateContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "People" ("Id", "HouseholdId", "DisplayName", "ParticipatesInHouseholdFinances")
                VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {"Person A"}, {true})
                """));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Fact]
    public async Task PostgresPreventsCascadingDeletionOfAReferencedHousehold()
    {
        var household = await _service.CreateAsync("Household A", "Person A");
        await using var context = CreateContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "Households" WHERE "Id" = {household.Id}
                """));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, exception.SqlState);
        Assert.Equal(1, await context.Households.CountAsync());
        Assert.Equal(1, await context.People.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PostgresRejectsMissingOrBlankPersonNames(string? displayName)
    {
        var household = await _service.CreateAsync("Household A", "Person A");
        await using var context = CreateContext();

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "People" ("Id", "HouseholdId", "DisplayName", "ParticipatesInHouseholdFinances")
                VALUES ({Guid.NewGuid()}, {household.Id}, {displayName}, {false})
                """));

        Assert.Equal(displayName is null ? PostgresErrorCodes.NotNullViolation : PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(1, await context.People.CountAsync());
    }

    [Fact]
    public async Task MigrationMatchesTheModelAndCanBeReappliedAfterRollback()
    {
        await using var context = CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync());

        await context.GetService<IMigrator>().MigrateAsync("0");
        await context.Database.MigrateAsync();

        await _service.CreateAsync("Household A", "Person A");
        Assert.Equal(1, await context.Households.CountAsync());
        Assert.Equal(1, await context.People.CountAsync());
    }

    private HearthcastDbContext CreateContext()
    {
        return _database.CreateDbContext();
    }
}
