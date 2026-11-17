namespace Hearthcast.Data.Budgets;

public sealed class BudgetSnapshotConflictException(Exception? innerException = null)
    : InvalidOperationException("The budget plan has changed. Reload it before correcting it.", innerException);
