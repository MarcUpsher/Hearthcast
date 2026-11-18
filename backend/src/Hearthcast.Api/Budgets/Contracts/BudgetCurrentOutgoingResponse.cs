namespace Hearthcast.Api.Budgets.Contracts;

public sealed record BudgetCurrentOutgoingResponse(BudgetItemResponse Item, BudgetEntryResponse? Entry);
