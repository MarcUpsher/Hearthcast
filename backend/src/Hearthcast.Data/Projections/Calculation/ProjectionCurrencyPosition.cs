namespace Hearthcast.Data.Projections.Calculation;

/// <param name="ConversionRate">Viewing currency per native unit, fixed for this projection.</param>
public sealed record ProjectionCurrencyPosition(string CurrencyCode, decimal Investments, decimal Savings, decimal? ConversionRate);
