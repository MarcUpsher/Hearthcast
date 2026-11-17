using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Spending;

public sealed class SpendingItem
{
    private SpendingItem() { }

    internal SpendingItem(ExpenseCategory category, string code, string name, string unitKey,
        string unitLabel, string? notes = null)
    {
        Id = Guid.NewGuid();
        HouseholdId = category.HouseholdId;
        ExpenseCategoryId = category.Id;
        Code = SpendingSetupRules.Code(code);
        UnitKey = SpendingSetupRules.UnitKey(unitKey);
        Rename(name);
        SetUnitLabel(unitLabel);
        SetNotes(notes);
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid ExpenseCategoryId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string UnitKey { get; private set; } = string.Empty;
    public string UnitLabel { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public bool IsRetired { get; private set; }

    public void Rename(string name) => Name = SpendingSetupRules.Text(name);
    public void SetUnitLabel(string label) => UnitLabel = SpendingSetupRules.Text(label);
    public void SetNotes(string? notes) => Notes = SpendingSetupRules.Notes(notes);
    public void SetRetired(bool isRetired) => IsRetired = isRetired;

    public void SetCategory(ExpenseCategory category)
    {
        if (category.HouseholdId != HouseholdId)
            throw new InvalidValueException("The category must belong to this household.", nameof(category));
        ExpenseCategoryId = category.Id;
    }
}
