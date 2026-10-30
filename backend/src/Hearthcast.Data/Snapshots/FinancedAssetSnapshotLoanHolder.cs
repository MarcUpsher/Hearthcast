using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Snapshots;

public sealed class FinancedAssetSnapshotLoanHolder
{
    private FinancedAssetSnapshotLoanHolder()
    {
    }

    internal FinancedAssetSnapshotLoanHolder(FinancedAssetSnapshot asset, SnapshotPerson person)
    {
        if (person.SnapshotId != asset.SnapshotId)
        {
            throw new InvalidOperationException("Saved associations require people from the same snapshot and eligible allocation recipients.");
        }
        FinancedAssetSnapshotId = asset.Id;
        SnapshotId = asset.SnapshotId;
        SnapshotPersonId = person.Id;
        SnapshotPerson = person;
    }

    public Guid FinancedAssetSnapshotId { get; private set; }
    public Guid SnapshotId { get; private set; }
    public Guid SnapshotPersonId { get; private set; }
    public SnapshotPerson SnapshotPerson { get; private set; } = null!;
}
