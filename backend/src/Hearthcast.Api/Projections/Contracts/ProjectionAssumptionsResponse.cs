using Hearthcast.Data.Projections;

namespace Hearthcast.Api.Projections.Contracts;

public sealed record ProjectionAssumptionsResponse(string CurrencyCode, int DurationYears,
    decimal AnnualInflationPercent, bool ShowInflationAdjustedValues, IReadOnlyList<ProjectionCurrencySettings> Currencies)
{
    internal static ProjectionAssumptionsResponse From(ProjectionPresetSettings s) =>
        new(s.CurrencyCode, s.DurationYears, s.AnnualInflationPercent, s.ShowInflationAdjustedValues, s.Currencies);
}
