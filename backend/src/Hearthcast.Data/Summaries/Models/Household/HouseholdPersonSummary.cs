namespace Hearthcast.Data.Summaries.Models.Household;

/// <summary>This person's allocated household contributions, independent of actual holdings.</summary>
public sealed record HouseholdPersonSummary(SnapshotPersonSummary Person, IReadOnlyList<HouseholdCurrencySummary> Currencies);
