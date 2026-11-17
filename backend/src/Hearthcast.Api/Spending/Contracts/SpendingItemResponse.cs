using Hearthcast.Data.Spending;

namespace Hearthcast.Api.Spending.Contracts;

public sealed record SpendingItemResponse(Guid Id, Guid HouseholdId, Guid ExpenseCategoryId, string Code,
    string Name, string UnitKey, string UnitLabel, string? Notes, bool IsRetired)
{
    internal static SpendingItemResponse From(SpendingItem item) => new(item.Id, item.HouseholdId,
        item.ExpenseCategoryId, item.Code, item.Name, item.UnitKey, item.UnitLabel, item.Notes, item.IsRetired);
}
