using Hearthcast.Data.Spending;

namespace Hearthcast.Api.Spending.Contracts;

public sealed record SpendingCategorySummaryResponse(Guid HouseholdId, DateOnly From, DateOnly To,
    IReadOnlyList<SpendingCategoryTotalResponse> Categories,
    SpendingReconciliationCountsResponse ReconciliationRecordCounts,
    IReadOnlyList<SpendingReceiptWarningResponse> Warnings)
{
    internal static SpendingCategorySummaryResponse FromSummary(SpendingCategorySummary summary) => new(
        summary.HouseholdId, summary.From, summary.To,
        summary.Categories.Select(SpendingCategoryTotalResponse.From).ToArray(),
        new(summary.ReconciliationRecordCounts["NotChecked"], summary.ReconciliationRecordCounts["Reconciled"],
            summary.ReconciliationRecordCounts["Unreconciled"]),
        summary.Warnings.Select(SpendingReceiptWarningResponse.From).ToArray());
}
