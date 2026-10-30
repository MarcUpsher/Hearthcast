namespace Hearthcast.Data.Summaries.Models.IndividualHoldings;

public sealed record PersonHoldingsGroupSummary(Guid GroupId, string GroupName, SnapshotSummaryTotals HoldingsTotals,
    IReadOnlyList<ItemSnapshotSummary> Items, IReadOnlyList<OwnedFinancedAssetSummary> FinancedAssets);
