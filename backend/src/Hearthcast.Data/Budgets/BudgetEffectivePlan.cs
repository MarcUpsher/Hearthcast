namespace Hearthcast.Data.Budgets;

public sealed record BudgetEffectivePlan(BudgetSnapshot Snapshot, DateOnly? EffectiveUntilExclusive);
