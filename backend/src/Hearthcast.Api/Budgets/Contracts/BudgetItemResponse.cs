using System.Text.Json.Serialization;
using Hearthcast.Data.Budgets;

namespace Hearthcast.Api.Budgets.Contracts;

public sealed record BudgetItemResponse(Guid Id, Guid HouseholdId, Guid ExpenseCategoryId, string Code, string Name,
    Guid? MerchantId, string CurrencyCode,
    [property: JsonConverter(typeof(BudgetFrequencyJsonConverter))] BudgetFrequency Frequency,
    int IntervalCount, string? Notes, bool IsRetired)
{
    internal static BudgetItemResponse From(BudgetItem item) => new(item.Id, item.HouseholdId, item.ExpenseCategoryId,
        item.Code, item.Name, item.MerchantId, item.CurrencyCode, item.Frequency, item.IntervalCount, item.Notes, item.IsRetired);
}
