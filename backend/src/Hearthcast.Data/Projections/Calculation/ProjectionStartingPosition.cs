namespace Hearthcast.Data.Projections.Calculation;

/// <summary>Known balances prepared by the caller; missing snapshot values must not become zero.</summary>
public sealed record ProjectionStartingPosition(DateOnly Date, decimal Investments, decimal Savings,
    IReadOnlyList<ProjectionFinancedAssetInput> FinancedAssets);
