using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Spending;

public sealed record SpendingDiscountValues(Guid ExpenseCategoryId, decimal Amount, string? Description = null);
internal sealed record SpendingDiscountInput(ExpenseCategory Category, decimal Amount, string? Description = null);

public sealed class SpendingDiscount
{
    private SpendingDiscount() { }
    internal SpendingDiscount(SpendingRecord parent, int position, SpendingDiscountInput input)
    {
        if (input.Category.HouseholdId != parent.HouseholdId) throw new InvalidValueException("Discount category must belong to this household.");
        Id = Guid.NewGuid(); HouseholdId = parent.HouseholdId; SpendingRecordId = parent.Id; Position = position;
        ExpenseCategoryId = input.Category.Id; ExpenseCategoryName = input.Category.Name;
        CurrencyCode = parent.CurrencyCode; CurrencyMinorUnits = parent.CurrencyMinorUnits;
        Amount = SpendingValueRules.Amount(input.Amount, CurrencyMinorUnits);
        if (Amount == 0) throw new InvalidValueException("Enter a positive receipt discount.");
        Description = SpendingSetupRules.Notes(input.Description);
    }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid SpendingRecordId { get; private set; }
    public int Position { get; private set; }
    public Guid ExpenseCategoryId { get; private set; }
    public string ExpenseCategoryName { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public int CurrencyMinorUnits { get; private set; }
    public decimal Amount { get; private set; }
    public string? Description { get; private set; }
}
