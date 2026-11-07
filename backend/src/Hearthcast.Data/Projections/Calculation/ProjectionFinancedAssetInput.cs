namespace Hearthcast.Data.Projections.Calculation;

/// <param name="ConversionRate">Units of preset currency per one unit of asset currency; fixed throughout the projection.</param>
/// <param name="AssetValue">Starting value in asset currency.</param>
/// <param name="LoanBalance">Known starting debt in asset currency; zero means confirmed paid off.</param>
public sealed record ProjectionFinancedAssetInput(string CurrencyCode, decimal ConversionRate,
    decimal AssetValue, decimal LoanBalance, ProjectionFinancedAssetSettings Assumptions);
