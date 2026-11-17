namespace Hearthcast.Data.Budgets;

public sealed record BudgetEntryMetadataValues(string ItemCode, string ItemName, Guid ExpenseCategoryId,
    string ExpenseCategoryName, string CurrencyCode, BudgetFrequency Frequency, int IntervalCount,
    Guid? MerchantId = null, string? MerchantName = null, string? Notes = null);
