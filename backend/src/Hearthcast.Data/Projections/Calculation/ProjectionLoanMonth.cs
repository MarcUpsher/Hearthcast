namespace Hearthcast.Data.Projections.Calculation;

/// <summary>Per-asset month-end values and cash flows, native except the explicitly named destination investment amount.</summary>
public sealed record ProjectionLoanMonth(Guid FinancedAssetId, string CurrencyCode, decimal AssetValue,
    decimal LoanBalance, decimal InterestCharged, decimal PaymentApplied, decimal RedirectedToInvestments,
    DateOnly? PayoffDate, string InvestmentCurrencyAfterPayoff, decimal InvestmentAmountRedirected);
