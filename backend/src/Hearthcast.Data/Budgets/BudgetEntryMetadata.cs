using Hearthcast.Data.Spending;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Budgets;

// Resolved setup or saved historical context. The service must resolve identities in household scope;
// composite FKs enforce their household at persistence. Current setup names are never read on carry.
internal sealed record BudgetEntryMetadata(Guid HouseholdId, Guid BudgetItemId, string ItemCode, string ItemName,
    Guid ExpenseCategoryId, string ExpenseCategoryName, string CurrencyCode, BudgetFrequency Frequency,
    int IntervalCount, Guid? MerchantId = null, string? MerchantName = null, string? Notes = null)
{
    internal static BudgetEntryMetadata FromSetup(BudgetItem item, ExpenseCategory category, Merchant? merchant = null)
    {
        if (item.HouseholdId != category.HouseholdId || item.ExpenseCategoryId != category.Id
            || item.MerchantId != merchant?.Id || (merchant is not null && merchant.HouseholdId != item.HouseholdId))
            throw new InvalidValueException("Resolve the item's category and optional merchant in its household.");
        return new(item.HouseholdId, item.Id, item.Code, item.Name, category.Id, category.Name,
            item.CurrencyCode, item.Frequency, item.IntervalCount, merchant?.Id, merchant?.Name, item.Notes);
    }

    internal static BudgetEntryMetadata FromEntry(BudgetEntry entry) => new(entry.HouseholdId, entry.BudgetItemId,
        entry.ItemCode, entry.ItemName, entry.ExpenseCategoryId, entry.ExpenseCategoryName, entry.CurrencyCode,
        entry.Frequency, entry.IntervalCount, entry.MerchantId, entry.MerchantName, entry.Notes);
}
