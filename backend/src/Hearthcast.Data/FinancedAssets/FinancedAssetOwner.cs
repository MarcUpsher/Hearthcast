using Hearthcast.Data.Households;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.FinancedAssets;

public sealed class FinancedAssetOwner
{
    private FinancedAssetOwner()
    {
    }

    internal FinancedAssetOwner(FinancedAsset asset, Person person, int shareBasisPoints)
    {
        FinancedAsset = asset;
        FinancedAssetId = asset.Id;
        HouseholdId = asset.HouseholdId;
        Person = person;
        PersonId = person.Id;
        SetShare(shareBasisPoints);
    }

    public Guid FinancedAssetId { get; private set; }
    public FinancedAsset FinancedAsset { get; private set; } = null!;
    public Guid HouseholdId { get; private set; }
    public Guid PersonId { get; private set; }
    public Person Person { get; private set; } = null!;
    public int ShareBasisPoints { get; private set; }

    internal void SetShare(int shareBasisPoints)
    {
        AllocationShareRules.EnsureValid(shareBasisPoints);
        ShareBasisPoints = shareBasisPoints;
    }
}
