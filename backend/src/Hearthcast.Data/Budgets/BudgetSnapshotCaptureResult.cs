namespace Hearthcast.Data.Budgets;

public sealed record BudgetSnapshotCaptureResult(BudgetSnapshot Snapshot, bool WasCreated);
