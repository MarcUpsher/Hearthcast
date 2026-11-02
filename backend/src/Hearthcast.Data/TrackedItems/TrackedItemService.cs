using Hearthcast.Data.Households;
using Hearthcast.Data.Persistence;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.TrackedItems;

public sealed class TrackedItemService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public Task<TrackedItem> UpdateDetailsAsync(Guid householdId, Guid trackedItemId,
        string name, string? institutionName, string? fundName, bool includeInHousehold,
        IReadOnlyCollection<TrackedItemAllocationShare>? allocations = null, CancellationToken cancellationToken = default) =>
        SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
        {
            var item = await context.TrackedItems.Include(item => item.HolderPerson)
                .Include(item => item.HouseholdAllocations).ThenInclude(allocation => allocation.Person)
                .SingleOrDefaultAsync(item => item.Id == trackedItemId && item.HouseholdId == householdId, token)
                ?? throw new ResourceNotFoundException("The tracked item does not belong to this household.");
            item.Rename(name);
            item.SetMetadata(institutionName, fundName);
            var shares = await ResolveAllocationsAsync(context, item, includeInHousehold, allocations, token);
            context.TrackedItemHouseholdAllocations.RemoveRange(item.SetHouseholdAllocation(includeInHousehold, shares));
            return item;
        }, cancellationToken);

    public Task<TrackedItem> SetMetadataAsync(Guid householdId, Guid trackedItemId,
        string? institutionName, string? fundName, CancellationToken cancellationToken = default) =>
        SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
        {
            var item = await context.TrackedItems.Include(item => item.HouseholdAllocations)
                .SingleOrDefaultAsync(item => item.Id == trackedItemId && item.HouseholdId == householdId, token)
                ?? throw new ResourceNotFoundException("The tracked item does not belong to this household.");
            item.SetMetadata(institutionName, fundName);
            return item;
        }, cancellationToken);

    public Task<TrackedItem> RenameAsync(Guid householdId, Guid trackedItemId, string name,
        CancellationToken cancellationToken = default) =>
        SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
        {
            var item = await context.TrackedItems.Include(item => item.HouseholdAllocations)
                .SingleOrDefaultAsync(item => item.Id == trackedItemId && item.HouseholdId == householdId, token)
                ?? throw new ResourceNotFoundException("The tracked item does not belong to this household.");
            item.Rename(name);
            return item;
        }, cancellationToken);

    public async Task<TrackedItem> CreateAsync(
        Guid householdId,
        Guid groupId,
        Guid holderPersonId,
        string name,
        string currencyCode,
        string countryCode,
        bool includeInHousehold,
        IReadOnlyCollection<TrackedItemAllocationShare>? allocations = null,
        string? institutionName = null,
        string? fundName = null,
        CancellationToken cancellationToken = default)
    {
        return await SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
        {
            var group = await context.ItemGroups.Include(group => group.Category)
                .SingleOrDefaultAsync(group => group.Id == groupId && group.HouseholdId == householdId, token)
                ?? throw new ResourceNotFoundException("The group does not belong to this household.");
            var holder = await context.People.SingleOrDefaultAsync(
                person => person.Id == holderPersonId && person.HouseholdId == householdId, token)
                ?? throw new ResourceNotFoundException("The holder does not belong to this household.");

            var item = new TrackedItem(group, holder, name, currencyCode, countryCode);
            item.SetMetadata(institutionName, fundName);
            var shares = await ResolveAllocationsAsync(context, item, includeInHousehold, allocations, token);
            item.SetHouseholdAllocation(includeInHousehold, shares);
            context.TrackedItems.Add(item);
            return item;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<TrackedItem>> ListAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.Households.AnyAsync(household => household.Id == householdId, cancellationToken))
        {
            throw new ResourceNotFoundException("The household does not exist.");
        }
        return await context.TrackedItems.AsNoTracking().Include(item => item.HouseholdAllocations)
            .Where(item => item.HouseholdId == householdId)
            .OrderBy(item => item.Name).ThenBy(item => item.Id).ToArrayAsync(cancellationToken);
    }

    public async Task<TrackedItem> GetAsync(Guid householdId, Guid trackedItemId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.TrackedItems.AsNoTracking().Include(item => item.HouseholdAllocations)
            .SingleOrDefaultAsync(item => item.HouseholdId == householdId && item.Id == trackedItemId, cancellationToken)
            ?? throw new ResourceNotFoundException("The tracked item does not belong to this household.");
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
                ?? throw new ResourceNotFoundException("The tracked item does not belong to this household.");

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
        return await SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
        {
            var item = await context.TrackedItems.Include(item => item.HouseholdAllocations)
                .SingleOrDefaultAsync(item => item.Id == trackedItemId && item.HouseholdId == householdId, token)
                ?? throw new ResourceNotFoundException("The tracked item does not belong to this household.");
            item.SetRetired(isRetired);
            return item;
        }, cancellationToken);
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
            throw new ResourceNotFoundException("An allocation recipient does not belong to this household.");
        }

        return requested.Select(share =>
            (Person: people[share.PersonId], ShareBasisPoints: share.ShareBasisPoints)).ToArray();
    }
}
