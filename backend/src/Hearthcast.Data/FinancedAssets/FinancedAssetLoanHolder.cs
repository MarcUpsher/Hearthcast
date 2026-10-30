using Hearthcast.Data.Households;

namespace Hearthcast.Data.FinancedAssets;

public sealed class FinancedAssetLoanHolder
{
    private FinancedAssetLoanHolder()
    {
    }

    internal FinancedAssetLoanHolder(FinancedAsset asset, Person person)
    {
        FinancedAsset = asset;
        FinancedAssetId = asset.Id;
        HouseholdId = asset.HouseholdId;
        Person = person;
        PersonId = person.Id;
    }

    public Guid FinancedAssetId { get; private set; }
    public FinancedAsset FinancedAsset { get; private set; } = null!;
    public Guid HouseholdId { get; private set; }
    public Guid PersonId { get; private set; }
    public Person Person { get; private set; } = null!;
}
