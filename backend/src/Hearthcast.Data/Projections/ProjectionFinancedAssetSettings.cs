namespace Hearthcast.Data.Projections;

/// <param name="MonthlyLoanPaymentInAssetCurrency">Payment in the referenced financed asset's native currency.</param>
public sealed record ProjectionFinancedAssetSettings(Guid FinancedAssetId, decimal MonthlyLoanPaymentInAssetCurrency,
    decimal AnnualLoanInterestPercent, decimal AnnualAssetAppreciationPercent);
