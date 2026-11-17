using System.Text.Json.Serialization;
using Hearthcast.Data.Spending;

namespace Hearthcast.Api.Spending.Contracts;

public sealed record SpendingRecordResponse(Guid Id, Guid HouseholdId,
    [property: JsonConverter(typeof(SpendingRecordKindJsonConverter))] SpendingRecordKind Kind,
    DateOnly SpendingDate, Guid MerchantId, string MerchantName, string CurrencyCode, int CurrencyMinorUnits,
    decimal? ReceiptTotal, string? ExternalReference, string? Notes, Guid? OriginalPaymentId,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, Guid Revision, DateTimeOffset? VoidedAtUtc,
    bool IsVoided, decimal TotalAmount, decimal CountedAmount, decimal? ReceiptDifference,
    string ReconciliationStatus, IReadOnlyList<SpendingLineResponse> Lines, bool? OriginalPaymentIsVoided = null)
{
    internal static SpendingRecordResponse From(SpendingRecord record, bool? originalPaymentIsVoided = null) => new(record.Id, record.HouseholdId,
        record.Kind, record.SpendingDate, record.MerchantId, record.MerchantName, record.CurrencyCode,
        record.CurrencyMinorUnits, record.ReceiptTotal, record.ExternalReference, record.Notes,
        record.OriginalSpendingRecordId, record.CreatedAtUtc, record.UpdatedAtUtc, record.Revision,
        record.VoidedAtUtc, record.IsVoided, record.TotalAmount, record.CountedAmount, record.ReceiptDifference,
        record.ReconciliationStatus, record.Lines.OrderBy(l => l.Position).Select(SpendingLineResponse.From).ToArray(), originalPaymentIsVoided);
}
