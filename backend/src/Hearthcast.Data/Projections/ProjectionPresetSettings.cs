namespace Hearthcast.Data.Projections;

public sealed record ProjectionPresetSettings(
    string Name, string CurrencyCode, int DurationYears,
    decimal MonthlyInvestmentContribution, decimal AnnualInvestmentReturnPercent,
    decimal MonthlySavingsContribution, decimal AnnualSavingsInterestPercent,
    decimal AnnualInflationPercent, bool ShowInflationAdjustedValues);
