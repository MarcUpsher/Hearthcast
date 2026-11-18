using System.Text.Json.Serialization;
using Hearthcast.Data.Budgets;

namespace Hearthcast.Api.Budgets.Contracts;

public sealed record BudgetEntryResponse(Guid Id, Guid BudgetItemId, string ItemCode, string ItemName,
    Guid ExpenseCategoryId, string ExpenseCategoryName, Guid? MerchantId, string? MerchantName,
    decimal? PlannedAmount, string CurrencyCode,
    [property: JsonConverter(typeof(BudgetFrequencyJsonConverter))] BudgetFrequency Frequency,
    int IntervalCount, string? Notes,
    [property: JsonConverter(typeof(BudgetValueOriginJsonConverter))] BudgetValueOrigin ValueOrigin,
    DateOnly? LastConfirmedDate, Guid? CarriedFromBudgetEntryId)
{
    internal static BudgetEntryResponse From(BudgetEntry entry) => new(entry.Id, entry.BudgetItemId, entry.ItemCode, entry.ItemName,
        entry.ExpenseCategoryId, entry.ExpenseCategoryName, entry.MerchantId, entry.MerchantName, entry.PlannedAmount,
        entry.CurrencyCode, entry.Frequency, entry.IntervalCount, entry.Notes, entry.ValueOrigin, entry.LastConfirmedDate, entry.CarriedFromBudgetEntryId);
}
