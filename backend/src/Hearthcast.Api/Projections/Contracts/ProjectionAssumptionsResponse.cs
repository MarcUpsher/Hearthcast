using Hearthcast.Data.Projections;

namespace Hearthcast.Api.Projections.Contracts;

public sealed record ProjectionAssumptionsResponse(
    string CurrencyCode,
    int DurationYears,
    decimal MonthlyInvestmentContribution,
    decimal AnnualInvestmentReturnPercent,
    decimal MonthlySavingsContribution,
    decimal AnnualSavingsInterestPercent,
    decimal AnnualInflationPercent,
    bool ShowInflationAdjustedValues)
{
    internal static ProjectionAssumptionsResponse From(ProjectionPresetSettings s) =>
        new(s.CurrencyCode, s.DurationYears, s.MonthlyInvestmentContribution, s.AnnualInvestmentReturnPercent,
            s.MonthlySavingsContribution, s.AnnualSavingsInterestPercent, s.AnnualInflationPercent, s.ShowInflationAdjustedValues);
}
