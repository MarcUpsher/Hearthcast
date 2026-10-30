using Hearthcast.Data.Classification;

namespace Hearthcast.Data.Summaries.Models;

public sealed record SnapshotCategorySummary(Guid CategoryId, string CategoryName, FinancialType FinancialType,
    SnapshotSummaryTotals AllRecordedTotals, IReadOnlyList<SnapshotGroupSummary> Groups);
