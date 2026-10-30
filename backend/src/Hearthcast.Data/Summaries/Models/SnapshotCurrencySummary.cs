namespace Hearthcast.Data.Summaries.Models;

public sealed record SnapshotCurrencySummary(string CurrencyCode, SnapshotSummaryTotals AllRecordedTotals,
    IReadOnlyList<SnapshotCategorySummary> Categories);
