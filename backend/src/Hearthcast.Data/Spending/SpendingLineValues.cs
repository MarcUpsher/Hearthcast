namespace Hearthcast.Data.Spending;

public sealed record SpendingLineValues(Guid ExpenseCategoryId, decimal NetAmount,
    Guid? SpendingItemId = null, decimal? Quantity = null, decimal? UnitPrice = null,
    string? UnitKey = null, string? UnitLabel = null, string? Description = null,
    string? ReceiptDescription = null, string? MerchantItemReference = null,
    PromotionStatus PromotionStatus = PromotionStatus.Unknown, string? Notes = null);
