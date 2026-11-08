namespace Hearthcast.Data.Projections.Calculation;

/// <summary>Known native balances; no missing snapshot value is implicitly zero.</summary>
public sealed record ProjectionStartingPosition(DateOnly Date, IReadOnlyList<ProjectionCurrencyPosition> Currencies,
    IReadOnlyList<ProjectionFinancedAssetInput> FinancedAssets, decimal? ExpenseCurrencyConversionRate = null);
