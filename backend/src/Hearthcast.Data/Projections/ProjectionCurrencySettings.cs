namespace Hearthcast.Data.Projections;

/// <summary>Contributions and expected returns expressed in this currency, independent of display currency.</summary>
public sealed record ProjectionCurrencySettings(string CurrencyCode, decimal MonthlyInvestmentContribution,
    decimal AnnualInvestmentReturnPercent, decimal MonthlySavingsContribution, decimal AnnualSavingsInterestPercent);
