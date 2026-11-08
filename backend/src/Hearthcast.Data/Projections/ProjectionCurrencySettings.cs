namespace Hearthcast.Data.Projections;

/// <summary>Contributions in starting-snapshot money and nominal expected returns, in this currency independent of display currency.</summary>
public sealed record ProjectionCurrencySettings(string CurrencyCode, decimal MonthlyInvestmentContribution,
    decimal AnnualInvestmentReturnPercent, decimal MonthlySavingsContribution, decimal AnnualSavingsInterestPercent);
