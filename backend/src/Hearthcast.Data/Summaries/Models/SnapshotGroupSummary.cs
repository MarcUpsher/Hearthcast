namespace Hearthcast.Data.Summaries.Models;

public sealed record SnapshotGroupSummary(Guid GroupId, string GroupName, SnapshotSummaryTotals AllRecordedTotals,
    IReadOnlyList<ItemSnapshotSummary> Items, IReadOnlyList<FinancedAssetSnapshotSummary> FinancedAssets);
