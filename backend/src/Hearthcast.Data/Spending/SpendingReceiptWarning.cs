namespace Hearthcast.Data.Spending;

public sealed record SpendingReceiptWarning(Guid SpendingRecordId, DateOnly SpendingDate,
    string MerchantName, string CurrencyCode, decimal ReceiptTotal, decimal LineTotal, decimal Difference);
