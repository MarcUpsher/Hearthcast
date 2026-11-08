namespace Hearthcast.Data.Projections;

/// <summary>Annual spending in starting-date purchasing power, independent of the viewing currency.</summary>
public sealed record ProjectionExpenseTargetSettings(decimal AnnualExpenses, string CurrencyCode,
    decimal AnnualWithdrawalRatePercent, bool IncludeSavings);
