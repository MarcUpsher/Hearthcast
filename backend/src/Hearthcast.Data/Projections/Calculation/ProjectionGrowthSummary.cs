namespace Hearthcast.Data.Projections.Calculation;

/// <summary>The complete horizon explained in nominal and starting-date purchasing-power terms.</summary>
public sealed record ProjectionGrowthSummary(ProjectionGrowthBreakdown Nominal, ProjectionGrowthBreakdown InflationAdjusted);
