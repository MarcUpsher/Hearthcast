using Hearthcast.Data.Households;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.TrackedItems;

public sealed class TrackedItemHouseholdAllocation
{
    private TrackedItemHouseholdAllocation()
    {
    }

    internal TrackedItemHouseholdAllocation(TrackedItem trackedItem, Person person, int shareBasisPoints)
    {
        Validate(person, trackedItem.HouseholdId, shareBasisPoints);
        TrackedItem = trackedItem;
        TrackedItemId = trackedItem.Id;
        HouseholdId = trackedItem.HouseholdId;
        Person = person;
        PersonId = person.Id;
        ShareBasisPoints = shareBasisPoints;
    }

    public Guid TrackedItemId { get; private set; }
    public TrackedItem TrackedItem { get; private set; } = null!;
    public Guid HouseholdId { get; private set; }
    public Guid PersonId { get; private set; }
    public Person Person { get; private set; } = null!;
    public int ShareBasisPoints { get; private set; }

    internal static void Validate(Person person, Guid householdId, int shareBasisPoints)
    {
        if (person.HouseholdId != householdId)
        {
            throw new ArgumentException("An allocation recipient must belong to the item's household.");
        }

        if (!person.ParticipatesInHouseholdFinances)
        {
            throw new InvalidOperationException("Only household participants can receive an allocation.");
        }

        AllocationShareRules.EnsureValid(shareBasisPoints);
    }

    internal void SetShare(int shareBasisPoints)
    {
        AllocationShareRules.EnsureValid(shareBasisPoints);
        ShareBasisPoints = shareBasisPoints;
    }
}
