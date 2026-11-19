using System.Text.Json.Serialization;
using Hearthcast.Data.Budgets;

namespace Hearthcast.Api.Budgets.Contracts;

public sealed record BudgetItemResponse(Guid Id, Guid HouseholdId, Guid ExpenseCategoryId, string Code, string Name,
    Guid? MerchantId, string CurrencyCode,
    [property: JsonConverter(typeof(BudgetFrequencyJsonConverter))] BudgetFrequency Frequency,
    int IntervalCount, string? Notes, bool IsRetired, DateOnly? PaymentAnchorDate = null,
    [property: JsonConverter(typeof(BudgetItemKindJsonConverter))] BudgetItemKind Kind = BudgetItemKind.Commitment)
{
    internal static BudgetItemResponse From(BudgetItem item) => new(item.Id, item.HouseholdId, item.ExpenseCategoryId,
        item.Code, item.Name, item.MerchantId, item.CurrencyCode, item.Frequency, item.IntervalCount, item.Notes, item.IsRetired, item.PaymentAnchorDate, item.Kind);
}
