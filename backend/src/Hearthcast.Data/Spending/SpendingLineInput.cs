namespace Hearthcast.Data.Spending;

// Resolved setup references, not an API request. Source loading belongs to the service layer.
internal sealed record SpendingLineInput(ExpenseCategory Category, decimal NetAmount,
    SpendingItem? Item = null, decimal? Quantity = null, decimal? UnitPrice = null,
    string? UnitKey = null, string? UnitLabel = null, string? Description = null,
    string? ReceiptDescription = null, string? MerchantItemReference = null,
    PromotionStatus PromotionStatus = PromotionStatus.Unknown, string? Notes = null);
