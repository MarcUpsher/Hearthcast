using Hearthcast.Data.Households;
using Hearthcast.Data.Persistence;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.TrackedItems;

public sealed class TrackedItemService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public async Task<TrackedItem> CreateAsync(
        Guid householdId,
        Guid groupId,
        Guid holderPersonId,
        string name,
        string currencyCode,
        string countryCode,
        bool includeInHousehold,
        IReadOnlyCollection<TrackedItemAllocationShare>? allocations = null,
        CancellationToken cancellationToken = default)
    {
        return await SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
        {
            var group = await context.ItemGroups.Include(group => group.Category)
                .SingleOrDefaultAsync(group => group.Id == groupId && group.HouseholdId == householdId, token)
                ?? throw new KeyNotFoundException("The group does not belong to this household.");
            var holder = await context.People.SingleOrDefaultAsync(
                person => person.Id == holderPersonId && person.HouseholdId == householdId, token)
                ?? throw new KeyNotFoundException("The holder does not belong to this household.");

            var item = new TrackedItem(group, holder, name, currencyCode, countryCode);
            var shares = await ResolveAllocationsAsync(context, item, includeInHousehold, allocations, token);
            item.SetHouseholdAllocation(includeInHousehold, shares);
            context.TrackedItems.Add(item);
            return item;
        }, cancellationToken);
    }

    public async Task<TrackedItem> SetHouseholdAllocationAsync(
        Guid householdId,
        Guid trackedItemId,
        bool includeInHousehold,
        IReadOnlyCollection<TrackedItemAllocationShare>? allocations = null,
        CancellationToken cancellationToken = default)
    {
        return await SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
        {
            var item = await context.TrackedItems.Include(item => item.HolderPerson)
                .Include(item => item.HouseholdAllocations).ThenInclude(allocation => allocation.Person)
                .SingleOrDefaultAsync(item => item.Id == trackedItemId && item.HouseholdId == householdId, token)
                ?? throw new KeyNotFoundException("The tracked item does not belong to this household.");

            var shares = await ResolveAllocationsAsync(context, item, includeInHousehold, allocations, token);
            var removed = item.SetHouseholdAllocation(includeInHousehold, shares);
            context.TrackedItemHouseholdAllocations.RemoveRange(removed);
            return item;
        }, cancellationToken);
    }

    public async Task<TrackedItem> SetRetiredAsync(
        Guid householdId,
        Guid trackedItemId,
        bool isRetired,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var item = await context.TrackedItems.SingleOrDefaultAsync(
            item => item.Id == trackedItemId && item.HouseholdId == householdId, cancellationToken)
            ?? throw new KeyNotFoundException("The tracked item does not belong to this household.");

        item.SetRetired(isRetired);
        await context.SaveChangesAsync(cancellationToken);
        return item;
    }

    private static async Task<(Person Person, int ShareBasisPoints)[]> ResolveAllocationsAsync(
        HearthcastDbContext context,
        TrackedItem item,
        bool includeInHousehold,
        IReadOnlyCollection<TrackedItemAllocationShare>? allocations,
        CancellationToken cancellationToken)
    {
        if (allocations is null)
        {
            return includeInHousehold ? [(item.HolderPerson, AllocationShareRules.FullShareBasisPoints)] : [];
        }

        var requested = allocations.ToArray();
        var personIds = requested.Select(share => share.PersonId).Distinct().ToArray();
        var people = await context.People
            .Where(person => person.HouseholdId == item.HouseholdId && personIds.Contains(person.Id))
            .ToDictionaryAsync(person => person.Id, cancellationToken);
        if (people.Count != personIds.Length)
        {
            throw new KeyNotFoundException("An allocation recipient does not belong to this household.");
        }

        return requested.Select(share =>
            (Person: people[share.PersonId], ShareBasisPoints: share.ShareBasisPoints)).ToArray();
    }
}
