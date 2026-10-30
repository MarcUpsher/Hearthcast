namespace Hearthcast.Data.Summaries.Models.Household;

/// <summary>Asset contains full saved balances; only AttributedEquity is counted in this scope.</summary>
public sealed record HouseholdFinancedAssetContribution(FinancedAssetSnapshotSummary Asset,
    int AllocationShareBasisPoints, decimal? AttributedEquity);
