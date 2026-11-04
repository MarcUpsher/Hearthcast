using Hearthcast.Data.Summaries.Models.Merged;

namespace Hearthcast.Data.Summaries.Models.History;

public sealed record HistoricalSummaryPoint(Guid SnapshotId, DateOnly SnapshotDate, Guid Revision,
    MergedAmount NetPosition, MergedAmount FinancedEquity, IReadOnlyList<HistoricalCategoryPoint> Categories,
    IReadOnlyList<HistoricalPersonPoint> People);
