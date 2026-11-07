namespace Hearthcast.Data.Projections.Calculation;

/// <summary>Per-asset month-end values and cash flows, all in the asset's native currency.</summary>
public sealed record ProjectionLoanMonth(Guid FinancedAssetId, string CurrencyCode, decimal AssetValue,
    decimal LoanBalance, decimal InterestCharged, decimal PaymentApplied, decimal RedirectedToInvestments,
    DateOnly? PayoffDate);
