namespace Hearthcast.Api.Budgets.Contracts;

public sealed record BudgetHistoryPageResponse(IReadOnlyList<BudgetSnapshotResponse> Records, DateOnly? NextBefore, int PageSize);
