namespace Hearthcast.Data.Summaries.Models.Household;

public sealed record HouseholdGroupSummary(Guid GroupId, string GroupName, SnapshotSummaryTotals Totals,
    IReadOnlyList<HouseholdItemContribution> Items, IReadOnlyList<HouseholdFinancedAssetContribution> FinancedAssets);
