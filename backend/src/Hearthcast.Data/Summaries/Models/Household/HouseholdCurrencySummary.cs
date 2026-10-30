namespace Hearthcast.Data.Summaries.Models.Household;

public sealed record HouseholdCurrencySummary(string CurrencyCode, SnapshotSummaryTotals Totals,
    IReadOnlyList<HouseholdCategorySummary> Categories);
