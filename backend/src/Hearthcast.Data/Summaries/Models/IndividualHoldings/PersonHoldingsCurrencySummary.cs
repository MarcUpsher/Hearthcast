namespace Hearthcast.Data.Summaries.Models.IndividualHoldings;

public sealed record PersonHoldingsCurrencySummary(string CurrencyCode, SnapshotSummaryTotals HoldingsTotals,
    IReadOnlyList<PersonHoldingsCategorySummary> Categories);
