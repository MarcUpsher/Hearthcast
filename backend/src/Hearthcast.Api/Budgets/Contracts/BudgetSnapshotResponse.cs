using Hearthcast.Data.Budgets;

namespace Hearthcast.Api.Budgets.Contracts;

public sealed record BudgetSnapshotResponse(Guid Id, Guid HouseholdId, DateOnly EffectiveFrom, string? Notes,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, Guid Revision, Guid? CopiedFromBudgetSnapshotId,
    bool IsComplete, IReadOnlyList<BudgetEntryResponse> Entries)
{
    internal static BudgetSnapshotResponse From(BudgetSnapshot plan) => new(plan.Id, plan.HouseholdId, plan.EffectiveFrom,
        plan.Notes, plan.CreatedAtUtc, plan.UpdatedAtUtc, plan.Revision, plan.CopiedFromBudgetSnapshotId, plan.IsComplete,
        plan.Entries.OrderBy(e => e.ItemCode).ThenBy(e => e.Id).Select(BudgetEntryResponse.From).ToArray());
}
