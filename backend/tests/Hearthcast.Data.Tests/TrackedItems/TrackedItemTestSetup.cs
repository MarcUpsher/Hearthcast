using Hearthcast.Data.Classification;
using Hearthcast.Data.Households;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;

namespace Hearthcast.Data.Tests.TrackedItems;

internal sealed record TrackedItemTestSetup(Guid HouseholdId, Guid GroupId, Guid HolderPersonId, Guid OtherPersonId)
{
    public static async Task<TrackedItemTestSetup> CreateAsync(
        PostgresTestDatabase database,
        bool otherPersonParticipates = true,
        FinancialType financialType = FinancialType.Asset)
    {
        var households = new HouseholdService(database);
        var household = await households.CreateAsync("Household A", "Person A");
        var holderId = household.People.Single().Id;
        var updated = await households.AddPersonAsync(household.Id, "Person B", otherPersonParticipates);
        var otherId = updated.People.Single(person => person.Id != holderId).Id;
        var classification = new ClassificationService(database);
        var category = await classification.CreateCategoryAsync(household.Id, "Category A", financialType);
        var group = await classification.CreateGroupAsync(household.Id, category.Id, "Group A");
        return new TrackedItemTestSetup(household.Id, group.Id, holderId, otherId);
    }

    public Task<TrackedItem> CreateItemAsync(
        TrackedItemService service,
        bool includeInHousehold = true,
        IReadOnlyCollection<TrackedItemAllocationShare>? allocations = null)
    {
        return service.CreateAsync(HouseholdId, GroupId, HolderPersonId,
            "Item A", "GBP", "GB", includeInHousehold, allocations);
    }
}
