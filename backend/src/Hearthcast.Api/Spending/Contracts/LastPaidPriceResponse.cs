using System.Text.Json.Serialization;
using Hearthcast.Data.Spending;

namespace Hearthcast.Api.Spending.Contracts;

public sealed record LastPaidPriceResponse(Guid SpendingRecordId, Guid SpendingLineId, DateOnly SpendingDate,
    string CurrencyCode, string UnitKey, string UnitLabel, decimal Quantity, decimal UnitPrice, decimal NetAmount,
    decimal EffectiveUnitPrice, [property: JsonConverter(typeof(PromotionStatusJsonConverter))] PromotionStatus PromotionStatus, bool HasReceiptDiscount = false)
{
    internal static LastPaidPriceResponse From(SpendingLastPaidPrice price) => new(price.SpendingRecordId,
        price.SpendingLineId, price.SpendingDate, price.CurrencyCode, price.UnitKey, price.UnitLabel,
        price.Quantity, price.UnitPrice, price.NetAmount, price.EffectiveUnitPrice, price.PromotionStatus, price.HasReceiptDiscount);
}
