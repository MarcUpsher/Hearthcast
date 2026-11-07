namespace Hearthcast.Data.Projections.Calculation;

/// <summary>Balances and redirected contributions in this pot's own currency.</summary>
public sealed record ProjectionCurrencyMonth(string CurrencyCode, decimal Investments, decimal Savings, decimal RedirectedToInvestments);
