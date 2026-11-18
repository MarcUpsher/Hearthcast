using Hearthcast.Data.Spending;

namespace Hearthcast.Api.Spending.Contracts;

public sealed record SpendingCategoryTotalResponse(Guid ExpenseCategoryId, string CategoryName,
    string CurrencyCode, int CurrencyMinorUnits, decimal NetAmount)
{
    internal static SpendingCategoryTotalResponse From(SpendingCategoryTotal total) => new(total.ExpenseCategoryId,
        total.CategoryName, total.CurrencyCode, total.CurrencyMinorUnits, total.NetAmount);
}
