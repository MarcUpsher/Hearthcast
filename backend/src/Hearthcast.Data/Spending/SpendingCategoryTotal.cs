namespace Hearthcast.Data.Spending;

public sealed record SpendingCategoryTotal(Guid ExpenseCategoryId, string CategoryName,
    string CurrencyCode, int CurrencyMinorUnits, decimal NetAmount);
