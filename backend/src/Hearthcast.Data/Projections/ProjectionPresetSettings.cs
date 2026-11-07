namespace Hearthcast.Data.Projections;

/// <param name="CurrencyCode">Viewing currency only; native currency pots retain their own units.</param>
public sealed record ProjectionPresetSettings(string Name, string CurrencyCode, int DurationYears,
    decimal AnnualInflationPercent, bool ShowInflationAdjustedValues, IReadOnlyList<ProjectionCurrencySettings> Currencies);
