namespace Hearthcast.Data.Budgets;

public sealed record BudgetSnapshotValues(DateOnly EffectiveFrom, IReadOnlyList<BudgetEntryValues> Entries,
    Guid? CopiedFromBudgetSnapshotId = null, string? Notes = null);
