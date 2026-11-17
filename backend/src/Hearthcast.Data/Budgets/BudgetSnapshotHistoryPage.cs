namespace Hearthcast.Data.Budgets;

public sealed record BudgetSnapshotHistoryPage(IReadOnlyList<BudgetSnapshot> Records, DateOnly? NextBefore, int PageSize);
