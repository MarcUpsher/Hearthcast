namespace Hearthcast.Data.Projections.Calculation;

public sealed record ProjectionResult(string CurrencyCode, IReadOnlyList<ProjectionMonth> Months,
    IReadOnlyList<ProjectionWarning> Warnings);
