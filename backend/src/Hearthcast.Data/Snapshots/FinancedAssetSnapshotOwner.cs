using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Snapshots;

public sealed class FinancedAssetSnapshotOwner
{
    private FinancedAssetSnapshotOwner()
    {
    }

    internal FinancedAssetSnapshotOwner(FinancedAssetSnapshot asset, SnapshotPerson person, int shareBasisPoints)
    {
        if (person.SnapshotId != asset.SnapshotId)
        {
            throw new InvalidOperationException("Saved associations require people from the same snapshot and eligible allocation recipients.");
        }
        AllocationShareRules.EnsureValid(shareBasisPoints);
        ShareBasisPoints = shareBasisPoints;
        FinancedAssetSnapshotId = asset.Id;
        SnapshotId = asset.SnapshotId;
        SnapshotPersonId = person.Id;
        SnapshotPerson = person;
    }

    public Guid FinancedAssetSnapshotId { get; private set; }
    public Guid SnapshotId { get; private set; }
    public Guid SnapshotPersonId { get; private set; }
    public SnapshotPerson SnapshotPerson { get; private set; } = null!;
    public int ShareBasisPoints { get; private set; }
}
