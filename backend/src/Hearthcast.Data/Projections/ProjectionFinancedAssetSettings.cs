namespace Hearthcast.Data.Projections;

/// <summary>Regular and extra payments use the financed asset's native currency.</summary>
public sealed record ProjectionFinancedAssetSettings(Guid FinancedAssetId, decimal MonthlyLoanPaymentInAssetCurrency,
    decimal AnnualLoanInterestPercent, decimal AnnualAssetAppreciationPercent,
    decimal ExtraMonthlyLoanPaymentInAssetCurrency, string InvestmentCurrencyAfterPayoff);
