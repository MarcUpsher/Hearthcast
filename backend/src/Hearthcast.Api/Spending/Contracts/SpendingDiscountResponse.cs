using Hearthcast.Data.Spending;

namespace Hearthcast.Api.Spending.Contracts;

public sealed record SpendingDiscountResponse(Guid Id, int Position, Guid ExpenseCategoryId, string ExpenseCategoryName, decimal Amount, string? Description)
{
    internal static SpendingDiscountResponse From(SpendingDiscount discount) => new(discount.Id, discount.Position,
        discount.ExpenseCategoryId, discount.ExpenseCategoryName, discount.Amount, discount.Description);
}
