using Hearthcast.Data.Households;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Classification;

public sealed class Category
{
    private Category()
    {
    }

    internal Category(Household household, string name, FinancialType financialType)
    {
        if (!Enum.IsDefined(financialType))
        {
            throw new ArgumentOutOfRangeException(nameof(financialType), "Select Asset or Debt.");
        }

        Id = Guid.NewGuid();
        Household = household;
        HouseholdId = household.Id;
        Rename(name);
        FinancialType = financialType;
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Household Household { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public FinancialType FinancialType { get; private set; }

    public void Rename(string name)
    {
        Name = RequiredText.Trim(name);
    }
}
