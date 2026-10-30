namespace Hearthcast.Data.Summaries.Models.IndividualHoldings;

/// <summary>Asset contains full recorded balances; only AttributedEquity contributes to this person's totals.</summary>
public sealed record OwnedFinancedAssetSummary(FinancedAssetSnapshotSummary Asset,
    int OwnershipShareBasisPoints, decimal? AttributedEquity);
