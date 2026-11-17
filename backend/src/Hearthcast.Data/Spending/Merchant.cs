using Hearthcast.Data.Households;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Spending;

public sealed class Merchant
{
    private Merchant() { }

    internal Merchant(Household household, string name)
    {
        Id = Guid.NewGuid();
        HouseholdId = household.Id;
        Rename(name);
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsRetired { get; private set; }

    public void Rename(string name) => Name = SpendingSetupRules.Text(name);
    public void SetRetired(bool isRetired) => IsRetired = isRetired;
}
