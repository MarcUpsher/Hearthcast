using Hearthcast.Data.Spending;

namespace Hearthcast.Api.Spending.Contracts;

public sealed record SpendingReceiptWarningResponse(Guid SpendingRecordId, DateOnly SpendingDate,
    string MerchantName, string CurrencyCode, decimal ReceiptTotal, decimal LineTotal, decimal Difference)
{
    internal static SpendingReceiptWarningResponse From(SpendingReceiptWarning warning) => new(warning.SpendingRecordId,
        warning.SpendingDate, warning.MerchantName, warning.CurrencyCode, warning.ReceiptTotal, warning.LineTotal,
        warning.Difference);
}
