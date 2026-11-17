namespace Hearthcast.Data.Spending;

public sealed record SpendingRecordValues(Guid MerchantId, DateOnly SpendingDate, string CurrencyCode,
    IReadOnlyList<SpendingLineValues> Lines, SpendingRecordKind Kind = SpendingRecordKind.Payment,
    Guid? OriginalPaymentId = null, decimal? ReceiptTotal = null, string? ExternalReference = null, string? Notes = null);

/// <summary>Null revision means create only; a supplied revision authorises replacing an existing usual price.</summary>
public sealed record RememberSpendingPrice(int LinePosition, decimal ChosenUnitPrice, Guid? ExpectedRevision = null);

public sealed record SpendingRecordCaptureResult(SpendingRecord Record, bool WasCreated);
