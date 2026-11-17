namespace Hearthcast.Data.Spending;

public sealed record SpendingRecordValues(Guid MerchantId, DateOnly SpendingDate, string CurrencyCode,
    IReadOnlyList<SpendingLineValues> Lines, SpendingRecordKind Kind = SpendingRecordKind.Payment,
    Guid? OriginalPaymentId = null, decimal? ReceiptTotal = null, string? ExternalReference = null, string? Notes = null);
