using Hearthcast.Data.Spending;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Budgets;

public sealed class BudgetItem
{
    private BudgetItem() { }

    internal BudgetItem(ExpenseCategory category, string code, string name, string currencyCode,
        BudgetFrequency frequency, int intervalCount = 1, Merchant? merchant = null, string? notes = null,
        DateOnly? paymentAnchorDate = null, BudgetItemKind kind = BudgetItemKind.Commitment)
    {
        Id = Guid.NewGuid();
        HouseholdId = category.HouseholdId;
        if (!Enum.IsDefined(kind)) throw new InvalidValueException("Supply a defined budget item kind.");
        Kind = kind;
        ExpenseCategoryId = category.Id;
        Code = SpendingSetupRules.Code(code);
        Rename(name);
        SetTerms(currencyCode, frequency, intervalCount);
        SetMerchant(merchant);
        SetNotes(notes);
        SetPaymentAnchorDate(paymentAnchorDate);
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public BudgetItemKind Kind { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public Guid ExpenseCategoryId { get; private set; }
    public Guid? MerchantId { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public BudgetFrequency Frequency { get; private set; }
    public int IntervalCount { get; private set; }
    public DateOnly? PaymentAnchorDate { get; private set; }
    public string? Notes { get; private set; }
    public bool IsRetired { get; private set; }

    public void Rename(string name) => Name = SpendingSetupRules.Text(name);
    public void SetNotes(string? notes) => Notes = SpendingSetupRules.Notes(notes);
    public void SetRetired(bool isRetired) => IsRetired = isRetired;
    public void SetPaymentAnchorDate(DateOnly? date)
    {
        if (Kind == BudgetItemKind.Allowance && date.HasValue) throw new InvalidValueException("Allowances do not have payment dates.");
        PaymentAnchorDate = date.HasValue ? BudgetRules.Date(date.Value) : null;
    }

    public void SetCategory(ExpenseCategory category)
    {
        if (category.HouseholdId != HouseholdId) throw new InvalidValueException("The category must belong to this household.");
        ExpenseCategoryId = category.Id;
    }

    public void SetMerchant(Merchant? merchant)
    {
        if (Kind == BudgetItemKind.Allowance && merchant is not null) throw new InvalidValueException("Allowances do not have merchants.");
        if (merchant is not null && merchant.HouseholdId != HouseholdId)
            throw new InvalidValueException("The merchant must belong to this household.");
        MerchantId = merchant?.Id;
    }

    public void SetTerms(string currencyCode, BudgetFrequency frequency, int intervalCount)
    {
        var currency = BudgetRules.Currency(currencyCode, CurrencyCode);
        BudgetRules.Recurrence(frequency, intervalCount);
        CurrencyCode = currency;
        Frequency = frequency;
        IntervalCount = intervalCount;
    }
}
