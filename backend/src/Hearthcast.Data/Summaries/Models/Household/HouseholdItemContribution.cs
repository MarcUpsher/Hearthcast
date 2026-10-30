namespace Hearthcast.Data.Summaries.Models.Household;

/// <summary>Item contains the full saved value; only ContributingAmount is counted in this scope.</summary>
public sealed record HouseholdItemContribution(ItemSnapshotSummary Item, int AllocationShareBasisPoints,
    decimal? ContributingAmount);
