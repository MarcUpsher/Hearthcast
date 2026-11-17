namespace Hearthcast.Data.Budgets;

public sealed record BudgetCurrentOutgoings(DateOnly AsOf, Guid? SnapshotId, DateOnly? EffectiveFrom,
    DateOnly? EffectiveUntilExclusive, bool? IsComplete, IReadOnlyList<BudgetCurrentOutgoing> Items);
