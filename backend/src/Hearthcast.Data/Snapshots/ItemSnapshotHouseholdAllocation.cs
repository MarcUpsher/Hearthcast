using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Snapshots;

public sealed class ItemSnapshotHouseholdAllocation
{
    private ItemSnapshotHouseholdAllocation()
    {
    }

    internal ItemSnapshotHouseholdAllocation(ItemSnapshot item, SnapshotPerson person, int shareBasisPoints)
    {
        if (person.SnapshotId != item.SnapshotId || !person.ParticipatesInHouseholdFinances)
        {
            throw new InvalidOperationException("Saved allocations must use participants from the same snapshot.");
        }

        AllocationShareRules.EnsureValid(shareBasisPoints);
        ItemSnapshotId = item.Id;
        SnapshotId = item.SnapshotId;
        SnapshotPersonId = person.Id;
        SnapshotPerson = person;
        ShareBasisPoints = shareBasisPoints;
    }

    public Guid ItemSnapshotId { get; private set; }
    public Guid SnapshotId { get; private set; }
    public Guid SnapshotPersonId { get; private set; }
    public SnapshotPerson SnapshotPerson { get; private set; } = null!;
    public int ShareBasisPoints { get; private set; }
}
