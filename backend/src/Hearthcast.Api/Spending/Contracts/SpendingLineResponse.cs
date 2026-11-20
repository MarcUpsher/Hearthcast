using System.Text.Json.Serialization;
using Hearthcast.Data.Spending;

namespace Hearthcast.Api.Spending.Contracts;

public sealed record SpendingLineResponse(Guid Id, int Position, Guid ExpenseCategoryId, string ExpenseCategoryName,
    Guid? SpendingItemId, string? ItemCode, string? ItemName, string? UnitKey, string? UnitLabel,
    string? Description, string? ReceiptDescription, string? MerchantItemReference, decimal? Quantity,
    decimal? UnitPrice, decimal NetAmount, decimal? SuggestedAmount, decimal? EffectiveUnitPrice,
    [property: JsonConverter(typeof(PromotionStatusJsonConverter))] PromotionStatus PromotionStatus, string? Notes, bool IsAdditionalCost = false)
{
    internal static SpendingLineResponse From(SpendingLine line) => new(line.Id, line.Position, line.ExpenseCategoryId,
        line.ExpenseCategoryName, line.SpendingItemId, line.ItemCode, line.ItemName, line.UnitKey, line.UnitLabel,
        line.Description, line.ReceiptDescription, line.MerchantItemReference, line.Quantity, line.UnitPrice,
        line.NetAmount, line.SuggestedAmount, line.EffectiveUnitPrice, line.PromotionStatus, line.Notes, line.IsAdditionalCost);
}
