namespace Hearthcast.Data.Spending;

public sealed record SpendingLastPaidPrice(Guid SpendingRecordId, Guid SpendingLineId, DateOnly SpendingDate,
    string CurrencyCode, string UnitKey, string UnitLabel, decimal Quantity, decimal UnitPrice,
    decimal NetAmount, PromotionStatus PromotionStatus)
{
    public decimal EffectiveUnitPrice => NetAmount / Quantity;
}
