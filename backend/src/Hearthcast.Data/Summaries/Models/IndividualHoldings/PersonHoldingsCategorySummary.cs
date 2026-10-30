using Hearthcast.Data.Classification;

namespace Hearthcast.Data.Summaries.Models.IndividualHoldings;

public sealed record PersonHoldingsCategorySummary(Guid CategoryId, string CategoryName, FinancialType FinancialType,
    SnapshotSummaryTotals HoldingsTotals, IReadOnlyList<PersonHoldingsGroupSummary> Groups);
