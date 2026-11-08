namespace Hearthcast.Data.Projections.Calculation;

/// <summary>Eligible portfolio and spending target in the projection's viewing currency.</summary>
public sealed record ProjectionExpenseTargetMonth(decimal NominalRequiredCapital, decimal InflationAdjustedRequiredCapital,
    decimal NominalEligibleBalance, decimal InflationAdjustedEligibleBalance, bool IsCovered);
