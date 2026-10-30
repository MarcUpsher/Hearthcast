namespace Hearthcast.Data.Summaries.Models.IndividualHoldings;

public sealed record PersonHoldingsSummary(SnapshotPersonSummary Person, IReadOnlyList<PersonHoldingsCurrencySummary> Currencies);
