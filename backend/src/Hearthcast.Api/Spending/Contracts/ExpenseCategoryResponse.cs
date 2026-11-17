using Hearthcast.Data.Spending;

namespace Hearthcast.Api.Spending.Contracts;

public sealed record ExpenseCategoryResponse(Guid Id, Guid HouseholdId, string Name, bool IsRetired)
{
    internal static ExpenseCategoryResponse From(ExpenseCategory category) =>
        new(category.Id, category.HouseholdId, category.Name, category.IsRetired);
}
