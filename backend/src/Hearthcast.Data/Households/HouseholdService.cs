using System.Data;
using Hearthcast.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Households;

public sealed class HouseholdService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public async Task<Household> CreateAsync(
        string name,
        string firstPersonDisplayName,
        CancellationToken cancellationToken = default)
    {
        var household = Household.Create(name, firstPersonDisplayName);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Households.Add(household);
        await context.SaveChangesAsync(cancellationToken);
        return household;
    }

    public Task<Household> AddPersonAsync(
        Guid householdId,
        string displayName,
        bool participatesInHouseholdFinances,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(householdId,
            household => household.AddPerson(displayName, participatesInHouseholdFinances),
            cancellationToken);

    public Task<Household> SetPersonParticipationAsync(
        Guid householdId,
        Guid personId,
        bool participatesInHouseholdFinances,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(householdId,
            household => household.SetPersonParticipation(personId, participatesInHouseholdFinances),
            cancellationToken);

    public Task<Household> RemovePersonAsync(
        Guid householdId,
        Guid personId,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(householdId,
            (household, context) => context.People.Remove(household.RemovePerson(personId)),
            cancellationToken);

    private Task<Household> UpdateAsync(
        Guid householdId,
        Action<Household> change,
        CancellationToken cancellationToken) =>
        UpdateAsync(householdId, (household, _) => change(household), cancellationToken);

    private async Task<Household> UpdateAsync(
        Guid householdId,
        Action<Household, HearthcastDbContext> change,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        // Load the complete household inside the transaction, using fresh tracked state.
        var household = await context.Households.Include(household => household.People)
            .SingleOrDefaultAsync(household => household.Id == householdId, cancellationToken)
            ?? throw new KeyNotFoundException("The household does not exist.");

        change(household, context);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return household;
    }
}
