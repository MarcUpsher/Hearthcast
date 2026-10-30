using Hearthcast.Data.Persistence;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Households;

public sealed class HouseholdService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public async Task<Household> GetAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Households.AsNoTracking().Include(household => household.People)
            .SingleOrDefaultAsync(household => household.Id == householdId, cancellationToken)
            ?? throw new ResourceNotFoundException("The household does not exist.");
    }

    public Task<Household> RenameAsync(Guid householdId, string name, CancellationToken cancellationToken = default) =>
        UpdateAsync(householdId, household => household.Rename(name), cancellationToken);

    public Task<Person> CreatePersonAsync(Guid householdId, string displayName, bool participatesInHouseholdFinances,
        CancellationToken cancellationToken = default) =>
        SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
        {
            var household = await context.Households.Include(household => household.People)
                .SingleOrDefaultAsync(household => household.Id == householdId, token)
                ?? throw new ResourceNotFoundException("The household does not exist.");
            return household.AddPerson(displayName, participatesInHouseholdFinances);
        }, cancellationToken);

    public Task<Household> UpdatePersonAsync(Guid householdId, Guid personId, string displayName,
        bool participatesInHouseholdFinances, CancellationToken cancellationToken = default) =>
        UpdateAsync(householdId, async (household, context, token) =>
        {
            household.RenamePerson(personId, displayName);
            if (!participatesInHouseholdFinances)
            {
                await EnsureNoHouseholdAllocationsAsync(context, householdId, personId, token);
            }
            household.SetPersonParticipation(personId, participatesInHouseholdFinances);
        }, cancellationToken);

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
        CancellationToken cancellationToken = default)
    {
        return UpdateAsync(householdId, async (household, context, token) =>
        {
            if (!participatesInHouseholdFinances)
            {
                await EnsureNoHouseholdAllocationsAsync(context, householdId, personId, token);
            }

            household.SetPersonParticipation(personId, participatesInHouseholdFinances);
        }, cancellationToken);
    }

    public Task<Household> RemovePersonAsync(
        Guid householdId,
        Guid personId,
        CancellationToken cancellationToken = default)
    {
        return UpdateAsync(householdId, async (household, context, token) =>
        {
            await EnsureNoHouseholdAllocationsAsync(context, householdId, personId, token);
            if (await context.TrackedItems.AnyAsync(
                item => item.HouseholdId == householdId && item.HolderPersonId == personId, token))
            {
                throw new BusinessRuleViolationException("A person who holds tracked items cannot be removed.");
            }

            if (await context.FinancedAssetOwners.AnyAsync(row => row.HouseholdId == householdId && row.PersonId == personId, token)
                || await context.FinancedAssetLoanHolders.AnyAsync(row => row.HouseholdId == householdId && row.PersonId == personId, token))
            {
                throw new BusinessRuleViolationException("Resolve the person's financed-asset ownership and loan-holder links before removing them.");
            }

            if (await context.SnapshotPeople.AnyAsync(
                person => person.HouseholdId == householdId && person.PersonId == personId, token))
            {
                throw new BusinessRuleViolationException("A person recorded in snapshot history cannot be removed.");
            }

            context.People.Remove(household.RemovePerson(personId));
        }, cancellationToken);
    }

    private Task<Household> UpdateAsync(
        Guid householdId,
        Action<Household> change,
        CancellationToken cancellationToken) =>
        UpdateAsync(householdId, (household, _, _) =>
        {
            change(household);
            return Task.CompletedTask;
        }, cancellationToken);

    private async Task<Household> UpdateAsync(
        Guid householdId,
        Func<Household, HearthcastDbContext, CancellationToken, Task> change,
        CancellationToken cancellationToken)
    {
        return await SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
        {
            // Load the complete household inside the transaction, using fresh tracked state.
            var household = await context.Households.Include(household => household.People)
                .SingleOrDefaultAsync(household => household.Id == householdId, token)
                ?? throw new ResourceNotFoundException("The household does not exist.");

            await change(household, context, token);
            return household;
        }, cancellationToken);
    }

    private static async Task EnsureNoHouseholdAllocationsAsync(
        HearthcastDbContext context, Guid householdId, Guid personId, CancellationToken cancellationToken)
    {
        if (await context.TrackedItemHouseholdAllocations.AnyAsync(
            allocation => allocation.HouseholdId == householdId && allocation.PersonId == personId, cancellationToken)
            || await context.FinancedAssetHouseholdAllocations.AnyAsync(
                allocation => allocation.HouseholdId == householdId && allocation.PersonId == personId, cancellationToken))
        {
            throw new BusinessRuleViolationException("Resolve the person's household allocations before changing participation or removing them.");
        }
    }
}
