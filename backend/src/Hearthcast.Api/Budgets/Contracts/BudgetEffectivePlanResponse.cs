namespace Hearthcast.Api.Budgets.Contracts;

public sealed record BudgetEffectivePlanResponse(DateOnly AsOf, BudgetSnapshotResponse Snapshot, DateOnly? EffectiveUntilExclusive);
