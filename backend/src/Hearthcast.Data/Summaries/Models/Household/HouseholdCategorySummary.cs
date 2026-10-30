using Hearthcast.Data.Classification;

namespace Hearthcast.Data.Summaries.Models.Household;

public sealed record HouseholdCategorySummary(Guid CategoryId, string CategoryName, FinancialType FinancialType,
    SnapshotSummaryTotals Totals, IReadOnlyList<HouseholdGroupSummary> Groups);
